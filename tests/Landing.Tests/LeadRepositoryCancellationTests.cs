using System.Diagnostics;
using Dapper;
using Landing.Configuration;
using Landing.Models;
using Npgsql;

namespace Landing.Tests;

/// <summary>
/// A cancelled request must stop costing the database. Each test blocks the leads table with an
/// ACCESS EXCLUSIVE lock, so a repository call cannot finish until the lock goes — the only way
/// out within the timeout is the token actually reaching the command.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class LeadRepositoryCancellationTests(PostgresFixture postgres) : IAsyncLifetime
{
    // Generous on purpose: the assertion is "seconds, not until the lock clears", and a tight
    // bound would only buy flakiness on a loaded CI runner.
    private static readonly TimeSpan CancellationBudget = TimeSpan.FromSeconds(5);

    // Longer than the budget, so a call that ignores its token fails the test instead of passing
    // late; short enough that a failing run does not stall the suite.
    private static readonly TimeSpan LockHold = TimeSpan.FromSeconds(30);

    public Task InitializeAsync() => postgres.ResetAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task CancellingACountStopsItInsteadOfWaitingForTheLock()
    {
        await using var theLock = await TableLock.TakeAsync(postgres.DataSource);
        using var cts = new CancellationTokenSource();

        var count = postgres.Leads.CountAsync(cts.Token);
        await WaitUntilBlockedAsync(count);
        cts.Cancel();

        await AssertCancelsWithinBudgetAsync(count);
    }

    [Fact]
    public async Task CancellingAClaimStopsItInsteadOfWaitingForTheLock()
    {
        await using var theLock = await TableLock.TakeAsync(postgres.DataSource);
        using var cts = new CancellationTokenSource();

        var claim = postgres.Leads.ClaimAsync(
            "cancelled@example.test", "Cancelled", Interests.OpenMercato, "OMC10-AAAAAA",
            marketingConsent: true, source: "test", cts.Token);
        await WaitUntilBlockedAsync(claim);
        cts.Cancel();

        await AssertCancelsWithinBudgetAsync(claim);
    }

    /// <summary>
    /// The reason this matters: ConnectionUrls caps the URL-configured pool at five connections,
    /// so five abandoned queries are enough to lock the whole process out of the database.
    /// </summary>
    [Fact]
    public async Task CancelledQueriesGiveTheirPooledConnectionsBack()
    {
        const int MaxPoolSize = 5;

        await using var theLock = await TableLock.TakeAsync(postgres.DataSource);
        using var cts = new CancellationTokenSource();

        var blocked = Enumerable.Range(0, MaxPoolSize)
            .Select(_ => postgres.Leads.CountAsync(cts.Token))
            .ToArray();
        foreach (var query in blocked) await WaitUntilBlockedAsync(query);

        cts.Cancel();
        foreach (var query in blocked) await AssertCancelsWithinBudgetAsync(query);

        // Still under the table lock: this only succeeds if every slot came back to the pool.
        using var reuse = new CancellationTokenSource(CancellationBudget);
        await using var connection = await postgres.DataSource.OpenConnectionAsync(reuse.Token);
        Assert.Equal(1, await connection.ExecuteScalarAsync<int>("select 1"));
    }

    /// <summary>Guards the other direction: the uncancelled paths still behave as before.</summary>
    [Fact]
    public async Task AnUncancelledClaimAndItsRepeatStillWork()
    {
        var (first, alreadyClaimed) = await postgres.Leads.ClaimAsync(
            "steady@example.test", "Steady", Interests.AiTechLeaders, "ATL10-BBBBBB",
            marketingConsent: true, source: "test", CancellationToken.None);

        Assert.False(alreadyClaimed);
        Assert.Equal("steady@example.test", first.Email);
        Assert.Equal(1, await postgres.Leads.CountAsync(CancellationToken.None));

        var (repeat, repeatAlreadyClaimed) = await postgres.Leads.ClaimAsync(
            "steady@example.test", "Steady", Interests.AiTechLeaders, "ATL10-CCCCCC",
            marketingConsent: true, source: "test", CancellationToken.None);

        Assert.True(repeatAlreadyClaimed);
        Assert.Equal(first.Id, repeat.Id);
        Assert.Equal(first.DiscountCode, repeat.DiscountCode);
        Assert.Equal(1, await postgres.Leads.CountAsync(CancellationToken.None));

        var found = await postgres.Leads.FindByEmailAsync("steady@example.test", CancellationToken.None);
        Assert.Equal(first.DiscountCode, found?.DiscountCode);

        await postgres.Leads.PingAsync(CancellationToken.None);
    }

    private static async Task AssertCancelsWithinBudgetAsync(Task call)
    {
        var started = Stopwatch.StartNew();
        var finished = await Task.WhenAny(call, Task.Delay(CancellationBudget));

        Assert.True(
            finished == call,
            $"The call was still running {started.Elapsed.TotalSeconds:F1}s after cancellation — " +
            "its token never reached the database command.");

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => call);
    }

    /// <summary>
    /// Gives the statement time to reach the server and queue behind the lock. Cancelling before
    /// it gets there would be answered by the token check inside OpenConnectionAsync, which is the
    /// one path that already worked — and would prove nothing.
    /// </summary>
    private static async Task WaitUntilBlockedAsync(Task call)
    {
        var waited = await Task.WhenAny(call, Task.Delay(TimeSpan.FromMilliseconds(500)));
        Assert.True(waited != call, "The call completed even though the leads table was locked.");
    }

    /// <summary>An ACCESS EXCLUSIVE lock on leads, held on its own connection until disposed.</summary>
    private sealed class TableLock : IAsyncDisposable
    {
        private readonly NpgsqlConnection connection;
        private readonly NpgsqlTransaction transaction;

        private TableLock(NpgsqlConnection connection, NpgsqlTransaction transaction)
        {
            this.connection = connection;
            this.transaction = transaction;
        }

        public static async Task<TableLock> TakeAsync(NpgsqlDataSource dataSource)
        {
            // Outside the pooled data source: the pool-exhaustion test needs every one of the
            // five slots available to the calls under test. The data source only tells us which
            // throwaway database the fixture created — it redacts the password — so the
            // credentials come from the same variable the fixture itself was built from.
            var connectionString = new NpgsqlConnectionStringBuilder(
                ConnectionUrls.ToNpgsqlConnectionString(
                    Environment.GetEnvironmentVariable("LANDING_TEST_DATABASE_URL")!))
            {
                Database = new NpgsqlConnectionStringBuilder(dataSource.ConnectionString).Database,
            }.ConnectionString;

            var connection = new NpgsqlConnection(connectionString);
            await connection.OpenAsync();
            var transaction = await connection.BeginTransactionAsync();

            await connection.ExecuteAsync(new CommandDefinition(
                "lock table leads in access exclusive mode",
                transaction: transaction,
                commandTimeout: (int)LockHold.TotalSeconds));

            return new TableLock(connection, transaction);
        }

        public async ValueTask DisposeAsync()
        {
            await transaction.RollbackAsync();
            await transaction.DisposeAsync();
            await connection.DisposeAsync();
        }
    }
}
