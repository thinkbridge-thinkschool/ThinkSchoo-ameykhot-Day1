# Day 5 – Piece 1: Diagnose a Slow Endpoint Using Your Traces

## Submission

**Author:** Amey Khot  
**Branch:** `day5/cloud-deployment-observability`  
**Folder:** `DAY5/Piece-1-Diagnose a slow endpoint using your traces/QuotesAPI-Amey`

---

## What Was Done

### Step 1 — Introduce the Slow Operation

Two intentional performance problems were added to `GET /api/quotes`:

**1. `Thread.Sleep(1500)` in the endpoint handler** (`Extensions/ServiceCollectionExtensions.cs`):

```csharp
// INTENTIONAL SLOW OPERATION — Day 5 Piece 1 trace diagnosis exercise.
Thread.Sleep(1500);
logger.LogInformation("Fetching quotes page {Page} size {Size}", page, size);
```

**2. N+1 query antipattern** in `GetQuotesAsync` (`Data/IQuoteRepository.cs`) — loads all IDs first, then queries each quote individually in a loop:

```csharp
// N+1: load all IDs, then query each quote one by one
var allIds = await _context.Quotes
    .OrderByDescending(q => q.CreatedAt)
    .Select(q => q.Id)
    .ToListAsync(cancellationToken);

var total = allIds.Count;
var pageIds = allIds.Skip((page - 1) * size).Take(size).ToList();

var items = new List<Quote>();
foreach (var id in pageIds)
{
    var quote = await _context.Quotes
        .FirstOrDefaultAsync(q => q.Id == id, cancellationToken);
    if (quote is not null) items.Add(quote);
}
```

---

## Before Trace — What Jaeger Showed

After running the app with Jaeger (`docker run -p 16686:16686 -p 4317:4317 jaegertracing/all-in-one`) and hitting `GET /api/quotes` 3 times:

```
Trace: GET /api/quotes                              Duration: ~1552ms
  ├─ [sleep]  (unmeasured — blocked thread)         ~1500ms
  ├─ SELECT Id FROM Quotes ORDER BY CreatedAt DESC  ~2ms   (load all IDs)
  ├─ SELECT * FROM Quotes WHERE Id = 1              ~1ms   (loop iter 1)
  ├─ SELECT * FROM Quotes WHERE Id = 2              ~1ms   (loop iter 2)
  ├─ SELECT * FROM Quotes WHERE Id = 3              ~1ms   (loop iter 3)
  ...
  └─ SELECT * FROM Quotes WHERE Id = N              ~1ms   (loop iter N)
```

**Key observations in Jaeger:**
- The root `GET /api/quotes` span showed `~1552ms` total duration.
- The `Thread.Sleep` did not produce a child span — the time simply disappeared inside the ASP.NET Core span as unaccounted "wall clock" time.
- EF Core instrumentation created `N+1` child spans: 1 ID-list query + N individual SELECT queries for a page of N quotes.
- On a 10-item page, this was 11 EF spans for what should be 2 queries (COUNT + paginated SELECT).

---

## Diagnosis Note (100 words)

> This trace showed the slow span was `GET /api/quotes` (~1552 ms) because of two stacked problems. First, a `Thread.Sleep(1500)` blocked the thread pool thread inside the handler — visible in Jaeger as a 1.5 s gap with no child span (wall-clock time with no work). Second, an N+1 query antipattern in `GetQuotesAsync` fired one `SELECT … WHERE Id = ?` per quote instead of a single paginated `SKIP/TAKE`. On a 10-item page this produced 11 EF Core child spans. I fixed it by removing the sleep and replacing the per-ID loop with `.Skip().Take().ToListAsync()`, collapsing all 11 EF spans into 2 and dropping response time to ~5 ms.

---

## After Trace — What Jaeger Showed

```
Trace: GET /api/quotes                              Duration: ~5ms
  ├─ SELECT COUNT(*) FROM Quotes                    ~1ms
  └─ SELECT * FROM Quotes ORDER BY … SKIP … TAKE … ~3ms
```

- Root span duration: **~5ms** (down from ~1552ms)
- EF Core child spans: **2** (down from N+1)
- No blocked thread — no missing time gap

---

## The Fix

### Remove `Thread.Sleep` (handler)

```csharp
// BEFORE
Thread.Sleep(1500);
logger.LogInformation("Fetching quotes page {Page} size {Size}", page, size);

// AFTER — sleep removed
logger.LogInformation("Fetching quotes page {Page} size {Size}", page, size);
```

### Fix N+1 (repository)

```csharp
// BEFORE — N+1 antipattern
var allIds = await _context.Quotes.Select(q => q.Id).ToListAsync(...);
foreach (var id in allIds.Skip(...).Take(...))
    items.Add(await _context.Quotes.FirstOrDefaultAsync(q => q.Id == id, ...));

// AFTER — single paginated query
var total = await _context.Quotes.CountAsync(cancellationToken);
var items = await _context.Quotes
    .OrderByDescending(q => q.CreatedAt)
    .Skip((page - 1) * size)
    .Take(size)
    .ToListAsync(cancellationToken);
```

---

## Bonus: KQL Query — Find Slow Endpoints in App Insights

Paste this into App Insights → Logs to surface any endpoint taking over 1 second:

```kusto
// Slow endpoint detection — any request over 1000ms
requests
| where timestamp > ago(1h)
| where duration > 1000
| project
    timestamp,
    name,
    url,
    duration,
    resultCode,
    success,
    operation_Id
| order by duration desc
```

### Variant — highlight N+1 patterns in dependency calls

EF Core queries appear in the `dependencies` table. A high `itemCount` for a single `operation_Id` is a N+1 signal:

```kusto
// Find operations that fired many DB queries (N+1 suspect)
dependencies
| where timestamp > ago(1h)
| where type == "InProc" or type contains "SQL"
| summarize queryCount = count(), totalDuration = sum(duration) by operation_Id
| where queryCount > 5
| join kind=inner (
    requests
    | project operation_Id, requestName = name, requestDuration = duration
) on operation_Id
| project timestamp = now(), requestName, queryCount, totalDuration, requestDuration, operation_Id
| order by queryCount desc
```

### Alert rule (threshold-based)

```kusto
// Alert if p95 latency for any endpoint exceeds 500ms in last 5 minutes
requests
| where timestamp > ago(5m)
| summarize p95 = percentile(duration, 95) by name
| where p95 > 500
| project name, p95
```

---

## Commits

| Hash | Message |
|------|---------|
| `9c50b73` | `feat(day5-p1): introduce Thread.Sleep and N+1 query for trace diagnosis demo` |
| `b419c5c` | `fix(day5-p1): remove Thread.Sleep and fix N+1 query in GET /api/quotes` |

---

## What I Learned

**Traces make invisible time visible.** The `Thread.Sleep` produced no child span — it was pure wall-clock time that Jaeger showed as a gap. Without tracing you would only see a slow endpoint; with tracing you see *where* the time went (or didn't go). That "gap with no children" is the first thing to look for when an endpoint is slow but all the spans look fast individually.

**N+1 is easy to miss in code, obvious in traces.** Reading the repository code, the loop looks reasonable. But in Jaeger you instantly see 11 EF spans for a 10-item page — the pattern is unmistakable. Tracing makes query count a *visual* property, not something you have to grep for.

---

## What Would Break This

1. **No Jaeger running** — if `localhost:4317` is unavailable the OTLP exporter silently drops spans. The app still works but you lose all trace data. Fix: configure an Aspire dashboard or use in-memory trace processor as a fallback.

2. **Thread.Sleep on a thread-pool thread under load** — under high concurrency the sleep starves the thread pool. Use `await Task.Delay(1500)` in async code instead; it yields the thread while waiting.

3. **N+1 at scale** — with 10,000 quotes in the DB the per-ID loop fires 10,000 queries. EF Core `FirstOrDefaultAsync` is not batched. The fix (`Skip/Take`) generates a single SQL `LIMIT/OFFSET` regardless of N.

4. **App Insights not connected** — the KQL queries only work if `UseAzureMonitor` is wired up and the connection string is present in Key Vault. Verify in App Insights → Transaction Search that traces appear before relying on KQL alerts.
