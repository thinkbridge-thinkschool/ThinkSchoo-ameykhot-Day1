using Azure.Messaging.ServiceBus;
using QuotesApi.Models;
using QuotesApi.Services;

namespace QuotesApi.BackgroundJobs;

// Competing-consumer worker: multiple instances share the email-notifications subscription.
// Service Bus delivers each message to ONLY ONE instance — no duplicate emails.
public class EmailNotificationsWorker : BackgroundService
{
    private const string TopicName = "quotes-events";
    private const string SubscriptionName = "email-notifications";

    private readonly ServiceBusProcessor _processor;
    private readonly IProcessedMessageStore _dedupeStore;
    private readonly ILogger<EmailNotificationsWorker> _logger;

    public EmailNotificationsWorker(
        ServiceBusClient client,
        IProcessedMessageStore dedupeStore,
        ILogger<EmailNotificationsWorker> logger)
    {
        _dedupeStore = dedupeStore;
        _logger = logger;

        _processor = client.CreateProcessor(TopicName, SubscriptionName,
            new ServiceBusProcessorOptions
            {
                MaxConcurrentCalls = 2,        // competing consumers: 2 messages processed in parallel
                AutoCompleteMessages = false    // we complete/abandon manually for full control
            });
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _processor.ProcessMessageAsync += HandleMessageAsync;
        _processor.ProcessErrorAsync += HandleErrorAsync;

        await _processor.StartProcessingAsync(stoppingToken);
        _logger.LogInformation("[EmailWorker] Started — listening on {Topic}/{Sub}", TopicName, SubscriptionName);

        try
        {
            // Block here until the host signals shutdown
            await Task.Delay(Timeout.Infinite, stoppingToken);
        }
        catch (OperationCanceledException) { }

        await _processor.StopProcessingAsync();
        _logger.LogInformation("[EmailWorker] Stopped");
    }

    private async Task HandleMessageAsync(ProcessMessageEventArgs args)
    {
        var messageId = args.Message.MessageId;
        var deliveryCount = args.Message.DeliveryCount;

        try
        {
            // ── IDEMPOTENCY CHECK ─────────────────────────────────────────────────
            // If we've already processed this MessageId (e.g., network re-delivery after a crash),
            // skip work and complete it — never send the same email twice.
            if (await _dedupeStore.IsProcessedAsync(messageId))
            {
                _logger.LogWarning(
                    "[EmailWorker] DUPLICATE detected — MessageId={MsgId} already processed, skipping (delivery #{Count})",
                    messageId, deliveryCount);
                await args.CompleteMessageAsync(args.Message);
                return;
            }

            var body = args.Message.Body.ToObjectFromJson<QuoteCreatedEvent>()!;

            _logger.LogInformation(
                "[EmailWorker] Processing QuoteId={Id} Author={Author} MessageId={MsgId} Delivery={Count}",
                body.QuoteId, body.Author, messageId, deliveryCount);

            // ── POISON MESSAGE TRIGGER ────────────────────────────────────────────
            // QuoteId 999 is invalid — throws every attempt until Service Bus moves it to DLQ.
            // With max-delivery-count=3 on the subscription, it will appear here 3 times then
            // land in the Dead Letter Queue automatically.
            if (body.QuoteId == 999)
                throw new InvalidOperationException(
                    $"[EmailWorker] Poison message — QuoteId 999 is permanently invalid (delivery #{deliveryCount})");

            // Simulate sending an email notification
            await Task.Delay(50);
            _logger.LogInformation(
                "[EmailWorker] Email sent for QuoteId={Id} by {Author}", body.QuoteId, body.Author);

            // Mark processed BEFORE completing — guarantees idempotency even if Complete throws
            await _dedupeStore.MarkProcessedAsync(messageId);

            // Tell Service Bus: success — remove the message from the subscription
            await args.CompleteMessageAsync(args.Message);

            _logger.LogInformation("[EmailWorker] Completed MessageId={MsgId}", messageId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "[EmailWorker] Failed MessageId={MsgId} delivery #{Count} — abandoning for retry/DLQ",
                messageId, deliveryCount);

            // Tell Service Bus: failed — re-queue for retry.
            // After max-delivery-count retries the broker moves it to the Dead Letter Queue.
            await args.AbandonMessageAsync(args.Message);
        }
    }

    private Task HandleErrorAsync(ProcessErrorEventArgs args)
    {
        _logger.LogError(args.Exception,
            "[EmailWorker] Processor infrastructure error — source={Source}", args.ErrorSource);
        return Task.CompletedTask;
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        await base.StopAsync(cancellationToken);
        await _processor.DisposeAsync();
    }
}
