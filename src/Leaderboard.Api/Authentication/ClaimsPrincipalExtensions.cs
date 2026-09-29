using System.Security.Claims;

namespace Leaderboard.Api.Authentication;

public static class ClaimsPrincipalExtensions
{
    public static Guid GetPlayerId(this ClaimsPrincipal user) => GetGuid(user, LeaderboardClaims.Subject);

    public static Guid GetApiKeyId(this ClaimsPrincipal user) => GetGuid(user, LeaderboardClaims.ApiKeyId);

    public static Guid GetApiKeyGameId(this ClaimsPrincipal user) => GetGuid(user, LeaderboardClaims.GameId);

    public static string GetNonce(this ClaimsPrincipal user) =>
        user.FindFirstValue(LeaderboardClaims.Nonce) ?? throw new InvalidOperationException("Missing nonce claim.");

    private static Guid GetGuid(ClaimsPrincipal user, string claim) =>
        Guid.TryParse(user.FindFirstValue(claim), out var value)
            ? value
            : throw new InvalidOperationException($"Authenticated principal has no valid '{claim}' claim.");
}
