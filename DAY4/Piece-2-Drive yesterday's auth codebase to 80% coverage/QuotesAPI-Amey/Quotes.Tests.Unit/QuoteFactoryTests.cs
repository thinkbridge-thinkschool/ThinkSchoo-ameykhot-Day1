using FluentAssertions;
using NSubstitute;
using QuotesApi.Services;
using QuotesApi.Time;
using Xunit;

namespace Quotes.Tests.Unit;

public class QuoteFactoryTests
{
    [Fact]
    public void Create_WhenNoTimestampProvided_UsesClockUtcNow()
    {
        var fixedNow = new DateTimeOffset(2026, 5, 19, 12, 30, 0, TimeSpan.Zero);
        var clock = Substitute.For<IClock>();
        clock.UtcNow.Returns(fixedNow);
        var sut = new QuoteFactory(clock);

        var quote = sut.Create("Author", "Text");

        quote.CreatedAt.Should().Be(fixedNow.UtcDateTime);
    }

    [Fact]
    public void Create_WhenExplicitTimestampProvided_IgnoresClock()
    {
        var explicitTimestamp = new DateTime(2024, 1, 2, 3, 4, 5, DateTimeKind.Utc);
        var clock = Substitute.For<IClock>();
        clock.UtcNow.Returns(new DateTimeOffset(2026, 5, 19, 12, 30, 0, TimeSpan.Zero));
        var sut = new QuoteFactory(clock);

        var quote = sut.Create("Author", "Text", explicitTimestamp);

        quote.CreatedAt.Should().Be(explicitTimestamp);
    }

    [Fact]
    public void Create_SetsAuthorFromInput()
    {
        var clock = Substitute.For<IClock>();
        clock.UtcNow.Returns(DateTimeOffset.UtcNow);
        var sut = new QuoteFactory(clock);

        var quote = sut.Create("Jane Austen", "It is a truth universally acknowledged");

        quote.Author.Should().Be("Jane Austen");
    }

    [Fact]
    public void Create_SetsTextFromInput()
    {
        var clock = Substitute.For<IClock>();
        clock.UtcNow.Returns(DateTimeOffset.UtcNow);
        var sut = new QuoteFactory(clock);

        var quote = sut.Create("Any Author", "The quick brown fox");

        quote.Text.Should().Be("The quick brown fox");
    }

    [Fact]
    public void Create_CreatedAtIsUtcKind()
    {
        var clock = Substitute.For<IClock>();
        clock.UtcNow.Returns(new DateTimeOffset(2026, 5, 19, 12, 0, 0, TimeSpan.Zero));
        var sut = new QuoteFactory(clock);

        var quote = sut.Create("Author", "Text");

        quote.CreatedAt.Kind.Should().Be(DateTimeKind.Utc);
    }
}
