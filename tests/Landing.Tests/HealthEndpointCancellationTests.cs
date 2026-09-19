using Landing.Endpoints;
using Microsoft.Extensions.Logging.Abstractions;

namespace Landing.Tests;

public sealed class HealthEndpointCancellationTests
{
    [Fact]
    public async Task CallerCancellationStopsAProbeInsteadOfReturningDegradedHealth()
    {
        var probeStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var cancellation = new CancellationTokenSource();

        var check = HealthEndpoints.CheckHealthAsync(
            async () =>
            {
                probeStarted.SetResult();
                await Task.Delay(Timeout.InfiniteTimeSpan);
            },
            redisEnabled: false,
            () => Task.CompletedTask,
            NullLogger.Instance,
            correlationId: "cancelled-health-check",
            cancellation.Token);

        await probeStarted.Task;
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => check);
    }
}
