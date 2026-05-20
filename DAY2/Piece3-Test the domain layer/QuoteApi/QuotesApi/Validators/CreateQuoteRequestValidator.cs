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
            .MaximumLength(200).WithMessage("Author must be at most 200 characters");

        RuleFor(x => x.Text)
            .NotEmpty().WithMessage("Text is required")
            .MaximumLength(1000).WithMessage("Text must be at most 1000 characters");
    }
}
