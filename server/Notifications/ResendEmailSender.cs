using System.Net.Http.Headers;
using System.Net.Http.Json;

namespace Landing.Notifications;

public sealed record EmailMessage(string To, string Subject, string Html, string? ReplyTo = null);

public enum EmailSendDisposition
{
    Delivered,
    RetryableFailure,
    PermanentFailure,
    Disabled,
}

public sealed record EmailSendResult(EmailSendDisposition Disposition, string? Error = null);

public interface IEmailSender
{
    Task<EmailSendResult> SendAsync(
        EmailMessage message,
        string idempotencyKey,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Thin Resend HTTP client. It classifies failures for the durable delivery worker and uses
/// Resend's idempotency key to make a retry after an ambiguous response safe.
/// </summary>
public sealed class ResendEmailSender(HttpClient http, EmailSettings settings, ILogger<ResendEmailSender> logger)
    : IEmailSender
{
    public bool Enabled => settings.IsConfigured;

    public async Task<EmailSendResult> SendAsync(
        EmailMessage message,
        string idempotencyKey,
        CancellationToken cancellationToken = default)
    {
        if (!settings.IsConfigured)
        {
            logger.LogInformation("RESEND_API_KEY is not set — skipping email to {To}", message.To);
            return new(EmailSendDisposition.Disabled, "RESEND_API_KEY is not configured");
        }

        var payload = new Dictionary<string, object?>
        {
            ["from"] = settings.From,
            ["to"] = new[] { message.To },
            ["subject"] = message.Subject,
            ["html"] = message.Html,
        };
        if (message.ReplyTo is not null) payload["reply_to"] = message.ReplyTo;

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, "https://api.resend.com/emails")
            {
                Content = JsonContent.Create(payload),
            };
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", settings.ApiKey);
            request.Headers.Add("Idempotency-Key", idempotencyKey);

            using var response = await http.SendAsync(request, cancellationToken);
            if (response.IsSuccessStatusCode) return new(EmailSendDisposition.Delivered);

            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            logger.LogWarning("Resend rejected the message to {To}: {Status} {Body}", message.To, (int)response.StatusCode, body);
            var status = (int)response.StatusCode;
            var disposition = status is 408 or 409 or 425 or 429 || status >= 500
                ? EmailSendDisposition.RetryableFailure
                : EmailSendDisposition.PermanentFailure;
            return new(disposition, $"Resend returned HTTP {status}: {Truncate(body)}");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Could not send the email to {To}", message.To);
            return new(EmailSendDisposition.RetryableFailure, $"{ex.GetType().Name}: {ex.Message}");
        }
    }

    private static string Truncate(string value) => value.Length <= 500 ? value : value[..500];
}
