using System.Diagnostics;
using Microsoft.AspNetCore.Mvc;

namespace QuotesApi.Middleware;

public class ExceptionMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<ExceptionMiddleware> _logger;

    public ExceptionMiddleware(RequestDelegate next, ILogger<ExceptionMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await _next(context);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unhandled exception occurred: {ExceptionMessage}", ex.Message);
            await HandleExceptionAsync(context, ex);
        }
    }

    private static Task HandleExceptionAsync(HttpContext context, Exception exception)
    {
        context.Response.ContentType = "application/problem+json";

        var response = new ProblemDetails
        {
            Title = "An error occurred while processing your request.",
            Status = StatusCodes.Status500InternalServerError,
            Type = "https://tools.ietf.org/html/rfc7231#section-6.6.1",
            Instance = context.Request.Path
        };

        if (exception is ArgumentException argEx)
        {
            context.Response.StatusCode = StatusCodes.Status400BadRequest;
            response.Status = StatusCodes.Status400BadRequest;
            response.Detail = argEx.Message;
        }

        context.Response.StatusCode = response.Status ?? StatusCodes.Status500InternalServerError;
        return context.Response.WriteAsJsonAsync(response);
    }
}

public static class ExceptionMiddlewareExtensions
{
    public static IApplicationBuilder UseExceptionMiddleware(this IApplicationBuilder builder)
    {
        return builder.UseMiddleware<ExceptionMiddleware>();
    }
}
