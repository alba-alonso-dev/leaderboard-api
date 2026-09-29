using Leaderboard.Application.Abstractions;
using Leaderboard.Application.Abstractions.Persistence;
using Leaderboard.Application.Abstractions.Security;
using Leaderboard.Domain.Players;
using Microsoft.Extensions.Options;

namespace Leaderboard.Application.Auth;

/// <summary>Creates an access token plus a (rotating) refresh token for a player.</summary>
internal sealed class TokenIssuer(
    ITokenService tokenService, IRefreshTokenRepository refreshTokens, IOptions<AuthOptions> options, TimeProvider timeProvider)
{
    public TokenResponse Issue(Player player, Guid? familyId, out RefreshToken refreshToken)
    {
        var accessToken = tokenService.CreateAccessToken(player);
        var (token, hash) = tokenService.CreateRefreshToken();

        refreshToken = RefreshToken.Issue(
            player.Id, hash, timeProvider.GetUtcNow(), TimeSpan.FromDays(options.Value.RefreshTokenLifetimeDays), familyId);
        refreshTokens.Add(refreshToken);

        return new TokenResponse(accessToken.Token, "Bearer", accessToken.ExpiresInSeconds, token, refreshToken.ExpiresAt);
    }
}
