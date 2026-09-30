namespace Leaderboard.Infrastructure.ReadModels;

/// <summary>
/// Denormalized counter so "total players" never needs a <c>COUNT(*)</c> over the whole game.
/// Incremented only when a player gets their first entry in a game (rare compared to reads).
/// </summary>
internal sealed class LeaderboardStats
{
    public Guid GameId { get; set; }

    public long PlayerCount { get; set; }
}
