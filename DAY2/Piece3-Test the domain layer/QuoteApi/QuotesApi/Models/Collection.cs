namespace QuotesApi.Models;

using Microsoft.EntityFrameworkCore;
using System.ComponentModel.DataAnnotations.Schema;

public class Collection
{
    public int Id { get; private set; }
    public string Name { get; private set; } = string.Empty;
    public int OwnerId { get; private set; }

    private readonly List<CollectionItem> _items = new();
    
    [NotMapped]
    public IReadOnlyList<CollectionItem> Items => _items.AsReadOnly();

    // Required by EF Core — never call directly
    private Collection() { }

    public Collection(string name, int ownerId)
    {
        ValidateName(name);
        Name = name;
        OwnerId = ownerId;
    }

    public void Rename(string name)
    {
        ValidateName(name);
        Name = name;
    }

    public void AddItem(int quoteId)
    {
        if (_items.Count >= 50)
            throw new DomainException("A collection cannot have more than 50 items.");

        if (_items.Any(i => i.QuoteId == quoteId))
            throw new DomainException($"Quote {quoteId} is already in this collection.");

        _items.Add(new CollectionItem(quoteId, DateTime.UtcNow));
    }

    public void RemoveItem(int quoteId)
    {
        var item = _items.FirstOrDefault(i => i.QuoteId == quoteId)
            ?? throw new DomainException($"Quote {quoteId} was not found in this collection.");
        _items.Remove(item);
    }

    private static void ValidateName(string name)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Trim().Length < 3 || name.Trim().Length > 80)
            throw new DomainException("Collection name must be between 3 and 80 characters.");
    }
}
