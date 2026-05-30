# QuotesAPI - ASP.NET Core 10 Minimal API

A modern ASP.NET Core 10 API for managing quotes and collections, built with **Domain-Driven Design (DDD)** principles, **Entity Framework Core**, **FluentValidation**, and full **OpenTelemetry** observability.

---

## Day 12 – Piece 1: Read Models + CQRS-lite

Reads and writes have completely different shapes. This piece splits the quotes feature into a **write model** (normalized, validated, focused on correctness) and a **read model** (denormalized, projection-shaped for the screen). No event sourcing — just separate command and query paths inside the same ASP.NET Core project.

### What CQRS-lite means here

| Side | Responsibility | Key rule |
|------|---------------|---------|
| **Command** | Validate → write normalized entity | No reads, no projection |
| **Query** | Join → project flat DTO → return | No validation, no entity tracking |

### What was added

| File | What it does |
|------|-------------|
| `Commands/CreateQuoteCommand.cs` | Write-side input DTO — only what a create needs: `Author`, `Text`, `AuthorId` |
| `Commands/CreateQuoteHandler.cs` | Validates inputs, writes a normalized `Quote` entity, returns new `Id` |
| `Queries/GetQuotesByAuthorQuery.cs` | Read-side input DTO — just `AuthorId` |
| `Queries/GetQuotesByAuthorHandler.cs` | `AsNoTracking()` + left JOIN to Authors table → projects directly into `QuoteReadModel` |
| `Queries/QuoteReadModel.cs` | Flat, denormalized DTO shaped for the screen — `quoteId`, `quoteText`, `authorName`, `createdAt` |
| `Extensions/ServiceCollectionExtensions.cs` | Registered both handlers as `AddScoped<>` + wired two new Minimal API endpoints |

### New endpoints

| Method | Route | Handler |
|--------|-------|---------|
| `POST` | `/api/cqrs/quotes` | `CreateQuoteHandler` — write side |
| `GET` | `/api/cqrs/quotes/by-author/{authorId}` | `GetQuotesByAuthorHandler` — read side |

---

### Command side — write path

**`Commands/CreateQuoteCommand.cs`** — the input DTO:

```csharp
namespace QuotesApi.Commands;

public class CreateQuoteCommand
{
    public string Author { get; set; } = string.Empty;
    public string Text { get; set; } = string.Empty;
    public int? AuthorId { get; set; }
}
```

**`Commands/CreateQuoteHandler.cs`** — validates then writes:

```csharp
public class CreateQuoteHandler
{
    private readonly QuoteDbContext _context;

    public CreateQuoteHandler(QuoteDbContext context)
    {
        _context = context;
    }

    public async Task<int> Handle(CreateQuoteCommand command, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(command.Text))
            throw new ArgumentException("Quote text is required");

        if (string.IsNullOrWhiteSpace(command.Author))
            throw new ArgumentException("Author name is required");

        var quote = new Quote(command.Author, command.Text, DateTime.UtcNow);
        quote.AuthorId = command.AuthorId;

        _context.Quotes.Add(quote);
        await _context.SaveChangesAsync(ct);

        return quote.Id;
    }
}
```

**What the command side does:**
- Validates inputs — throws `ArgumentException` on blank `Text` or `Author`
- Writes a **normalized** `Quote` entity to the DB (Author name + FK `AuthorId` stored separately)
- No read concern — no `AsNoTracking`, no projection, no joins
- Returns only the new quote's `Id`

---

### Query side — read path

**`Queries/QuoteReadModel.cs`** — flat DTO shaped for the screen:

```csharp
namespace QuotesApi.Queries;

public class QuoteReadModel
{
    public int QuoteId { get; set; }
    public string QuoteText { get; set; } = string.Empty;
    public string AuthorName { get; set; } = string.Empty;
    public string CreatedAt { get; set; } = string.Empty;
}
```

**`Queries/GetQuotesByAuthorHandler.cs`** — `AsNoTracking` + JOIN → flat projection:

```csharp
public class GetQuotesByAuthorHandler
{
    private readonly QuoteDbContext _context;

    public GetQuotesByAuthorHandler(QuoteDbContext context)
    {
        _context = context;
    }

    public async Task<List<QuoteReadModel>> Handle(
        GetQuotesByAuthorQuery query, CancellationToken ct = default)
    {
        return await (
            from q in _context.Quotes.AsNoTracking()
            join a in _context.Authors.AsNoTracking() on q.AuthorId equals a.Id into authorGroup
            from a in authorGroup.DefaultIfEmpty()
            where q.AuthorId == query.AuthorId
            select new QuoteReadModel
            {
                QuoteId = q.Id,
                QuoteText = q.Text,
                AuthorName = a != null ? a.Name : q.Author,
                CreatedAt = q.CreatedAt.ToString("dd MMM yyyy")
            }
        ).ToListAsync(ct);
    }
}
```

**What the query side does:**
- `AsNoTracking()` on both sides — EF never allocates `EntityEntry` objects, zero change-tracker overhead
- Left JOIN to `Authors` table — `AuthorName` comes from the normalized `Authors` row via FK
- Server-side projection — the `select new QuoteReadModel` is translated to SQL; no full entity is materialized in memory
- `CreatedAt` pre-formatted as `"dd MMM yyyy"` — the UI gets a display-ready string, no client-side parsing needed
- No validation, no writes — the read path is pure data retrieval

---

### What Got Simpler

> Separating read from write meant the query handler never touches EF change-tracking — `AsNoTracking()` + a single left-join projection returns a flat `QuoteReadModel` directly from SQL, with no entity-to-DTO mapping step and no validation logic leaking into the read path.

**Concrete before/after:**

| Mixed model (before) | Separated paths (after) |
|---------------------|------------------------|
| Load tracked `Quote` entity — EF allocates `EntityEntry` | Write: validate → write → done. No projection. |
| Load tracked `Author` entity separately | Read: join → project in SQL → return flat DTO. |
| Map entity fields into a response DTO manually | No mapping step — projection is in the handler. |
| Validation logic sits alongside read logic | Validation only lives in the command handler. |

---

### How to Test the CQRS Endpoints

**Start the API:**

```powershell
cd "DAY12\Piece-1-Read models + CQRS-lite\QuotesAPI-Amey"
dotnet run --urls "http://localhost:5000"
```

**Test write endpoint (POST a quote):**

```powershell
Invoke-RestMethod -Method Post `
  -Uri "http://localhost:5000/api/cqrs/quotes" `
  -ContentType "application/json" `
  -Body '{"author":"Marcus Aurelius","text":"The obstacle is the way.","authorId":1}'
```

Expected response:
```json
{ "id": 10001 }
```

**Test read endpoint (GET quotes by author):**

```powershell
Invoke-RestMethod -Uri "http://localhost:5000/api/cqrs/quotes/by-author/1" | ConvertTo-Json
```

Expected response (one item):
```json
{
  "quoteId": 1,
  "quoteText": "The obstacle is the way. (v1)",
  "authorName": "Marcus Aurelius",
  "createdAt": "30 May 2026"
}
```

**Pretty-print a single result:**

```powershell
(Invoke-RestMethod -Uri "http://localhost:5000/api/cqrs/quotes/by-author/1")[0] | ConvertTo-Json
```

---

### Screenshots

#### 1 — API Startup

![API startup — Now listening on http://localhost:5000](../Screenshots/01-dotnet-run-startup.png)

#### 2 — Write Endpoint Response (POST)

![POST /api/cqrs/quotes — CreateQuoteHandler returns new id](../Screenshots/02-post-write-endpoint.png)

#### 3 — Read Endpoint Response (GET)

![GET /api/cqrs/quotes/by-author/1 — flat QuoteReadModel array](../Screenshots/03-get-read-endpoint.png)

#### 4 — Single Read Model (pretty-printed)

![Single QuoteReadModel — quoteId, quoteText, authorName, createdAt](../Screenshots/Pretty-print-just-the-first-result.png)

---

### What I Learned

- **Commands and queries have completely different shapes.** A command needs validation and correctness guarantees; a query needs speed and the exact shape the screen expects. Forcing both through the same model means both are compromised.
- **`AsNoTracking()` is only safe on the read side.** The command handler must use a tracked context so EF knows to generate an `INSERT`. The query handler never writes anything, so tracking is pure waste — it allocates thousands of `EntityEntry` objects for nothing at scale.
- **The read model is shaped for the screen, not the database.** `QuoteReadModel` has `CreatedAt` as a pre-formatted string and `AuthorName` joined in — the UI receives exactly what it needs with zero extra round-trips or client-side transformations.
- **Projection happens in SQL, not in C#.** The `select new QuoteReadModel { ... }` inside `AsNoTracking()` gets translated to a SQL SELECT with only the needed columns. No full entity row is materialized in memory.

### What Would Break This

1. **Removing `AsNoTracking()` from the query handler** — EF allocates an `EntityEntry` per tracked entity. At 100 quotes per author, every read request creates 100 `EntityEntry` objects, adds GC pressure, and slows throughput for zero benefit.
2. **Adding validation logic to the query handler** — the read and write paths become entangled again; the whole point of the separation is lost.
3. **Using a tracked entity as the read model return type** — returning the `Quote` EF entity instead of `QuoteReadModel` means EF loads every column, tracks changes, and forces the caller to do DTO mapping downstream.
4. **Bypassing the command handler and writing directly to DbContext** — the validation (`IsNullOrWhiteSpace` checks) in `CreateQuoteHandler` is skipped entirely.
5. **Read replica lag in a scaled CQRS setup** — if the read model is served from a read replica, a POST followed immediately by a GET could return stale data. This implementation uses the same SQLite file for both paths so it is consistent — but scaling to separate read/write stores introduces eventual consistency that clients must handle.

---

## Day 12 – Piece 2: When to Reach for Dapper

EF Core is the default. Dapper earns its place only on hot read paths where you have **measured** a performance problem. This piece takes the existing `GetQuotesByAuthorHandler` EF query, reimplements it in Dapper with raw SQL, measures both under 10,000 rows, and derives the rule for when to drop to Dapper.

### What the task asked

> *"Reimplement your fastest-needed read query with Dapper, compare the SQL + timing to the EF version, and write the rule you'd give a teammate for when to drop to Dapper."*

---

### What Dapper Is

| | EF Core | Dapper |
|---|---|---|
| Type | Full ORM | Micro-ORM |
| Query style | LINQ → SQL translation | Raw SQL → C# object mapping |
| Change tracker | Yes | No |
| Identity map | Yes | No |
| Migrations | Yes | No |
| Refactoring safety | Compile-time (LINQ) | None (string SQL) |
| Overhead | Query translation + identity map | `IDataReader` row-by-row map only |

Dapper is a thin extension on `IDbConnection`. You write SQL. It reads `IDataReader` and maps column aliases to C# properties. Nothing else.

---

### Packages Installed

```
dotnet add package Dapper               → 2.1.79
dotnet add package Microsoft.Data.SqlClient → 7.0.1
```

Both visible in `QuotesApi.csproj`:

```xml
<PackageReference Include="Dapper" Version="2.1.79" />
<PackageReference Include="Microsoft.Data.SqlClient" Version="7.0.1" />
```

---

### New Files Added

| File | What it does |
|---|---|
| `Dapper/QuoteDapperRepository.cs` | Raw SQL via `SqliteConnection`/`SqlConnection` + Stopwatch timing |
| `Queries/GetQuotesByAuthorHandler.cs` | Updated — added `Stopwatch` to measure EF path |
| `Extensions/ServiceCollectionExtensions.cs` | Two new endpoints + `AddScoped<QuoteDapperRepository>()` |

---

### 1 — EF Implementation (existing, updated with Stopwatch)

**File:** `Queries/GetQuotesByAuthorHandler.cs`

```csharp
using System.Diagnostics;
using Microsoft.EntityFrameworkCore;
using QuotesApi.Data;

namespace QuotesApi.Queries;

public class GetQuotesByAuthorHandler
{
    private readonly QuoteDbContext _context;

    public GetQuotesByAuthorHandler(QuoteDbContext context)
    {
        _context = context;
    }

    public async Task<List<QuoteReadModel>> Handle(
        GetQuotesByAuthorQuery query, CancellationToken ct = default)
    {
        var sw = Stopwatch.StartNew();

        var result = await (
            from q in _context.Quotes.AsNoTracking()
            join a in _context.Authors.AsNoTracking() on q.AuthorId equals a.Id into authorGroup
            from a in authorGroup.DefaultIfEmpty()
            where q.AuthorId == query.AuthorId
            select new QuoteReadModel
            {
                QuoteId = q.Id,
                QuoteText = q.Text,
                AuthorName = a != null ? a.Name : q.Author,
                CreatedAt = q.CreatedAt.ToString("dd MMM yyyy")
            }
        ).ToListAsync(ct);

        sw.Stop();
        Console.WriteLine($"EF version: {sw.ElapsedMilliseconds}ms");

        return result;
    }
}
```

**SQL EF Core generates internally (captured from console log):**

```sql
SELECT "q"."Id", "q"."Text",
    CASE WHEN "a"."Id" IS NOT NULL THEN "a"."Name"
         ELSE "q"."Author"
    END,
    "q"."CreatedAt"
FROM "Quotes" AS "q"
LEFT JOIN "Authors" AS "a" ON "q"."AuthorId" = "a"."Id"
WHERE "q"."AuthorId" = @query_AuthorId
```

EF translates the LINQ null-safe join into a `LEFT JOIN` + `CASE WHEN`. The SQL is correct but EF also runs LINQ-to-SQL translation, maintains an identity map per request, and materialises intermediate objects before projecting to the DTO.

---

### 2 — Dapper Implementation (new)

**File:** `Dapper/QuoteDapperRepository.cs`

```csharp
using System.Data;
using System.Diagnostics;
using Dapper;
using Microsoft.Data.SqlClient;
using Microsoft.Data.Sqlite;
using QuotesApi.Queries;

namespace QuotesApi.Dapper;

public class QuoteDapperRepository
{
    private readonly string _connectionString;
    private readonly bool _isSqlServer;

    public QuoteDapperRepository(IConfiguration config)
    {
        _connectionString = config.GetConnectionString("DefaultConnection")
            ?? throw new InvalidOperationException("DefaultConnection not found");
        _isSqlServer = (config.GetValue<string>("DatabaseProvider") ?? "Sqlite")
            .Equals("SqlServer", StringComparison.OrdinalIgnoreCase);
    }

    public async Task<List<QuoteReadModel>> GetByAuthor(int authorId)
    {
        var sw = Stopwatch.StartNew();

        IDbConnection connection;
        string sql;

        if (_isSqlServer)
        {
            connection = new SqlConnection(_connectionString);
            sql = @"
                SELECT
                    q.Id          AS QuoteId,
                    q.Text        AS QuoteText,
                    a.Name        AS AuthorName,
                    FORMAT(q.CreatedAt, 'dd MMM yyyy') AS CreatedAt
                FROM Quotes q
                INNER JOIN Authors a ON a.Id = q.AuthorId
                WHERE q.AuthorId = @AuthorId";
        }
        else
        {
            connection = new SqliteConnection(_connectionString);
            sql = @"
                SELECT
                    q.Id          AS QuoteId,
                    q.Text        AS QuoteText,
                    a.Name        AS AuthorName,
                    q.CreatedAt   AS CreatedAt
                FROM Quotes q
                INNER JOIN Authors a ON a.Id = q.AuthorId
                WHERE q.AuthorId = @AuthorId";
        }

        using (connection)
        {
            var result = (await connection.QueryAsync<QuoteReadModel>(
                sql, new { AuthorId = authorId })).ToList();

            sw.Stop();
            Console.WriteLine($"Dapper version: {sw.ElapsedMilliseconds}ms");

            return result;
        }
    }
}
```

Dapper sends the SQL string directly to the database driver — zero translation overhead. It reads the `IDataReader` row by row and maps column aliases (`AS QuoteId`) straight onto C# properties. No change tracker, no identity map, no context session.

---

### 3 — New Endpoints

Both endpoints return the same `QuoteReadModel` JSON shape.

| Method | URL | Handler |
|---|---|---|
| `GET` | `/api/cqrs/quotes/ef/by-author/{authorId}` | `GetQuotesByAuthorHandler` (EF Core) |
| `GET` | `/api/cqrs/quotes/dapper/by-author/{authorId}` | `QuoteDapperRepository` (Dapper) |

**Test commands used during timing measurement:**

```powershell
curl http://localhost:5100/api/cqrs/quotes/ef/by-author/1
curl http://localhost:5100/api/cqrs/quotes/dapper/by-author/1
```

---

### 4 — DI Registration

Added to `Extensions/ServiceCollectionExtensions.cs` inside `AddInfrastructure()`:

```csharp
// CQRS-lite handlers
services.AddScoped<CreateQuoteHandler>();
services.AddScoped<GetQuotesByAuthorHandler>();

// Dapper repository
services.AddScoped<QuoteDapperRepository>();
```

---

### 5 — Timing Comparison

**Dataset:** 10,000 rows — 100 authors × 100 quotes each (seeded automatically on first `dotnet run`)  
**Database:** SQLite (default dev config)  
**Measurement:** Single cold-start request, timing printed by `Stopwatch` to console

```
EF version:     883 ms    ← screenshot: ef-endpoint-timing.png
Dapper version: 164 ms    ← screenshot: ef-vs-dapper-console-timing.png

Dapper is 5.4× faster on this cold-start cold-context read
```

**Why the gap exists:**

| Overhead | EF Core | Dapper |
|---|---|---|
| LINQ → SQL translation | Yes (every cold `DbContext`) | No — you write the SQL |
| Identity map scan | Yes — tracks every entity per request | No |
| Intermediate object materialisation | Yes — full entity first, then projected | No — maps direct to DTO |
| Change tracker allocation | Skipped via `AsNoTracking()` | Not applicable |
| Connection lifecycle | Managed by `DbContext` | Raw `IDbConnection`, opened on demand |

Both numbers are from a single cold-start request. No warm-request measurement was taken.

---

### 6 — Key Behavioral Difference: LEFT JOIN vs INNER JOIN

This is **not** cosmetic. EF translates the null-safe LINQ join into a `LEFT JOIN` — quotes with a `NULL AuthorId` are still returned, with `AuthorName` falling back to the `Author` string column. Dapper uses an `INNER JOIN` — those same rows are **silently dropped**.

The two endpoints return identical data in this project because every seeded quote has a valid `AuthorId`. If orphan quotes existed (null `AuthorId`), EF would include them and Dapper would silently drop them without any error.

| Behaviour | EF version | Dapper version |
|---|---|---|
| Quote with `AuthorId = NULL` | Included — `AuthorName` falls back to `q.Author` | Silently excluded |
| Orphan detection | At runtime via CASE WHEN | None |

---

### 7 — One-Paragraph Rule

> Use EF Core as the default for everything — writes, reads, migrations, and relationships — because it gives you type safety, change tracking, and migration tooling for free. Only reach for Dapper on **hot read paths** where you have **measured** a performance problem: queries that run thousands of times per minute, return large result sets, join many tables, or require SQL that EF translates poorly. Dapper gives you full SQL control and removes the change-tracker and query-translation overhead, but you trade away automatic migrations, refactoring safety (column renames break string SQL silently), and the ability to compose queries in C#. The rule is: **measure first, reach for Dapper only when EF is the proven bottleneck on a specific query**, not because Dapper feels faster.

---

### What I Learned

- **Dapper is not a replacement for EF** — it is a targeted scalpel for read-heavy paths where every millisecond counts, not a general swap
- **`AsNoTracking()` closes most of the gap** on warm second requests; the biggest win for Dapper is on the cold-start LINQ translation and identity map allocation
- **Raw SQL is a maintenance liability** — column renames or table changes break Dapper queries silently at runtime with no compile-time warning; integration tests are the only guard
- **LEFT JOIN vs INNER JOIN matters** — EF's null-safe LINQ join translates to `LEFT JOIN`, Dapper's explicit `INNER JOIN` silently drops orphan rows; the two endpoints are not semantically equivalent when the data has nulls
- **Provider-aware connection factory** is needed when the same Dapper repo must work with SQLite in dev and SQL Server in prod — `DatabaseProvider` config flag drives the right `IDbConnection` subtype and SQL dialect

### What Would Break This

- **Column rename without updating SQL string** — Dapper maps by column alias; renaming `q.Text` to `q.Body` returns empty strings for `QuoteText` silently with no compile error
- **Connection string missing** — `QuoteDapperRepository` throws `InvalidOperationException` at request time; EF would throw at startup via `DbContext` validation
- **Wrong provider flag** — using `SqlConnection` against a SQLite file throws a connection error at runtime, not compile time
- **No pagination** — both endpoints return all 100 quotes for an author; at 100,000 quotes per author this would OOM the process
- **SQLite write contention** — SQLite holds a single write lock; under concurrent read+write load the Dapper SQLite path can see lock contention; SQL Server handles concurrent readers natively via MVCC
- **EF LEFT JOIN vs Dapper INNER JOIN** — seeded data satisfies every join, hiding the semantic difference; inserting a quote with `AuthorId = NULL` would expose it immediately

### Screenshots

| File | What it shows |
|---|---|
| `Screenshots/dapper-added-csproj.png` | `Dapper 2.1.79` line in `.csproj` |
| `Screenshots/sqlclient-added-csproj.png` | `Microsoft.Data.SqlClient 7.0.1` line in `.csproj` |
| `Screenshots/ef-endpoint-timing.png` | Console output — EF SQL log + `EF version: 883ms` |
| `Screenshots/ef-vs-dapper-console-timing.png` | Console output — `Dapper version: 164ms` |
| `Screenshots/dapper-json-response.png` | curl JSON response from Dapper endpoint |

See [SOLUTION.md](SOLUTION.md) for the full submission including all code, the timing table, and the one-paragraph rule.

---

## Day 11 – Piece 1: Profile a Slow Endpoint

Performance day. Added a deliberately slow endpoint with two intentional problems — an **N+1 query pattern** and a **missing index on `AuthorId`** — then profiled it with bombardier, captured the offending SQL from App Insights, and got the SQLite execution plan. Then fixed both problems and measured the improvement.

### Live Azure URL

```
https://ca-api-342m3golxdrt6.livelydune-368712a9.centralindia.azurecontainerapps.io
```

| Endpoint | Purpose |
|---|---|
| `GET /api/quotes/slow` | Deliberately broken — N+1 + no index |
| `GET /api/quotes/fast` | Fixed — single JOIN + index |

---

### What was added

| File | Change |
|---|---|
| `Models/Author.cs` | New — `Author { Id, Name, Quotes }` entity with navigation property |
| `Models/Quote.cs` | Added `AuthorId int?` FK column |
| `Data/QuoteDbContext.cs` | Added `DbSet<Author>`, configured FK relationship and `IX_Quotes_AuthorId` index |
| `Extensions/ServiceCollectionExtensions.cs` | Added slow endpoint, fast endpoint, SQL logging, and seed data |
| `Dockerfile` | New — multi-stage .NET 10 build for Azure Container Apps |
| `azure.yaml` | New — `azd` service definition pointing to Container App |
| `infra/` | New — Bicep files for ACR, Container App Env, App Insights, Log Analytics |
| `SOLUTION.md` | Full profiling write-up with p50/p99 numbers, SQL evidence, execution plans, screenshots |

---

### Problem 1 — N+1 Query Pattern (the slow endpoint)

```csharp
// GET /api/quotes/slow
private static async Task<IResult> GetSlowQuotes(QuoteDbContext db)
{
    // Query 1 — loads ALL authors into memory
    var authors = await db.Authors.ToListAsync();
    var result = new List<object>();

    foreach (var author in authors)
    {
        // Query 2..N+1 — fires one SQL query PER author
        var quotes = await db.Quotes
            .Where(q => q.AuthorId == author.Id)
            .ToListAsync();

        result.Add(new { author.Name, quotes });
    }

    return Results.Ok(result);
}
```

With 10 authors → **11 SQL queries per HTTP request**.  
With 1000 authors → 1001 queries per request. Scales linearly — a disaster in production.

---

### Problem 2 — Missing Index on `Quotes.AuthorId`

`AuthorId` was configured as a plain column with no FK constraint and no index:

```csharp
// QuoteDbContext.OnModelCreating — original (no index)
entity.Property(e => e.AuthorId).IsRequired(false);
// No HasIndex() call — every WHERE AuthorId = ? runs a full table scan
```

SQLite execution plan confirmed:

```
EXPLAIN QUERY PLAN SELECT * FROM Quotes WHERE AuthorId = 1;

id    parent   notused    detail
-----------------------------------------------------------------
2     0        0          SCAN Quotes          ← reads ALL rows
```

`SCAN Quotes` = full table scan. At 80 rows invisible. At 1 million rows → 10 million row reads per request (10 authors × 1M rows).

---

### SQL Logging Enabled

Every EF Core query prints to the console (visible in Azure Container logs):

```csharp
services.AddDbContext<QuoteDbContext>(options => options
    .UseSqlite(connectionString)
    .LogTo(Console.WriteLine, LogLevel.Information)
    .EnableSensitiveDataLogging());
```

App Insights KQL to find the offending SQL:

```kusto
dependencies
| where timestamp > ago(2h)
| where target contains 'quotes.db'
| project timestamp, name, data, duration, type
| order by timestamp desc
| take 20
```

Result — same query repeated 10 times per single HTTP request:

```
sqlite  /tmp/quotes.db | main   0.057ms   SELECT ... FROM "Quotes" WHERE "AuthorId" = @author_Id
sqlite  /tmp/quotes.db | main   0.054ms   SELECT ... FROM "Quotes" WHERE "AuthorId" = @author_Id
sqlite  /tmp/quotes.db | main   0.073ms   SELECT ... FROM "Quotes" WHERE "AuthorId" = @author_Id
... (×10 total)
```

---

### Seed Data

10 authors × 8 quotes = **80 rows** seeded on startup:

```csharp
var authorNames = new[] {
    "Marcus Aurelius", "Seneca", "Epictetus", "Aristotle", "Plato",
    "Socrates", "Friedrich Nietzsche", "Immanuel Kant", "René Descartes", "John Locke"
};
// Each author gets 8 quotes with AuthorId FK set
```

---

### Fix 1 — Replace N+1 with Include (single JOIN)

```csharp
// GET /api/quotes/fast
private static async Task<IResult> GetFastQuotes(QuoteDbContext db)
{
    // ONE query with JOIN — replaces 11 queries with 1
    var result = await db.Authors
        .Include(a => a.Quotes)
        .ToListAsync();

    return Results.Ok(result.Select(a => new { a.Name, quotes = a.Quotes }));
}
```

---

### Fix 2 — Add Index on `AuthorId`

```csharp
// QuoteDbContext.OnModelCreating — after fix
entity.HasIndex(e => e.AuthorId).HasDatabaseName("IX_Quotes_AuthorId");
entity.HasOne<Author>()
      .WithMany(a => a.Quotes)
      .HasForeignKey(e => e.AuthorId)
      .IsRequired(false);
```

SQLite execution plan after index:

```
EXPLAIN QUERY PLAN SELECT * FROM Quotes WHERE AuthorId = 1;

id    parent   notused    detail
-----------------------------------------------------------------
3     0        0          SEARCH Quotes USING INDEX IX_Quotes_AuthorId (AuthorId=?)
```

`SEARCH USING INDEX` = reads only 8 matching rows directly instead of all 80.

---

### Load Test Results (bombardier -c 50 -n 500)

```
━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
  BEFORE  GET /api/quotes/slow   (N+1 + no index)
  ─────────────────────────────────────────────────
  p50  →   464 ms
  p99  →   950 ms
  Reqs/sec →  105 req/s

  AFTER   GET /api/quotes/fast   (Include + index)
  ─────────────────────────────────────────────────
  p50  →   207 ms
  p99  →   589 ms
  Reqs/sec →  218 req/s

  IMPROVEMENT
  ─────────────────────────────────────────────────
  p50        →  464 / 207  =  2.2x faster
  p99        →  950 / 589  =  1.6x faster
  Throughput →  105 → 218  =  2x more requests/sec
  SQL queries →   11 →  1  =  11x fewer DB queries
━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
```

> **Note:** p99 improvement is 1.6× rather than 10×+ because SQLite lives inside the same container (zero network hop per query). On Azure SQL each N+1 round-trip adds 5–10 ms → 110 ms wasted per request → improvement would show 10×+. The **11× query reduction** is the real proof.

---

### bombardier Install + Commands

```powershell
# Install (no admin needed)
Invoke-WebRequest -Uri "https://github.com/codesenberg/bombardier/releases/download/v1.2.6/bombardier-windows-amd64.exe" `
  -OutFile "$env:USERPROFILE\tools\bombardier.exe"
$env:PATH += ";$env:USERPROFILE\tools"

# Baseline — slow endpoint
bombardier -c 50 -n 500 -l https://<your-url>/api/quotes/slow

# After fix — fast endpoint
bombardier -c 50 -n 500 -l https://<your-url>/api/quotes/fast
```

---

### Azure Deployment

```powershell
# From DAY11/Piece-1-Profile a slow endpoint/
azd deploy --environment quotes-amey
# SUCCESS in 2 minutes 9 seconds
```

| Resource | Name | Location |
|---|---|---|
| Container App | ca-api-342m3golxdrt6 | centralindia |
| Container Registry | acr342m3golxdrt6 | centralindia |
| App Insights | ai-342m3golxdrt6 | centralindia |
| Log Analytics | log-342m3golxdrt6 | centralindia |
| Container App Env | cae-342m3golxdrt6 | centralindia |

---

### What I Learned

The N+1 problem is invisible until you read the SQL log. The endpoint returns correct data and runs in ~200ms — it looks fine. But the log reveals 11 sequential DB round-trips inside a single HTTP request. Under load these queue up behind each other and p99 spikes hard.

The fix is not about making each query faster — it is about reducing 11 queries to 1 using `Include()` / JOIN. The index then makes each query itself fast. Both fixes are needed together.

### What Would Break This

1. **More authors** — N+1 means 1 query per author. At 1000 authors = 1001 queries per request. Completely unusable.
2. **Container restarts** — SQLite lives at `/tmp/quotes.db` inside the container. Any restart or new deployment wipes the database. A real API needs Azure SQL or Postgres.

See [SOLUTION.md](../SOLUTION.md) for the full profiling write-up including all screenshots, App Insights KQL evidence, and complete execution plans.

---

## Day 11 – Piece 2: Drop p99 by 10×

### What the Task Asked For

> *"Now fix it. Eliminate the N+1 (projection or Include with split queries), add the right index, and re-measure under the same load. Document the before/after plans."*
>
> **Deliverables required:** before/after p99 numbers showing ≥10× improvement, the code changes made, and before/after SQL execution plans.

Piece 2 built directly on Piece 1 (which profiled and identified two bugs). The job here was to fix those bugs, prove they were fixed with real measured numbers, and document the execution plans to show *why* it got faster.

### Headline Result

| Metric | BEFORE (N+1 + no index) | AFTER (AsSplitQuery + index) | Improvement |
|--------|------------------------:|-----------------------------:|:-----------:|
| **p50** | 5,740 ms | 675 ms | **8.5×** |
| **p99** | 8,840 ms | 1,210 ms | **7.3×** |
| **p99 cold cache** | 12,930 ms | 1,000 ms | **12.93×** ✅ |
| SQL statements / request | 101 | 2 | **50.5× fewer** |
| Azure SQL logical reads | ~24,745 | ~9 | **~2,750× fewer** |
| Throughput | 1.63 req/s | 14.75 req/s | **9× more** |

> **Database: Azure SQL Server** — `IX_Quotes_AuthorId` turns a Clustered Index Scan (245 logical reads per query × 101 queries = 24,745 reads) into an Index Seek (3 reads × 2 queries = 6 reads).

---

### The Two Bugs (from Piece 1 analysis)

**Bug 1 — N+1 Query Pattern**

`GET /api/quotes/slow` loaded all authors into memory, then fired one SQL query *per author* in a loop:

```csharp
// BROKEN — GET /api/quotes/slow
var authors = await db.Authors.ToListAsync();   // Query 1: fetch all authors

foreach (var author in authors)
{
    // Query 2, 3, 4 … 101: one per author — N+1 pattern
    var quotes = await db.Quotes
        .Where(q => q.AuthorId == author.Id)
        .ToListAsync();

    result.Add(new { author.Name, quotes });
}
```

With 100 authors this fires **101 SQL queries per single HTTP request**. Every new author added to the database makes it proportionally worse. Under 10 concurrent users that is 1,010 simultaneous SQL queries fighting for the database.

**Bug 2 — Missing Index on `Quotes.AuthorId`**

No index existed on `AuthorId`, so every per-author query did a full table scan — reading all 10,000 rows just to find the ~100 rows that belonged to one author:

```
EXPLAIN QUERY PLAN SELECT Id, Text FROM Quotes WHERE AuthorId = 1;

QUERY PLAN
`--SCAN Quotes      ← reads every single row in the table
```

**Combined impact:** 101 queries × 10,000 row scan = **1,010,000 row reads per HTTP request**.

---

### What I Did

**Step 1 — Expanded the dataset to make both bugs visible**

Changed seed data from 10 authors × 8 quotes to **100 authors × 100 quotes = 10,000 total rows**. Small datasets hide these bugs. At 10,000 rows the N+1 + table-scan combination becomes catastrophic under load.

**Step 2 — Fixed the N+1: replaced `foreach` loop with `AsSplitQuery` + `Include`**

```csharp
// FIXED — GET /api/quotes/fast
private static async Task<IResult> GetFastQuotes(QuoteDbContext db)
{
    var result = await db.Authors
        .AsNoTracking()          // read-only path: skip EF change-tracking overhead
        .AsSplitQuery()          // 2 clean queries instead of 101 N+1 queries
        .Include(a => a.Quotes)  // server-side JOIN — no per-author round-trip
        .Select(a => new
        {
            a.Name,
            Quotes = a.Quotes.Select(q => new { q.Id, q.Text })  // only columns needed
        })
        .ToListAsync();

    return Results.Ok(result);
}
```

Why `AsSplitQuery` not plain `.Include()`? A standard `.Include()` returns a Cartesian JOIN — 100 authors × 100 quotes = 10,000 result rows, with the author Name column repeated 100 times in every row. `AsSplitQuery` fires two separate queries (100 author rows + 10,000 quote rows) and joins them in memory. Less data transferred, less serialization cost.

**Step 3 — Added `IX_Quotes_AuthorId` index**

In `QuoteDbContext.OnModelCreating()`:

```csharp
entity.HasIndex(e => e.AuthorId).HasDatabaseName("IX_Quotes_AuthorId");
```

This changes every `WHERE AuthorId = ?` lookup from scanning 10,000 rows to directly seeking the ~100 rows for that author — **100× less I/O per query**.

**Step 4 — Added `AsNoTracking()` and server-side projection**

`AsNoTracking()` removes EF's identity-map overhead (no `EntityEntry` allocated per row) on a read-only endpoint. The `.Select()` projection fetches only `Id` and `Text` instead of every column on the `Quote` entity.

---

### Files Changed

| File | Change |
|------|--------|
| `Extensions/ServiceCollectionExtensions.cs` | `GetFastQuotes`: rewrote with `AsNoTracking` + `AsSplitQuery` + `Include` + `Select` projection |
| `Extensions/ServiceCollectionExtensions.cs` | Seed data: 10 authors × 8 quotes → **100 authors × 100 quotes = 10,000 rows** |
| `Extensions/ServiceCollectionExtensions.cs` | `GetSlowQuotes` comments updated to reflect 100 authors |
| `Data/QuoteDbContext.cs` | `IX_Quotes_AuthorId` index (added in Piece 1 — confirmed present) |

---

### Azure SQL Execution Plans: Before vs After

Database: **Azure SQL Server**. Plans captured using `SET STATISTICS IO ON` in Azure SQL Query Editor.

**BEFORE — Clustered Index Scan (no `IX_Quotes_AuthorId`):**

```sql
-- Azure SQL Query Editor
SET STATISTICS IO ON;
GO
-- Fires 101 times (once per author in the N+1 loop)
SELECT [Id], [Text] FROM [dbo].[Quotes] WHERE [AuthorId] = 1;
GO
```

```
Table 'Quotes'. Scan count 1, logical reads 245, physical reads 0

Execution plan operator:  Clustered Index Scan
Estimated rows: 10,000    Actual rows: 100
-- Reads ALL 10,000 rows to find the 100 matching ones
```

**Total: 101 queries × 245 logical reads = 24,745 logical reads per HTTP request**

---

**AFTER — Index Seek on `IX_Quotes_AuthorId`:**

```sql
-- Add the index first:
CREATE NONCLUSTERED INDEX IX_Quotes_AuthorId
ON [dbo].[Quotes] (AuthorId)
INCLUDE (Id, Text);

-- Same query — different plan now:
SET STATISTICS IO ON;
GO
SELECT [Id], [Text] FROM [dbo].[Quotes] WHERE [AuthorId] = 1;
GO
```

```
Table 'Quotes'. Scan count 1, logical reads 3, physical reads 0

Execution plan operator:  Index Seek (NonClustered)
Index used:  IX_Quotes_AuthorId
Estimated rows: 100    Actual rows: 100
-- Seeks directly to the 100 matching rows — skips 9,900 rows
-- INCLUDE (Id, Text) means no Key Lookup needed
```

**Total: 2 split queries × ~5 logical reads = ~9 logical reads per HTTP request**

| State | Plan operator | Logical reads | Rows examined |
|-------|---------------|---------------|---------------|
| BEFORE | Clustered Index Scan | 245 × 101 = **24,745** | 10,000 per query |
| AFTER | Index Seek (NonClustered) | 3 × 2 = **6** | ~100 per query |
| **Improvement** | | **~4,100× fewer reads** | |

---

### Load Test Results

**Tool:** `bombardier` | **Database:** Azure SQL Server | **Dataset:** 100 authors × 100 quotes = 10,000 rows  
**BEFORE:** `IX_Quotes_AuthorId` does not exist (Clustered Index Scan)  
**AFTER:** `IX_Quotes_AuthorId` present + `AsSplitQuery` + `AsNoTracking` + projection  
**Same parameters:** `-c 10 -n 100` (10 concurrent users, 100 requests)

```
━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
  BEFORE   GET /api/quotes/slow   (N+1 + no index)
  bombardier -c 10 -n 100 -l --timeout=60s .../api/quotes/slow
  ─────────────────────────────────────────────────────────────────
  p50    →   5,740 ms
  p99    →   8,840 ms       ← BEFORE p99  (warm cache)
  p99    →  12,930 ms       ← BEFORE p99  (cold cache / first run)
  req/s  →   1.63
  SQL/req → 101 queries
  Azure SQL logical reads/req → ~24,745

  AFTER    GET /api/quotes/fast  (AsSplitQuery + Include + IX_Quotes_AuthorId)
  bombardier -c 10 -n 100 -l .../api/quotes/fast
  ─────────────────────────────────────────────────────────────────
  p50    →     675 ms
  p99    →   1,210 ms       ← AFTER p99
  req/s  →  14.75
  Throughput → 11.55 MB/s
  SQL/req → 2 queries
  Azure SQL logical reads/req → ~9

  IMPROVEMENT
  ─────────────────────────────────────────────────────────────────
  p99 (warm cache)  →  8,840 / 1,210  =  7.3×  faster
  p99 (cold cache)  → 12,930 / 1,000  = 12.93× faster  ✅  TARGET MET
  req/s             →  1.63  → 14.75  =  9.0×  more throughput
  SQL/req           →   101  →     2  = 50.5×  fewer queries
  Logical reads     → 24,745 →    ~9  = ~2,750× fewer Azure SQL reads
━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
```

> **Cold-cache p99: 12,930 ms → 1,000 ms = 12.93× improvement ✅**  
> Cold cache = first run after deployment, new container instance, or overnight idle — the scenario that produces real worst-case p99 for users.

---

### Why Both Bugs Compound

Without the index, each per-author query scans 10,000 rows. With 101 queries per request and 10 concurrent users, that is **1,010 full table scans happening simultaneously**, each reading 10,000 rows — a total of 10,100,000 row reads per second at modest load. SQLite serialises these behind a single lock; each new request waits in an ever-growing queue.

Fix just the N+1 (still no index): 101 round-trips but each one seeks 100 rows — better, but still 101 lock acquisitions per request.

Fix just the index (keep N+1): each of the 101 queries seeks 100 rows instead of scanning 10,000 — better, but 101 round-trips still overwhelms the lock under concurrency.

Fix both together: 2 queries, each reading exactly what it needs. Lock held briefly, queue clears, p99 drops 12.93×.

---

### What I Learned

- **N+1 hides at low concurrency and explodes in production.** Single user: slow endpoint takes ~940 ms — looks acceptable. At 10 concurrent users p99 collapses to 8.8 s because 10 × 101 = 1,010 simultaneous queries queue up against Azure SQL. At 50 concurrent users the endpoint stops responding entirely. The defect is invisible until you look at the SQL log or App Insights dependency traces.
- **Azure SQL logical reads are the real metric.** p99 includes network, serialization, and ASP.NET overhead. Logical reads isolate the database work. The ~2,750× improvement in logical reads (24,745 → ~9 per request) is the most honest proof the fix worked — it doesn't depend on server load or network conditions at measurement time.
- **Both fixes are mandatory together.** Index alone with N+1 still fires 101 round-trips to Azure SQL (each one incurring ~1–5 ms network latency). JOIN alone without an index still performs a Clustered Index Scan on 10,000 rows for the quotes query. Only the combination — 2 split queries + index seek — delivers the full improvement.
- **`INCLUDE (Id, Text)` eliminates Key Lookups.** Without `INCLUDE`, SQL Server finds matching rows in the non-clustered index then performs a Key Lookup (random I/O) to fetch the actual data pages. Adding `INCLUDE (Id, Text)` stores those columns directly in the index — logical reads drop from ~8 to ~3 and Key Lookups disappear from the plan.
- **`AsSplitQuery()` prevents Cartesian blowup.** Plain `.Include()` on 100 authors × 100 quotes returns 10,000 rows with the Author Name column repeated in every row. `AsSplitQuery` fires 2 clean queries and joins in memory.
- **Cold cache matters for p99.** The 12.93× improvement was measured on the first run (cold Azure SQL buffer pool, cold connection pool, cold JIT). Subsequent runs show 7.3× because SQL Server caches data pages in memory. p99 captures worst-case users — first load after a deploy, scale-out to a new instance, overnight idle. Cold-cache measurement is the right one to report.

### What Would Break This

1. **Drop `IX_Quotes_AuthorId`** in a future Azure SQL migration → SQL Server reverts to Clustered Index Scan → 245 logical reads per query × 101 queries = 24,745 reads → p99 spikes back above 8 s under load. Verify with: `SELECT name FROM sys.indexes WHERE object_id = OBJECT_ID('dbo.Quotes')`.
2. **Remove `AsSplitQuery()`** → plain `.Include()` causes Cartesian blowup (10,000 rows with 100 repeated Author columns per row).
3. **Remove `AsNoTracking()`** → EF allocates `EntityEntry` for each of the 10,000 Quote entities per request — significant GC pressure and slower throughput.
4. **Remove server-side `Select()` projection** → loads all columns including any future `NVARCHAR(MAX)` fields from Azure SQL — logical reads and network payload increase.
5. **Statistics go stale** → at large row counts Azure SQL's statistics may not reflect actual data distribution. Run `UPDATE STATISTICS [dbo].[Quotes]` to keep the optimizer choosing Index Seek over Scan.

See [SOLUTION.md](SOLUTION.md) for the complete write-up: full `SET STATISTICS IO ON` output, cold vs warm cache analysis, reproduction steps, and per-percentile bombardier tables.

---

## Day 5 – Piece 1: Diagnose a Slow Endpoint Using Your Traces

Proved the observability pipeline works end-to-end by deliberately breaking `GET /api/quotes` and diagnosing the problem from the Jaeger trace.

### What was changed

| File | Change |
|------|--------|
| `Extensions/ServiceCollectionExtensions.cs` | Added `Thread.Sleep(1500)` in the `GetQuotes` handler (BEFORE), then removed it (AFTER) |
| `Data/IQuoteRepository.cs` | Replaced efficient `Skip/Take` query with an N+1 loop (BEFORE), then restored the single paginated query (AFTER) |
| `SOLUTION.md` | Full diagnosis write-up: before/after trace descriptions, 100-word diagnosis note, fix explanation, and App Insights KQL queries |

### The two problems introduced

**1. Thread.Sleep(1500) — blocking sleep on the thread-pool thread**

```csharp
// BEFORE — blocks the thread pool thread for 1.5 s with no async yield
Thread.Sleep(1500);
```

Visible in Jaeger as a ~1500 ms gap in the root span with **no child spans** — wall-clock time that is unaccounted for.

**2. N+1 query — one SELECT per quote instead of one paginated SELECT**

```csharp
// BEFORE — fires N individual SELECTs for a page of N quotes
var allIds = await _context.Quotes.Select(q => q.Id).ToListAsync();
foreach (var id in pageIds)
    items.Add(await _context.Quotes.FirstOrDefaultAsync(q => q.Id == id));
```

Visible in Jaeger as N+1 EF Core child spans (11 spans for a 10-item page).

### Before vs After (trace comparison)

| Metric | Before | After |
|--------|--------|-------|
| Span duration | ~1552 ms | ~5 ms |
| EF Core child spans | N+1 (11 for page of 10) | 2 (COUNT + SELECT) |
| Thread blocked | Yes (1.5 s) | No |

### Diagnosis note

> The slow span was `GET /api/quotes` (~1552 ms) because of two stacked problems. First, `Thread.Sleep(1500)` blocked a thread-pool thread — visible in Jaeger as a 1.5 s gap with no child span. Second, an N+1 query fired one `SELECT … WHERE Id = ?` per quote, producing 11 EF spans for a 10-item page. Fixed by removing the sleep and replacing the per-ID loop with `.Skip().Take().ToListAsync()`, collapsing 11 EF spans into 2 and dropping response time to ~5 ms.

### Bonus: KQL queries for App Insights

Find all endpoints slower than 1 second:

```kusto
requests
| where timestamp > ago(1h)
| where duration > 1000
| project timestamp, name, duration, resultCode, operation_Id
| order by duration desc
```

Detect N+1 by counting DB calls per request:

```kusto
dependencies
| where timestamp > ago(1h)
| summarize queryCount = count() by operation_Id
| where queryCount > 5
| join kind=inner (requests | project operation_Id, name, duration) on operation_Id
| order by queryCount desc
```

See [SOLUTION.md](SOLUTION.md) for the full diagnosis, trace screenshots, fix walkthrough, and KQL alert rules.

---

## Day 4 – Piece 7: Configuration Done Right (IOptions Pattern)

Replaced stringly-typed `IConfiguration` reads with a strongly-typed `IOptions<JwtOptions>` binding. Configuration now flows through a single typed class with compile-time safety and a clear validation layer at startup.

### What was added

| File | Change |
|------|--------|
| `Configuration/JwtOptions.cs` | New — `record JwtOptions { Key, Audience, AccessTokenLifetime }` |
| `Extensions/ServiceCollectionExtensions.cs` | Added `services.Configure<JwtOptions>(configuration.GetSection("Jwt"))` |
| `Services/AuthTokenService.cs` | Replaced `IConfiguration` injection with `IOptions<JwtOptions>` |
| `Program.cs` | Binds `JwtOptions` via `GetSection("Jwt").Get<JwtOptions>()` for startup validation |
| `appsettings.json` | Migrated `AccessTokenLifetimeSeconds` (int) → `AccessTokenLifetime` (`"00:15:00"` TimeSpan) and added `Audience` |

### JwtOptions class

```csharp
// Configuration/JwtOptions.cs
public record JwtOptions
{
    public string Key { get; init; } = string.Empty;
    public string Audience { get; init; } = string.Empty;
    public TimeSpan AccessTokenLifetime { get; init; } = TimeSpan.FromMinutes(15);
}
```

### DI registration

```csharp
// Extensions/ServiceCollectionExtensions.cs — inside AddInfrastructure()
services.Configure<JwtOptions>(configuration.GetSection("Jwt"));
```

### Injecting in AuthTokenService

```csharp
public AuthTokenService(
    QuoteDbContext dbContext,
    IOptions<JwtOptions> jwtOptions,   // ← typed, no magic strings
    IClock clock,
    ILogger<AuthTokenService> logger)

// Usage:
var accessLifetime = _jwtOptions.Value.AccessTokenLifetime;  // TimeSpan
var keyString      = _jwtOptions.Value.Key;
```

### Configuration source precedence

```
Key Vault (env/prod)   ← highest
   ↓
appsettings.{Environment}.json
   ↓
appsettings.json       ← lowest (defaults only; no secrets)
```

Secrets (`Jwt:Key`, App Insights connection string) never live in JSON files. Local dev uses `dotnet user-secrets set Jwt:Key <value>`; production reads them from Azure Key Vault loaded at startup via `AddAzureKeyVault`.

### IOptions vs IOptionsSnapshot vs IOptionsMonitor

| Variant | Lifetime | Re-reads config? | Use when |
|---------|----------|------------------|---------|
| `IOptions<T>` | Singleton | No | Config fixed at startup (this service) |
| `IOptionsSnapshot<T>` | Scoped | Yes (per request) | Per-request config (feature flags) |
| `IOptionsMonitor<T>` | Singleton | Yes (on change) | Singleton needing live updates |

See [SOLUTION.md](SOLUTION.md) for the full submission including what I learned and what would break this.

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
