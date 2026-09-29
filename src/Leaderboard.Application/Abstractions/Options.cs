using System.ComponentModel.DataAnnotations;

namespace Leaderboard.Application.Abstractions;

public sealed class AuthOptions
{
    public const string SectionName = "Auth";

    [Range(1, 90)]
    public int RefreshTokenLifetimeDays { get; set; } = 7;
}

public sealed class ScoreSubmissionOptions
{
    public const string SectionName = "Scores";

    /// <summary>Accepted scores per player and game per minute. 0 disables the check.</summary>
    [Range(0, 10_000)]
    public int MaxPerPlayerPerMinute { get; set; } = 10;
}

public sealed class ApiKeyOptions
{
    public const string SectionName = "ApiKeys";

    /// <summary>Maximum allowed difference between the request timestamp and server time.</summary>
    [Range(30, 3600)]
    public int SignatureWindowSeconds { get; set; } = 300;
}
