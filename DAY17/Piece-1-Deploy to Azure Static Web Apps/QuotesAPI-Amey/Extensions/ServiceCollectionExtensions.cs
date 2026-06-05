using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;
using System.Text.Json.Serialization;
using FluentValidation;
using QuotesApi.Commands;
using QuotesApi.Configuration;
using QuotesApi.Dapper;
using QuotesApi.Data;
using QuotesApi.Middleware;
using QuotesApi.Models;
using QuotesApi.Queries;
using QuotesApi.Services;
using QuotesApi.Time;
using QuotesApi.Validators;

namespace QuotesApi.Extensions;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<JwtOptions>(configuration.GetSection("Jwt"));

        var connectionString = configuration.GetConnectionString("DefaultConnection")
            ?? throw new InvalidOperationException("DefaultConnection not found in configuration");

        var provider = configuration.GetValue<string>("DatabaseProvider") ?? "Sqlite";
        if (provider.Equals("SqlServer", StringComparison.OrdinalIgnoreCase))
            services.AddDbContext<QuoteDbContext>(options => options
                .UseSqlServer(connectionString)
                .LogTo(Console.WriteLine, Microsoft.Extensions.Logging.LogLevel.Information)
                .EnableSensitiveDataLogging());
        else
            services.AddDbContext<QuoteDbContext>(options => options
                .UseSqlite(connectionString)
                .LogTo(Console.WriteLine, Microsoft.Extensions.Logging.LogLevel.Information)
                .EnableSensitiveDataLogging());

        services.AddSingleton<IClock, SystemClock>();
        services.AddScoped<IQuoteFactory, QuoteFactory>();
        services.AddScoped<IQuoteRepository, QuoteRepository>();
        services.AddScoped<ICollectionRepository, CollectionRepository>(); // NEW
        services.AddScoped<IAuthTokenService, AuthTokenService>();
        services.AddValidatorsFromAssemblyContaining<CreateQuoteRequestValidator>();

        // CQRS-lite handlers
        services.AddScoped<CreateQuoteHandler>();
        services.AddScoped<GetQuotesByAuthorHandler>();

        // Dapper repository
        services.AddScoped<QuoteDapperRepository>();

        return services;
    }

    public static WebApplication ApplyMigrations(this WebApplication app)
    {
        using var scope = app.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<QuoteDbContext>();
        var logger = scope.ServiceProvider.GetRequiredService<ILogger<Program>>();

        try
        {
            logger.LogInformation("Applying database schema...");
            // EnsureCreated only creates the schema when the DB is brand new.
            // For in-memory SQLite (tests) it is always fresh; for file-based SQLite
            // (dev/prod) it is a no-op when tables already exist (returns false).
            dbContext.Database.EnsureCreated();

            if (!dbContext.Users.Any())
            {
                dbContext.Users.Add(new User
                {
                    Id = Guid.NewGuid(),
                    Email = "user@test.com",
                    PasswordHash = BCrypt.Net.BCrypt.HashPassword("password123")
                });
                dbContext.SaveChanges();
                logger.LogInformation("Seeded default user: user@test.com");
            }

            if (!dbContext.Authors.Any())
            {
                // 100 authors × 100 quotes = 10,000 rows — makes N+1 + table-scan vs JOIN + index difference dramatic under load
                // 100 authors × 100 quotes = 10,000 rows — amplifies N+1 + table-scan difference dramatically
                var authorNames = new[]
                {
                    "Marcus Aurelius", "Seneca", "Epictetus", "Aristotle", "Plato",
                    "Socrates", "Friedrich Nietzsche", "Immanuel Kant", "René Descartes", "John Locke",
                    "David Hume", "John Stuart Mill", "Bertrand Russell", "Ludwig Wittgenstein", "Martin Heidegger",
                    "Jean-Paul Sartre", "Simone de Beauvoir", "Albert Camus", "Friedrich Schiller", "Georg Hegel",
                    "Karl Marx", "Arthur Schopenhauer", "Søren Kierkegaard", "Francis Bacon", "Thomas Hobbes",
                    "Baruch Spinoza", "Gottfried Leibniz", "George Berkeley", "Jean-Jacques Rousseau", "Voltaire",
                    "Denis Diderot", "Blaise Pascal", "Michel de Montaigne", "Francis Hutcheson", "Thomas Reid",
                    "William James", "John Dewey", "Charles Peirce", "Josiah Royce", "George Santayana",
                    "Alfred North Whitehead", "Edmund Husserl", "Maurice Merleau-Ponty", "Jacques Derrida", "Michel Foucault",
                    "Hannah Arendt", "Isaiah Berlin", "John Rawls", "Robert Nozick", "Martha Nussbaum",
                    "Thales of Miletus", "Anaximander", "Heraclitus", "Parmenides", "Zeno of Elea",
                    "Democritus", "Pythagoras", "Protagoras", "Gorgias", "Thrasymachus",
                    "Xenophon", "Antisthenes", "Diogenes of Sinope", "Crates of Thebes", "Hipparchia",
                    "Pyrrho of Elis", "Timon of Phlius", "Arcesilaus", "Carneades", "Philo of Larissa",
                    "Epicurus", "Metrodorus", "Zeno of Citium", "Cleanthes", "Chrysippus",
                    "Posidonius", "Cicero", "Quintilian", "Plotinus", "Porphyry",
                    "Iamblichus", "Proclus", "Augustine of Hippo", "Boethius", "John Scotus Eriugena",
                    "Avicenna", "Averroes", "Maimonides", "Anselm of Canterbury", "Peter Abelard",
                    "Thomas Aquinas", "John Duns Scotus", "William of Ockham", "Meister Eckhart", "Nicholas of Cusa",
                    "Erasmus of Rotterdam", "Thomas More", "Niccolò Machiavelli", "Hugo Grotius", "Samuel Pufendorf"
                };

                var authors = authorNames.Select(n => new Author { Name = n }).ToList();
                dbContext.Authors.AddRange(authors);
                dbContext.SaveChanges();

                var quoteTemplates = new[]
                {
                    "The obstacle is the way.",
                    "We suffer more in imagination than in reality.",
                    "Make the best use of what is in your power.",
                    "Knowing yourself is the beginning of all wisdom.",
                    "At the touch of love everyone becomes a poet.",
                    "The unexamined life is not worth living.",
                    "Without music, life would be a mistake.",
                    "Act only according to that maxim you could will to be a universal law.",
                    "I think, therefore I am.",
                    "The end of law is not to abolish or restrain, but to preserve and enlarge freedom.",
                    "Happiness is the highest good.",
                    "He who has a why to live can bear almost any how.",
                    "One cannot step twice into the same river.",
                    "The only true wisdom is in knowing you know nothing.",
                    "Man is condemned to be free.",
                    "Hell is other people.",
                    "God is dead. God remains dead. And we have killed him.",
                    "To live is to suffer, to survive is to find some meaning in the suffering.",
                    "The function of prayer is not to influence God, but rather to change the nature of the one who prays.",
                    "Life can only be understood backwards; but it must be lived forwards.",
                    "The limits of my language mean the limits of my world.",
                    "Whereof one cannot speak, thereof one must be silent.",
                    "A thinker sees his own actions as experiments.",
                    "The secret for harvesting from existence the greatest fruitfulness is to live dangerously.",
                    "There is always some madness in love.",
                    "We are what we repeatedly do. Excellence, then, is not an act, but a habit.",
                    "It is the mark of an educated mind to entertain a thought without accepting it.",
                    "The more I know, the more I realize I do not know.",
                    "In the middle of difficulty lies opportunity.",
                    "Imagination is more important than knowledge.",
                    "Two things are infinite: the universe and human stupidity.",
                    "Life is not a problem to be solved but a reality to be experienced.",
                    "The greatest glory in living lies not in never falling, but in rising every time we fall.",
                    "In order to be irreplaceable one must always be different.",
                    "It does not matter how slowly you go as long as you do not stop.",
                    "Our greatest weakness lies in giving up.",
                    "Before God we are all equally wise and equally foolish.",
                    "Strive not to be a success, but rather to be of value.",
                    "The world as we have created it is a process of our thinking.",
                    "Do not dwell in the past, do not dream of the future, concentrate the mind on the present moment.",
                    "When I let go of what I am, I become what I might be.",
                    "Life is what happens when you're busy making other plans.",
                    "You only live once, but if you do it right, once is enough.",
                    "To be yourself in a world that is constantly trying to make you something else is the greatest accomplishment.",
                    "In three words I can sum up everything I've learned about life: it goes on.",
                    "The future belongs to those who believe in the beauty of their dreams.",
                    "Tell me and I forget. Teach me and I remember. Involve me and I learn.",
                    "Whether you think you can or think you can't, you're right.",
                    "The best time to plant a tree was 20 years ago. The second best time is now.",
                    "An unexamined life is not worth living."
                };

                var quotes = new List<Quote>();
                // Interleave authors so page 1 shows 10 different authors (not 10 quotes from one author)
                for (int j = 0; j < 100; j++)
                {
                    for (int i = 0; i < authors.Count; i++)
                    {
                        var template = quoteTemplates[(i * 7 + j) % quoteTemplates.Length];
                        var q = new Quote(authors[i].Name, $"{template} (v{j + 1})", DateTime.UtcNow);
                        q.AuthorId = authors[i].Id;
                        quotes.Add(q);
                    }
                }
                dbContext.Quotes.AddRange(quotes);
                dbContext.SaveChanges();
                logger.LogInformation("Seeded {AuthorCount} authors with {QuoteCount} quotes", authors.Count, quotes.Count);
            }

            logger.LogInformation("Migrations applied successfully");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error applying migrations");
            throw;
        }

        return app;
    }
}

public static class EndpointExtensions
{
    public static WebApplication MapQuoteEndpoints(this WebApplication app)
    {
        var auth = app.MapGroup("/api/auth").WithName("Auth");
        auth.MapPost("/login", Login).WithName("Login");
        auth.MapPost("/refresh", Refresh).WithName("Refresh");
        auth.MapPost("/logout", Logout).WithName("Logout");

        // ── Existing Quote endpoints ──────────────────────────────────
        var quotes = app.MapGroup("/api/quotes").WithName("Quotes");

        quotes.MapGet("/slow", GetSlowQuotes).WithName("GetSlowQuotes");
        quotes.MapGet("/fast", GetFastQuotes).WithName("GetFastQuotes");
        quotes.MapGet("/", GetQuotes).WithName("GetQuotes");
        quotes.MapPost("/", CreateQuote).WithName("CreateQuote").RequireAuthorization("can-edit-quotes");
        quotes.MapGet("/{id}", GetQuoteById).WithName("GetQuoteById");
        quotes.MapDelete("/{id}", DeleteQuote).WithName("DeleteQuote").RequireAuthorization("can-edit-quotes");

        // ── CQRS-lite endpoints (Day 12) ─────────────────────────────
        var cqrs = app.MapGroup("/api/cqrs/quotes").WithName("CqrsQuotes");
        cqrs.MapPost("/", CqrsCreateQuote).WithName("CqrsCreateQuote");
        cqrs.MapGet("/by-author/{authorId}", CqrsGetByAuthor).WithName("CqrsGetByAuthor");

        // ── EF vs Dapper comparison endpoints (Day 12 Piece 2) ───────
        cqrs.MapGet("/ef/by-author/{authorId}", CqrsGetByAuthorEF).WithName("CqrsGetByAuthorEF");
        cqrs.MapGet("/dapper/by-author/{authorId}", CqrsGetByAuthorDapper).WithName("CqrsGetByAuthorDapper");

        // ── New Collection endpoints ──────────────────────────────────
        var collections = app.MapGroup("/api/collections").WithName("Collections");

        collections.MapPost("/", CreateCollection).WithName("CreateCollection").RequireAuthorization("can-edit-quotes");
        collections.MapGet("/{id}", GetCollectionById).WithName("GetCollectionById");
        collections.MapPost("/{id}/items", AddItemToCollection).WithName("AddItemToCollection").RequireAuthorization("can-edit-quotes");
        collections.MapDelete("/{id}/items/{quoteId}", RemoveItemFromCollection).WithName("RemoveItemFromCollection").RequireAuthorization("can-edit-quotes");
        collections.MapDelete("/{id}", DeleteCollection).WithName("DeleteCollection").RequireAuthorization("can-edit-quotes");

        return app;
    }

    private static async Task<IResult> Login(
        LoginRequest request,
        QuoteDbContext dbContext,
        IAuthTokenService authTokenService,
        ILogger<Program> logger,
        CancellationToken cancellationToken = default)
    {
        logger.LogInformation("Login attempt for user {Email}", request.Email);

        var user = await dbContext.Users
            .FirstOrDefaultAsync(u => u.Email == request.Email, cancellationToken);

        if (user is null || !BCrypt.Net.BCrypt.Verify(request.Password, user.PasswordHash))
        {
            logger.LogWarning("Login failed for user {Email} — invalid credentials", request.Email);
            return Results.Unauthorized();
        }

        var pair = await authTokenService.IssueTokenPairAsync(user, cancellationToken: cancellationToken);

        logger.LogInformation("Login succeeded for user {UserId} ({Email})", user.Id, user.Email);

        return Results.Ok(new
        {
            access_token = pair.AccessToken,
            refresh_token = pair.RefreshToken,
            expires_in = pair.ExpiresIn
        });
    }

    private static async Task<IResult> Refresh(
        RefreshRequest request,
        IAuthTokenService authTokenService,
        CancellationToken cancellationToken = default)
    {
        var outcome = await authTokenService.RefreshAsync(request.RefreshToken, cancellationToken);

        if (!outcome.IsSuccess)
        {
            var detail = outcome.FailureReason switch
            {
                RefreshFailureReason.InvalidToken => "Invalid refresh token.",
                RefreshFailureReason.ExpiredToken => "Refresh token expired.",
                RefreshFailureReason.ReuseDetected => "Token reuse detected. Please log in again.",
                _ => "Refresh token revoked. Please log in again."
            };

            return Results.Json(
                new ProblemDetails { Title = "Unauthorized", Status = 401, Detail = detail },
                statusCode: 401);
        }

        return Results.Ok(new
        {
            access_token = outcome.Tokens!.AccessToken,
            refresh_token = outcome.Tokens.RefreshToken,
            expires_in = outcome.Tokens.ExpiresIn
        });
    }

    private static async Task<IResult> Logout(
        RefreshRequest request,
        IAuthTokenService authTokenService,
        CancellationToken cancellationToken = default)
    {
        await authTokenService.RevokeAsync(request.RefreshToken, cancellationToken);
        return Results.NoContent();
    }

    // ── Fast endpoint: split-query with index seek, no change-tracking ──

    private static async Task<IResult> GetFastQuotes(QuoteDbContext db)
    {
        // Fix 1: AsSplitQuery fires 2 queries (authors + quotes joined separately)
        //        instead of 51 queries (N+1 per author).
        // Fix 2: AsNoTracking removes EF change-tracking overhead on read-only data.
        // Fix 3: Server-side projection reduces payload — only Id and Text, not full Quote.
        // Fix 4: IX_Quotes_AuthorId index lets the DB engine seek instead of scan.
        var result = await db.Authors
            .AsNoTracking()
            .AsSplitQuery()
            .Include(a => a.Quotes)
            .Select(a => new
            {
                a.Name,
                Quotes = a.Quotes.Select(q => new { q.Id, q.Text })
            })
            .ToListAsync();

        return Results.Ok(result);
    }

    // ── Slow endpoint: N+1 query pattern, full entity load ───────────

    private static async Task<IResult> GetSlowQuotes(QuoteDbContext db)
    {
        // Problem 1: loads ALL 100 authors into memory first (1 query)
        var authors = await db.Authors.ToListAsync();
        var result = new List<object>();

        foreach (var author in authors)
        {
            // Problem 2: fires one SQL query PER author (100 more queries = 101 total)
            // With 100 authors × 100 quotes = 10,000 rows, each request touches 101 queries.
            // Without an index each query scans the full 10,000-row table.
            // Under concurrent load these queries queue up and p99 skyrockets.
            var quotes = await db.Quotes
                .Where(q => q.AuthorId == author.Id)
                .ToListAsync();

            result.Add(new { author.Name, quotes });
        }

        return Results.Ok(result);
    }

    // ── Quote handlers ────────────────────────────────────────────────

    private static async Task<IResult> GetQuotes(
        IQuoteRepository repository,
        ILogger<Program> logger,
        int page = 1, int size = 10, string? search = null,
        CancellationToken cancellationToken = default)
    {
        if (page < 1 || size < 1)
            return Results.BadRequest(new ProblemDetails
            {
                Title = "Validation Failed",
                Status = 400,
                Detail = "Page and size must be greater than 0"
            });

        logger.LogInformation("Fetching quotes page {Page} size {Size} search {Search}", page, size, search);
        var result = await repository.GetQuotesAsync(page, size, search, cancellationToken);
        logger.LogInformation("Returned {Count} of {Total} quotes", result.Items.Count(), result.Total);
        return Results.Ok(new { data = result.Items, pagination = new { result.Page, result.Size, result.Total } });
    }

    private static async Task<IResult> CreateQuote(
        CreateQuoteRequest request,
        IQuoteRepository repository,
        IQuoteFactory quoteFactory,
        IClock clock,
        HttpContext httpContext,
        ILogger<Program> logger,
        IValidator<CreateQuoteRequest> validator,
        CancellationToken cancellationToken = default)
    {
        logger.LogInformation("Received CreateQuote request for author {Author}", request.Author);

        var validation = await validator.ValidateAsync(request, cancellationToken);
        if (!validation.IsValid)
        {
            logger.LogWarning("Validation failed for CreateQuote: {ErrorCount} errors", validation.Errors.Count);
            return Results.UnprocessableEntity(new ValidationProblemDetails
            {
                Title = "One or more validation errors occurred.",
                Status = 422,
                Errors = validation.Errors
                    .GroupBy(e => e.PropertyName)
                    .ToDictionary(g => g.Key, g => g.Select(e => e.ErrorMessage).ToArray())
            });
        }

        logger.LogInformation("Validation passed for author {Author} — building quote entity", request.Author);
        var quote = quoteFactory.Create(request.Author, request.Text, clock.UtcNow.UtcDateTime);

        var userIdStr = httpContext.User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (Guid.TryParse(userIdStr, out var ownerId))
        {
            quote.OwnerId = ownerId;
            logger.LogInformation("Assigned OwnerId {OwnerId} to new quote", ownerId);
        }

        var created = await repository.CreateQuoteAsync(quote, cancellationToken);
        logger.LogInformation("Created quote {QuoteId} by author {Author} for user {UserId}", created.Id, created.Author, userIdStr);
        return Results.Created($"/api/quotes/{created.Id}", created);
    }

    private static async Task<IResult> GetQuoteById(
        int id, IQuoteRepository repository,
        ILogger<Program> logger, CancellationToken cancellationToken = default)
    {
        var quote = await repository.GetQuoteByIdAsync(id, cancellationToken);
        return quote is null
            ? Results.NotFound(new ProblemDetails { Title = "Not Found", Status = 404, Detail = $"Quote with ID {id} not found" })
            : Results.Ok(quote);
    }

    private static async Task<IResult> DeleteQuote(
        int id,
        IQuoteRepository repository,
        IAuthorizationService authorizationService,
        HttpContext httpContext,
        ILogger<Program> logger,
        CancellationToken cancellationToken = default)
    {
        var quote = await repository.GetQuoteByIdAsync(id, cancellationToken);
        if (quote is null)
        {
            logger.LogWarning("Delete failed — quote {QuoteId} not found", id);
            return Results.NotFound(new ProblemDetails { Title = "Not Found", Status = 404, Detail = $"Quote with ID {id} not found" });
        }

        // Resource-based check: user must own this specific quote
        var authResult = await authorizationService.AuthorizeAsync(httpContext.User, quote, "quote-owner");
        if (!authResult.Succeeded)
        {
            logger.LogWarning("Delete forbidden — user {UserId} does not own quote {QuoteId}",
                httpContext.User.FindFirstValue(ClaimTypes.NameIdentifier), id);
            return Results.Forbid();
        }

        await repository.DeleteQuoteAsync(id, cancellationToken);
        logger.LogInformation("Deleted quote {QuoteId} by user {UserId}", id,
            httpContext.User.FindFirstValue(ClaimTypes.NameIdentifier));
        return Results.NoContent();
    }

    // ── Collection handlers (NEW) ─────────────────────────────────────

    private static async Task<IResult> CreateCollection(
        CreateCollectionRequest request,
        ICollectionRepository repo,
        ILogger<Program> logger,
        CancellationToken cancellationToken = default)
    {
        // DomainException thrown here if name invalid → caught by ExceptionMiddleware → 400
        var collection = new Collection(request.Name, request.OwnerId);
        await repo.AddAsync(collection, cancellationToken);
        logger.LogInformation("Created collection {CollectionId} named {Name} for owner {OwnerId}",
            collection.Id, collection.Name, collection.OwnerId);
        return Results.Created($"/api/collections/{collection.Id}", collection);
    }

    private static async Task<IResult> GetCollectionById(
        int id, ICollectionRepository repo,
        CancellationToken cancellationToken = default)
    {
        var collection = await repo.GetByIdAsync(id, cancellationToken);
        return collection is null
            ? Results.NotFound(new ProblemDetails { Title = "Not Found", Status = 404, Detail = $"Collection {id} not found" })
            : Results.Ok(collection);
    }

    private static async Task<IResult> AddItemToCollection(
        int id,
        AddCollectionItemRequest request,
        IClock clock,
        ICollectionRepository repo,
        CancellationToken cancellationToken = default)
    {
        var collection = await repo.GetByIdAsync(id, cancellationToken);
        if (collection is null)
            return Results.NotFound(new ProblemDetails { Title = "Not Found", Status = 404, Detail = $"Collection {id} not found" });

        // ALL invariants enforced HERE inside the aggregate — not in this handler
        collection.AddItem(request.QuoteId, clock.UtcNow.UtcDateTime);

        await repo.UpdateAsync(collection, cancellationToken);
        return Results.Ok(collection);
    }

    private static async Task<IResult> RemoveItemFromCollection(
        int id, int quoteId,
        ICollectionRepository repo,
        CancellationToken cancellationToken = default)
    {
        var collection = await repo.GetByIdAsync(id, cancellationToken);
        if (collection is null)
            return Results.NotFound(new ProblemDetails { Title = "Not Found", Status = 404, Detail = $"Collection {id} not found" });

        collection.RemoveItem(quoteId);
        await repo.UpdateAsync(collection, cancellationToken);
        return Results.Ok(collection);
    }

    private static async Task<IResult> DeleteCollection(
        int id, ICollectionRepository repo,
        CancellationToken cancellationToken = default)
    {
        await repo.DeleteAsync(id, cancellationToken);
        return Results.NoContent();
    }

    // ── CQRS-lite handlers (Day 12) ───────────────────────────────────

    private static async Task<IResult> CqrsCreateQuote(
        CreateQuoteCommand command,
        CreateQuoteHandler handler,
        CancellationToken cancellationToken = default)
    {
        var id = await handler.Handle(command, cancellationToken);
        return Results.Created($"/api/cqrs/quotes/{id}", new { id });
    }

    private static async Task<IResult> CqrsGetByAuthor(
        int authorId,
        GetQuotesByAuthorHandler handler,
        CancellationToken cancellationToken = default)
    {
        var query = new GetQuotesByAuthorQuery { AuthorId = authorId };
        var result = await handler.Handle(query, cancellationToken);
        return Results.Ok(result);
    }

    // EF endpoint — timing printed to console via Stopwatch inside the handler
    private static async Task<IResult> CqrsGetByAuthorEF(
        int authorId,
        GetQuotesByAuthorHandler handler,
        CancellationToken cancellationToken = default)
    {
        var query = new GetQuotesByAuthorQuery { AuthorId = authorId };
        var result = await handler.Handle(query, cancellationToken);
        return Results.Ok(result);
    }

    // Dapper endpoint — timing printed to console via Stopwatch inside the repo
    private static async Task<IResult> CqrsGetByAuthorDapper(
        int authorId,
        QuoteDapperRepository dapperRepo)
    {
        var result = await dapperRepo.GetByAuthor(authorId);
        return Results.Ok(result);
    }
}

// ── Request DTOs ──────────────────────────────────────────────────────

public record CreateCollectionRequest(string Name, int OwnerId);
public record AddCollectionItemRequest(int QuoteId);
public record LoginRequest(string Email, string Password);
public record RefreshRequest([property: JsonPropertyName("refresh_token")] string RefreshToken);

