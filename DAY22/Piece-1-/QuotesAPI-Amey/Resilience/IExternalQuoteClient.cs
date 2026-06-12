namespace QuotesApi.Resilience;

public interface IExternalQuoteClient
{
    Task<ExternalQuoteResponse?> GetQuoteAsync(int id, CancellationToken ct = default);
}

public record ExternalQuoteResponse(int Id, string Author, string Text);
