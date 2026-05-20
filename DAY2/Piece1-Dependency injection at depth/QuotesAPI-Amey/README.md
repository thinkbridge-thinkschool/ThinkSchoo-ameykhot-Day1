# QuotesAPI - ASP.NET Core 10 Minimal API

A modern ASP.NET Core 10 API for managing quotes and collections, built with **Domain-Driven Design (DDD)** principles, **Entity Framework Core**, and **FluentValidation**.

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

## 📧 Support

For issues or questions, contact the development team.

**Happy quoting! 🎉**
