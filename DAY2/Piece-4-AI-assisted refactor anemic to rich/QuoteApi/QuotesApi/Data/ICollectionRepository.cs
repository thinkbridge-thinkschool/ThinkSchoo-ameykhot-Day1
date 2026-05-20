using Microsoft.EntityFrameworkCore;
using QuotesApi.Models;

namespace QuotesApi.Data;

public interface ICollectionRepository
{
    Task<Collection?> GetByIdAsync(int id, CancellationToken cancellationToken = default);
    Task AddAsync(Collection collection, CancellationToken cancellationToken = default);
    Task UpdateAsync(Collection collection, CancellationToken cancellationToken = default);
    Task DeleteAsync(int id, CancellationToken cancellationToken = default);
}

public class CollectionRepository : ICollectionRepository
{
    private readonly QuoteDbContext _context;

    public CollectionRepository(QuoteDbContext context)
    {
        _context = context;
    }

    public async Task<Collection?> GetByIdAsync(int id, CancellationToken cancellationToken = default)
    {
        return await _context.Collections
            .Include("_items")
            .FirstOrDefaultAsync(c => c.Id == id, cancellationToken);
    }

    public async Task AddAsync(Collection collection, CancellationToken cancellationToken = default)
    {
        await _context.Collections.AddAsync(collection, cancellationToken);
        await _context.SaveChangesAsync(cancellationToken);
    }

    public async Task UpdateAsync(Collection collection, CancellationToken cancellationToken = default)
    {
        _context.Collections.Update(collection);
        await _context.SaveChangesAsync(cancellationToken);
    }

    public async Task DeleteAsync(int id, CancellationToken cancellationToken = default)
    {
        var collection = await GetByIdAsync(id, cancellationToken);
        if (collection is not null)
        {
            _context.Collections.Remove(collection);
            await _context.SaveChangesAsync(cancellationToken);
        }
    }
}
