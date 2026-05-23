# Piece 7 — Smoke-test the deployed API & Week 1 Reflection

## Live URL

```
https://ca-api-nb3bgcnwnlpwe.lemoncliff-d4727121.southeastasia.azurecontainerapps.io
```

---

## Smoke Test Results

**Date:** 2026-05-23  
**Test credentials:** `test@example.com` / `password123`

---

### Health

| # | Check | Request | Expected | Actual | Result |
|---|-------|---------|----------|--------|--------|
| 1 | Health endpoint | `GET /health` | 200 Healthy | 200 Healthy | ✅ PASS |

---

### Auth — Login

| # | Check | Request | Expected | Actual | Result |
|---|-------|---------|----------|--------|--------|
| 2 | Login with valid credentials | `POST /api/auth/login` `{"email":"test@example.com","password":"password123"}` | 200 + tokens | 200 `{"access_token":"…","refresh_token":"…","expires_in":900}` | ✅ PASS |
| 3 | Login with wrong password | `POST /api/auth/login` `{"email":"nobody@x.com","password":"wrong"}` | 401 | 401 | ✅ PASS |

Login response shape:

```json
{
  "access_token": "<JWT>",
  "refresh_token": "<base64>",
  "expires_in": 900
}
```

---

### Quotes — Anonymous reads

| # | Check | Request | Expected | Actual | Result |
|---|-------|---------|----------|--------|--------|
| 4 | List quotes (paginated) | `GET /api/quotes/?page=1&size=10` | 200 array | 200 JSON array | ✅ PASS |
| 5 | List quotes without query params | `GET /api/quotes/` | 400 | 400 | ✅ PASS |
| 6 | Get existing quote by ID | `GET /api/quotes/3` | 200 | 200 `{"id":3,"author":"Marcus Aurelius",…}` | ✅ PASS |
| 7 | Get non-existent quote | `GET /api/quotes/99999` | 404 | 404 | ✅ PASS |

List response shape (flat array, no pagination wrapper):

```json
[
  {"id":1,"author":"Marcus Aurelius","text":"The impediment to action advances action.","createdAt":"2026-05-23T11:32:57.89Z","ownerId":1},
  …
]
```

---

### Quotes — Authenticated writes

| # | Check | Request | Expected | Actual | Result |
|---|-------|---------|----------|--------|--------|
| 8 | Create quote without auth | `POST /api/quotes/` (no Bearer) | 401 | 401 | ✅ PASS |
| 9 | Create quote with valid auth + body | `POST /api/quotes/` Bearer + `{"author":"Marcus Aurelius","text":"…"}` | 201 | 201 `{"id":5,…,"ownerId":1}` | ✅ PASS |
| 10 | Create quote with empty author/text | `POST /api/quotes/` Bearer + `{"author":"","text":""}` | 400 ValidationProblem | 400 `{"errors":{"author":["Author is required"],"text":["Text is required"]},…}` | ✅ PASS |

---

### Quotes — Delete

| # | Check | Request | Expected | Actual | Result |
|---|-------|---------|----------|--------|--------|
| 11 | Delete own quote (authed) | `DELETE /api/quotes/5` Bearer | 204 | 204 | ✅ PASS |
| 12 | Delete already-deleted quote | `DELETE /api/quotes/5` Bearer | 404 | 404 | ✅ PASS |
| 13 | Delete non-existent quote | `DELETE /api/quotes/99999` Bearer | 404 | 404 | ✅ PASS |
| 14 | Delete without auth | `DELETE /api/quotes/1` (no Bearer) | 401 | 401 | ✅ PASS |

> **Not tested:** DELETE a quote owned by a different user (→ should return 403). Only one user exists in the deployed DB, so this path could not be exercised.

---

### Auth — Refresh token

> **Important:** the request body field is `refresh_token` (snake_case), not `refreshToken`.

| # | Check | Request | Expected | Actual | Result |
|---|-------|---------|----------|--------|--------|
| 15 | Refresh with valid token | `POST /api/auth/refresh` `{"refresh_token":"<token>"}` | 200 + new tokens | 200 `{"access_token":"…","refresh_token":"…","expires_in":900}` | ✅ PASS |
| 16 | Refresh reuse (rotated token) | `POST /api/auth/refresh` same old token after rotation | 401 | 401 | ✅ PASS |
| 17 | Refresh with invalid token | `POST /api/auth/refresh` `{"refresh_token":"totally-fake-token"}` | 401 | 401 | ✅ PASS |

---

### Auth — Logout

| # | Check | Request | Expected | Actual | Result |
|---|-------|---------|----------|--------|--------|
| 18 | Logout without auth | `POST /api/auth/logout` (no Bearer) `{"refresh_token":"…"}` | 401 | 401 | ✅ PASS |
| 19 | Logout with valid Bearer + refresh token | `POST /api/auth/logout` Bearer + `{"refresh_token":"<token>"}` | 204 | 204 | ✅ PASS |
| 20 | Refresh after successful logout | `POST /api/auth/refresh` with now-revoked token | 401 | 401 | ✅ PASS |

---

### Summary

| Status | Count |
|--------|-------|
| ✅ PASS | 20 |
| ❌ FAIL | 0 |
| ⏭ SKIP | 1 (DELETE not-own quote — only 1 user in DB) |

**All deployed endpoints respond correctly end-to-end.**

---

## Fragility Notes

| Fragility | Why It Matters |
|---|---|
| `GET /api/quotes/` requires `?page` + `?size` params | Missing params return 400 — callers must always supply pagination |
| SQLite in ephemeral container storage | Container restart wipes all quotes — need Azure Files mount for production persistence |
| Only one seeded user in deployed DB | Cannot smoke-test cross-user 403 path (delete other user's quote) |
| `Jwt:Key` in `appsettings.json` | Key visible in source; should be stored in Key Vault |
| Refresh token rotation (check 16) | Old token is invalidated on rotation — clients that cache the previous token are locked out on retry |

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

What I didn't expect was how much of the week's value came from *reading and catching* AI output rather than from generating it. The Polly v7/v8 API mismatch, the pagination 400 surfaced in smoke testing — both were cases where AI produced plausible-looking code that worked locally but broke at runtime. The human judgment layer — running the actual thing and questioning unexpected responses — was irreplaceable.

---

## What I Learned This Session

The smoke test surfaced behavior the unit and integration test suites never covered: that `GET /api/quotes/` without pagination params returns 400 in production, and that refresh token rotation correctly rejects the previous token on reuse (check 16). Local tests use `WebApplicationFactory` with controlled inputs — smoke-testing the real deployed artifact is the only way to verify the full request pipeline, cold-start behavior, and environment config is all wired correctly.

---

## What Would Break This

| Failure | Impact |
|---|---|
| Container restart | SQLite data wiped — all quotes lost |
| Caller omits `?page&size` on list endpoint | 400 — no graceful default pagination |
| `Jwt:Key` shorter than 32 bytes | App crashes on startup with `InvalidOperationException` |
| Missing `ASPNETCORE_HTTP_PORTS=8080` | Container Apps sends HTTP to 8080, app redirects → redirect loop |
| Only 1 seeded user | 403 cross-user delete path is untestable without a second user |
| Refresh token reuse after rotation | Client locked out if it retries with the old token after a 401 |

---

## GitHub Folder

[DAY5/Piece-7-Smoke-test the deployed API and Week 1 reflection](https://github.com/thinkbridge-thinkschool/ThinkSchoo-ameykhot-Day1/tree/day5/cloud-deployment-observability/DAY5/Piece-7-Smoke-test%20the%20deployed%20API%20and%20Week%201%20reflection)
