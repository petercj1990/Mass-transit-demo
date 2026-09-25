namespace OrderSystem.Api.Inventory;

public class Product
{
    public required string Sku { get; set; }
    public required string Name { get; set; }
    public decimal UnitPrice { get; set; }
    public int QuantityOnHand { get; private set; }

    /// <summary>
    /// Optimistic concurrency token. EF Core adds "WHERE Version = @original" to every
    /// UPDATE, so writing a stale copy fails with DbUpdateConcurrencyException instead of
    /// silently overwriting someone else's change (a "lost update"). Consumers are
    /// also protected by the outbox's REPEATABLE READ transaction. This token also covers
    /// code that runs outside one, like the restock endpoint.
    /// </summary>
    public int Version { get; private set; }

    public void AddStock(int quantity)
    {
        QuantityOnHand += quantity;
        Version++;
    }

    public void RemoveStock(int quantity)
    {
        QuantityOnHand -= quantity;
        Version++;
    }
}

/// <summary>Stock held for an order, so it can be released if the order fails.</summary>
public class StockReservation
{
    public Guid OrderId { get; set; }
    public required string Sku { get; set; }
    public int Quantity { get; set; }
}
