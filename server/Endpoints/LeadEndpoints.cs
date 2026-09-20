using System.Net.Mail;
using Landing.Caching;
using Landing.Configuration;
using Landing.Data;
using Landing.Models;
using Landing.Notifications;
using Microsoft.AspNetCore.Mvc;

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
            EmailSettings emailSettings,
            ILogger<Lead> logger,
            CancellationToken ct) =>
        {
            if (!offer.IsActive(DateTimeOffset.UtcNow))
            {
                return Results.Json(
                    new { error = "The offer closed at the end of Sunday 20.09 — thank you for the interest!" },
                    statusCode: StatusCodes.Status410Gone);
            }

            var email = request.Email ?? string.Empty;
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

            var (lead, alreadyClaimed) = await leads.ClaimAsync(
                email,
                request.Name,
                request.Interest!,
                () => DiscountCodes.Generate(request.Interest!, offer.DiscountPercent),
                offer.DiscountPercent,
                emailSettings.LeadsInbox,
                request.MarketingConsent,
                request.Source,
                ct);

            await cache.DropAsync(CacheStore.LeadCountKey);
            logger.LogInformation(
                "Lead {Status} for {Interest}; email notifications are queued",
                alreadyClaimed ? "returning" : "captured",
                lead.Interest);

            // Keep the legacy fields as a compatibility bridge, but populate them only from this
            // request. Returning persisted values or the real duplicate state would let anyone
            // probe whether an address is registered and recover that person's saved details.
            return Results.Ok(new
            {
                lead = new
                {
                    Email = email,
                    Name = request.Name,
                    Interest = request.Interest!,
                    CreatedAt = DateTimeOffset.UtcNow,
                },
                discountPercent = offer.DiscountPercent,
                endsAt = offer.EndsAt,
                alreadyClaimed = false,
            });
        })
            .WithMetadata(new RequestSizeLimitAttribute(LeadInputLimits.MaxRequestBodyBytes))
            .AddEndpointFilter<LeadRequestBoundsFilter>()
            .WithTags("Offer");

        return routes;
    }

    private static bool IsEmail(string value) =>
        MailAddress.TryCreate(value, out var address) && address.Host.Contains('.');
}

public sealed class LeadRequestBoundsFilter : IEndpointFilter
{
    public ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var request = context.GetArgument<LeadRequest>(0);
        if (!LeadRequestBounds.TryNormalize(request, out var normalized, out var error))
        {
            return ValueTask.FromResult<object?>(Results.BadRequest(new { error }));
        }

        context.Arguments[0] = normalized;
        return next(context);
    }
}
