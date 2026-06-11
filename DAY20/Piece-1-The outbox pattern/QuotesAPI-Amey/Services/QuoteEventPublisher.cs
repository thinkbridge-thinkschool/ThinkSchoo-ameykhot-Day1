using Azure.Messaging.ServiceBus;
using QuotesApi.Models;

namespace QuotesApi.Services;

// Singleton: ServiceBusSender is thread-safe and long-lived — one per topic is correct.
public class QuoteEventPublisher : IQuoteEventPublisher
{
    private const string TopicName = "quotes-events";

    private readonly ServiceBusSender _sender;
    private readonly ILogger<QuoteEventPublisher> _logger;

    public QuoteEventPublisher(ServiceBusClient client, ILogger<QuoteEventPublisher> logger)
    {
        _sender = client.CreateSender(TopicName);
        _logger = logger;
    }

    public Task PublishQuoteCreatedAsync(Quote quote, CancellationToken cancellationToken = default)
        => SendAsync(new QuoteCreatedEvent
        {
            QuoteId = quote.Id,
            Author = quote.Author,
            Text = quote.Text,
            CreatedAt = DateTime.UtcNow
        },
        // Deterministic key: same quote re-published → same MessageId → consumer dedupes it
        messageId: $"quote-created-{quote.Id}",
        cancellationToken);

    public Task PublishTestEventAsync(int quoteId, string author, string text, string? messageId = null, CancellationToken cancellationToken = default)
        => SendAsync(new QuoteCreatedEvent
        {
            QuoteId = quoteId,
            Author = author,
            Text = text,
            CreatedAt = DateTime.UtcNow
        },
        messageId: messageId ?? Guid.NewGuid().ToString(),
        cancellationToken);

    private async Task SendAsync(QuoteCreatedEvent evt, string messageId, CancellationToken cancellationToken)
    {
        var message = new ServiceBusMessage(BinaryData.FromObjectAsJson(evt))
        {
            MessageId = messageId,
            Subject = "QuoteCreated",
            ContentType = "application/json"
        };

        await _sender.SendMessageAsync(message, cancellationToken);

        _logger.LogInformation(
            "[Publisher] Sent QuoteCreated — QuoteId={Id} Author={Author} MessageId={MsgId}",
            evt.QuoteId, evt.Author, message.MessageId);
    }
}
