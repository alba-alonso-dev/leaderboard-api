namespace Leaderboard.Domain.Common;

/// <summary>Thrown when a domain invariant is violated by a programming error (never for user input).</summary>
public sealed class DomainException : Exception
{
    public DomainException() { }

    public DomainException(string message) : base(message) { }

    public DomainException(string message, Exception innerException) : base(message, innerException) { }
}
