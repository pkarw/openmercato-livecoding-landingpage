using Dapper;
using Npgsql;
using Landing.Models;

namespace Landing.Data;

public sealed class LeadRepository(NpgsqlDataSource dataSource)
{
    private const int DiscountCodeAttempts = 5;
    private const string DiscountCodeConstraint = "leads_discount_code_key";

    private const string Columns =
        "id as Id, email as Email, name as Name, interest as Interest, discount_code as DiscountCode, created_at as CreatedAt";

    private const string SelectByEmailSql = $"select {Columns} from leads where email = lower(@email)";

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
            SelectByEmailSql,
            new { email },
            cancellationToken));
    }

    /// <summary>
    /// Claiming twice with the same address keeps the original code instead of minting a new one,
    /// so a double submit (or a lost email) is harmless.
    /// </summary>
    /// <remarks>
    /// The claim endpoint is anonymous: knowing an address is not proof of owning it. A repeat claim
    /// therefore reads the existing reservation back and writes nothing — otherwise anybody could
    /// rewrite a stranger's stored interest or consent by posting their address (issue #3). Changing
    /// a stored preference needs an ownership proof this endpoint does not have.
    /// </remarks>
    public async Task<(Lead Lead, bool AlreadyClaimed)> ClaimAsync(
        string email,
        string? name,
        string interest,
        string discountCode,
        bool marketingConsent,
        string? source,
        CancellationToken cancellationToken = default) =>
        await ClaimAsync(
            email,
            name,
            interest,
            () => discountCode,
            marketingConsent,
            source,
            cancellationToken);

    /// <summary>
    /// Reserves the first candidate that is unique across all leads. Callers that can generate
    /// another candidate should use this overload so a random collision does not fail the claim.
    /// </summary>
    public async Task<(Lead Lead, bool AlreadyClaimed)> ClaimAsync(
        string email,
        string? name,
        string interest,
        Func<string> discountCodeFactory,
        bool marketingConsent,
        string? source,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(discountCodeFactory);
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);

        // Two attempts only matter in the race where the existing row is deleted between the
        // insert and the read; the second pass then reserves the address outright.
        for (var rowAttempt = 0; rowAttempt < 2; rowAttempt++)
        {
            for (var codeAttempt = 1; codeAttempt <= DiscountCodeAttempts; codeAttempt++)
            {
                var discountCode = discountCodeFactory();
                try
                {
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
                    break;
                }
                catch (PostgresException exception) when (
                    exception.SqlState == PostgresErrorCodes.UniqueViolation &&
                    exception.ConstraintName == DiscountCodeConstraint)
                {
                    if (codeAttempt == DiscountCodeAttempts)
                    {
                        throw new InvalidOperationException(
                            $"Could not reserve a unique discount code after {DiscountCodeAttempts} attempts.",
                            exception);
                    }
                }
            }

            var existing = await connection.QuerySingleOrDefaultAsync<Lead>(Command(
                SelectByEmailSql,
                new { email },
                cancellationToken));
            if (existing is not null) return (existing, true);
        }

        throw new InvalidOperationException(
            "Could not reserve or read the lead for this address — it was created and removed concurrently.");
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
