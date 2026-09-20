using Landing.Data;
using Landing.Models;

namespace Landing.Notifications;

public sealed class NotificationDeliveryWorker(
    INotificationDeliveryStore deliveries,
    IEmailSender sender,
    LeadNotifier notifier,
    TimeProvider clock,
    ILogger<NotificationDeliveryWorker> logger) : BackgroundService
{
    internal const int MaxAttempts = 5;
    private static readonly TimeSpan LeaseDuration = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan DisabledDelay = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan IdleDelay = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan ErrorDelay = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan[] RetryDelays =
    [
        TimeSpan.FromSeconds(10),
        TimeSpan.FromMinutes(1),
        TimeSpan.FromMinutes(5),
        TimeSpan.FromMinutes(30),
    ];

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                if (!await ProcessNextAsync(stoppingToken))
                {
                    await Task.Delay(IdleDelay, clock, stoppingToken);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Notification delivery worker failed; retrying the queue poll.");
                await Task.Delay(ErrorDelay, clock, stoppingToken);
            }
        }
    }

    public async Task<bool> ProcessNextAsync(CancellationToken cancellationToken = default)
    {
        var delivery = await deliveries.LeaseNextAsync(LeaseDuration, cancellationToken);
        if (delivery is null) return false;

        var message = notifier.CreateMessage(delivery);
        var result = await sender.SendAsync(
            message,
            $"lead-notification/{delivery.Id}",
            cancellationToken);

        switch (result.Disposition)
        {
            case EmailSendDisposition.Delivered:
                await deliveries.MarkDeliveredAsync(delivery, cancellationToken);
                logger.LogInformation(
                    "Delivered {Kind} notification {NotificationId} for lead {LeadId}.",
                    delivery.Kind,
                    delivery.Id,
                    delivery.LeadId);
                break;

            case EmailSendDisposition.Disabled:
                await deliveries.DeferAsync(
                    delivery,
                    result.Error ?? "Email provider is disabled",
                    clock.GetUtcNow() + DisabledDelay,
                    cancellationToken);
                logger.LogWarning(
                    "Notification {NotificationId} remains pending because the email provider is disabled.",
                    delivery.Id);
                break;

            case EmailSendDisposition.PermanentFailure:
                await deliveries.MarkFailedAsync(
                    delivery,
                    result.Error ?? "Permanent email provider failure",
                    terminal: true,
                    nextAttemptAt: null,
                    cancellationToken);
                logger.LogError(
                    "Notification {NotificationId} permanently failed on attempt {Attempt}: {Error}",
                    delivery.Id,
                    delivery.AttemptCount + 1,
                    result.Error);
                break;

            case EmailSendDisposition.RetryableFailure:
                var nextAttempt = delivery.AttemptCount + 1;
                var exhausted = nextAttempt >= MaxAttempts;
                var retryAt = exhausted
                    ? (DateTimeOffset?)null
                    : clock.GetUtcNow() + RetryDelays[nextAttempt - 1];
                await deliveries.MarkFailedAsync(
                    delivery,
                    result.Error ?? "Retryable email provider failure",
                    terminal: exhausted,
                    nextAttemptAt: retryAt,
                    cancellationToken);
                if (exhausted)
                {
                    logger.LogError(
                        "Notification {NotificationId} exhausted {AttemptCount} attempts: {Error}",
                        delivery.Id,
                        nextAttempt,
                        result.Error);
                }
                else
                {
                    logger.LogWarning(
                        "Notification {NotificationId} failed on attempt {Attempt}; next attempt at {RetryAt}: {Error}",
                        delivery.Id,
                        nextAttempt,
                        retryAt,
                        result.Error);
                }
                break;

            default:
                throw new ArgumentOutOfRangeException(nameof(result.Disposition));
        }

        return true;
    }
}
