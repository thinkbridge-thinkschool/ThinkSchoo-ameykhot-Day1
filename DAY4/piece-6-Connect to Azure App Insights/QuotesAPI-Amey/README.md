# QuotesAPI - ASP.NET Core 10 Minimal API

A modern ASP.NET Core 10 API for managing quotes and collections, built with **Domain-Driven Design (DDD)** principles, **Entity Framework Core**, and **FluentValidation**.

---

## Day 4 – Piece 6: Azure Application Insights via OpenTelemetry

Connected the existing OpenTelemetry pipeline to **Azure Application Insights** using `Azure.Monitor.OpenTelemetry.AspNetCore`. The App Insights connection string is stored in **Azure Key Vault** and loaded at startup via `DefaultAzureCredential` — no secrets in config files or environment variables.

### Packages

| Package | Version | Purpose |
|---------|---------|---------|
| `Azure.Identity` | 1.13.2 | `DefaultAzureCredential` — picks correct auth per environment (Azure CLI in dev, Managed Identity in prod) |
| `Azure.Extensions.AspNetCore.Configuration.Secrets` | 1.3.2 | Key Vault config provider (`AddAzureKeyVault`) |
| `Azure.Monitor.OpenTelemetry.AspNetCore` | 1.3.0 | `UseAzureMonitor()` — routes traces, metrics, and logs to App Insights |

### How it works

**`Program.cs`** — Key Vault is loaded first so all downstream services see the secrets:

```csharp
// 1. Pull secrets from Key Vault before anything else
var keyVaultUrl = builder.Configuration["KeyVault:Url"]
    ?? "https://quotes-api-keyvault1.vault.azure.net/";
builder.Configuration.AddAzureKeyVault(
    new Uri(keyVaultUrl),
    new DefaultAzureCredential());

// 2. Wire App Insights into the existing OTel pipeline
builder.Services.AddOpenTelemetry()
    .UseAzureMonitor(o =>
    {
        o.ConnectionString = builder.Configuration["application-insights-connectionstring1"];
    })
    .ConfigureResource(r => r.AddService("QuotesApi"))
    .WithTracing(t => t
        .AddAspNetCoreInstrumentation()
        .AddEntityFrameworkCoreInstrumentation()
        .AddHttpClientInstrumentation()
        .AddSource(QuotesApi.Services.AuthTokenService.ActivitySourceName)
        .AddOtlpExporter(o =>
        {
            o.Endpoint = new Uri(builder.Configuration["OpenTelemetry:OtlpEndpoint"] ?? "http://localhost:4317");
        }));
```

**`appsettings.json`** — Key Vault URL (not the secret itself):

```json
"KeyVault": {
  "Url": "https://quotes-api-keyvault1.vault.azure.net/"
}
```

**`Services/AuthTokenService.cs`** — custom spans on the happy path now set `Ok` status and tag the token family, making them filterable in App Insights Transaction Search:

```csharp
// success path
activity?.SetTag("user.id", existing.UserId.ToString());
activity?.SetTag("token.family", existing.Family);
activity?.SetStatus(ActivityStatusCode.Ok);

// failure path (reuse detected)
activity?.SetTag("security.reuse_detected", true);
activity?.SetStatus(ActivityStatusCode.Error, "Token reuse detected");
```

### Signal flow

```
HTTP request → OpenTelemetry SDK
                 ├── OTLP exporter  → Jaeger (local dev)
                 └── Azure Monitor  → App Insights (all envs)
                       ├── requests table   (ASP.NET Core spans)
                       ├── dependencies table (EF Core + HTTP + custom spans)
                       └── traces table     (W3C TraceContext)
```

### Azure resources

| Resource | Value |
|----------|-------|
| App Insights | Southeast Asia region |
| Key Vault | `https://quotes-api-keyvault1.vault.azure.net/` |
| Secret name | `application-insights-connectionstring1` |

See [SOLUTION.md](SOLUTION.md) for the full submission including KQL queries, what I learned, and what would break this.

---

## Day 4 – Piece 5: OpenTelemetry Tracing

Added distributed tracing via **OpenTelemetry** with automatic instrumentation for every ASP.NET Core request, every EF Core query, and every outbound HTTP call — plus custom spans for non-trivial business operations.

### Packages

| Package | Version | Purpose |
|---------|---------|---------|
| `OpenTelemetry.Extensions.Hosting` | 1.15.3 | Core DI integration (`AddOpenTelemetry()`) |
| `OpenTelemetry.Instrumentation.AspNetCore` | 1.15.2 | Auto-span per HTTP request |
| `OpenTelemetry.Instrumentation.EntityFrameworkCore` | 1.15.1-beta.1 | Child span per EF Core database query |
| `OpenTelemetry.Instrumentation.Http` | 1.15.1 | Child span per outbound `HttpClient` call |
| `OpenTelemetry.Exporter.OpenTelemetryProtocol` | 1.15.3 | OTLP gRPC export (Jaeger / Aspire dashboard) |

### How it works

**`Program.cs`** — one call wires up all instrumentation and the OTLP exporter:

```csharp
builder.Services.AddOpenTelemetry()
    .ConfigureResource(r => r.AddService("QuotesApi"))
    .WithTracing(t => t
        .AddAspNetCoreInstrumentation()
        .AddEntityFrameworkCoreInstrumentation()
        .AddHttpClientInstrumentation()
        .AddSource(QuotesApi.Services.AuthTokenService.ActivitySourceName)
        .AddOtlpExporter(o =>
        {
            o.Endpoint = new Uri(builder.Configuration["OpenTelemetry:OtlpEndpoint"] ?? "http://localhost:4317");
        }));
```

**`Services/AuthTokenService.cs`** — custom spans for token issuance and refresh (not covered by auto-instrumentation):

```csharp
public const string ActivitySourceName = "QuotesApi.AuthTokenService";
private static readonly ActivitySource _activitySource = new(ActivitySourceName);

// IssueTokenPairAsync:
using var activity = _activitySource.StartActivity("issue-token-pair");
activity?.SetTag("user.id", user.Id.ToString());
activity?.SetTag("token.lifetime_seconds", accessLifetimeSeconds);

// RefreshAsync — marks security events on the span:
activity?.SetTag("security.reuse_detected", true);
activity?.SetStatus(ActivityStatusCode.Error, "Token reuse detected");
```

**`appsettings.json`** — OTLP endpoint (override per environment):

```json
"OpenTelemetry": {
  "OtlpEndpoint": "http://localhost:4317"
}
```

### Trace structure for `POST /api/auth/login`

```
POST /api/auth/login                               ← AspNetCore auto-span
  └── SELECT ... FROM Users WHERE Email=?          ← EF Core auto-span
  └── issue-token-pair                             ← custom span
        user.id = "9bb72369-..."
        token.lifetime_seconds = 900
        └── INSERT INTO RefreshTokens ...          ← EF Core auto-span
```

### Log ↔ Trace correlation

The W3C TraceId from `Activity.Current` is the same value Serilog emits in `{TraceId}`. Logs and traces correlate automatically — paste the TraceId from a log line into Jaeger's search to jump directly to the trace.

See [SOLUTION.md](SOLUTION.md) for the full submission including how to run Jaeger locally, what I learned, and what would break this.

---

## Day 4 – Piece 4: Serilog with Correlation IDs

Replaced the default Microsoft.Extensions.Logging provider with **Serilog**. Every log line produced during an HTTP request carries a `TraceId` property that links all log entries for that request — across layers (endpoint handler, repository, auth service, exception middleware) — into a single correlated trace.

### Packages

| Package | Version | Purpose |
|---------|---------|---------|
| `Serilog.AspNetCore` | 10.0.0 | Core integration — reads config from `appsettings.json`, bridges `ILogger<T>` |
| `Serilog.Sinks.Console` | 6.1.1 | Structured console output |

### How it works

**`Program.cs`** — Serilog replaces the default logger and the correlation middleware stamps every request:

```csharp
builder.Host.UseSerilog((context, services, configuration) =>
    configuration
        .ReadFrom.Configuration(context.Configuration)
        .ReadFrom.Services(services)
        .Enrich.FromLogContext());

// In the middleware pipeline — must be FIRST:
app.Use((ctx, next) =>
{
    using (LogContext.PushProperty("TraceId", ctx.TraceIdentifier))
        return next();
});
```

**`appsettings.json`** — log levels per category:

```json
"Serilog": {
  "MinimumLevel": {
    "Default": "Information",
    "Override": {
      "Microsoft.AspNetCore": "Warning",
      "Microsoft.EntityFrameworkCore.Database.Command": "Warning",
      "System": "Warning"
    }
  },
  "WriteTo": [{
    "Name": "Console",
    "Args": {
      "outputTemplate": "[{Timestamp:HH:mm:ss} {Level:u3}] [{TraceId}] {SourceContext}: {Message:lj}{NewLine}{Exception}"
    }
  }]
}
```

**`appsettings.Development.json`** — EF Core SQL and own code go to `Debug` in dev, `Warning` in prod.

### Five correlated log lines from one `POST /api/quotes` request

All share TraceId `ed069b9899766ad06ccd63b542423cad`:

```
[14:47:32 INF] [ed069b9899766ad06ccd63b542423cad] Program: Received CreateQuote request for author Marcus Aurelius
[14:47:32 INF] [ed069b9899766ad06ccd63b542423cad] Program: Validation passed for author Marcus Aurelius — building quote entity
[14:47:32 INF] [ed069b9899766ad06ccd63b542423cad] Program: Assigned OwnerId 9bb72369-5bff-47a4-8e58-365edf9e4491 to new quote
[14:47:32 INF] [ed069b9899766ad06ccd63b542423cad] QuotesApi.Data.QuoteRepository: Creating quote by Marcus Aurelius
[14:47:32 INF] [ed069b9899766ad06ccd63b542423cad] Program: Created quote 2 by author Marcus Aurelius for user 9bb72369-5bff-47a4-8e58-365edf9e4491
```

See [SOLUTION.md](SOLUTION.md) for the full submission including what I learned and what would break this.

---

## Day 4 – Piece 2: 80% Coverage Achievement

**Coverage results** (migrations excluded):

| Module    | Line  | Branch | Method |
|-----------|-------|--------|--------|
| QuotesApi | 94.8% | 82.8%  | 100%   |

**87 tests** passing across: unit tests (validators, factories, models, token service), repository tests (InMemory DB), middleware tests (direct invocation), authorization handler tests, and WebApplicationFactory endpoint integration tests.

### How to run with coverage

```bash
cd Quotes.Tests.Unit
dotnet test \
  -p:CollectCoverage=true \
  -p:CoverletOutputFormat=cobertura \
  "-p:ExcludeByFile=**/Migrations/**" \
  -p:CoverletOutput="../coverage.xml" \
  -p:Threshold=80 \
  -p:ThresholdType=line
```

### What tests were added

| File | Tests | What's covered |
|------|-------|----------------|
| `Quotes.Tests.Unit/RepositoryTests.cs` | 13 | QuoteRepository CRUD + delete-not-found branch; CollectionRepository add/get/update/delete |
| `Quotes.Tests.Unit/ExceptionMiddlewareTests.cs` | 4 | No exception, DomainException → 400, ArgumentException → 400, generic → 500 |
| `Quotes.Tests.Unit/AuthorizationHandlerTests.cs` | 4 | Owner match, owner mismatch, no owner on quote, no claim on principal |
| `Quotes.Tests.Unit/EndpointTests.cs` | 29 | All auth, quote, and collection endpoints via WebApplicationFactory |

---

## 🚀 What You Can Do

### 1. **Manage Quotes**
- Create new quotes with author and text
- Retrieve paginated quotes
- Get a specific quote by ID
- Delete quotes

### 2. **Create Collections**
- Create personal quote collections (name 3-80 characters)
- View collection details with items
- Rename collections
- Delete entire collections

### 3. **Manage Collection Items**
- Add quotes to collections (max 50 items per collection)
- Prevent duplicate quotes in same collection (domain validation)
- Remove quotes from collections
- View all items in a collection with timestamps

### 4. **Domain-Driven Design Features**
- Aggregate root pattern with Collection as root
- Value objects (CollectionItem)
- Domain exceptions for business rule violations
- Automatic 400 BadRequest responses for domain violations
- All invariants enforced at domain layer, not API layer

---

## 📋 API Endpoints

### **Quotes** 

#### Get All Quotes (Paginated)
```bash
GET /api/quotes?page=1&size=10
```
**Response:**
```json
{
  "data": [
    {
      "id": 1,
      "author": "Albert Einstein",
      "text": "Imagination is more important than knowledge."
    }
  ],
  "pagination": {
    "page": 1,
    "size": 10,
    "total": 25
  }
}
```

#### Create Quote
```bash
POST /api/quotes
Content-Type: application/json

{
  "author": "Steve Jobs",
  "text": "The only way to do great work is to love what you do."
}
```
**Response:** `201 Created`

#### Get Quote by ID
```bash
GET /api/quotes/1
```

#### Delete Quote
```bash
DELETE /api/quotes/1
```
**Response:** `204 No Content`

---

### **Collections**

#### Create Collection
```bash
POST /api/collections
Content-Type: application/json

{
  "name": "My Favourites",
  "ownerId": 1
}
```
**Response:** `201 Created`
```json
{
  "id": 1,
  "name": "My Favourites",
  "ownerId": 1,
  "items": []
}
```

#### Get Collection by ID
```bash
GET /api/collections/1
```

#### Add Quote to Collection
```bash
POST /api/collections/1/items
Content-Type: application/json

{
  "quoteId": 1
}
```
**Response:** `200 OK`
```json
{
  "id": 1,
  "name": "My Favourites",
  "ownerId": 1,
  "items": [
    {
      "quoteId": 1,
      "addedAt": "2026-05-19T11:27:09.2267085Z"
    }
  ]
}
```

#### ❌ Add Duplicate Quote (Domain Validation)
```bash
POST /api/collections/1/items
Content-Type: application/json

{
  "quoteId": 1
}  # This quote already exists in collection
```
**Response:** `400 Bad Request`
```json
{
  "type": "https://tools.ietf.org/html/rfc7231#section-6.6.1",
  "title": "Domain rule violation.",
  "status": 400,
  "detail": "Quote 1 is already in this collection.",
  "instance": "/api/collections/1/items"
}
```

#### Remove Quote from Collection
```bash
DELETE /api/collections/1/items/1
```
**Response:** `200 OK`

#### Delete Collection
```bash
DELETE /api/collections/1
```
**Response:** `204 No Content`

---

## 🛠️ How to Run

### Prerequisites
- .NET 10 SDK
- SQLite (included with EF Core)

### Terminal Commands (Run in Order)

**Step 1: Create the EF Core migration for Collections table**
```bash
dotnet ef migrations add AddCollectionAggregate
```

**Step 2: Verify the project builds**
```bash
dotnet build
```

**Expected Output:**
```
Build succeeded in 10.3s
```

**Step 3: Run the application**
```bash
dotnet run
```

**Expected Output:**
```
info: Program[0]
      Applying EF Core migrations...
info: Program[0]
      Migrations applied successfully
info: Microsoft.Hosting.Lifetime[14]
      Now listening on: http://localhost:5000
info: Microsoft.Hosting.Lifetime[0]
      Application started. Press Ctrl+C to exit.
```

Server will start at: `http://localhost:5000`

### Alternative Setup Steps

### 1. Restore Dependencies
```bash
dotnet restore
```

### 2. Build the Project
```bash
dotnet build
```

### 3. Run the Application
```bash
dotnet run
```

---

## Dependency Injection Lifetimes Exercise

This project includes a DI-focused exercise with explicit lifetimes and abstractions:

- `IClock` is registered as `Singleton` via `SystemClock`
- `IQuoteFactory` is registered as `Scoped` and consumes `IClock`
- Quote creation endpoints resolve `IQuoteFactory` and `IClock` through DI
- Quote timestamps are passed as parameters to domain constructors

Run tests to validate the behavior:

```bash
dotnet test
```

---

## 🧪 Testing with PowerShell

### Create a Collection
```powershell
$response = Invoke-RestMethod -Uri "http://localhost:5000/api/collections" `
  -Method Post `
  -Headers @{"Content-Type"="application/json"} `
  -Body '{"name":"My Favorites","ownerId":1}'

$response | ConvertTo-Json
```

### Add a Quote to Collection (First Time - Succeeds)
```powershell
$response = Invoke-RestMethod -Uri "http://localhost:5000/api/collections/1/items" `
  -Method Post `
  -Headers @{"Content-Type"="application/json"} `
  -Body '{"quoteId":1}'

$response | ConvertTo-Json
```

### Add Same Quote Again (Should Fail - Domain Violation)
```powershell
Try {
  Invoke-RestMethod -Uri "http://localhost:5000/api/collections/1/items" `
    -Method Post `
    -Headers @{"Content-Type"="application/json"} `
    -Body '{"quoteId":1}'
} Catch {
  $stream = $_.Exception.Response.GetResponseStream()
  $reader = New-Object System.IO.StreamReader($stream)
  $body = $reader.ReadToEnd()
  $body | ConvertFrom-Json | ConvertTo-Json
}
```

---

## ⚡ Key Test Cases (What to Watch For)

### ✅ Success Case: Add First Quote to Collection
```bash
curl -s -X POST http://localhost:5000/api/collections/1/items \
  -H "Content-Type: application/json" \
  -d '{"quoteId": 1}'
```
**Status Code:** `200 OK` ✅
- Quote successfully added to collection
- Items array shows the quote with timestamp

### ❌ Domain Validation Case: Add Duplicate Quote
```bash
curl -s -X POST http://localhost:5000/api/collections/1/items \
  -H "Content-Type: application/json" \
  -d '{"quoteId": 1}'
```
**Status Code:** `400 Bad Request` ⚠️
- DomainException caught by ExceptionMiddleware
- Returns problem details with specific error message
- **This demonstrates DDD invariant enforcement**

### ⚡ Other Domain Validations to Test

**Try adding to collection with invalid name (less than 3 chars):**
```bash
curl -s -X POST http://localhost:5000/api/collections \
  -H "Content-Type: application/json" \
  -d '{"name": "AB", "ownerId": 1}'
```
**Expected:** `400 Bad Request` - "Collection name must be between 3 and 80 characters."

**Try adding more than 50 items to a collection:**

---

## Authorization Policies and Claims (Day 3 — Piece 2)

This API now enforces **policy-based authorization** for quote mutations. Authentication answers *who*; policies answer *can they*.

### Two policies

#### 1. Claim-based: `can-edit-quotes`

Registered in `Program.cs`:

```csharp
options.AddPolicy("can-edit-quotes", p => p.RequireClaim("scope", "quotes.write"));
```

- `POST /api/quotes` and `DELETE /api/quotes/{id}` both require this policy via `.RequireAuthorization("can-edit-quotes")`.
- Every token issued by `/api/auth/login` carries `scope: quotes.write`, so any authenticated user satisfies it.
- A request with no token or a token missing the `scope` claim returns **401/403**.

#### 2. Custom `IAuthorizationRequirement`: `quote-owner`

```csharp
options.AddPolicy("quote-owner", p =>
    p.RequireClaim("scope", "quotes.write")
     .AddRequirements(new QuoteOwnerRequirement()));
```

- `QuoteOwnerRequirement` (marker class) + `QuoteOwnerAuthorizationHandler` (resolves from DI).
- The `DELETE /api/quotes/{id}` endpoint loads the quote, then calls:
  ```csharp
  var result = await authorizationService.AuthorizeAsync(httpContext.User, quote, "quote-owner");
  if (!result.Succeeded) return Results.Forbid();
  ```
- The handler compares the `NameIdentifier` claim against `quote.OwnerId`. If they don't match → **403 Forbidden**.

### What was added

| File | Change |
|---|---|
| `Models/Quote.cs` | Added `OwnerId` (`Guid?`) property |
| `Authorization/QuoteOwnerRequirement.cs` | New — marker requirement |
| `Authorization/QuoteOwnerAuthorizationHandler.cs` | New — resource-based handler comparing user ID to quote owner |
| `Services/AuthTokenService.cs` | Added `scope: quotes.write` claim to every issued JWT |
| `Program.cs` | Replaced `AddAuthorization()` with two named policies; registered `QuoteOwnerAuthorizationHandler` in DI |
| `Extensions/ServiceCollectionExtensions.cs` | `CreateQuote` stamps `OwnerId` from claims; `DeleteQuote` does resource-based auth check |
| `QuotesApi.Tests/AuthorizationPolicyTests.cs` | New — 5 tests covering both policy pass and fail cases |
| `Migrations/` | `AddOwnerIdToQuotes` migration adds nullable `OwnerId` column |

### Auth flow for delete

```
DELETE /api/quotes/1
Authorization: Bearer <token>

1. Middleware validates JWT → extracts claims (including scope + userId)
2. RequireAuthorization("can-edit-quotes") checks scope claim → 401/403 if missing
3. Handler loads quote from DB
4. authorizationService.AuthorizeAsync(user, quote, "quote-owner")
   └─ QuoteOwnerAuthorizationHandler: userId == quote.OwnerId? → 403 if not
5. Delete proceeds
```

### Test evidence

```
dotnet test
→ Passed! Failed: 0, Passed: 9
```

Tests in `AuthorizationPolicyTests.cs`:
- `CanEditQuotesPolicy_WithoutScopeClaim_Fails` → Assert.False
- `CanEditQuotesPolicy_WithScopeClaim_Succeeds` → Assert.True
- `QuoteOwnerPolicy_WhenUserIsNotOwner_Fails` → Assert.False
- `QuoteOwnerPolicy_WhenUserIsOwner_Succeeds` → Assert.True
- `QuoteOwnerPolicy_WhenQuoteHasNoOwner_Fails` → Assert.False
- `QuoteOwnerPolicy_FullPipeline_WhenNotOwner_ReturnsForbid` → Assert.False

---

## JWT Authentication Implementation (Piece-6)

This project now includes JWT-based authentication for write operations.

### What was added

1. Users persistence
- Added `User` entity with:
  - `Id` (`Guid`)
  - `Email` (`string`, unique)
  - `PasswordHash` (`string`)
- Added EF Core migration: `AddUsersTable`
- Updated `QuoteDbContext` with `DbSet<User>` and model configuration

2. Password hashing with BCrypt
- Installed package: `BCrypt.Net-Next`
- Passwords are verified using `BCrypt.Net.BCrypt.Verify(...)`
- A default seed user is created on first run (for local testing):
  - email: `user@test.com`
  - password: `password123`

3. Auth login endpoint
- Added `POST /api/auth/login`
- Request body:

```json
{
  "email": "user@test.com",
  "password": "password123"
}
```

- Success response:

```json
{
  "access_token": "<jwt>",
  "refresh_token": "<random-guid>",
  "expires_in": 900
}
```

4. JWT bearer setup
- Installed package: `Microsoft.AspNetCore.Authentication.JwtBearer`
- Configured in `Program.cs` using:
  - `AddAuthentication().AddJwtBearer(...)`
  - `AddAuthorization()`
  - `UseAuthentication()` before `UseAuthorization()`
- Token validation rules:
  - HS256 signature validation
  - Key from `IConfiguration` (`Jwt:Key`)
  - `ClockSkew = TimeSpan.Zero`
  - Lifetime validation enabled
- Startup enforces minimum key size: at least 32 UTF-8 bytes (256 bits)

5. Endpoint protection
- `POST /api/quotes` now requires auth
- `DELETE /api/quotes/{id}` now requires auth
- `GET /api/quotes` and `GET /api/quotes/{id}` remain open

### Configuration

`appsettings.json` includes:

```json
"Jwt": {
  "Key": "ThinkSchoolDay2JwtSigningKey-UseAtLeast32Chars",
  "AccessTokenLifetimeSeconds": 900
}
```

For expired-token testing, a temporary override can be used:

```powershell
$env:Jwt__AccessTokenLifetimeSeconds = "-10"
```

### Auth test evidence

See `curl_output.txt` for full raw responses of:
- no token -> `401 Unauthorized`
- valid token -> success (`201 Created` on quote creation)
- expired token -> `401 Unauthorized` with `WWW-Authenticate: Bearer error="invalid_token"...`
- Add 50 items successfully
- Try adding 51st item
**Expected:** `400 Bad Request` - "A collection cannot have more than 50 items."

---

### Quick Start Testing (3 Commands)

**Step 1: Create a Collection**
```bash
curl -s -X POST http://localhost:5000/api/collections \
  -H "Content-Type: application/json" \
  -d '{"name": "My Favourites", "ownerId": 1}'
```

**Expected Response (201 Created):**
```json
{
  "id": 1,
  "name": "My Favourites",
  "ownerId": 1,
  "items": []
}
```

**Step 2: Add a Quote to Collection (First Time - Succeeds)**
```bash
curl -s -X POST http://localhost:5000/api/collections/1/items \
  -H "Content-Type: application/json" \
  -d '{"quoteId": 1}'
```

**Expected Response (200 OK):**
```json
{
  "id": 1,
  "name": "My Favourites",
  "ownerId": 1,
  "items": [
    {
      "quoteId": 1,
      "addedAt": "2026-05-19T11:27:09.2267085Z"
    }
  ]
}
```

**Step 3: Add Same Quote Again (Domain Validation - Returns 400)**
```bash
curl -s -X POST http://localhost:5000/api/collections/1/items \
  -H "Content-Type: application/json" \
  -d '{"quoteId": 1}'
```

**Expected Response (400 Bad Request):**
```json
{
  "type": "https://tools.ietf.org/html/rfc7231#section-6.6.1",
  "title": "Domain rule violation.",
  "status": 400,
  "detail": "Quote 1 is already in this collection.",
  "instance": "/api/collections/1/items"
}
```

### More cURL Examples

**Get Collection by ID**
```bash
curl -s -X GET http://localhost:5000/api/collections/1
```

**Remove Quote from Collection**
```bash
curl -s -X DELETE http://localhost:5000/api/collections/1/items/1
```

**Delete Collection**
```bash
curl -s -X DELETE http://localhost:5000/api/collections/1
```

**Get All Quotes (Paginated)**
```bash
curl -s -X GET "http://localhost:5000/api/quotes?page=1&size=10"
```

**Create a Quote**
```bash
curl -s -X POST http://localhost:5000/api/quotes \
  -H "Content-Type: application/json" \
  -d '{"author": "Albert Einstein", "text": "Imagination is more important than knowledge."}'
```

---

## 🏗️ Architecture

### Domain Models
- **Collection** (Aggregate Root): Owns the collection of quotes
- **CollectionItem** (Value Object): Represents a quote in a collection
- **DomainException**: Custom exception for business rule violations

### Data Layer
- **ICollectionRepository**: Contract for collection operations
- **CollectionRepository**: EF Core implementation
- **QuoteDbContext**: Database context with Collections and CollectionItem mappings

### API Layer
- **ExceptionMiddleware**: Catches DomainException and returns 400 BadRequest
- **Endpoints**: RESTful endpoints for Quotes and Collections
- **Validators**: FluentValidation for request validation

### Database

---

## Refresh Tokens With Rotation (Piece-7)

This API now uses rotating refresh tokens with reuse detection:

- Access token lifetime: 15 minutes (`900` seconds)
- Refresh token lifetime: 7 days
- Refresh tokens are stored server-side as SHA-256 hashes
- Refresh tokens are single-use and rotated on every successful refresh
- Reuse detection revokes the entire token family and forces re-authentication

### RefreshTokens table

The `RefreshTokens` table contains:

- `Token` (hashed, unique)
- `UserId`
- `ExpiresAt`
- `RevokedAt`
- `ReplacedByToken`
- `Family` (used to revoke the chain on reuse detection)

Migration added: `AddRefreshTokensTable`

### Auth endpoints

1. `POST /api/auth/login`
- Validates credentials
- Returns `access_token`, `refresh_token`, `expires_in`
- Creates refresh token row in DB

2. `POST /api/auth/refresh`
- Accepts body:

```json
{
  "refresh_token": "<token>"
}
```

- Validates token exists, is not expired, and not revoked
- Rotates token (old token revoked, new token created)
- Returns new access + refresh pair
- If a replaced token is reused, logs a security event and revokes full family

3. `POST /api/auth/logout`
- Accepts `refresh_token`
- Revokes that refresh token

### Reuse-detection test

`AuthTokenServiceTests.Refresh_WhenTokenReused_RevokesEntireChain` proves:

- First refresh with a valid token succeeds
- Reusing the old token triggers reuse detection
- Entire token family is revoked
- The child token from the first refresh is also rejected afterwards
- **SQLite** with EF Core Code-First migrations
- **Collections table**: Stores collection metadata
- **CollectionItem table**: Stores quotes in collections (owned entity)

---

## 📊 Domain Rules (Invariants)

✅ **Enforced at Collection Aggregate Level:**
- Collection name must be 3-80 characters
- Max 50 items per collection
- No duplicate quotes in same collection
- Quote must exist before adding to collection

**All violations return `400 Bad Request` with domain-specific error message**

---

## 🔗 Database

Database file: `quotes.db` (SQLite)

### Collections Table
```sql
CREATE TABLE Collections (
  Id INTEGER PRIMARY KEY,
  Name TEXT NOT NULL,
  OwnerId INTEGER NOT NULL
);
```

### CollectionItem Table
```sql
CREATE TABLE CollectionItem (
  QuoteId INTEGER NOT NULL,
  CollectionId INTEGER NOT NULL,
  AddedAt TEXT NOT NULL,
  PRIMARY KEY (CollectionId, QuoteId)
);
```

---

## 📝 Recent Commits

1. **feat: add DDD domain models** - Collection, CollectionItem, DomainException
2. **feat: add CollectionRepository** - Data persistence layer
3. **feat: add Collection tables to database schema** - EF Core migrations
4. **feat: add Collection endpoints and domain exception handling** - API endpoints
5. **chore: add project configuration** - Project setup and dependencies

---

## 🚦 Status

✅ All endpoints tested and working
✅ Domain validation working (returns 400 on violations)
✅ Database migrations applied
✅ Build successful
✅ Ready for production use

---

## 📌 Example Usage Scenario

```bash
# 1. Create a collection
POST /api/collections
{"name": "Best Quotes", "ownerId": 1}
# Returns: Collection with id=1

# 2. Add first quote
POST /api/collections/1/items
{"quoteId": 1}
# Returns: 200 OK with collection containing 1 item

# 3. Try adding same quote again
POST /api/collections/1/items
{"quoteId": 1}
# Returns: 400 Bad Request "Quote 1 is already in this collection."

# 4. Remove the quote
DELETE /api/collections/1/items/1
# Returns: 200 OK

# 5. Delete collection
DELETE /api/collections/1
# Returns: 204 No Content
```

---

---

## 💉 Dependency Injection Deep Dive — What This Exercise Adds

This project was extended as part of a **DI Lifetimes & Abstractions** exercise. Here is what was learned and what was added.

### The Three Lifetimes in Action

| Lifetime | Registration | Service | Why |
|---|---|---|---|
| **Singleton** | `AddSingleton<IClock, SystemClock>()` | `IClock` | Stateless, safe to share across all requests. The real clock never changes behaviour. |
| **Transient** | `AddTransient<IQuoteFactory, QuoteFactory>()` | `IQuoteFactory` | Stateless factory; a new instance per resolution is cheap and avoids any risk of shared mutable state. |
| **Scoped** | `AddScoped<IQuoteRepository, ...>()` | `IQuoteRepository`, `ICollectionRepository`, `QuoteDbContext` | One instance per HTTP request — the correct lifetime for EF Core's `DbContext`, which is not thread-safe and tracks change state per request. |

### Why Wrong Lifetimes Are Dangerous

The classic mistake is registering a **singleton** that holds a **scoped** dependency (e.g., `DbContext`).  
.NET's DI container will throw a `InvalidOperationException` ("cannot consume scoped service from singleton") in development mode, but the logic error is: the singleton keeps the `DbContext` alive across requests, silently sharing tracked entity state — leading to corrupt or stale data responses.

**Rule of thumb:** a service's lifetime must be ≥ every lifetime it depends on.  
Singleton → can only depend on singletons.  
Scoped → can depend on scoped or transient.  
Transient → can depend on anything.

### IClock Abstraction — Why It Matters

Before this exercise, `DateTime.UtcNow` was called directly inside models:

```csharp
// ❌ Before — untestable, time is fixed at object creation
public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
```

After this exercise, the `IClock` interface is injected wherever a timestamp is needed:

```csharp
// ✅ After — constructor injection, time comes from the container
public interface IClock
{
    DateTimeOffset UtcNow { get; }
}

public sealed class SystemClock : IClock
{
    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;  // real clock
}
```

**Benefits:**
- **Testability** — tests inject a `FakeClock` with a fixed instant; no flaky time-dependent assertions.
- **Determinism** — the timestamp is computed at the moment the factory or handler runs, not at object construction.
- **Single source of truth** — every service that needs the current time asks the container for `IClock`; the implementation is swapped in one place.

### Constructor Injection — No `new` Inside Methods

All services declare their dependencies in the constructor. The container wires everything:

```csharp
// QuoteFactory — declares IClock in constructor, container provides it
public sealed class QuoteFactory : IQuoteFactory
{
    private readonly IClock _clock;
    public QuoteFactory(IClock clock) => _clock = clock;

    public Quote Create(string author, string text) =>
        new Quote { Author = author, Text = text, CreatedAt = _clock.UtcNow.UtcDateTime };
}
```

This means `QuoteFactory` is fully testable with zero infrastructure:

```csharp
[Fact]
public void Create_UsesClockUtcNow_ForCreatedAt()
{
    var fixedTime = new DateTimeOffset(2026, 5, 19, 12, 30, 0, TimeSpan.Zero);
    var factory = new QuoteFactory(new FakeClock(fixedTime));

    var quote = factory.Create("Author", "Text");

    Assert.Equal(fixedTime.UtcDateTime, quote.CreatedAt);  // always passes
}
```

### Files Added / Changed

| File | What Changed |
|---|---|
| `Time/IClock.cs` | New — `IClock` abstraction |
| `Time/SystemClock.cs` | New — real-clock singleton implementation |
| `Services/IQuoteFactory.cs` | New — factory interface |
| `Services/QuoteFactory.cs` | New — factory injects `IClock`, creates `Quote` with correct timestamp |
| `Models/Quote.cs` | `CreatedAt` default removed; set by factory via clock |
| `Models/Collection.cs` | `AddItem` receives `DateTime addedAtUtc` param instead of calling `DateTime.UtcNow` internally |
| `Extensions/ServiceCollectionExtensions.cs` | Registered `IClock` (singleton), `IQuoteFactory` (transient) |
| `QuotesApi.Tests/QuoteFactoryTests.cs` | New — unit test using `FakeClock` proving deterministic timestamp |
| `QuotesApi.csproj` | Excluded test folder from main project glob |

---

## Integration Tests with WebApplicationFactory (Day 3 — Piece 6)

### Test project: `Quotes.Tests.Integration`

A dedicated integration-test project that boots the **real application pipeline in-memory** using `WebApplicationFactory<Program>`. No live HTTP server, no external database — but the full middleware stack, JWT auth, EF Core migrations, and routing all run exactly as they do in production.

### Isolation strategy

Each test class owns its own `IntegrationTestFactory` instance. Because xUnit creates a new class instance per test method, every test gets its own temp SQLite file (GUID-named in `%TEMP%`) that is created fresh, migrated, seeded, and deleted on dispose. Zero shared state between tests.

```csharp
// xUnit creates a new QuoteEndpointTests for every [Fact] →
// each [Fact] gets a brand-new DB.
public sealed class QuoteEndpointTests : IDisposable
{
    private readonly IntegrationTestFactory _factory = new();
    public void Dispose() => _factory.Dispose();
}
```

### WebApplicationFactory subclass

```csharp
public sealed class IntegrationTestFactory : WebApplicationFactory<Program>
{
    public const string TestJwtKey = "ThinkSchoolDay2JwtSigningKey-UseAtLeast32Chars";

    private readonly string _dbPath = Path.Combine(
        Path.GetTempPath(), $"quotes_int_{Guid.NewGuid():N}.db");

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureAppConfiguration((_, cfg) =>
        {
            cfg.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:DefaultConnection"] = $"Data Source={_dbPath}",
                ["Jwt:Key"]                            = TestJwtKey,
                ["Jwt:AccessTokenLifetimeSeconds"]     = "900",
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
```

### Test coverage (15 tests)

| # | Test | Expected |
|---|------|----------|
| 1 | `GetQuotes_ReturnsOk_WithPaginationShape` | 200 + pagination envelope |
| 2 | `GetQuotes_InvalidPage_ReturnsBadRequest` | 400 + ProblemDetails |
| 3 | `GetQuoteById_UnknownId_ReturnsNotFound` | 404 |
| 4 | `GetQuoteById_AfterCreate_ReturnsMatchingQuote` | 201 then 200 (verifies EF migrations) |
| 5 | `CreateQuote_Anonymous_ReturnsUnauthorized` | 401 |
| 6 | `CreateQuote_WithoutScopeClaim_ReturnsForbidden` | 403 |
| 7 | `CreateQuote_ValidRequest_ReturnsCreatedWithQuote` | 201 with body |
| 8 | `CreateQuote_EmptyAuthor_Returns422WithValidationErrors` | 422 + ValidationProblemDetails |
| 9 | `DeleteQuote_Anonymous_ReturnsUnauthorized` | 401 |
| 10 | `DeleteQuote_NotOwner_ReturnsForbidden` | 403 (resource-based auth) |
| 11 | `DeleteQuote_Owner_ReturnsNoContent` | 204 |
| 12 | `Login_ValidCredentials_ReturnsTokenPair` | 200 with access + refresh tokens |
| 13 | `Login_WrongPassword_ReturnsUnauthorized` | 401 |
| 14 | `Refresh_ValidToken_ReturnsNewRotatedTokenPair` | 200, new refresh token differs from old |
| 15 | `Refresh_InvalidToken_ReturnsUnauthorized` | 401 |

### Files added / changed

| File | Change |
|---|---|
| `Quotes.Tests.Integration/Quotes.Tests.Integration.csproj` | New — xUnit + FluentAssertions + Mvc.Testing |
| `Quotes.Tests.Integration/IntegrationTestFactory.cs` | New — `WebApplicationFactory<Program>` subclass with per-test isolation |
| `Quotes.Tests.Integration/QuoteEndpointTests.cs` | New — 11 tests covering all quote endpoints |
| `Quotes.Tests.Integration/AuthEndpointTests.cs` | New — 4 tests covering login, refresh, and revocation |
| `QuotesApi.csproj` | Added exclusion glob for the new test folder |

### How to run

```bash
cd "DAY3/Piece-6-Integration tests with WebApplicationFactory/QuotesAPI-Amey/Quotes.Tests.Integration"
dotnet test --logger "console;verbosity=normal"
```

### Test run output

```
Passed!  - Failed: 0, Passed: 15, Skipped: 0, Total: 15, Duration: 14 s
```

---

## xUnit with Fluent Assertions (Day 3 — Piece 4)

### Test project: `Quotes.Tests.Unit`

A dedicated unit-test project using **xUnit 2.9**, **FluentAssertions 7.0**, and **NSubstitute 5.3**.  
All tests follow the **Arrange / Act / Assert** pattern with no shared `[SetUp]` — every test is self-contained.  
Parameterised cases use `[Theory]` + `[InlineData]`.

### Test classes and coverage

| Class | Tests | What's covered |
|---|---|---|
| `CreateQuoteRequestValidatorTests` | 11 | Every branch: empty author, whitespace author, author > 256 chars, boundary 256, empty text, whitespace text, text > 2000 chars, boundary 2000, valid request |
| `QuoteFactoryTests` | 5 | Clock used when no timestamp given, explicit timestamp overrides clock, author mapped, text mapped, `DateTimeKind.Utc` enforced |
| `CollectionTests` | 14 | Constructor: name too short (3 cases via Theory), name too long, valid name (2 cases); AddItem: 50-item cap, duplicate quote, success; RemoveItem: not found, success; Rename: too short, valid |
| `AuthTokenServiceTests` | 7 | Token not found → InvalidToken; expired → ExpiredToken; revoked without replacement → RevokedToken; valid → new pair issued; reuse → ReuseDetected + entire family revoked; RevokeAsync sets RevokedAt; IssueTokenPairAsync returns configured ExpiresIn |

**Total: 37 tests**

### How to run

```bash
cd "DAY3/Piece-4-xUnit with Fluent Assertions/QuotesAPI-Amey/Quotes.Tests.Unit"
dotnet test --logger "console;verbosity=detailed"
```

### Sample test — FluentAssertions pattern

```csharp
[Fact]
public void Create_WhenNoTimestampProvided_UsesClockUtcNow()
{
    // Arrange
    var fixedNow = new DateTimeOffset(2026, 5, 19, 12, 30, 0, TimeSpan.Zero);
    var clock = Substitute.For<IClock>();
    clock.UtcNow.Returns(fixedNow);
    var sut = new QuoteFactory(clock);

    // Act
    var quote = sut.Create("Author", "Text");

    // Assert
    quote.CreatedAt.Should().Be(fixedNow.UtcDateTime);
}
```

---

## 📧 Support

For issues or questions, contact the development team.

**Happy quoting! 🎉**
