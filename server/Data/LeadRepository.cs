using Dapper;
using Npgsql;
using Landing.Models;

namespace Landing.Data;

public sealed class LeadRepository(NpgsqlDataSource dataSource)
{
    private const string Columns =
        "id as Id, email as Email, name as Name, interest as Interest, discount_code as DiscountCode, created_at as CreatedAt";

    private const string SelectByEmailSql = $"select {Columns} from leads where email = lower(@email)";

    public async Task<Lead?> FindByEmailAsync(string email, CancellationToken cancellationToken = default)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        return await connection.QuerySingleOrDefaultAsync<Lead>(SelectByEmailSql, new { email });
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
        CancellationToken cancellationToken = default)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);

        // Two attempts only matter in the race where the existing row is deleted between the
        // insert and the read; the second pass then reserves the address outright.
        for (var attempt = 0; attempt < 2; attempt++)
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

            var existing = await connection.QuerySingleOrDefaultAsync<Lead>(SelectByEmailSql, new { email });
            if (existing is not null) return (existing, true);
        }

        throw new InvalidOperationException(
            "Could not reserve or read the lead for this address — it was created and removed concurrently.");
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
