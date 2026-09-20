using Landing.Models;
using Landing.Notifications;

namespace Landing.Tests;

public sealed class LeadNotifierTests
{
    [Theory]
    [InlineData(10)]
    [InlineData(15)]
    public void ConfirmationUsesTheStoredDiscount(int discountPercent)
    {
        var notifier = new LeadNotifier();

        var message = notifier.CreateMessage(Delivery(NotificationKinds.Lead, discountPercent));

        Assert.Equal($"Your {discountPercent}% discount is reserved", message.Subject);
        Assert.Contains($">{discountPercent}% discount</strong>", message.Html, StringComparison.Ordinal);
    }

    [Fact]
    public void NewLeadNotificationShowsTheReservedDiscountForFulfillment()
    {
        var notifier = new LeadNotifier();

        var inbox = notifier.CreateMessage(Delivery(NotificationKinds.Inbox, 15));

        Assert.Equal("sales@example.test", inbox.To);
        Assert.Contains("<strong>Reserved discount</strong></td><td>15%</td>", inbox.Html, StringComparison.Ordinal);
    }

    private static NotificationDelivery Delivery(string kind, int discountPercent) => new(
        Id: 1,
        LeadId: 1,
        Kind: kind,
        LeadEmail: "lead@example.test",
        LeadName: "Lead",
        Interest: Interests.OpenMercato,
        DiscountCode: $"OMC{discountPercent}-AAAAAA",
        DiscountPercent: discountPercent,
        LeadCreatedAt: DateTime.UtcNow,
        LeadsInbox: "sales@example.test",
        AttemptCount: 0,
        LeaseToken: Guid.NewGuid());
}
