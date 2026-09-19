using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using SpaceCraft.Api.Common;
using SpaceCraft.Api.Email;
using SpaceCraft.Api.Features.Notifications;
using SpaceCraft.Domain.Notifications;
using SpaceCraft.Infrastructure.Persistence;
using Shouldly;
using Xunit;

namespace SpaceCraft.UnitTests;

/// <summary>
/// What a delivery row becomes once the transport has answered.
/// </summary>
/// <remarks>
/// No database here. The claim is Postgres's — <c>FOR UPDATE SKIP LOCKED</c> — and belongs
/// to the integration suite; what is pinned here is the settlement, which used to be
/// unreachable: the dispatcher handed the message to a queue that accepted everything, so no
/// transport failure ever came back to the row, and every message the relay refused stayed
/// recorded as sent.
/// </remarks>
public sealed class NotificationDispatcherTests
{
    private static readonly EmailMessage Message = new(
        "somebody@example.test", "Run failed — sdk", "The build did not finish.");

    [Fact]
    public async Task A_refused_message_is_put_back_with_the_attempt_counted()
    {
        var transport = new RefusingTransport(refusals: 1);
        var delivery = Claimed();

        await DispatcherWith(transport).SettleAsync(delivery, Message, TestContext.Current.CancellationToken);

        // Back on the sweep's list, but not for free: the refusal is what stops a recipient
        // the relay will never accept from being offered again for ever.
        delivery.SentAt.ShouldBeNull();
        delivery.Attempts.ShouldBe(1);
        delivery.IsAbandoned.ShouldBeFalse();
        transport.Offered.ShouldBe(1);
    }

    [Fact]
    public async Task A_message_the_relay_takes_on_the_second_sweep_is_sent_once_and_stays_sent()
    {
        var transport = new RefusingTransport(refusals: 1);
        var dispatcher = DispatcherWith(transport);
        var delivery = Claimed();

        await dispatcher.SettleAsync(delivery, Message, TestContext.Current.CancellationToken);

        // The next sweep finds it, claims it again, and this time the relay says yes.
        delivery.Claim(DateTimeOffset.UtcNow);
        await dispatcher.SettleAsync(delivery, Message, TestContext.Current.CancellationToken);

        delivery.SentAt.ShouldNotBeNull();
        delivery.Attempts.ShouldBe(2);
        transport.Offered.ShouldBe(2);
        transport.Accepted.ShouldBe(1);
    }

    [Fact]
    public async Task A_message_refused_every_time_is_given_up_on()
    {
        var transport = new RefusingTransport(refusals: int.MaxValue);
        var dispatcher = DispatcherWith(transport);
        var delivery = new NotificationDelivery(Guid.CreateVersion7(), NotificationChannel.Email);

        for (var sweep = 0; sweep < NotificationDelivery.MaxAttempts; sweep++)
        {
            delivery.IsAbandoned.ShouldBeFalse("the sweep still offers it");
            delivery.Claim(DateTimeOffset.UtcNow);
            await dispatcher.SettleAsync(delivery, Message, TestContext.Current.CancellationToken);
        }

        // Unsent, and no longer anyone's to send: the row stops matching the sweep.
        delivery.SentAt.ShouldBeNull();
        delivery.IsAbandoned.ShouldBeTrue();
        transport.Offered.ShouldBe(NotificationDelivery.MaxAttempts);
    }

    [Fact]
    public async Task An_accepted_message_keeps_the_stamp_the_claim_gave_it()
    {
        var transport = new RefusingTransport(refusals: 0);
        var delivery = Claimed();
        var stamped = delivery.SentAt;

        await DispatcherWith(transport).SettleAsync(delivery, Message, TestContext.Current.CancellationToken);

        // Sent means sent at the claim, on purpose — see the entity: the stamp goes on before
        // the send so that a crash in between loses the message rather than doubling it.
        delivery.SentAt.ShouldBe(stamped);
        delivery.Attempts.ShouldBe(1);
        transport.Accepted.ShouldBe(1);
    }

    [Fact]
    public async Task A_message_that_cannot_be_composed_is_put_back_and_the_failure_let_through()
    {
        var transport = new RefusingTransport(refusals: 0);
        var delivery = Claimed();

        // A context with a provider and no database behind it: the first query throws, the
        // way it does when the database went away between the claim and the compose.
        await using var db = new DesignTimeDbContextFactory().CreateDbContext([]);

        await Should.ThrowAsync<InvalidOperationException>(
            () => DispatcherWith(transport).DeliverAsync(db, delivery, TestContext.Current.CancellationToken));

        // Nothing was offered, so nothing was sent — and the row must not say otherwise. Back
        // on the sweep's list with the attempt counted, exactly as a refusal leaves it.
        delivery.SentAt.ShouldBeNull();
        delivery.Attempts.ShouldBe(1);
        transport.Offered.ShouldBe(0);
    }

    private static NotificationDelivery Claimed()
    {
        var delivery = new NotificationDelivery(Guid.CreateVersion7(), NotificationChannel.Email);
        delivery.Claim(DateTimeOffset.UtcNow);
        return delivery;
    }

    private static NotificationDispatcher DispatcherWith(IEmailTransport transport)
    {
        // The scope factory is what the sweep uses to reach the database; the settlement never
        // asks it for anything, so an empty container is enough to construct the service.
        var services = new ServiceCollection().BuildServiceProvider();

        return new NotificationDispatcher(
            services.GetRequiredService<IServiceScopeFactory>(),
            transport,
            Options.Create(new NotificationOptions()),
            Options.Create(new PublicUrlsOptions
            {
                App = "https://app.example.test",
                Api = "https://api.example.test",
            }),
            NullLogger<NotificationDispatcher>.Instance);
    }

    /// <summary>
    /// A relay that says no the first few times and yes after — the same exception the SMTP
    /// transport lets through once its own retries are spent.
    /// </summary>
    private sealed class RefusingTransport(int refusals) : IEmailTransport
    {
        private int _remaining = refusals;

        public int Offered { get; private set; }

        public int Accepted { get; private set; }

        public Task SendAsync(EmailMessage message, CancellationToken ct = default)
        {
            Offered++;

            if (_remaining > 0)
            {
                _remaining--;
                throw new IOException("Connection reset by the relay.");
            }

            Accepted++;
            return Task.CompletedTask;
        }
    }
}
