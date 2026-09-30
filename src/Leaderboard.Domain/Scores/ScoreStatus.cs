namespace Leaderboard.Domain.Scores;

public enum ScoreStatus
{
    /// <summary>Counts for the leaderboard.</summary>
    Accepted = 0,

    /// <summary>Held for moderation; does not count.</summary>
    PendingReview = 1,

    /// <summary>Invalidated by an administrator; does not count.</summary>
    Rejected = 2,
}
