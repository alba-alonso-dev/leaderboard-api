namespace Leaderboard.Domain.Games;

/// <summary>
/// Server credential of a game. <see cref="KeyId"/> is public and travels in every request; the secret never does
/// (requests are HMAC-signed). The secret is stored encrypted because the server must recompute signatures.
/// </summary>
public sealed class GameApiKey
{
    public const string KeyIdPrefix = "lbk_";
    public const int NameMaxLength = 64;

    private GameApiKey() { } // EF Core

    private GameApiKey(Guid id, Guid gameId, string keyId, string name, byte[] secretCiphertext, DateTimeOffset createdAt)
    {
        Id = id;
        GameId = gameId;
        KeyId = keyId;
        Name = name;
        SecretCiphertext = secretCiphertext;
        CreatedAt = createdAt;
    }

    public Guid Id { get; private set; }

    public Guid GameId { get; private set; }

    public string KeyId { get; private set; } = null!;

    public string Name { get; private set; } = null!;

    public byte[] SecretCiphertext { get; private set; } = null!;

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset? LastUsedAt { get; private set; }

    public DateTimeOffset? RevokedAt { get; private set; }

    public bool IsActive => RevokedAt is null;

    public static GameApiKey Issue(Guid gameId, string keyId, string name, byte[] secretCiphertext, DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(keyId);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(secretCiphertext);

        return new GameApiKey(Guid.CreateVersion7(now), gameId, keyId, name.Trim(), secretCiphertext, now);
    }

    public void Revoke(DateTimeOffset now) => RevokedAt ??= now;

    public void MarkUsed(DateTimeOffset now) => LastUsedAt = now;
}
