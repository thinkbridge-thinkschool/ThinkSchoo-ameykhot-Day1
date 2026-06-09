using QuotesApi.Models;

namespace QuotesApi.Services;

public interface IQuoteEventPublisher
{
    Task PublishQuoteCreatedAsync(Quote quote, CancellationToken cancellationToken = default);

    // For demo/testing: publish a raw event with a specific QuoteId (e.g., 999 for poison-message demo)
    Task PublishTestEventAsync(int quoteId, string author, string text, string? messageId = null, CancellationToken cancellationToken = default);
}
