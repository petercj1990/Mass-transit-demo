namespace OrderSystem.Contracts;

// REQUEST/RESPONSE messages. The API sends a request with IRequestClient<T> and
// awaits the reply. A request can have more than one response type, and the
// caller checks which one came back (see OrderEndpoints).

// SubmitOrder (in Commands.cs) is answered with OrderAccepted or OrderRejected.

public record OrderAccepted(Guid OrderId, decimal Total);

public record OrderRejected(Guid OrderId, string Reason);

// GetOrderStatus is answered by the order saga itself, with OrderStatus or OrderNotFound.

public record GetOrderStatus(Guid OrderId);

public record OrderStatus(
    Guid OrderId,
    string State,
    string CustomerId,
    decimal Total,
    string? TransactionId,
    string? FailureReason,
    DateTime SubmittedAt,
    DateTime UpdatedAt);

public record OrderNotFound(Guid OrderId);
