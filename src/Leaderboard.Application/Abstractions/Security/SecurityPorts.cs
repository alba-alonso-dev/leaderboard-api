using Leaderboard.Domain.Players;

namespace Leaderboard.Application.Abstractions.Security;

public interface IPasswordHasher
{
    string Hash(Player player, string password);

    bool Verify(Player player, string passwordHash, string providedPassword);
}

public interface ITokenService
{
    AccessToken CreateAccessToken(Player player);

    /// <summary>Creates a random opaque refresh token and the hash to store.</summary>
    (string Token, byte[] Hash) CreateRefreshToken();

    byte[] HashRefreshToken(string token);
}

public sealed record AccessToken(string Token, DateTimeOffset ExpiresAt, int ExpiresInSeconds);

/// <summary>Encrypts API key secrets at rest (ASP.NET Core Data Protection in production).</summary>
public interface IApiKeySecretProtector
{
    byte[] Protect(string secret);

    string Unprotect(byte[] ciphertext);
}
