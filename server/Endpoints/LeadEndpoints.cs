using System.Net.Mail;
using Landing.Caching;
using Landing.Configuration;
using Landing.Data;
using Landing.Models;
using Landing.Notifications;

namespace Landing.Endpoints;

public static class LeadEndpoints
{
    public static IEndpointRouteBuilder MapLeadEndpoints(this IEndpointRouteBuilder routes)
    {
        routes.MapGet("/api/offer", async (OfferSettings offer, LeadRepository leads, CacheStore cache, CancellationToken ct) =>
        {
            var claimed = await cache.ReadAsync<int[]>(CacheStore.LeadCountKey);
            var source = "postgres";

            if (claimed is { Length: 1 })
            {
                source = "redis";
            }
            else
            {
                claimed = [await leads.CountAsync(ct)];
                await cache.WriteAsync(CacheStore.LeadCountKey, claimed);
            }

            return Results.Ok(new
            {
                discountPercent = offer.DiscountPercent,
                endsAt = offer.EndsAt,
                active = offer.IsActive(DateTimeOffset.UtcNow),
                claimed = claimed[0],
                source,
            });
        }).WithTags("Offer");

        routes.MapPost("/api/leads", async (
            LeadRequest request,
            OfferSettings offer,
            LeadRepository leads,
            CacheStore cache,
            LeadNotifier notifier,
            ILogger<Lead> logger,
            CancellationToken ct) =>
        {
            if (!offer.IsActive(DateTimeOffset.UtcNow))
            {
                return Results.Json(
                    new { error = "The offer closed at the end of Sunday 20.09 — thank you for the interest!" },
                    statusCode: StatusCodes.Status410Gone);
            }

            var email = request.Email?.Trim() ?? string.Empty;
            if (!IsEmail(email)) return Results.BadRequest(new { error = "A valid email address is required." });

            if (!Interests.IsValid(request.Interest))
            {
                return Results.BadRequest(new { error = "Pick Open Mercato Cloud, the AI Tech Leaders training, or both." });
            }

            if (!request.PrivacyAccepted)
            {
                return Results.BadRequest(new { error = "Please accept the privacy policy to receive the code." });
            }

            if (!request.MarketingConsent)
            {
                return Results.BadRequest(new { error = "The discount code is delivered by email, so marketing consent is required." });
            }

            var name = string.IsNullOrWhiteSpace(request.Name) ? null : request.Name.Trim();
            var code = DiscountCodes.Generate(request.Interest!, offer.DiscountPercent);

            var (lead, alreadyClaimed) = await leads.ClaimAsync(
                email, name, request.Interest!, code, offer.DiscountPercent,
                request.MarketingConsent, Truncate(request.Source, 200), ct);

            await cache.DropAsync(CacheStore.LeadCountKey);
            await notifier.NotifyAsync(lead, alreadyClaimed, ct);
            logger.LogInformation("Lead {Status} for {Interest}", alreadyClaimed ? "returning" : "captured", lead.Interest);

            // The code itself stays server-side — it is emailed out separately, closer to launch.
            var payload = new
            {
                lead = new { lead.Email, lead.Name, lead.Interest, lead.CreatedAt },
                discountPercent = lead.DiscountPercent,
                endsAt = offer.EndsAt,
                alreadyClaimed,
            };

            return alreadyClaimed
                ? Results.Ok(payload)
                : Results.Created($"/api/leads/{lead.Id}", payload);
        }).WithTags("Offer");

        return routes;
    }

    private static bool IsEmail(string value) =>
        MailAddress.TryCreate(value, out var address) && address.Host.Contains('.');

    private static string? Truncate(string? value, int max) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Length <= max ? value : value[..max];
}
