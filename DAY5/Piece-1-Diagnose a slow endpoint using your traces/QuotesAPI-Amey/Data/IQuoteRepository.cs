using Microsoft.EntityFrameworkCore;
using QuotesApi.Models;

namespace QuotesApi.Data;

public interface IQuoteRepository
{
    Task<PaginatedResult<Quote>> GetQuotesAsync(int page, int size, CancellationToken cancellationToken = default);
    Task<Quote?> GetQuoteByIdAsync(int id, CancellationToken cancellationToken = default);
    Task<Quote> CreateQuoteAsync(Quote quote, CancellationToken cancellationToken = default);
    Task<bool> DeleteQuoteAsync(int id, CancellationToken cancellationToken = default);
    Task SaveChangesAsync(CancellationToken cancellationToken = default);
}

public class PaginatedResult<T>
{
    public List<T> Items { get; set; } = new();
    public int Total { get; set; }
    public int Page { get; set; }
    public int Size { get; set; }
}

public class QuoteRepository : IQuoteRepository
{
    private readonly QuoteDbContext _context;
    private readonly ILogger<QuoteRepository> _logger;

    public QuoteRepository(QuoteDbContext context, ILogger<QuoteRepository> logger)
    {
        _context = context;
        _logger = logger;
    }

    public async Task<PaginatedResult<Quote>> GetQuotesAsync(int page, int size, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Fetching quotes with page={Page}, size={Size}", page, size);

        // N+1 ANTIPATTERN: load all IDs then query each quote individually.
        // This is intentionally slow — see Day 5 Piece 1 diagnosis exercise.
        var allIds = await _context.Quotes
            .OrderByDescending(q => q.CreatedAt)
            .Select(q => q.Id)
            .ToListAsync(cancellationToken);

        var total = allIds.Count;
        var pageIds = allIds.Skip((page - 1) * size).Take(size).ToList();

        var items = new List<Quote>();
        foreach (var id in pageIds)
        {
            var quote = await _context.Quotes
                .FirstOrDefaultAsync(q => q.Id == id, cancellationToken);
            if (quote is not null)
                items.Add(quote);
        }

        return new PaginatedResult<Quote>
        {
            Items = items,
            Total = total,
            Page = page,
            Size = size
        };
    }

    public async Task<Quote?> GetQuoteByIdAsync(int id, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Fetching quote with id={QuoteId}", id);
        return await _context.Quotes.FirstOrDefaultAsync(q => q.Id == id, cancellationToken);
    }

    public async Task<Quote> CreateQuoteAsync(Quote quote, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Creating quote by {Author}", quote.Author);
        _context.Quotes.Add(quote);
        await SaveChangesAsync(cancellationToken);
        return quote;
    }

    public async Task<bool> DeleteQuoteAsync(int id, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Deleting quote with id={QuoteId}", id);
        var quote = await GetQuoteByIdAsync(id, cancellationToken);
        if (quote is null)
            return false;

        _context.Quotes.Remove(quote);
        await SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        await _context.SaveChangesAsync(cancellationToken);
    }
}
