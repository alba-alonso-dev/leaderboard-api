using Leaderboard.Domain.Common;

namespace Leaderboard.Domain.Scores;

/// <summary>Immutable score event sent by a game server. The full history enables audits and recalculation.</summary>
public sealed class Score
{
    public const int NonceMinLength = 16;
    public const int NonceMaxLength = 64;
    public const int MetadataMaxLength = 2048;

    private Score() { } // EF Core

    private Score(Guid id, Guid gameId, Guid playerId, Guid apiKeyId, long value, string nonce, string? metadata, DateTimeOffset submittedAt)
    {
        Id = id;
        GameId = gameId;
        PlayerId = playerId;
        ApiKeyId = apiKeyId;
        Value = value;
        Nonce = nonce;
        Metadata = metadata;
        SubmittedAt = submittedAt;
        Status = ScoreStatus.Accepted;
    }

    public Guid Id { get; private set; }

    public Guid GameId { get; private set; }

    public Guid PlayerId { get; private set; }

    public Guid ApiKeyId { get; private set; }

    public long Value { get; private set; }

    /// <summary>Client-generated unique value; unique per API key (idempotency and anti-replay).</summary>
    public string Nonce { get; private set; } = null!;

    /// <summary>Raw JSON object with game-specific context (level, duration…).</summary>
    public string? Metadata { get; private set; }

    public ScoreStatus Status { get; private set; }

    public DateTimeOffset SubmittedAt { get; private set; }

    public static Score Submit(Guid gameId, Guid playerId, Guid apiKeyId, long value, string nonce, string? metadata, DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(nonce);
        if (metadata is { Length: > MetadataMaxLength })
        {
            throw new DomainException($"Metadata cannot exceed {MetadataMaxLength} characters.");
        }

        return new Score(Guid.CreateVersion7(now), gameId, playerId, apiKeyId, value, nonce, metadata, now);
    }

    /// <summary>Held for moderation before being persisted: it is stored but does not count for the leaderboard.</summary>
    public void HoldForReview()
    {
        if (Status != ScoreStatus.Accepted)
        {
            throw new DomainException($"Only a new accepted score can be held for review (status: {Status}).");
        }

        Status = ScoreStatus.PendingReview;
    }

    /// <summary>A moderator confirms a held score; it then counts for the leaderboard.</summary>
    public Result Approve()
    {
        if (Status != ScoreStatus.PendingReview)
        {
            return ScoreErrors.NotPending;
        }

        Status = ScoreStatus.Accepted;
        return Result.Success();
    }

    public void Reject() => Status = ScoreStatus.Rejected;
}
