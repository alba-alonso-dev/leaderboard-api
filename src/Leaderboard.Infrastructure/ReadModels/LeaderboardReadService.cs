using Leaderboard.Application.Abstractions.Persistence;
using Leaderboard.Domain.Scores;
using Microsoft.EntityFrameworkCore;

namespace Leaderboard.Infrastructure.ReadModels;

/// <summary>
/// Ranking queries over <c>leaderboard_entries</c>, all served by the index
/// <c>(game_id, sort_key DESC, achieved_at, player_id)</c>. Ranks are unique: ties are broken by who achieved the score first.
/// </summary>
internal sealed class LeaderboardReadService(Persistence.AppDbContext context) : ILeaderboardReadService
{
    public async Task<IReadOnlyList<LeaderboardRow>> GetRangeAsync(Guid gameId, int offset, int limit, CancellationToken cancellationToken) =>
        await ToRows(Best(WithUsername(context.LeaderboardEntries.Where(e => e.GameId == gameId))).Skip(offset).Take(limit))
            .ToListAsync(cancellationToken);

    public Task<long> CountPlayersAsync(Guid gameId, CancellationToken cancellationToken) =>
        context.LeaderboardEntries.LongCountAsync(e => e.GameId == gameId, cancellationToken);

    public async Task<RankedRow?> GetPlayerRankAsync(Guid gameId, Guid playerId, CancellationToken cancellationToken)
    {
        var row = await ToRows(WithUsername(context.LeaderboardEntries.Where(e => e.GameId == gameId && e.PlayerId == playerId)))
            .FirstOrDefaultAsync(cancellationToken);
        if (row is null)
        {
            return null;
        }

        var better = await context.LeaderboardEntries
            .FromSql(
                $"""
                SELECT * FROM leaderboard_entries
                WHERE game_id = {gameId}
                  AND (sort_key > {row.SortKey}
                       OR (sort_key = {row.SortKey} AND achieved_at < {row.AchievedAt})
                       OR (sort_key = {row.SortKey} AND achieved_at = {row.AchievedAt} AND player_id < {row.PlayerId}))
                """)
            .LongCountAsync(cancellationToken);

        return new RankedRow(row, better + 1);
    }

    public async Task<IReadOnlyList<LeaderboardRow>> GetAboveAsync(
        Guid gameId, LeaderboardRow target, int count, CancellationToken cancellationToken)
    {
        var better = context.LeaderboardEntries.FromSql(
            $"""
            SELECT * FROM leaderboard_entries
            WHERE game_id = {gameId}
              AND (sort_key > {target.SortKey}
                   OR (sort_key = {target.SortKey} AND achieved_at < {target.AchievedAt})
                   OR (sort_key = {target.SortKey} AND achieved_at = {target.AchievedAt} AND player_id < {target.PlayerId}))
            """);

        // Walk the index backwards from the target (closest first), then restore ranking order.
        var closestFirst = await ToRows(
                WithUsername(better)
                    .OrderBy(x => x.Entry.SortKey).ThenByDescending(x => x.Entry.AchievedAt).ThenByDescending(x => x.Entry.PlayerId)
                    .Take(count))
            .ToListAsync(cancellationToken);
        closestFirst.Reverse();
        return closestFirst;
    }

    public async Task<IReadOnlyList<LeaderboardRow>> GetBelowAsync(
        Guid gameId, LeaderboardRow target, int count, CancellationToken cancellationToken)
    {
        var worse = context.LeaderboardEntries.FromSql(
            $"""
            SELECT * FROM leaderboard_entries
            WHERE game_id = {gameId}
              AND (sort_key < {target.SortKey}
                   OR (sort_key = {target.SortKey} AND achieved_at > {target.AchievedAt})
                   OR (sort_key = {target.SortKey} AND achieved_at = {target.AchievedAt} AND player_id > {target.PlayerId}))
            """);

        return await ToRows(Best(WithUsername(worse)).Take(count)).ToListAsync(cancellationToken);
    }

    /// <summary>Ranking order. Applied after the join so the final SQL keeps it.</summary>
    private static IQueryable<EntryWithUsername> Best(IQueryable<EntryWithUsername> rows) =>
        rows.OrderByDescending(x => x.Entry.SortKey).ThenBy(x => x.Entry.AchievedAt).ThenBy(x => x.Entry.PlayerId);

    private IQueryable<EntryWithUsername> WithUsername(IQueryable<LeaderboardEntry> entries) =>
        entries.Join(context.Players, e => e.PlayerId, p => p.Id, (e, p) => new EntryWithUsername { Entry = e, Username = p.Username });

    private static IQueryable<LeaderboardRow> ToRows(IQueryable<EntryWithUsername> rows) =>
        rows.Select(x => new LeaderboardRow(
            x.Entry.PlayerId, x.Username, x.Entry.BestScore, x.Entry.SortKey, x.Entry.AchievedAt, x.Entry.ScoreId));

    private sealed class EntryWithUsername
    {
        public required LeaderboardEntry Entry { get; init; }

        public required string Username { get; init; }
    }
}
