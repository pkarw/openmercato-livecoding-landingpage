using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace Landing.Tests;

[Collection(PostgresWebApplicationCollection.Name)]
public sealed class LeadEndpointPrivacyTests(PostgresWebApplicationFixture application)
{
    [Fact]
    public async Task FirstAndRepeatClaimsReturnTheSameAnonymousAcknowledgement()
    {
        using var client = application.Factory.CreateClient();
        var email = $"privacy-{Guid.NewGuid():N}@example.invalid";

        using var first = await client.PostAsJsonAsync("/api/leads", new
        {
            email,
            name = "Stored Private Name",
            interest = "openmercato",
            privacyAccepted = true,
            marketingConsent = true,
            source = "privacy-regression-test",
        });

        using var repeat = await client.PostAsJsonAsync("/api/leads", new
        {
            email,
            name = "Attacker Supplied Name",
            interest = "aitechleaders",
            privacyAccepted = true,
            marketingConsent = true,
            source = "privacy-regression-test",
        });

        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal(HttpStatusCode.OK, repeat.StatusCode);
        Assert.Null(first.Headers.Location);
        Assert.Null(repeat.Headers.Location);

        using var firstJson = JsonDocument.Parse(await first.Content.ReadAsStringAsync());
        using var repeatJson = JsonDocument.Parse(await repeat.Content.ReadAsStringAsync());

        Assert.Equal(firstJson.RootElement.GetRawText(), repeatJson.RootElement.GetRawText());
        Assert.Equal(
            ["discountPercent", "endsAt"],
            firstJson.RootElement.EnumerateObject().Select(property => property.Name).Order().ToArray());

        var publicBody = firstJson.RootElement.GetRawText();
        Assert.DoesNotContain(email, publicBody, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Stored Private Name", publicBody, StringComparison.Ordinal);
        Assert.DoesNotContain("openmercato", publicBody, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("alreadyClaimed", publicBody, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("lead", publicBody, StringComparison.OrdinalIgnoreCase);
    }
}
