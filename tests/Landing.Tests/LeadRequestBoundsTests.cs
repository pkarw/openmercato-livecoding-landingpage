using Landing.Caching;
using Landing.Configuration;
using Landing.Data;
using Landing.Endpoints;
using Landing.Models;
using Landing.Notifications;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Metadata;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Landing.Tests;

public sealed class LeadRequestBoundsTests
{
    [Fact]
    public async Task BoundaryValuesReachTheClaimHandlerNormalized()
    {
        var email = ValidEmailAtMaximumLength();
        var request = ValidRequest(
            email: $" {email} ",
            name: $" {new string('n', LeadInputLimits.MaxNameLength)} ",
            source: $" {new string('s', LeadInputLimits.MaxSourceLength)} ");
        var (result, called, normalized) = await InvokeAsync(request);

        Assert.True(called);
        Assert.Same(ContinuationResult, result);
        Assert.NotNull(normalized);
        Assert.Equal(email, normalized.Email);
        Assert.Equal(LeadInputLimits.MaxNameLength, normalized.Name?.Length);
        Assert.Equal(LeadInputLimits.MaxSourceLength, normalized.Source?.Length);
    }

    [Theory]
    [MemberData(nameof(RejectedRequests))]
    public async Task InvalidBoundsDoNotEnterTheSideEffectingClaimHandler(LeadRequest request)
    {
        var (result, called, _) = await InvokeAsync(request);

        Assert.False(called);
        Assert.NotSame(ContinuationResult, result);
    }

    [Fact]
    public async Task ClaimEndpointPublishesTheFourKiBBodyLimit()
    {
        var builder = WebApplication.CreateBuilder();
        builder.Services.AddSingleton<OfferSettings>(_ => null!);
        builder.Services.AddSingleton<LeadRepository>(_ => null!);
        builder.Services.AddSingleton<CacheStore>(_ => null!);
        builder.Services.AddSingleton<LeadNotifier>(_ => null!);
        await using var app = builder.Build();
        app.MapLeadEndpoints();

        var endpoint = ((IEndpointRouteBuilder)app).DataSources
            .SelectMany(source => source.Endpoints)
            .OfType<RouteEndpoint>()
            .Single(candidate => candidate.RoutePattern.RawText == "/api/leads");
        var bodyLimit = endpoint.Metadata.GetMetadata<IRequestSizeLimitMetadata>();

        Assert.NotNull(bodyLimit);
        Assert.Equal(LeadInputLimits.MaxRequestBodyBytes, bodyLimit.MaxRequestBodySize);
    }

    public static TheoryData<LeadRequest> RejectedRequests => new()
    {
        ValidRequest(email: $"user@{new string('e', LeadInputLimits.MaxEmailLength - 4)}"),
        ValidRequest(name: new string('n', LeadInputLimits.MaxNameLength + 1)),
        ValidRequest(source: new string('s', LeadInputLimits.MaxSourceLength + 1)),
        ValidRequest(name: new string('n', 100_000)),
        ValidRequest(email: "user\n@example.com"),
        ValidRequest(name: "Ada\0Lovelace"),
        ValidRequest(source: "/claim\rtracking"),
    };

    private static readonly object ContinuationResult = new();

    private static async Task<(object? Result, bool Called, LeadRequest? Normalized)> InvokeAsync(LeadRequest request)
    {
        var filter = new LeadRequestBoundsFilter();
        var context = EndpointFilterInvocationContext.Create(new DefaultHttpContext(), request);
        var called = false;
        LeadRequest? normalized = null;

        var result = await filter.InvokeAsync(context, invocation =>
        {
            called = true;
            normalized = invocation.GetArgument<LeadRequest>(0);
            return ValueTask.FromResult<object?>(ContinuationResult);
        });

        return (result, called, normalized);
    }

    private static LeadRequest ValidRequest(string? email = null, string? name = "Ada", string? source = "/claim") =>
        new(
            email ?? "ada@example.com",
            name,
            Interests.OpenMercato,
            PrivacyAccepted: true,
            MarketingConsent: true,
            source);

    private static string ValidEmailAtMaximumLength()
    {
        var local = new string('a', 64);
        var domain = $"{new string('b', 63)}.{new string('c', 63)}.{new string('d', 61)}";
        var email = $"{local}@{domain}";
        Assert.Equal(LeadInputLimits.MaxEmailLength, email.Length);
        return email;
    }
}
