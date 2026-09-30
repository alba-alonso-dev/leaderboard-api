namespace Leaderboard.Domain.Players;

/// <summary>A registered person. May own games and appear in leaderboards.</summary>
public sealed class Player
{
    public const int UsernameMinLength = 3;
    public const int UsernameMaxLength = 32;
    public const int EmailMaxLength = 254;

    private Player() { } // EF Core

    private Player(Guid id, string username, string email, string role, DateTimeOffset createdAt)
    {
        Id = id;
        Username = username;
        Email = email;
        Role = role;
        CreatedAt = createdAt;
    }

    public Guid Id { get; private set; }

    public string Username { get; private set; } = null!;

    public string Email { get; private set; } = null!;

    public string PasswordHash { get; private set; } = null!;

    public string Role { get; private set; } = null!;

    public DateTimeOffset CreatedAt { get; private set; }

    public bool IsAdmin => Role == PlayerRoles.Admin;

    public static Player Register(string username, string email, DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(username);
        ArgumentException.ThrowIfNullOrWhiteSpace(email);

        return new Player(Guid.CreateVersion7(now), username.Trim(), email.Trim().ToLowerInvariant(), PlayerRoles.Player, now);
    }

    /// <summary>The hash is produced outside the domain (Application port) and attached here.</summary>
    public void SetPasswordHash(string passwordHash)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(passwordHash);
        PasswordHash = passwordHash;
    }

    public void PromoteToAdmin() => Role = PlayerRoles.Admin;
}
