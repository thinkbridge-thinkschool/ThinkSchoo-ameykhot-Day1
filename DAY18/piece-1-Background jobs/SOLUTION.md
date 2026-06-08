# Day 18 — Background Jobs

## BackgroundService Implementation

```csharp
// QuotesAPI-Amey/BackgroundJobs/QuoteProcessingService.cs
public class QuoteProcessingService : BackgroundService
{
    private readonly ILogger<QuoteProcessingService> _logger;
    private readonly Channel<QuoteJob> _channel;

    public QuoteProcessingService(ILogger<QuoteProcessingService> logger, Channel<QuoteJob> channel)
    {
        _logger = logger;
        _channel = channel;
    }

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
        await Task.Delay(300, ct); // Replace with real work: email, analytics, etc.
        _logger.LogInformation(
            "[BackgroundService] Completed {JobType} for QuoteId={QuoteId}",
            job.JobType, job.QuoteId);
    }
}
```

## How it shuts down cleanly

When the app receives a shutdown signal (Ctrl+C / Azure SIGTERM), `stoppingToken` is cancelled.
`ReadAllAsync(stoppingToken)` stops waiting for new items and exits the loop.
`OperationCanceledException` is caught and logged as a normal event, not an error.
The `finally` block logs the stop timestamp.
Any job currently in `ProcessAsync` also receives the token and stops at the next `await`,
so no job is killed mid-execution — Azure/Kubernetes get a clean exit within their 30-second window.

## When Hangfire over a hosted service?

Use Hangfire when jobs must survive a process restart, need automatic retry on failure,
require a cron schedule, or you need a dashboard to inspect job history — `BackgroundService`
is correct for in-process fire-and-forget work that can be lost on shutdown.

## Screenshots

| # | What it proves |
|---|---------------|
| [01-startup.png](ScreenShots/01-startup.png) | `[BackgroundService] Started at ...` — service boots with the app |
| [02-enqueue-202.png](ScreenShots/02-enqueue-202.png) | `POST /api/jobs/enqueue` returns **202 Accepted** instantly |
| [03-processing-logs.png](ScreenShots/03-processing-logs.png) | Console shows Processing + Completed logs off the request thread |
| [04-create-quote-autoenqueue.png](ScreenShots/04-create-quote-autoenqueue.png) | `POST /api/quotes` → 201 Created, console shows `notify-followers` job enqueued and processed |
| [05-graceful-shutdown.png](ScreenShots/05-graceful-shutdown.png) | Ctrl+C triggers `Shutdown requested — stopping cleanly` then `Stopped at ...` |

## GitHub

Branch: `Day18/background-jobs`  
Folder: `DAY18/piece-1-Background jobs/QuotesAPI-Amey/BackgroundJobs/`
