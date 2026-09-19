using Landing.Data;
using Landing.Models;
using Landing.Notifications;
using Microsoft.Extensions.Logging.Abstractions;

namespace Landing.Tests;

public sealed class NotificationDeliveryWorkerTests
{
    [Fact]
    public async Task RetryableFailureIsRetriedWithTheSameSendKeyAndDeliveredOnce()
    {
        var delivery = Delivery(kind: NotificationKinds.Inbox);
        var store = new FakeStore(delivery);
        var sender = new FakeSender(
            new(EmailSendDisposition.RetryableFailure, "HTTP 503"),
            new(EmailSendDisposition.Delivered));
        var clock = new TestTimeProvider();
        var worker = Worker(store, sender, clock);

        Assert.True(await worker.ProcessNextAsync());
        Assert.False(store.Delivered);
        Assert.False(store.Terminal);
        Assert.Equal(1, store.Current.AttemptCount);

        clock.Advance(TimeSpan.FromMinutes(1));
        Assert.True(await worker.ProcessNextAsync());

        Assert.True(store.Delivered);
        Assert.Equal(2, sender.Keys.Count);
        Assert.Single(sender.Keys.Distinct());
        Assert.Equal("sales@example.test", sender.Messages[0].To);
    }

    [Fact]
    public async Task RetryableFailureStopsAfterTheBoundedAttemptLimit()
    {
        var delivery = Delivery(attemptCount: 4);
        var store = new FakeStore(delivery);
        var sender = new FakeSender(new EmailSendResult(EmailSendDisposition.RetryableFailure, "HTTP 503"));

        Assert.True(await Worker(store, sender, new TestTimeProvider()).ProcessNextAsync());

        Assert.True(store.Terminal);
        Assert.False(store.Delivered);
        Assert.Equal(5, store.Current.AttemptCount);
    }

    [Fact]
    public async Task DisabledProviderLeavesWorkPendingWithoutConsumingAnAttempt()
    {
        var store = new FakeStore(Delivery());
        var sender = new FakeSender(new EmailSendResult(EmailSendDisposition.Disabled, "not configured"));

        Assert.True(await Worker(store, sender, new TestTimeProvider()).ProcessNextAsync());

        Assert.False(store.Delivered);
        Assert.False(store.Terminal);
        Assert.Equal(0, store.Current.AttemptCount);
        Assert.Contains("not configured", store.Error);
    }

    private static NotificationDeliveryWorker Worker(
        INotificationDeliveryStore store,
        IEmailSender sender,
        TimeProvider clock) =>
        new(store, sender, new LeadNotifier(), clock, NullLogger<NotificationDeliveryWorker>.Instance);

    private static NotificationDelivery Delivery(string kind = NotificationKinds.Lead, int attemptCount = 0) =>
        new(
            Id: 42,
            LeadId: 7,
            Kind: kind,
            LeadEmail: "lead@example.test",
            LeadName: "Lead",
            Interest: Interests.OpenMercato,
            DiscountCode: "OMC10-AAAAAA",
            DiscountPercent: 10,
            LeadCreatedAt: DateTime.UtcNow,
            LeadsInbox: "sales@example.test",
            AttemptCount: attemptCount,
            LeaseToken: Guid.NewGuid());

    private sealed class FakeStore(NotificationDelivery delivery) : INotificationDeliveryStore
    {
        public NotificationDelivery Current { get; private set; } = delivery;
        public bool Delivered { get; private set; }
        public bool Terminal { get; private set; }
        public string Error { get; private set; } = string.Empty;

        public Task<NotificationDelivery?> LeaseNextAsync(
            TimeSpan leaseDuration,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<NotificationDelivery?>(Delivered || Terminal ? null : Current);

        public Task MarkDeliveredAsync(
            NotificationDelivery leased,
            CancellationToken cancellationToken = default)
        {
            Delivered = true;
            Current = Current with { AttemptCount = Current.AttemptCount + 1 };
            return Task.CompletedTask;
        }

        public Task MarkFailedAsync(
            NotificationDelivery leased,
            string error,
            bool terminal,
            DateTimeOffset? nextAttemptAt,
            CancellationToken cancellationToken = default)
        {
            Error = error;
            Terminal = terminal;
            Current = Current with
            {
                AttemptCount = Current.AttemptCount + 1,
                LeaseToken = Guid.NewGuid(),
            };
            return Task.CompletedTask;
        }

        public Task DeferAsync(
            NotificationDelivery leased,
            string reason,
            DateTimeOffset nextAttemptAt,
            CancellationToken cancellationToken = default)
        {
            Error = reason;
            Current = Current with { LeaseToken = Guid.NewGuid() };
            return Task.CompletedTask;
        }
    }

    private sealed class FakeSender(params EmailSendResult[] results) : IEmailSender
    {
        private readonly Queue<EmailSendResult> remaining = new(results);
        public List<string> Keys { get; } = [];
        public List<EmailMessage> Messages { get; } = [];

        public Task<EmailSendResult> SendAsync(
            EmailMessage message,
            string idempotencyKey,
            CancellationToken cancellationToken = default)
        {
            Messages.Add(message);
            Keys.Add(idempotencyKey);
            return Task.FromResult(remaining.Dequeue());
        }
    }

    private sealed class TestTimeProvider : TimeProvider
    {
        private DateTimeOffset now = new(2026, 9, 19, 0, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => now;
        public void Advance(TimeSpan duration) => now += duration;
    }
}
