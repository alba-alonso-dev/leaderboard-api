using Leaderboard.Domain.Common;
using Leaderboard.Domain.Games;

namespace Leaderboard.Domain.UnitTests.Games;

public sealed class GameTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 2, 10, 0, 0, TimeSpan.Zero);

    private static Game CreateGame(ScoreOrder order = ScoreOrder.HigherIsBetter, long? min = null, long? max = null) =>
        Game.Create(Guid.CreateVersion7(), "space-blaster", "Space Blaster", null, order, min, max, Now);

    [Fact]
    public void Create_NormalizesSlugAndTrimsName()
    {
        var game = Game.Create(Guid.CreateVersion7(), "  Space-Blaster ", "  Space Blaster  ", "  ", ScoreOrder.HigherIsBetter, null, null, Now);

        game.Slug.ShouldBe("space-blaster");
        game.Name.ShouldBe("Space Blaster");
        game.Description.ShouldBeNull();
        game.IsArchived.ShouldBeFalse();
        game.CreatedAt.ShouldBe(Now);
    }

    [Fact]
    public void Create_WhenMinGreaterThanMax_Throws() =>
        Should.Throw<DomainException>(() => CreateGame(min: 10, max: 5));

    [Theory]
    [InlineData(ScoreOrder.HigherIsBetter, 500, -500)]
    [InlineData(ScoreOrder.LowerIsBetter, 500, 500)]
    public void ToRankKey_NormalizesSoThatLowerIsAlwaysBetter(ScoreOrder order, long value, long expected) =>
        CreateGame(order).ToRankKey(value).ShouldBe(expected);

    [Theory]
    [InlineData(ScoreOrder.HigherIsBetter, 200, 100, true)]
    [InlineData(ScoreOrder.HigherIsBetter, 100, 200, false)]
    [InlineData(ScoreOrder.HigherIsBetter, 100, 100, false)]
    [InlineData(ScoreOrder.LowerIsBetter, 59_000, 61_000, true)]
    [InlineData(ScoreOrder.LowerIsBetter, 61_000, 59_000, false)]
    public void IsBetter_RespectsScoreOrder(ScoreOrder order, long candidate, long current, bool expected) =>
        CreateGame(order).IsBetter(candidate, current).ShouldBe(expected);

    [Theory]
    [InlineData(0)]
    [InlineData(500)]
    [InlineData(1000)]
    public void EnsureAcceptsScore_WhenWithinRange_Succeeds(long value) =>
        CreateGame(min: 0, max: 1000).EnsureAcceptsScore(value).IsSuccess.ShouldBeTrue();

    [Theory]
    [InlineData(-1)]
    [InlineData(1001)]
    public void EnsureAcceptsScore_WhenOutOfRange_ReturnsOutOfRange(long value)
    {
        var result = CreateGame(min: 0, max: 1000).EnsureAcceptsScore(value);

        result.IsFailure.ShouldBeTrue();
        result.Error!.Code.ShouldBe("score.out_of_range");
        result.Error.Type.ShouldBe(ErrorType.BusinessRule);
    }

    [Fact]
    public void EnsureAcceptsScore_WhenNoRangeConfigured_AcceptsAnyValue() =>
        CreateGame().EnsureAcceptsScore(long.MaxValue).IsSuccess.ShouldBeTrue();

    [Fact]
    public void EnsureAcceptsScore_WhenArchived_ReturnsArchived()
    {
        var game = CreateGame();
        game.Archive();

        game.EnsureAcceptsScore(1).Error.ShouldBe(GameErrors.Archived);
    }

    [Fact]
    public void Update_ChangesMetadataButKeepsScoreOrder()
    {
        var game = CreateGame(ScoreOrder.LowerIsBetter);

        game.Update("Renamed", "New description", 1, 99);

        game.Name.ShouldBe("Renamed");
        game.Description.ShouldBe("New description");
        game.MinScore.ShouldBe(1);
        game.MaxScore.ShouldBe(99);
        game.ScoreOrder.ShouldBe(ScoreOrder.LowerIsBetter);
    }

    [Fact]
    public void IsOwnedBy_OnlyForOwner()
    {
        var owner = Guid.CreateVersion7();
        var game = Game.Create(owner, "slug", "Name", null, ScoreOrder.HigherIsBetter, null, null, Now);

        game.IsOwnedBy(owner).ShouldBeTrue();
        game.IsOwnedBy(Guid.CreateVersion7()).ShouldBeFalse();
    }
}
