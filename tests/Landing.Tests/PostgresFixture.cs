using Dapper;
using Landing.Configuration;
using Landing.Data;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;

namespace Landing.Tests;

/// <summary>
/// A throwaway database per test run, created on the server named by LANDING_TEST_DATABASE_URL,
/// migrated with the repository's own db/migrations, and dropped afterwards. The variable is
/// deliberately separate from DATABASE_URL so a test run can never reach real lead data.
/// </summary>
public sealed class PostgresFixture : IAsyncLifetime
{
    private const string UrlVariable = "LANDING_TEST_DATABASE_URL";

    private string maintenanceConnectionString = string.Empty;
    private string databaseName = string.Empty;

    public NpgsqlDataSource DataSource { get; private set; } = null!;

    public LeadRepository Leads { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        var url = Environment.GetEnvironmentVariable(UrlVariable);
        if (string.IsNullOrWhiteSpace(url))
        {
            throw new InvalidOperationException(
                $"{UrlVariable} is not set. Point it at a PostgreSQL server these tests may create and drop a " +
                "throwaway database on, for example postgres://postgres@127.0.0.1:5432/postgres. Never point it " +
                "at the production database.");
        }

        maintenanceConnectionString = ConnectionUrls.ToNpgsqlConnectionString(url);
        databaseName = $"landing_test_{Guid.NewGuid():N}";

        await using (var maintenance = new NpgsqlConnection(maintenanceConnectionString))
        {
            await maintenance.OpenAsync();
            // The database name is generated here, never supplied by a caller, and identifiers
            // cannot be parameterised — quote it so the statement stays well-formed regardless.
            await maintenance.ExecuteAsync($"create database \"{databaseName}\"");
        }

        var testConnectionString = new NpgsqlConnectionStringBuilder(maintenanceConnectionString)
        {
            Database = databaseName,
        }.ConnectionString;

        DataSource = new NpgsqlDataSourceBuilder(testConnectionString).Build();
        Leads = new LeadRepository(DataSource);

        var migrator = new Migrator(DataSource, MigrationsDirectory(), NullLogger<Migrator>.Instance);
        await migrator.MigrateAsync();
    }

    public async Task DisposeAsync()
    {
        if (DataSource is not null) await DataSource.DisposeAsync();
        if (string.IsNullOrEmpty(databaseName)) return;

        NpgsqlConnection.ClearAllPools();
        await using var maintenance = new NpgsqlConnection(maintenanceConnectionString);
        await maintenance.OpenAsync();
        await maintenance.ExecuteAsync($"drop database if exists \"{databaseName}\" with (force)");
    }

    public async Task ResetAsync()
    {
        await using var connection = await DataSource.OpenConnectionAsync();
        await connection.ExecuteAsync("truncate table leads restart identity");
    }

    /// <summary>The stored row as the database holds it — including the columns Lead does not carry.</summary>
    public async Task<StoredLead> ReadAsync(string email)
    {
        await using var connection = await DataSource.OpenConnectionAsync();
        return await connection.QuerySingleAsync<StoredLead>(
            """
            select email as Email, name as Name, interest as Interest, discount_code as DiscountCode,
                   privacy_accepted as PrivacyAccepted, marketing_consent as MarketingConsent,
                   source as Source, updated_at as UpdatedAt
              from leads
             where email = lower(@email)
            """,
            new { email });
    }

    public async Task SetMarketingConsentAsync(string email, bool consent)
    {
        await using var connection = await DataSource.OpenConnectionAsync();
        await connection.ExecuteAsync(
            "update leads set marketing_consent = @consent where email = lower(@email)",
            new { email, consent });
    }

    public async Task<int> CountAsync()
    {
        await using var connection = await DataSource.OpenConnectionAsync();
        return await connection.ExecuteScalarAsync<int>("select count(*)::int from leads");
    }

    // The migrations live next to openmercato.toml, the same marker the app uses at startup.
    private static string MigrationsDirectory() =>
        Path.Combine(DotEnv.FindRepositoryRoot(AppContext.BaseDirectory), "db", "migrations");
}

public sealed record StoredLead(
    string Email,
    string? Name,
    string Interest,
    string DiscountCode,
    bool PrivacyAccepted,
    bool MarketingConsent,
    string? Source,
    DateTime UpdatedAt);

[CollectionDefinition(Name)]
public sealed class PostgresCollection : ICollectionFixture<PostgresFixture>
{
    public const string Name = "postgres";
}
