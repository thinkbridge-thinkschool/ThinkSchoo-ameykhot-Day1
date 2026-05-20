using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Http;
using FluentValidation;
using QuotesApi.Data;
using QuotesApi.Middleware;
using QuotesApi.Models;
using QuotesApi.Validators;

namespace QuotesApi.Extensions;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("DefaultConnection")
            ?? throw new InvalidOperationException("DefaultConnection not found in configuration");

        services.AddDbContext<QuoteDbContext>(options =>
            options.UseSqlite(connectionString));

        services.AddScoped<IQuoteRepository, QuoteRepository>();
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
            logger.LogInformation("Applying EF Core migrations...");
            dbContext.Database.Migrate();
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
        var group = app.MapGroup("/api/quotes")
            .WithName("Quotes");

        group.MapGet("/", GetQuotes)
            .WithName("GetQuotes")
            .WithSummary("Get paginated quotes")
            .Produces<dynamic>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status400BadRequest);

        group.MapPost("/", CreateQuote)
            .WithName("CreateQuote")
            .WithSummary("Create a new quote")
            .Accepts<CreateQuoteRequest>("application/json")
            .Produces<Quote>(StatusCodes.Status201Created)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status422UnprocessableEntity);

        group.MapGet("/{id}", GetQuoteById)
            .WithName("GetQuoteById")
            .WithSummary("Get a quote by ID")
            .Produces<Quote>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status404NotFound);

        group.MapDelete("/{id}", DeleteQuote)
            .WithName("DeleteQuote")
            .WithSummary("Delete a quote by ID")
            .Produces(StatusCodes.Status204NoContent)
            .Produces(StatusCodes.Status404NotFound);

        return app;
    }

    private static async Task<IResult> GetQuotes(
        IQuoteRepository repository,
        ILogger<Program> logger,
        int page = 1,
        int size = 10,
        CancellationToken cancellationToken = default)
    {
        if (page < 1 || size < 1)
        {
            logger.LogWarning("Invalid pagination parameters: page={Page}, size={Size}", page, size);
            return Results.BadRequest(new ProblemDetails
            {
                Title = "Validation Failed",
                Status = StatusCodes.Status400BadRequest,
                Detail = "Page and size must be greater than 0"
            });
        }

        var result = await repository.GetQuotesAsync(page, size, cancellationToken);
        logger.LogInformation("Retrieved {Count} quotes from page {Page}", result.Items.Count, page);
        
        return Results.Ok(new
        {
            data = result.Items,
            pagination = new { result.Page, result.Size, result.Total }
        });
    }

    private static async Task<IResult> CreateQuote(
        CreateQuoteRequest request,
        IQuoteRepository repository,
        ILogger<Program> logger,
        IValidator<CreateQuoteRequest> validator,
        CancellationToken cancellationToken = default)
    {
        var validation = await validator.ValidateAsync(request, cancellationToken);
        if (!validation.IsValid)
        {
            logger.LogWarning("Validation failed for create quote request");
            return Results.UnprocessableEntity(new ValidationProblemDetails
            {
                Title = "One or more validation errors occurred.",
                Status = StatusCodes.Status422UnprocessableEntity,
                Errors = validation.Errors
                    .GroupBy(e => e.PropertyName)
                    .ToDictionary(
                        g => g.Key,
                        g => g.Select(e => e.ErrorMessage).ToArray()
                    )
            });
        }

        var quote = new Quote { Author = request.Author, Text = request.Text };
        var created = await repository.CreateQuoteAsync(quote, cancellationToken);
        
        logger.LogInformation("Quote created with ID={QuoteId}", created.Id);
        return Results.Created($"/api/quotes/{created.Id}", created);
    }

    private static async Task<IResult> GetQuoteById(
        int id,
        IQuoteRepository repository,
        ILogger<Program> logger,
        CancellationToken cancellationToken = default)
    {
        var quote = await repository.GetQuoteByIdAsync(id, cancellationToken);
        if (quote is null)
        {
            logger.LogWarning("Quote not found: id={QuoteId}", id);
            return Results.NotFound(new ProblemDetails
            {
                Title = "Not Found",
                Status = StatusCodes.Status404NotFound,
                Detail = $"Quote with ID {id} not found"
            });
        }

        return Results.Ok(quote);
    }

    private static async Task<IResult> DeleteQuote(
        int id,
        IQuoteRepository repository,
        ILogger<Program> logger,
        CancellationToken cancellationToken = default)
    {
        var deleted = await repository.DeleteQuoteAsync(id, cancellationToken);
        if (!deleted)
        {
            logger.LogWarning("Failed to delete quote: id={QuoteId} not found", id);
            return Results.NotFound(new ProblemDetails
            {
                Title = "Not Found",
                Status = StatusCodes.Status404NotFound,
                Detail = $"Quote with ID {id} not found"
            });
        }

        logger.LogInformation("Quote deleted: id={QuoteId}", id);
        return Results.NoContent();
    }
}
