namespace Leaderboard.Domain.Common;

/// <summary>Category of an expected failure. The API layer maps each value to one HTTP status code.</summary>
public enum ErrorType
{
    Validation,
    Unauthorized,
    Forbidden,
    NotFound,
    Conflict,
    PreconditionFailed,
    BusinessRule,
    TooManyRequests,
}
