using System.Security.Cryptography;

namespace Landing.Models;

// CreatedAt is a DateTime because Npgsql hands back `timestamptz` as a UTC DateTime,
// and Dapper matches the record constructor by exact type.
public sealed record Lead(
    int Id,
    string Email,
    string? Name,
    string Interest,
    string DiscountCode,
    DateTime CreatedAt);

/// <summary>What the landing page posts when someone claims the discount.</summary>
public sealed record LeadRequest(
    string? Email,
    string? Name,
    string? Interest,
    bool PrivacyAccepted,
    bool MarketingConsent,
    string? Source);

public static class Interests
{
    public const string OpenMercato = "openmercato";
    public const string AiTechLeaders = "aitechleaders";
    public const string Both = "both";

    public static readonly string[] All = [OpenMercato, AiTechLeaders, Both];

    public static bool IsValid(string? interest) =>
        interest is not null && All.Contains(interest, StringComparer.Ordinal);
}

public static class DiscountCodes
{
    // No 0/O/1/I — these codes get read off a screen and typed by hand.
    private const string Alphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";

    public static string Generate(string interest, int discountPercent)
    {
        var prefix = interest switch
        {
            Interests.OpenMercato => "OMC",
            Interests.AiTechLeaders => "ATL",
            _ => "DUO",
        };

        var suffix = string.Concat(
            RandomNumberGenerator.GetBytes(6).Select(b => Alphabet[b % Alphabet.Length]));

        return $"{prefix}{discountPercent}-{suffix}";
    }
}
