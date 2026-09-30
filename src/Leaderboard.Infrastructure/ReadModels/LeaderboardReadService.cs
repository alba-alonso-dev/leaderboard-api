using Leaderboard.Application.Abstractions.Persistence;
using Leaderboard.Domain.Scores;
using Microsoft.EntityFrameworkCore;

namespace Leaderboard.Infrastructure.ReadModels;

/// <summary>
/// Ranking queries over <c>leaderboard_entries</c>. Ranking order is <c>(rank_key, achieved_at, player_id)</c> ascending
/// (lower rank key is better; earlier achievement wins ties), served by the all-ascending covering index
/// <c>ix_leaderboard_entries_rank</c>. Every query seeks the index and touches only the rows it returns (or counts),
/// then joins usernames for that small set.
/// </summary>
internal sealed class LeaderboardReadService(Persistence.AppDbContext context) : ILeaderboardReadService
{
    public async Task<IReadOnlyList<LeaderboardRow>> GetRangeAsync(Guid gameId, int offset, int limit, CancellationToken cancellationToken)
    {
        // Page the index first, then join: the join only sees `limit` rows.
        var page = context.LeaderboardEntries
            .Where(e => e.GameId == gameId)
            .OrderBy(e => e.RankKey).ThenBy(e => e.AchievedAt).ThenBy(e => e.PlayerId)
            .Skip(offset).Take(limit);

        return await ToRows(page).ToListAsync(cancellationToken);
    }

    public async Task<long> CountPlayersAsync(Guid gameId, CancellationToken cancellationToken) =>
        await context.LeaderboardStats.Where(s => s.GameId == gameId).Select(s => (long?)s.PlayerCount).FirstOrDefaultAsync(cancellationToken)
        ?? 0;

    public async Task<RankedRow?> GetPlayerRankAsync(Guid gameId, Guid playerId, CancellationToken cancellationToken)
    {
        var row = await ToRows(context.LeaderboardEntries.Where(e => e.GameId == gameId && e.PlayerId == playerId))
            .FirstOrDefaultAsync(cancellationToken);
        if (row is null)
        {
            return null;
        }

        // Rank = 1 + entries strictly ahead: an index range scan thanks to the row-value comparison.
        var ahead = await context.LeaderboardEntries
            .FromSql(
                $"""
                SELECT * FROM leaderboard_entries
                WHERE game_id = {gameId}
                  AND (rank_key, achieved_at, player_id) < ({row.RankKey}, {row.AchievedAt}, {row.PlayerId})
                """)
            .LongCountAsync(cancellationToken);

        return new RankedRow(row, ahead + 1);
    }

    public async Task<IReadOnlyList<LeaderboardRow>> GetAboveAsync(
        Guid gameId, LeaderboardRow target, int count, CancellationToken cancellationToken)
    {
        // Walk the index backwards from the target (closest first), then restore ranking order.
        var closestFirst = context.LeaderboardEntries.FromSql(
            $"""
            SELECT * FROM leaderboard_entries
            WHERE game_id = {gameId}
              AND (rank_key, achieved_at, player_id) < ({target.RankKey}, {target.AchievedAt}, {target.PlayerId})
            ORDER BY rank_key DESC, achieved_at DESC, player_id DESC
            LIMIT {count}
            """);

        return await ToRows(closestFirst.OrderBy(e => e.RankKey).ThenBy(e => e.AchievedAt).ThenBy(e => e.PlayerId))
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<LeaderboardRow>> GetBelowAsync(
        Guid gameId, LeaderboardRow target, int count, CancellationToken cancellationToken)
    {
        var next = context.LeaderboardEntries.FromSql(
            $"""
            SELECT * FROM leaderboard_entries
            WHERE game_id = {gameId}
              AND (rank_key, achieved_at, player_id) > ({target.RankKey}, {target.AchievedAt}, {target.PlayerId})
            ORDER BY rank_key, achieved_at, player_id
            LIMIT {count}
            """);

        return await ToRows(next.OrderBy(e => e.RankKey).ThenBy(e => e.AchievedAt).ThenBy(e => e.PlayerId))
            .ToListAsync(cancellationToken);
    }

    /// <summary>Joins usernames and keeps the ranking order in the outer query.</summary>
    private IQueryable<LeaderboardRow> ToRows(IQueryable<LeaderboardEntry> entries) =>
        entries
            .Join(context.Players, e => e.PlayerId, p => p.Id, (e, p) => new { Entry = e, p.Username })
            .OrderBy(x => x.Entry.RankKey).ThenBy(x => x.Entry.AchievedAt).ThenBy(x => x.Entry.PlayerId)
            .Select(x => new LeaderboardRow(
                x.Entry.PlayerId, x.Username, x.Entry.BestScore, x.Entry.RankKey, x.Entry.AchievedAt, x.Entry.ScoreId));
}
