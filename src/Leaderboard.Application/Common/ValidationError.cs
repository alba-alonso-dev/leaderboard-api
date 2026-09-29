using Leaderboard.Domain.Common;

namespace Leaderboard.Application.Common;

/// <summary>Input validation failure with the messages of each invalid field (camelCase keys).</summary>
public sealed record ValidationError(IReadOnlyDictionary<string, string[]> Errors)
    : Error("validation.failed", "One or more validation errors occurred.", ErrorType.Validation);
