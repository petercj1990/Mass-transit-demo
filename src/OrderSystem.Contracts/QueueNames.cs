namespace OrderSystem.Contracts;

/// <summary>
/// The queues that commands and requests are sent to. A queue name is part of a
/// service's public contract, the way a URL is for an HTTP API. Events need no
/// address: they're published, and the broker routes them by message type.
/// </summary>
public static class QueueNames
{
    public const string SubmitOrder = "submit-order";
    public const string OrderSaga = "order-saga";
    public const string ReserveInventory = "reserve-inventory";
    public const string ReleaseInventory = "release-inventory";
    public const string ChargePayment = "charge-payment";

    /// <summary>A "queue:name" short address, which MassTransit resolves on any transport.</summary>
    public static Uri Address(string queueName) => new($"queue:{queueName}");
}
