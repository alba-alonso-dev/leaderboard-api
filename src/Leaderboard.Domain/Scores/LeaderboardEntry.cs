namespace Leaderboard.Domain.Scores;

/// <summary>
/// Read-optimized projection: the current best score of one player in one game. Ranking queries run over this
/// table (one row per player) instead of the score history. Maintained atomically with each accepted score.
/// </summary>
public sealed class LeaderboardEntry
{
    private LeaderboardEntry() { } // EF Core

    public Guid GameId { get; private set; }

    public Guid PlayerId { get; private set; }

    public long BestScore { get; private set; }

    /// <summary>Normalized key: greater is always better (see <see cref="Games.Game.ToSortKey"/>).</summary>
    public long SortKey { get; private set; }

    public Guid ScoreId { get; private set; }

    /// <summary>When the best score was achieved. Earlier wins ties.</summary>
    public DateTimeOffset AchievedAt { get; private set; }

    public int SubmissionsCount { get; private set; }
}
