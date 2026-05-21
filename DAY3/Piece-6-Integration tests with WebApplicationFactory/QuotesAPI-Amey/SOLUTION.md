# Piece 6 — Integration Tests with WebApplicationFactory

## Submission

### WebApplicationFactory subclass

```csharp
// IntegrationTestFactory.cs
public sealed class IntegrationTestFactory : WebApplicationFactory<Program>
{
    public const string TestJwtKey = "ThinkSchoolDay2JwtSigningKey-UseAtLeast32Chars";

    private readonly string _dbPath = Path.Combine(
        Path.GetTempPath(),
        $"quotes_int_{Guid.NewGuid():N}.db");

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureAppConfiguration((_, cfg) =>
        {
            cfg.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:DefaultConnection"] = $"Data Source={_dbPath}",
                ["Jwt:Key"]                            = TestJwtKey,
                ["Jwt:AccessTokenLifetimeSeconds"]     = "900",
                // Fake Entra values — OIDC discovery is lazy so tests never hit it
                ["EntraId:TenantId"] = "00000000-0000-0000-0000-000000000000",
                ["EntraId:ClientId"] = "00000000-0000-0000-0000-000000000001"
            });
        });
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        foreach (var f in new[] { _dbPath, _dbPath + "-shm", _dbPath + "-wal" })
            if (File.Exists(f)) File.Delete(f);
    }

    public HttpClient CreateAnonymousClient() => CreateClient();

    public HttpClient CreateAuthenticatedClient(
        bool withScope = true, Guid? userId = null, int lifetimeSeconds = 3600)
    {
        var client = CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", MakeToken(userId, withScope, lifetimeSeconds));
        return client;
    }

    public static string MakeToken(
        Guid? userId = null, bool withScope = true, int lifetimeSeconds = 3600)
    {
        var key   = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(TestJwtKey));
        var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, (userId ?? Guid.NewGuid()).ToString()),
            new(ClaimTypes.Email, "test@test.com")
        };
        if (withScope) claims.Add(new("scope", "quotes.write"));

        var token = new JwtSecurityToken(
            claims: claims,
            expires: DateTime.UtcNow.AddSeconds(lifetimeSeconds),
            signingCredentials: creds);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}
```

### Isolation design

`IntegrationTestFactory` is **not** used with `IClassFixture`. Instead, each test class
declares it as a plain field and implements `IDisposable`:

```csharp
public sealed class QuoteEndpointTests : IDisposable
{
    private readonly IntegrationTestFactory _factory = new();
    public void Dispose() => _factory.Dispose();
    // ...
}
```

Because xUnit creates a **new class instance per test method**, every `[Fact]` gets its
own factory → its own GUID-named SQLite file → its own migrated + seeded database.
Tests are truly isolated — no shared state, no `[BeforeEach]` teardown needed.

---

### Happy-path test: `CreateQuote_ValidRequest_ReturnsCreatedWithQuote`

```csharp
[Fact]
public async Task CreateQuote_ValidRequest_ReturnsCreatedWithQuote()
{
    // Arrange
    using var client = _factory.CreateAuthenticatedClient();

    // Act
    var response = await client.PostAsJsonAsync("/api/quotes",
        new { author = "Ada Lovelace", text = "The Analytical Engine weaves algebraic patterns." });

    // Assert
    response.StatusCode.Should().Be(HttpStatusCode.Created);
    var body = await response.Content.ReadFromJsonAsync<QuoteDto>();
    body.Should().NotBeNull();
    body!.Author.Should().Be("Ada Lovelace");
    body.Id.Should().BeGreaterThan(0);
}
```

---

### Error-path test: `DeleteQuote_NotOwner_ReturnsForbidden`

```csharp
[Fact]
public async Task DeleteQuote_NotOwner_ReturnsForbidden()
{
    // Arrange: two distinct user IDs
    var ownerAId = Guid.NewGuid();
    var ownerBId = Guid.NewGuid();

    // Owner A creates a quote — OwnerId is stamped from the JWT claim
    using var creatorClient = _factory.CreateAuthenticatedClient(userId: ownerAId);
    var createResp = await creatorClient.PostAsJsonAsync("/api/quotes",
        new { author = "Owner A", text = "This quote belongs exclusively to owner A." });
    createResp.EnsureSuccessStatusCode();
    var created = await createResp.Content.ReadFromJsonAsync<QuoteDto>();

    // Owner B (different user, same scope) attempts to delete Owner A's quote
    using var attackerClient = _factory.CreateAuthenticatedClient(userId: ownerBId);
    var deleteResp = await attackerClient.DeleteAsync($"/api/quotes/{created!.Id}");

    // Assert: resource-based auth handler denies → 403
    deleteResp.StatusCode.Should().Be(HttpStatusCode.Forbidden);
}
```

---

### Test run output

```
dotnet test --no-build

Passed!  - Failed: 0, Passed: 15, Skipped: 0, Total: 15, Duration: 14 s - Quotes.Tests.Integration.dll (net10.0)
```

All 15 tests passed.

Full test list:

| Test | Status |
|------|--------|
| `QuoteEndpointTests.GetQuotes_ReturnsOk_WithPaginationShape` | Passed |
| `QuoteEndpointTests.GetQuotes_InvalidPage_ReturnsBadRequest` | Passed |
| `QuoteEndpointTests.GetQuoteById_UnknownId_ReturnsNotFound` | Passed |
| `QuoteEndpointTests.GetQuoteById_AfterCreate_ReturnsMatchingQuote` | Passed |
| `QuoteEndpointTests.CreateQuote_Anonymous_ReturnsUnauthorized` | Passed |
| `QuoteEndpointTests.CreateQuote_WithoutScopeClaim_ReturnsForbidden` | Passed |
| `QuoteEndpointTests.CreateQuote_ValidRequest_ReturnsCreatedWithQuote` | Passed |
| `QuoteEndpointTests.CreateQuote_EmptyAuthor_Returns422WithValidationErrors` | Passed |
| `QuoteEndpointTests.DeleteQuote_Anonymous_ReturnsUnauthorized` | Passed |
| `QuoteEndpointTests.DeleteQuote_NotOwner_ReturnsForbidden` | Passed |
| `QuoteEndpointTests.DeleteQuote_Owner_ReturnsNoContent` | Passed |
| `AuthEndpointTests.Login_ValidCredentials_ReturnsTokenPair` | Passed |
| `AuthEndpointTests.Login_WrongPassword_ReturnsUnauthorized` | Passed |
| `AuthEndpointTests.Refresh_ValidToken_ReturnsNewRotatedTokenPair` | Passed |
| `AuthEndpointTests.Refresh_InvalidToken_ReturnsUnauthorized` | Passed |

---

## What I learned this session

The key insight was understanding **why** `WebApplicationFactory` is powerful: it boots the
*same* wiring — middleware order, policy evaluation, EF migrations, seeding — that runs in
production, but entirely in-memory without a real HTTP listener. You get confidence that the
real pipeline works, not just that individual units behave correctly in isolation.

The per-test isolation pattern (factory per class instance, IDisposable teardown) clicked
cleanly: xUnit's new-instance-per-test behaviour becomes an isolation primitive itself —
no `[BeforeEach]`, no manual DB truncation, just construct and dispose.

For auth: the `MultiScheme` policy scheme routes tokens by issuer. Test tokens without an
issuer automatically route to the `InternalScheme` (HS256), which is exactly what we want
for integration tests — no need to involve Entra at all.

---

## What would break this?

1. **Parallel test execution across processes** — each test writes a unique temp file so
   multiple processes are safe, but xUnit's default in-process parallelism is also fine
   since each class instance owns a different GUID path.

2. **EF migration that adds a NOT NULL column with no default** — `ApplyMigrations` would
   fail on the temp DB and every test would error before even starting.

3. **Singleton state inside the app** — if any service registered as `Singleton` caches
   state between requests (e.g., an in-memory counter), tests within the same
   `IntegrationTestFactory` instance could interfere. Here everything stateful (DB, tokens)
   lives in SQLite, so the concern doesn't apply.

4. **`ClockSkew = TimeSpan.Zero`** — the expired-token test in `QuotesApi.Tests` crafts a
   token with `lifetimeSeconds: -1`. If someone changed the JWT config to allow clock skew,
   that test would start flaking. The factory overrides the config so it's protected.

5. **The seeded user** — tests 12–15 depend on `user@test.com / password123` being seeded
   by `ApplyMigrations`. If the seed condition changes, auth tests would start returning 401
   even on the "valid login" path.
