using QuotesApi.Data;
using QuotesApi.Extensions;
using QuotesApi.Middleware;

var builder = WebApplication.CreateBuilder(args);

// Add services
builder.Services.AddInfrastructure(builder.Configuration);

var app = builder.Build();

// Middleware
app.UseExceptionMiddleware();
app.UseHttpsRedirection();

// Apply migrations
app.ApplyMigrations();

// Map endpoints
app.MapQuoteEndpoints();

app.Run();
