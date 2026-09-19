using Landing.Caching;
using Landing.Data;
using Landing.Models;
using Landing.Notifications;

namespace Landing.Endpoints;

public sealed record ProbeResult(bool Ok, string? Error);

public static class HealthEndpoints
{
    public static IEndpointRouteBuilder MapHealthEndpoints(this IEndpointRouteBuilder routes)
    {
        routes.MapGet("/api/health", async (
            LeadRepository leads,
            NotificationDeliveryRepository notifications,
            EmailSettings emailSettings,
            CacheStore cache,
            CancellationToken ct) =>
        {
            var postgres = await Probe(() => leads.PingAsync(ct));
            var redis = cache.Enabled
                ? await Probe(cache.PingAsync)
                : new ProbeResult(false, "REDIS_URL is not configured");
            NotificationDeliveryStatus? notificationStatus = null;
            if (postgres.Ok)
            {
                notificationStatus = await notifications.GetStatusAsync(ct);
            }
            var notificationsOk = emailSettings.IsConfigured
                && notificationStatus is not null
                && notificationStatus.Failed == 0;

            return Results.Json(
                new
                {
                    status = postgres.Ok && redis.Ok && notificationsOk ? "ok" : "degraded",
                    postgres,
                    redis,
                    notifications = new
                    {
                        providerConfigured = emailSettings.IsConfigured,
                        pending = notificationStatus?.Pending,
                        failed = notificationStatus?.Failed,
                        oldestPendingAt = notificationStatus?.OldestPendingAt,
                        status = !emailSettings.IsConfigured
                            ? "disabled"
                            : notificationStatus is null
                                ? "unavailable"
                                : notificationStatus.Failed > 0
                                    ? "degraded"
                                    : "ok",
                    },
                },
                statusCode: postgres.Ok ? StatusCodes.Status200OK : StatusCodes.Status503ServiceUnavailable);
        }).WithTags("Health");

        return routes;
    }

    private static async Task<ProbeResult> Probe(Func<Task> probe)
    {
        try
        {
            await probe();
            return new ProbeResult(true, null);
        }
        catch (Exception ex)
        {
            return new ProbeResult(false, ex.Message);
        }
    }
}
