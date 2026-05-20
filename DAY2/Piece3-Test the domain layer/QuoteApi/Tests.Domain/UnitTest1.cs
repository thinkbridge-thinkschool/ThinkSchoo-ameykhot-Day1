using FluentAssertions;
using QuotesApi.Models;

namespace Tests.Domain;

public class CollectionInvariantTests
{
    [Fact]
    public void Empty_name_throws()
    {
        Action act = () => new Collection(" ", 1);
        act.Should().Throw<DomainException>();
    }

    [Fact]
    public void Name_over_80_chars_throws()
    {
        Action act = () => new Collection(new string('a', 81), 1);
        act.Should().Throw<DomainException>();
    }

    [Fact]
    public void Fifty_first_item_throws()
    {
        var collection = new Collection("Favorites", 1);
        Enumerable.Range(1, 50).ToList().ForEach(collection.AddItem);
        Action act = () => collection.AddItem(51);
        act.Should().Throw<DomainException>();
    }

    [Fact]
    public void Duplicate_quote_id_throws()
    {
        var collection = new Collection("Favorites", 1);
        collection.AddItem(42);
        Action act = () => collection.AddItem(42);
        act.Should().Throw<DomainException>();
    }

    [Fact]
    public void Removing_non_existent_item_throws()
    {
        var collection = new Collection("Favorites", 1);
        Action act = () => collection.RemoveItem(999);
        act.Should().Throw<DomainException>();
    }

    [Fact]
    public void Add_then_remove_leaves_zero_items()
    {
        var collection = new Collection("Favorites", 1);
        collection.AddItem(7);
        collection.RemoveItem(7);
        collection.Items.Should().BeEmpty();
    }
}
