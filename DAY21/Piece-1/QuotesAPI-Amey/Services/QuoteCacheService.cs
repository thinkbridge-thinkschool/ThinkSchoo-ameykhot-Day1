using Microsoft.Extensions.Caching.Hybrid;
using QuotesApi.Data;
using QuotesApi.Models;

namespace QuotesApi.Services;

/// <summary>
/// Wraps IQuoteRepository with a two-layer HybridCache (L1 in-memory + L2 Redis).
/// Stampede protection is built into HybridCache.GetOrCreateAsync — under concurrent
/// cache misses only ONE factory (DB call) fires; all other callers wait and receive
/// the same result without hitting the database.
/// </summary>
public sealed class QuoteCacheService
{
    // Internal DTO — a record is a plain POCO that System.Text.Json can round-trip
    // without any configuration, avoiding the private-setter issue on Quote.
    private sealed record QuoteDto(
        int Id, string Author, string Text, DateTime CreatedAt,
        Guid? OwnerId, int? AuthorId);

    private readonly IQuoteRepository _repo;
    private readonly HybridCache _cache;
    private readonly ILogger<QuoteCacheService> _logger;

    // Process-wide counters — static so every scoped instance shares them.
    private static int _dbHits;
    private static int _cacheHits;

    public QuoteCacheService(
        IQuoteRepository repo,
        HybridCache cache,
        ILogger<QuoteCacheService> logger)
    {
        _repo = repo;
        _cache = cache;
        _logger = logger;
    }

    /// <summary>
    /// Returns the quote from L1 → L2 → DB.
    /// Only ONE DB round-trip fires per key regardless of concurrent callers (stampede protection).
    /// </summary>
    public async Task<Quote?> GetByIdAsync(int id, CancellationToken ct = default)
    {
        var key = $"quote:{id}";
        var dbFired = false;

        var dto = await _cache.GetOrCreateAsync<QuoteDto?>(
            key,
            async innerCt =>
            {
                // This factory runs only ONCE even if 1000 concurrent requests miss the cache
                // at the same moment. HybridCache serialises duplicate calls — the other 999
                // await the first result and get it from the cache without reaching the DB.
                dbFired = true;
                Interlocked.Increment(ref _dbHits);
                _logger.LogInformation("[Cache MISS] Fetching quote {Id} from DB", id);

                var q = await _repo.GetQuoteByIdAsync(id, innerCt);
                if (q is null) return null;
                return new QuoteDto(q.Id, q.Author, q.Text, q.CreatedAt, q.OwnerId, q.AuthorId);
            },
            new HybridCacheEntryOptions
            {
                Expiration = TimeSpan.FromMinutes(5),           // L2 Redis TTL
                LocalCacheExpiration = TimeSpan.FromMinutes(1)  // L1 in-memory TTL
            },
            cancellationToken: ct
        );

        // dbFired == false means the result came from L1 or L2 (no DB round-trip)
        if (!dbFired) Interlocked.Increment(ref _cacheHits);

        if (dto is null) return null;

        // Reconstruct Quote from the DTO (avoids private-setter serialisation issues)
        var quote = new Quote(dto.Author, dto.Text, dto.CreatedAt);
        quote.Id = dto.Id;
        quote.OwnerId = dto.OwnerId;
        quote.AuthorId = dto.AuthorId;
        return quote;
    }

    /// <summary>
    /// Call after a quote is mutated (update / delete) so stale data isn't served.
    /// </summary>
    public async Task InvalidateAsync(int id)
    {
        await _cache.RemoveAsync($"quote:{id}");
        _logger.LogInformation("[Cache] Invalidated quote:{Id}", id);
    }

    // ── Metrics ──────────────────────────────────────────────────────────────

    public static (int DbHits, int CacheHits, double HitRatePct) GetStats()
    {
        var db = _dbHits;
        var hits = _cacheHits;
        var total = db + hits;
        return (db, hits, total == 0 ? 0.0 : Math.Round((double)hits / total * 100.0, 2));
    }

    public static void ResetStats()
    {
        Interlocked.Exchange(ref _dbHits, 0);
        Interlocked.Exchange(ref _cacheHits, 0);
    }
}
