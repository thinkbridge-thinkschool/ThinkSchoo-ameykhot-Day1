using System.Net.Http.Json;

namespace QuotesApi.Resilience;

public class ExternalQuoteClient : IExternalQuoteClient
{
    private readonly HttpClient _http;
    private readonly ILogger<ExternalQuoteClient> _logger;

    public ExternalQuoteClient(HttpClient http, ILogger<ExternalQuoteClient> logger)
    {
        _http = http;
        _logger = logger;
    }

    public async Task<ExternalQuoteResponse?> GetQuoteAsync(int id, CancellationToken ct = default)
    {
        _logger.LogInformation("[Client] Calling external service for quote {Id}", id);
        var response = await _http.GetAsync($"/api/quotes/unstable/{id}", ct);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<ExternalQuoteResponse>(
            cancellationToken: ct);
    }
}
