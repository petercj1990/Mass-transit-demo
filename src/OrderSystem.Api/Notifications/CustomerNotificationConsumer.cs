using MassTransit;
using OrderSystem.Api.Data;
using OrderSystem.Contracts;

namespace OrderSystem.Api.Notifications;

/// <summary>
/// Publish/subscribe in action. The saga publishes OrderCompleted and OrderFailed
/// without knowing this consumer exists. Adding a subscriber is a new consumer, with
/// no change to the publisher. One consumer class can also handle several message types.
/// </summary>
public class CustomerNotificationConsumer(ILogger<CustomerNotificationConsumer> logger) :
    IConsumer<OrderCompleted>,
    IConsumer<OrderFailed>,
    IConsumer<InventoryRestocked>
{
    public Task Consume(ConsumeContext<OrderCompleted> context)
    {
        logger.LogInformation("EMAIL to {CustomerId}: your order {OrderId} is confirmed, total {Total}",
            context.Message.CustomerId, context.Message.OrderId, context.Message.Total);
        return Task.CompletedTask;
    }

    public Task Consume(ConsumeContext<OrderFailed> context)
    {
        logger.LogInformation("EMAIL to {CustomerId}: sorry, order {OrderId} failed: {Reason}",
            context.Message.CustomerId, context.Message.OrderId, context.Message.Reason);
        return Task.CompletedTask;
    }

    public Task Consume(ConsumeContext<InventoryRestocked> context)
    {
        logger.LogInformation("EMAIL to subscribers: {Sku} was restocked, {QuantityOnHand} now available",
            context.Message.Sku, context.Message.QuantityOnHand);
        return Task.CompletedTask;
    }
}

public class CustomerNotificationConsumerDefinition : ConsumerDefinition<CustomerNotificationConsumer>
{
    protected override void ConfigureConsumer(IReceiveEndpointConfigurator endpointConfigurator,
        IConsumerConfigurator<CustomerNotificationConsumer> consumerConfigurator, IRegistrationContext context)
    {
        endpointConfigurator.UseMessageRetry(r => r.Intervals(100, 500, 1000));

        // No database writes here, but the outbox's inbox still earns its keep: if RabbitMQ
        // redelivers a message this consumer already handled, it's skipped, so no second email.
        endpointConfigurator.UseEntityFrameworkOutbox<OrderSystemDbContext>(context);
    }
}
