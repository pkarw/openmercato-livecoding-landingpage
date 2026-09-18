namespace Landing.Notifications;

/// <summary>
/// Resend configuration. <see cref="From"/> must be an address on a domain verified in Resend
/// (ADMIN_EMAIL); <see cref="LeadsInbox"/> is where the internal lead notifications land.
/// </summary>
public sealed record EmailSettings(string? ApiKey, string From, string LeadsInbox)
{
    public bool IsConfigured => !string.IsNullOrWhiteSpace(ApiKey);

    public static EmailSettings FromEnvironment() => new(
        ApiKey: Environment.GetEnvironmentVariable("RESEND_API_KEY"),
        From: Environment.GetEnvironmentVariable("ADMIN_EMAIL") ?? "info@updates.openmercato.com",
        LeadsInbox: Environment.GetEnvironmentVariable("LEADS_INBOX") ?? "info@openmercato.com");
}
