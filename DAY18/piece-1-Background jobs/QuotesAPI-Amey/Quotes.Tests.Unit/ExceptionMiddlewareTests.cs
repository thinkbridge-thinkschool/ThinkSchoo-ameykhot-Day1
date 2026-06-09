using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using QuotesApi.Middleware;
using QuotesApi.Models;
using System.Text.Json;
using Xunit;

namespace Quotes.Tests.Unit;

public class ExceptionMiddlewareTests
{
    private static DefaultHttpContext MakeContext()
    {
        var ctx = new DefaultHttpContext();
        ctx.Response.Body = new MemoryStream();
        return ctx;
    }

    [Fact]
    public async Task InvokeAsync_WhenNoException_CallsNextAndDoesNotAlterResponse()
    {
        var ctx = MakeContext();
        var called = false;
        RequestDelegate next = _ => { called = true; return Task.CompletedTask; };
        var middleware = new ExceptionMiddleware(next, NullLogger<ExceptionMiddleware>.Instance);

        await middleware.InvokeAsync(ctx);

        called.Should().BeTrue();
        ctx.Response.StatusCode.Should().Be(200);
    }

    [Fact]
    public async Task InvokeAsync_WhenDomainExceptionThrown_Returns400WithDetail()
    {
        var ctx = MakeContext();
        RequestDelegate next = _ => throw new DomainException("Name too short");
        var middleware = new ExceptionMiddleware(next, NullLogger<ExceptionMiddleware>.Instance);

        await middleware.InvokeAsync(ctx);

        ctx.Response.StatusCode.Should().Be(400);

        ctx.Response.Body.Seek(0, SeekOrigin.Begin);
        var body = await new StreamReader(ctx.Response.Body).ReadToEndAsync();
        var doc = JsonDocument.Parse(body);
        doc.RootElement.GetProperty("status").GetInt32().Should().Be(400);
        doc.RootElement.GetProperty("detail").GetString().Should().Be("Name too short");
    }

    [Fact]
    public async Task InvokeAsync_WhenArgumentExceptionThrown_Returns400()
    {
        var ctx = MakeContext();
        RequestDelegate next = _ => throw new ArgumentException("Bad arg");
        var middleware = new ExceptionMiddleware(next, NullLogger<ExceptionMiddleware>.Instance);

        await middleware.InvokeAsync(ctx);

        ctx.Response.StatusCode.Should().Be(400);
    }

    [Fact]
    public async Task InvokeAsync_WhenUnhandledExceptionThrown_Returns500()
    {
        var ctx = MakeContext();
        RequestDelegate next = _ => throw new InvalidOperationException("Boom");
        var middleware = new ExceptionMiddleware(next, NullLogger<ExceptionMiddleware>.Instance);

        await middleware.InvokeAsync(ctx);

        ctx.Response.StatusCode.Should().Be(500);
        ctx.Response.Body.Seek(0, SeekOrigin.Begin);
        var body = await new StreamReader(ctx.Response.Body).ReadToEndAsync();
        body.Should().NotBeEmpty();
    }
}
