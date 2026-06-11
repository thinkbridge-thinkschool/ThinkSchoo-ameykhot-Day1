using System.Threading.Channels;

namespace QuotesApi.BackgroundJobs;

// BackgroundService is the right base class for a long-lived queue-drain loop.
// It wraps IHostedService and hands you a single ExecuteAsync to override —
// the host calls Start/Stop; you only write the work loop.
public class QuoteProcessingService : BackgroundService
{
    private readonly ILogger<QuoteProcessingService> _logger;
    private readonly Channel<QuoteJob> _channel;

    public QuoteProcessingService(
        ILogger<QuoteProcessingService> logger,
        Channel<QuoteJob> channel)
    {
        _logger = logger;
        _channel = channel;
    }

    // How graceful shutdown works:
    //   1. Host signals shutdown → stoppingToken is cancelled.
    //   2. ReadAllAsync(stoppingToken) stops waiting for new items and exits the loop.
    //   3. OperationCanceledException is caught and logged as a normal event, not an error.
    //   4. The finally block logs the stop timestamp.
    //   5. Any job already in ProcessAsync also receives the token and stops at the next await.
    // Azure / Kubernetes give the process 30 s to stop cleanly before SIGKILL.
    // Passing stoppingToken everywhere means we respect that window without data corruption.
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("[BackgroundService] Started at {Time}", DateTimeOffset.UtcNow);

        try
        {
            await foreach (var job in _channel.Reader.ReadAllAsync(stoppingToken))
            {
                _logger.LogInformation(
                    "[BackgroundService] Processing job {JobId} — {JobType} for QuoteId={QuoteId}",
                    job.Id, job.JobType, job.QuoteId);

                await ProcessAsync(job, stoppingToken);
            }
        }
        catch (OperationCanceledException)
        {
            // Normal shutdown path — not an error.
            _logger.LogInformation("[BackgroundService] Shutdown requested — stopping cleanly");
        }
        finally
        {
            _logger.LogInformation("[BackgroundService] Stopped at {Time}", DateTimeOffset.UtcNow);
        }
    }

    private async Task ProcessAsync(QuoteJob job, CancellationToken ct)
    {
        // Simulate slow work (send email, push notification, analytics event, etc.).
        // In production replace Task.Delay with real I/O, passing ct into every await
        // so the call stops cooperatively if the host is shutting down.
        await Task.Delay(300, ct);

        _logger.LogInformation(
            "[BackgroundService] Completed {JobType} for QuoteId={QuoteId} (JobId={JobId})",
            job.JobType, job.QuoteId, job.Id);
    }
}
