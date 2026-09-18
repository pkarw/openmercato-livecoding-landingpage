using System.Net.Http.Headers;
using System.Net.Http.Json;

namespace Landing.Notifications;

public sealed record EmailMessage(string To, string Subject, string Html, string? ReplyTo = null);

/// <summary>
/// Thin Resend HTTP client. Email is a side effect of claiming a code, never a reason to
/// fail the request, so every failure is logged and swallowed.
/// </summary>
public sealed class ResendEmailSender(HttpClient http, EmailSettings settings, ILogger<ResendEmailSender> logger)
{
    public bool Enabled => settings.IsConfigured;

    public async Task<bool> SendAsync(EmailMessage message, CancellationToken cancellationToken = default)
    {
        if (!settings.IsConfigured)
        {
            logger.LogInformation("RESEND_API_KEY is not set — skipping email to {To}", message.To);
            return false;
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

            using var response = await http.SendAsync(request, cancellationToken);
            if (response.IsSuccessStatusCode) return true;

            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            logger.LogWarning("Resend rejected the message to {To}: {Status} {Body}", message.To, (int)response.StatusCode, body);
            return false;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Could not send the email to {To}", message.To);
            return false;
        }
    }
}
