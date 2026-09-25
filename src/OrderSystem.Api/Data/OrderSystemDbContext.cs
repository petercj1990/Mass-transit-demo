using MassTransit;
using MassTransit.EntityFrameworkCoreIntegration;
using Microsoft.EntityFrameworkCore;
using OrderSystem.Api.Inventory;
using OrderSystem.Api.Orders;

namespace OrderSystem.Api.Data;

/// <summary>
/// One database for the whole demo. In production each service (orders, inventory,
/// payments) would own its own database, and each would have its own copy of the
/// outbox tables.
/// </summary>
public class OrderSystemDbContext(DbContextOptions<OrderSystemDbContext> options) : SagaDbContext(options)
{
    public DbSet<OrderState> Orders => Set<OrderState>();
    public DbSet<Product> Products => Set<Product>();
    public DbSet<StockReservation> StockReservations => Set<StockReservation>();

    protected override IEnumerable<ISagaClassMap> Configurations
    {
        get { yield return new OrderStateMap(); }
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // The transactional outbox tables:
        //   InboxState    - messages each consumer has already processed (to skip duplicates)
        //   OutboxMessage - messages waiting to be delivered to the broker
        //   OutboxState   - delivery bookkeeping for the bus outbox
        modelBuilder.AddInboxStateEntity();
        modelBuilder.AddOutboxMessageEntity();
        modelBuilder.AddOutboxStateEntity();

        modelBuilder.Entity<Product>(entity =>
        {
            entity.HasKey(x => x.Sku);
            entity.Property(x => x.Sku).HasMaxLength(32);
            entity.Property(x => x.Name).HasMaxLength(200);
            entity.Property(x => x.UnitPrice).HasPrecision(18, 2);
            entity.Property(x => x.Version).IsConcurrencyToken();
        });

        modelBuilder.Entity<StockReservation>(entity =>
        {
            entity.HasKey(x => new { x.OrderId, x.Sku });
            entity.Property(x => x.Sku).HasMaxLength(32);
        });
    }
}
