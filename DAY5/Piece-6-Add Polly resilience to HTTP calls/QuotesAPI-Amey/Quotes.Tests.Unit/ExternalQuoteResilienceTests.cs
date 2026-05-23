using System.Net;
using System.Text;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http.Resilience;
using Microsoft.Extensions.Logging;
using Polly;
using QuotesApi.Services;
using Xunit;
using Xunit.Abstractions;

namespace Quotes.Tests.Unit;

/// <summary>
/// Verifies Polly retry + circuit-breaker + timeout behavior for ExternalQuoteService.
/// Uses a SequentialResponseHandler (no real network calls) to inject transient 503s.
/// </summary>
public sealed class ExternalQuoteResilienceTests(ITestOutputHelper output)
{
    // ── Test 1: retry succeeds on 3rd attempt ────────────────────────────────

    [Fact]
    public async Task Polly_RetriesOnTransientFailure_AndSucceedsOnThirdAttempt()
    {
        // Arrange — fail twice with 503, succeed on the third call
        const string successBody = """[{"q":"Life is good","a":"Test Author"}]""";
        var retryLog = new List<string>();

        var handler = new SequentialResponseHandler(
            new HttpResponseMessage(HttpStatusCode.ServiceUnavailable),  // attempt 1 → fail
            new HttpResponseMessage(HttpStatusCode.ServiceUnavailable),  // attempt 2 → fail
            new HttpResponseMessage(HttpStatusCode.OK)                   // attempt 3 → succeed
            {
                Content = new StringContent(successBody, Encoding.UTF8, "application/json")
            });

        var services = new ServiceCollection();
        services.AddLogging(b => b.SetMinimumLevel(LogLevel.Warning));
        services
            .AddHttpClient<IExternalQuoteService, ExternalQuoteService>(
                c => c.BaseAddress = new Uri("http://test.local"))
            .ConfigurePrimaryHttpMessageHandler(() => handler)
            .AddResilienceHandler("test", pipeline =>
            {
                pipeline.AddRetry(new HttpRetryStrategyOptions
                {
                    MaxRetryAttempts = 3,
                    BackoffType = DelayBackoffType.Constant,
                    Delay = TimeSpan.Zero,   // no wait in tests
                    OnRetry = args =>
                    {
                        var msg = $"[RETRY] attempt={args.AttemptNumber} status={args.Outcome.Result?.StatusCode}";
                        retryLog.Add(msg);
                        output.WriteLine(msg);
                        return default;
                    }
                });
                pipeline.AddCircuitBreaker(new HttpCircuitBreakerStrategyOptions
                {
                    MinimumThroughput = 10,  // high — won't trip in this test
                    FailureRatio = 0.5,
                    SamplingDuration = TimeSpan.FromSeconds(30),
                    BreakDuration = TimeSpan.FromSeconds(1)
                });
                pipeline.AddTimeout(TimeSpan.FromSeconds(30));
            });

        await using var provider = services.BuildServiceProvider();
        var svc = provider.GetRequiredService<IExternalQuoteService>();

        // Act
        var result = await svc.GetRandomQuoteAsync(CancellationToken.None);

        // Assert
        result.Should().NotBeNull();
        result!.Text.Should().Be("Life is good");
        result.Author.Should().Be("Test Author");
        handler.CallCount.Should().Be(3, "initial call + 2 retries");
        retryLog.Should().HaveCount(2, "Polly fired OnRetry twice before succeeding");

        output.WriteLine($"\nSummary: handler called {handler.CallCount}×, retries logged: {retryLog.Count}");
        foreach (var entry in retryLog)
            output.WriteLine($"  {entry}");
    }

    // ── Test 2: all retries exhausted → HttpRequestException ────────────────

    [Fact]
    public async Task Polly_ExhaustsAllRetries_WhenEveryAttemptFails()
    {
        var retryLog = new List<string>();

        // 4 failures: initial + 3 retries
        var handler = new SequentialResponseHandler(
            new HttpResponseMessage(HttpStatusCode.ServiceUnavailable),
            new HttpResponseMessage(HttpStatusCode.ServiceUnavailable),
            new HttpResponseMessage(HttpStatusCode.ServiceUnavailable),
            new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));

        var services = new ServiceCollection();
        services.AddLogging(b => b.SetMinimumLevel(LogLevel.Warning));
        services
            .AddHttpClient<IExternalQuoteService, ExternalQuoteService>(
                c => c.BaseAddress = new Uri("http://test.local"))
            .ConfigurePrimaryHttpMessageHandler(() => handler)
            .AddResilienceHandler("test", pipeline =>
            {
                pipeline.AddRetry(new HttpRetryStrategyOptions
                {
                    MaxRetryAttempts = 3,
                    BackoffType = DelayBackoffType.Constant,
                    Delay = TimeSpan.Zero,
                    OnRetry = args =>
                    {
                        var msg = $"[RETRY] attempt={args.AttemptNumber} status={args.Outcome.Result?.StatusCode}";
                        retryLog.Add(msg);
                        output.WriteLine(msg);
                        return default;
                    }
                });
            });

        await using var provider = services.BuildServiceProvider();
        var svc = provider.GetRequiredService<IExternalQuoteService>();

        // Act & Assert
        var act = async () => await svc.GetRandomQuoteAsync(CancellationToken.None);
        await act.Should().ThrowAsync<HttpRequestException>(
            "EnsureSuccessStatusCode throws when all retries return 503");

        handler.CallCount.Should().Be(4, "initial call + 3 retries, all failing");
        retryLog.Should().HaveCount(3, "all 3 retry callbacks were logged");

        output.WriteLine($"\nSummary: handler called {handler.CallCount}×, retries logged: {retryLog.Count}");
    }
}

// ── Fake handler: returns pre-queued responses in order ─────────────────────
internal sealed class SequentialResponseHandler(params HttpResponseMessage[] responses) : HttpMessageHandler
{
    private readonly Queue<HttpResponseMessage> _queue = new(responses);
    public int CallCount { get; private set; }

    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        CallCount++;
        return Task.FromResult(
            _queue.Count > 0
                ? _queue.Dequeue()
                : new HttpResponseMessage(HttpStatusCode.OK));
    }
}
