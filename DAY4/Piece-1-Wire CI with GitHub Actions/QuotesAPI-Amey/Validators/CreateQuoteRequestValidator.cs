using FluentValidation;
using QuotesApi.Models;

namespace QuotesApi.Validators;

public class CreateQuoteRequest
{
    public string Author { get; set; } = string.Empty;
    public string Text { get; set; } = string.Empty;
}

public class CreateQuoteRequestValidator : AbstractValidator<CreateQuoteRequest>
{
    public CreateQuoteRequestValidator()
    {
        RuleFor(x => x.Author)
            .NotEmpty().WithMessage("Author is required")
            .MaximumLength(256).WithMessage("Author must be at most 256 characters");

        RuleFor(x => x.Text)
            .NotEmpty().WithMessage("Text is required")
            .MaximumLength(2000).WithMessage("Text must be at most 2000 characters");
    }
}
