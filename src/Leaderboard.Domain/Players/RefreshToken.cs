namespace Leaderboard.Domain.Players;

/// <summary>
/// Opaque, rotating refresh token. Only a SHA-256 hash is stored. All tokens issued from the same login share a
/// <see cref="FamilyId"/> so that reuse of a rotated token can revoke the whole chain (theft detection).
/// </summary>
public sealed class RefreshToken
{
    private RefreshToken() { } // EF Core

    private RefreshToken(Guid id, Guid playerId, byte[] tokenHash, Guid familyId, DateTimeOffset createdAt, DateTimeOffset expiresAt)
    {
        Id = id;
        PlayerId = playerId;
        TokenHash = tokenHash;
        FamilyId = familyId;
        CreatedAt = createdAt;
        ExpiresAt = expiresAt;
    }

    public Guid Id { get; private set; }

    public Guid PlayerId { get; private set; }

    public byte[] TokenHash { get; private set; } = null!;

    public Guid FamilyId { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset ExpiresAt { get; private set; }

    public DateTimeOffset? RevokedAt { get; private set; }

    public Guid? ReplacedById { get; private set; }

    public static RefreshToken Issue(Guid playerId, byte[] tokenHash, DateTimeOffset now, TimeSpan lifetime, Guid? familyId = null) =>
        new(Guid.CreateVersion7(now), playerId, tokenHash, familyId ?? Guid.CreateVersion7(now), now, now.Add(lifetime));

    public bool IsActive(DateTimeOffset now) => RevokedAt is null && now < ExpiresAt;

    /// <summary>A token that was rotated (replaced) and is presented again indicates it was stolen.</summary>
    public bool WasRotated => ReplacedById is not null;

    public void Revoke(DateTimeOffset now, Guid? replacedById = null)
    {
        RevokedAt ??= now;
        ReplacedById ??= replacedById;
    }
}
