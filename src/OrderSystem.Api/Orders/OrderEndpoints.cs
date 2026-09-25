using MassTransit;
using OrderSystem.Contracts;

namespace OrderSystem.Api.Orders;

public static class OrderEndpoints
{
    public record SubmitOrderRequest(string CustomerId, string PaymentMethod, List<OrderLine> Lines);

    public static void MapOrderEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/orders", SubmitOrder);
        app.MapGet("/orders/{orderId:guid}", GetOrder);
    }

    /// <summary>
    /// Request/response over the message bus: send SubmitOrder to the "submit-order"
    /// queue, then wait (without blocking a thread) for the consumer's reply.
    /// </summary>
    static async Task<IResult> SubmitOrder(SubmitOrderRequest request, IRequestClient<SubmitOrder> client,
        CancellationToken cancellationToken)
    {
        // Check the shape of the HTTP request here. Business rules (known SKUs, positive
        // quantities) belong to the consumer, which answers with OrderRejected.
        if (string.IsNullOrWhiteSpace(request.CustomerId) || string.IsNullOrWhiteSpace(request.PaymentMethod) ||
            request.Lines is null || request.Lines.Any(line => line is null || string.IsNullOrWhiteSpace(line.Sku)))
        {
            return Results.BadRequest("customerId, paymentMethod and lines (each with a sku) are required.");
        }

        // NewId generates sequential GUIDs, which are much kinder to database indexes than Guid.NewGuid().
        var orderId = NewId.NextGuid();

        try
        {
            // The request can be answered with either of two response types.
            var response = await client.GetResponse<OrderAccepted, OrderRejected>(
                new SubmitOrder(orderId, request.CustomerId, request.PaymentMethod, request.Lines),
                cancellationToken);

            return response.Message switch
            {
                // 202: the order is accepted, but reserving stock and charging the card
                // happen in the background. The Location header says where to poll for progress.
                OrderAccepted accepted => Results.Accepted($"/orders/{orderId}", accepted),
                OrderRejected rejected => Results.BadRequest(rejected),
                _ => throw new InvalidOperationException($"Unexpected response: {response.Message}")
            };
        }
        catch (RequestTimeoutException)
        {
            // Nobody answered in time. Is the consumer running?
            return Results.Problem("The order service did not respond in time.", statusCode: StatusCodes.Status504GatewayTimeout);
        }
    }

    /// <summary>Request/response with a saga: the order saga answers with its own current state.</summary>
    static async Task<IResult> GetOrder(Guid orderId, IRequestClient<GetOrderStatus> client,
        CancellationToken cancellationToken)
    {
        var response = await client.GetResponse<OrderStatus, OrderNotFound>(new GetOrderStatus(orderId),
            cancellationToken);

        return response.Is(out Response<OrderStatus>? status)
            ? Results.Ok(status.Message)
            : Results.NotFound();
    }
}
