# Day 3 Piece 1 — Wire Entra ID as Identity Provider

**Author:** Amey Khot  
**Date:** 2026-05-21

---

## Azure App Registration Details

| Property   | Value                                  |
|------------|----------------------------------------|
| Tenant ID  | `0a0aa63d-82d0-4ba1-b909-d7986ece4c4c` |
| Client ID  | `d2871fd9-8a9f-4838-b4f0-7deec1b73369` |
| Object ID  | `4613141e-83e1-4eb1-a09a-994366dcc609` |
| Authority  | `https://login.microsoftonline.com/0a0aa63d-82d0-4ba1-b909-d7986ece4c4c/v2.0` |
| Audience   | `api://d2871fd9-8a9f-4838-b4f0-7deec1b73369` or bare client ID |

---

## Program.cs — Auth Setup

The key change is replacing the single `AddJwtBearer(...)` call with **two named schemes** plus a **policy scheme** that inspects the token's issuer and forwards to the right handler.

```csharp
const string InternalScheme = "Internal";
const string EntraScheme = "Entra";
const string MultiScheme = "MultiScheme";

builder.Services
    .AddAuthentication(MultiScheme)
    // ── Policy scheme: peeks at issuer, forwards to correct handler ──
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
                    var jwt = new JsonWebTokenHandler().ReadJsonWebToken(raw);
                    if (jwt.Issuer.Contains("login.microsoftonline.com", StringComparison.OrdinalIgnoreCase))
                        return EntraScheme;
                }
                catch { }
            }
            return InternalScheme;
        };
    })
    // ── Scheme 1: internal HS256 tokens issued by /api/auth/login ──
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
    // ── Scheme 2: Entra ID RS256 tokens (SPA / az CLI callers) ──
    .AddJwtBearer(EntraScheme, options =>
    {
        // Authority triggers OIDC discovery — signing keys fetched automatically
        options.Authority = $"https://login.microsoftonline.com/{tenantId}/v2.0";
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidAudiences = [clientId, $"api://{clientId}"],
            ValidateLifetime = true,
            ClockSkew = TimeSpan.Zero
        };
    });
```

**appsettings.json addition:**
```json
"EntraId": {
  "TenantId": "0a0aa63d-82d0-4ba1-b909-d7986ece4c4c",
  "ClientId": "d2871fd9-8a9f-4838-b4f0-7deec1b73369",
  "Audience": "api://d2871fd9-8a9f-4838-b4f0-7deec1b73369"
}
```

---

## Test Results (Visible in VS Terminal)

All tests run against `http://localhost:5100`.

### Test 1 — Internal Login
```
POST /api/auth/login
Body: {"email":"user@test.com","password":"password123"}
→ 200 OK  access_token: eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9...
          expires_in: 900
```

### Test 2 — Public endpoint (no auth)
```
GET /api/quotes
→ 200 OK  total=2
```

### Test 3 — Protected endpoint, no token → 401
```
POST /api/quotes (no Authorization header)
→ 401 Unauthorized  ✓
```

### Test 4 — Protected endpoint with internal JWT → 201
```
POST /api/quotes
Authorization: Bearer eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9...
Body: {"author":"Amey Khot","text":"Entra ID delegates identity so your API trusts signatures, not passwords."}
→ 201 Created  id=4, author=Amey Khot  ✓
```

### Test 5 — Refresh token rotation
```
POST /api/auth/refresh  (valid refresh_token)
→ 200 OK  new access_token and refresh_token issued  ✓
```

### Test 6 — Token reuse detection
```
POST /api/auth/refresh  (same refresh_token reused)
→ 401  "Token reuse detected. Please log in again."  ✓
```

### Test 7 — PolicyScheme routing proof
```
Internal token header: {"alg":"HS256","typ":"JWT"}
→ alg=HS256, no MS issuer  → forwarded to [Internal] scheme  ✓

Entra token header:    {"alg":"RS256","kid":"...","x5t":"..."}
→ issuer = "https://login.microsoftonline.com/..."
→ forwarded to [Entra] scheme  ✓
```

---

## Curl — Testing with Entra-issued Token

### Step 1: Get an Entra access token via Azure CLI
```bash
az login   # if not already authenticated

ENTRA_TOKEN=$(az account get-access-token \
  --resource api://d2871fd9-8a9f-4838-b4f0-7deec1b73369 \
  --query accessToken -o tsv)
```

### Step 2: Hit the protected API with that token
```bash
# Create a quote using Entra-issued token
curl -s -X POST http://localhost:5100/api/quotes \
  -H "Content-Type: application/json" \
  -H "Authorization: Bearer $ENTRA_TOKEN" \
  -d '{"author":"Entra User","text":"Identity is now someone else problem."}' | jq .
```

Expected response:
```json
{
  "id": 5,
  "author": "Entra User",
  "text": "Identity is now someone else problem.",
  "createdAt": "2026-05-21T..."
}
```

The API accepts this because:
1. `PolicyScheme` peeks at the token, sees issuer = `https://login.microsoftonline.com/...`
2. Forwards to `EntraScheme` handler
3. Handler calls OIDC discovery at `https://login.microsoftonline.com/{tenantId}/v2.0/.well-known/openid-configuration`
4. Fetches Microsoft's public RS256 signing keys
5. Validates signature + audience + lifetime — passes

### PowerShell equivalent
```powershell
$entraToken = az account get-access-token `
  --resource api://d2871fd9-8a9f-4838-b4f0-7deec1b73369 `
  --query accessToken -o tsv

$headers = @{ Authorization = "Bearer $entraToken" }
Invoke-RestMethod -Uri "http://localhost:5100/api/quotes" `
  -Method POST -ContentType "application/json" -Headers $headers `
  -Body '{"author":"Entra User","text":"Identity is now someone elses problem."}'
```

---

## What I Learned This Session

**The thing that clicked:** `AddPolicyScheme` is just a lightweight router — it doesn't validate anything itself. It reads the raw JWT header (no signature check), extracts the `iss` claim, and decides which real bearer handler should own the validation. This means you can run two completely different trust models (symmetric HS256 key + asymmetric RS256 PKI) on the same endpoint without any conditional logic in the route handlers. `[Authorize]` stays untouched.

The broader idea I'll keep: **delegate identity work outward**. Self-hosted JWT means you own the crypto, the rotation, the storage of keys. Entra ID means Microsoft owns all of that, and you only trust a well-known HTTPS discovery URL. For anything customer-facing, that tradeoff is almost always worth it.

---

## What Would Break This

1. **Entra app not configured as API** — if the app registration has no "Expose an API" scope, `az account get-access-token --resource api://...` returns a token with the wrong audience and validation fails with `IDX10214: Audience validation failed`.

2. **Wrong tenant in Authority** — if `TenantId` is wrong, the OIDC discovery URL 404s at startup (or first request) and all Entra tokens are rejected with a metadata fetch error.

3. **Multi-tenant apps** — `ValidateIssuer = true` (the default when Authority is set) rejects tokens from any tenant other than yours. For a multi-tenant app you'd set `ValidateIssuer = false` and validate the tenant yourself from the `tid` claim.

4. **Clock drift on the token client** — `ClockSkew = TimeSpan.Zero` means a token that expired 1 second ago is rejected immediately. This is intentionally strict for internal tokens, but Entra tokens can have up to 5 minutes of clock drift between client and server in practice — consider `ClockSkew = TimeSpan.FromMinutes(2)` for the Entra scheme in production.

5. **The internal key leaks** — if `Jwt:Key` is compromised, an attacker can mint arbitrary internal tokens. Entra tokens can't be forged without Microsoft's private key. This asymmetry is the core reason to move customer-facing callers to Entra.

6. **`[Authorize]` without a scheme** — the policy scheme (`MultiScheme`) is registered as the default, so unqualified `[Authorize]` works for both token types. If you later add `[Authorize(AuthenticationSchemes = "Entra")]` to lock a specific route to Entra-only, internal tokens on that route will be rejected (which may be intentional for high-privilege endpoints).
