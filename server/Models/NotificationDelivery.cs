namespace Landing.Models;

public static class NotificationKinds
{
    public const string Lead = "lead";
    public const string Inbox = "inbox";
}

public sealed record NotificationDelivery(
    long Id,
    int LeadId,
    string Kind,
    string LeadEmail,
    string? LeadName,
    string Interest,
    string DiscountCode,
    int DiscountPercent,
    DateTime LeadCreatedAt,
    string LeadsInbox,
    int AttemptCount,
    Guid LeaseToken);

public sealed record NotificationDeliveryStatus(int Pending, int Failed, DateTime? OldestPendingAt);
