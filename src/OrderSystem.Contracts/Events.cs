namespace OrderSystem.Contracts;

// EVENTS announce that something already happened. They are named in the past
// tense and PUBLISHED: the publisher doesn't know who is listening, and the broker
// delivers a copy to every queue that subscribes to the event type (0..N consumers).

public record OrderSubmitted(
    Guid OrderId,
    string CustomerId,
    string PaymentMethod,
    IReadOnlyList<OrderLine> Lines,
    decimal Total,
    DateTime SubmittedAt);

public record InventoryReserved(Guid OrderId);

public record InventoryUnavailable(Guid OrderId, string Reason);

public record InventoryRestocked(string Sku, int QuantityAdded, int QuantityOnHand);

public record PaymentCompleted(Guid OrderId, string TransactionId, decimal Amount);

public record PaymentDeclined(Guid OrderId, string Reason);

public record OrderCompleted(Guid OrderId, string CustomerId, decimal Total);

public record OrderFailed(Guid OrderId, string CustomerId, string Reason);
