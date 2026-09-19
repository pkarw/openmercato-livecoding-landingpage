using Dapper;
using Npgsql;
using Landing.Models;

namespace Landing.Data;

public sealed class LeadRepository(NpgsqlDataSource dataSource)
{
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
        int discountPercent,
        string leadsInbox,
        bool marketingConsent,
        string? source,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        var lead = await connection.QuerySingleOrDefaultAsync<Lead>(
            $"""
            insert into leads (email, name, interest, discount_code, privacy_accepted, marketing_consent, source)
            values (lower(@email), @name, @interest, @discountCode, true, @marketingConsent, @source)
            on conflict (email) do nothing
            returning {Columns}
            """,
            new { email, name, interest, discountCode, marketingConsent, source },
            transaction);

        var alreadyClaimed = lead is null;

        if (lead is null)
        {
            // Someone already claimed with this address — hand back the code they were given, and
            // record the latest interest so a second visit can widen the selection.
            lead = await connection.QuerySingleAsync<Lead>(
                $"""
                update leads
                   set interest = case when interest = @interest then interest else 'both' end,
                       marketing_consent = marketing_consent or @marketingConsent,
                       updated_at = now()
                 where email = lower(@email)
                returning {Columns}
                """,
                new { email, interest, marketingConsent },
                transaction);
        }

        // The reservation and both independent delivery intents commit together. A duplicate claim
        // can recover a pre-outbox reservation, while the unique key keeps concurrent submissions
        // from creating duplicate work or replacing an existing payload snapshot.
        await connection.ExecuteAsync(
            """
            insert into lead_notification_deliveries (
              lead_id, kind, lead_email, lead_name, interest, discount_code,
              discount_percent, lead_created_at, leads_inbox)
            values
              (@leadId, 'lead', @leadEmail, @leadName, @leadInterest, @leadDiscountCode,
               @discountPercent, @leadCreatedAt, @leadsInbox),
              (@leadId, 'inbox', @leadEmail, @leadName, @leadInterest, @leadDiscountCode,
               @discountPercent, @leadCreatedAt, @leadsInbox)
            on conflict (lead_id, kind) do nothing
            """,
            new
            {
                leadId = lead.Id,
                leadEmail = lead.Email,
                leadName = lead.Name,
                leadInterest = lead.Interest,
                leadDiscountCode = lead.DiscountCode,
                discountPercent,
                leadCreatedAt = lead.CreatedAt,
                leadsInbox,
            },
            transaction);

        await transaction.CommitAsync(cancellationToken);

        return (lead, alreadyClaimed);
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
