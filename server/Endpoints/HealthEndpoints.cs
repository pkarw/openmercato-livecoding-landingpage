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
            var postgres = await Probe(() => leads.PingAsync(ct));
            var redis = cache.Enabled
                ? await Probe(cache.PingAsync)
                : new ProbeResult(false, "REDIS_URL is not configured");

            return Results.Json(
                new { status = postgres.Ok && redis.Ok ? "ok" : "degraded", postgres, redis },
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
