using Dapper;
using Npgsql;
using Landing.Models;

namespace Landing.Data;

public sealed class LeadRepository(NpgsqlDataSource dataSource)
{
    private const string Columns =
        "id as Id, email as Email, name as Name, interest as Interest, discount_code as DiscountCode, created_at as CreatedAt";

    /// <summary>
    /// Nothing here is worth more than a few seconds of a visitor's wait, and the URL-configured
    /// pool only holds five connections — so a query that overruns this is abandoned rather than
    /// left to occupy a slot until Npgsql's 30 s default expires.
    /// </summary>
    private const int CommandTimeoutSeconds = 10;

    /// <summary>
    /// Dapper's (sql, param) overloads build a command with <c>CancellationToken.None</c>, which
    /// leaves a query running on the server after the caller has gone. Every statement below goes
    /// through this instead, so the request's token reaches the command itself.
    /// </summary>
    private static CommandDefinition Command(string sql, object? parameters, CancellationToken cancellationToken) =>
        new(sql, parameters, commandTimeout: CommandTimeoutSeconds, cancellationToken: cancellationToken);

    public async Task<Lead?> FindByEmailAsync(string email, CancellationToken cancellationToken = default)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        return await connection.QuerySingleOrDefaultAsync<Lead>(Command(
            $"select {Columns} from leads where email = lower(@email)",
            new { email },
            cancellationToken));
    }

    /// <summary>
    /// Claiming twice with the same address keeps the original code instead of minting a new one,
    /// so a double submit (or a lost email) is harmless.
    /// </summary>
    public async Task<(Lead Lead, bool AlreadyClaimed)> ClaimAsync(
        string email,
        string? name,
        string interest,
        string discountCode,
        bool marketingConsent,
        string? source,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);

        var lead = await connection.QuerySingleOrDefaultAsync<Lead>(Command(
            $"""
            insert into leads (email, name, interest, discount_code, privacy_accepted, marketing_consent, source)
            values (lower(@email), @name, @interest, @discountCode, true, @marketingConsent, @source)
            on conflict (email) do nothing
            returning {Columns}
            """,
            new { email, name, interest, discountCode, marketingConsent, source },
            cancellationToken));

        if (lead is not null) return (lead, false);

        // Someone already claimed with this address — hand back the code they were given, and
        // record the latest interest so a second visit can widen the selection.
        var existing = await connection.QuerySingleAsync<Lead>(Command(
            $"""
            update leads
               set interest = case when interest = @interest then interest else 'both' end,
                   marketing_consent = marketing_consent or @marketingConsent,
                   updated_at = now()
             where email = lower(@email)
            returning {Columns}
            """,
            new { email, interest, marketingConsent },
            cancellationToken));

        return (existing, true);
    }

    public async Task<int> CountAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        return await connection.ExecuteScalarAsync<int>(Command(
            "select count(*)::int from leads", null, cancellationToken));
    }

    public async Task PingAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await connection.ExecuteScalarAsync<int>(Command("select 1", null, cancellationToken));
    }
}
