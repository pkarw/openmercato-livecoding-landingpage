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

    public async Task<Lead?> FindByEmailAsync(string email, CancellationToken cancellationToken = default)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        return await connection.QuerySingleOrDefaultAsync<Lead>(
            $"select {Columns} from leads where email = lower(@email)",
            new { email });
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

        for (var attempt = 1; attempt <= DiscountCodeAttempts; attempt++)
        {
            var discountCode = discountCodeFactory();
            try
            {
                var lead = await connection.QuerySingleOrDefaultAsync<Lead>(
                    $"""
                    insert into leads (email, name, interest, discount_code, privacy_accepted, marketing_consent, source)
                    values (lower(@email), @name, @interest, @discountCode, true, @marketingConsent, @source)
                    on conflict (email) do nothing
                    returning {Columns}
                    """,
                    new { email, name, interest, discountCode, marketingConsent, source });

                if (lead is not null) return (lead, false);
                break;
            }
            catch (PostgresException exception) when (
                exception.SqlState == PostgresErrorCodes.UniqueViolation &&
                exception.ConstraintName == DiscountCodeConstraint)
            {
                if (attempt == DiscountCodeAttempts)
                {
                    throw new InvalidOperationException(
                        $"Could not reserve a unique discount code after {DiscountCodeAttempts} attempts.",
                        exception);
                }
            }
        }

        // Someone already claimed with this address — hand back the code they were given, and
        // record the latest interest so a second visit can widen the selection.
        var existing = await connection.QuerySingleAsync<Lead>(
            $"""
            update leads
               set interest = case when interest = @interest then interest else 'both' end,
                   marketing_consent = marketing_consent or @marketingConsent,
                   updated_at = now()
             where email = lower(@email)
            returning {Columns}
            """,
            new { email, interest, marketingConsent });

        return (existing, true);
    }

    public async Task<int> CountAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        return await connection.ExecuteScalarAsync<int>("select count(*)::int from leads");
    }

    public async Task PingAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await connection.ExecuteScalarAsync<int>("select 1");
    }
}
