using Landing.Models;

namespace Landing.Tests;

/// <summary>
/// POST /api/leads is anonymous, so ClaimAsync is reachable by anyone who knows — or guesses — an
/// address. These cover the negative paths from issue #3: a repeat claim must hand the existing
/// reservation back and write nothing at all.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class LeadRepositoryClaimTests(PostgresFixture postgres) : IAsyncLifetime
{
    private const string Victim = "lead@example.test";
    private const string Attacker = "attacker@example.test";

    public Task InitializeAsync() => postgres.ResetAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task FirstClaimStoresExactlyWhatWasSubmitted()
    {
        var (lead, alreadyClaimed) = await ClaimAsync(Victim, Interests.OpenMercato, name: "Lead", code: "OMC10-AAAAAA");

        Assert.False(alreadyClaimed);
        Assert.Equal(Victim, lead.Email);
        Assert.Equal(Interests.OpenMercato, lead.Interest);

        var stored = await postgres.ReadAsync(Victim);
        Assert.Equal(Interests.OpenMercato, stored.Interest);
        Assert.Equal("Lead", stored.Name);
        Assert.True(stored.PrivacyAccepted);
        Assert.True(stored.MarketingConsent);
    }

    [Fact]
    public async Task RepeatClaimWithADifferentInterestLeavesTheStoredRowUntouched()
    {
        await ClaimAsync(Victim, Interests.OpenMercato, name: "Lead", code: "OMC10-AAAAAA", source: "newsletter");
        var before = await postgres.ReadAsync(Victim);

        // A stranger posts the same address with a different choice and no proof of ownership.
        var (lead, alreadyClaimed) = await ClaimAsync(
            Victim, Interests.AiTechLeaders, name: "Someone Else", code: "ATL10-BBBBBB", source: "attack");

        Assert.True(alreadyClaimed);
        Assert.Equal(Interests.OpenMercato, lead.Interest);
        Assert.Equal("OMC10-AAAAAA", lead.DiscountCode);

        var after = await postgres.ReadAsync(Victim);
        Assert.Equal(before, after);
        Assert.Equal(1, await postgres.CountAsync());
    }

    [Fact]
    public async Task RepeatClaimCannotReEnableAStoredFalseMarketingConsent()
    {
        await ClaimAsync(Victim, Interests.OpenMercato, code: "OMC10-AAAAAA");
        await postgres.SetMarketingConsentAsync(Victim, consent: false);

        // The endpoint only forwards marketingConsent: true, so the old `or` could only flip
        // a withdrawn consent back on.
        await ClaimAsync(Victim, Interests.OpenMercato, code: "OMC10-CCCCCC", marketingConsent: true);

        Assert.False((await postgres.ReadAsync(Victim)).MarketingConsent);
    }

    [Fact]
    public async Task RepeatClaimUnderDifferentCasingIsStillNonMutating()
    {
        await ClaimAsync(Victim, Interests.OpenMercato, code: "OMC10-AAAAAA");
        var before = await postgres.ReadAsync(Victim);

        var (lead, alreadyClaimed) = await ClaimAsync(Victim.ToUpperInvariant(), Interests.Both, code: "DUO10-BBBBBB");

        Assert.True(alreadyClaimed);
        Assert.Equal(Interests.OpenMercato, lead.Interest);
        Assert.Equal(before, await postgres.ReadAsync(Victim));
        Assert.Equal(1, await postgres.CountAsync());
    }

    [Fact]
    public async Task IdenticalRetryReturnsTheOriginalReservation()
    {
        var (first, _) = await ClaimAsync(Victim, Interests.Both, name: "Lead", code: "DUO10-AAAAAA");
        var before = await postgres.ReadAsync(Victim);

        var (second, alreadyClaimed) = await ClaimAsync(Victim, Interests.Both, name: "Lead", code: "DUO10-BBBBBB");

        Assert.True(alreadyClaimed);
        Assert.Equal(first.Id, second.Id);
        Assert.Equal(first.DiscountCode, second.DiscountCode);
        Assert.Equal(before, await postgres.ReadAsync(Victim));
    }

    [Fact]
    public async Task AnotherAddressStillGetsItsOwnReservation()
    {
        await ClaimAsync(Victim, Interests.OpenMercato, code: "OMC10-AAAAAA");

        var (lead, alreadyClaimed) = await ClaimAsync(Attacker, Interests.AiTechLeaders, code: "ATL10-BBBBBB");

        Assert.False(alreadyClaimed);
        Assert.Equal(Interests.AiTechLeaders, lead.Interest);
        Assert.Equal(2, await postgres.CountAsync());
        Assert.Equal(Interests.OpenMercato, (await postgres.ReadAsync(Victim)).Interest);
    }

    [Fact]
    public async Task ConcurrentRepeatClaimsMutateNothing()
    {
        await ClaimAsync(Victim, Interests.OpenMercato, name: "Lead", code: "OMC10-AAAAAA");
        var before = await postgres.ReadAsync(Victim);

        var results = await Task.WhenAll(Enumerable.Range(0, 8).Select(i =>
            ClaimAsync(Victim, Interests.AiTechLeaders, name: $"Attacker {i}", code: $"ATL10-{i:D6}")));

        Assert.All(results, result => Assert.True(result.AlreadyClaimed));
        Assert.All(results, result => Assert.Equal("OMC10-AAAAAA", result.Lead.DiscountCode));
        Assert.Equal(before, await postgres.ReadAsync(Victim));
        Assert.Equal(1, await postgres.CountAsync());
    }

    [Fact]
    public async Task ConcurrentFirstClaimsReserveExactlyOneRow()
    {
        var results = await Task.WhenAll(Enumerable.Range(0, 8).Select(i =>
            ClaimAsync(Victim, Interests.OpenMercato, name: $"Racer {i}", code: $"OMC10-{i:D6}")));

        Assert.Equal(1, results.Count(result => !result.AlreadyClaimed));
        Assert.Equal(1, await postgres.CountAsync());

        var stored = await postgres.ReadAsync(Victim);
        Assert.All(results, result => Assert.Equal(stored.DiscountCode, result.Lead.DiscountCode));
    }

    private Task<(Lead Lead, bool AlreadyClaimed)> ClaimAsync(
        string email,
        string interest,
        string code,
        string? name = null,
        bool marketingConsent = true,
        string? source = null) =>
        postgres.Leads.ClaimAsync(
            email, name, interest, code, discountPercent: 10, marketingConsent, source);
}
