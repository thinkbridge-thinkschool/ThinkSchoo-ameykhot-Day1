namespace QuotesApi.Models;

public class QuoteCreatedEvent
{
    public int QuoteId { get; set; }
    public string Author { get; set; } = string.Empty;
    public string Text { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
}
