# Day 4 – Piece 7: Configuration Done Right — Submission

## Exercise: IOptions Pattern for JwtOptions

### 1. JwtOptions class

```csharp
// Configuration/JwtOptions.cs
namespace QuotesApi.Configuration;

public record JwtOptions
{
    public string Key { get; init; } = string.Empty;
    public string Audience { get; init; } = string.Empty;
    public TimeSpan AccessTokenLifetime { get; init; } = TimeSpan.FromMinutes(15);
}
```

### 2. appsettings.json section

```json
"Jwt": {
  "Key": "ThinkSchoolDay2JwtSigningKey-UseAtLeast32Chars",
  "Audience": "",
  "AccessTokenLifetime": "00:15:00"
}
```

> **Secrets never go in config files.** The real signing key comes from Azure Key Vault (loaded at startup via `DefaultAzureCredential + AddAzureKeyVault`), which overrides the placeholder value above at runtime.
> Local dev uses `dotnet user-secrets set Jwt:Key <real-key>`.

### 3. DI registration

```csharp
// Extensions/ServiceCollectionExtensions.cs  — inside AddInfrastructure()
services.Configure<JwtOptions>(configuration.GetSection("Jwt"));
```

`IOptions<T>` is registered as a singleton automatically. `IOptionsSnapshot<T>` (scoped, re-reads on config change) would be used if hot-reload were needed.

### 4. Injecting in a service

```csharp
// Services/AuthTokenService.cs
public class AuthTokenService : IAuthTokenService
{
    private readonly IOptions<JwtOptions> _jwtOptions;

    public AuthTokenService(
        QuoteDbContext dbContext,
        IOptions<JwtOptions> jwtOptions,   // ← typed, no magic strings
        IClock clock,
        ILogger<AuthTokenService> logger)
    {
        _jwtOptions = jwtOptions;
        // ...
    }

    public async Task<TokenPair> IssueTokenPairAsync(User user, ...)
    {
        var accessLifetime = _jwtOptions.Value.AccessTokenLifetime;  // TimeSpan
        var accessToken = GenerateJwtToken(user, accessLifetime);
        // ...
    }

    private string GenerateJwtToken(User user, TimeSpan lifetime)
    {
        var key = new SymmetricSecurityKey(
            Encoding.UTF8.GetBytes(_jwtOptions.Value.Key));
        // ...
    }
}
```

`IConfiguration` was removed from the service entirely — no magic strings, no `GetValue<int?>` calls.

### 5. Pre-Build usage in Program.cs

Before `builder.Build()` is called, DI is not yet available. The options section is bound directly for startup validation:

```csharp
var jwtOpts = builder.Configuration.GetSection("Jwt").Get<JwtOptions>()
    ?? throw new InvalidOperationException("Jwt section not found in configuration");

if (string.IsNullOrEmpty(jwtOpts.Key))
    throw new InvalidOperationException("Jwt:Key not found in configuration");

if (Encoding.UTF8.GetByteCount(jwtOpts.Key) < 32)
    throw new InvalidOperationException("Jwt:Key must be at least 256 bits (32 UTF-8 bytes)");

var jwtKey = jwtOpts.Key;  // used for JwtBearer setup below
```

---

## What did you learn this session?

The IOptions pattern eliminates magic strings scattered across services. Instead of `_configuration["Jwt:Key"]` (a stringly-typed lookup that fails silently when the key is missing or renamed), you get a compile-time-typed `_jwtOptions.Value.Key`. The three variants — `IOptions<T>` (singleton snapshot), `IOptionsSnapshot<T>` (per-request re-read), `IOptionsMonitor<T>` (live change notifications for singletons) — map directly to the three DI lifetimes, which clicked immediately after the DI lifetimes exercise.

The key insight: `IConfiguration` is the raw bag of key-value pairs loaded from all sources. `IOptions<T>` is the strongly-typed view you bind to a section. Services should depend on the typed view, not the raw bag.

## What would break this?

1. **TimeSpan binding format mismatch** — if someone writes `"AccessTokenLifetime": 900` (an integer) instead of `"00:15:00"`, the binder throws a runtime exception because `int` cannot be converted to `TimeSpan`. Mitigation: add `ValidateDataAnnotations()` or `ValidateOnStart()` so the failure happens at startup, not on first token issuance.

2. **Missing Key Vault connection** — in production the placeholder key in `appsettings.json` is never 32 bytes. If Key Vault is unreachable (network policy, expired credential), the startup guard throws before the app listens — which is the correct behaviour (fail fast rather than issue tokens signed with a known-public placeholder key).

3. **IOptionsSnapshot in a singleton** — if someone accidentally registers a singleton service and injects `IOptionsSnapshot<JwtOptions>` instead of `IOptions<JwtOptions>`, the DI container throws at startup: "Cannot consume scoped service from singleton." Using `IOptionsMonitor<T>` would be the fix for a singleton that needs live updates.

4. **Audience left empty** — the `Audience` property defaults to `""`. The internal token handler has `ValidateAudience = false`, so this is safe now. If audience validation is ever turned on and the value is empty, every token will be rejected. A data-annotation `[Required]` on `Audience` (with `ValidateDataAnnotations()`) would catch this at startup.
