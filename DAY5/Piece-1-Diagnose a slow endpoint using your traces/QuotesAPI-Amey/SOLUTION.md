# Day 5 – Piece 1: Diagnose a Slow Endpoint Using Your Traces

**Author:** Amey Khot  
**Branch:** `day5/cloud-deployment-observability`  
**Repo:** https://github.com/thinkbridge-thinkschool/ThinkSchoo-ameykhot-Day1

---

## Step 1 — Slow Operation Introduced

Two problems were deliberately added to `GET /api/quotes`:

### Problem 1: `Thread.Sleep(1500)` in the endpoint handler

```csharp
// Extensions/ServiceCollectionExtensions.cs — GetQuotes handler
Thread.Sleep(1500);   // blocks the thread-pool thread for 1.5 s with no async yield
logger.LogInformation("Fetching quotes page {Page} size {Size}", page, size);
```

### Problem 2: N+1 query antipattern in the repository

```csharp
// Data/IQuoteRepository.cs — GetQuotesAsync
// Step 1: load ALL quote IDs (full table scan)
var allIds = await _context.Quotes
    .OrderByDescending(q => q.CreatedAt)
    .Select(q => q.Id)
    .ToListAsync(cancellationToken);

var total = allIds.Count;
var pageIds = allIds.Skip((page - 1) * size).Take(size).ToList();

// Step 2: query EACH quote individually — one SELECT per row
var items = new List<Quote>();
foreach (var id in pageIds)
{
    var quote = await _context.Quotes
        .FirstOrDefaultAsync(q => q.Id == id, cancellationToken);
    if (quote is not null) items.Add(quote);
}
```

---

## Jaeger UI — Screenshots

### Jaeger Dashboard (service list)
![Jaeger UI](../jagger%20Ui.png)

### BEFORE — Slow Trace (~1.5–2.4 s per request)
![Before slow trace](../Before%20-slow%20trace.png)

### AFTER — Fast Trace (~7–17 ms per request)
![After fast trace](../After%20-fast%20trace.png)

---

## Step 2 — BEFORE Trace (OpenTelemetry Console Exporter — Real Output)

App was run with `OpenTelemetry.Exporter.Console` added to the tracing pipeline.  
Endpoint hit 3 times. Below is the **real span output** captured from the console.

### Request 1 — TraceId: `52d3b4bc580047d9916de81f1a581df3`

```
Activity.DisplayName:   GET /api/quotes/
Activity.Kind:          Server
Activity.TraceId:       52d3b4bc580047d9916de81f1a581df3
Activity.SpanId:        9a8801667851aedd
Activity.Duration:      00:00:02.6761971          ← ROOT SPAN: 2.676 s
  http.request.method:  GET
  url.path:             /api/quotes
  http.response.status_code: 200

  └─ Child span (SpanId: bf061b490b6a2e69)        ← N+1 Step 1: load all IDs
       Activity.DisplayName:  main
       Activity.Duration:     00:00:00.0725258
       db.statement: SELECT "q"."Id"
                     FROM "Quotes" AS "q"
                     ORDER BY "q"."CreatedAt" DESC

  └─ Child span (SpanId: 5d8ce2b87a5ebe84)        ← N+1 Step 2a: quote by ID
       Activity.DisplayName:  main
       Activity.Duration:     00:00:00.0193731
       db.statement: SELECT "q"."Id", "q"."Author", "q"."CreatedAt", "q"."OwnerId", "q"."Text"
                     FROM "Quotes" AS "q"
                     WHERE "q"."Id" = @id LIMIT 1

  └─ Child span (SpanId: 3e7ed46cb5255368)        ← N+1 Step 2b: quote by ID
       Activity.DisplayName:  main
       Activity.Duration:     00:00:00.0008089
       db.statement: SELECT "q"."Id", "q"."Author", "q"."CreatedAt", "q"."OwnerId", "q"."Text"
                     FROM "Quotes" AS "q"
                     WHERE "q"."Id" = @id LIMIT 1
```

> **Key observation:** Root span is **2.676 s** but all 3 EF child spans total only **~92 ms**.  
> The missing **~2.58 s** is the `Thread.Sleep` — wall-clock time with **no child span** and **no activity** at all. That invisible gap is the signature of a blocking sleep on the thread.

### Request 2 — TraceId: `ea6668d2191884b870a4aaf032514ea8`

```
Activity.DisplayName:   GET /api/quotes/
Activity.Duration:      00:00:01.7458192          ← 1.745 s (sleep + N+1)
  └─ SELECT Id list     00:00:00.0006427           ← N+1 Step 1
  └─ SELECT WHERE Id=?  00:00:00.0005204           ← N+1 Step 2a
  └─ SELECT WHERE Id=?  00:00:00.0004916           ← N+1 Step 2b
```

### Request 3 — TraceId: `161636407225a2cf69ddec9f56940c4a`

```
Activity.DisplayName:   GET /api/quotes/
Activity.Duration:      00:00:01.5301376          ← 1.530 s (sleep dominates)
  └─ SELECT Id list     00:00:00.0010515
  └─ SELECT WHERE Id=?  00:00:00.0009659
  └─ SELECT WHERE Id=?  00:00:00.0009275
```

### BEFORE Summary

| Metric | Value |
|--------|-------|
| Root span duration | **1.5 – 2.68 s** |
| EF child spans per request | **3** (1 ID list + N individual per-quote SELECTs) |
| SQL fired per page of 2 quotes | **3 queries** instead of 2 |
| Thread blocked by sleep | **Yes — 1500 ms, no child span** |
| `curl` reported response time | `2.75 s` / `2.75 s` / `2.75 s` |

---

## Step 3 — Diagnosis Note

> **Slow span:** `GET /api/quotes/` — root span duration **2.68 s** (requests 1–3: 2.68 s, 1.75 s, 1.53 s).
>
> **Cause 1 — Thread.Sleep(1500):** The root span shows a ~1.5 s gap with **zero child spans**. All EF children together took < 100 ms, yet the root lasted 2.68 s. That unaccounted wall-clock time is the signature of `Thread.Sleep` — the thread was parked but no activity was recorded. There is no span for the sleep itself, only the absence of one.
>
> **Cause 2 — N+1 query:** Each request produced **3 EF Core child spans** for a 2-quote page: one `SELECT Id FROM Quotes` (full scan to get all IDs), then one `SELECT … WHERE Id = @id LIMIT 1` per quote. On 100 quotes this would be 101 queries. The fix is a single `.Skip().Take().ToListAsync()` which collapses it to 2 queries (COUNT + paginated SELECT).
>
> **Fix:** Remove `Thread.Sleep(1500)`; replace the per-ID loop with a single paginated EF query.

---

## Step 4 — Fix Applied

### Fix 1: Remove the blocking sleep

```csharp
// BEFORE
Thread.Sleep(1500);
logger.LogInformation("Fetching quotes page {Page} size {Size}", page, size);

// AFTER — sleep removed
logger.LogInformation("Fetching quotes page {Page} size {Size}", page, size);
```

### Fix 2: Replace N+1 loop with efficient paginated query

```csharp
// BEFORE — N+1: loads all IDs then queries each quote individually
var allIds = await _context.Quotes.Select(q => q.Id).ToListAsync();
foreach (var id in pageIds)
    items.Add(await _context.Quotes.FirstOrDefaultAsync(q => q.Id == id));

// AFTER — 2 queries: COUNT + single paginated SELECT
var total = await _context.Quotes.CountAsync(cancellationToken);
var items = await _context.Quotes
    .OrderByDescending(q => q.CreatedAt)
    .Skip((page - 1) * size)
    .Take(size)
    .ToListAsync(cancellationToken);
```

**Fix commit:** [`b419c5c`](https://github.com/thinkbridge-thinkschool/ThinkSchoo-ameykhot-Day1/commit/b419c5c)  
`fix(day5-p1): remove Thread.Sleep and fix N+1 query in GET /api/quotes`

---

## Step 5 — AFTER Trace (Real Output — Fixed Version)

Same console exporter, same 3 requests after the fix.

### Request 1 — TraceId: `901d4892b2a60faf0defcf4468c89404`

```
Activity.DisplayName:   GET /api/quotes/
Activity.Kind:          Server
Activity.TraceId:       901d4892b2a60faf0defcf4468c89404
Activity.SpanId:        735cccdee77ab261
Activity.Duration:      00:00:00.7651442          ← ROOT SPAN: 765 ms (first request JIT warmup)
  http.request.method:  GET
  url.path:             /api/quotes
  http.response.status_code: 200

  └─ Child span (SpanId: 7114800c5eea51f2)        ← COUNT query
       Activity.DisplayName:  main
       Activity.Duration:     00:00:00.0560940
       db.statement: SELECT COUNT(*)
                     FROM "Quotes" AS "q"

  └─ Child span (SpanId: 3a3202a2527782fd)        ← paginated SELECT (all columns, SKIP/TAKE)
       Activity.DisplayName:  main
       Activity.Duration:     00:00:00.0148035
       db.statement: SELECT "q"."Id", "q"."Author", "q"."CreatedAt", "q"."OwnerId", "q"."Text"
                     FROM "Quotes" AS "q"
                     ORDER BY "q"."CreatedAt" DESC
                     LIMIT @p__linq__1 OFFSET @p__linq__0
```

### Request 2 — TraceId: `4102803f0c85db9d9e084247e76c6b0f`

```
Activity.DisplayName:   GET /api/quotes/
Activity.Duration:      00:00:00.2071219          ← 207 ms (still warming up)
  └─ SELECT COUNT(*)    00:00:00.0006869
  └─ SELECT … LIMIT ?   00:00:00.0007952
```

### Request 3 — TraceId: `3efa71e24c3921317ca27ce400462164`

```
Activity.DisplayName:   GET /api/quotes/
Activity.Duration:      00:00:00.0155721          ← 15.5 ms (fully warmed up ✅)
  └─ SELECT COUNT(*)    00:00:00.0006810
  └─ SELECT … LIMIT ?   00:00:00.0005978
```

### AFTER Summary

| Metric | Value |
|--------|-------|
| Root span duration (warmed up) | **15.5 ms** |
| EF child spans per request | **2** (COUNT + paginated SELECT) |
| SQL fired per page | **2 queries** |
| Thread blocked | **No** |
| `curl` reported response time | `825 ms` / `210 ms` / **`17 ms`** |

---

## Before vs After — Side-by-Side Comparison

| Metric | BEFORE (slow) | AFTER (fixed) | Change |
|--------|--------------|---------------|--------|
| Root span duration | 1.53 – 2.68 s | **15.5 ms** (warm) | **~100× faster** |
| EF child spans | 3 per request | **2 per request** | −1 (N+1 eliminated) |
| SQL per page of N quotes | N+1 queries | **2 queries** | O(N) → O(1) |
| Thread.Sleep visible | Yes (gap, no child span) | **Gone** | ✅ |
| `SELECT WHERE Id=@id` loop | Yes | **Gone** | ✅ |

> **The slow span is confirmed gone.** Trace `3efa71e24c39...` shows `GET /api/quotes/` completing in **15.5 ms** with exactly 2 EF child spans (COUNT + LIMIT/OFFSET) and no time gap.

---

## Bonus: KQL Queries for App Insights

### 1. Find all slow endpoints (over 1 second)

```kusto
requests
| where timestamp > ago(1h)
| where duration > 1000
| project timestamp, name, url, duration, resultCode, operation_Id
| order by duration desc
```

### 2. Detect N+1 — find requests that fired many DB calls

EF Core queries appear in the `dependencies` table. A high count per `operation_Id` is the N+1 signal:

```kusto
dependencies
| where timestamp > ago(1h)
| where type == "InProc" or type has "SQL"
| summarize queryCount = count(), totalDbMs = sum(duration) by operation_Id
| where queryCount > 5
| join kind=inner (
    requests
    | project operation_Id, requestName = name, requestDuration = duration
) on operation_Id
| project requestName, queryCount, totalDbMs, requestDuration, operation_Id
| order by queryCount desc
```

### 3. p95 latency alert — endpoints slower than 500 ms

```kusto
requests
| where timestamp > ago(5m)
| summarize p95 = percentile(duration, 95) by name
| where p95 > 500
| project name, p95
| order by p95 desc
```

### 4. Find the "invisible gap" — requests where non-DB time is high

Requests where total duration >> sum of all dependency durations (the Thread.Sleep signature):

```kusto
let reqDurations = requests
    | where timestamp > ago(1h)
    | project operation_Id, requestName = name, requestDuration = duration;
let depDurations = dependencies
    | where timestamp > ago(1h)
    | summarize totalDepMs = sum(duration) by operation_Id;
reqDurations
| join kind=leftouter depDurations on operation_Id
| extend gapMs = requestDuration - coalesce(totalDepMs, 0)
| where gapMs > 500
| project requestName, requestDuration, totalDepMs, gapMs, operation_Id
| order by gapMs desc
```

> This last query is what would have **directly found the Thread.Sleep** — the root span duration was 2676 ms but all DB children together were only 92 ms, leaving a gap of ~2584 ms.

---

## Commit History

| Hash | Message |
|------|---------|
| `9c50b73` | `feat(day5-p1): introduce Thread.Sleep and N+1 query for trace diagnosis demo` |
| `b419c5c` | `fix(day5-p1): remove Thread.Sleep and fix N+1 query in GET /api/quotes` |
| `7900a89` | `docs(day5-p1): add SOLUTION.md and update README with Day 5 observability exercise` |

---

## What I Learned

**Traces make invisible time visible.** `Thread.Sleep(1500)` produces no child span — it shows up only as a gap. Looking at the BEFORE trace: the root span was 2.68 s, but all children together were 92 ms. That missing ~2.5 s is where the sleep hid. Without tracing you know the endpoint is slow; with tracing you can see *which part* is slow and *why*.

**N+1 is obvious in traces, invisible in code.** Reading the repository code the loop looks reasonable. In the trace it's immediately obvious: 3 DB child spans for a 2-item page, with SQL showing `WHERE "q"."Id" = @id LIMIT 1` repeated. The query pattern is visible before you even look at the code.

**The "gap query" in KQL.** The most powerful App Insights query isn't "find slow requests" — it's "find requests where the wall time is much bigger than the DB time." That directly surfaces Thread.Sleep and any other non-observable blocking work.

---

## What Would Break This

1. **Thread pool starvation under load.** `Thread.Sleep` on a thread-pool thread under concurrent load consumes a worker thread. At high concurrency this queues requests. The fix is `await Task.Delay(1500)` which yields the thread back to the pool while waiting.

2. **N+1 scales with data.** With 2 quotes in the DB the N+1 adds only 3 extra queries. With 1000 quotes on a page it adds 1001 queries. The performance cliff is invisible in dev with a small DB.

3. **No Jaeger/OTLP receiver.** If `localhost:4317` is unavailable the OTLP exporter silently drops all spans. Traces still appear via the console exporter (development) but are invisible to Jaeger/Aspire. The app keeps working.

4. **App Insights not connected.** KQL queries only work if `UseAzureMonitor` is wired up and the Key Vault secret is present. The code now skips Azure Monitor when the connection string is missing so local dev still starts cleanly.
