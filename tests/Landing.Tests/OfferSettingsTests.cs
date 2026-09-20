using Landing.Configuration;

namespace Landing.Tests;

public sealed class OfferSettingsTests
{
    [Fact]
    public void RepositoryFallbackOffersFifteenPercent()
    {
        var previous = Environment.GetEnvironmentVariable("OFFER_DISCOUNT_PERCENT");
        try
        {
            Environment.SetEnvironmentVariable("OFFER_DISCOUNT_PERCENT", null);

            Assert.Equal(15, OfferSettings.FromEnvironment().DiscountPercent);
        }
        finally
        {
            Environment.SetEnvironmentVariable("OFFER_DISCOUNT_PERCENT", previous);
        }
    }
}
