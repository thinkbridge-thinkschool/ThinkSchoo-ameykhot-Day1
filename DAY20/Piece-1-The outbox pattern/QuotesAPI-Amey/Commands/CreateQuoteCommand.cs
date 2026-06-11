namespace QuotesApi.Commands;

public class CreateQuoteCommand
{
    public string Author { get; set; } = string.Empty;
    public string Text { get; set; } = string.Empty;
    public int? AuthorId { get; set; }
}
