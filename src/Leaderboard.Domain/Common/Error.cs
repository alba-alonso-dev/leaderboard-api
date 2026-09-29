namespace Leaderboard.Domain.Common;

/// <summary>An expected failure with a stable, machine-readable <see cref="Code"/> (part of the public API contract).</summary>
public record Error(string Code, string Description, ErrorType Type)
{
    public static Error NotFound(string code, string description) => new(code, description, ErrorType.NotFound);

    public static Error Conflict(string code, string description) => new(code, description, ErrorType.Conflict);

    public static Error BusinessRule(string code, string description) => new(code, description, ErrorType.BusinessRule);

    public static Error Unauthorized(string code, string description) => new(code, description, ErrorType.Unauthorized);

    public static Error Forbidden(string code, string description) => new(code, description, ErrorType.Forbidden);
}
