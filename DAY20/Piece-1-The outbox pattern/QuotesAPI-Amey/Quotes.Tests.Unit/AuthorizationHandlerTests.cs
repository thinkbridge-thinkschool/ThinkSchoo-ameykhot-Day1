using FluentAssertions;
using Microsoft.AspNetCore.Authorization;
using QuotesApi.Authorization;
using QuotesApi.Models;
using System.Security.Claims;
using Xunit;

namespace Quotes.Tests.Unit;

public class QuoteOwnerAuthorizationHandlerTests
{
    private static ClaimsPrincipal MakePrincipal(Guid userId) =>
        new(new ClaimsIdentity(
            [new Claim(ClaimTypes.NameIdentifier, userId.ToString())],
            "Test"));

    private static ClaimsPrincipal AnonymousPrincipal() =>
        new(new ClaimsIdentity());

    private static AuthorizationHandlerContext BuildContext(ClaimsPrincipal user, Quote resource)
    {
        var requirement = new QuoteOwnerRequirement();
        return new AuthorizationHandlerContext([requirement], user, resource);
    }

    [Fact]
    public async Task HandleRequirement_WhenUserIsOwner_Succeeds()
    {
        var ownerId = Guid.NewGuid();
        var user = MakePrincipal(ownerId);
        var quote = new Quote("A", "T", DateTime.UtcNow) { OwnerId = ownerId };
        var context = BuildContext(user, quote);
        var handler = new QuoteOwnerAuthorizationHandler();

        await handler.HandleAsync(context);

        context.HasSucceeded.Should().BeTrue();
    }

    [Fact]
    public async Task HandleRequirement_WhenUserIsNotOwner_DoesNotSucceed()
    {
        var user = MakePrincipal(Guid.NewGuid());
        var quote = new Quote("A", "T", DateTime.UtcNow) { OwnerId = Guid.NewGuid() };
        var context = BuildContext(user, quote);
        var handler = new QuoteOwnerAuthorizationHandler();

        await handler.HandleAsync(context);

        context.HasSucceeded.Should().BeFalse();
    }

    [Fact]
    public async Task HandleRequirement_WhenQuoteHasNoOwner_DoesNotSucceed()
    {
        var user = MakePrincipal(Guid.NewGuid());
        var quote = new Quote("A", "T", DateTime.UtcNow) { OwnerId = null };
        var context = BuildContext(user, quote);
        var handler = new QuoteOwnerAuthorizationHandler();

        await handler.HandleAsync(context);

        context.HasSucceeded.Should().BeFalse();
    }

    [Fact]
    public async Task HandleRequirement_WhenUserHasNoNameIdentifier_DoesNotSucceed()
    {
        var user = AnonymousPrincipal();
        var quote = new Quote("A", "T", DateTime.UtcNow) { OwnerId = Guid.NewGuid() };
        var context = BuildContext(user, quote);
        var handler = new QuoteOwnerAuthorizationHandler();

        await handler.HandleAsync(context);

        context.HasSucceeded.Should().BeFalse();
    }
}
