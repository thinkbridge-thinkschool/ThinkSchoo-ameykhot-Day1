using FluentAssertions;
using QuotesApi.Models;

namespace Tests.Domain;

public class QuoteInvariantTests
{
    [Fact]
    public void Create_fails_when_author_empty()
    {
        var (quote, error) = Quote.Create(" ", "valid text");

        quote.Should().BeNull();
        error.Should().Be("Author must be between 1 and 200 characters.");
    }

    [Fact]
    public void Create_fails_when_text_empty()
    {
        var (quote, error) = Quote.Create("Author", " ");

        quote.Should().BeNull();
        error.Should().Be("Text must be between 1 and 1000 characters.");
    }

    [Fact]
    public void Create_fails_when_author_too_long()
    {
        var (_, error) = Quote.Create(new string('a', 201), "valid text");

        error.Should().Be("Author must be between 1 and 200 characters.");
    }

    [Fact]
    public void Create_fails_when_text_too_long()
    {
        var (_, error) = Quote.Create("Author", new string('t', 1001));

        error.Should().Be("Text must be between 1 and 1000 characters.");
    }

    [Fact]
    public void Create_succeeds_and_text_is_immutable_by_api()
    {
        var (quote, error) = Quote.Create("Author", "Hello");

        error.Should().BeNull();
        quote.Should().NotBeNull();
        quote!.Text.Should().Be("Hello");
    }

    [Fact]
    public void SoftDelete_sets_deleted_flag()
    {
        var (quote, _) = Quote.Create("Author", "Hello");
        var entity = quote!;

        entity.IsDeleted.Should().BeFalse();
        entity.SoftDelete();
        entity.IsDeleted.Should().BeTrue();
    }
}
