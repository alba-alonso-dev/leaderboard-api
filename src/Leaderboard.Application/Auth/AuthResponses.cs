namespace Leaderboard.Application.Auth;

public sealed record PlayerResponse(Guid Id, string Username, DateTimeOffset CreatedAt);

public sealed record TokenResponse(
    string AccessToken, string TokenType, int ExpiresIn, string RefreshToken, DateTimeOffset RefreshTokenExpiresAt);
