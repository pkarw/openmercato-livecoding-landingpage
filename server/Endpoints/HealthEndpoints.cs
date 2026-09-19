using Landing.Caching;
using Landing.Data;

namespace Landing.Endpoints;

public sealed record ProbeResult(bool Ok, string? Error);

public static class HealthEndpoints
{
    public static IEndpointRouteBuilder MapHealthEndpoints(this IEndpointRouteBuilder routes)
    {
        routes.MapGet("/api/health", (
            LeadRepository leads,
            CacheStore cache,
            ILoggerFactory loggerFactory,
            HttpContext context,
            CancellationToken ct) => CheckHealthAsync(
                () => leads.PingAsync(ct),
                cache.Enabled,
                cache.PingAsync,
                loggerFactory.CreateLogger("Landing.Endpoints.Health"),
                context.TraceIdentifier,
                ct)).WithTags("Health");

        return routes;
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
