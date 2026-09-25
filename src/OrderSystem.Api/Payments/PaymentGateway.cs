using System.Collections.Concurrent;

namespace OrderSystem.Api.Payments;

public interface IPaymentGateway
{
    /// <summary>
    /// Charges a payment method. Throws <see cref="PaymentGatewayUnavailableException"/>
    /// when the gateway can't be reached. That failure is temporary and worth retrying.
    /// </summary>
    /// <param name="idempotencyKey">
    /// Charging the same key twice returns the first result instead of charging again.
    /// Real gateways (Stripe, Adyen, ...) support this, and you need it: with retries
    /// and at-least-once delivery, the same charge WILL be attempted more than once.
    /// </param>
    Task<PaymentResult> Charge(Guid idempotencyKey, decimal amount, string paymentMethod,
        CancellationToken cancellationToken);
}

public record PaymentResult(bool Approved, string? TransactionId, string? DeclineReason);

public class PaymentGatewayUnavailableException(string message) : Exception(message);

/// <summary>
/// Stands in for a third-party payment API. Pick a scenario with the payment method:
/// </summary>
public class FakePaymentGateway : IPaymentGateway
{
    /// <summary>Always approved.</summary>
    public const string CardOk = "card-ok";

    /// <summary>The gateway fails twice, then approves: the retry policy saves the day.</summary>
    public const string CardFlaky = "card-flaky";

    /// <summary>The bank says no. A business outcome, not an error, so it's never retried.</summary>
    public const string CardDeclined = "card-declined";

    /// <summary>The gateway is down for good: retries run out and the message faults.</summary>
    public const string GatewayDown = "gateway-down";

    const int FlakyFailures = 2;

    readonly ConcurrentDictionary<Guid, int> _attempts = new();
    readonly ConcurrentDictionary<Guid, PaymentResult> _results = new();

    public async Task<PaymentResult> Charge(Guid idempotencyKey, decimal amount, string paymentMethod,
        CancellationToken cancellationToken)
    {
        await Task.Delay(TimeSpan.FromMilliseconds(100), cancellationToken); // "network latency"

        if (_results.TryGetValue(idempotencyKey, out var previous))
            return previous;

        var attempt = _attempts.AddOrUpdate(idempotencyKey, 1, (_, count) => count + 1);

        if (paymentMethod == GatewayDown)
            throw new PaymentGatewayUnavailableException("Payment gateway timed out");

        if (paymentMethod == CardFlaky && attempt <= FlakyFailures)
            throw new PaymentGatewayUnavailableException($"Payment gateway returned 503 (call #{attempt})");

        var result = paymentMethod == CardDeclined
            ? new PaymentResult(false, null, "Insufficient funds")
            : new PaymentResult(true, $"txn_{Guid.NewGuid():N}"[..16], null);

        return _results.GetOrAdd(idempotencyKey, result);
    }
}
