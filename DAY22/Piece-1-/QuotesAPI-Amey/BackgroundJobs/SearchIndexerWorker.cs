using Azure.Messaging.ServiceBus;
using QuotesApi.Models;
using QuotesApi.Services;

namespace QuotesApi.BackgroundJobs;

// Second subscriber: receives the SAME messages as EmailNotificationsWorker but independently.
// This demonstrates topics fan-out — both subscriptions get every QuoteCreated event.
public class SearchIndexerWorker : BackgroundService
{
    private const string TopicName = "quotes-events";
    private const string SubscriptionName = "search-indexer";

    private readonly ServiceBusProcessor _processor;
    private readonly IProcessedMessageStore _dedupeStore;
    private readonly ILogger<SearchIndexerWorker> _logger;

    public SearchIndexerWorker(
        ServiceBusClient client,
        IProcessedMessageStore dedupeStore,
        ILogger<SearchIndexerWorker> logger)
    {
        _dedupeStore = dedupeStore;
        _logger = logger;

        _processor = client.CreateProcessor(TopicName, SubscriptionName,
            new ServiceBusProcessorOptions
            {
                MaxConcurrentCalls = 2,
                AutoCompleteMessages = false
            });
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _processor.ProcessMessageAsync += HandleMessageAsync;
        _processor.ProcessErrorAsync += HandleErrorAsync;

        await _processor.StartProcessingAsync(stoppingToken);
        _logger.LogInformation("[SearchIndexer] Started — listening on {Topic}/{Sub}", TopicName, SubscriptionName);

        try
        {
            await Task.Delay(Timeout.Infinite, stoppingToken);
        }
        catch (OperationCanceledException) { }

        await _processor.StopProcessingAsync();
        _logger.LogInformation("[SearchIndexer] Stopped");
    }

    private async Task HandleMessageAsync(ProcessMessageEventArgs args)
    {
        var messageId = args.Message.MessageId;
        var deliveryCount = args.Message.DeliveryCount;

        // Idempotency key is subscription-scoped: prefix prevents collision with EmailWorker's store
        var storeKey = $"search:{messageId}";

        try
        {
            if (await _dedupeStore.IsProcessedAsync(storeKey))
            {
                _logger.LogWarning(
                    "[SearchIndexer] DUPLICATE detected — MessageId={MsgId} already indexed, skipping",
                    messageId);
                await args.CompleteMessageAsync(args.Message);
                return;
            }

            var body = args.Message.Body.ToObjectFromJson<QuoteCreatedEvent>()!;

            _logger.LogInformation(
                "[SearchIndexer] Indexing QuoteId={Id} Author={Author} MessageId={MsgId} Delivery={Count}",
                body.QuoteId, body.Author, messageId, deliveryCount);

            // Simulate updating a search index (Elasticsearch, Azure Cognitive Search, etc.)
            await Task.Delay(80);
            _logger.LogInformation(
                "[SearchIndexer] Indexed QuoteId={Id} — search index updated", body.QuoteId);

            await _dedupeStore.MarkProcessedAsync(storeKey);
            await args.CompleteMessageAsync(args.Message);

            _logger.LogInformation("[SearchIndexer] Completed MessageId={MsgId}", messageId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "[SearchIndexer] Failed MessageId={MsgId} delivery #{Count} — abandoning",
                messageId, deliveryCount);
            await args.AbandonMessageAsync(args.Message);
        }
    }

    private Task HandleErrorAsync(ProcessErrorEventArgs args)
    {
        _logger.LogError(args.Exception,
            "[SearchIndexer] Processor infrastructure error — source={Source}", args.ErrorSource);
        return Task.CompletedTask;
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        await base.StopAsync(cancellationToken);
        await _processor.DisposeAsync();
    }
}
