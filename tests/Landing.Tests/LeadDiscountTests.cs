using Dapper;
using Landing.Configuration;
using Landing.Models;

namespace Landing.Tests;

[Collection(PostgresCollection.Name)]
public sealed class LeadDiscountTests(PostgresFixture postgres) : IAsyncLifetime
{
    private const string Email = "lead@example.test";

    public Task InitializeAsync() => postgres.ResetAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task NewClaimStoresTheActivePercentage()
    {
        var (lead, alreadyClaimed) = await ClaimAsync(Email, 15, "OMC15-AAAAAA");

        Assert.False(alreadyClaimed);
        Assert.Equal(15, lead.DiscountPercent);
        Assert.Equal(15, await postgres.ReadDiscountPercentAsync(Email));
    }

    [Fact]
    public async Task RepeatClaimKeepsTheStoredPercentageAndCode()
    {
        await ClaimAsync(Email, 10, "OMC10-AAAAAA");

        var (lead, alreadyClaimed) = await ClaimAsync(Email, 15, "OMC15-BBBBBB");

        Assert.True(alreadyClaimed);
        Assert.Equal(10, lead.DiscountPercent);
        Assert.Equal("OMC10-AAAAAA", lead.DiscountCode);
        Assert.Equal(10, await postgres.ReadDiscountPercentAsync(Email));
    }

    [Fact]
    public async Task ConcurrentClaimsAgreeOnTheWinningEntitlement()
    {
        var results = await Task.WhenAll(Enumerable.Range(0, 8).Select(i =>
            ClaimAsync(Email, i % 2 == 0 ? 10 : 15, $"OMC{i % 2 * 5 + 10}-{i:D6}")));

        Assert.Single(results, result => !result.AlreadyClaimed);
        Assert.Single(results.Select(result => result.Lead.DiscountCode).Distinct());
        Assert.Single(results.Select(result => result.Lead.DiscountPercent).Distinct());
        Assert.Equal(results[0].Lead.DiscountPercent, await postgres.ReadDiscountPercentAsync(Email));
    }

    [Fact]
    public async Task DatabaseDefaultProtectsAnInsertThatOmitsThePercentage()
    {
        await postgres.InsertWithoutDiscountAsync(Email, "OMC10-DEFAULT");

        Assert.Equal(10, await postgres.ReadDiscountPercentAsync(Email));
    }

    [Fact]
    public async Task MigrationBackfillsAnExistingLeadToTenPercent()
    {
        await using var connection = await postgres.DataSource.OpenConnectionAsync();
        await using var transaction = await connection.BeginTransactionAsync();

        await connection.ExecuteAsync("alter table leads drop column discount_percent", transaction: transaction);
        await connection.ExecuteAsync(
            """
            insert into leads (email, interest, discount_code, privacy_accepted, marketing_consent)
            values (lower(@email), 'openmercato', 'OMC10-LEGACY', true, true)
            """,
            new { email = Email }, transaction);

        var root = DotEnv.FindRepositoryRoot(AppContext.BaseDirectory);
        var migration = await File.ReadAllTextAsync(
            Path.Combine(root, "db", "migrations", "004_leads_discount_percent.sql"));
        await connection.ExecuteAsync(migration, transaction: transaction);

        var stored = await connection.QuerySingleAsync<int>(
            "select discount_percent from leads where email = lower(@email)",
            new { email = Email }, transaction);

        Assert.Equal(10, stored);
        await transaction.RollbackAsync();
    }

    private Task<(Lead Lead, bool AlreadyClaimed)> ClaimAsync(string email, int discountPercent, string code) =>
        postgres.Leads.ClaimAsync(
            email,
            name: "Lead",
            interest: Interests.OpenMercato,
            discountCode: code,
            discountPercent,
            marketingConsent: true,
            source: "test");
}
