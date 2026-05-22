# Day 4 – Piece 4: Serilog with Correlation IDs

## What was done

Replaced the default Microsoft.Extensions.Logging provider with **Serilog** across the entire QuotesAPI. Every log line produced by a request now carries a `TraceId` property (ASP.NET Core's `HttpContext.TraceIdentifier`) that links all log entries for that request together.

---

## Serilog Setup

### 1. Packages added (`QuotesApi.csproj`)

```xml
<PackageReference Include="Serilog.AspNetCore" Version="10.0.0" />
<PackageReference Include="Serilog.Sinks.Console" Version="6.1.1" />
```

`Serilog.AspNetCore` bundles the core library, hosting integration, `Serilog.Settings.Configuration` (reads from `appsettings.json`), and the console sink. The explicit `Serilog.Sinks.Console` reference satisfies the exercise requirement.

---

### 2. `Program.cs` — wire Serilog + correlation middleware

```csharp
using Serilog;
using Serilog.Context;

var builder = WebApplication.CreateBuilder(args);

// Replace default Microsoft logger with Serilog.
// ReadFrom.Configuration() picks up the "Serilog" section in appsettings.
// Enrich.FromLogContext() allows LogContext.PushProperty() calls to attach
// per-request properties to every downstream log line.
builder.Host.UseSerilog((context, services, configuration) =>
    configuration
        .ReadFrom.Configuration(context.Configuration)
        .ReadFrom.Services(services)
        .Enrich.FromLogContext());

// ... service registration, authentication, authorisation ... //

var app = builder.Build();

// Stamp every log line produced during a request with the ASP.NET Core
// TraceIdentifier.  The using block ensures the property is popped when
// the request ends, so it never leaks across requests.
app.Use((ctx, next) =>
{
    using (LogContext.PushProperty("TraceId", ctx.TraceIdentifier))
        return next();
});
```

---

### 3. `appsettings.json` — log levels per category

```json
"Serilog": {
  "Using": [ "Serilog.Sinks.Console" ],
  "MinimumLevel": {
    "Default": "Information",
    "Override": {
      "Microsoft.AspNetCore": "Warning",
      "Microsoft.EntityFrameworkCore.Database.Command": "Warning",
      "System": "Warning"
    }
  },
  "WriteTo": [
    {
      "Name": "Console",
      "Args": {
        "outputTemplate": "[{Timestamp:HH:mm:ss} {Level:u3}] [{TraceId}] {SourceContext}: {Message:lj}{NewLine}{Exception}"
      }
    }
  ],
  "Enrich": [ "FromLogContext" ]
}
```

- **`Microsoft.AspNetCore` → Warning**: silences the noisy request/response pipeline info logs.
- **`Microsoft.EntityFrameworkCore.Database.Command` → Warning**: suppresses SQL echo in production.
- **Own code (`QuotesApi.*`) → Information** (the default).

### 4. `appsettings.Development.json` — verbose overrides for dev

```json
"Serilog": {
  "MinimumLevel": {
    "Override": {
      "QuotesApi": "Debug",
      "Microsoft.EntityFrameworkCore.Database.Command": "Debug"
    }
  }
}
```

EF Core SQL logging only appears in development — exactly one config change needed in production to silence it.

---

### 5. Structured log calls (never interpolated strings)

All log calls use named placeholders so Serilog can index the values as structured key-value pairs:

```csharp
// Login handler
logger.LogInformation("Login attempt for user {Email}", request.Email);
logger.LogInformation("Login succeeded for user {UserId} ({Email})", user.Id, user.Email);

// CreateQuote handler
logger.LogInformation("Received CreateQuote request for author {Author}", request.Author);
logger.LogInformation("Validation passed for author {Author} — building quote entity", request.Author);
logger.LogInformation("Assigned OwnerId {OwnerId} to new quote", ownerId);
logger.LogInformation("Created quote {QuoteId} by author {Author} for user {UserId}", created.Id, created.Author, userIdStr);

// DeleteQuote handler
logger.LogInformation("Deleted quote {QuoteId} by user {UserId}", id, userId);

// AuthTokenService (security event)
_logger.LogWarning("SECURITY_EVENT Refresh token reuse detected. UserId: {UserId}, Family: {Family}", userId, family);
```

**Never** `logger.LogInformation($"Created quote {quoteId}...")` — that collapses everything into one unsearchable string.

---

## 5 Lines of Structured Output from a Single `POST /api/quotes` Request

All five lines share **TraceId `ed069b9899766ad06ccd63b542423cad`** — proving they belong to the same request.

```
[14:47:32 INF] [ed069b9899766ad06ccd63b542423cad] Program: Received CreateQuote request for author Marcus Aurelius
[14:47:32 INF] [ed069b9899766ad06ccd63b542423cad] Program: Validation passed for author Marcus Aurelius — building quote entity
[14:47:32 INF] [ed069b9899766ad06ccd63b542423cad] Program: Assigned OwnerId 9bb72369-5bff-47a4-8e58-365edf9e4491 to new quote
[14:47:32 INF] [ed069b9899766ad06ccd63b542423cad] QuotesApi.Data.QuoteRepository: Creating quote by Marcus Aurelius
[14:47:32 INF] [ed069b9899766ad06ccd63b542423cad] Program: Created quote 2 by author Marcus Aurelius for user 9bb72369-5bff-47a4-8e58-365edf9e4491
```

Output template used: `[{Timestamp:HH:mm:ss} {Level:u3}] [{TraceId}] {SourceContext}: {Message:lj}{NewLine}{Exception}`

The `{SourceContext}` column shows **which class** emitted each line — `Program` for endpoint handlers, `QuotesApi.Data.QuoteRepository` for the repository layer — so you can see the call path across layers without reading a stack trace.

---

## What I Learned This Session

**Structured logging is a search index, not a text file.** When you write `logger.LogInformation("Created quote {QuoteId}", id)`, Serilog stores `QuoteId` as an indexed field. A query like `QuoteId = 42` in Seq/Application Insights instantly finds every log line that touched quote 42 — across services, restarts, and time. A string-interpolated message buries the ID inside prose; you can only grep it, not query it.

The other thing that clicked: **`LogContext.PushProperty` uses an ambient stack**. The `using` block pushes the property onto a `CallContext`-like stack and pops it when disposed. Serilog enrichers walk that stack for every log call inside the `using` scope. No thread-local, no parameter plumbing — just one middleware line correlates every log in the entire request pipeline.

---

## What Would Break This

1. **`ReadFrom.Configuration()` missing the `"Serilog"` section** — Serilog falls back silently to no sinks; the app runs but emits nothing. Easy to miss in a fresh environment where `appsettings.json` wasn't copied.

2. **Calling `LogContext.PushProperty` without `using`** — the property leaks into subsequent requests on the same thread. Under thread-pool reuse (which is always true in Kestrel), a random other request would inherit the previous request's `TraceId`. Always `using`.

3. **Middleware ordering** — if `app.Use(LogContext.PushProperty(...))` is placed after `app.UseAuthentication()`, auth logs won't carry the `TraceId`. The correlation middleware must be the first middleware registered.

4. **Async void / fire-and-forget** — work started with `Task.Run(() => ...)` or `async void` escapes the `using` scope. The spawned task runs with no ambient log context; its logs won't have `TraceId`. Always `await` or propagate context explicitly.

5. **Switching to `Serilog.Sinks.ApplicationInsights` in production** — Application Insights traces use a different correlation model (`operation_Id` / `x-ms-request-id`). The `TraceId` property we push here would appear as a *custom dimension* alongside AI's own correlation, not replace it. To align them, populate `Activity.Current.TraceId` from the HTTP request and let AI pick it up automatically.

---

## Files Changed

| File | Change |
|------|--------|
| `QuotesApi.csproj` | Added `Serilog.AspNetCore` + `Serilog.Sinks.Console` |
| `Program.cs` | `builder.Host.UseSerilog(...)` + correlation ID middleware |
| `appsettings.json` | Replaced `Logging` section with `Serilog` section |
| `appsettings.Development.json` | Dev-only Debug overrides for EF SQL + own code |
| `Extensions/ServiceCollectionExtensions.cs` | Structured `LogInformation` / `LogWarning` calls in Login, GetQuotes, CreateQuote, DeleteQuote, CreateCollection handlers |
