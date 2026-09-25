using MassTransit;
using MassTransit.Testing;
using Microsoft.Extensions.DependencyInjection;
using OrderSystem.Api.Orders;
using OrderSystem.Api.Payments;
using OrderSystem.Contracts;

namespace OrderSystem.Tests;

/// <summary>
/// Saga tests on MassTransit's in-memory test harness, with no RabbitMQ and no database.
/// The test plays the other services: it publishes their events, then checks the
/// saga's state and what the saga sent or published in response.
/// </summary>
public class OrderStateMachineTests : IAsyncLifetime
{
    readonly ServiceProvider _provider;
    readonly ITestHarness _harness;
    readonly ISagaStateMachineTestHarness<OrderStateMachine, OrderState> _saga;

    public OrderStateMachineTests()
    {
        _provider = new ServiceCollection()
            .AddLogging()
            .AddMassTransitTestHarness(x =>
            {
                x.AddSagaStateMachine<OrderStateMachine, OrderState>()
                    .InMemoryRepository();
            })
            .BuildServiceProvider(true);

        _harness = _provider.GetRequiredService<ITestHarness>();
        _saga = _harness.GetSagaStateMachineHarness<OrderStateMachine, OrderState>();
    }

    public Task InitializeAsync() => _harness.Start();

    public async Task DisposeAsync() => await _provider.DisposeAsync();

    [Fact]
    public async Task Submitted_order_reserves_inventory()
    {
        var orderId = await SubmitOrder();

        Assert.True(await _harness.Sent.Any<ReserveInventory>(x => x.Context.Message.OrderId == orderId));
    }

    [Fact]
    public async Task Order_completes_when_payment_succeeds()
    {
        var orderId = await SubmitOrder();

        await _harness.Bus.Publish(new InventoryReserved(orderId));

        Assert.NotNull(await _saga.Exists(orderId, x => x.ProcessingPayment));
        Assert.True(await _harness.Sent.Any<ChargePayment>(x =>
            x.Context.Message.OrderId == orderId && x.Context.Message.Amount == 25.00m));

        await _harness.Bus.Publish(new PaymentCompleted(orderId, "txn_123", 25.00m));

        Assert.NotNull(await _saga.Exists(orderId, x => x.Completed));
        Assert.True(await _harness.Published.Any<OrderCompleted>(x => x.Context.Message.OrderId == orderId));
    }

    [Fact]
    public async Task Declined_payment_releases_the_reserved_inventory()
    {
        var orderId = await SubmitOrder();
        await _harness.Bus.Publish(new InventoryReserved(orderId));
        Assert.NotNull(await _saga.Exists(orderId, x => x.ProcessingPayment));

        await _harness.Bus.Publish(new PaymentDeclined(orderId, "Insufficient funds"));

        Assert.NotNull(await _saga.Exists(orderId, x => x.Failed));
        Assert.True(await _harness.Sent.Any<ReleaseInventory>(x => x.Context.Message.OrderId == orderId));
        Assert.True(await _harness.Published.Any<OrderFailed>(x =>
            x.Context.Message.OrderId == orderId && x.Context.Message.Reason.Contains("Insufficient funds")));
    }

    [Fact]
    public async Task Out_of_stock_fails_the_order_with_nothing_to_compensate()
    {
        var orderId = await SubmitOrder();

        await _harness.Bus.Publish(new InventoryUnavailable(orderId, "Out of stock: MUG"));

        Assert.NotNull(await _saga.Exists(orderId, x => x.Failed));
        Assert.True(await _harness.Published.Any<OrderFailed>(x => x.Context.Message.OrderId == orderId));

        // Nothing was reserved, so nothing is released. Select() checks what has been sent
        // so far without waiting, unlike Any(), which waits for the message to show up.
        Assert.Empty(_harness.Sent.Select<ReleaseInventory>(x => x.Context.Message.OrderId == orderId));
    }

    [Fact]
    public async Task Saga_answers_status_requests()
    {
        var orderId = await SubmitOrder();
        var client = _harness.GetRequestClient<GetOrderStatus>();

        var response = await client.GetResponse<OrderStatus, OrderNotFound>(new GetOrderStatus(orderId));

        Assert.True(response.Is(out Response<OrderStatus>? status));
        Assert.Equal(nameof(OrderStateMachine.ReservingInventory), status.Message.State);
        Assert.Equal(25.00m, status.Message.Total);
    }

    [Fact]
    public async Task Saga_answers_not_found_for_an_unknown_order()
    {
        var client = _harness.GetRequestClient<GetOrderStatus>();

        var response = await client.GetResponse<OrderStatus, OrderNotFound>(new GetOrderStatus(NewId.NextGuid()));

        Assert.True(response.Is(out Response<OrderNotFound>? _));
    }

    async Task<Guid> SubmitOrder()
    {
        var orderId = NewId.NextGuid();

        await _harness.Bus.Publish(new OrderSubmitted(orderId, "alice", FakePaymentGateway.CardOk,
            [new OrderLine("MUG", 2)], 25.00m, DateTime.UtcNow));

        // Exists() waits until the saga instance reaches the state (or the harness times out).
        Assert.NotNull(await _saga.Exists(orderId, x => x.ReservingInventory));

        return orderId;
    }
}
