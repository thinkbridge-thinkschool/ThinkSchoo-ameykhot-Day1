# Day 5 – Piece 6: Add Polly Resilience to HTTP Calls

## What was built

An `ExternalQuoteService` that calls the public **ZenQuotes** API (`https://zenquotes.io/api/random`)
and is wrapped in a three-layer Polly resilience pipeline registered via
`Microsoft.Extensions.Http.Resilience` (Polly v8 under the hood).

### New files

| File | Purpose |
|---|---|
| `Services/IExternalQuoteService.cs` | Interface + `ExternalQuoteDto` record |
| `Services/ExternalQuoteService.cs` | Typed `HttpClient` service — calls ZenQuotes |
| `Quotes.Tests.Unit/ExternalQuoteResilienceTests.cs` | Two tests forcing transient 503s |
| `.github/workflows/day5-p6-polly.yml` | CI workflow for this piece |

### Changed files

| File | Change |
|---|---|
| `QuotesApi.csproj` | Added `Microsoft.Extensions.Http.Resilience 9.0.0` |
| `Quotes.Tests.Unit.csproj` | Added `Microsoft.Extensions.Http.Resilience 9.0.0` |
| `Extensions/ServiceCollectionExtensions.cs` | Registered service + Polly pipeline + `/api/quotes/inspire` endpoint |
| `appsettings.json` | Added `ExternalQuotes:BaseUrl` config key |

---

## Resilience pipeline config

```csharp
services.AddHttpClient<IExternalQuoteService, ExternalQuoteService>(
        client => client.BaseAddress = new Uri(baseUrl))
    .AddResilienceHandler("default", (pipeline, context) =>
    {
        var logger = context.ServiceProvider
            .GetRequiredService<ILogger<ExternalQuoteService>>();

        // 1. Retry — 3 attempts, exponential backoff + jitter, log every attempt
        pipeline.AddRetry(new HttpRetryStrategyOptions
        {
            MaxRetryAttempts = 3,
            BackoffType = DelayBackoffType.Exponential,
            UseJitter = true,
            Delay = TimeSpan.FromSeconds(1),
            OnRetry = args =>
            {
                logger.LogWarning("HTTP retry {Attempt}/3 for {Url} — waiting {Delay:g} — reason: {Reason}", ...);
                return default;
            }
        });

        // 2. Circuit breaker — opens after 50% failure rate over 30 s (min 2 requests)
        pipeline.AddCircuitBreaker(new HttpCircuitBreakerStrategyOptions
        {
            FailureRatio = 0.5,
            SamplingDuration = TimeSpan.FromSeconds(30),
            MinimumThroughput = 2,
            BreakDuration = TimeSpan.FromSeconds(30),
            OnOpened = args => { logger.LogError("Circuit breaker OPENED ..."); return default; },
            OnClosed = args => { logger.LogInformation("Circuit breaker CLOSED"); return default; }
        });

        // 3. Total timeout — 10 s for the full request (all retries included)
        pipeline.AddTimeout(TimeSpan.FromSeconds(10));
    });
```

---

## New endpoint

```
GET /api/quotes/inspire
```

Returns an inspirational quote fetched from the ZenQuotes API, protected by the Polly pipeline.
If all retries fail the endpoint returns `503 Service Unavailable`.

---

## Test output — forcing transient failures

**Test 1: `Polly_RetriesOnTransientFailure_AndSucceedsOnThirdAttempt`**

The `SequentialResponseHandler` returns 503, 503, then 200.
Polly fires `OnRetry` twice and the service ultimately returns the quote.

```
[RETRY] attempt=0 status=ServiceUnavailable
[RETRY] attempt=1 status=ServiceUnavailable

Summary: handler called 3×, retries logged: 2
```

Assertions:
- `result.Text == "Life is good"` ✅
- `handler.CallCount == 3` ✅  (initial + 2 retries)
- `retryLog.Count == 2` ✅

**Test 2: `Polly_ExhaustsAllRetries_WhenEveryAttemptFails`**

All 4 calls return 503. Polly exhausts MaxRetryAttempts = 3; `EnsureSuccessStatusCode()` throws.

```
[RETRY] attempt=0 status=ServiceUnavailable
[RETRY] attempt=1 status=ServiceUnavailable
[RETRY] attempt=2 status=ServiceUnavailable

Summary: handler called 4×, retries logged: 3
```

Assertions:
- Throws `HttpRequestException` ✅
- `handler.CallCount == 4` ✅  (initial + 3 retries)
- `retryLog.Count == 3` ✅

---

## What I learned

`Microsoft.Extensions.Http.Resilience` makes wiring up Polly v8 to a typed `HttpClient` a single
fluent call — `AddResilienceHandler`. The `(pipeline, context)` overload lets you resolve
`ILogger` from the DI container, so every retry is stamped in the same structured log as the
rest of the request rather than appearing in a separate trace.

## What would break this

The circuit breaker's `MinimumThroughput = 2` means it can trip on just two bad requests in a
30-second window. In a low-traffic environment or during a startup burst, two accidental timeouts
would open the circuit and block legitimate calls for 30 seconds. Setting a higher
`MinimumThroughput` (e.g. 10) reduces false trips at the cost of letting more failures through
before the breaker opens.
