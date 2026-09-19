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

public static class LeadInputLimits
{
    public const int MaxRequestBodyBytes = 4 * 1024;
    public const int MaxEmailLength = 254;
    public const int MaxNameLength = 200;
    public const int MaxSourceLength = 200;
}

public static class LeadRequestBounds
{
    public static bool TryNormalize(LeadRequest request, out LeadRequest normalized, out string? error)
    {
        if (HasControlCharacter(request.Email))
        {
            normalized = request;
            error = "Email cannot contain control characters.";
            return false;
        }

        var email = request.Email?.Trim();
        if (email is { Length: > LeadInputLimits.MaxEmailLength })
        {
            normalized = request;
            error = $"Email must be {LeadInputLimits.MaxEmailLength} characters or fewer.";
            return false;
        }

        if (HasControlCharacter(request.Name))
        {
            normalized = request;
            error = "Name cannot contain control characters.";
            return false;
        }

        var name = string.IsNullOrWhiteSpace(request.Name) ? null : request.Name.Trim();
        if (name is { Length: > LeadInputLimits.MaxNameLength })
        {
            normalized = request;
            error = $"Name must be {LeadInputLimits.MaxNameLength} characters or fewer.";
            return false;
        }

        if (HasControlCharacter(request.Source))
        {
            normalized = request;
            error = "Source cannot contain control characters.";
            return false;
        }

        var source = string.IsNullOrWhiteSpace(request.Source) ? null : request.Source.Trim();
        if (source is { Length: > LeadInputLimits.MaxSourceLength })
        {
            normalized = request;
            error = $"Source must be {LeadInputLimits.MaxSourceLength} characters or fewer.";
            return false;
        }

        normalized = request with { Email = email, Name = name, Source = source };
        error = null;
        return true;
    }

    private static bool HasControlCharacter(string? value) =>
        value is not null && value.Any(char.IsControl);
}

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
