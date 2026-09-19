using System.Net;
using System.Text.Json;
using Landing.Models;
using Landing.Notifications;
using Microsoft.Extensions.Logging.Abstractions;

namespace Landing.Tests;

public sealed class LeadNotifierTests
{
    [Theory]
    [InlineData(10)]
    [InlineData(15)]
    public async Task ConfirmationUsesTheStoredDiscount(int discountPercent)
    {
        var handler = new CapturingHandler();
        var notifier = CreateNotifier(handler);

        await notifier.NotifyAsync(Lead(discountPercent), alreadyClaimed: true);

        var message = Assert.Single(handler.Messages);
        Assert.Equal($"Your {discountPercent}% discount is reserved", message.Subject);
        Assert.Contains($">{discountPercent}% discount</strong>", message.Html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task NewLeadNotificationShowsTheReservedDiscountForFulfillment()
    {
        var handler = new CapturingHandler();
        var notifier = CreateNotifier(handler);

        await notifier.NotifyAsync(Lead(15), alreadyClaimed: false);

        var inbox = Assert.Single(handler.Messages, message => message.To == "sales@example.test");
        Assert.Contains("<strong>Reserved discount</strong></td><td>15%</td>", inbox.Html, StringComparison.Ordinal);
    }

    private static LeadNotifier CreateNotifier(CapturingHandler handler)
    {
        var settings = new EmailSettings("test-key", "sender@example.test", "sales@example.test");
        var sender = new ResendEmailSender(
            new HttpClient(handler), settings, NullLogger<ResendEmailSender>.Instance);
        return new LeadNotifier(sender, settings);
    }

    private static Lead Lead(int discountPercent) => new(
        Id: 1,
        Email: "lead@example.test",
        Name: "Lead",
        Interest: Interests.OpenMercato,
        DiscountCode: $"OMC{discountPercent}-AAAAAA",
        DiscountPercent: discountPercent,
        CreatedAt: DateTime.UtcNow);

    private sealed class CapturingHandler : HttpMessageHandler
    {
        public List<CapturedMessage> Messages { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var json = await request.Content!.ReadAsStringAsync(cancellationToken);
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            Messages.Add(new CapturedMessage(
                root.GetProperty("to")[0].GetString()!,
                root.GetProperty("subject").GetString()!,
                root.GetProperty("html").GetString()!));
            return new HttpResponseMessage(HttpStatusCode.OK);
        }
    }

    private sealed record CapturedMessage(string To, string Subject, string Html);
}
