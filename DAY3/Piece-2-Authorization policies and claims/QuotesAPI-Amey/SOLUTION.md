# Day 3 — Piece 2: Authorization Policies and Claims

## Submission

### Policy 1 — Claim-based: `can-edit-quotes`

**Program.cs**
```csharp
builder.Services.AddAuthorization(options =>
{
    // Policy 1 (claim-based): any token with scope=quotes.write can create/edit quotes
    options.AddPolicy("can-edit-quotes", p => p.RequireClaim("scope", "quotes.write"));

    // Policy 2 (custom requirement): user can only delete their own quotes
    options.AddPolicy("quote-owner", p =>
        p.RequireClaim("scope", "quotes.write")
         .AddRequirements(new QuoteOwnerRequirement()));
});

builder.Services.AddSingleton<IAuthorizationHandler, QuoteOwnerAuthorizationHandler>();
```

Applied to endpoints:
```csharp
quotes.MapPost("/", CreateQuote).RequireAuthorization("can-edit-quotes");
quotes.MapDelete("/{id}", DeleteQuote).RequireAuthorization("can-edit-quotes");
```

---

### Policy 2 — Custom `IAuthorizationRequirement`: `quote-owner`

**Authorization/QuoteOwnerRequirement.cs**
```csharp
public class QuoteOwnerRequirement : IAuthorizationRequirement { }
```

**Authorization/QuoteOwnerAuthorizationHandler.cs**
```csharp
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
```

Used in the delete endpoint (resource-based):
```csharp
var quote = await repository.GetQuoteByIdAsync(id, cancellationToken);
var authResult = await authorizationService.AuthorizeAsync(httpContext.User, quote, "quote-owner");
if (!authResult.Succeeded)
    return Results.Forbid();   // 403
```

---

### Tests showing 403 when policy fails

**QuotesApi.Tests/AuthorizationPolicyTests.cs**

```csharp
// Test: scope claim missing → can-edit-quotes fails
[Fact]
public async Task CanEditQuotesPolicy_WithoutScopeClaim_Fails()
{
    var authService = BuildAuthService();
    var user = MakePrincipal(Guid.NewGuid(), withScope: false);

    var result = await authService.AuthorizeAsync(user, null, "can-edit-quotes");

    Assert.False(result.Succeeded);   // → 403 at endpoint
}

// Test: different user's quote → quote-owner fails
[Fact]
public async Task QuoteOwnerPolicy_WhenUserIsNotOwner_Fails()
{
    var requirement = new QuoteOwnerRequirement();
    var context = new AuthorizationHandlerContext(
        new[] { requirement },
        MakePrincipal(Guid.NewGuid()),          // requester
        new Quote("A", "T", DateTime.UtcNow) { OwnerId = Guid.NewGuid() }); // different owner

    var handler = new QuoteOwnerAuthorizationHandler();
    await handler.HandleAsync(context);

    Assert.False(context.HasSucceeded);   // → Results.Forbid()
}
```

**Actual terminal output (`dotnet test --logger "console;verbosity=detailed"`):**
```
  QuotesApi -> ...\bin\Debug\net10.0\QuotesApi.dll
  QuotesApi.Tests -> ...\bin\Debug\net10.0\QuotesApi.Tests.dll
Test run for QuotesApi.Tests.dll (.NETCoreApp,Version=v10.0)

  Passed QuotesApi.Tests.QuoteFactoryTests.Create_WithExplicitTimestamp_UsesProvidedTimestamp [17 ms]
  Passed QuotesApi.Tests.QuoteFactoryTests.Create_UsesClockUtcNow_ForCreatedAt [1 ms]
  Passed QuotesApi.Tests.AuthorizationPolicyTests.CanEditQuotesPolicy_WithoutScopeClaim_Fails [148 ms]
  Passed QuotesApi.Tests.AuthorizationPolicyTests.QuoteOwnerPolicy_WhenQuoteHasNoOwner_Fails [22 ms]
  Passed QuotesApi.Tests.AuthorizationPolicyTests.QuoteOwnerPolicy_WhenUserIsNotOwner_Fails [3 ms]
  Passed QuotesApi.Tests.AuthorizationPolicyTests.QuoteOwnerPolicy_FullPipeline_WhenNotOwner_ReturnsForbid [4 ms]
  Passed QuotesApi.Tests.AuthorizationPolicyTests.CanEditQuotesPolicy_WithScopeClaim_Succeeds [3 ms]
  Passed QuotesApi.Tests.AuthorizationPolicyTests.QuoteOwnerPolicy_WhenUserIsOwner_Succeeds [3 ms]
  Passed QuotesApi.Tests.AuthTokenServiceTests.Refresh_WhenTokenReused_RevokesEntireChain [4 s]

Test Run Successful.
     Passed: 9
```

---

## What I learned this session

The biggest click was understanding **why policies beat roles** for long-lived systems. `RequireRole("admin")` hard-codes a string that can drift as the org evolves; `RequireClaim("scope", "quotes.write")` makes the *capability* the first-class thing — you can reassign who gets that scope without touching the code.

The second click was the difference between **claim-based** and **resource-based** authorization. Claim-based answers "does this user have permission to do this *kind* of thing?" globally. Resource-based answers "does this user have permission to do this *specific* thing?" — and requires the object to be loaded first, then passed to `AuthorizeAsync` as the `resource` argument.

Using `AuthorizationHandler<TRequirement, TResource>` makes that intent explicit: the handler *only* fires when the resource is of that exact type, and you can inject services (like `DbContext`) via DI.

---

## What would break this?

1. **Scope claim always injected** — right now `AuthTokenService` stamps `scope: quotes.write` on every login token, so the claim-based policy is effectively the same as `RequireAuthentication`. In a real system you'd differentiate: read-only users get no scope, write users get `quotes.write`, admins get `quotes.admin`.

2. **OwnerId is nullable** — legacy/seeded quotes have `OwnerId = null`. The current handler denies on null (safe-by-default), but that means nobody can delete seeded data without an admin bypass policy.

3. **No admin override** — there is no `role=admin` bypass on the `quote-owner` policy. An admin cannot delete another user's quote without impersonation or a separate policy that short-circuits the handler.

4. **Race condition on delete** — the handler checks ownership using the loaded `Quote`, but then `DeleteQuoteAsync` re-fetches by ID internally. If the quote is deleted between those two calls (by the same user on two tabs), the second call silently returns `false` (not found). Not a security issue but a consistency gap.

5. **Token scope not re-validated on refresh** — the `scope` claim is baked into the access token at login. Refreshing issues a new access token with the same scope, so revoking access requires invalidating all refresh tokens, not just access tokens.
