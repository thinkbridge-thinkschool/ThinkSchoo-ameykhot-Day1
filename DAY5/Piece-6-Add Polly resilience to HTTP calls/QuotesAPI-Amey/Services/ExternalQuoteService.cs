using System.Net.Http.Json;
using System.Text.Json.Serialization;

namespace QuotesApi.Services;

public sealed class ExternalQuoteService(
    HttpClient client,
    ILogger<ExternalQuoteService> logger) : IExternalQuoteService
{
    public async Task<ExternalQuoteDto?> GetRandomQuoteAsync(CancellationToken ct = default)
    {
        logger.LogInformation("Fetching random quote from external provider at {BaseAddress}", client.BaseAddress);

        using var response = await client.GetAsync("/api/random", ct);
        response.EnsureSuccessStatusCode();

        var items = await response.Content.ReadFromJsonAsync<ZenQuoteItem[]>(ct);
        var item = items?.FirstOrDefault();

        if (item is null)
        {
            logger.LogWarning("External quote provider returned empty response");
            return null;
        }

        logger.LogInformation("Received quote by {Author} from external provider", item.A);
        return new ExternalQuoteDto(item.Q, item.A);
    }

    private sealed record ZenQuoteItem(
        [property: JsonPropertyName("q")] string Q,
        [property: JsonPropertyName("a")] string A);
}
