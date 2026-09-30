namespace Leaderboard.Application.Abstractions.Persistence;

public interface IUnitOfWork
{
    /// <summary>Persists tracked changes. Throws <see cref="UniqueConstraintException"/> or <see cref="ConcurrencyConflictException"/>.</summary>
    Task SaveChangesAsync(CancellationToken cancellationToken);

    /// <summary>Runs <paramref name="operation"/> in a database transaction, retrying the whole block on transient failures.</summary>
    Task<T> ExecuteInTransactionAsync<T>(Func<CancellationToken, Task<T>> operation, CancellationToken cancellationToken);
}

/// <summary>A unique index was violated (usually a race between two requests). Carries the constraint name.</summary>
public sealed class UniqueConstraintException : Exception
{
    public UniqueConstraintException() { }

    public UniqueConstraintException(string message) : base(message) { }

    public UniqueConstraintException(string constraintName, Exception innerException)
        : base($"Unique constraint '{constraintName}' was violated.", innerException) => ConstraintName = constraintName;

    public string ConstraintName { get; } = string.Empty;
}

/// <summary>An optimistic concurrency token did not match (the row changed since it was read).</summary>
public sealed class ConcurrencyConflictException : Exception
{
    public ConcurrencyConflictException() { }

    public ConcurrencyConflictException(string message) : base(message) { }

    public ConcurrencyConflictException(string message, Exception innerException) : base(message, innerException) { }
}

/// <summary>Names of unique indexes that handlers translate into domain errors.</summary>
public static class UniqueConstraints
{
    public const string PlayerEmail = "ux_players_email";
    public const string PlayerUsername = "ux_players_username";
    public const string GameSlug = "ux_games_slug";
    public const string ScoreNonce = "ux_scores_api_key_id_nonce";
}
