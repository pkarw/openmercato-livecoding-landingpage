using System.Text.Json;
using Landing.Endpoints;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Xunit;

namespace Landing.Tests;

public sealed class HealthEndpointsTests
{
    private const string SensitiveMessage =
        "password=synthetic-secret host=internal-db.example stack=/srv/private/Health.cs:42";

    [Fact]
    public async Task PostgresFailureReturns503WithoutInternalDiagnostics()
    {
        var logger = new RecordingLogger();

        var result = await HealthEndpoints.CheckHealthAsync(
            () => Task.FromException(new InvalidOperationException(SensitiveMessage)),
            redisEnabled: true,
            () => Task.CompletedTask,
            logger,
            correlationId: "health-trace-42");

        Assert.Equal(StatusCodes.Status503ServiceUnavailable, GetStatusCode(result));

        var json = SerializeValue(result);
        Assert.Contains("\"status\":\"degraded\"", json);
        Assert.Contains("\"error\":\"unavailable\"", json);
        Assert.DoesNotContain(SensitiveMessage, json);
        Assert.DoesNotContain("synthetic-secret", json);
        Assert.DoesNotContain("internal-db.example", json);
        Assert.DoesNotContain("/srv/private", json);

        var log = Assert.Single(logger.Messages);
        Assert.Contains("health-trace-42", log);
        Assert.Contains(typeof(InvalidOperationException).FullName!, log);
        Assert.DoesNotContain(SensitiveMessage, log);
        Assert.DoesNotContain("synthetic-secret", log);
    }

    [Fact]
    public async Task RedisFailureKeeps200AndRedactsPublicDiagnostics()
    {
        var logger = new RecordingLogger();

        var result = await HealthEndpoints.CheckHealthAsync(
            () => Task.CompletedTask,
            redisEnabled: true,
            () => Task.FromException(new TimeoutException(SensitiveMessage)),
            logger,
            correlationId: "health-trace-redis");

        Assert.Equal(StatusCodes.Status200OK, GetStatusCode(result));

        var json = SerializeValue(result);
        Assert.Contains("\"status\":\"degraded\"", json);
        Assert.Contains("\"error\":\"unavailable\"", json);
        Assert.DoesNotContain(SensitiveMessage, json);
        Assert.Contains("health-trace-redis", Assert.Single(logger.Messages));
    }

    [Fact]
    public async Task DisabledRedisDoesNotExposeConfigurationVariableName()
    {
        var result = await HealthEndpoints.CheckHealthAsync(
            () => Task.CompletedTask,
            redisEnabled: false,
            () => Task.FromException(new InvalidOperationException("should not run")),
            new RecordingLogger(),
            correlationId: "health-trace-disabled");

        Assert.Equal(StatusCodes.Status200OK, GetStatusCode(result));

        var json = SerializeValue(result);
        Assert.Contains("\"error\":\"disabled\"", json);
        Assert.DoesNotContain("REDIS_URL", json);
    }

    private static int? GetStatusCode(IResult result) =>
        Assert.IsAssignableFrom<IStatusCodeHttpResult>(result).StatusCode;

    private static string SerializeValue(IResult result) =>
        JsonSerializer.Serialize(
            Assert.IsAssignableFrom<IValueHttpResult>(result).Value,
            JsonSerializerOptions.Web);

    private sealed class RecordingLogger : ILogger
    {
        public List<string> Messages { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter) =>
            Messages.Add(formatter(state, exception));
    }
}
