namespace QuotesApi.Services;

public interface IExternalQuoteService
{
    Task<ExternalQuoteDto?> GetRandomQuoteAsync(CancellationToken ct = default);
}

public record ExternalQuoteDto(string Text, string Author);
