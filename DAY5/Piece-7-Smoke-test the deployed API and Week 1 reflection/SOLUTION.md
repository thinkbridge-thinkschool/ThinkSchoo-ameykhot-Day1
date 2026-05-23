# Piece 7 — Smoke-test the deployed API & Week 1 Reflection

## Live URL

```
https://ca-api-342m3golxdrt6.livelydune-368712a9.centralindia.azurecontainerapps.io
```

---

## Smoke Test Results

All tests run on **2026-05-23** against the live Azure Container Apps deployment.

### Endpoint Matrix

| # | Method | Endpoint | Auth | Expected | Actual | Status |
|---|--------|----------|------|----------|--------|--------|
| 1 | GET | `/health` | None | 200 | 200 | ✅ |
| 2 | POST | `/api/auth/login` (valid) | None | 200 + tokens | 200 | ✅ |
| 3 | POST | `/api/auth/login` (wrong password) | None | 401 | 401 | ✅ |
| 4 | GET | `/api/quotes` | None | 200 + array | 200 | ✅ |
| 5 | POST | `/api/quotes` | Bearer | 201 | 201 | ✅ |
| 6 | POST | `/api/quotes` | None | 401 | 401 | ✅ |
| 7 | GET | `/api/quotes/{id}` | None | 200 | 200 | ✅ |
| 8 | GET | `/api/quotes/999` (not found) | None | 404 | 404 | ✅ |
| 9 | GET | `/api/quotes/inspire` | None | 200/503 | **400** | ⚠️ |
| 10 | POST | `/api/auth/refresh` | None | 200 + new tokens | 200 | ✅ |
| 11 | POST | `/api/collections` | Bearer | 201 | 201 | ✅ |
| 12 | POST | `/api/collections/{id}/items` | Bearer | 200 | 200 | ✅ |
| 13 | GET | `/api/collections/{id}` | None | 200 | 200 | ✅ |
| 14 | DELETE | `/api/collections/{id}/items/{qid}` | Bearer | 200 | 200 | ✅ |
| 15 | DELETE | `/api/collections/{id}` | Bearer | 204 | 204 | ✅ |
| 16 | DELETE | `/api/quotes/{id}` | Bearer | 204 | 204 | ✅ |
| 17 | POST | `/api/auth/logout` (with refresh_token) | Bearer | 204 | 204 | ✅ |
| 18 | POST | `/api/auth/logout` (empty body) | Bearer | 400/422 | 400 | ⚠️ |

**16/18 endpoints fully correct. 1 bug found, 1 UX roughness found.**

---

## Curl Commands Used

```bash
BASE="https://ca-api-342m3golxdrt6.livelydune-368712a9.centralindia.azurecontainerapps.io"

# Health
curl "$BASE/health"
# → {"status":"healthy","timestamp":"2026-05-23T14:08:54.6780574+00:00"}

# Login
curl -X POST "$BASE/api/auth/login" \
  -H "Content-Type: application/json" \
  -d '{"email":"user@test.com","password":"password123"}'
# → {"access_token":"eyJ...","refresh_token":"w1m8...","expires_in":900}

# Get quotes (no auth — open read)
curl "$BASE/api/quotes"
# → {"data":[...],"pagination":{"page":1,"size":10,"total":1}}

# Create quote (requires scope=quotes.write in token)
curl -X POST "$BASE/api/quotes" \
  -H "Authorization: Bearer $TOKEN" \
  -H "Content-Type: application/json" \
  -d '{"text":"The only way to do great work is to love what you do.","author":"Steve Jobs"}'
# → 201 Created

# Inspire (external ZenQuotes)
curl "$BASE/api/quotes/inspire"
# → 400 Bad Request (empty body) — route conflict bug

# Refresh
curl -X POST "$BASE/api/auth/refresh" \
  -H "Content-Type: application/json" \
  -d '{"refresh_token":"<token>"}'
# → 200 + new token pair

# Logout (refresh_token required in body)
curl -X POST "$BASE/api/auth/logout" \
  -H "Authorization: Bearer $TOKEN" \
  -H "Content-Type: application/json" \
  -d '{"refresh_token":"<token>"}'
# → 204 No Content
```

---

## Issues Found

### Bug 1 — `GET /api/quotes/inspire` returns 400 (route conflict)

**Root cause:** The route template `/{id}` has no `:int` constraint. When ASP.NET Core's routing engine resolves `GET /api/quotes/inspire`, the literal route `/inspire` should win over `/{id}`, but under .NET 10 Container Apps the `/{id}` route is selected instead. The parameter binding then tries to parse `"inspire"` as `int`, fails, and returns `400 Bad Request` with an empty body — before any handler code runs.

**Fix:** Add an int route constraint:
```csharp
quotes.MapGet("/{id:int}", GetQuoteById)
quotes.MapDelete("/{id:int}", DeleteQuote)
```

**ZenQuotes API itself is healthy** — a direct call returns `200 OK` with a valid quote array, so the external dependency is not the problem.

### Observation 2 — Logout requires `refresh_token` in body (not documented)

`POST /api/auth/logout` with an empty body returns `400` with the obscure message:
```
"Value cannot be null. (Parameter 's')"
```
This surfaces an internal BCrypt / string-processing detail. The endpoint should accept an empty body and revoke by reading the access token from the `Authorization` header, or return a clear validation error.

### Observation 3 — `ownerId` is 0 in Collections response

When a collection is created, the response shows `"ownerId": 0` instead of the authenticated user's ID. The `CreateCollectionRequest` likely doesn't propagate the JWT subject claim to the aggregate.

---

## Fragility List

| Fragility | Why It Matters |
|---|---|
| No `:int` route constraint on `/{id}` | `/inspire` returns 400 in production today |
| SQLite in ephemeral container storage | Every container restart wipes all quotes data — need Azure Files mount for persistence |
| Seeded single user `user@test.com` | No registration endpoint — rotating the seeded password breaks all clients |
| `Jwt:Key` hardcoded in `appsettings.json` | Key is visible in source; should be in Key Vault secret |
| ZenQuotes rate-limit not handled | After 5 calls/hour ZenQuotes throttles — the circuit breaker opens and `/inspire` returns 503 for 30 s |
| `MinimumThroughput = 2` in circuit breaker | Two cold-start timeout spikes open the circuit; low-traffic apps get locked out |

---

## Week 1 Reflection — Amey Khot

### What AI Accelerated Most

The single biggest acceleration was **Piece 4 — azd deployment**. Shipping a working multi-file Bicep stack (subscription-scope `main.bicep` + resource-group-scope `resources.bicep`) from zero, with correct `azd-service-name` tags, Container Registry wiring, Log Analytics linkage, and Container Apps ingress config, in under an hour would have taken me half a day consulting Azure docs and debugging ARM errors. AI generated the Bicep skeleton, named the right resource API versions, and explained *why* the `publicNetworkAccessForIngestion: 'Enabled'` flag is needed for App Insights — none of which I would have found quickly on my own.

The second biggest was **Day 3 authorization testing** (Piece 4 — FluentAssertions + xUnit). AI produced a `WebApplicationFactory<Program>` setup with seeded data and the exact `services.PostConfigureAll<JwtBearerOptions>` trick needed to skip real JWT signing in integration tests. Getting that bootstrapping right without a model would have cost significant trial-and-error time.

### Where AI Got It Subtly Wrong

In Piece 6 (Polly resilience), AI initially generated a typed `HttpClient` registration that used the old Polly v7 `AddPolicyHandler` API. The project was already using `Microsoft.Extensions.Http.Resilience` (Polly v8), which uses `AddResilienceHandler` with a completely different pipeline configuration syntax. The code compiled but the pipeline was silently ignored — the retries never fired in tests.

I caught it by running the tests and noticing `handler.CallCount == 1` despite the sequence returning three 503s. Once I added retry logging and saw no retry callbacks in the output, I looked at the actual DI registration and found the mismatch. The lesson: **always verify resilience behavior with a test that forces failures**, not just a test that verifies the happy path.

### Weakest Competency

Infrastructure as Code (Bicep). I can read and modify Bicep files, but I can't yet write one from scratch or confidently debug resource dependency errors without heavy AI assist. During Piece 5 I ran into a `CircularDependency` error between the App Insights resource and the Container App secrets block and had to rely entirely on AI to untangle the dependency order.

**Plan for Week 2:** Work through one Bicep exercise without AI scaffolding — write the resource definitions from the official ARM reference docs, then compare with AI output. The goal is to understand the structure well enough to recognize when AI-generated Bicep is wrong, not just to copy working snippets.

### What Surprised Me About the Pace

The fact that a six-day sprint can produce a production-shaped system — JWT auth, EF Core persistence, structured logs, distributed traces, Polly resilience, and Azure Container Apps deployment — was genuinely surprising. Two things made this possible: AI compressed the "what is the right API / pattern" lookup time to near-zero, and the Minimal API surface area in .NET 10 is small enough that the cognitive load per feature is low.

What I didn't expect was how much of the week's value came from *reading and catching* AI output rather than from generating it. The route constraint bug I found in the smoke test, the Polly v7/v8 API mismatch — both were cases where AI produced plausible-looking code that worked locally but broke at runtime. The human judgment layer — running the actual thing and questioning empty responses — was irreplaceable.

---

## What I Learned This Session

The smoke test surfaced something the unit and integration test suites missed entirely: **the `/api/quotes/inspire` route returning 400 in production**. Local tests use `WebApplicationFactory` which may resolve the route conflict differently. This is the value of smoke-testing the actual deployed artifact rather than relying on test doubles — the routing infrastructure, environment variables, and cold-start behavior are all real.

---

## What Would Break This

| Failure | Impact |
|---|---|
| Container restart | SQLite data wiped — all quotes lost |
| Student subscription quota | Can't provision a second Container Apps Environment |
| ZenQuotes blocks Azure egress IPs | `/inspire` returns 503 after retries (actually returns 400 today due to the route bug) |
| `Jwt:Key` shorter than 32 bytes | App crashes on startup with `InvalidOperationException` |
| Missing `ASPNETCORE_HTTP_PORTS=8080` | Container Apps sends HTTP to 8080 but app redirects, causing redirect loops |
| `/{id}` route without `:int` constraint | `/inspire` returns 400 (confirmed in production today) |

---

## GitHub Folder

[DAY5/Piece-7-Smoke-test the deployed API and Week 1 reflection](https://github.com/thinkbridge-thinkschool/ThinkSchoo-ameykhot-Day1/tree/day5/cloud-deployment-observability/DAY5/Piece-7-Smoke-test%20the%20deployed%20API%20and%20Week%201%20reflection)
