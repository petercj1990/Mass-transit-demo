using MassTransit;
using Microsoft.EntityFrameworkCore;
using OrderSystem.Api.Data;
using OrderSystem.Contracts;

namespace OrderSystem.Api.Inventory;

public static class InventoryEndpoints
{
    public record RestockRequest(int Quantity);

    public static void MapInventoryEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/inventory", GetInventory);
        app.MapPost("/inventory/{sku}/restock", Restock);
    }

    static async Task<IResult> GetInventory(OrderSystemDbContext dbContext, CancellationToken cancellationToken)
    {
        var products = await dbContext.Products
            .AsNoTracking()
            .OrderBy(product => product.Sku)
            .Select(product => new { product.Sku, product.Name, product.UnitPrice, product.QuantityOnHand })
            .ToListAsync(cancellationToken);

        return Results.Ok(products);
    }

    /// <summary>
    /// The bus outbox: publish an event from a web request, in the same transaction as
    /// the database change.
    ///
    /// Without an outbox you pick your poison. Publish first and the save may fail, so
    /// subscribers heard about stock that doesn't exist. Save first and the publish may
    /// fail, so the stock exists but nobody hears about it.
    /// </summary>
    static async Task<IResult> Restock(string sku, RestockRequest request, OrderSystemDbContext dbContext,
        IPublishEndpoint publishEndpoint, CancellationToken cancellationToken)
    {
        if (request.Quantity <= 0)
            return Results.BadRequest("Quantity must be positive.");

        var product = await dbContext.Products.FindAsync([sku], cancellationToken);
        if (product is null)
            return Results.NotFound();

        product.AddStock(request.Quantity);

        // With UseBusOutbox() this doesn't touch RabbitMQ. It adds a row to the
        // OutboxMessage table, tracked by this request's DbContext...
        await publishEndpoint.Publish(new InventoryRestocked(sku, request.Quantity, product.QuantityOnHand),
            cancellationToken);

        try
        {
            // ...and this commits the new stock level AND that row in one transaction. A
            // background service then delivers the message to RabbitMQ, retrying until it gets through.
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            // An order reserved this product between our read and our write (see Product.Version).
            // Nothing was saved, and nothing will be published.
            return Results.Conflict("The stock level changed while restocking. Please try again.");
        }

        return Results.Ok(new { product.Sku, product.QuantityOnHand });
    }
}
