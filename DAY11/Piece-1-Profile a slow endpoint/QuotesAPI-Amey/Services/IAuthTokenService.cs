using QuotesApi.Models;

namespace QuotesApi.Services;

public interface IAuthTokenService
{
    Task<TokenPair> IssueTokenPairAsync(User user, Guid? familyId = null, CancellationToken cancellationToken = default);
    Task<RefreshOutcome> RefreshAsync(string refreshToken, CancellationToken cancellationToken = default);
    Task RevokeAsync(string refreshToken, CancellationToken cancellationToken = default);
}

public sealed record TokenPair(string AccessToken, string RefreshToken, int ExpiresIn);

public enum RefreshFailureReason
{
    None,
    InvalidToken,
    ExpiredToken,
    RevokedToken,
    ReuseDetected
}

public sealed record RefreshOutcome(TokenPair? Tokens, RefreshFailureReason FailureReason)
{
    public bool IsSuccess => Tokens is not null;
}
