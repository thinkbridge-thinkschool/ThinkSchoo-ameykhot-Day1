# Day 3 — Piece 1: Wire Entra ID as Identity Provider
**Author:** Amey Khot | **Date:** 2026-05-21 | **API:** http://localhost:5100

---

## Azure App Registration

| Property  | Value                                  |
|-----------|----------------------------------------|
| Tenant ID | `0a0aa63d-82d0-4ba1-b909-d7986ece4c4c` |
| Client ID | `d2871fd9-8a9f-4838-b4f0-7deec1b73369` |
| Object ID | `4613141e-83e1-4eb1-a09a-994366dcc609` |
| Authority | `https://login.microsoftonline.com/0a0aa63d-82d0-4ba1-b909-d7986ece4c4c/v2.0` |
| Audience  | `api://d2871fd9-8a9f-4838-b4f0-7deec1b73369` |

---

## Program.cs — Auth Setup

```csharp
const string InternalScheme = "Internal";
const string EntraScheme    = "Entra";
const string MultiScheme    = "MultiScheme";

builder.Services
    .AddAuthentication(MultiScheme)

    // Policy scheme: peeks at issuer, routes to correct handler
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

    // Scheme 1: internal HS256 tokens issued by /api/auth/login
    .AddJwtBearer(InternalScheme, options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtKey)),
            ValidateIssuer   = false,
            ValidateAudience = false,
            ValidateLifetime = true,
            ClockSkew        = TimeSpan.Zero
        };
    })

    // Scheme 2: Entra ID RS256 tokens (SPA / az CLI callers)
    .AddJwtBearer(EntraScheme, options =>
    {
        // Authority triggers OIDC discovery — signing keys fetched automatically
        options.Authority = $"https://login.microsoftonline.com/{tenantId}/v2.0";
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidAudiences   = [clientId, $"api://{clientId}"],
            ValidateLifetime = true,
            ClockSkew        = TimeSpan.Zero
        };
    });
```

**appsettings.json**
```json
"EntraId": {
  "TenantId": "0a0aa63d-82d0-4ba1-b909-d7986ece4c4c",
  "ClientId": "d2871fd9-8a9f-4838-b4f0-7deec1b73369",
  "Audience": "api://d2871fd9-8a9f-4838-b4f0-7deec1b73369"
}
```

---

## All Test Results — Live Run 2026-05-21

---

### TEST 1 — Internal Login → JWT Issued

```
curl -s -X POST http://localhost:5100/api/auth/login \
  -H "Content-Type: application/json" \
  -d '{"email":"user@test.com","password":"password123"}'
```

**Response: 200 OK**
```json
{
  "access_token":  "eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9.eyJodHRwOi8vc2NoZW1hcy54bWxzb2FwLm9yZy93cy8yMDA1LzA1L2lkZW50aXR5L2NsYWltcy9uYW1laWRlbnRpZmllciI6IjY5NzNiOWU0LTEzNjMtNDFiMy1iOWY5LTljMTRmMWZmODdkMSIsImh0dHA6Ly9zY2hlbWFzLnhtbHNvYXAub3JnL3dzLzIwMDUvMDUvaWRlbnRpdHkvY2xhaW1zL2VtYWlsYWRkcmVzcyI6InVzZXJAdGVzdC5jb20iLCJleHAiOjE3NzkzNDgyOTl9.vDFuBWEM_tVas3W-UsiLVMeO5bjNDZU8rw62vOS8pYM",
  "refresh_token": "XBgUPvH3r7PLzztcBtwdPjce/YMBpLa1vTMEzh5/McIzI0GqXzl77T15QjCEhNwAT8+kDJ+oY15ZMhLAnyiIMA==",
  "expires_in":    900
}
```

---

### TEST 2 — Public Endpoint (No Auth Required)

```
curl -s http://localhost:5100/api/quotes
```

**Response: 200 OK**
```json
{
  "data": [...],
  "pagination": { "page": 1, "size": 10, "total": 6 }
}
```

---

### TEST 3 — Protected Endpoint WITHOUT Token → 401

```
curl -s -o /dev/null -w "%{http_code}" -X POST http://localhost:5100/api/quotes \
  -H "Content-Type: application/json" \
  -d '{"author":"X","text":"Y"}'
```

**Response: 401 Unauthorized**  
*(no Authorization header — middleware rejects before the handler runs)*

---

### TEST 4 — Protected Endpoint WITH Internal JWT → 201 Created

```
curl -s -X POST http://localhost:5100/api/quotes \
  -H "Content-Type: application/json" \
  -H "Authorization: Bearer eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9.eyJodHRwOi8vc2NoZW1hcy54bWxzb2FwLm9yZy93cy8yMDA1LzA1L2lkZW50aXR5L2NsYWltcy9uYW1laWRlbnRpZmllciI6IjY5NzNiOWU0LTEzNjMtNDFiMy1iOWY5LTljMTRmMWZmODdkMSIsImh0dHA6Ly9zY2hlbWFzLnhtbHNvYXAub3JnL3dzLzIwMDUvMDUvaWRlbnRpdHkvY2xhaW1zL2VtYWlsYWRkcmVzcyI6InVzZXJAdGVzdC5jb20iLCJleHAiOjE3NzkzNDgyOTl9.vDFuBWEM_tVas3W-UsiLVMeO5bjNDZU8rw62vOS8pYM" \
  -d '{"author":"Amey Khot","text":"Entra ID delegates identity so your API trusts signatures not passwords."}'
```

**Response: 201 Created**
```json
{
  "id":     7,
  "author": "Amey Khot",
  "text":   "Entra ID delegates identity so your API trusts signatures not passwords."
}
```

---

### TEST 5 — Refresh Token Rotation → New Pair Issued

```
curl -s -X POST http://localhost:5100/api/auth/refresh \
  -H "Content-Type: application/json" \
  -d '{"refresh_token":"XBgUPvH3r7PLzztcBtwdPjce/YMBpLa1vTMEzh5/McIzI0GqXzl77T15QjCEhNwAT8+kDJ+oY15ZMhLAnyiIMA=="}'
```

**Response: 200 OK**
```json
{
  "access_token":  "eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9.eyJodHRwOi8vc2NoZW1hcy54bWxzb2FwLm9yZy93cy8yMDA1LzA1L2lkZW50aXR5L2NsYWltcy9uYW1laWRlbnRpZmllciI6IjY5NzNiOWU0LTEzNjMtNDFiMy1iOWY5LTljMTRmMWZmODdkMSIsImh0dHA6Ly9zY2hlbWFzLnhtbHNvYXAub3JnL3dzLzIwMDUvMDUvaWRlbnRpdHkvY2xhaW1zL2VtYWlsYWRkcmVzcyI6InVzZXJAdGVzdC5jb20iLCJleHAiOjE3NzkzNDgzMDB9.77JCclvY-Vuw4oJVRrGFWYR7oj6x28lZ94YAw6OuDt0",
  "refresh_token": "<new_rotated_token>",
  "expires_in":    900
}
```
*Old refresh token is now permanently invalidated.*

---

### TEST 6 — Token Reuse Detection → 401

```
curl -s -X POST http://localhost:5100/api/auth/refresh \
  -H "Content-Type: application/json" \
  -d '{"refresh_token":"XBgUPvH3r7PLzztcBtwdPjce/YMBpLa1vTMEzh5/McIzI0GqXzl77T15QjCEhNwAT8+kDJ+oY15ZMhLAnyiIMA=="}'
```
*(same token replayed — already used in TEST 5)*

**Response: 401 Unauthorized**
```json
{
  "title":  "Unauthorized",
  "status": 401,
  "detail": "Token reuse detected. Please log in again."
}
```
*Entire token family revoked — attacker cannot use any token from this lineage.*

---

### TEST 7 — Post-Rotation Token Works → 201

```
curl -s -X POST http://localhost:5100/api/quotes \
  -H "Content-Type: application/json" \
  -H "Authorization: Bearer eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9.eyJodHRwOi8vc2NoZW1hcy54bWxzb2FwLm9yZy93cy8yMDA1LzA1L2lkZW50aXR5L2NsYWltcy9uYW1laWRlbnRpZmllciI6IjY5NzNiOWU0LTEzNjMtNDFiMy1iOWY5LTljMTRmMWZmODdkMSIsImh0dHA6Ly9zY2hlbWFzLnhtbHNvYXAub3JnL3dzLzIwMDUvMDUvaWRlbnRpdHkvY2xhaW1zL2VtYWlsYWRkcmVzcyI6InVzZXJAdGVzdC5jb20iLCJleHAiOjE3NzkzNDgzMDB9.77JCclvY-Vuw4oJVRrGFWYR7oj6x28lZ94YAw6OuDt0" \
  -d '{"author":"Post-Rotation","text":"New rotated token accepted."}'
```

**Response: 201 Created** — id=8 *(rotated access token is fully valid)*

---

### TEST 8 — Logout → 204 No Content

```
curl -s -o /dev/null -w "%{http_code}" -X POST http://localhost:5100/api/auth/logout \
  -H "Content-Type: application/json" \
  -d '{"refresh_token":"<rotated_refresh_token>"}'
```

**Response: 204 No Content** *(refresh token revoked in DB)*

---

### TEST 9 — PolicyScheme Routing Proof

**Decode the internal token header (base64):**
```
{"alg":"HS256","typ":"JWT"}
```
- `alg = HS256` → no Microsoft issuer → **PolicyScheme routes to [Internal]**
- Validated with HS256 symmetric key from `Jwt:Key` in appsettings.json

**Entra token JWT header looks like:**
```json
{"alg":"RS256","typ":"JWT","kid":"<azure-key-id>","x5t":"<thumbprint>"}
```
- `iss = https://login.microsoftonline.com/0a0aa63d-82d0-4ba1-b909-d7986ece4c4c/v2.0`
- → **PolicyScheme routes to [Entra]**
- RS256 public keys auto-fetched from OIDC discovery — no key config needed in code

---

### TEST 10 — Entra Token via Azure CLI

```bash
# Step 1 — get token from Entra ID
ENTRA_TOKEN=$(az account get-access-token \
  --resource api://d2871fd9-8a9f-4838-b4f0-7deec1b73369 \
  --query accessToken -o tsv)

# Step 2 — call the protected API
curl -s -X POST http://localhost:5100/api/quotes \
  -H "Content-Type: application/json" \
  -H "Authorization: Bearer $ENTRA_TOKEN" \
  -d '{"author":"Entra User","text":"Identity delegated to Microsoft."}'
```

**Expected Response: 201 Created**

**What happens:**
1. `PolicyScheme` peeks at token — issuer = `login.microsoftonline.com` → forwards to `[Entra]`
2. `[Entra]` handler calls OIDC discovery:
   `https://login.microsoftonline.com/0a0aa63d-82d0-4ba1-b909-d7986ece4c4c/v2.0/.well-known/openid-configuration`
3. Fetches Microsoft's RS256 public signing keys
4. Validates signature + audience `api://d2871fd9...` + lifetime
5. Returns **201 Created**

**PowerShell equivalent:**
```powershell
$t = az account get-access-token `
      --resource api://d2871fd9-8a9f-4838-b4f0-7deec1b73369 `
      --query accessToken -o tsv

Invoke-RestMethod http://localhost:5100/api/quotes -Method POST `
  -ContentType application/json `
  -Headers @{ Authorization = "Bearer $t" } `
  -Body '{"author":"Entra User","text":"Identity delegated to Microsoft."}'
```

---

## What I Learned This Session

**The thing that clicked:** `AddPolicyScheme` is just a lightweight router — it reads the raw JWT header without validating anything, extracts the `iss` claim, and decides which real bearer handler owns the validation. Two completely different trust models (HS256 symmetric key and RS256 Azure PKI) coexist on the same endpoint with zero changes to `[Authorize]` attributes or route handlers.

The broader idea I'll keep: **delegate identity outward**. Self-hosted JWT means you own the crypto, key rotation, and storage. Entra ID means Microsoft owns all of that — you only trust a well-known HTTPS discovery URL. For customer-facing APIs, that tradeoff is almost always correct.

---

## What Would Break This

1. **App not configured as API in Azure portal** — no "Expose an API" scope means the token comes back with the wrong audience → `IDX10214: Audience validation failed`

2. **Wrong TenantId in Authority** — OIDC discovery URL 404s → all Entra tokens rejected

3. **Multi-tenant apps** — `ValidateIssuer = true` (default when Authority is set) rejects tokens from any tenant other than yours; set `ValidateIssuer = false` and validate `tid` claim manually

4. **Clock skew** — `ClockSkew = TimeSpan.Zero` is strict; Entra tokens can have ~5 minutes of real-world drift; consider `TimeSpan.FromMinutes(2)` for the Entra scheme in production

5. **Internal key leak** — if `Jwt:Key` is compromised, an attacker can mint arbitrary internal tokens; Entra tokens cannot be forged without Microsoft's private key — the core reason to move customer-facing callers to Entra
