using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using QuotesApi.Data;
using QuotesApi.Models;
using Xunit;

namespace Quotes.Tests.Unit;

public class QuoteRepositoryTests
{
    private static QuoteDbContext NewDb() =>
        new(new DbContextOptionsBuilder<QuoteDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options);

    private static ILogger<QuoteRepository> NullLog =>
        NullLogger<QuoteRepository>.Instance;

    [Fact]
    public async Task GetQuotesAsync_ReturnsAllQuotesWithPagination()
    {
        await using var db = NewDb();
        db.Quotes.AddRange(
            new Quote("A1", "T1", DateTime.UtcNow),
            new Quote("A2", "T2", DateTime.UtcNow),
            new Quote("A3", "T3", DateTime.UtcNow));
        await db.SaveChangesAsync();

        var repo = new QuoteRepository(db, NullLog);
        var result = await repo.GetQuotesAsync(page: 1, size: 2);

        result.Items.Should().HaveCount(2);
        result.Total.Should().Be(3);
        result.Page.Should().Be(1);
        result.Size.Should().Be(2);
    }

    [Fact]
    public async Task GetQuoteByIdAsync_WhenExists_ReturnsQuote()
    {
        await using var db = NewDb();
        var quote = new Quote("Auth", "Text", DateTime.UtcNow);
        db.Quotes.Add(quote);
        await db.SaveChangesAsync();

        var repo = new QuoteRepository(db, NullLog);
        var found = await repo.GetQuoteByIdAsync(quote.Id);

        found.Should().NotBeNull();
        found!.Author.Should().Be("Auth");
    }

    [Fact]
    public async Task GetQuoteByIdAsync_WhenNotExists_ReturnsNull()
    {
        await using var db = NewDb();
        var repo = new QuoteRepository(db, NullLog);

        var found = await repo.GetQuoteByIdAsync(9999);

        found.Should().BeNull();
    }

    [Fact]
    public async Task CreateQuoteAsync_PersistsQuoteAndReturnsIt()
    {
        await using var db = NewDb();
        var repo = new QuoteRepository(db, NullLog);
        var quote = new Quote("Author", "Some text here", DateTime.UtcNow);

        var created = await repo.CreateQuoteAsync(quote);

        created.Id.Should().BeGreaterThan(0);
        db.Quotes.Should().ContainSingle();
    }

    [Fact]
    public async Task DeleteQuoteAsync_WhenExists_RemovesAndReturnsTrue()
    {
        await using var db = NewDb();
        var quote = new Quote("Author", "Text", DateTime.UtcNow);
        db.Quotes.Add(quote);
        await db.SaveChangesAsync();
        var repo = new QuoteRepository(db, NullLog);

        var deleted = await repo.DeleteQuoteAsync(quote.Id);

        deleted.Should().BeTrue();
        db.Quotes.Should().BeEmpty();
    }

    [Fact]
    public async Task DeleteQuoteAsync_WhenNotExists_ReturnsFalse()
    {
        await using var db = NewDb();
        var repo = new QuoteRepository(db, NullLog);

        var deleted = await repo.DeleteQuoteAsync(9999);

        deleted.Should().BeFalse();
    }

    [Fact]
    public async Task SaveChangesAsync_PersistsChanges()
    {
        await using var db = NewDb();
        var repo = new QuoteRepository(db, NullLog);
        var quote = new Quote("SaveAuth", "SaveText", DateTime.UtcNow);
        db.Quotes.Add(quote);

        await repo.SaveChangesAsync();

        db.Quotes.Should().ContainSingle(q => q.Author == "SaveAuth");
    }
}

public class CollectionRepositoryTests
{
    private static QuoteDbContext NewDb() =>
        new(new DbContextOptionsBuilder<QuoteDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options);

    [Fact]
    public async Task AddAsync_PersistsCollection()
    {
        await using var db = NewDb();
        var repo = new CollectionRepository(db);
        var col = new Collection("My Favourites", 42);

        await repo.AddAsync(col);

        db.Collections.Should().ContainSingle(c => c.Name == "My Favourites");
    }

    [Fact]
    public async Task GetByIdAsync_WhenExists_ReturnsCollection()
    {
        await using var db = NewDb();
        var repo = new CollectionRepository(db);
        var col = new Collection("Test Col", 1);
        await repo.AddAsync(col);

        var found = await repo.GetByIdAsync(col.Id);

        found.Should().NotBeNull();
        found!.Name.Should().Be("Test Col");
    }

    [Fact]
    public async Task GetByIdAsync_WhenNotExists_ReturnsNull()
    {
        await using var db = NewDb();
        var repo = new CollectionRepository(db);

        var found = await repo.GetByIdAsync(9999);

        found.Should().BeNull();
    }

    [Fact]
    public async Task UpdateAsync_PersistsChanges()
    {
        await using var db = NewDb();
        var repo = new CollectionRepository(db);
        var col = new Collection("Old Name", 1);
        await repo.AddAsync(col);

        col.Rename("New Name");
        await repo.UpdateAsync(col);

        var updated = await repo.GetByIdAsync(col.Id);
        updated!.Name.Should().Be("New Name");
    }

    [Fact]
    public async Task DeleteAsync_WhenExists_RemovesCollection()
    {
        await using var db = NewDb();
        var repo = new CollectionRepository(db);
        var col = new Collection("To Delete", 1);
        await repo.AddAsync(col);

        await repo.DeleteAsync(col.Id);

        db.Collections.Should().BeEmpty();
    }

    [Fact]
    public async Task DeleteAsync_WhenNotExists_DoesNotThrow()
    {
        await using var db = NewDb();
        var repo = new CollectionRepository(db);

        var act = () => repo.DeleteAsync(9999);

        await act.Should().NotThrowAsync();
    }
}
