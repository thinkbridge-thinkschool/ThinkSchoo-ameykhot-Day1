using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using QuotesApi.Data;
using QuotesApi.Models;
using QuotesApi.Time;
using System.Diagnostics;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;

namespace QuotesApi.Services;

public class AuthTokenService : IAuthTokenService
{
    public const string ActivitySourceName = "QuotesApi.AuthTokenService";
    private static readonly ActivitySource _activitySource = new(ActivitySourceName);

    private readonly QuoteDbContext _dbContext;
    private readonly IConfiguration _configuration;
    private readonly IClock _clock;
    private readonly ILogger<AuthTokenService> _logger;

    public AuthTokenService(
        QuoteDbContext dbContext,
        IConfiguration configuration,
        IClock clock,
        ILogger<AuthTokenService> logger)
    {
        _dbContext = dbContext;
        _configuration = configuration;
        _clock = clock;
        _logger = logger;
    }

    public async Task<TokenPair> IssueTokenPairAsync(User user, Guid? familyId = null, CancellationToken cancellationToken = default)
    {
        using var activity = _activitySource.StartActivity("issue-token-pair");
        activity?.SetTag("user.id", user.Id.ToString());
        activity?.SetTag("user.email", user.Email);

        var accessLifetimeSeconds = _configuration.GetValue<int?>("Jwt:AccessTokenLifetimeSeconds") ?? 900;
        var accessToken = GenerateJwtToken(user, TimeSpan.FromSeconds(accessLifetimeSeconds));

        var refreshToken = await CreateRefreshTokenAsync(
            userId: user.Id,
            family: (familyId ?? Guid.NewGuid()).ToString("N"),
            cancellationToken);

        activity?.SetTag("token.lifetime_seconds", accessLifetimeSeconds);
        return new TokenPair(accessToken, refreshToken.RawToken, accessLifetimeSeconds);
    }

    public async Task<RefreshOutcome> RefreshAsync(string refreshToken, CancellationToken cancellationToken = default)
    {
        using var activity = _activitySource.StartActivity("refresh-token");
        var now = _clock.UtcNow.UtcDateTime;
        var hashedIncomingToken = HashToken(refreshToken);

        var existing = await _dbContext.RefreshTokens
            .Include(r => r.User)
            .FirstOrDefaultAsync(r => r.Token == hashedIncomingToken, cancellationToken);

        if (existing is null)
            return new RefreshOutcome(null, RefreshFailureReason.InvalidToken);

        if (existing.RevokedAt is not null)
        {
            if (!string.IsNullOrWhiteSpace(existing.ReplacedByToken))
            {
                await RevokeEntireFamilyAsync(existing.Family, now, cancellationToken);

                _logger.LogWarning(
                    "SECURITY_EVENT Refresh token reuse detected. UserId: {UserId}, Family: {Family}",
                    existing.UserId,
                    existing.Family);

                activity?.SetTag("security.reuse_detected", true);
                activity?.SetTag("user.id", existing.UserId.ToString());
                activity?.SetStatus(ActivityStatusCode.Error, "Token reuse detected");
                return new RefreshOutcome(null, RefreshFailureReason.ReuseDetected);
            }

            return new RefreshOutcome(null, RefreshFailureReason.RevokedToken);
        }

        if (existing.ExpiresAt <= now)
            return new RefreshOutcome(null, RefreshFailureReason.ExpiredToken);

        var accessLifetimeSeconds = _configuration.GetValue<int?>("Jwt:AccessTokenLifetimeSeconds") ?? 900;
        var accessToken = GenerateJwtToken(existing.User, TimeSpan.FromSeconds(accessLifetimeSeconds));

        var nextRefresh = await CreateRefreshTokenAsync(
            existing.UserId,
            existing.Family,
            cancellationToken);

        existing.RevokedAt = now;
        existing.ReplacedByToken = nextRefresh.Entity.Token;

        await _dbContext.SaveChangesAsync(cancellationToken);

        activity?.SetTag("user.id", existing.UserId.ToString());
        activity?.SetTag("token.family", existing.Family);
        activity?.SetStatus(ActivityStatusCode.Ok);

        return new RefreshOutcome(
            new TokenPair(accessToken, nextRefresh.RawToken, accessLifetimeSeconds),
            RefreshFailureReason.None);
    }

    public async Task RevokeAsync(string refreshToken, CancellationToken cancellationToken = default)
    {
        var hashedIncomingToken = HashToken(refreshToken);

        var existing = await _dbContext.RefreshTokens
            .FirstOrDefaultAsync(r => r.Token == hashedIncomingToken, cancellationToken);

        if (existing is null || existing.RevokedAt is not null)
            return;

        existing.RevokedAt = _clock.UtcNow.UtcDateTime;
        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    private async Task<(string RawToken, RefreshToken Entity)> CreateRefreshTokenAsync(
        Guid userId,
        string family,
        CancellationToken cancellationToken)
    {
        var rawToken = Convert.ToBase64String(RandomNumberGenerator.GetBytes(64));
        var entity = new RefreshToken
        {
            Id = Guid.NewGuid(),
            Token = HashToken(rawToken),
            UserId = userId,
            Family = family,
            ExpiresAt = _clock.UtcNow.UtcDateTime.AddDays(7)
        };

        _dbContext.RefreshTokens.Add(entity);
        await _dbContext.SaveChangesAsync(cancellationToken);

        return (rawToken, entity);
    }

    private async Task RevokeEntireFamilyAsync(string family, DateTime nowUtc, CancellationToken cancellationToken)
    {
        var familyTokens = await _dbContext.RefreshTokens
            .Where(r => r.Family == family && r.RevokedAt == null)
            .ToListAsync(cancellationToken);

        foreach (var token in familyTokens)
            token.RevokedAt = nowUtc;

        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    private string GenerateJwtToken(User user, TimeSpan lifetime)
    {
        var keyString = _configuration["Jwt:Key"]
            ?? throw new InvalidOperationException("Jwt:Key not found in configuration");

        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(keyString));
        var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        var token = new JwtSecurityToken(
            claims:
            [
                new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()),
                new Claim(ClaimTypes.Email, user.Email),
                new Claim("scope", "quotes.write")
            ],
            expires: _clock.UtcNow.UtcDateTime.Add(lifetime),
            signingCredentials: creds);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    private static string HashToken(string rawToken)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(rawToken));
        return Convert.ToBase64String(bytes);
    }
}
