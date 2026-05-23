using FluentAssertions;
using QuotesApi.Validators;
using Xunit;

namespace Quotes.Tests.Unit;

public class CreateQuoteRequestValidatorTests
{
    // ── Author rules ──────────────────────────────────────────────────────────

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Validate_WhenAuthorEmptyOrWhitespace_ReturnsAuthorRequiredError(string author)
    {
        var sut = new CreateQuoteRequestValidator();
        var request = new CreateQuoteRequest { Author = author, Text = "Some text" };

        var result = sut.Validate(request);

        result.IsValid.Should().BeFalse();
        result.Errors.Should()
            .Contain(e => e.PropertyName == "Author" && e.ErrorMessage == "Author is required");
    }

    [Fact]
    public void Validate_WhenAuthorExceeds256Characters_ReturnsMaxLengthError()
    {
        var sut = new CreateQuoteRequestValidator();
        var request = new CreateQuoteRequest { Author = new string('A', 257), Text = "Some text" };

        var result = sut.Validate(request);

        result.IsValid.Should().BeFalse();
        result.Errors.Should()
            .Contain(e => e.PropertyName == "Author" && e.ErrorMessage == "Author must be at most 256 characters");
    }

    [Fact]
    public void Validate_WhenAuthorIs256Characters_NoAuthorErrors()
    {
        var sut = new CreateQuoteRequestValidator();
        var request = new CreateQuoteRequest { Author = new string('A', 256), Text = "Some text" };

        var result = sut.Validate(request);

        result.Errors.Should().NotContain(e => e.PropertyName == "Author");
    }

    // ── Text rules ────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Validate_WhenTextEmptyOrWhitespace_ReturnsTextRequiredError(string text)
    {
        var sut = new CreateQuoteRequestValidator();
        var request = new CreateQuoteRequest { Author = "Valid Author", Text = text };

        var result = sut.Validate(request);

        result.IsValid.Should().BeFalse();
        result.Errors.Should()
            .Contain(e => e.PropertyName == "Text" && e.ErrorMessage == "Text is required");
    }

    [Fact]
    public void Validate_WhenTextExceeds2000Characters_ReturnsMaxLengthError()
    {
        var sut = new CreateQuoteRequestValidator();
        var request = new CreateQuoteRequest { Author = "Valid Author", Text = new string('T', 2001) };

        var result = sut.Validate(request);

        result.IsValid.Should().BeFalse();
        result.Errors.Should()
            .Contain(e => e.PropertyName == "Text" && e.ErrorMessage == "Text must be at most 2000 characters");
    }

    [Fact]
    public void Validate_WhenTextIs2000Characters_NoTextErrors()
    {
        var sut = new CreateQuoteRequestValidator();
        var request = new CreateQuoteRequest { Author = "Valid Author", Text = new string('T', 2000) };

        var result = sut.Validate(request);

        result.Errors.Should().NotContain(e => e.PropertyName == "Text");
    }

    // ── Happy path ────────────────────────────────────────────────────────────

    [Fact]
    public void Validate_WithBothFieldsValid_ReturnsIsValidTrue()
    {
        var sut = new CreateQuoteRequestValidator();
        var request = new CreateQuoteRequest
        {
            Author = "Albert Einstein",
            Text = "Imagination is more important than knowledge."
        };

        var result = sut.Validate(request);

        result.IsValid.Should().BeTrue();
        result.Errors.Should().BeEmpty();
    }

    [Theory]
    [InlineData("X", "Y")]
    [InlineData("Author Name", "Short text")]
    public void Validate_WithVariousValidInputs_Succeeds(string author, string text)
    {
        var sut = new CreateQuoteRequestValidator();
        var request = new CreateQuoteRequest { Author = author, Text = text };

        var result = sut.Validate(request);

        result.IsValid.Should().BeTrue();
    }
}
