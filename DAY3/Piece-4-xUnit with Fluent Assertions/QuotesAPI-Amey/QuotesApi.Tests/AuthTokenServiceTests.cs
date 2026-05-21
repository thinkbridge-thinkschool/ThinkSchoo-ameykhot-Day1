using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using QuotesApi.Data;
using QuotesApi.Models;
using QuotesApi.Services;
using QuotesApi.Time;
using Xunit;

namespace QuotesApi.Tests;

public class AuthTokenServiceTests
{
    [Fact]
    public async Task Refresh_WhenTokenReused_RevokesEntireChain()
    {
        var dbName = Guid.NewGuid().ToString("N");
        var options = new DbContextOptionsBuilder<QuoteDbContext>()
            .UseInMemoryDatabase(dbName)
            .Options;

        await using var dbContext = new QuoteDbContext(options);
        var user = new User
        {
            Id = Guid.NewGuid(),
            Email = "test@test.com",
            PasswordHash = BCrypt.Net.BCrypt.HashPassword("password123")
        };

        dbContext.Users.Add(user);
        await dbContext.SaveChangesAsync();

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Jwt:Key"] = "ThinkSchoolDay2JwtSigningKey-UseAtLeast32Chars",
                ["Jwt:AccessTokenLifetimeSeconds"] = "900"
            })
            .Build();

        IClock clock = new TestClock(new DateTimeOffset(2026, 5, 20, 12, 0, 0, TimeSpan.Zero));
        var service = new AuthTokenService(dbContext, configuration, clock, NullLogger<AuthTokenService>.Instance);

        var loginPair = await service.IssueTokenPairAsync(user);

        var firstRefresh = await service.RefreshAsync(loginPair.RefreshToken);
        Assert.True(firstRefresh.IsSuccess);

        var secondRefresh = await service.RefreshAsync(loginPair.RefreshToken);
        Assert.False(secondRefresh.IsSuccess);
        Assert.Equal(RefreshFailureReason.ReuseDetected, secondRefresh.FailureReason);

        var thirdRefresh = await service.RefreshAsync(firstRefresh.Tokens!.RefreshToken);
        Assert.False(thirdRefresh.IsSuccess);
        Assert.Equal(RefreshFailureReason.RevokedToken, thirdRefresh.FailureReason);

        var family = await dbContext.RefreshTokens
            .Select(t => t.Family)
            .FirstAsync();

        var activeFamilyTokens = await dbContext.RefreshTokens
            .Where(t => t.Family == family && t.RevokedAt == null)
            .CountAsync();

        Assert.Equal(0, activeFamilyTokens);
    }

    private sealed class TestClock : IClock
    {
        public TestClock(DateTimeOffset utcNow)
        {
            UtcNow = utcNow;
        }

        public DateTimeOffset UtcNow { get; }
    }
}