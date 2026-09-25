using MassTransit;
using OrderSystem.Api.Data;
using OrderSystem.Contracts;

namespace OrderSystem.Api.Orders;

// The order saga: a long-running process that reacts to events and sends commands.
//
//   OrderSubmitted
//        |
//        v
//   ReservingInventory --InventoryReserved--> ProcessingPayment --PaymentCompleted--> Completed
//        |                                           |
//   InventoryUnavailable                  PaymentDeclined or Fault<ChargePayment>
//        |                                (compensate: send ReleaseInventory)
//        v                                           |
//      Failed <--------------------------------------+
//
// The saga never calls another service directly. It sends a command, saves its
// state, and waits for the next event, so a crash or restart between steps loses nothing.
public class OrderStateMachine : MassTransitStateMachine<OrderState>
{
    public OrderStateMachine(ILogger<OrderStateMachine> logger)
    {
        // Which property stores the current state.
        InstanceState(x => x.CurrentState);

        // Correlation: how each incoming message finds its saga instance.
        Event(() => OrderSubmitted, x => x.CorrelateById(context => context.Message.OrderId));
        Event(() => InventoryReserved, x => x.CorrelateById(context => context.Message.OrderId));
        Event(() => InventoryUnavailable, x => x.CorrelateById(context => context.Message.OrderId));
        Event(() => PaymentCompleted, x => x.CorrelateById(context => context.Message.OrderId));
        Event(() => PaymentDeclined, x => x.CorrelateById(context => context.Message.OrderId));

        // When a consumer gives up on a message (its retries are exhausted), MassTransit
        // publishes Fault<T> containing the original message. The saga can react to it
        // like any other event.
        Event(() => PaymentFaulted, x => x.CorrelateById(context => context.Message.Message.OrderId));

        Event(() => StatusRequested, x =>
        {
            x.CorrelateById(context => context.Message.OrderId);
            x.ReadOnly = true; // a query: don't save the saga afterwards

            // By default a message for an unknown saga instance is an error. For a status
            // query, reply "not found" instead.
            x.OnMissingInstance(m => m.ExecuteAsync(context =>
                context.RespondAsync(new OrderNotFound(context.Message.OrderId))));
        });

        Initially(
            When(OrderSubmitted)
                .Then(context =>
                {
                    context.Saga.CustomerId = context.Message.CustomerId;
                    context.Saga.PaymentMethod = context.Message.PaymentMethod;
                    context.Saga.Total = context.Message.Total;
                    context.Saga.SubmittedAt = context.Message.SubmittedAt;
                    context.Saga.UpdatedAt = DateTime.UtcNow;
                })
                .Send(QueueNames.Address(QueueNames.ReserveInventory),
                    context => new ReserveInventory(context.Saga.CorrelationId, context.Message.Lines))
                .TransitionTo(ReservingInventory));

        During(ReservingInventory,
            When(InventoryReserved)
                .Then(context => context.Saga.UpdatedAt = DateTime.UtcNow)
                .Send(QueueNames.Address(QueueNames.ChargePayment),
                    context => new ChargePayment(context.Saga.CorrelationId, context.Saga.CustomerId,
                        context.Saga.Total, context.Saga.PaymentMethod))
                .TransitionTo(ProcessingPayment),
            When(InventoryUnavailable)
                .Then(context => MarkFailed(context.Saga, context.Message.Reason))
                .Publish(context => new OrderFailed(context.Saga.CorrelationId, context.Saga.CustomerId,
                    context.Saga.FailureReason!))
                .TransitionTo(Failed));

        During(ProcessingPayment,
            When(PaymentCompleted)
                .Then(context =>
                {
                    context.Saga.TransactionId = context.Message.TransactionId;
                    context.Saga.UpdatedAt = DateTime.UtcNow;
                })
                .Publish(context => new OrderCompleted(context.Saga.CorrelationId, context.Saga.CustomerId,
                    context.Saga.Total))
                .TransitionTo(Completed),

            // Business failure: the card was declined. Stock is already reserved, so
            // compensate by sending a command that undoes the reservation.
            When(PaymentDeclined)
                .Then(context => MarkFailed(context.Saga, $"Payment declined: {context.Message.Reason}"))
                .Send(QueueNames.Address(QueueNames.ReleaseInventory),
                    context => new ReleaseInventory(context.Saga.CorrelationId, context.Saga.FailureReason!))
                .Publish(context => new OrderFailed(context.Saga.CorrelationId, context.Saga.CustomerId,
                    context.Saga.FailureReason!))
                .TransitionTo(Failed),

            // Technical failure: the payment consumer kept throwing until its retries
            // ran out. Same compensation as above.
            When(PaymentFaulted)
                .Then(context => MarkFailed(context.Saga,
                    $"Payment failed after retries: {context.Message.Exceptions.FirstOrDefault()?.Message}"))
                .Send(QueueNames.Address(QueueNames.ReleaseInventory),
                    context => new ReleaseInventory(context.Saga.CorrelationId, context.Saga.FailureReason!))
                .Publish(context => new OrderFailed(context.Saga.CorrelationId, context.Saga.CustomerId,
                    context.Saga.FailureReason!))
                .TransitionTo(Failed));

        // The saga answers status queries in every state (request/response with a saga).
        DuringAny(
            When(StatusRequested)
                .Respond(context => new OrderStatus(
                    context.Saga.CorrelationId,
                    context.Saga.CurrentState,
                    context.Saga.CustomerId,
                    context.Saga.Total,
                    context.Saga.TransactionId,
                    context.Saga.FailureReason,
                    context.Saga.SubmittedAt,
                    context.Saga.UpdatedAt)));

        WhenEnterAny(binder => binder.Then(context =>
            logger.LogInformation("Order {OrderId} is now {State}",
                context.Saga.CorrelationId, context.Saga.CurrentState)));
    }

    public State ReservingInventory { get; private set; } = null!;
    public State ProcessingPayment { get; private set; } = null!;
    public State Completed { get; private set; } = null!;
    public State Failed { get; private set; } = null!;

    public Event<OrderSubmitted> OrderSubmitted { get; private set; } = null!;
    public Event<InventoryReserved> InventoryReserved { get; private set; } = null!;
    public Event<InventoryUnavailable> InventoryUnavailable { get; private set; } = null!;
    public Event<PaymentCompleted> PaymentCompleted { get; private set; } = null!;
    public Event<PaymentDeclined> PaymentDeclined { get; private set; } = null!;
    public Event<Fault<ChargePayment>> PaymentFaulted { get; private set; } = null!;
    public Event<GetOrderStatus> StatusRequested { get; private set; } = null!;

    static void MarkFailed(OrderState order, string reason)
    {
        order.FailureReason = reason;
        order.UpdatedAt = DateTime.UtcNow;
    }
}

public class OrderStateDefinition : SagaDefinition<OrderState>
{
    public OrderStateDefinition()
    {
        EndpointName = QueueNames.OrderSaga;
    }

    protected override void ConfigureSaga(IReceiveEndpointConfigurator endpointConfigurator,
        ISagaConfigurator<OrderState> sagaConfigurator, IRegistrationContext context)
    {
        // Retry first, then the outbox, so each retry attempt runs in a fresh transaction.
        // Retries here mostly cover database hiccups and two messages for the same
        // order arriving at once.
        endpointConfigurator.UseMessageRetry(r => r.Intervals(100, 500, 1000));

        // Saga state and outgoing messages are saved in the same database transaction.
        endpointConfigurator.UseEntityFrameworkOutbox<OrderSystemDbContext>(context);
    }
}
