using Leaderboard.Domain.Common;

namespace Leaderboard.Domain.Games;

public static class ApiKeyErrors
{
    public static readonly Error NotFound = Error.NotFound("apikey.not_found", "The API key was not found.");

    public static readonly Error LimitReached = Error.Conflict(
        "apikey.limit_reached", $"A game can have at most {Game.MaxActiveApiKeys} active API keys. Revoke one first.");

    public static readonly Error Missing = Error.Unauthorized(
        "apikey.missing", "The X-Api-Key, X-Timestamp, X-Nonce and X-Signature headers are required.");

    public static readonly Error Invalid = Error.Unauthorized("apikey.invalid", "The API key is invalid or has been revoked.");

    public static readonly Error InvalidSignature = Error.Unauthorized("apikey.invalid_signature", "The request signature is invalid.");

    public static readonly Error StaleRequest = Error.Unauthorized(
        "apikey.stale_request", "The request timestamp is outside the accepted window.");

    public static readonly Error GameMismatch = Error.Forbidden("apikey.game_mismatch", "The API key does not belong to this game.");
}
