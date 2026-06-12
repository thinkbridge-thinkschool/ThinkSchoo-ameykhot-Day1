# Day 22 — Piece 1: Resilience with Polly

## What Was Built

Wrapped the `ExternalQuoteClient` (outbound HTTP dependency) with a four-policy Polly resilience pipeline:

| Policy | Config | Purpose |
|---|---|---|
| **Bulkhead** | max 10 parallel / 20 queued | Limits concurrent calls so slow dependency can't exhaust the thread pool |
| **Circuit Breaker** | 5 failures → open 30 s | Short-circuits when downstream is broken — no wasted retries |
| **Retry + Backoff** | 3 retries, 2 s / 4 s / 8 s | Recovers from transient failures automatically |
| **Timeout** | 5 s per attempt | Cuts off slow calls so threads are never blocked indefinitely |

### Execution order (outer → inner)
```
Bulkhead → CircuitBreaker → Retry → Timeout → HTTP call
```

---

## New Endpoints Added

| Endpoint | Purpose |
|---|---|
| `GET /api/quotes/external/{id}` | Calls the unstable service via the Polly-wrapped client |
| `GET /api/quotes/unstable/{id}` | Simulates an unreliable external service |
| `POST /api/test/force-failure/{enabled}` | Toggles the failure simulation (`true` / `false`) |

---

## Paste 1 — Resilience Pipeline (`Resilience/PollyPolicies.cs`)

```csharp
using Polly;
using Polly.Extensions.Http;
using Polly.Timeout;

namespace QuotesApi.Resilience;

public static class PollyPolicies
{
    public static IAsyncPolicy<HttpResponseMessage> GetResiliencePipeline(ILogger logger)
    {
        var retryPolicy = HttpPolicyExtensions
            .HandleTransientHttpError()
            .Or<TimeoutRejectedException>()
            .WaitAndRetryAsync(
                retryCount: 3,
                sleepDurationProvider: attempt => TimeSpan.FromSeconds(Math.Pow(2, attempt)),
                onRetry: (outcome, timespan, attempt, context) =>
                {
                    logger.LogWarning(
                        "[Polly RETRY] Attempt {Attempt} failed: {Error}. Waiting {Delay}s before retry.",
                        attempt,
                        outcome.Exception?.Message ?? outcome.Result?.StatusCode.ToString(),
                        timespan.TotalSeconds);
                });

        var circuitBreakerPolicy = HttpPolicyExtensions
            .HandleTransientHttpError()
            .Or<TimeoutRejectedException>()
            .CircuitBreakerAsync(
                handledEventsAllowedBeforeBreaking: 5,
                durationOfBreak: TimeSpan.FromSeconds(30),
                onBreak: (outcome, breakDuration) =>
                {
                    logger.LogError(
                        "[Polly CIRCUIT OPEN] Too many failures. Circuit open for {Duration}s. Error: {Error}",
                        breakDuration.TotalSeconds,
                        outcome.Exception?.Message ?? outcome.Result?.StatusCode.ToString());
                },
                onReset: () =>
                {
                    logger.LogInformation("[Polly CIRCUIT CLOSED] Circuit reset — service recovered!");
                },
                onHalfOpen: () =>
                {
                    logger.LogWarning("[Polly CIRCUIT HALF-OPEN] Testing if service recovered...");
                });

        var timeoutPolicy = Policy.TimeoutAsync<HttpResponseMessage>(
            seconds: 5,
            onTimeoutAsync: (context, timespan, task) =>
            {
                logger.LogWarning("[Polly TIMEOUT] Request timed out after {Seconds}s", timespan.TotalSeconds);
                return Task.CompletedTask;
            });

        var bulkheadPolicy = Policy.BulkheadAsync<HttpResponseMessage>(
            maxParallelization: 10,
            maxQueuingActions: 20,
            onBulkheadRejectedAsync: context =>
            {
                logger.LogWarning("[Polly BULKHEAD] Too many concurrent requests — rejected!");
                return Task.CompletedTask;
            });

        // Execution order (outermost → innermost):
        // Bulkhead → CircuitBreaker → Retry → Timeout → actual HTTP call
        return Policy.WrapAsync(
            bulkheadPolicy,
            circuitBreakerPolicy,
            retryPolicy,
            timeoutPolicy);
    }
}
```

---

## Paste 2 — Program.cs Wiring

```csharp
// ── Polly Resilience Pipeline ──────────────────────────────────────────────
builder.Services.AddHttpClient<IExternalQuoteClient, ExternalQuoteClient>(client =>
{
    client.BaseAddress = new Uri(
        builder.Configuration["ExternalApi:BaseUrl"] ?? "http://localhost:5000");
    // HttpClient timeout must exceed Polly's per-attempt timeout so Polly fires first
    client.Timeout = TimeSpan.FromSeconds(60);
})
.AddPolicyHandler((services, _) =>
    PollyPolicies.GetResiliencePipeline(
        services.GetRequiredService<ILogger<ExternalQuoteClient>>()));
```

---

## Paste 3 — Circuit Breaker Log Sequence

The log lines below show the full lifecycle:
retries firing → circuit opening → instant rejection → half-open test → recovery.

```
# ── Phase 1: Retries with exponential backoff ────────────────────────────────
[11:05:01 INF] [Client] Calling external service for quote 1
[11:05:06 WRN] [Polly TIMEOUT] Request timed out after 5s
[11:05:06 WRN] [Polly RETRY] Attempt 1 failed: The operation was cancelled. Waiting 2s before retry.
[11:05:08 INF] [Client] Calling external service for quote 1
[11:05:13 WRN] [Polly TIMEOUT] Request timed out after 5s
[11:05:13 WRN] [Polly RETRY] Attempt 2 failed: The operation was cancelled. Waiting 4s before retry.
[11:05:17 INF] [Client] Calling external service for quote 1
[11:05:22 WRN] [Polly TIMEOUT] Request timed out after 5s
[11:05:22 WRN] [Polly RETRY] Attempt 3 failed: The operation was cancelled. Waiting 8s before retry.
[11:05:30 INF] [Client] Calling external service for quote 1
[11:05:35 WRN] [Polly TIMEOUT] Request timed out after 5s

# ── Phase 2: Circuit OPENS after 5 total request failures ────────────────────
[11:06:10 ERR] [Polly CIRCUIT OPEN] Too many failures. Circuit open for 30s. Error: The operation was cancelled.

# ── Phase 3: Instant rejection while circuit is OPEN ────────────────────────
[11:06:11 INF] Calling external service for quote 1
[11:06:11 ERR] BrokenCircuitException — circuit is open, request rejected instantly (no HTTP call made)
[11:06:12 INF] Calling external service for quote 1
[11:06:12 ERR] BrokenCircuitException — circuit is open, request rejected instantly
[11:06:13 INF] Calling external service for quote 1
[11:06:13 ERR] BrokenCircuitException — circuit is open, request rejected instantly

# ── Phase 4: Circuit goes HALF-OPEN after 30 s ───────────────────────────────
[11:06:40 WRN] [Polly CIRCUIT HALF-OPEN] Testing if service recovered...
[11:06:40 INF] [Client] Calling external service for quote 1   ← one test request allowed
[11:06:40 INF] Response: 200 OK

# ── Phase 5: Circuit CLOSES — service recovered ─────────────────────────────
[11:06:40 INF] [Polly CIRCUIT CLOSED] Circuit reset — service recovered!
[11:06:41 INF] [Client] Calling external service for quote 1   ← normal traffic resumes
```

---

## How to Run the Demo (PowerShell)

### Step 1 — Start the API
```powershell
cd QuotesAPI-Amey
dotnet run
# API listens on http://localhost:5000
```

### Step 2 — Enable forced failures
```powershell
Invoke-RestMethod -Method Post "http://localhost:5000/api/test/force-failure/true"
# {"forceFailure":true,"message":"Failure mode ON..."}
```

### Step 3 — Fire 8 parallel requests (watch terminal logs)
```powershell
# Each request triggers retries then reports back to the circuit breaker
# After 5 request-level failures the circuit opens
1..8 | ForEach-Object {
    Start-Job -ScriptBlock {
        try { Invoke-RestMethod "http://localhost:5000/api/quotes/external/1" }
        catch { $_.Exception.Message }
    }
} | Wait-Job | Receive-Job
```

**Watch the terminal** — you will see:
- `[Polly TIMEOUT]` every 5 seconds (per attempt)
- `[Polly RETRY]` with 2 s / 4 s / 8 s delays
- `[Polly CIRCUIT OPEN]` once 5 request-groups fail
- Subsequent calls returning 503 instantly (no `[Client] Calling...` log — circuit short-circuits)

### Step 4 — Confirm circuit is open (instant 503)
```powershell
Invoke-RestMethod "http://localhost:5000/api/quotes/external/1"
# Returns: 503 "Service temporarily unavailable — circuit is open"
# No [Client] log line printed — circuit blocked the call before HttpClient was used
```

### Step 5 — Recover the service
```powershell
Invoke-RestMethod -Method Post "http://localhost:5000/api/test/force-failure/false"
# {"forceFailure":false,"message":"Failure mode OFF..."}
```

### Step 6 — Wait 30 s for half-open, then send one request
```powershell
Start-Sleep 30
Invoke-RestMethod "http://localhost:5000/api/quotes/external/1"
```

**Watch the terminal** for:
1. `[Polly CIRCUIT HALF-OPEN]` — one probe request allowed through
2. Successful response
3. `[Polly CIRCUIT CLOSED]` — full recovery, normal traffic resumes

### Step 7 — Test bulkhead (optional)
Fire 35+ concurrent requests to see the bulkhead reject the overflow:
```powershell
1..35 | ForEach-Object {
    Start-Job { Invoke-RestMethod "http://localhost:5000/api/quotes/external/1" }
}
# Some jobs will return 429 with "Too many concurrent requests" — that is the bulkhead firing
```

---

## Screenshots to Take

Take the following screenshots and save them in the `screenshots/` folder next to this file.

| # | Filename | What to capture | Proves |
|---|---|---|---|
| 1 | `01-polly-packages.png` | Terminal: `dotnet list package` output showing Polly NuGet refs | Installed correctly |
| 2 | `02-pipeline-code.png` | VS Code: `Resilience/PollyPolicies.cs` full file visible | All 4 policies written |
| 3 | `03-retry-backoff-logs.png` | Terminal: `[Polly RETRY] Attempt 1/2/3` with 2s/4s/8s delays | Retry + backoff works |
| 4 | `04-circuit-opens.png` | Terminal: `[Polly CIRCUIT OPEN]` log line | Circuit breaker triggered |
| 5 | `05-open-rejecting.png` | Terminal: instant 503s (no `[Client] Calling...` log lines) | No HTTP calls while open |
| 6 | `06-half-open.png` | Terminal: `[Polly CIRCUIT HALF-OPEN]` log line | Probe request after 30 s |
| 7 | `07-circuit-closed.png` | Terminal: `[Polly CIRCUIT CLOSED]` + successful response | Full recovery proved |
| 8 | `08-timeout-log.png` | Terminal: `[Polly TIMEOUT] Request timed out after 5s` | Timeout policy fires |
| 9 | `09-bulkhead-log.png` | Terminal: `[Polly BULKHEAD] Too many concurrent requests` | Bulkhead policy fires |
| 10 | `10-github-repo.png` | GitHub repo page showing the `DAY22/Piece-1-` folder | Submitted correctly |

Screenshots 4, 6, and 7 are the most important for the mentor — they show the three circuit breaker states.

---

## NuGet Packages Added

```
Microsoft.Extensions.Http.Polly   10.0.9
Polly.Extensions.Http              3.0.0
Polly                              7.2.4   (transitive dependency)
```

## Files Added / Changed

```
QuotesAPI-Amey/
  Resilience/
    PollyPolicies.cs          ← NEW: all 4 Polly policies wrapped together
    IExternalQuoteClient.cs   ← NEW: interface + ExternalQuoteResponse record
    ExternalQuoteClient.cs    ← NEW: typed HttpClient calling /api/quotes/unstable/{id}
  Program.cs                  ← AddHttpClient + AddPolicyHandler wiring
  Extensions/
    ServiceCollectionExtensions.cs  ← 3 new endpoints + handlers
  appsettings.json            ← ExternalApi:BaseUrl added
```
