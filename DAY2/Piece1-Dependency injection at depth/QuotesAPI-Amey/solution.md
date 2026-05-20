# DI Lifetimes + IClock Exercise Solution

## 1) DI Lifetimes Used in Quotes API

- **Transient**: `IQuoteFactory -> QuoteFactory`
  - Registered as transient because it is stateless and safe to create per resolution.
- **Scoped**: `IQuoteRepository -> QuoteRepository`, `ICollectionRepository -> CollectionRepository`, `QuoteDbContext`
  - One instance per request, which is correct for repository + EF Core DbContext usage.
- **Singleton**: `IClock -> SystemClock`
  - One app-wide instance for cross-cutting, stateless time provider.

## 2) Why `IClock` is useful

Using `IClock` removes direct calls to `DateTime.UtcNow` from business flow. In tests, we can inject a fake clock with a fixed timestamp, so tests are deterministic and do not fail due to real-time changes.

## 3) `IClock` interface

```csharp
namespace QuotesApi.Time;

public interface IClock
{
    DateTimeOffset UtcNow { get; }
}
```

## 4) Production implementation

```csharp
namespace QuotesApi.Time;

public sealed class SystemClock : IClock
{
    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
}
```

## 5) DI registration

```csharp
services.AddDbContext<QuoteDbContext>(options =>
    options.UseSqlite(connectionString));

services.AddSingleton<IClock, SystemClock>();
services.AddTransient<IQuoteFactory, QuoteFactory>();
services.AddScoped<IQuoteRepository, QuoteRepository>();
services.AddScoped<ICollectionRepository, CollectionRepository>();
```

## 6) One test using fake clock

```csharp
using QuotesApi.Services;
using QuotesApi.Time;
using Xunit;

namespace QuotesApi.Tests;

public class QuoteFactoryTests
{
    [Fact]
    public void Create_UsesClockUtcNow_ForCreatedAt()
    {
        var fixedUtcNow = new DateTimeOffset(2026, 5, 19, 12, 30, 0, TimeSpan.Zero);
        IClock clock = new FakeClock(fixedUtcNow);
        var factory = new QuoteFactory(clock);

        var quote = factory.Create("Test Author", "Test Quote");

        Assert.Equal(fixedUtcNow.UtcDateTime, quote.CreatedAt);
    }

    private sealed class FakeClock : IClock
    {
        public FakeClock(DateTimeOffset utcNow)
        {
            UtcNow = utcNow;
        }

        public DateTimeOffset UtcNow { get; }
    }
}
```

## 7) Additional note (what was changed in code)

- Replaced direct `DateTime.UtcNow` usage by injecting `IClock`:
  - `Quote.CreatedAt` is set via `QuoteFactory` using `IClock`.
  - `Collection.AddItem(...)` now receives `addedAtUtc` from caller; endpoint passes `clock.UtcNow.UtcDateTime`.
