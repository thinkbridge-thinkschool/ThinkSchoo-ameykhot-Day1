using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using QuotesApi.Data;
using QuotesApi.Models;
using QuotesApi.Services;
using QuotesApi.Time;
using Xunit;

namespace Quotes.Tests.Unit;

public class AuthTokenServiceTests
{
    // ── Helpers (not a SetUp — each test calls them explicitly) ───────────────

    private static AuthTokenService BuildService(QuoteDbContext db, IClock clock, int accessLifetimeSeconds = 900)
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Jwt:Key"] = "ThinkSchoolDay3UnitTestSigningKey-AtLeast32",
                ["Jwt:AccessTokenLifetimeSeconds"] = accessLifetimeSeconds.ToString()
            })
            .Build();

        return new AuthTokenService(db, config, clock, NullLogger<AuthTokenService>.Instance);
    }

    private static QuoteDbContext NewDb() =>
        new(new DbContextOptionsBuilder<QuoteDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options);

    private static async Task<User> SeedUserAsync(QuoteDbContext db)
    {
        var user = new User { Id = Guid.NewGuid(), Email = "unit@test.com", PasswordHash = "hash" };
        db.Users.Add(user);
        await db.SaveChangesAsync();
        return user;
    }

    // ── RefreshAsync ──────────────────────────────────────────────────────────

    [Fact]
    public async Task RefreshAsync_WhenTokenNotInDatabase_ReturnsInvalidToken()
    {
        var clock = Substitute.For<IClock>();
        clock.UtcNow.Returns(new DateTimeOffset(2026, 5, 20, 12, 0, 0, TimeSpan.Zero));
        await using var db = NewDb();
        var sut = BuildService(db, clock);

        var result = await sut.RefreshAsync("token-that-does-not-exist");

        result.IsSuccess.Should().BeFalse();
        result.FailureReason.Should().Be(RefreshFailureReason.InvalidToken);
    }

    [Fact]
    public async Task RefreshAsync_WhenTokenIsExpired_ReturnsExpiredToken()
    {
        var issuedAt = new DateTimeOffset(2026, 5, 1, 12, 0, 0, TimeSpan.Zero);
        await using var db = NewDb();
        var user = await SeedUserAsync(db);

        var issuingClock = Substitute.For<IClock>();
        issuingClock.UtcNow.Returns(issuedAt);
        var pair = await BuildService(db, issuingClock).IssueTokenPairAsync(user);

        // Advance clock 8 days past the 7-day refresh token lifetime
        var expiredClock = Substitute.For<IClock>();
        expiredClock.UtcNow.Returns(issuedAt.AddDays(8));
        var sut = BuildService(db, expiredClock);

        var result = await sut.RefreshAsync(pair.RefreshToken);

        result.IsSuccess.Should().BeFalse();
        result.FailureReason.Should().Be(RefreshFailureReason.ExpiredToken);
    }

    [Fact]
    public async Task RefreshAsync_WhenTokenRevokedWithoutReplacement_ReturnsRevokedToken()
    {
        var clock = Substitute.For<IClock>();
        clock.UtcNow.Returns(new DateTimeOffset(2026, 5, 20, 12, 0, 0, TimeSpan.Zero));
        await using var db = NewDb();
        var user = await SeedUserAsync(db);
        var sut = BuildService(db, clock);

        var pair = await sut.IssueTokenPairAsync(user);
        await sut.RevokeAsync(pair.RefreshToken);

        var result = await sut.RefreshAsync(pair.RefreshToken);

        result.IsSuccess.Should().BeFalse();
        result.FailureReason.Should().Be(RefreshFailureReason.RevokedToken);
    }

    [Fact]
    public async Task RefreshAsync_WhenTokenIsValid_ReturnsNewTokenPair()
    {
        var clock = Substitute.For<IClock>();
        clock.UtcNow.Returns(new DateTimeOffset(2026, 5, 20, 12, 0, 0, TimeSpan.Zero));
        await using var db = NewDb();
        var user = await SeedUserAsync(db);
        var sut = BuildService(db, clock);
        var loginPair = await sut.IssueTokenPairAsync(user);

        var result = await sut.RefreshAsync(loginPair.RefreshToken);

        result.IsSuccess.Should().BeTrue();
        result.FailureReason.Should().Be(RefreshFailureReason.None);
        result.Tokens.Should().NotBeNull();
        result.Tokens!.AccessToken.Should().NotBeNullOrEmpty();
        result.Tokens!.RefreshToken.Should().NotBe(loginPair.RefreshToken);
    }

    [Fact]
    public async Task RefreshAsync_WhenTokenReused_RevokesEntireFamilyAndReturnsReuseDetected()
    {
        var clock = Substitute.For<IClock>();
        clock.UtcNow.Returns(new DateTimeOffset(2026, 5, 20, 12, 0, 0, TimeSpan.Zero));
        await using var db = NewDb();
        var user = await SeedUserAsync(db);
        var sut = BuildService(db, clock);

        var loginPair = await sut.IssueTokenPairAsync(user);
        var firstRefresh = await sut.RefreshAsync(loginPair.RefreshToken);
        firstRefresh.IsSuccess.Should().BeTrue();

        // Reuse the already-consumed original token
        var reuseResult = await sut.RefreshAsync(loginPair.RefreshToken);

        reuseResult.IsSuccess.Should().BeFalse();
        reuseResult.FailureReason.Should().Be(RefreshFailureReason.ReuseDetected);

        // Child token issued during first refresh must also be revoked
        var childResult = await sut.RefreshAsync(firstRefresh.Tokens!.RefreshToken);
        childResult.FailureReason.Should().Be(RefreshFailureReason.RevokedToken);

        var activeTokens = await db.RefreshTokens.CountAsync(t => t.RevokedAt == null);
        activeTokens.Should().Be(0, "entire family must be revoked after reuse detection");
    }

    // ── RevokeAsync ───────────────────────────────────────────────────────────

    [Fact]
    public async Task RevokeAsync_WhenTokenExists_SetsRevokedAtToClockTime()
    {
        var fixedNow = new DateTimeOffset(2026, 5, 20, 12, 0, 0, TimeSpan.Zero);
        var clock = Substitute.For<IClock>();
        clock.UtcNow.Returns(fixedNow);
        await using var db = NewDb();
        var user = await SeedUserAsync(db);
        var sut = BuildService(db, clock);

        var pair = await sut.IssueTokenPairAsync(user);
        await sut.RevokeAsync(pair.RefreshToken);

        var stored = await db.RefreshTokens.FirstAsync();
        stored.RevokedAt.Should().Be(fixedNow.UtcDateTime);
    }

    // ── IssueTokenPairAsync ───────────────────────────────────────────────────

    [Fact]
    public async Task IssueTokenPairAsync_ReturnsTokenPairWithConfiguredExpiresIn()
    {
        var clock = Substitute.For<IClock>();
        clock.UtcNow.Returns(new DateTimeOffset(2026, 5, 20, 12, 0, 0, TimeSpan.Zero));
        await using var db = NewDb();
        var user = await SeedUserAsync(db);
        var sut = BuildService(db, clock, accessLifetimeSeconds: 1800);

        var pair = await sut.IssueTokenPairAsync(user);

        pair.ExpiresIn.Should().Be(1800);
        pair.AccessToken.Should().NotBeNullOrEmpty();
        pair.RefreshToken.Should().NotBeNullOrEmpty();
    }
}
