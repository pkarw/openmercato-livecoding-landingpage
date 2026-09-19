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
        routes.MapGet("/api/health", (
            LeadRepository leads,
            NotificationDeliveryRepository notifications,
            EmailSettings emailSettings,
            CacheStore cache,
            ILoggerFactory loggerFactory,
            HttpContext context,
            CancellationToken ct) => CheckHealthWithNotificationsAsync(
                () => leads.PingAsync(ct),
                cache.Enabled,
                cache.PingAsync,
                token => notifications.GetStatusAsync(token),
                emailSettings.IsConfigured,
                loggerFactory.CreateLogger("Landing.Endpoints.Health"),
                context.TraceIdentifier,
                ct)).WithTags("Health");

        return routes;
    }

    internal static async Task<IResult> CheckHealthWithNotificationsAsync(
        Func<Task> pingPostgres,
        bool redisEnabled,
        Func<Task> pingRedis,
        Func<CancellationToken, Task<NotificationDeliveryStatus>> getNotificationStatus,
        bool emailConfigured,
        ILogger logger,
        string correlationId,
        CancellationToken cancellationToken = default)
    {
        var postgres = await Probe(pingPostgres, "postgres", logger, correlationId, cancellationToken);
        var redis = redisEnabled
            ? await Probe(pingRedis, "redis", logger, correlationId, cancellationToken)
            : new ProbeResult(false, "disabled");

        NotificationDeliveryStatus? notificationStatus = null;
        if (postgres.Ok)
        {
            try
            {
                notificationStatus = await getNotificationStatus(cancellationToken).WaitAsync(cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                logger.LogWarning(
                    "Health probe {Dependency} failed. CorrelationId: {CorrelationId}; ExceptionType: {ExceptionType}; HResult: {HResult}",
                    "notifications",
                    correlationId,
                    ex.GetType().FullName,
                    ex.HResult);
            }
        }

        var notificationsOk = emailConfigured
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
                    providerConfigured = emailConfigured,
                    pending = notificationStatus?.Pending,
                    failed = notificationStatus?.Failed,
                    oldestPendingAt = notificationStatus?.OldestPendingAt,
                    status = !emailConfigured
                        ? "disabled"
                        : notificationStatus is null
                            ? "unavailable"
                            : notificationStatus.Failed > 0
                                ? "degraded"
                                : "ok",
                },
            },
            statusCode: postgres.Ok ? StatusCodes.Status200OK : StatusCodes.Status503ServiceUnavailable);
    }

    internal static async Task<IResult> CheckHealthAsync(
        Func<Task> pingPostgres,
        bool redisEnabled,
        Func<Task> pingRedis,
        ILogger logger,
        string correlationId,
        CancellationToken cancellationToken = default)
    {
        var postgres = await Probe(pingPostgres, "postgres", logger, correlationId, cancellationToken);
        var redis = redisEnabled
            ? await Probe(pingRedis, "redis", logger, correlationId, cancellationToken)
            : new ProbeResult(false, "disabled");

        return Results.Json(
            new { status = postgres.Ok && redis.Ok ? "ok" : "degraded", postgres, redis },
            statusCode: postgres.Ok ? StatusCodes.Status200OK : StatusCodes.Status503ServiceUnavailable);
    }

    private static async Task<ProbeResult> Probe(
        Func<Task> probe,
        string dependency,
        ILogger logger,
        string correlationId,
        CancellationToken cancellationToken)
    {
        try
        {
            await probe().WaitAsync(cancellationToken);
            return new ProbeResult(true, null);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogWarning(
                "Health probe {Dependency} failed. CorrelationId: {CorrelationId}; ExceptionType: {ExceptionType}; HResult: {HResult}",
                dependency,
                correlationId,
                ex.GetType().FullName,
                ex.HResult);
            return new ProbeResult(false, "unavailable");
        }
    }
}
