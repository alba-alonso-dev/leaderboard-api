using Leaderboard.Domain.Common;

namespace Leaderboard.Domain.Players;

public static class PlayerErrors
{
    public static readonly Error EmailTaken = Error.Conflict("player.email_taken", "The email address is already registered.");

    public static readonly Error UsernameTaken = Error.Conflict("player.username_taken", "The username is already taken.");

    public static readonly Error NotFound = Error.NotFound("player.not_found", "The player was not found.");

    public static readonly Error InvalidCredentials = Error.Unauthorized("auth.invalid_credentials", "Invalid email or password.");

    public static readonly Error InvalidRefreshToken = Error.Unauthorized("auth.invalid_refresh_token", "The refresh token is invalid, expired or revoked.");
}
