namespace QuotesApi.Models;

public class Quote
{
    public int Id { get; set; }
    public string Author { get; private set; } = string.Empty;
    public string Text { get; private set; } = string.Empty;
    public DateTime CreatedAt { get; private set; }

    // Required by EF Core
    private Quote() { }

    public Quote(string author, string text, DateTime createdAtUtc)
    {
        Author = author;
        Text = text;
        CreatedAt = DateTime.SpecifyKind(createdAtUtc, DateTimeKind.Utc);
    }
}
