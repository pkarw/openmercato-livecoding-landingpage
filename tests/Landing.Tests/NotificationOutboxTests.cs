using Landing.Models;
using Landing.Notifications;
using Microsoft.Extensions.Logging.Abstractions;

namespace Landing.Tests;

[Collection(PostgresCollection.Name)]
public sealed class NotificationOutboxTests(PostgresFixture postgres) : IAsyncLifetime
{
    private const string Email = "lead@example.test";

    public Task InitializeAsync() => postgres.ResetAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task ReservationAndBothNotificationIntentsCommitTogether()
    {
        var (lead, alreadyClaimed) = await ClaimAsync();

        Assert.False(alreadyClaimed);
        var deliveries = await postgres.ReadDeliveriesAsync();
        Assert.Collection(
            deliveries,
            delivery => AssertDelivery(delivery, lead.Id, NotificationKinds.Lead),
            delivery => AssertDelivery(delivery, lead.Id, NotificationKinds.Inbox));
    }

    [Fact]
    public async Task DuplicateAndConcurrentClaimsDoNotDuplicatePendingNotifications()
    {
        await ClaimAsync();

        var repeats = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => ClaimAsync()));

        Assert.All(repeats, result => Assert.True(result.AlreadyClaimed));
        var deliveries = await postgres.ReadDeliveriesAsync();
        Assert.Equal(2, deliveries.Count);
        Assert.Equal(2, deliveries.Select(delivery => delivery.Kind).Distinct().Count());
    }

    [Fact]
    public async Task ExpiredLeaseCanBeRecoveredAfterAWorkerRestart()
    {
        await ClaimAsync();
        var firstLease = await postgres.Deliveries.LeaseNextAsync(TimeSpan.Zero);

        var recoveredLease = await postgres.Deliveries.LeaseNextAsync(TimeSpan.FromSeconds(30));

        Assert.NotNull(firstLease);
        Assert.NotNull(recoveredLease);
        Assert.Equal(firstLease.Id, recoveredLease.Id);
        Assert.NotEqual(firstLease.LeaseToken, recoveredLease.LeaseToken);
    }

    [Fact]
    public async Task InitialProviderFailureThenRecoveryDeliversEachIntendedMessageOnce()
    {
        await ClaimAsync();
        var sender = new FailOncePerMessageSender();
        var worker = new NotificationDeliveryWorker(
            postgres.Deliveries,
            sender,
            new LeadNotifier(),
            new PastTimeProvider(),
            NullLogger<NotificationDeliveryWorker>.Instance);

        for (var i = 0; i < 4; i++) Assert.True(await worker.ProcessNextAsync());

        var status = await postgres.Deliveries.GetStatusAsync();
        Assert.Equal(0, status.Pending);
        Assert.Equal(0, status.Failed);
        Assert.Equal(2, sender.AttemptsByKey.Count);
        Assert.All(sender.AttemptsByKey.Values, attempts => Assert.Equal(2, attempts));
    }

    private Task<(Lead Lead, bool AlreadyClaimed)> ClaimAsync() =>
        postgres.Leads.ClaimAsync(
            Email,
            "Lead",
            Interests.OpenMercato,
            "OMC10-AAAAAA",
            10,
            "sales@example.test",
            marketingConsent: true,
            source: "test");

    private static void AssertDelivery(StoredDelivery delivery, int leadId, string kind)
    {
        Assert.Equal(leadId, delivery.LeadId);
        Assert.Equal(kind, delivery.Kind);
        Assert.Equal("pending", delivery.Status);
        Assert.Equal(Email, delivery.LeadEmail);
        Assert.Equal(Interests.OpenMercato, delivery.Interest);
        Assert.Equal(0, delivery.AttemptCount);
    }

    private sealed class FailOncePerMessageSender : IEmailSender
    {
        public Dictionary<string, int> AttemptsByKey { get; } = [];

        public Task<EmailSendResult> SendAsync(
            EmailMessage message,
            string idempotencyKey,
            CancellationToken cancellationToken = default)
        {
            AttemptsByKey.TryGetValue(idempotencyKey, out var attempts);
            AttemptsByKey[idempotencyKey] = ++attempts;
            return Task.FromResult(attempts == 1
                ? new EmailSendResult(EmailSendDisposition.RetryableFailure, "HTTP 503")
                : new EmailSendResult(EmailSendDisposition.Delivered));
        }
    }

    private sealed class PastTimeProvider : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => DateTimeOffset.UtcNow - TimeSpan.FromHours(1);
    }
}
