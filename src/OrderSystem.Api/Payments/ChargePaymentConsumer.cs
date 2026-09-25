using MassTransit;
using OrderSystem.Api.Data;
using OrderSystem.Contracts;

namespace OrderSystem.Api.Payments;

public class ChargePaymentConsumer(IPaymentGateway paymentGateway, ILogger<ChargePaymentConsumer> logger)
    : IConsumer<ChargePayment>
{
    public async Task Consume(ConsumeContext<ChargePayment> context)
    {
        var payment = context.Message;

        // GetRetryAttempt() is 0 on the first try, then 1, 2, 3... as the retry policy tries again.
        var attempt = context.GetRetryAttempt() + 1;

        logger.LogInformation("Order {OrderId}: charging {Amount} to {PaymentMethod} (attempt {Attempt})",
            payment.OrderId, payment.Amount, payment.PaymentMethod, attempt);

        // Transient failures surface as exceptions and are NOT caught here: throwing is
        // how a consumer tells MassTransit "this didn't work, apply the retry policy".
        var result = await paymentGateway.Charge(payment.OrderId, payment.Amount, payment.PaymentMethod,
            context.CancellationToken);

        if (result.Approved)
        {
            await context.Publish(new PaymentCompleted(payment.OrderId, result.TransactionId!, payment.Amount));

            logger.LogInformation("Order {OrderId}: payment approved ({TransactionId})",
                payment.OrderId, result.TransactionId);
        }
        else
        {
            // A declined card is an expected business outcome, not an error. Publish an
            // event and let the saga decide what to do. Throwing would trigger pointless retries.
            await context.Publish(new PaymentDeclined(payment.OrderId, result.DeclineReason!));

            logger.LogInformation("Order {OrderId}: payment declined ({Reason})",
                payment.OrderId, result.DeclineReason);
        }
    }
}

public class ChargePaymentConsumerDefinition : ConsumerDefinition<ChargePaymentConsumer>
{
    public ChargePaymentConsumerDefinition()
    {
        EndpointName = QueueNames.ChargePayment;
    }

    protected override void ConfigureConsumer(IReceiveEndpointConfigurator endpointConfigurator,
        IConsumerConfigurator<ChargePaymentConsumer> consumerConfigurator, IRegistrationContext context)
    {
        // When the last retry fails, MassTransit moves the message to the "charge-payment_error"
        // queue and publishes Fault<ChargePayment>, which the saga handles.
        endpointConfigurator.UseMessageRetry(r =>
        {
            // Only retry failures that might succeed next time. Anything else (a bug, bad
            // data) goes straight to the error queue instead of being retried pointlessly.
            r.Handle<PaymentGatewayUnavailableException>();

            // Back off exponentially so a struggling gateway gets room to recover. Waits are
            // min + 2^n * delta, with +/-20% random jitter so many failing consumers
            // don't retry in lockstep: about 0.5s, 0.8s, 1.4s, 2.6s for the 4 retries.
            r.Exponential(retryLimit: 4,
                minInterval: TimeSpan.FromMilliseconds(200),
                maxInterval: TimeSpan.FromSeconds(5),
                intervalDelta: TimeSpan.FromMilliseconds(300));
        });

        endpointConfigurator.UseEntityFrameworkOutbox<OrderSystemDbContext>(context);
    }
}
