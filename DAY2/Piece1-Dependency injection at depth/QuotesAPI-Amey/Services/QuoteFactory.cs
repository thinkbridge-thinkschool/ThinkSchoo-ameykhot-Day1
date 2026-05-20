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

    public Quote Create(string author, string text)
    {
        return new Quote
        {
            Author = author,
            Text = text,
            CreatedAt = _clock.UtcNow.UtcDateTime
        };
    }
}