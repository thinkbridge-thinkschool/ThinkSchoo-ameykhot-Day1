# Day 3 – Piece 3: Lock Down the API End-to-End

## What Was Built

This submission wraps everything from Day 3 into a production-ready, end-to-end secured Quotes API.

---

## Features Implemented

### 1. Dual-Scheme JWT Authentication
- **Internal HS256** – issued by `POST /api/auth/login` for service-to-service / internal callers
- **Entra ID RS256** – accepted for SPA users authenticating via Azure AD
- A **policy scheme** (`MultiScheme`) peeks at the `iss` claim in the Bearer token and routes to the correct handler — no manual switching needed at endpoint level

### 2. Refresh-Token Rotation with Reuse Detection
- Every login issues a `(accessToken, refreshToken)` pair
- On refresh the old token is revoked and a new one issued (rotation)
- If a **previously-rotated** token is replayed, the entire family is revoked immediately and a `ReuseDetected` error returned
- Tokens are stored as **SHA-256 hashes** — raw token never persists to disk

### 3. Authorization Policies on Every Mutating Endpoint

| Endpoint | Policy |
|---|---|
| `POST /api/quotes` | `can-edit-quotes` (requires `scope=quotes.write`) |
| `DELETE /api/quotes/{id}` | `can-edit-quotes` + resource-based `quote-owner` handler |
| `POST /api/collections` | `can-edit-quotes` |
| `POST /api/collections/{id}/items` | `can-edit-quotes` |
| `DELETE /api/collections/{id}/items/{quoteId}` | `can-edit-quotes` |
| `DELETE /api/collections/{id}` | `can-edit-quotes` |

Read endpoints (`GET`) remain public.

### 4. Integration Tests (5 Scenarios) — all green locally

| Test | Scenario | Expected |
|---|---|---|
| `Anonymous_PostQuote_Returns401` | No Authorization header | 401 |
| `Authenticated_WithoutScopeClaim_PostQuote_Returns403` | Valid JWT, missing scope | 403 |
| `Authenticated_WithScope_PostQuote_Returns201` | Valid JWT with scope | 201 Created |
| `ExpiredToken_PostQuote_Returns401` | JWT expired 1s ago, `ClockSkew=Zero` | 401 |
| `RevokedRefreshChain_Returns401` | Login → refresh → reuse original → both tokens 401 | 401 |

**Total test count: 14** (5 integration + 5 authorization policy unit tests + 2 factory tests + 1 token-reuse unit test + 1 authorization pipeline test)

---

## Test Run Output (local)

```
Passed!  - Failed: 0, Passed: 14, Skipped: 0, Total: 14, Duration: 7 s
```

---

## PR & CI

- **PR URL**: https://github.com/thinkbridge-thinkschool/ThinkSchoo-ameykhot-Day1/pulls  
- **CI Workflow**: `.github/workflows/day3-p3-auth-tests.yml` — triggers on push/PR touching `DAY3/Piece-3-*/**`

> CI run URL will be updated once the push completes and the workflow executes on GitHub Actions.

---

## PR Self-Assessment

**this is solid**

The auth pipeline is complete: dual schemes, rotation with reuse detection, and a policy on every write surface. The integration tests cover all the failure modes (anonymous, wrong policy, expired, revoked chain) plus the golden path. The only caveat worth noting is that Entra ID validation relies on OIDC discovery at request time — if the tenant ID is invalid in the config, Entra-issued tokens would fail silently with 401 instead of a clear config error, but that's acceptable for internal APIs where SPA auth is optional.

---

## What I Learned

**The thing that clicked**: Refresh-token *families* are the key insight. Without family tracking, reuse detection only works one hop back. With a `Family` column, one replayed token revokes everything that was ever issued from that login — an attacker who steals a rotated-away token can't stay logged in no matter how many times they try.

**The idea I'll keep**: `ClockSkew = TimeSpan.Zero` in tests combined with `lifetimeSeconds: -1` is a dead-simple way to test token expiry without mocking the system clock in the integration layer.

---

## What Would Break This

1. **The JWT signing key in `appsettings.json` is hardcoded** — it's in source control. Real deployment must rotate this to a secret store (Azure Key Vault / environment variable).
2. **SQLite refresh-token table is local** — horizontal scale would require a shared store (Redis or RDBMS). The current hash-based approach is correct but the storage is single-node.
3. **No refresh-token expiry cleanup** — the `RefreshTokens` table will grow unbounded. A background job that deletes expired/revoked tokens older than N days is missing.
4. **Entra ID validation depends on live OIDC discovery** — an air-gapped or rate-limited environment would fail to validate Entra tokens silently (the Entra scheme returns 401 if it can't fetch signing keys).
5. **BCrypt work factor is default (11)** — acceptable today but should be reviewed against hardware benchmarks every 18 months.
