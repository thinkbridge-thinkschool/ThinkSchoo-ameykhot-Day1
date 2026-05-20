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
