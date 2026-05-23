using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;
using System.Text.Json.Serialization;
using FluentValidation;
using QuotesApi.Configuration;
using QuotesApi.Data;
using QuotesApi.Middleware;
using QuotesApi.Models;
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
            services.AddDbContext<QuoteDbContext>(options => options.UseSqlServer(connectionString));
        else
            services.AddDbContext<QuoteDbContext>(options => options.UseSqlite(connectionString));

        services.AddSingleton<IClock, SystemClock>();
        services.AddScoped<IQuoteFactory, QuoteFactory>();
        services.AddScoped<IQuoteRepository, QuoteRepository>();
        services.AddScoped<ICollectionRepository, CollectionRepository>(); // NEW
        services.AddScoped<IAuthTokenService, AuthTokenService>();
        services.AddValidatorsFromAssemblyContaining<CreateQuoteRequestValidator>();

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

            // Guard against re-seeding when the factory is reused across tests
            // or when a dev DB already contains the seed user.
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

        quotes.MapGet("/", GetQuotes).WithName("GetQuotes");
        quotes.MapPost("/", CreateQuote).WithName("CreateQuote").RequireAuthorization("can-edit-quotes");
        quotes.MapGet("/{id}", GetQuoteById).WithName("GetQuoteById");
        quotes.MapDelete("/{id}", DeleteQuote).WithName("DeleteQuote").RequireAuthorization("can-edit-quotes");

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

    // ── Quote handlers (unchanged) ────────────────────────────────────

    private static async Task<IResult> GetQuotes(
        IQuoteRepository repository,
        ILogger<Program> logger,
        int page = 1, int size = 10,
        CancellationToken cancellationToken = default)
    {
        if (page < 1 || size < 1)
            return Results.BadRequest(new ProblemDetails
            {
                Title = "Validation Failed",
                Status = 400,
                Detail = "Page and size must be greater than 0"
            });

        // INTENTIONAL SLOW OPERATION — Day 5 Piece 1 trace diagnosis exercise.
        // Simulates a blocking downstream call that inflates the span duration.
        Thread.Sleep(1500);

        logger.LogInformation("Fetching quotes page {Page} size {Size}", page, size);
        var result = await repository.GetQuotesAsync(page, size, cancellationToken);
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
}

// ── Request DTOs ──────────────────────────────────────────────────────

public record CreateCollectionRequest(string Name, int OwnerId);
public record AddCollectionItemRequest(int QuoteId);
public record LoginRequest(string Email, string Password);
public record RefreshRequest([property: JsonPropertyName("refresh_token")] string RefreshToken);

