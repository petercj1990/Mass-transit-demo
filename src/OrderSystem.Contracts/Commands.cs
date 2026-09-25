namespace OrderSystem.Contracts;

// COMMANDS ask one specific service to do something. They are named in the
// imperative ("ChargePayment") and SENT to that service's queue (see QueueNames),
// so exactly one consumer handles each command. Compare with Events.cs.

/// <summary>
/// Place a new order. The API sends this as a request and waits for an
/// <see cref="OrderAccepted"/> or <see cref="OrderRejected"/> response.
/// </summary>
/// <param name="PaymentMethod">
/// A reference to a card stored at the payment provider (like Stripe's "pm_..." ids),
/// never raw card data. The fake gateway uses special values to simulate failures.
/// </param>
public record SubmitOrder(Guid OrderId, string CustomerId, string PaymentMethod, IReadOnlyList<OrderLine> Lines);

/// <summary>Reserve stock for every line of an order. Sent by the order saga.</summary>
public record ReserveInventory(Guid OrderId, IReadOnlyList<OrderLine> Lines);

/// <summary>
/// Put reserved stock back on the shelf. Sent by the order saga as a
/// compensating action: it undoes <see cref="ReserveInventory"/> when a later step fails.
/// </summary>
public record ReleaseInventory(Guid OrderId, string Reason);

/// <summary>Charge the customer for an order. Sent by the order saga.</summary>
public record ChargePayment(Guid OrderId, string CustomerId, decimal Amount, string PaymentMethod);

public record OrderLine(string Sku, int Quantity);
