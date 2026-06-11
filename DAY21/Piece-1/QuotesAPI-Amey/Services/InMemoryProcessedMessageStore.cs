using System.Collections.Concurrent;

namespace QuotesApi.Services;

// Thread-safe in-memory deduplication store.
// In production use Redis or a DB-backed table so the store survives restarts.
public class InMemoryProcessedMessageStore : IProcessedMessageStore
{
    private readonly ConcurrentDictionary<string, DateTime> _processed = new();

    public Task<bool> IsProcessedAsync(string messageId)
        => Task.FromResult(_processed.ContainsKey(messageId));

    public Task MarkProcessedAsync(string messageId)
    {
        _processed.TryAdd(messageId, DateTime.UtcNow);
        return Task.CompletedTask;
    }
}
