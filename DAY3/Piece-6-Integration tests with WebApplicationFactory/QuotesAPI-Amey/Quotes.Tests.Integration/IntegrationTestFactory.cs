using System.IdentityModel.Tokens.Jwt;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.IdentityModel.Tokens;

namespace Quotes.Tests.Integration;

// One factory = one isolated SQLite file.
// xUnit constructs a new test-class instance per test method, so putting
// the factory in the constructor gives each test its own clean database.
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
        bool withScope = true,
        Guid? userId = null,
        int lifetimeSeconds = 3600)
    {
        var client = CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", MakeToken(userId, withScope, lifetimeSeconds));
        return client;
    }

    public static string MakeToken(
        Guid? userId = null,
        bool withScope = true,
        int lifetimeSeconds = 3600)
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
            claims:             claims,
            expires:            DateTime.UtcNow.AddSeconds(lifetimeSeconds),
            signingCredentials: creds);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}
