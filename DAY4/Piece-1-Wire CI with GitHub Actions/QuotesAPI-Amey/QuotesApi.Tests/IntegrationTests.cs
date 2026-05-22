using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.IdentityModel.Tokens;
using Xunit;

namespace QuotesApi.Tests;

// ── Shared factory: one DB per test-class run ─────────────────────────────

public sealed class TestWebApplicationFactory : WebApplicationFactory<Program>
{
    private readonly string _dbPath = Path.Combine(
        Path.GetTempPath(),
        $"quotes_test_{Guid.NewGuid():N}.db");

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureAppConfiguration((_, cfg) =>
        {
            cfg.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:DefaultConnection"] = $"Data Source={_dbPath}",
                ["Jwt:Key"]                            = IntegrationTests.TestJwtKey,
                ["Jwt:AccessTokenLifetimeSeconds"]     = "900",
                // Fake Entra values – OIDC discovery is lazy so tests never trigger it
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
}

// ── Integration test suite ────────────────────────────────────────────────

public class IntegrationTests : IClassFixture<TestWebApplicationFactory>
{
    public const string TestJwtKey = "ThinkSchoolDay2JwtSigningKey-UseAtLeast32Chars";

    private readonly TestWebApplicationFactory _factory;

    public IntegrationTests(TestWebApplicationFactory factory) => _factory = factory;

    // ── Helpers ──────────────────────────────────────────────────────────

    private HttpClient NewClient(string? bearerToken = null)
    {
        var client = _factory.CreateClient();
        if (bearerToken is not null)
            client.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Bearer", bearerToken);
        return client;
    }

    private static string MakeToken(Guid? userId = null, bool withScope = true, int lifetimeSeconds = 3600)
    {
        var key   = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(TestJwtKey));
        var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, (userId ?? Guid.NewGuid()).ToString()),
            new(ClaimTypes.Email, "test@test.com")
        };
        if (withScope)
            claims.Add(new("scope", "quotes.write"));

        var token = new JwtSecurityToken(
            claims:            claims,
            expires:           DateTime.UtcNow.AddSeconds(lifetimeSeconds),
            signingCredentials: creds);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    private static object QuoteBody(string author = "Author", string text = "Some quote text that is long enough") =>
        new { author, text };

    // ── 1. Anonymous → 401 ───────────────────────────────────────────────

    [Fact]
    public async Task Anonymous_PostQuote_Returns401()
    {
        using var client = NewClient();
        var response = await client.PostAsJsonAsync("/api/quotes", QuoteBody());
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    // ── 2. Authenticated but wrong policy (no scope claim) → 403 ─────────

    [Fact]
    public async Task Authenticated_WithoutScopeClaim_PostQuote_Returns403()
    {
        using var client = NewClient(MakeToken(withScope: false));
        var response = await client.PostAsJsonAsync("/api/quotes", QuoteBody());
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    // ── 3. Authenticated + right policy (has scope claim) → 201 ──────────

    [Fact]
    public async Task Authenticated_WithScope_PostQuote_Returns201()
    {
        using var client = NewClient(MakeToken(withScope: true));
        var response = await client.PostAsJsonAsync("/api/quotes", QuoteBody("Integration", "Created by integration test"));
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    // ── 4. Expired access token → 401 ────────────────────────────────────

    [Fact]
    public async Task ExpiredToken_PostQuote_Returns401()
    {
        // lifetimeSeconds: -1 → token expired 1 second ago; ClockSkew=Zero so rejected immediately
        using var client = NewClient(MakeToken(lifetimeSeconds: -1));
        var response = await client.PostAsJsonAsync("/api/quotes", QuoteBody());
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    // ── 5. Revoked refresh chain → 401 ───────────────────────────────────

    [Fact]
    public async Task RevokedRefreshChain_Returns401()
    {
        using var client = NewClient();

        // Step 1 – login with the seeded user
        var loginResp = await client.PostAsJsonAsync("/api/auth/login",
            new { email = "user@test.com", password = "password123" });
        Assert.Equal(HttpStatusCode.OK, loginResp.StatusCode);
        var login = await loginResp.Content.ReadFromJsonAsync<TokenDto>();
        var originalRefresh = login!.RefreshToken;

        // Step 2 – first refresh succeeds, produces new token
        var r1 = await client.PostAsJsonAsync("/api/auth/refresh",
            new { refresh_token = originalRefresh });
        Assert.True(r1.IsSuccessStatusCode, "First refresh should succeed");
        var r1Body = await r1.Content.ReadFromJsonAsync<TokenDto>();

        // Step 3 – reuse the ORIGINAL refresh token → reuse detection, whole family revoked
        var r2 = await client.PostAsJsonAsync("/api/auth/refresh",
            new { refresh_token = originalRefresh });
        Assert.Equal(HttpStatusCode.Unauthorized, r2.StatusCode);

        // Step 4 – the NEW token from step 2 is also revoked (family-wide revocation)
        var r3 = await client.PostAsJsonAsync("/api/auth/refresh",
            new { refresh_token = r1Body!.RefreshToken });
        Assert.Equal(HttpStatusCode.Unauthorized, r3.StatusCode);
    }

    // ── DTO for deserialising token responses ────────────────────────────

    private sealed record TokenDto(
        [property: System.Text.Json.Serialization.JsonPropertyName("access_token")]  string AccessToken,
        [property: System.Text.Json.Serialization.JsonPropertyName("refresh_token")] string RefreshToken,
        [property: System.Text.Json.Serialization.JsonPropertyName("expires_in")]    int    ExpiresIn);
}
