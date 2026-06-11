using Azure.Messaging.ServiceBus;
using Microsoft.EntityFrameworkCore;
using QuotesApi.Data;
using QuotesApi.Models;

namespace QuotesApi.Workers;

// BackgroundService that polls the OutboxMessages table every 5 seconds and forwards
// any unsent rows to the Service Bus topic, then marks them ProcessedAt = UtcNow.
//
// At-least-once delivery:
//   If the process crashes AFTER SendMessageAsync but BEFORE setting ProcessedAt,
//   the row remains unprocessed. On restart the relay will publish it again.
//   The consumer deduplicates on MessageId (= outbox row Id) — so duplicates are harmless.
public class OutboxRelayService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ServiceBusSender _sender;
    private readonly ILogger<OutboxRelayService> _logger;
    private static readonly TimeSpan PollingInterval = TimeSpan.FromSeconds(5);

    public OutboxRelayService(
        IServiceScopeFactory scopeFactory,
        ServiceBusClient client,
        ILogger<OutboxRelayService> logger)
    {
        _scopeFactory = scopeFactory;
        _sender = client.CreateSender("quotes-events");
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("[Relay] Started — polling every {Interval}s for unsent outbox rows",
            PollingInterval.TotalSeconds);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ProcessPendingMessagesAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[Relay] Unhandled error in polling loop — will retry next cycle");
            }

            await Task.Delay(PollingInterval, stoppingToken);
        }

        _logger.LogInformation("[Relay] Stopped");
    }

    private async Task ProcessPendingMessagesAsync(CancellationToken ct)
    {
        // New scope per cycle: EF Core DbContext is scoped and must not be reused across cycles
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<QuoteDbContext>();

        var pending = await db.OutboxMessages
            .Where(m => m.ProcessedAt == null && m.RetryCount < 5)
            .OrderBy(m => m.CreatedAt)
            .Take(10)
            .ToListAsync(ct);

        if (!pending.Any())
            return;

        _logger.LogInformation("[Relay] Found {Count} unsent outbox row(s) — publishing now", pending.Count);

        foreach (var outboxMsg in pending)
            await PublishAsync(db, outboxMsg, ct);
    }

    private async Task PublishAsync(QuoteDbContext db, OutboxMessage outboxMsg, CancellationToken ct)
    {
        try
        {
            var message = new ServiceBusMessage(outboxMsg.Payload)
            {
                // Outbox row Id as MessageId → consumer can deduplicate if this row is published twice
                // (i.e. after a crash-before-mark-sent restart)
                MessageId = outboxMsg.Id.ToString(),
                Subject = outboxMsg.EventType,
                ContentType = "application/json"
            };

            // ── CRASH POINT ────────────────────────────────────────────────────────────────
            // If the process dies HERE (after send, before ProcessedAt is written):
            //   → ProcessedAt stays null
            //   → On restart, relay picks up this row again and sends it a second time
            //   → Consumer receives a duplicate but sees MessageId already processed → skips it
            //   → Net result: exactly-once processing despite at-least-once delivery
            await _sender.SendMessageAsync(message, ct);

            outboxMsg.ProcessedAt = DateTime.UtcNow;
            await db.SaveChangesAsync(ct);

            _logger.LogInformation(
                "[Relay] Published and marked sent — Id={Id} EventType={Type} ProcessedAt={At}",
                outboxMsg.Id, outboxMsg.EventType, outboxMsg.ProcessedAt);
        }
        catch (Exception ex)
        {
            outboxMsg.RetryCount++;
            outboxMsg.Error = ex.Message;
            await db.SaveChangesAsync(ct);

            _logger.LogWarning(
                "[Relay] Publish failed — Id={Id} RetryCount={Count} Error={Error}",
                outboxMsg.Id, outboxMsg.RetryCount, ex.Message);
        }
    }
}
