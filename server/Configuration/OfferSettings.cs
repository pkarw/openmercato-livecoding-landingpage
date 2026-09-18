using System.Globalization;

namespace Landing.Configuration;

/// <summary>
/// The limited-time offer: -10% on both products until the end of Sunday 20.09 (Warsaw time).
/// Both values are overridable so the campaign can be extended without a code change.
/// </summary>
public sealed record OfferSettings(int DiscountPercent, DateTimeOffset EndsAt)
{
    public const string DefaultEndsAt = "2026-09-20T23:59:59+02:00";

    public static OfferSettings FromEnvironment()
    {
        var percent = int.TryParse(Environment.GetEnvironmentVariable("OFFER_DISCOUNT_PERCENT"), out var parsed)
            ? parsed
            : 10;

        var endsAtRaw = Environment.GetEnvironmentVariable("OFFER_ENDS_AT") ?? DefaultEndsAt;
        var endsAt = DateTimeOffset.TryParse(endsAtRaw, CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal, out var parsedEndsAt)
            ? parsedEndsAt
            : DateTimeOffset.Parse(DefaultEndsAt, CultureInfo.InvariantCulture);

        return new OfferSettings(percent, endsAt);
    }

    public bool IsActive(DateTimeOffset now) => now <= EndsAt;
}
