# Day 10 — Piece 2: Query Translation + Projections

## What this task is about

When you write a LINQ query in EF Core, EF translates it into SQL and sends it to the database.
The problem: if you query the whole entity object, EF writes `SELECT *` — every column in the table
comes back, even ones you never look at.

This piece teaches you to:

1. **See what SQL EF is actually sending** — by turning on `LogTo` and `EnableSensitiveDataLogging`
2. **Shrink the SELECT list** — by using `.Select(p => new Dto { ... })` so only needed columns travel
3. **Spot client-side evaluation** — in EF Core 3.0+, any untranslatable predicate throws immediately
   instead of silently loading the whole table into C# memory

---

## Concepts explained

### LogTo — seeing the SQL

```csharp
optionsBuilder
    .LogTo(
        Console.WriteLine,
        new[] { DbLoggerCategory.Database.Command.Name },
        LogLevel.Information,
        DbContextLoggerOptions.None)
    .EnableSensitiveDataLogging();
```

- `DbLoggerCategory.Database.Command.Name` — filters to command-execution events only (no noise)
- `DbContextLoggerOptions.None` — removes timestamp prefix so the SQL is readable at a glance
- `EnableSensitiveDataLogging` — shows actual parameter values like `@p='5'`

### SELECT * vs Projection

```csharp
// BAD — EF selects every column in the Products table
var entities = context.Products.Where(p => p.IsActive).ToList();
// SQL: SELECT ProductId, Category, Description, IsActive, Name, Price, Stock FROM Products

// GOOD — EF only selects what the DTO needs
var dtos = context.Products
    .Where(p => p.IsActive)
    .Select(p => new ProductSummaryDto { ProductId = p.ProductId, Name = p.Name, Price = p.Price })
    .ToList();
// SQL: SELECT ProductId, Name, Price FROM Products
```

The projection also means EF does **not** track the returned objects — no change-tracker overhead.

### Client-side evaluation (the foot-gun)

```csharp
// This custom method has no SQL translation — EF throws InvalidOperationException
.Where(p => IsHighValue(p.Price))   // BAD

// This is a simple comparison EF knows how to translate
.Where(p => p.Price > 500m)         // GOOD
```

EF Core 3.0+ refuses to run untranslatable predicates silently.
Before 3.0 it would load every row to C# and filter there — devastating performance on large tables.

---

## Project structure

```
Piece-2-Query translation + projections/
│
├── QueryProjectionDemo/           ← the runnable demo app
│   ├── Models/
│   │   └── Product.cs             ← 7-column entity
│   ├── Dtos/
│   │   └── ProductSummaryDto.cs   ← 3-column read model
│   ├── AppDbContext.cs            ← EF context with optional LogTo
│   ├── Seeder.cs                  ← inserts 1 000 product rows
│   ├── Demo1_FullEntityQuery.cs   ← shows SELECT * SQL
│   ├── Demo2_ProjectedQuery.cs    ← shows 3-column SELECT SQL
│   ├── Demo3_ClientEval.cs        ← triggers + fixes client-eval error
│   ├── Program.cs                 ← runs all three demos in order
│   └── QueryProjectionDemo.csproj
│
├── screenshots/
│   ├── screenshot_01_full_run.png          ← complete demo output
│   ├── screenshot_02_demo1_full_entity.png ← Demo 1 output (7-col SQL)
│   ├── screenshot_03_demo2_projected.png   ← Demo 2 output (3-col SQL)
│   └── screenshot_04_demo3_client_eval.png ← Demo 3 output (error + fix)
│
├── SOLUTION.md   ← full submission write-up (SQL, code, explanation)
└── README.md     ← this file
```

---

## How to run

```bash
cd QueryProjectionDemo
dotnet run -c Release
```

Requirements: .NET 10 SDK  
No external database needed — SQLite file (`QueryProjectionDemo.db`) is created automatically.

---

## Key outputs to look for

| Demo | What you see in the console |
|---|---|
| Demo 1 | `SELECT "p"."ProductId", "p"."Category", "p"."Description", "p"."IsActive", "p"."Name", "p"."Price", "p"."Stock"` |
| Demo 2 | `SELECT "p"."ProductId", "p"."Name", "p"."Price"` |
| Demo 3 | `InvalidOperationException: The LINQ expression ... could not be translated` then the fixed SQL |

---

## Technologies used

| Tool | Version | Purpose |
|---|---|---|
| .NET | 10 | Runtime |
| EF Core | 10.0.8 | ORM + SQL generation |
| EF Core SQLite | 10.0.8 | Database provider |
| SQLite | embedded | Local file database |
