namespace QuotesApi.Models;

public class Quote
{
    public int Id { get; private set; }
    public string Author { get; private set; } = string.Empty;
    public string Text { get; private set; } = string.Empty;
    public DateTime CreatedAt { get; private set; }
    public bool IsDeleted { get; private set; }

    // Required by EF Core
    private Quote() { }

    private Quote(string author, string text)
    {
        Author = author;
        Text = text;
        CreatedAt = DateTime.UtcNow;
        IsDeleted = false;
    }

    public static (Quote? Quote, string? Error) Create(string author, string text)
    {
        var normalizedAuthor = author?.Trim() ?? string.Empty;
        var normalizedText = text?.Trim() ?? string.Empty;

        if (normalizedAuthor.Length is < 1 or > 200)
            return (null, "Author must be between 1 and 200 characters.");

        if (normalizedText.Length is < 1 or > 1000)
            return (null, "Text must be between 1 and 1000 characters.");

        return (new Quote(normalizedAuthor, normalizedText), null);
    }

    public void SoftDelete()
    {
        IsDeleted = true;
    }
}
