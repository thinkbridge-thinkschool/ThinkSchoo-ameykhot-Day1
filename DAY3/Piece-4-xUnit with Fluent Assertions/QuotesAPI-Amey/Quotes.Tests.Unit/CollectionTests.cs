using FluentAssertions;
using QuotesApi.Models;
using Xunit;

namespace Quotes.Tests.Unit;

public class CollectionTests
{
    // ── Constructor – name validation ─────────────────────────────────────────

    [Theory]
    [InlineData("AB")]      // 2 chars — below the 3-char minimum
    [InlineData("  A  ")]   // trims to 1 char
    [InlineData("")]        // empty
    public void Constructor_WhenNameTooShort_ThrowsDomainException(string name)
    {
        var act = () => new Collection(name, ownerId: 1);

        act.Should().Throw<DomainException>()
           .WithMessage("Collection name must be between 3 and 80 characters.");
    }

    [Fact]
    public void Constructor_WhenNameExceeds80Characters_ThrowsDomainException()
    {
        var longName = new string('A', 81);

        var act = () => new Collection(longName, ownerId: 1);

        act.Should().Throw<DomainException>()
           .WithMessage("Collection name must be between 3 and 80 characters.");
    }

    [Theory]
    [InlineData("ABC")]            // exactly 3 chars — the minimum boundary
    [InlineData("My Collection")]  // typical name
    public void Constructor_WhenNameIsValid_DoesNotThrow(string name)
    {
        var act = () => new Collection(name, ownerId: 42);

        act.Should().NotThrow();
    }

    [Fact]
    public void Constructor_WhenNameIs80Characters_DoesNotThrow()
    {
        var name80 = new string('A', 80);

        var act = () => new Collection(name80, ownerId: 1);

        act.Should().NotThrow();
    }

    // ── AddItem ───────────────────────────────────────────────────────────────

    [Fact]
    public void AddItem_WhenCollectionAlreadyHas50Items_ThrowsDomainException()
    {
        var collection = new Collection("My Collection", ownerId: 1);
        var ts = DateTime.UtcNow;
        for (var i = 1; i <= 50; i++)
            collection.AddItem(i, ts);

        var act = () => collection.AddItem(quoteId: 51, ts);

        act.Should().Throw<DomainException>()
           .WithMessage("A collection cannot have more than 50 items.");
    }

    [Fact]
    public void AddItem_WhenQuoteAlreadyPresent_ThrowsDomainException()
    {
        var collection = new Collection("My Collection", ownerId: 1);
        collection.AddItem(quoteId: 7, DateTime.UtcNow);

        var act = () => collection.AddItem(quoteId: 7, DateTime.UtcNow);

        act.Should().Throw<DomainException>()
           .WithMessage("Quote 7 is already in this collection.");
    }

    [Fact]
    public void AddItem_WhenNewUniqueQuote_AddsItemAndIncreasesCount()
    {
        var collection = new Collection("My Collection", ownerId: 1);
        var ts = new DateTime(2026, 5, 21, 10, 0, 0, DateTimeKind.Utc);

        collection.AddItem(quoteId: 99, ts);

        collection.Items.Should().HaveCount(1);
        collection.Items[0].QuoteId.Should().Be(99);
        collection.Items[0].AddedAt.Should().Be(ts);
    }

    // ── RemoveItem ────────────────────────────────────────────────────────────

    [Fact]
    public void RemoveItem_WhenQuoteNotInCollection_ThrowsDomainException()
    {
        var collection = new Collection("My Collection", ownerId: 1);

        var act = () => collection.RemoveItem(quoteId: 42);

        act.Should().Throw<DomainException>()
           .WithMessage("Quote 42 was not found in this collection.");
    }

    [Fact]
    public void RemoveItem_WhenQuoteExists_RemovesItFromItems()
    {
        var collection = new Collection("My Collection", ownerId: 1);
        collection.AddItem(quoteId: 5, DateTime.UtcNow);

        collection.RemoveItem(quoteId: 5);

        collection.Items.Should().BeEmpty();
    }

    // ── Rename ────────────────────────────────────────────────────────────────

    [Fact]
    public void Rename_WhenNameIsTooShort_ThrowsDomainException()
    {
        var collection = new Collection("Valid Name", ownerId: 1);

        var act = () => collection.Rename("AB");

        act.Should().Throw<DomainException>()
           .WithMessage("Collection name must be between 3 and 80 characters.");
    }

    [Fact]
    public void Rename_WhenNameIsValid_UpdatesTheName()
    {
        var collection = new Collection("Original Name", ownerId: 1);

        collection.Rename("Renamed");

        collection.Name.Should().Be("Renamed");
    }
}
