using Leaderboard.Domain.Common;

namespace Leaderboard.Domain.Games;

public static class GameErrors
{
    public static readonly Error NotFound = Error.NotFound("game.not_found", "The game was not found.");

    public static readonly Error SlugTaken = Error.Conflict("game.slug_taken", "Another game already uses this slug.");

    public static readonly Error Archived = Error.Conflict("game.archived", "The game is archived and no longer accepts scores.");

    public static readonly Error ConcurrencyConflict = new(
        "concurrency.conflict", "The game was modified by another request. Reload it and try again.", ErrorType.PreconditionFailed);

    public static Error ScoreOutOfRange(long value, long? min, long? max) => Error.BusinessRule(
        "score.out_of_range",
        $"Score {value} is outside the range [{min?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "-∞"}, " +
        $"{max?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "+∞"}] configured for this game.");
}
