using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.DependencyInjection;
using QuotesApi.Authorization;
using QuotesApi.Models;
using System.Security.Claims;
using Xunit;

namespace QuotesApi.Tests;

public class AuthorizationPolicyTests
{
    // ── Helpers ──────────────────────────────────────────────────────────

    private static IAuthorizationService BuildAuthService(Action<AuthorizationOptions>? configure = null)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddAuthorization(options =>
        {
            options.AddPolicy("can-edit-quotes", p => p.RequireClaim("scope", "quotes.write"));
            options.AddPolicy("quote-owner", p =>
                p.RequireClaim("scope", "quotes.write")
                 .AddRequirements(new QuoteOwnerRequirement()));
            configure?.Invoke(options);
        });
        services.AddSingleton<IAuthorizationHandler, QuoteOwnerAuthorizationHandler>();
        return services.BuildServiceProvider().GetRequiredService<IAuthorizationService>();
    }

    private static ClaimsPrincipal MakePrincipal(Guid userId, bool withScope = true)
    {
        var claims = new List<Claim>
        {
            new Claim(ClaimTypes.NameIdentifier, userId.ToString()),
            new Claim(ClaimTypes.Email, "test@test.com")
        };
        if (withScope)
            claims.Add(new Claim("scope", "quotes.write"));

        return new ClaimsPrincipal(new ClaimsIdentity(claims, "Test"));
    }

    // ── Policy 1: claim-based "can-edit-quotes" ───────────────────────

    [Fact]
    public async Task CanEditQuotesPolicy_WithoutScopeClaim_Fails()
    {
        var authService = BuildAuthService();
        var user = MakePrincipal(Guid.NewGuid(), withScope: false);

        var result = await authService.AuthorizeAsync(user, null, "can-edit-quotes");

        Assert.False(result.Succeeded);
    }

    [Fact]
    public async Task CanEditQuotesPolicy_WithScopeClaim_Succeeds()
    {
        var authService = BuildAuthService();
        var user = MakePrincipal(Guid.NewGuid(), withScope: true);

        var result = await authService.AuthorizeAsync(user, null, "can-edit-quotes");

        Assert.True(result.Succeeded);
    }

    // ── Policy 2: resource-based "quote-owner" ────────────────────────

    [Fact]
    public async Task QuoteOwnerPolicy_WhenUserIsNotOwner_Fails()
    {
        var authService = BuildAuthService();
        var requestingUser = MakePrincipal(Guid.NewGuid(), withScope: true);

        var quote = new Quote("Author", "Text", DateTime.UtcNow)
        {
            OwnerId = Guid.NewGuid() // different owner
        };

        // Evaluate the handler directly for precision
        var requirement = new QuoteOwnerRequirement();
        var context = new AuthorizationHandlerContext(
            new[] { requirement },
            requestingUser,
            quote);

        var handler = new QuoteOwnerAuthorizationHandler();
        await handler.HandleAsync(context);

        Assert.False(context.HasSucceeded);
    }

    [Fact]
    public async Task QuoteOwnerPolicy_WhenUserIsOwner_Succeeds()
    {
        var authService = BuildAuthService();
        var ownerId = Guid.NewGuid();
        var requestingUser = MakePrincipal(ownerId, withScope: true);

        var quote = new Quote("Author", "Text", DateTime.UtcNow)
        {
            OwnerId = ownerId // same owner
        };

        var requirement = new QuoteOwnerRequirement();
        var context = new AuthorizationHandlerContext(
            new[] { requirement },
            requestingUser,
            quote);

        var handler = new QuoteOwnerAuthorizationHandler();
        await handler.HandleAsync(context);

        Assert.True(context.HasSucceeded);
    }

    [Fact]
    public async Task QuoteOwnerPolicy_WhenQuoteHasNoOwner_Fails()
    {
        var authService = BuildAuthService();
        var requestingUser = MakePrincipal(Guid.NewGuid(), withScope: true);

        // Quote with no OwnerId (e.g., seeded or legacy data)
        var quote = new Quote("Author", "Text", DateTime.UtcNow) { OwnerId = null };

        var requirement = new QuoteOwnerRequirement();
        var context = new AuthorizationHandlerContext(
            new[] { requirement },
            requestingUser,
            quote);

        var handler = new QuoteOwnerAuthorizationHandler();
        await handler.HandleAsync(context);

        // No owner set → cannot prove ownership → deny
        Assert.False(context.HasSucceeded);
    }

    [Fact]
    public async Task QuoteOwnerPolicy_FullPipeline_WhenNotOwner_ReturnsForbid()
    {
        var authService = BuildAuthService();
        var user = MakePrincipal(Guid.NewGuid(), withScope: true);
        var quote = new Quote("Author", "Text", DateTime.UtcNow)
        {
            OwnerId = Guid.NewGuid() // someone else's quote
        };

        var result = await authService.AuthorizeAsync(user, quote, "quote-owner");

        Assert.False(result.Succeeded);
    }
}
