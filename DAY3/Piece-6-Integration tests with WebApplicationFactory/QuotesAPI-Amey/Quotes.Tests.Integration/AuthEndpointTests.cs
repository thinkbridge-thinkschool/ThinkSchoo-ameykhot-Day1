using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using FluentAssertions;
using Xunit;

namespace Quotes.Tests.Integration;

// Each test class instance owns its factory → each test gets a fresh database.
public sealed class AuthEndpointTests : IDisposable
{
    private readonly IntegrationTestFactory _factory = new();

    public void Dispose() => _factory.Dispose();

    // ── Response DTO ─────────────────────────────────────────────────────

    private sealed record TokenResponse(
        [property: JsonPropertyName("access_token")]  string AccessToken,
        [property: JsonPropertyName("refresh_token")] string RefreshToken,
        [property: JsonPropertyName("expires_in")]    int    ExpiresIn);

    // ── 12. POST /api/auth/login — valid credentials → 200 with token pair

    [Fact]
    public async Task Login_ValidCredentials_ReturnsTokenPair()
    {
        using var client = _factory.CreateAnonymousClient();

        var response = await client.PostAsJsonAsync("/api/auth/login",
            new { email = "user@test.com", password = "password123" });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<TokenResponse>();
        body.Should().NotBeNull();
        body!.AccessToken.Should().NotBeNullOrWhiteSpace();
        body.RefreshToken.Should().NotBeNullOrWhiteSpace();
        body.ExpiresIn.Should().BeGreaterThan(0);
    }

    // ── 13. POST /api/auth/login — wrong password → 401 ─────────────────

    [Fact]
    public async Task Login_WrongPassword_ReturnsUnauthorized()
    {
        using var client = _factory.CreateAnonymousClient();

        var response = await client.PostAsJsonAsync("/api/auth/login",
            new { email = "user@test.com", password = "wrongpassword" });

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    // ── 14. POST /api/auth/refresh — valid token → 200 with rotated pair ─

    [Fact]
    public async Task Refresh_ValidToken_ReturnsNewRotatedTokenPair()
    {
        using var client = _factory.CreateAnonymousClient();

        // Login first
        var loginResp = await client.PostAsJsonAsync("/api/auth/login",
            new { email = "user@test.com", password = "password123" });
        loginResp.EnsureSuccessStatusCode();
        var login = await loginResp.Content.ReadFromJsonAsync<TokenResponse>();

        // Refresh
        var refreshResp = await client.PostAsJsonAsync("/api/auth/refresh",
            new { refresh_token = login!.RefreshToken });

        refreshResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var refreshed = await refreshResp.Content.ReadFromJsonAsync<TokenResponse>();
        refreshed.Should().NotBeNull();
        refreshed!.AccessToken.Should().NotBeNullOrWhiteSpace();
        // Rotation: new refresh token must differ from the original
        refreshed.RefreshToken.Should().NotBe(login.RefreshToken);
    }

    // ── 15. POST /api/auth/refresh — garbage token → 401 ────────────────

    [Fact]
    public async Task Refresh_InvalidToken_ReturnsUnauthorized()
    {
        using var client = _factory.CreateAnonymousClient();

        var response = await client.PostAsJsonAsync("/api/auth/refresh",
            new { refresh_token = "not-a-valid-refresh-token-at-all" });

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }
}
