using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using Microsoft.IdentityModel.Tokens;
using QuotesApi.Data;
using Xunit;

namespace Quotes.Tests.Unit;

// ── Shared WebApplicationFactory — unique in-memory SQLite per instance ───────

public sealed class QuotesApiFactory : WebApplicationFactory<Program>
{
    // A persistent open connection keeps the in-memory SQLite database alive for
    // the entire factory lifetime. Without this, EF Core closes the connection after
    // EnsureCreated() and the schema is destroyed before the next query runs.
    private readonly SqliteConnection _keepAlive = new("DataSource=:memory:");

    // Must match the key in appsettings.json (avoids key mismatch during JWT validation)
    public const string JwtKey = "ThinkSchoolDay2JwtSigningKey-UseAtLeast32Chars";

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        _keepAlive.Open();

        // Fix the content-root path issue caused by apostrophe in folder name
        builder.UseContentRoot(AppContext.BaseDirectory);

        builder.ConfigureAppConfiguration((_, cfg) =>
        {
            cfg.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["DatabaseProvider"] = "Sqlite",
                ["Jwt:Key"] = JwtKey,
                ["Jwt:AccessTokenLifetimeSeconds"] = "900",
                ["EntraId:TenantId"] = "00000000-0000-0000-0000-000000000000",
                ["EntraId:ClientId"] = "00000000-0000-0000-0000-000000000001"
            });
        });

        builder.ConfigureServices(services =>
        {
            // Remove the connection-string-based DbContext registered by AddInfrastructure
            var descriptor = services.SingleOrDefault(d =>
                d.ServiceType == typeof(DbContextOptions<QuoteDbContext>));
            if (descriptor != null)
                services.Remove(descriptor);

            // Share the persistent connection: schema survives across EF Core open/close cycles
            services.AddDbContext<QuoteDbContext>(options =>
                options.UseSqlite(_keepAlive));
        });
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing) _keepAlive.Dispose();
    }
}

// ── Base class shared by all endpoint test classes ────────────────────────────

public abstract class EndpointTestBase : IClassFixture<QuotesApiFactory>
{
    protected readonly QuotesApiFactory Factory;

    protected EndpointTestBase(QuotesApiFactory factory) => Factory = factory;

    protected HttpClient AnonymousClient() => Factory.CreateClient();

    protected HttpClient AuthenticatedClient(Guid? userId = null, bool withScope = true)
    {
        var client = Factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", MakeToken(userId, withScope));
        return client;
    }

    protected static string MakeToken(Guid? userId = null, bool withScope = true, int lifetimeSeconds = 3600)
    {
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(QuotesApiFactory.JwtKey));
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

// ── Auth endpoint tests ───────────────────────────────────────────────────────

public class AuthEndpointTests : EndpointTestBase
{
    public AuthEndpointTests(QuotesApiFactory factory) : base(factory) { }

    [Fact]
    public async Task Login_WithValidCredentials_Returns200WithTokenPair()
    {
        using var client = AnonymousClient();
        var resp = await client.PostAsJsonAsync("/api/auth/login",
            new { email = "user@test.com", password = "password123" });

        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("access_token").GetString().Should().NotBeNullOrEmpty();
        body.GetProperty("refresh_token").GetString().Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task Login_WithWrongPassword_Returns401()
    {
        using var client = AnonymousClient();
        var resp = await client.PostAsJsonAsync("/api/auth/login",
            new { email = "user@test.com", password = "wrongpassword" });

        resp.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Login_WithUnknownEmail_Returns401()
    {
        using var client = AnonymousClient();
        var resp = await client.PostAsJsonAsync("/api/auth/login",
            new { email = "nobody@test.com", password = "password123" });

        resp.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Refresh_WithValidToken_Returns200WithNewTokenPair()
    {
        using var client = AnonymousClient();
        var loginResp = await client.PostAsJsonAsync("/api/auth/login",
            new { email = "user@test.com", password = "password123" });
        var login = await loginResp.Content.ReadFromJsonAsync<JsonElement>();
        var refreshToken = login.GetProperty("refresh_token").GetString()!;

        var resp = await client.PostAsJsonAsync("/api/auth/refresh",
            new { refresh_token = refreshToken });

        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("access_token").GetString().Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task Refresh_WithInvalidToken_Returns401()
    {
        using var client = AnonymousClient();
        var resp = await client.PostAsJsonAsync("/api/auth/refresh",
            new { refresh_token = "not-a-real-token" });

        resp.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Refresh_WithExpiredToken_Returns401()
    {
        using var client = AnonymousClient();
        var loginResp = await client.PostAsJsonAsync("/api/auth/login",
            new { email = "user@test.com", password = "password123" });
        var login = await loginResp.Content.ReadFromJsonAsync<JsonElement>();
        var refreshToken = login.GetProperty("refresh_token").GetString()!;

        // Reuse detection: consume the token, then reuse the original
        await client.PostAsJsonAsync("/api/auth/refresh", new { refresh_token = refreshToken });
        var reuseResp = await client.PostAsJsonAsync("/api/auth/refresh", new { refresh_token = refreshToken });

        reuseResp.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Logout_WithAnyToken_Returns204()
    {
        using var client = AnonymousClient();
        var loginResp = await client.PostAsJsonAsync("/api/auth/login",
            new { email = "user@test.com", password = "password123" });
        var login = await loginResp.Content.ReadFromJsonAsync<JsonElement>();
        var refreshToken = login.GetProperty("refresh_token").GetString()!;

        var resp = await client.PostAsJsonAsync("/api/auth/logout",
            new { refresh_token = refreshToken });

        resp.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }
}

// ── Quote endpoint tests ──────────────────────────────────────────────────────

public class QuoteEndpointTests : EndpointTestBase
{
    public QuoteEndpointTests(QuotesApiFactory factory) : base(factory) { }

    [Fact]
    public async Task GetQuotes_ReturnsOkWithPaginationShape()
    {
        using var client = AnonymousClient();
        var resp = await client.GetAsync("/api/quotes?page=1&size=5");

        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("data").ValueKind.Should().Be(JsonValueKind.Array);
        body.GetProperty("pagination").ValueKind.Should().Be(JsonValueKind.Object);
    }

    [Fact]
    public async Task GetQuotes_WithInvalidPage_Returns400()
    {
        using var client = AnonymousClient();
        var resp = await client.GetAsync("/api/quotes?page=0&size=10");

        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task GetQuoteById_WhenNotExists_Returns404()
    {
        using var client = AnonymousClient();
        var resp = await client.GetAsync("/api/quotes/99999");

        resp.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task CreateQuote_WhenAnonymous_Returns401()
    {
        using var client = AnonymousClient();
        var resp = await client.PostAsJsonAsync("/api/quotes",
            new { author = "Test", text = "Some text here" });

        resp.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task CreateQuote_WithoutScope_Returns403()
    {
        using var client = AuthenticatedClient(withScope: false);
        var resp = await client.PostAsJsonAsync("/api/quotes",
            new { author = "Test", text = "Some text here" });

        resp.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task CreateQuote_WithValidAuth_Returns201()
    {
        var ownerId = Guid.NewGuid();
        using var client = AuthenticatedClient(ownerId);
        var resp = await client.PostAsJsonAsync("/api/quotes",
            new { author = "Integration Author", text = "Integration test quote text here" });

        resp.StatusCode.Should().Be(HttpStatusCode.Created);
    }

    [Fact]
    public async Task CreateQuote_WithEmptyAuthor_Returns422()
    {
        using var client = AuthenticatedClient();
        var resp = await client.PostAsJsonAsync("/api/quotes",
            new { author = "", text = "Some valid text here" });

        resp.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
    }

    [Fact]
    public async Task GetQuoteById_AfterCreate_Returns200()
    {
        var ownerId = Guid.NewGuid();
        using var client = AuthenticatedClient(ownerId);
        var createResp = await client.PostAsJsonAsync("/api/quotes",
            new { author = "Get Test Author", text = "Some text here for retrieval" });
        var created = await createResp.Content.ReadFromJsonAsync<JsonElement>();
        var id = created.GetProperty("id").GetInt32();

        var getResp = await client.GetAsync($"/api/quotes/{id}");

        getResp.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task DeleteQuote_WhenAnonymous_Returns401()
    {
        using var client = AnonymousClient();
        var resp = await client.DeleteAsync("/api/quotes/1");

        resp.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task DeleteQuote_WhenNotOwner_Returns403()
    {
        var ownerId = Guid.NewGuid();
        using var createClient = AuthenticatedClient(ownerId);
        var createResp = await createClient.PostAsJsonAsync("/api/quotes",
            new { author = "Owner", text = "Owner text here for delete test" });
        createResp.StatusCode.Should().Be(HttpStatusCode.Created, "quote creation must succeed first");
        var created = await createResp.Content.ReadFromJsonAsync<JsonElement>();
        var id = created.GetProperty("id").GetInt32();

        using var otherClient = AuthenticatedClient(Guid.NewGuid()); // different user
        var resp = await otherClient.DeleteAsync($"/api/quotes/{id}");

        resp.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task DeleteQuote_WhenOwner_Returns204()
    {
        var ownerId = Guid.NewGuid();
        using var client = AuthenticatedClient(ownerId);
        var createResp = await client.PostAsJsonAsync("/api/quotes",
            new { author = "Delete Me", text = "This quote will be deleted now" });
        var created = await createResp.Content.ReadFromJsonAsync<JsonElement>();
        var id = created.GetProperty("id").GetInt32();

        var resp = await client.DeleteAsync($"/api/quotes/{id}");

        resp.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task DeleteQuote_WhenNotFound_Returns404()
    {
        using var client = AuthenticatedClient();
        var resp = await client.DeleteAsync("/api/quotes/99999");

        resp.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task ExpiredToken_Returns401()
    {
        using var client = Factory.CreateClient();
        var expiredToken = MakeToken(lifetimeSeconds: -1);
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", expiredToken);

        var resp = await client.PostAsJsonAsync("/api/quotes",
            new { author = "Test", text = "Some text here" });

        resp.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }
}

// ── Collection endpoint tests ─────────────────────────────────────────────────

public class CollectionEndpointTests : EndpointTestBase
{
    public CollectionEndpointTests(QuotesApiFactory factory) : base(factory) { }

    [Fact]
    public async Task CreateCollection_WhenAnonymous_Returns401()
    {
        using var client = AnonymousClient();
        var resp = await client.PostAsJsonAsync("/api/collections",
            new { name = "My Collection", ownerId = 1 });

        resp.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task CreateCollection_WithValidAuth_Returns201()
    {
        using var client = AuthenticatedClient();
        var resp = await client.PostAsJsonAsync("/api/collections",
            new { name = "My Collection", ownerId = 1 });

        resp.StatusCode.Should().Be(HttpStatusCode.Created);
    }

    [Fact]
    public async Task GetCollectionById_WhenNotExists_Returns404()
    {
        using var client = AnonymousClient();
        var resp = await client.GetAsync("/api/collections/99999");

        resp.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task GetCollectionById_AfterCreate_Returns200()
    {
        using var client = AuthenticatedClient();
        var createResp = await client.PostAsJsonAsync("/api/collections",
            new { name = "Fetched Collection", ownerId = 1 });
        var created = await createResp.Content.ReadFromJsonAsync<JsonElement>();
        var id = created.GetProperty("id").GetInt32();

        var getResp = await client.GetAsync($"/api/collections/{id}");

        getResp.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task AddItemToCollection_WhenExists_Returns200()
    {
        using var authorClient = AuthenticatedClient(Guid.NewGuid());
        var quoteResp = await authorClient.PostAsJsonAsync("/api/quotes",
            new { author = "Col Item Auth", text = "Text for collection item" });
        quoteResp.StatusCode.Should().Be(HttpStatusCode.Created, "quote must be created first");
        var quote = await quoteResp.Content.ReadFromJsonAsync<JsonElement>();
        var quoteId = quote.GetProperty("id").GetInt32();

        using var client = AuthenticatedClient();
        var colResp = await client.PostAsJsonAsync("/api/collections",
            new { name = "Item Collection", ownerId = 1 });
        colResp.StatusCode.Should().Be(HttpStatusCode.Created, "collection must be created first");
        var col = await colResp.Content.ReadFromJsonAsync<JsonElement>();
        var colId = col.GetProperty("id").GetInt32();

        var addResp = await client.PostAsJsonAsync($"/api/collections/{colId}/items",
            new { quoteId });

        addResp.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task AddItemToCollection_WhenNotFound_Returns404()
    {
        using var client = AuthenticatedClient();
        var resp = await client.PostAsJsonAsync("/api/collections/99999/items",
            new { quoteId = 1 });

        resp.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task RemoveItemFromCollection_WhenNotFound_Returns404()
    {
        using var client = AuthenticatedClient();
        var resp = await client.DeleteAsync("/api/collections/99999/items/1");

        resp.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task DeleteCollection_Returns204()
    {
        using var client = AuthenticatedClient();
        var createResp = await client.PostAsJsonAsync("/api/collections",
            new { name = "Delete Me Collection", ownerId = 1 });
        var created = await createResp.Content.ReadFromJsonAsync<JsonElement>();
        var id = created.GetProperty("id").GetInt32();

        var resp = await client.DeleteAsync($"/api/collections/{id}");

        resp.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task CreateCollection_WithInvalidName_Returns400ViaDomainException()
    {
        using var client = AuthenticatedClient();
        var resp = await client.PostAsJsonAsync("/api/collections",
            new { name = "AB", ownerId = 1 }); // Name < 3 chars triggers DomainException → 400

        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }
}
