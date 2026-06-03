namespace QuotesApi.Models;

public class Author
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public ICollection<Quote> Quotes { get; set; } = new List<Quote>();
}
