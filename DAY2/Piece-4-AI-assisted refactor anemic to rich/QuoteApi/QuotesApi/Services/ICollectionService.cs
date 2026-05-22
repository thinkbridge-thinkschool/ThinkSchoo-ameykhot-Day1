using QuotesApi.Data;
using QuotesApi.Models;

namespace QuotesApi.Services;

public interface ICollectionService
{
    Task<Collection> CreateCollectionAsync(string name, int ownerId, CancellationToken cancellationToken);
    Task<Collection?> GetCollectionByIdAsync(int id, CancellationToken cancellationToken);
    Task<Collection?> AddItemToCollectionAsync(int id, int quoteId, CancellationToken cancellationToken);
    Task<Collection?> RemoveItemFromCollectionAsync(int id, int quoteId, CancellationToken cancellationToken);
    Task DeleteCollectionAsync(int id, CancellationToken cancellationToken);
}

public class CollectionService : ICollectionService
{
    private readonly ICollectionRepository _repository;

    public CollectionService(ICollectionRepository repository)
    {
        _repository = repository;
    }

    public async Task<Collection> CreateCollectionAsync(string name, int ownerId, CancellationToken cancellationToken)
    {
        var collection = new Collection(name, ownerId);
        await _repository.AddAsync(collection, cancellationToken);
        return collection;
    }

    public Task<Collection?> GetCollectionByIdAsync(int id, CancellationToken cancellationToken)
    {
        return _repository.GetByIdAsync(id, cancellationToken);
    }

    public async Task<Collection?> AddItemToCollectionAsync(int id, int quoteId, CancellationToken cancellationToken)
    {
        var collection = await _repository.GetByIdAsync(id, cancellationToken);
        if (collection is null)
            return null;

        collection.AddItem(quoteId);
        await _repository.UpdateAsync(collection, cancellationToken);
        return collection;
    }

    public async Task<Collection?> RemoveItemFromCollectionAsync(int id, int quoteId, CancellationToken cancellationToken)
    {
        var collection = await _repository.GetByIdAsync(id, cancellationToken);
        if (collection is null)
            return null;

        collection.RemoveItem(quoteId);
        await _repository.UpdateAsync(collection, cancellationToken);
        return collection;
    }

    public Task DeleteCollectionAsync(int id, CancellationToken cancellationToken)
    {
        return _repository.DeleteAsync(id, cancellationToken);
    }
}