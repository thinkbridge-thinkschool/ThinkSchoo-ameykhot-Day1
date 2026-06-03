using QuotesApi.Data;
using QuotesApi.Models;

namespace QuotesApi.Commands;

public class CreateQuoteHandler
{
    private readonly QuoteDbContext _context;

    public CreateQuoteHandler(QuoteDbContext context)
    {
        _context = context;
    }

    public async Task<int> Handle(CreateQuoteCommand command, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(command.Text))
            throw new ArgumentException("Quote text is required");

        if (string.IsNullOrWhiteSpace(command.Author))
            throw new ArgumentException("Author name is required");

        var quote = new Quote(command.Author, command.Text, DateTime.UtcNow);
        quote.AuthorId = command.AuthorId;

        _context.Quotes.Add(quote);
        await _context.SaveChangesAsync(ct);

        return quote.Id;
    }
}
