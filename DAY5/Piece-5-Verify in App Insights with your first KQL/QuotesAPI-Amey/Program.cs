using QuotesApi.Authorization;
using QuotesApi.Configuration;
using QuotesApi.Data;
using QuotesApi.Extensions;
using QuotesApi.Middleware;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using System.Text;
using Serilog;
using Serilog.Context;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using Azure.Identity;
using Azure.Extensions.AspNetCore.Configuration.Secrets;
using Azure.Monitor.OpenTelemetry.AspNetCore;

var builder = WebApplication.CreateBuilder(args);

// Azure Key Vault — optional; skipped when running without Azure credentials locally
var keyVaultUrl = builder.Configuration["KeyVault:Url"];
if (!string.IsNullOrWhiteSpace(keyVaultUrl) &&
    Uri.TryCreate(keyVaultUrl, UriKind.Absolute, out var kvUri))
{
    try { builder.Configuration.AddAzureKeyVault(kvUri, new DefaultAzureCredential()); }
    catch { /* Key Vault unreachable in local dev — continue without it */ }
}

// Replace default Microsoft logger with Serilog — reads config from "Serilog" section in appsettings
builder.Host.UseSerilog((context, services, configuration) =>
    configuration
        .ReadFrom.Configuration(context.Configuration)
        .ReadFrom.Services(services)
        .Enrich.FromLogContext());

// Add services
builder.Services.AddInfrastructure(builder.Configuration);

// OpenTelemetry: ASP.NET Core + EF Core + outbound HTTP + console + OTLP export
// Azure Monitor is wired only when the connection string is present (prod/Azure envs)
var otelBuilder = builder.Services.AddOpenTelemetry();
var aiConnStr = builder.Configuration["application-insights-connectionstring1"];
if (!string.IsNullOrWhiteSpace(aiConnStr))
    otelBuilder.UseAzureMonitor(o => { o.ConnectionString = aiConnStr; });
otelBuilder
    .ConfigureResource(r => r.AddService("QuotesApi"))
    .WithTracing(t => t
        .AddAspNetCoreInstrumentation()
        .AddEntityFrameworkCoreInstrumentation()
        .AddHttpClientInstrumentation()
        .AddSource(QuotesApi.Services.AuthTokenService.ActivitySourceName)
        .AddOtlpExporter(o =>
        {
            o.Endpoint = new Uri(builder.Configuration["OpenTelemetry:OtlpEndpoint"] ?? "http://localhost:4317");
        })
        .AddConsoleExporter());

var jwtOpts = builder.Configuration.GetSection("Jwt").Get<JwtOptions>()
    ?? throw new InvalidOperationException("Jwt section not found in configuration");

if (string.IsNullOrEmpty(jwtOpts.Key))
    throw new InvalidOperationException("Jwt:Key not found in configuration");

if (Encoding.UTF8.GetByteCount(jwtOpts.Key) < 32)
    throw new InvalidOperationException("Jwt:Key must be at least 256 bits (32 UTF-8 bytes)");

var jwtKey = jwtOpts.Key;

var tenantId = builder.Configuration["EntraId:TenantId"]
    ?? throw new InvalidOperationException("EntraId:TenantId not found in configuration");
var clientId = builder.Configuration["EntraId:ClientId"]
    ?? throw new InvalidOperationException("EntraId:ClientId not found in configuration");

// Two named schemes + a policy scheme that picks between them based on issuer
const string InternalScheme = "Internal";
const string EntraScheme = "Entra";
const string MultiScheme = "MultiScheme";

builder.Services
    .AddAuthentication(MultiScheme)
    .AddPolicyScheme(MultiScheme, "Internal or Entra JWT", options =>
    {
        options.ForwardDefaultSelector = ctx =>
        {
            var authHeader = ctx.Request.Headers["Authorization"].FirstOrDefault();
            if (authHeader?.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase) == true)
            {
                var raw = authHeader["Bearer ".Length..].Trim();
                try
                {
                    // Peek at the issuer without full validation
                    var jwt = new JsonWebTokenHandler().ReadJsonWebToken(raw);
                    if (jwt.Issuer.Contains("login.microsoftonline.com", StringComparison.OrdinalIgnoreCase))
                        return EntraScheme;
                }
                catch { /* unparseable token — fall through to internal handler */ }
            }
            return InternalScheme;
        };
    })
    // ── Scheme 1: internal HS256 tokens (this API issues them via /api/auth/login) ──
    .AddJwtBearer(InternalScheme, options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtKey)),
            ValidateIssuer = false,
            ValidateAudience = false,
            ValidateLifetime = true,
            ClockSkew = TimeSpan.Zero
        };
    })
    // ── Scheme 2: Entra ID RS256 tokens (SPA / az CLI callers) ────────────────────
    .AddJwtBearer(EntraScheme, options =>
    {
        // Authority triggers OIDC discovery: fetches signing keys automatically
        options.Authority = $"https://login.microsoftonline.com/{tenantId}/v2.0";
        // Accept both bare client-id and api:// URI as audience
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidAudiences = [clientId, $"api://{clientId}"],
            ValidateLifetime = true,
            ClockSkew = TimeSpan.Zero
        };
    });

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

var app = builder.Build();

// Stamp every log line in a request with the ASP.NET Core TraceIdentifier
app.Use((ctx, next) =>
{
    using (LogContext.PushProperty("TraceId", ctx.TraceIdentifier))
        return next();
});

// Middleware
app.UseExceptionMiddleware();
app.UseHttpsRedirection();
app.UseAuthentication();
app.UseAuthorization();

// Apply migrations
app.ApplyMigrations();

// Map endpoints
app.MapQuoteEndpoints();

app.MapGet("/health", () => Results.Ok(new { status = "healthy", timestamp = DateTimeOffset.UtcNow }));

app.MapGet("/", () => Results.Ok(new
{
    app     = "QuotesAPI",
    version = "1.0",
    status  = "running",
    endpoints = new[]
    {
        "GET  /health",
        "GET  /api/quotes",
        "GET  /api/quotes/{id}",
        "POST /api/quotes  (requires Bearer token)",
        "POST /api/auth/login",
        "POST /api/auth/refresh",
        "POST /api/auth/logout"
    }
}));

app.Run();

// Expose to integration test assembly
public partial class Program { }
