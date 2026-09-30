using Leaderboard.Application.Common;
using Leaderboard.Domain.Games;
using Leaderboard.Domain.Players;
using Leaderboard.Domain.Scores;

namespace Leaderboard.Application.Abstractions.Persistence;

public interface IPlayerRepository
{
    Task<Player?> GetByIdAsync(Guid id, CancellationToken cancellationToken);

    Task<Player?> GetByEmailAsync(string email, CancellationToken cancellationToken);

    Task<bool> ExistsAsync(Guid id, CancellationToken cancellationToken);

    Task<bool> EmailExistsAsync(string email, CancellationToken cancellationToken);

    Task<bool> UsernameExistsAsync(string username, CancellationToken cancellationToken);

    void Add(Player player);
}

public interface IRefreshTokenRepository
{
    Task<RefreshToken?> GetByHashAsync(byte[] tokenHash, CancellationToken cancellationToken);

    Task RevokeFamilyAsync(Guid familyId, DateTimeOffset now, CancellationToken cancellationToken);

    void Add(RefreshToken token);
}

public interface IGameRepository
{
    Task<Game?> GetByIdAsync(Guid id, CancellationToken cancellationToken);

    Task<bool> ExistsAsync(Guid id, CancellationToken cancellationToken);

    Task<bool> SlugExistsAsync(string slug, CancellationToken cancellationToken);

    Task<PagedResult<Game>> ListActiveAsync(string? search, int page, int pageSize, CancellationToken cancellationToken);

    void Add(Game game);
}

public interface IGameApiKeyRepository
{
    Task<GameApiKey?> GetByIdAsync(Guid gameId, Guid apiKeyId, CancellationToken cancellationToken);

    Task<GameApiKey?> GetActiveByKeyIdAsync(string keyId, CancellationToken cancellationToken);

    Task<IReadOnlyList<GameApiKey>> ListByGameAsync(Guid gameId, CancellationToken cancellationToken);

    Task<int> CountActiveAsync(Guid gameId, CancellationToken cancellationToken);

    void Add(GameApiKey apiKey);
}

public interface IScoreRepository
{
    Task<Score?> GetByIdAsync(Guid id, CancellationToken cancellationToken);

    Task<Score?> GetByNonceAsync(Guid apiKeyId, string nonce, CancellationToken cancellationToken);

    Task<int> CountSinceAsync(Guid gameId, Guid playerId, DateTimeOffset since, CancellationToken cancellationToken);

    /// <summary>Current best score of the player in the game, or <c>null</c> if they have no ranked score yet.</summary>
    Task<long?> GetBestScoreAsync(Guid gameId, Guid playerId, CancellationToken cancellationToken);

    Task<PagedResult<Score>> ListByStatusAsync(
        ScoreStatus status, Guid? gameId, int page, int pageSize, CancellationToken cancellationToken);

    Task<PagedResult<Score>> ListByPlayerAsync(Guid playerId, Guid? gameId, int page, int pageSize, CancellationToken cancellationToken);

    void Add(Score score);

    /// <summary>
    /// Atomically inserts or improves the player's leaderboard entry (INSERT … ON CONFLICT DO UPDATE) and keeps the
    /// game's player counter in sync.
    /// </summary>
    Task UpsertLeaderboardEntryAsync(Score score, long rankKey, CancellationToken cancellationToken);

    /// <summary>Rebuilds the player's entry from the accepted score history (after moderation).</summary>
    Task RecalculateLeaderboardEntryAsync(Game game, Guid playerId, CancellationToken cancellationToken);
}
