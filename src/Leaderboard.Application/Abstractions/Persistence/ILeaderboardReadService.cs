namespace Leaderboard.Application.Abstractions.Persistence;

/// <summary>Optimized read side for rankings (SQL projections over <c>leaderboard_entries</c>).</summary>
public interface ILeaderboardReadService
{
    Task<IReadOnlyList<LeaderboardRow>> GetRangeAsync(Guid gameId, int offset, int limit, CancellationToken cancellationToken);

    Task<long> CountPlayersAsync(Guid gameId, CancellationToken cancellationToken);

    /// <summary>Absolute rank of a player or <c>null</c> when the player has no entry in the game.</summary>
    Task<RankedRow?> GetPlayerRankAsync(Guid gameId, Guid playerId, CancellationToken cancellationToken);

    /// <summary>Up to <paramref name="count"/> entries ranked immediately above the target (closest first is last).</summary>
    Task<IReadOnlyList<LeaderboardRow>> GetAboveAsync(Guid gameId, LeaderboardRow target, int count, CancellationToken cancellationToken);

    /// <summary>Up to <paramref name="count"/> entries ranked immediately below the target.</summary>
    Task<IReadOnlyList<LeaderboardRow>> GetBelowAsync(Guid gameId, LeaderboardRow target, int count, CancellationToken cancellationToken);
}

public sealed record LeaderboardRow(Guid PlayerId, string Username, long BestScore, long RankKey, DateTimeOffset AchievedAt, Guid ScoreId);

public sealed record RankedRow(LeaderboardRow Row, long Rank);
