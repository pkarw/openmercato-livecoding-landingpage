using Dapper;
using Landing.Configuration;
using Landing.Data;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;

namespace Landing.Tests;

/// <summary>
/// Hosts the real application against a throwaway database created on the PostgreSQL server named
/// by LANDING_TEST_DATABASE_URL. Email and Redis are disabled before Program starts, so the test
/// cannot send messages or touch a shared cache.
/// </summary>
public sealed class PostgresWebApplicationFixture : IAsyncLifetime
{
    private const string UrlVariable = "LANDING_TEST_DATABASE_URL";
    private readonly Dictionary<string, string?> originalEnvironment = new();
    private string maintenanceConnectionString = string.Empty;
    private string databaseName = string.Empty;
    private NpgsqlDataSource? dataSource;

    public WebApplicationFactory<Program> Factory { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        var url = Environment.GetEnvironmentVariable(UrlVariable);
        if (string.IsNullOrWhiteSpace(url))
        {
            throw new InvalidOperationException(
                $"{UrlVariable} is not set. Point it at a PostgreSQL server these tests may use " +
                "to create and drop a throwaway database. Never point it at production.");
        }

        maintenanceConnectionString = ConnectionUrls.ToNpgsqlConnectionString(url);
        databaseName = $"landing_test_{Guid.NewGuid():N}";

        await using (var maintenance = new NpgsqlConnection(maintenanceConnectionString))
        {
            await maintenance.OpenAsync();
            await maintenance.ExecuteAsync($"create database \"{databaseName}\"");
        }

        var testConnectionString = new NpgsqlConnectionStringBuilder(maintenanceConnectionString)
        {
            Database = databaseName,
        }.ConnectionString;

        dataSource = new NpgsqlDataSourceBuilder(testConnectionString).Build();
        var migrator = new Migrator(dataSource, MigrationsDirectory(), NullLogger<Migrator>.Instance);
        await migrator.MigrateAsync();

        SetEnvironment("DATABASE_URL", testConnectionString);
        SetEnvironment("REDIS_URL", string.Empty);
        SetEnvironment("RESEND_API_KEY", string.Empty);
        SetEnvironment("OFFER_DISCOUNT_PERCENT", "10");
        SetEnvironment("OFFER_ENDS_AT", "2099-09-20T23:59:59+02:00");

        Factory = new WebApplicationFactory<Program>();
    }

    public async Task DisposeAsync()
    {
        if (Factory is not null) await Factory.DisposeAsync();
        if (dataSource is not null) await dataSource.DisposeAsync();

        RestoreEnvironment();

        if (string.IsNullOrEmpty(databaseName)) return;

        NpgsqlConnection.ClearAllPools();
        await using var maintenance = new NpgsqlConnection(maintenanceConnectionString);
        await maintenance.OpenAsync();
        await maintenance.ExecuteAsync($"drop database if exists \"{databaseName}\" with (force)");
    }

    private void SetEnvironment(string name, string value)
    {
        originalEnvironment[name] = Environment.GetEnvironmentVariable(name);
        Environment.SetEnvironmentVariable(name, value);
    }

    private void RestoreEnvironment()
    {
        foreach (var (name, value) in originalEnvironment)
        {
            Environment.SetEnvironmentVariable(name, value);
        }
    }

    private static string MigrationsDirectory() =>
        Path.Combine(DotEnv.FindRepositoryRoot(AppContext.BaseDirectory), "db", "migrations");
}

[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class PostgresWebApplicationCollection : ICollectionFixture<PostgresWebApplicationFixture>
{
    public const string Name = "postgres-web-application";
}
