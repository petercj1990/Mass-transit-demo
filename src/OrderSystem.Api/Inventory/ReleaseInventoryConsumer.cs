using MassTransit;
using Microsoft.EntityFrameworkCore;
using OrderSystem.Api.Data;
using OrderSystem.Contracts;

namespace OrderSystem.Api.Inventory;

/// <summary>
/// The compensating action for <see cref="ReserveInventoryConsumer"/>: puts an order's
/// reserved stock back. Running it twice is harmless, because the second run finds
/// no reservations. Compensations must be idempotent, since messages can be redelivered.
/// </summary>
public class ReleaseInventoryConsumer(OrderSystemDbContext dbContext, ILogger<ReleaseInventoryConsumer> logger)
    : IConsumer<ReleaseInventory>
{
    public async Task Consume(ConsumeContext<ReleaseInventory> context)
    {
        var orderId = context.Message.OrderId;

        var reservations = await dbContext.StockReservations
            .Where(reservation => reservation.OrderId == orderId)
            .ToListAsync(context.CancellationToken);

        if (reservations.Count == 0)
        {
            logger.LogInformation("Order {OrderId}: no reserved stock to release", orderId);
            return;
        }

        var skus = reservations.Select(reservation => reservation.Sku).ToList();
        var products = await dbContext.Products
            .Where(product => skus.Contains(product.Sku))
            .ToDictionaryAsync(product => product.Sku, context.CancellationToken);

        foreach (var reservation in reservations)
            products[reservation.Sku].AddStock(reservation.Quantity);

        dbContext.StockReservations.RemoveRange(reservations);
        await dbContext.SaveChangesAsync(context.CancellationToken);

        logger.LogInformation("Order {OrderId}: released reserved stock ({Reason})", orderId, context.Message.Reason);
    }
}

public class ReleaseInventoryConsumerDefinition : ConsumerDefinition<ReleaseInventoryConsumer>
{
    public ReleaseInventoryConsumerDefinition()
    {
        EndpointName = QueueNames.ReleaseInventory;
    }

    protected override void ConfigureConsumer(IReceiveEndpointConfigurator endpointConfigurator,
        IConsumerConfigurator<ReleaseInventoryConsumer> consumerConfigurator, IRegistrationContext context)
    {
        endpointConfigurator.UseMessageRetry(r => r.Intervals(50, 100, 250, 500, 1000));
        endpointConfigurator.UseEntityFrameworkOutbox<OrderSystemDbContext>(context);
    }
}
