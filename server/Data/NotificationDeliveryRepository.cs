using Dapper;
using Landing.Models;
using Npgsql;

namespace Landing.Data;

public interface INotificationDeliveryStore
{
    Task<NotificationDelivery?> LeaseNextAsync(TimeSpan leaseDuration, CancellationToken cancellationToken = default);
    Task MarkDeliveredAsync(NotificationDelivery delivery, CancellationToken cancellationToken = default);
    Task MarkFailedAsync(
        NotificationDelivery delivery,
        string error,
        bool terminal,
        DateTimeOffset? nextAttemptAt,
        CancellationToken cancellationToken = default);
    Task DeferAsync(
        NotificationDelivery delivery,
        string reason,
        DateTimeOffset nextAttemptAt,
        CancellationToken cancellationToken = default);
}

public sealed class NotificationDeliveryRepository(NpgsqlDataSource dataSource) : INotificationDeliveryStore
{
    private const string ReturningColumns = """
        delivery.id as Id,
        delivery.lead_id as LeadId,
        delivery.kind as Kind,
        delivery.lead_email as LeadEmail,
        delivery.lead_name as LeadName,
        delivery.interest as Interest,
        delivery.discount_code as DiscountCode,
        delivery.discount_percent as DiscountPercent,
        delivery.lead_created_at as LeadCreatedAt,
        delivery.leads_inbox as LeadsInbox,
        delivery.attempt_count as AttemptCount,
        delivery.lease_token as LeaseToken
        """;

    public async Task<NotificationDelivery?> LeaseNextAsync(
        TimeSpan leaseDuration,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        var leaseToken = Guid.NewGuid();
        var command = new CommandDefinition(
            $"""
            with candidate as (
              select id
                from lead_notification_deliveries
               where status = 'pending'
                 and next_attempt_at <= now()
                 and (locked_until is null or locked_until <= now())
               order by next_attempt_at, id
               for update skip locked
               limit 1
            )
            update lead_notification_deliveries as delivery
               set locked_until = now() + (@leaseSeconds * interval '1 second'),
                   lease_token = @leaseToken,
                   updated_at = now()
              from candidate
             where delivery.id = candidate.id
            returning {ReturningColumns}
            """,
            new { leaseSeconds = leaseDuration.TotalSeconds, leaseToken },
            cancellationToken: cancellationToken);

        return await connection.QuerySingleOrDefaultAsync<NotificationDelivery>(command);
    }

    public async Task MarkDeliveredAsync(
        NotificationDelivery delivery,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        var command = new CommandDefinition(
            """
            update lead_notification_deliveries
               set status = 'delivered',
                   attempt_count = attempt_count + 1,
                   delivered_at = now(),
                   locked_until = null,
                   lease_token = null,
                   last_error = null,
                   updated_at = now()
             where id = @id and status = 'pending' and lease_token = @leaseToken
            """,
            new { id = delivery.Id, delivery.LeaseToken },
            cancellationToken: cancellationToken);
        await connection.ExecuteAsync(command);
    }

    public async Task MarkFailedAsync(
        NotificationDelivery delivery,
        string error,
        bool terminal,
        DateTimeOffset? nextAttemptAt,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        var command = new CommandDefinition(
            """
            update lead_notification_deliveries
               set status = case when @terminal then 'failed' else 'pending' end,
                   attempt_count = attempt_count + 1,
                   next_attempt_at = coalesce(@nextAttemptAt, next_attempt_at),
                   locked_until = null,
                   lease_token = null,
                   last_error = @error,
                   updated_at = now()
             where id = @id and status = 'pending' and lease_token = @leaseToken
            """,
            new
            {
                id = delivery.Id,
                delivery.LeaseToken,
                terminal,
                nextAttemptAt,
                error = Truncate(error),
            },
            cancellationToken: cancellationToken);
        await connection.ExecuteAsync(command);
    }

    public async Task DeferAsync(
        NotificationDelivery delivery,
        string reason,
        DateTimeOffset nextAttemptAt,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        var command = new CommandDefinition(
            """
            update lead_notification_deliveries
               set next_attempt_at = @nextAttemptAt,
                   locked_until = null,
                   lease_token = null,
                   last_error = @reason,
                   updated_at = now()
             where id = @id and status = 'pending' and lease_token = @leaseToken
            """,
            new { id = delivery.Id, delivery.LeaseToken, nextAttemptAt, reason = Truncate(reason) },
            cancellationToken: cancellationToken);
        await connection.ExecuteAsync(command);
    }

    public async Task<NotificationDeliveryStatus> GetStatusAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        var command = new CommandDefinition(
            """
            select count(*) filter (where status = 'pending')::int as Pending,
                   count(*) filter (where status = 'failed')::int as Failed,
                   min(created_at) filter (where status = 'pending') as OldestPendingAt
              from lead_notification_deliveries
            """,
            cancellationToken: cancellationToken);
        return await connection.QuerySingleAsync<NotificationDeliveryStatus>(command);
    }

    private static string Truncate(string value) => value.Length <= 1_000 ? value : value[..1_000];
}
