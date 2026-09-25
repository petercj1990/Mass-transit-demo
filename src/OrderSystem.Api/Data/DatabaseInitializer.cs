using OrderSystem.Api.Inventory;

namespace OrderSystem.Api.Data;

/// <summary>
/// Creates the tables and seeds a small catalog on startup. A real app would use EF Core
/// migrations. EnsureCreated keeps generated migration files out of this demo.
/// To start over with an empty database, run: docker compose down, then docker compose up.
/// </summary>
public class DatabaseInitializer(IServiceScopeFactory scopeFactory, ILogger<DatabaseInitializer> logger)
    : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<OrderSystemDbContext>();

        if (!await dbContext.Database.EnsureCreatedAsync(cancellationToken))
            return;

        dbContext.Products.AddRange(
            NewProduct("MUG", "MassTransit coffee mug", 12.50m, stock: 100),
            NewProduct("PLUSH", "RabbitMQ plush rabbit", 19.99m, stock: 3),
            NewProduct("BOOK", "Enterprise Integration Patterns (hardcover)", 54.00m, stock: 0));

        await dbContext.SaveChangesAsync(cancellationToken);

        logger.LogInformation("Created the database and seeded the product catalog");
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    static Product NewProduct(string sku, string name, decimal unitPrice, int stock)
    {
        var product = new Product { Sku = sku, Name = name, UnitPrice = unitPrice };
        product.AddStock(stock);
        return product;
    }
}
