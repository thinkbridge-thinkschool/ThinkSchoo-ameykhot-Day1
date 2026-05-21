using QuotesApi.Models;
using QuotesApi.Time;

namespace QuotesApi.Services;

public sealed class QuoteFactory : IQuoteFactory
{
    private readonly IClock _clock;

    public QuoteFactory(IClock clock)
    {
        _clock = clock;
    }

    public Quote Create(string author, string text, DateTime? createdAtUtc = null)
    {
        var timestamp = createdAtUtc ?? _clock.UtcNow.UtcDateTime;
        return new Quote(author, text, timestamp);
    }
}