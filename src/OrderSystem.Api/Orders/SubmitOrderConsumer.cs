using MassTransit;
using Microsoft.EntityFrameworkCore;
using OrderSystem.Api.Data;
using OrderSystem.Contracts;

namespace OrderSystem.Api.Orders;

/// <summary>
/// Handles the API's SubmitOrder request: validates the order, prices it, replies to
/// the caller, and publishes OrderSubmitted, which starts the order saga.
/// </summary>
public class SubmitOrderConsumer(OrderSystemDbContext dbContext, ILogger<SubmitOrderConsumer> logger)
    : IConsumer<SubmitOrder>
{
    public async Task Consume(ConsumeContext<SubmitOrder> context)
    {
        var order = context.Message;

        if (order.Lines.Count == 0 || order.Lines.Any(line => line.Quantity <= 0))
        {
            await Reject(context, "An order needs at least one line, and every quantity must be positive.");
            return;
        }

        var skus = order.Lines.Select(line => line.Sku).Distinct().ToList();
        var prices = await dbContext.Products
            .Where(product => skus.Contains(product.Sku))
            .ToDictionaryAsync(product => product.Sku, product => product.UnitPrice, context.CancellationToken);

        var unknownSkus = skus.Where(sku => !prices.ContainsKey(sku)).ToList();
        if (unknownSkus.Count > 0)
        {
            await Reject(context, $"Unknown SKU(s): {string.Join(", ", unknownSkus)}");
            return;
        }

        var total = order.Lines.Sum(line => prices[line.Sku] * line.Quantity);

        // Neither message goes to RabbitMQ yet. The outbox stores both and delivers them
        // only after this consumer finishes and the transaction commits. If the consumer
        // throws or the commit fails, neither message is ever sent.
        await context.Publish(new OrderSubmitted(order.OrderId, order.CustomerId, order.PaymentMethod,
            order.Lines, total, DateTime.UtcNow));

        await context.RespondAsync(new OrderAccepted(order.OrderId, total));

        logger.LogInformation("Order {OrderId} accepted for {CustomerId}, total {Total}",
            order.OrderId, order.CustomerId, total);
    }

    Task Reject(ConsumeContext<SubmitOrder> context, string reason)
    {
        logger.LogInformation("Order {OrderId} rejected: {Reason}", context.Message.OrderId, reason);

        return context.RespondAsync(new OrderRejected(context.Message.OrderId, reason));
    }
}

/// <summary>
/// A definition configures the consumer's receive endpoint: queue name, retry, outbox,
/// concurrency, and so on. Each consumer's middleware pipeline is kept next to the consumer.
/// </summary>
public class SubmitOrderConsumerDefinition : ConsumerDefinition<SubmitOrderConsumer>
{
    public SubmitOrderConsumerDefinition()
    {
        EndpointName = QueueNames.SubmitOrder;
    }

    protected override void ConfigureConsumer(IReceiveEndpointConfigurator endpointConfigurator,
        IConsumerConfigurator<SubmitOrderConsumer> consumerConfigurator, IRegistrationContext context)
    {
        // Middleware runs in the order it's added: retry -> outbox -> consumer.
        // Retry sits OUTSIDE the outbox, so each attempt starts a fresh transaction and
        // a fresh DbContext. The reverse order would retry inside a failed transaction.
        endpointConfigurator.UseMessageRetry(r => r.Intervals(100, 500, 1000));
        endpointConfigurator.UseEntityFrameworkOutbox<OrderSystemDbContext>(context);
    }
}
