namespace QuotesApi.Queries;

public class QuoteReadModel
{
    public int QuoteId { get; set; }
    public string QuoteText { get; set; } = string.Empty;
    public string AuthorName { get; set; } = string.Empty;
    public string CreatedAt { get; set; } = string.Empty;
}
