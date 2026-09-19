using System.Net;
using Landing.Models;

namespace Landing.Notifications;

/// <summary>Emails triggered by a claim: the code for the lead, a heads-up for the inbox.</summary>
public sealed class LeadNotifier(ResendEmailSender sender, EmailSettings settings)
{
    private const string OpenMercatoUrl = "https://openmercatocloud.com/";
    private const string AiTechLeadersUrl = "https://aitechleaders.pl/";

    public async Task NotifyAsync(Lead lead, bool alreadyClaimed, CancellationToken cancellationToken = default)
    {
        var toLead = sender.SendAsync(
            new EmailMessage(
                To: lead.Email,
                Subject: $"Your {lead.DiscountPercent}% discount is reserved",
                Html: LeadHtml(lead),
                ReplyTo: settings.LeadsInbox),
            cancellationToken);

        // A repeat claim keeps the original code, so the inbox already heard about this address.
        var toInbox = alreadyClaimed
            ? Task.FromResult(false)
            : sender.SendAsync(
                new EmailMessage(
                    To: settings.LeadsInbox,
                    Subject: $"New lead: {lead.Email} — {Label(lead.Interest)}",
                    Html: InboxHtml(lead),
                    ReplyTo: lead.Email),
                cancellationToken);

        await Task.WhenAll(toLead, toInbox);
    }

    private string LeadHtml(Lead lead)
    {
        var greeting = string.IsNullOrWhiteSpace(lead.Name) ? "Hi," : $"Hi {Encode(lead.Name)},";
        var links = lead.Interest switch
        {
            Interests.OpenMercato => $"<a href=\"{OpenMercatoUrl}\">openmercatocloud.com</a>",
            Interests.AiTechLeaders => $"<a href=\"{AiTechLeadersUrl}\">aitechleaders.pl</a>",
            _ => $"<a href=\"{OpenMercatoUrl}\">openmercatocloud.com</a> and <a href=\"{AiTechLeadersUrl}\">aitechleaders.pl</a>",
        };

        return $"""
            <div style="font-family:Inter,Arial,sans-serif;background:#141313;color:#fff;padding:32px;border-radius:16px">
              <p>{greeting}</p>
              <p>
                Thank you — your <strong style="color:#e5f520">{lead.DiscountPercent}% discount</strong> for
                {Label(lead.Interest)} is reserved.
              </p>
              <p>
                We will email you the personal discount code shortly. Redeem it when you sign up on {links} —
                the code is tied to this address, so sign up with it.
              </p>
              <p style="color:#a3a09c;font-size:12px">
                You are receiving this because you requested the discount on our landing page and agreed to
                marketing communications about OpenMercatoCloud.com and AiTechLeaders.pl.
                Reply to this email to opt out at any time.
              </p>
            </div>
            """;
    }

    private string InboxHtml(Lead lead) =>
        $"""
        <div style="font-family:Inter,Arial,sans-serif">
          <h2>New discount lead</h2>
          <table cellpadding="6" style="border-collapse:collapse">
            <tr><td><strong>Email</strong></td><td>{Encode(lead.Email)}</td></tr>
            <tr><td><strong>Name</strong></td><td>{Encode(lead.Name ?? "—")}</td></tr>
            <tr><td><strong>Interested in</strong></td><td>{Label(lead.Interest)}</td></tr>
            <tr><td><strong>Reserved discount</strong></td><td>{lead.DiscountPercent}%</td></tr>
            <tr><td><strong>Code</strong></td><td>{Encode(lead.DiscountCode)}</td></tr>
            <tr><td><strong>Claimed at</strong></td><td>{lead.CreatedAt:u}</td></tr>
            <tr><td><strong>Consents</strong></td><td>privacy policy + marketing communications</td></tr>
          </table>
        </div>
        """;

    private static string Label(string interest) => interest switch
    {
        Interests.OpenMercato => "Open Mercato Cloud",
        Interests.AiTechLeaders => "the AI Tech Leaders training",
        _ => "Open Mercato Cloud and the AI Tech Leaders training",
    };

    private static string Encode(string value) => WebUtility.HtmlEncode(value);
}
