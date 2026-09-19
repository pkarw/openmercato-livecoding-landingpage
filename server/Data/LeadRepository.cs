using Dapper;
using Npgsql;
using Landing.Models;

namespace Landing.Data;

public sealed class LeadRepository(NpgsqlDataSource dataSource)
{
    private const string Columns =
        "id as Id, email as Email, name as Name, interest as Interest, discount_code as DiscountCode, " +
        "discount_percent as DiscountPercent, created_at as CreatedAt";

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
        int discountPercent,
        bool marketingConsent,
        string? source,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);

        var lead = await connection.QuerySingleOrDefaultAsync<Lead>(
            $"""
            insert into leads (
                email, name, interest, discount_code, discount_percent,
                privacy_accepted, marketing_consent, source)
            values (
                lower(@email), @name, @interest, @discountCode, @discountPercent,
                true, @marketingConsent, @source)
            on conflict (email) do nothing
            returning {Columns}
            """,
            new { email, name, interest, discountCode, discountPercent, marketingConsent, source });

        if (lead is not null) return (lead, false);

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
