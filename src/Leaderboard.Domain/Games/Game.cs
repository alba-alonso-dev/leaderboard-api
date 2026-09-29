using Leaderboard.Domain.Common;

namespace Leaderboard.Domain.Games;

/// <summary>A game registered by a player (its owner). Receives scores from its game servers.</summary>
public sealed class Game
{
    public const int NameMaxLength = 100;
    public const int SlugMaxLength = 64;
    public const int DescriptionMaxLength = 1000;
    public const int MaxActiveApiKeys = 5;

    private Game() { } // EF Core

    private Game(Guid id, Guid ownerId, string slug, DateTimeOffset createdAt)
    {
        Id = id;
        OwnerId = ownerId;
        Slug = slug;
        CreatedAt = createdAt;
    }

    public Guid Id { get; private set; }

    public Guid OwnerId { get; private set; }

    public string Slug { get; private set; } = null!;

    public string Name { get; private set; } = null!;

    public string? Description { get; private set; }

    public ScoreOrder ScoreOrder { get; private set; }

    public long? MinScore { get; private set; }

    public long? MaxScore { get; private set; }

    public bool IsArchived { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    /// <summary>Optimistic concurrency token (mapped to PostgreSQL <c>xmin</c>).</summary>
    public uint Version { get; private set; }

    public static Game Create(
        Guid ownerId, string slug, string name, string? description, ScoreOrder scoreOrder, long? minScore, long? maxScore, DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(slug);
        var game = new Game(Guid.CreateVersion7(now), ownerId, slug.Trim().ToLowerInvariant(), now) { ScoreOrder = scoreOrder };
        game.Update(name, description, minScore, maxScore);
        return game;
    }

    /// <summary>
    /// Updates editable metadata. <see cref="ScoreOrder"/> is immutable: changing it would invalidate every stored sort key.
    /// </summary>
    public void Update(string name, string? description, long? minScore, long? maxScore)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        if (minScore is not null && maxScore is not null && minScore > maxScore)
        {
            throw new DomainException($"minScore ({minScore}) cannot be greater than maxScore ({maxScore}).");
        }

        Name = name.Trim();
        Description = string.IsNullOrWhiteSpace(description) ? null : description.Trim();
        MinScore = minScore;
        MaxScore = maxScore;
    }

    public void Archive() => IsArchived = true;

    public bool IsOwnedBy(Guid playerId) => OwnerId == playerId;

    /// <summary>Checks that the game accepts scores and that <paramref name="value"/> is within the configured range.</summary>
    public Result EnsureAcceptsScore(long value)
    {
        if (IsArchived)
        {
            return GameErrors.Archived;
        }

        if (MinScore is { } min && value < min)
        {
            return GameErrors.ScoreOutOfRange(value, MinScore, MaxScore);
        }

        if (MaxScore is { } max && value > max)
        {
            return GameErrors.ScoreOutOfRange(value, MinScore, MaxScore);
        }

        return Result.Success();
    }

    /// <summary>
    /// Normalizes a score so that "lower rank key = better" regardless of <see cref="ScoreOrder"/>.
    /// Ranking order is then (rank_key, achieved_at, player_id) ascending: one all-ascending index serves both
    /// orders and supports row-value keyset comparisons.
    /// </summary>
    public long ToRankKey(long value) => ScoreOrder == ScoreOrder.LowerIsBetter ? value : checked(-value);

    /// <summary>Whether <paramref name="candidate"/> beats <paramref name="current"/> for this game.</summary>
    public bool IsBetter(long candidate, long current) => ToRankKey(candidate) < ToRankKey(current);
}
