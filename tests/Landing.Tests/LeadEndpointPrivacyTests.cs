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

        var expectedProperties = new[] { "alreadyClaimed", "discountPercent", "endsAt", "lead" };
        Assert.Equal(expectedProperties, PropertyNames(firstJson.RootElement));
        Assert.Equal(expectedProperties, PropertyNames(repeatJson.RootElement));
        Assert.False(firstJson.RootElement.GetProperty("alreadyClaimed").GetBoolean());
        Assert.False(repeatJson.RootElement.GetProperty("alreadyClaimed").GetBoolean());

        var repeatLead = repeatJson.RootElement.GetProperty("lead");
        Assert.Equal(email, repeatLead.GetProperty("email").GetString());
        Assert.Equal("Attacker Supplied Name", repeatLead.GetProperty("name").GetString());
        Assert.Equal("aitechleaders", repeatLead.GetProperty("interest").GetString());
        Assert.True(DateTimeOffset.TryParse(repeatLead.GetProperty("createdAt").GetString(), out _));

        var repeatBody = repeatJson.RootElement.GetRawText();
        Assert.DoesNotContain("Stored Private Name", repeatBody, StringComparison.Ordinal);
        Assert.DoesNotContain("openmercato", repeatBody, StringComparison.OrdinalIgnoreCase);
    }

    private static string[] PropertyNames(JsonElement element) =>
        element.EnumerateObject().Select(property => property.Name).Order().ToArray();
}
