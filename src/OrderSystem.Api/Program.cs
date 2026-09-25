using MassTransit;
using Microsoft.EntityFrameworkCore;
using OrderSystem.Api.Data;
using OrderSystem.Api.Inventory;
using OrderSystem.Api.Notifications;
using OrderSystem.Api.Orders;
using OrderSystem.Api.Payments;
using OrderSystem.Contracts;

var builder = WebApplication.CreateBuilder(args);

builder.Logging.AddSimpleConsole(options =>
{
    options.SingleLine = true;
    options.TimestampFormat = "HH:mm:ss.fff ";
});

builder.Services.AddDbContext<OrderSystemDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("OrderSystem")));

// Hosted services start in registration order, so the tables exist before the bus
// (started by a hosted service that AddMassTransit registers) begins consuming messages.
builder.Services.AddHostedService<DatabaseInitializer>();

builder.Services.AddSingleton<IPaymentGateway, FakePaymentGateway>();

// Host, username and password for RabbitMQ, read from appsettings.json.
builder.Services.AddOptions<RabbitMqTransportOptions>().BindConfiguration("RabbitMqTransport");

builder.Services.AddMassTransit(x =>
{
    // Name queues in kebab-case, e.g. CustomerNotificationConsumer -> "customer-notification".
    // The definitions set explicit names for the queues that other services send to.
    x.SetKebabCaseEndpointNameFormatter();

    // The transactional outbox, stored in Postgres through EF Core. Each consumer
    // definition switches it on for its endpoint with UseEntityFrameworkOutbox().
    x.AddEntityFrameworkOutbox<OrderSystemDbContext>(o =>
    {
        o.UsePostgres();

        // The bus outbox covers code that ISN'T a consumer, such as an API endpoint.
        // Messages published through the scoped IPublishEndpoint are saved to the
        // OutboxMessage table by SaveChanges, then delivered by a background service.
        o.UseBusOutbox();
    });

    x.AddConsumer<SubmitOrderConsumer, SubmitOrderConsumerDefinition>();
    x.AddConsumer<ReserveInventoryConsumer, ReserveInventoryConsumerDefinition>();
    x.AddConsumer<ReleaseInventoryConsumer, ReleaseInventoryConsumerDefinition>();
    x.AddConsumer<ChargePaymentConsumer, ChargePaymentConsumerDefinition>();
    x.AddConsumer<CustomerNotificationConsumer, CustomerNotificationConsumerDefinition>();

    x.AddSagaStateMachine<OrderStateMachine, OrderState, OrderStateDefinition>()
        .EntityFrameworkRepository(r =>
        {
            // Use the same DbContext (and so the same transaction) as the outbox:
            // saga state and outgoing messages are committed together.
            r.ExistingDbContext<OrderSystemDbContext>();

            // Pessimistic concurrency: the order's row is locked (SELECT ... FOR UPDATE)
            // while a message for that order is being processed.
            r.UsePostgres();
        });

    // Request clients: where each request goes and how long to wait for a reply.
    x.AddRequestClient<SubmitOrder>(QueueNames.Address(QueueNames.SubmitOrder), RequestTimeout.After(s: 10));
    x.AddRequestClient<GetOrderStatus>(QueueNames.Address(QueueNames.OrderSaga), RequestTimeout.After(s: 5));

    x.UsingRabbitMq((context, cfg) =>
    {
        // Create a queue for every consumer and saga above, apply their definitions,
        // and subscribe each queue to the message types it consumes.
        cfg.ConfigureEndpoints(context);
    });
});

var app = builder.Build();

app.MapOrderEndpoints();
app.MapInventoryEndpoints();
app.MapHealthChecks("/health");

// Everything MassTransit built at startup: each queue, its exchange bindings, and its
// middleware pipeline. Search the output for "charge-payment" to see retry -> outbox -> consumer.
app.MapGet("/debug/bus", (IBus bus) => Results.Json(bus.GetProbeResult()));

app.Run();
