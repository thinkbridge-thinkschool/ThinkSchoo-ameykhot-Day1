using QuotesApi.Models;

namespace QuotesApi.Services;

// Used when ServiceBus:ConnectionString is absent — app starts normally in local dev without Azure.
public class NoOpQuoteEventPublisher : IQuoteEventPublisher
{
    private readonly ILogger<NoOpQuoteEventPublisher> _logger;

    public NoOpQuoteEventPublisher(ILogger<NoOpQuoteEventPublisher> logger)
    {
        _logger = logger;
    }

    public Task PublishQuoteCreatedAsync(Quote quote, CancellationToken cancellationToken = default)
    {
        _logger.LogWarning(
            "[Publisher] Service Bus not configured — skipping QuoteCreated event for QuoteId={Id}",
            quote.Id);
        return Task.CompletedTask;
    }

    public Task PublishTestEventAsync(int quoteId, string author, string text, string? messageId = null, CancellationToken cancellationToken = default)
    {
        _logger.LogWarning("[Publisher] Service Bus not configured — cannot publish test event");
        return Task.CompletedTask;
    }
}
