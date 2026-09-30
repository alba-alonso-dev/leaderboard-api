using Leaderboard.Application.Abstractions.Persistence;
using Leaderboard.Application.Common;
using Leaderboard.Domain.Games;
using Leaderboard.Domain.Players;
using Leaderboard.Domain.Scores;
using Microsoft.EntityFrameworkCore;

namespace Leaderboard.Infrastructure.Persistence.Repositories;

internal sealed class PlayerRepository(AppDbContext context) : IPlayerRepository
{
    public Task<Player?> GetByIdAsync(Guid id, CancellationToken cancellationToken) =>
        context.Players.FirstOrDefaultAsync(p => p.Id == id, cancellationToken);

    public Task<Player?> GetByEmailAsync(string email, CancellationToken cancellationToken) =>
        context.Players.FirstOrDefaultAsync(p => p.Email == email, cancellationToken);

    public Task<bool> ExistsAsync(Guid id, CancellationToken cancellationToken) =>
        context.Players.AnyAsync(p => p.Id == id, cancellationToken);

    public Task<bool> EmailExistsAsync(string email, CancellationToken cancellationToken) =>
        context.Players.AnyAsync(p => p.Email == email, cancellationToken);

    public Task<bool> UsernameExistsAsync(string username, CancellationToken cancellationToken) =>
        context.Players.AnyAsync(p => p.Username == username, cancellationToken); // citext: case-insensitive

    public void Add(Player player) => context.Players.Add(player);
}

internal sealed class RefreshTokenRepository(AppDbContext context) : IRefreshTokenRepository
{
    public Task<RefreshToken?> GetByHashAsync(byte[] tokenHash, CancellationToken cancellationToken) =>
        context.RefreshTokens.FirstOrDefaultAsync(t => t.TokenHash == tokenHash, cancellationToken);

    public Task RevokeFamilyAsync(Guid familyId, DateTimeOffset now, CancellationToken cancellationToken) =>
        context.RefreshTokens
            .Where(t => t.FamilyId == familyId && t.RevokedAt == null)
            .ExecuteUpdateAsync(s => s.SetProperty(t => t.RevokedAt, now), cancellationToken);

    public void Add(RefreshToken token) => context.RefreshTokens.Add(token);
}

internal sealed class GameRepository(AppDbContext context) : IGameRepository
{
    public Task<Game?> GetByIdAsync(Guid id, CancellationToken cancellationToken) =>
        context.Games.FirstOrDefaultAsync(g => g.Id == id, cancellationToken);

    public Task<bool> ExistsAsync(Guid id, CancellationToken cancellationToken) =>
        context.Games.AnyAsync(g => g.Id == id, cancellationToken);

    public Task<bool> SlugExistsAsync(string slug, CancellationToken cancellationToken)
    {
        var normalized = slug.Trim().ToLowerInvariant();
        return context.Games.AnyAsync(g => g.Slug == normalized, cancellationToken);
    }

    public async Task<PagedResult<Game>> ListActiveAsync(string? search, int page, int pageSize, CancellationToken cancellationToken)
    {
        var query = context.Games.AsNoTracking().Where(g => !g.IsArchived);
        if (!string.IsNullOrWhiteSpace(search))
        {
            var pattern = "%" + EscapeLike(search.Trim()) + "%";
            query = query.Where(g => EF.Functions.ILike(g.Name, pattern, "\\") || EF.Functions.ILike(g.Slug, pattern, "\\"));
        }

        var total = await query.LongCountAsync(cancellationToken);
        var items = await query
            .OrderBy(g => g.Name).ThenBy(g => g.Id)
            .Skip(Paging.Offset(page, pageSize)).Take(pageSize)
            .ToListAsync(cancellationToken);

        return new PagedResult<Game>(items, page, pageSize, total);
    }

    public void Add(Game game) => context.Games.Add(game);

    private static string EscapeLike(string value) =>
        value.Replace("\\", "\\\\", StringComparison.Ordinal)
             .Replace("%", "\\%", StringComparison.Ordinal)
             .Replace("_", "\\_", StringComparison.Ordinal);
}

internal sealed class GameApiKeyRepository(AppDbContext context) : IGameApiKeyRepository
{
    public Task<GameApiKey?> GetByIdAsync(Guid gameId, Guid apiKeyId, CancellationToken cancellationToken) =>
        context.GameApiKeys.FirstOrDefaultAsync(k => k.GameId == gameId && k.Id == apiKeyId, cancellationToken);

    public Task<GameApiKey?> GetActiveByKeyIdAsync(string keyId, CancellationToken cancellationToken) =>
        context.GameApiKeys.FirstOrDefaultAsync(k => k.KeyId == keyId && k.RevokedAt == null, cancellationToken);

    public async Task<IReadOnlyList<GameApiKey>> ListByGameAsync(Guid gameId, CancellationToken cancellationToken) =>
        await context.GameApiKeys.AsNoTracking()
            .Where(k => k.GameId == gameId)
            .OrderByDescending(k => k.CreatedAt)
            .ToListAsync(cancellationToken);

    public Task<int> CountActiveAsync(Guid gameId, CancellationToken cancellationToken) =>
        context.GameApiKeys.CountAsync(k => k.GameId == gameId && k.RevokedAt == null, cancellationToken);

    public void Add(GameApiKey apiKey) => context.GameApiKeys.Add(apiKey);
}

internal sealed class ScoreRepository(AppDbContext context) : IScoreRepository
{
    public Task<Score?> GetByIdAsync(Guid id, CancellationToken cancellationToken) =>
        context.Scores.FirstOrDefaultAsync(s => s.Id == id, cancellationToken);

    public Task<Score?> GetByNonceAsync(Guid apiKeyId, string nonce, CancellationToken cancellationToken) =>
        context.Scores.AsNoTracking().FirstOrDefaultAsync(s => s.ApiKeyId == apiKeyId && s.Nonce == nonce, cancellationToken);

    public Task<int> CountSinceAsync(Guid gameId, Guid playerId, DateTimeOffset since, CancellationToken cancellationToken) =>
        context.Scores.CountAsync(s => s.PlayerId == playerId && s.GameId == gameId && s.SubmittedAt >= since, cancellationToken);

    public Task<long?> GetBestScoreAsync(Guid gameId, Guid playerId, CancellationToken cancellationToken) =>
        context.LeaderboardEntries
            .Where(e => e.GameId == gameId && e.PlayerId == playerId)
            .Select(e => (long?)e.BestScore)
            .FirstOrDefaultAsync(cancellationToken);

    public async Task<PagedResult<Score>> ListByStatusAsync(
        ScoreStatus status, Guid? gameId, int page, int pageSize, CancellationToken cancellationToken)
    {
        var query = context.Scores.AsNoTracking().Where(s => s.Status == status);
        if (gameId is not null)
        {
            query = query.Where(s => s.GameId == gameId);
        }

        var total = await query.LongCountAsync(cancellationToken);
        var items = await query
            .OrderBy(s => s.SubmittedAt).ThenBy(s => s.Id) // oldest first: a moderation queue
            .Skip(Paging.Offset(page, pageSize)).Take(pageSize)
            .ToListAsync(cancellationToken);

        return new PagedResult<Score>(items, page, pageSize, total);
    }

    public async Task<PagedResult<Score>> ListByPlayerAsync(
        Guid playerId, Guid? gameId, int page, int pageSize, CancellationToken cancellationToken)
    {
        var query = context.Scores.AsNoTracking().Where(s => s.PlayerId == playerId);
        if (gameId is not null)
        {
            query = query.Where(s => s.GameId == gameId);
        }

        var total = await query.LongCountAsync(cancellationToken);
        var items = await query
            .OrderByDescending(s => s.SubmittedAt).ThenByDescending(s => s.Id)
            .Skip(Paging.Offset(page, pageSize)).Take(pageSize)
            .ToListAsync(cancellationToken);

        return new PagedResult<Score>(items, page, pageSize, total);
    }

    public void Add(Score score) => context.Scores.Add(score);

    public Task UpsertLeaderboardEntryAsync(Score score, long rankKey, CancellationToken cancellationToken) =>
        // One statement, atomic under concurrency: the row lock taken by ON CONFLICT serializes competing submissions of
        // the same player. `xmax = 0` identifies a fresh insert (first score of the player) to bump the player counter.
        context.Database.ExecuteSqlAsync(
            $"""
            WITH upsert AS (
                INSERT INTO leaderboard_entries (game_id, player_id, best_score, rank_key, score_id, achieved_at, submissions_count)
                VALUES ({score.GameId}, {score.PlayerId}, {score.Value}, {rankKey}, {score.Id}, {score.SubmittedAt}, 1)
                ON CONFLICT (game_id, player_id) DO UPDATE SET
                    submissions_count = leaderboard_entries.submissions_count + 1,
                    best_score  = CASE WHEN EXCLUDED.rank_key < leaderboard_entries.rank_key THEN EXCLUDED.best_score  ELSE leaderboard_entries.best_score  END,
                    score_id    = CASE WHEN EXCLUDED.rank_key < leaderboard_entries.rank_key THEN EXCLUDED.score_id    ELSE leaderboard_entries.score_id    END,
                    achieved_at = CASE WHEN EXCLUDED.rank_key < leaderboard_entries.rank_key THEN EXCLUDED.achieved_at ELSE leaderboard_entries.achieved_at END,
                    rank_key    = LEAST(EXCLUDED.rank_key, leaderboard_entries.rank_key)
                RETURNING (xmax = 0) AS inserted
            )
            INSERT INTO leaderboard_stats (game_id, player_count)
            SELECT {score.GameId}, 1 FROM upsert WHERE inserted
            ON CONFLICT (game_id) DO UPDATE SET player_count = leaderboard_stats.player_count + 1
            """,
            cancellationToken);

    public async Task RecalculateLeaderboardEntryAsync(Game game, Guid playerId, CancellationToken cancellationToken)
    {
        var direction = game.ScoreOrder == ScoreOrder.LowerIsBetter ? 1 : -1; // rank_key = value * direction
        await context.Database.ExecuteSqlAsync(
            $"DELETE FROM leaderboard_entries WHERE game_id = {game.Id} AND player_id = {playerId}", cancellationToken);
        await context.Database.ExecuteSqlAsync(
            $"""
            INSERT INTO leaderboard_entries (game_id, player_id, best_score, rank_key, score_id, achieved_at, submissions_count)
            SELECT s.game_id, s.player_id, s.value, s.value * {direction}, s.id, s.submitted_at,
                   (SELECT COUNT(*) FROM scores c WHERE c.game_id = s.game_id AND c.player_id = s.player_id AND c.status = 0)
            FROM scores s
            WHERE s.game_id = {game.Id} AND s.player_id = {playerId} AND s.status = 0
            ORDER BY s.value * {direction} ASC, s.submitted_at ASC
            LIMIT 1
            """,
            cancellationToken);
        await context.Database.ExecuteSqlAsync(
            $"""
            INSERT INTO leaderboard_stats (game_id, player_count)
            VALUES ({game.Id}, (SELECT COUNT(*) FROM leaderboard_entries WHERE game_id = {game.Id}))
            ON CONFLICT (game_id) DO UPDATE SET player_count = EXCLUDED.player_count
            """,
            cancellationToken);
    }
}
