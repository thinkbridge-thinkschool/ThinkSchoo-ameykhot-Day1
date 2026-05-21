using Microsoft.AspNetCore.Authorization;
using QuotesApi.Models;
using System.Security.Claims;

namespace QuotesApi.Authorization;

public class QuoteOwnerAuthorizationHandler
    : AuthorizationHandler<QuoteOwnerRequirement, Quote>
{
    protected override Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        QuoteOwnerRequirement requirement,
        Quote resource)
    {
        var userId = context.User.FindFirstValue(ClaimTypes.NameIdentifier);

        if (userId is not null
            && resource.OwnerId.HasValue
            && resource.OwnerId.Value.ToString() == userId)
        {
            context.Succeed(requirement);
        }

        return Task.CompletedTask;
    }
}
