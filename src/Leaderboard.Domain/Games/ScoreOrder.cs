namespace Leaderboard.Domain.Games;

/// <summary>How scores of a game are ranked.</summary>
public enum ScoreOrder
{
    /// <summary>Arcade style: the highest value wins.</summary>
    HigherIsBetter = 0,

    /// <summary>Speedrun style (times, strokes…): the lowest value wins.</summary>
    LowerIsBetter = 1,
}
