using Leaderboard.Domain.Common;

namespace Leaderboard.Domain.Scores;

public static class ScoreErrors
{
    public static readonly Error NotFound = Error.NotFound("score.not_found", "The score was not found.");

    public static readonly Error PlayerNotFound = Error.BusinessRule(
        "player.not_found_for_score", "The player referenced by the score does not exist.");

    public static readonly Error EntryNotFound = Error.NotFound(
        "leaderboard.entry_not_found", "The player has no scores in this game.");

    public static readonly Error PlayerRateLimited = new(
        "rate_limit.player_exceeded", "Too many scores submitted for this player in this game. Try again later.", ErrorType.TooManyRequests);

    public static readonly Error NotPending = Error.Conflict(
        "score.not_pending", "Only scores pending review can be approved.");

    public static readonly Error NonceReused = Error.Conflict(
        "score.nonce_reused", "The nonce was already used by this API key for a different score.");
}
