using System.IdentityModel.Tokens.Jwt;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.IdentityModel.Tokens;

namespace Quotes.Tests.Integration;

// One factory = one isolated SQL Server database (unique name per instance).
// xUnit constructs a new test-class instance per test method, so putting
// the factory in the constructor gives each test its own clean database
// on the shared SQL Server container.
public sealed class IntegrationTestFactory : WebApplicationFactory<Program>
{
    public const string TestJwtKey = "ThinkSchoolDay2JwtSigningKey-UseAtLeast32Chars";

    private readonly string _connectionString;

    public IntegrationTestFactory(string masterConnectionString)
    {
        // Stamp a unique catalog name so every factory instance gets its own DB.
        var csb = new SqlConnectionStringBuilder(masterConnectionString)
        {
            InitialCatalog = $"QuotesTest_{Guid.NewGuid():N}"
        };
        _connectionString = csb.ConnectionString;
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        // ConfigureAppConfiguration runs before AddInfrastructure, so setting
        // "DatabaseProvider" = "SqlServer" here causes AddInfrastructure to call
        // UseSqlServer instead of UseSqlite — only ONE provider is ever registered.
        builder.ConfigureAppConfiguration((_, cfg) =>
        {
            cfg.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:DefaultConnection"] = _connectionString,
                ["DatabaseProvider"]                   = "SqlServer",
                ["Jwt:Key"]                            = TestJwtKey,
                ["Jwt:AccessTokenLifetime"]            = "00:15:00",
                // Fake Entra values — OIDC discovery is lazy so tests never hit it
                ["EntraId:TenantId"] = "00000000-0000-0000-0000-000000000000",
                ["EntraId:ClientId"] = "00000000-0000-0000-0000-000000000001"
            });
        });
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
