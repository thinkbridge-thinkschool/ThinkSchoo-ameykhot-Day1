using QuotesApi.Data;
using QuotesApi.Extensions;
using QuotesApi.Middleware;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddInfrastructure(builder.Configuration);
builder.Services.AddHealthChecks();
builder.Services.AddCors(o => o.AddDefaultPolicy(p =>
    p.WithOrigins(
        "http://localhost:4200",
        "https://lively-field-0238eb80f.7.azurestaticapps.net"
    )
    .AllowAnyHeader()
    .AllowAnyMethod()
    .AllowCredentials()));

var app = builder.Build();

app.UseExceptionMiddleware();
app.UseCors();

// Container Apps terminate TLS at the ingress — skip HTTPS redirect inside the container
if (app.Environment.IsDevelopment())
    app.UseHttpsRedirection();

app.ApplyMigrations();
app.MapHealthChecks("/health");
app.MapQuoteEndpoints();

app.Run();
