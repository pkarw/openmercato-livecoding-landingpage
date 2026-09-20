using Dapper;
using Landing.Configuration;
using Landing.Models;
using Npgsql;

namespace Landing.Tests;

[Collection(PostgresCollection.Name)]
public sealed class DiscountCodeUniquenessTests(PostgresFixture postgres) : IAsyncLifetime
{
    public Task InitializeAsync() => postgres.ResetAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task CollisionRegeneratesWithoutChangingTheFirstReservation()
    {
        await ClaimAsync("first@example.test", () => "OMC10-SHARED");
        var candidates = new Queue<string>(["OMC10-SHARED", "OMC10-SECOND"]);

        var (second, alreadyClaimed) = await ClaimAsync("second@example.test", candidates.Dequeue);

        Assert.False(alreadyClaimed);
        Assert.Equal("OMC10-SECOND", second.DiscountCode);
        Assert.Equal("OMC10-SHARED", (await postgres.Leads.FindByEmailAsync("first@example.test"))!.DiscountCode);
        Assert.Equal(2, await postgres.CountAsync());
        Assert.Equal(4, (await postgres.ReadDeliveriesAsync()).Count);
    }

    [Fact]
    public async Task ExhaustedCollisionsFailWithoutCreatingAnAmbiguousAssignment()
    {
        await ClaimAsync("first@example.test", () => "OMC10-SHARED");

        var error = await Assert.ThrowsAsync<InvalidOperationException>(
            () => ClaimAsync("second@example.test", () => "OMC10-SHARED"));

        Assert.Contains("after 5 attempts", error.Message);
        Assert.Equal(1, await postgres.CountAsync());
        Assert.Null(await postgres.Leads.FindByEmailAsync("second@example.test"));
        Assert.Equal(2, (await postgres.ReadDeliveriesAsync()).Count);
    }

    [Fact]
    public async Task RepeatClaimKeepsTheOriginalReservation()
    {
        var (first, _) = await ClaimAsync("lead@example.test", () => "OMC10-FIRST");

        var (repeat, alreadyClaimed) = await ClaimAsync("LEAD@example.test", () => "OMC10-OTHER");

        Assert.True(alreadyClaimed);
        Assert.Equal(first.Id, repeat.Id);
        Assert.Equal("OMC10-FIRST", repeat.DiscountCode);
        Assert.Equal(1, await postgres.CountAsync());
        Assert.Equal(2, (await postgres.ReadDeliveriesAsync()).Count);
    }

    [Fact]
    public async Task ConcurrentClaimsNeverCommitTheSharedCandidateTwice()
    {
        var claims = Enumerable.Range(0, 8).Select(index =>
        {
            var calls = 0;
            return ClaimAsync(
                $"lead-{index}@example.test",
                () => Interlocked.Increment(ref calls) == 1
                    ? "OMC10-SHARED"
                    : $"OMC10-{index:D6}");
        });

        var results = await Task.WhenAll(claims);

        Assert.All(results, result => Assert.False(result.AlreadyClaimed));
        Assert.Equal(8, results.Select(result => result.Lead.DiscountCode).Distinct().Count());
        Assert.Single(results, result => result.Lead.DiscountCode == "OMC10-SHARED");
        Assert.Equal(8, await postgres.CountAsync());
        Assert.Equal(16, (await postgres.ReadDeliveriesAsync()).Count);
    }

    [Fact]
    public async Task MigrationRejectsLegacyDuplicatesInsteadOfRewritingIssuedCodes()
    {
        await using var connection = await postgres.DataSource.OpenConnectionAsync();
        await using var transaction = await connection.BeginTransactionAsync();
        await connection.ExecuteAsync(
            "alter table leads drop constraint leads_discount_code_key",
            transaction: transaction);
        await connection.ExecuteAsync(
            """
            insert into leads (email, interest, discount_code, privacy_accepted, marketing_consent)
            values
              ('first@example.test', 'openmercato', 'OMC10-DUPLICATE', true, true),
              ('second@example.test', 'openmercato', 'OMC10-DUPLICATE', true, true)
            """,
            transaction: transaction);

        var root = DotEnv.FindRepositoryRoot(AppContext.BaseDirectory);
        var migration = await File.ReadAllTextAsync(
            Path.Combine(root, "db", "migrations", "20260919111000_unique_discount_codes.sql"));

        var error = await Assert.ThrowsAsync<PostgresException>(
            () => connection.ExecuteAsync(migration, transaction: transaction));

        Assert.Contains("duplicate issued codes exist", error.MessageText);
        await transaction.RollbackAsync();
    }

    private Task<(Lead Lead, bool AlreadyClaimed)> ClaimAsync(string email, Func<string> codeFactory) =>
        postgres.Leads.ClaimAsync(
            email,
            name: "Lead",
            interest: Interests.OpenMercato,
            discountCodeFactory: codeFactory,
            discountPercent: 10,
            leadsInbox: "sales@example.test",
            marketingConsent: true,
            source: "test");
}
