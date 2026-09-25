using MassTransit;
using MassTransit.Testing;
using Microsoft.Extensions.DependencyInjection;
using OrderSystem.Api.Orders;
using OrderSystem.Api.Payments;
using OrderSystem.Contracts;

namespace OrderSystem.Tests;

/// <summary>
/// The saga and the real ChargePaymentConsumer running together, to watch retries and
/// faults play out: a flaky gateway recovers thanks to retry, and a dead gateway uses up
/// the retries, so MassTransit publishes Fault&lt;ChargePayment&gt; and the saga compensates.
/// </summary>
public class PaymentRetryTests : IAsyncLifetime
{
    readonly ServiceProvider _provider;
    readonly ITestHarness _harness;
    readonly ISagaStateMachineTestHarness<OrderStateMachine, OrderState> _saga;

    public PaymentRetryTests()
    {
        _provider = new ServiceCollection()
            .AddLogging()
            .AddSingleton<IPaymentGateway, FakePaymentGateway>()
            .AddMassTransitTestHarness(x =>
            {
                x.AddSagaStateMachine<OrderStateMachine, OrderState>()
                    .InMemoryRepository();

                // Listen on the queue the saga sends ChargePayment to.
                x.AddConsumer<ChargePaymentConsumer>()
                    .Endpoint(e => e.Name = QueueNames.ChargePayment);

                // Production backs off exponentially (see ChargePaymentConsumerDefinition).
                // Tests retry immediately so they stay fast.
                x.AddConfigureEndpointsCallback((_, _, endpoint) =>
                    endpoint.UseMessageRetry(r => r.Immediate(4)));
            })
            .BuildServiceProvider(true);

        _harness = _provider.GetRequiredService<ITestHarness>();
        _saga = _harness.GetSagaStateMachineHarness<OrderStateMachine, OrderState>();
    }

    public Task InitializeAsync() => _harness.Start();

    public async Task DisposeAsync() => await _provider.DisposeAsync();

    [Fact]
    public async Task Flaky_gateway_is_retried_until_the_order_completes()
    {
        var orderId = await ReserveInventoryFor(FakePaymentGateway.CardFlaky);

        Assert.NotNull(await _saga.Exists(orderId, x => x.Completed));
        Assert.Empty(_harness.Published.Select<Fault<ChargePayment>>());
    }

    [Fact]
    public async Task When_retries_run_out_the_fault_triggers_compensation()
    {
        var orderId = await ReserveInventoryFor(FakePaymentGateway.GatewayDown);

        Assert.True(await _harness.Published.Any<Fault<ChargePayment>>(x =>
            x.Context.Message.Message.OrderId == orderId));

        Assert.NotNull(await _saga.Exists(orderId, x => x.Failed));
        Assert.True(await _harness.Sent.Any<ReleaseInventory>(x => x.Context.Message.OrderId == orderId));
    }

    [Fact]
    public async Task Declined_card_is_not_retried()
    {
        var orderId = await ReserveInventoryFor(FakePaymentGateway.CardDeclined);

        Assert.NotNull(await _saga.Exists(orderId, x => x.Failed));
        Assert.True(await _harness.Published.Any<PaymentDeclined>(x => x.Context.Message.OrderId == orderId));
        Assert.Empty(_harness.Published.Select<Fault<ChargePayment>>());
    }

    /// <summary>Starts an order and plays the inventory service, so the saga moves on to payment.</summary>
    async Task<Guid> ReserveInventoryFor(string paymentMethod)
    {
        var orderId = NewId.NextGuid();

        await _harness.Bus.Publish(new OrderSubmitted(orderId, "bob", paymentMethod,
            [new OrderLine("MUG", 1)], 12.50m, DateTime.UtcNow));
        Assert.NotNull(await _saga.Exists(orderId, x => x.ReservingInventory));

        await _harness.Bus.Publish(new InventoryReserved(orderId));

        return orderId;
    }
}
