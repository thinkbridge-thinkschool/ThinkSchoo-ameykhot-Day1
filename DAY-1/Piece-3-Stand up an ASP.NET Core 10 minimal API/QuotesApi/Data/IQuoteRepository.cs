using Microsoft.EntityFrameworkCore;
using QuotesApi.Models;

namespace QuotesApi.Data;

public interface IQuoteRepository
{
    Task<PaginatedResult<Quote>> GetQuotesAsync(int page, int size, string search = "", CancellationToken cancellationToken = default);
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

    public async Task<PaginatedResult<Quote>> GetQuotesAsync(int page, int size, string search = "", CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Fetching quotes page={Page} size={Size} search={Search}", page, size, search);

        var query = _context.Quotes.AsQueryable();

        if (!string.IsNullOrWhiteSpace(search))
            query = query.Where(q =>
                q.Author.Contains(search) || q.Text.Contains(search));

        var total = await query.CountAsync(cancellationToken);
        var items = await query
            .OrderByDescending(q => q.CreatedAt)
            .Skip((page - 1) * size)
            .Take(size)
            .ToListAsync(cancellationToken);

        return new PaginatedResult<Quote> { Items = items, Total = total, Page = page, Size = size };
    }

    public async Task<Quote?> GetQuoteByIdAsync(int id, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Fetching quote id={QuoteId}", id);
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
        _logger.LogInformation("Deleting quote id={QuoteId}", id);
        var quote = await GetQuoteByIdAsync(id, cancellationToken);
        if (quote is null) return false;
        _context.Quotes.Remove(quote);
        await SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task SaveChangesAsync(CancellationToken cancellationToken = default)
        => await _context.SaveChangesAsync(cancellationToken);
}
