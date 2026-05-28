# EF Core Change Tracker + AsNoTracking

Day 10 · Piece 1 — demonstrates EF Core's change tracking mechanism and the read-path performance win of `AsNoTracking()`.

## Project layout

```
ChangeTrackerDemo/
├── Models/
│   └── Product.cs              — entity with ProductId, Name, Category, Price, Stock
├── AppDbContext.cs              — DbContext wired to SQLite, SQL model config
├── Seeder.cs                   — batch-inserts 10 000 rows, clears tracker between batches
├── TrackingDemo.cs             — Demo 1 (identity resolution) + Demo 2 (mutation detection)
├── NoTrackingDemo.cs           — Demo 3 (no identity map) + Demo 5 (silent failure) + Demo 7 (context-level NoTracking)
├── BenchmarkDemo.cs            — Demo 8: 5-iteration tracked vs AsNoTracking timing + allocation
├── Program.cs                  — orchestrates all five sections
└── screenshots/
    ├── identity-resolution-result.png
    ├── mutation-detection-result.png
    ├── no-tracking-result.png
    └── benchmark-result.png
```

---

## Key Concepts

**What tracking does:** When EF Core queries data, it performs four steps beyond materialising objects — allocates an `EntityEntry`, creates a snapshot of all property values, registers the object in an identity map keyed by primary key, and maintains the state machine (`Unchanged → Modified → Deleted`).

**The cost:** This overhead applies to every tracked query, even read-only operations that never call `SaveChanges()`.

---

## Important Findings

**Identity resolution differs between methods:**
- `FirstAsync()` queries the database every time, then checks the identity map — two round-trips but one shared object.
- `FindAsync(id)` checks the identity map *before* sending any SQL — if found, no SELECT fires at all.

**Automatic dirty detection works through snapshots:** Mutate a tracked entity's property and EF transitions its state from `Unchanged` to `Modified` automatically by comparing current values against the stored snapshot.

**Silent failure risk:** Loading entities with `AsNoTracking()`, modifying properties, and calling `SaveChanges()` produces *no error*. The database is untouched. The only signal is `SaveChanges()` returning zero affected rows.

---

## Performance Results

Measured with 5-iteration average, Release build, SQLite warm, `GC.GetAllocatedBytesForCurrentThread()`:

| Metric | Tracked | AsNoTracking | Saved |
|---|---|---|---|
| Avg time (ms) | 433.4 | 133.8 | +299.6 ms |
| Avg allocated (KB) | 8 744 | 3 899 | +4 845 KB |

The allocation gap **is** the change-tracker tax: one `EntityEntry` + one property-values snapshot per row, plus identity-map bookkeeping.

---

## Screenshots

### Demo 1 — Identity Resolution

![Identity Resolution](screenshots/identity-resolution-result.png)

### Demo 2 — Mutation Detection

![Mutation Detection](screenshots/mutation-detection-result.png)

### Demos 3, 5, 7 — AsNoTracking Behaviour

![No Tracking](screenshots/no-tracking-result.png)

### Demo 8 — Benchmark

![Benchmark](screenshots/benchmark-result.png)

---

## When to Use What

| Scenario | Use |
|---|---|
| GET endpoint / report / projection / export | `AsNoTracking()` |
| Load → mutate → `SaveChanges()` | Default tracking |
| Whole service is read-only | `UseQueryTrackingBehavior(NoTracking)` in `OnConfiguring` |
| Re-attach a detached entity for update | `db.Update(entity)` then `SaveChanges()` |

**One-line rule:** `AsNoTracking()` on every query that will never call `SaveChanges()`.
