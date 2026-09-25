using MassTransit;
using Microsoft.EntityFrameworkCore;
using OrderSystem.Api.Data;
using OrderSystem.Contracts;

namespace OrderSystem.Api.Inventory;

/// <summary>
/// Reserves stock for an order and publishes the outcome. The stock change and the
/// outgoing event are committed together (the outbox), so it can never happen that
/// stock was taken but InventoryReserved was lost, or the other way around.
/// </summary>
public class ReserveInventoryConsumer(OrderSystemDbContext dbContext, ILogger<ReserveInventoryConsumer> logger)
    : IConsumer<ReserveInventory>
{
    public async Task Consume(ConsumeContext<ReserveInventory> context)
    {
        var orderId = context.Message.OrderId;
        var requested = context.Message.Lines
            .GroupBy(line => line.Sku)
            .ToDictionary(group => group.Key, group => group.Sum(line => line.Quantity));

        var skus = requested.Keys.ToList();
        var products = await dbContext.Products
            .Where(product => skus.Contains(product.Sku))
            .ToDictionaryAsync(product => product.Sku, context.CancellationToken);

        var shortages = requested
            .Where(line => !products.TryGetValue(line.Key, out var product) || product.QuantityOnHand < line.Value)
            .Select(line => $"{line.Key} (wanted {line.Value}, have {products.GetValueOrDefault(line.Key)?.QuantityOnHand ?? 0})")
            .ToList();

        if (shortages.Count > 0)
        {
            logger.LogInformation("Order {OrderId}: not enough stock for {Shortages}", orderId, shortages);

            await context.Publish(new InventoryUnavailable(orderId, $"Out of stock: {string.Join(", ", shortages)}"));
            return;
        }

        foreach (var (sku, quantity) in requested)
        {
            products[sku].RemoveStock(quantity);
            dbContext.StockReservations.Add(new StockReservation { OrderId = orderId, Sku = sku, Quantity = quantity });
        }

        // If another order took the same stock after we read it, this throws instead of
        // overselling. Postgres rejects the update with "could not serialize access" because
        // the outbox transaction is REPEATABLE READ. The retry policy then runs this consumer
        // again with fresh data, and it sees the real stock level.
        await dbContext.SaveChangesAsync(context.CancellationToken);

        await context.Publish(new InventoryReserved(orderId));

        logger.LogInformation("Order {OrderId}: reserved {Lines}", orderId,
            string.Join(", ", requested.Select(line => $"{line.Value} x {line.Key}")));
    }
}

public class ReserveInventoryConsumerDefinition : ConsumerDefinition<ReserveInventoryConsumer>
{
    public ReserveInventoryConsumerDefinition()
    {
        EndpointName = QueueNames.ReserveInventory;
    }

    protected override void ConfigureConsumer(IReceiveEndpointConfigurator endpointConfigurator,
        IConsumerConfigurator<ReserveInventoryConsumer> consumerConfigurator, IRegistrationContext context)
    {
        // Short, quick retries suit concurrency conflicts: the competing transaction has
        // usually committed by the next attempt. (Try 8 simultaneous orders for PLUSH.)
        endpointConfigurator.UseMessageRetry(r => r.Intervals(50, 100, 250, 500, 1000));
        endpointConfigurator.UseEntityFrameworkOutbox<OrderSystemDbContext>(context);
    }
}
