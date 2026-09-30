using Leaderboard.Application.Auth;
using Leaderboard.Application.Games;
using Leaderboard.Application.Leaderboards;
using Leaderboard.Application.Players;
using Leaderboard.Application.Scores;
using Leaderboard.Domain.Games;

namespace Leaderboard.Application.UnitTests.Validation;

public sealed class ValidatorTests
{
    [Theory]
    [InlineData("ana_dev", "ana@example.com", "Sup3rSecret1", true)]
    [InlineData("ab", "ana@example.com", "Sup3rSecret1", false)]
    [InlineData("ana dev", "ana@example.com", "Sup3rSecret1", false)]
    [InlineData("ana_dev", "not-an-email", "Sup3rSecret1", false)]
    [InlineData("ana_dev", "ana@example.com", "short1", false)]
    [InlineData("ana_dev", "ana@example.com", "onlyletterslong", false)]
    [InlineData("ana_dev", "ana@example.com", "1234567890", false)]
    public void RegisterPlayer(string username, string email, string password, bool valid) =>
        new RegisterPlayerCommandValidator().Validate(new RegisterPlayerCommand(username, email, password)).IsValid.ShouldBe(valid);

    [Theory]
    [InlineData("space-blaster", true)]
    [InlineData("game2", true)]
    [InlineData("Space-Blaster", false)]
    [InlineData("space--blaster", false)]
    [InlineData("-space", false)]
    [InlineData("ab", false)]
    public void CreateGame_Slug(string slug, bool valid) =>
        new CreateGameCommandValidator()
            .Validate(new CreateGameCommand(Guid.NewGuid(), "Name", slug, null, ScoreOrder.HigherIsBetter, null, null))
            .IsValid.ShouldBe(valid);

    [Fact]
    public void CreateGame_MinGreaterThanMax_IsInvalid()
    {
        var result = new CreateGameCommandValidator()
            .Validate(new CreateGameCommand(Guid.NewGuid(), "Name", "slug", null, ScoreOrder.HigherIsBetter, 10, 5));

        result.Errors.ShouldContain(e => e.PropertyName == "minScore");
    }

    [Theory]
    [InlineData(null, true)]
    [InlineData("""{"level":3}""", true)]
    [InlineData("[1,2]", false)]
    [InlineData("42", false)]
    [InlineData("{not json", false)]
    public void SubmitScore_Metadata(string? metadata, bool valid) =>
        new SubmitScoreCommandValidator()
            .Validate(new SubmitScoreCommand(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), 10, "0123456789abcdef", metadata))
            .IsValid.ShouldBe(valid);

    [Theory]
    [InlineData("short", false)]
    [InlineData("0123456789abcdef", true)]
    public void SubmitScore_Nonce(string nonce, bool valid) =>
        new SubmitScoreCommandValidator()
            .Validate(new SubmitScoreCommand(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), 10, nonce, null))
            .IsValid.ShouldBe(valid);

    [Theory]
    [InlineData(0, false)]
    [InlineData(1, true)]
    [InlineData(100, true)]
    [InlineData(101, false)]
    public void GetTop_N(int n, bool valid) =>
        new GetTopQueryValidator().Validate(new GetTopQuery(Guid.NewGuid(), n)).IsValid.ShouldBe(valid);

    [Theory]
    [InlineData(0, false)]
    [InlineData(25, true)]
    [InlineData(26, false)]
    public void GetAround_Range(int range, bool valid) =>
        new GetAroundPlayerQueryValidator().Validate(new GetAroundPlayerQuery(Guid.NewGuid(), Guid.NewGuid(), range)).IsValid.ShouldBe(valid);

    [Theory]
    [InlineData(1, 20, true)]
    [InlineData(0, 20, false)]
    [InlineData(1, 0, false)]
    [InlineData(1, 101, false)]
    public void Paging(int page, int pageSize, bool valid)
    {
        var result = new GetLeaderboardPageQueryValidator().Validate(new GetLeaderboardPageQuery(Guid.NewGuid(), page, pageSize));

        result.IsValid.ShouldBe(valid);
        if (!valid)
        {
            result.Errors.ShouldAllBe(e => e.PropertyName == "page" || e.PropertyName == "pageSize");
        }
    }

    [Fact]
    public void GetMyScores_UsesSharedPagingRules() =>
        new GetMyScoresQueryValidator().Validate(new GetMyScoresQuery(Guid.NewGuid(), null, 1, 500)).IsValid.ShouldBeFalse();
}
