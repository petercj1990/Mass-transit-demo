using MassTransit;
using MassTransit.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using OrderSystem.Api.Data;
using OrderSystem.Api.Inventory;
using OrderSystem.Api.Orders;
using OrderSystem.Contracts;

namespace OrderSystem.Tests;

/// <summary>
/// Request/response against a consumer that uses EF Core. The database is in-memory
/// SQLite, which is enough to exercise the consumer's queries.
/// </summary>
public class SubmitOrderConsumerTests : IAsyncLifetime
{
    // An in-memory SQLite database exists for as long as this connection stays open.
    readonly SqliteConnection _connection = new("DataSource=:memory:");
    readonly ServiceProvider _provider;
    readonly ITestHarness _harness;

    public SubmitOrderConsumerTests()
    {
        _provider = new ServiceCollection()
            .AddLogging()
            .AddDbContext<OrderSystemDbContext>(options => options.UseSqlite(_connection))
            .AddMassTransitTestHarness(x => x.AddConsumer<SubmitOrderConsumer>())
            .BuildServiceProvider(true);

        _harness = _provider.GetRequiredService<ITestHarness>();
    }

    public async Task InitializeAsync()
    {
        await _connection.OpenAsync();

        await using (var scope = _provider.CreateAsyncScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<OrderSystemDbContext>();
            await dbContext.Database.EnsureCreatedAsync();

            var mug = new Product { Sku = "MUG", Name = "Mug", UnitPrice = 12.50m };
            mug.AddStock(10);
            dbContext.Products.Add(mug);
            await dbContext.SaveChangesAsync();
        }

        await _harness.Start();
    }

    public async Task DisposeAsync()
    {
        await _provider.DisposeAsync();
        await _connection.DisposeAsync();
    }

    [Fact]
    public async Task Accepts_a_valid_order_and_publishes_OrderSubmitted()
    {
        var orderId = NewId.NextGuid();
        var client = _harness.GetRequestClient<SubmitOrder>();

        var response = await client.GetResponse<OrderAccepted, OrderRejected>(
            new SubmitOrder(orderId, "alice", "card-ok", [new OrderLine("MUG", 2)]));

        Assert.True(response.Is(out Response<OrderAccepted>? accepted));
        Assert.Equal(25.00m, accepted.Message.Total);

        // Publishing OrderSubmitted is what starts the order saga.
        Assert.True(await _harness.Published.Any<OrderSubmitted>(x =>
            x.Context.Message.OrderId == orderId && x.Context.Message.Total == 25.00m));
    }

    [Fact]
    public async Task Rejects_an_order_for_an_unknown_product()
    {
        var orderId = NewId.NextGuid();
        var client = _harness.GetRequestClient<SubmitOrder>();

        var response = await client.GetResponse<OrderAccepted, OrderRejected>(
            new SubmitOrder(orderId, "alice", "card-ok", [new OrderLine("LAPTOP", 1)]));

        Assert.True(response.Is(out Response<OrderRejected>? rejected));
        Assert.Contains("LAPTOP", rejected.Message.Reason);
        Assert.Empty(_harness.Published.Select<OrderSubmitted>(x => x.Context.Message.OrderId == orderId));
    }

    [Fact]
    public async Task Rejects_an_order_with_a_zero_quantity()
    {
        var client = _harness.GetRequestClient<SubmitOrder>();

        var response = await client.GetResponse<OrderAccepted, OrderRejected>(
            new SubmitOrder(NewId.NextGuid(), "alice", "card-ok", [new OrderLine("MUG", 0)]));

        Assert.True(response.Is(out Response<OrderRejected>? _));
    }
}
