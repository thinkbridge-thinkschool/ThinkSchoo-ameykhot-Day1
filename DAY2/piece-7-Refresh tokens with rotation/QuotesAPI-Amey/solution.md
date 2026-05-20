# Piece-7 Submission - Refresh Tokens With Rotation

## What I implemented

1. Added `RefreshTokens` persistence with server-side hashing and chain tracking.
2. Added login token minting for both access and refresh tokens.
3. Added `POST /api/auth/refresh` with single-use rotation.
4. Added `POST /api/auth/logout` refresh-token revocation.
5. Added reuse detection: if replaced token is reused, revoke whole family and log security event.
6. Added test proving reuse detection kills the chain.

---

## Refresh endpoint code

```csharp
private static async Task<IResult> Refresh(
    RefreshRequest request,
    IAuthTokenService authTokenService,
    CancellationToken cancellationToken = default)
{
    var outcome = await authTokenService.RefreshAsync(request.RefreshToken, cancellationToken);

    if (!outcome.IsSuccess)
    {
        var detail = outcome.FailureReason switch
        {
            RefreshFailureReason.InvalidToken => "Invalid refresh token.",
            RefreshFailureReason.ExpiredToken => "Refresh token expired.",
            RefreshFailureReason.ReuseDetected => "Token reuse detected. Please log in again.",
            _ => "Refresh token revoked. Please log in again."
        };

        return Results.Json(
            new ProblemDetails { Title = "Unauthorized", Status = 401, Detail = detail },
            statusCode: 401);
    }

    return Results.Ok(new
    {
        access_token = outcome.Tokens!.AccessToken,
        refresh_token = outcome.Tokens.RefreshToken,
        expires_in = outcome.Tokens.ExpiresIn
    });
}
```

---

## Reuse-detection test code

```csharp
[Fact]
public async Task Refresh_WhenTokenReused_RevokesEntireChain()
{
    var dbName = Guid.NewGuid().ToString("N");
    var options = new DbContextOptionsBuilder<QuoteDbContext>()
        .UseInMemoryDatabase(dbName)
        .Options;

    await using var dbContext = new QuoteDbContext(options);
    var user = new User
    {
        Id = Guid.NewGuid(),
        Email = "test@test.com",
        PasswordHash = BCrypt.Net.BCrypt.HashPassword("password123")
    };

    dbContext.Users.Add(user);
    await dbContext.SaveChangesAsync();

    var configuration = new ConfigurationBuilder()
        .AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Jwt:Key"] = "ThinkSchoolDay2JwtSigningKey-UseAtLeast32Chars",
            ["Jwt:AccessTokenLifetimeSeconds"] = "900"
        })
        .Build();

    IClock clock = new TestClock(new DateTimeOffset(2026, 5, 20, 12, 0, 0, TimeSpan.Zero));
    var service = new AuthTokenService(dbContext, configuration, clock, NullLogger<AuthTokenService>.Instance);

    var loginPair = await service.IssueTokenPairAsync(user);

    var firstRefresh = await service.RefreshAsync(loginPair.RefreshToken);
    Assert.True(firstRefresh.IsSuccess);

    var secondRefresh = await service.RefreshAsync(loginPair.RefreshToken);
    Assert.False(secondRefresh.IsSuccess);
    Assert.Equal(RefreshFailureReason.ReuseDetected, secondRefresh.FailureReason);

    var thirdRefresh = await service.RefreshAsync(firstRefresh.Tokens!.RefreshToken);
    Assert.False(thirdRefresh.IsSuccess);
    Assert.Equal(RefreshFailureReason.RevokedToken, thirdRefresh.FailureReason);

    var family = await dbContext.RefreshTokens
        .Select(t => t.Family)
        .FirstAsync();

    var activeFamilyTokens = await dbContext.RefreshTokens
        .Where(t => t.Family == family && t.RevokedAt == null)
        .CountAsync();

    Assert.Equal(0, activeFamilyTokens);
}
```

---

## Files changed

- `Models/RefreshToken.cs`
- `Data/QuoteDbContext.cs`
- `Services/IAuthTokenService.cs`
- `Services/AuthTokenService.cs`
- `Extensions/ServiceCollectionExtensions.cs`
- `Migrations/20260520123915_AddRefreshTokensTable.cs`
- `Migrations/20260520123915_AddRefreshTokensTable.Designer.cs`
- `Migrations/QuoteDbContextModelSnapshot.cs`
- `QuotesApi.Tests/AuthTokenServiceTests.cs`
- `QuotesApi.Tests/QuotesApi.Tests.csproj`
- `README.md`

---

## Test output

```text
Test summary: total: 3, failed: 0, succeeded: 3, skipped: 0
```
