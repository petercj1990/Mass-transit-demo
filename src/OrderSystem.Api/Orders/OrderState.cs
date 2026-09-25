using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace OrderSystem.Api.Orders;

/// <summary>
/// The saga instance: one row per order, holding everything the state machine needs
/// to remember between messages. CorrelationId (the order id) ties every message
/// about this order back to this row.
/// </summary>
public class OrderState : SagaStateMachineInstance
{
    public Guid CorrelationId { get; set; }

    /// <summary>The current state's name, e.g. "ProcessingPayment". MassTransit keeps this up to date.</summary>
    public string CurrentState { get; set; } = null!;

    public string CustomerId { get; set; } = null!;
    public string PaymentMethod { get; set; } = null!;
    public decimal Total { get; set; }
    public string? TransactionId { get; set; }
    public string? FailureReason { get; set; }
    public DateTime SubmittedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

/// <summary>Maps the saga instance to a table (MassTransit configures the CorrelationId key).</summary>
public class OrderStateMap : SagaClassMap<OrderState>
{
    protected override void Configure(EntityTypeBuilder<OrderState> entity, ModelBuilder model)
    {
        entity.Property(x => x.CurrentState).HasMaxLength(64);
        entity.Property(x => x.CustomerId).HasMaxLength(100);
        entity.Property(x => x.PaymentMethod).HasMaxLength(100);
        entity.Property(x => x.Total).HasPrecision(18, 2);
    }
}
