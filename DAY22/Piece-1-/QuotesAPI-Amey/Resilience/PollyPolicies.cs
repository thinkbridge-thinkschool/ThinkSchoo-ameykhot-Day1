using Polly;
using Polly.Extensions.Http;
using Polly.Timeout;

namespace QuotesApi.Resilience;

public static class PollyPolicies
{
    public static IAsyncPolicy<HttpResponseMessage> GetResiliencePipeline(ILogger logger)
    {
        var retryPolicy = HttpPolicyExtensions
            .HandleTransientHttpError()
            .Or<TimeoutRejectedException>()
            .WaitAndRetryAsync(
                retryCount: 3,
                sleepDurationProvider: attempt => TimeSpan.FromSeconds(Math.Pow(2, attempt)),
                onRetry: (outcome, timespan, attempt, context) =>
                {
                    logger.LogWarning(
                        "[Polly RETRY] Attempt {Attempt} failed: {Error}. Waiting {Delay}s before retry.",
                        attempt,
                        outcome.Exception?.Message ?? outcome.Result?.StatusCode.ToString(),
                        timespan.TotalSeconds);
                });

        var circuitBreakerPolicy = HttpPolicyExtensions
            .HandleTransientHttpError()
            .Or<TimeoutRejectedException>()
            .CircuitBreakerAsync(
                handledEventsAllowedBeforeBreaking: 5,
                durationOfBreak: TimeSpan.FromSeconds(30),
                onBreak: (outcome, breakDuration) =>
                {
                    logger.LogError(
                        "[Polly CIRCUIT OPEN] Too many failures. Circuit open for {Duration}s. Error: {Error}",
                        breakDuration.TotalSeconds,
                        outcome.Exception?.Message ?? outcome.Result?.StatusCode.ToString());
                },
                onReset: () =>
                {
                    logger.LogInformation("[Polly CIRCUIT CLOSED] Circuit reset — service recovered!");
                },
                onHalfOpen: () =>
                {
                    logger.LogWarning("[Polly CIRCUIT HALF-OPEN] Testing if service recovered...");
                });

        var timeoutPolicy = Policy.TimeoutAsync<HttpResponseMessage>(
            seconds: 5,
            onTimeoutAsync: (context, timespan, task) =>
            {
                logger.LogWarning("[Polly TIMEOUT] Request timed out after {Seconds}s", timespan.TotalSeconds);
                return Task.CompletedTask;
            });

        var bulkheadPolicy = Policy.BulkheadAsync<HttpResponseMessage>(
            maxParallelization: 10,
            maxQueuingActions: 20,
            onBulkheadRejectedAsync: context =>
            {
                logger.LogWarning("[Polly BULKHEAD] Too many concurrent requests — rejected!");
                return Task.CompletedTask;
            });

        // Execution order (outermost → innermost):
        // Bulkhead → CircuitBreaker → Retry → Timeout → actual HTTP call
        return Policy.WrapAsync(
            bulkheadPolicy,
            circuitBreakerPolicy,
            retryPolicy,
            timeoutPolicy);
    }
}
