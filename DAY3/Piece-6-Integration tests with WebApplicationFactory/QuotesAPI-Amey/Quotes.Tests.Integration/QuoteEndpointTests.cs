using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using FluentAssertions;
using Xunit;

namespace Quotes.Tests.Integration;

// Each test class instance owns its factory → each test gets a fresh database.
public sealed class QuoteEndpointTests : IDisposable
{
    private readonly IntegrationTestFactory _factory = new();

    public void Dispose() => _factory.Dispose();

    // ── Response DTOs ────────────────────────────────────────────────────

    private sealed record QuoteDto(
        [property: JsonPropertyName("id")]     int    Id,
        [property: JsonPropertyName("author")] string Author,
        [property: JsonPropertyName("text")]   string Text);

    private sealed record PaginationDto(
        [property: JsonPropertyName("page")]  int Page,
        [property: JsonPropertyName("size")]  int Size,
        [property: JsonPropertyName("total")] int Total);

    private sealed record PaginatedDto(
        [property: JsonPropertyName("data")]       IReadOnlyList<QuoteDto> Data,
        [property: JsonPropertyName("pagination")] PaginationDto           Pagination);

    // ── 1. GET /api/quotes (public) → 200 with pagination envelope ───────

    [Fact]
    public async Task GetQuotes_ReturnsOk_WithPaginationShape()
    {
        using var client = _factory.CreateAnonymousClient();

        var response = await client.GetAsync("/api/quotes");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<PaginatedDto>();
        body.Should().NotBeNull();
        body!.Data.Should().NotBeNull();
        body.Pagination.Should().NotBeNull();
        body.Pagination.Page.Should().Be(1);
    }

    // ── 2. GET /api/quotes?page=0 → 400 with ProblemDetails ─────────────

    [Fact]
    public async Task GetQuotes_InvalidPage_ReturnsBadRequest()
    {
        using var client = _factory.CreateAnonymousClient();

        var response = await client.GetAsync("/api/quotes?page=0");

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("Validation Failed");
    }

    // ── 3. GET /api/quotes/{id} for unknown ID → 404 ────────────────────

    [Fact]
    public async Task GetQuoteById_UnknownId_ReturnsNotFound()
    {
        using var client = _factory.CreateAnonymousClient();

        var response = await client.GetAsync("/api/quotes/99999");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    // ── 4. Create then GET — also verifies EF migrations were applied ────

    [Fact]
    public async Task GetQuoteById_AfterCreate_ReturnsMatchingQuote()
    {
        // Arrange + Act: create
        using var authClient = _factory.CreateAuthenticatedClient();
        var createResp = await authClient.PostAsJsonAsync("/api/quotes",
            new { author = "Marie Curie", text = "Nothing in life is to be feared, only to be understood." });
        createResp.StatusCode.Should().Be(HttpStatusCode.Created);
        var created = await createResp.Content.ReadFromJsonAsync<QuoteDto>();
        created.Should().NotBeNull();

        // Act: read back (uses anonymous client — public endpoint)
        using var anonClient = _factory.CreateAnonymousClient();
        var getResp = await anonClient.GetAsync($"/api/quotes/{created!.Id}");

        // Assert
        getResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var quote = await getResp.Content.ReadFromJsonAsync<QuoteDto>();
        quote!.Author.Should().Be("Marie Curie");
        quote.Text.Should().Be("Nothing in life is to be feared, only to be understood.");
    }

    // ── 5. POST /api/quotes without token → 401 ─────────────────────────

    [Fact]
    public async Task CreateQuote_Anonymous_ReturnsUnauthorized()
    {
        using var client = _factory.CreateAnonymousClient();

        var response = await client.PostAsJsonAsync("/api/quotes",
            new { author = "Author", text = "Some quote text here" });

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    // ── 6. POST /api/quotes — valid auth but no scope claim → 403 ────────

    [Fact]
    public async Task CreateQuote_WithoutScopeClaim_ReturnsForbidden()
    {
        using var client = _factory.CreateAuthenticatedClient(withScope: false);

        var response = await client.PostAsJsonAsync("/api/quotes",
            new { author = "Author", text = "Some quote text here" });

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    // ── 7. POST /api/quotes — valid auth + body → 201 with quote body ────

    [Fact]
    public async Task CreateQuote_ValidRequest_ReturnsCreatedWithQuote()
    {
        using var client = _factory.CreateAuthenticatedClient();

        var response = await client.PostAsJsonAsync("/api/quotes",
            new { author = "Ada Lovelace", text = "The Analytical Engine weaves algebraic patterns." });

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var body = await response.Content.ReadFromJsonAsync<QuoteDto>();
        body.Should().NotBeNull();
        body!.Author.Should().Be("Ada Lovelace");
        body.Id.Should().BeGreaterThan(0);
    }

    // ── 8. POST /api/quotes — empty author → 422 with ProblemDetails ─────

    [Fact]
    public async Task CreateQuote_EmptyAuthor_Returns422WithValidationErrors()
    {
        using var client = _factory.CreateAuthenticatedClient();

        var response = await client.PostAsJsonAsync("/api/quotes",
            new { author = "", text = "Some valid quote text here" });

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        // ValidationProblemDetails serialises as { "errors": { "Author": [...] } }
        body.GetProperty("errors").TryGetProperty("Author", out _).Should().BeTrue();
    }

    // ── 9. DELETE /api/quotes/{id} without token → 401 ──────────────────

    [Fact]
    public async Task DeleteQuote_Anonymous_ReturnsUnauthorized()
    {
        using var client = _factory.CreateAnonymousClient();

        var response = await client.DeleteAsync("/api/quotes/1");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    // ── 10. DELETE /api/quotes/{id} — authenticated but NOT the owner → 403

    [Fact]
    public async Task DeleteQuote_NotOwner_ReturnsForbidden()
    {
        var ownerAId = Guid.NewGuid();
        var ownerBId = Guid.NewGuid();

        // Create quote as owner A
        using var creatorClient = _factory.CreateAuthenticatedClient(userId: ownerAId);
        var createResp = await creatorClient.PostAsJsonAsync("/api/quotes",
            new { author = "Owner A", text = "This quote belongs exclusively to owner A." });
        createResp.EnsureSuccessStatusCode();
        var created = await createResp.Content.ReadFromJsonAsync<QuoteDto>();

        // Attempt delete as owner B (different user, same scope)
        using var attackerClient = _factory.CreateAuthenticatedClient(userId: ownerBId);
        var deleteResp = await attackerClient.DeleteAsync($"/api/quotes/{created!.Id}");

        deleteResp.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    // ── 11. DELETE /api/quotes/{id} — owner deletes own quote → 204 ──────

    [Fact]
    public async Task DeleteQuote_Owner_ReturnsNoContent()
    {
        var ownerId = Guid.NewGuid();

        using var client = _factory.CreateAuthenticatedClient(userId: ownerId);
        var createResp = await client.PostAsJsonAsync("/api/quotes",
            new { author = "To Be Deleted", text = "This quote will be removed by its owner." });
        createResp.EnsureSuccessStatusCode();
        var created = await createResp.Content.ReadFromJsonAsync<QuoteDto>();

        var deleteResp = await client.DeleteAsync($"/api/quotes/{created!.Id}");

        deleteResp.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }
}
