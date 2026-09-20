using System.Net;
using Landing.Notifications;
using Microsoft.Extensions.Logging.Abstractions;

namespace Landing.Tests;

public sealed class ResendEmailSenderTests
{
    [Fact]
    public async Task RetryableProviderFailureKeepsTheCallerSuppliedIdempotencyKey()
    {
        var handler = new RecordingHandler(HttpStatusCode.ServiceUnavailable, "temporarily unavailable");
        var sender = new ResendEmailSender(
            new HttpClient(handler),
            new EmailSettings("test-key", "sender@example.test", "sales@example.test"),
            NullLogger<ResendEmailSender>.Instance);

        var result = await sender.SendAsync(
            new EmailMessage("lead@example.test", "subject", "<p>body</p>"),
            "lead-notification/42");

        Assert.Equal(EmailSendDisposition.RetryableFailure, result.Disposition);
        Assert.Equal("lead-notification/42", handler.IdempotencyKey);
    }

    [Fact]
    public async Task InvalidRequestIsPermanent()
    {
        var sender = new ResendEmailSender(
            new HttpClient(new RecordingHandler(HttpStatusCode.BadRequest, "invalid")),
            new EmailSettings("test-key", "sender@example.test", "sales@example.test"),
            NullLogger<ResendEmailSender>.Instance);

        var result = await sender.SendAsync(
            new EmailMessage("lead@example.test", "subject", "<p>body</p>"),
            "lead-notification/42");

        Assert.Equal(EmailSendDisposition.PermanentFailure, result.Disposition);
    }

    private sealed class RecordingHandler(HttpStatusCode status, string body) : HttpMessageHandler
    {
        public string? IdempotencyKey { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            IdempotencyKey = request.Headers.GetValues("Idempotency-Key").Single();
            return Task.FromResult(new HttpResponseMessage(status) { Content = new StringContent(body) });
        }
    }
}
