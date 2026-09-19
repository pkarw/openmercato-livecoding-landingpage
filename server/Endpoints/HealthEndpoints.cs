using Landing.Caching;
using Landing.Data;

namespace Landing.Endpoints;

public sealed record ProbeResult(bool Ok, string? Error);

public static class HealthEndpoints
{
    public static IEndpointRouteBuilder MapHealthEndpoints(this IEndpointRouteBuilder routes)
    {
        routes.MapGet("/api/health", async (LeadRepository leads, CacheStore cache, CancellationToken ct) =>
        {
            var postgres = await Probe(() => leads.PingAsync(ct), ct);
            var redis = cache.Enabled
                ? await Probe(cache.PingAsync, ct)
                : new ProbeResult(false, "REDIS_URL is not configured");

            return Results.Json(
                new { status = postgres.Ok && redis.Ok ? "ok" : "degraded", postgres, redis },
                statusCode: postgres.Ok ? StatusCodes.Status200OK : StatusCodes.Status503ServiceUnavailable);
        }).WithTags("Health");

        return routes;
    }

    // A probe failure is the answer this endpoint exists to give, so every exception becomes a
    // result — except the caller hanging up. Reporting that as "postgres is down" would invent a
    // diagnosis out of a cancelled request, so it is rethrown and the pipeline abandons the reply.
    private static async Task<ProbeResult> Probe(Func<Task> probe, CancellationToken cancellationToken)
    {
        try
        {
            await probe();
            return new ProbeResult(true, null);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            return new ProbeResult(false, ex.Message);
        }
    }
}
