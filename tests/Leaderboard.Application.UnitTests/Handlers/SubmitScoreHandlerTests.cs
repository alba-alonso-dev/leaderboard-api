using Leaderboard.Application.Abstractions;
using Leaderboard.Application.Abstractions.Persistence;
using Leaderboard.Application.Scores;
using Leaderboard.Domain.Games;
using Leaderboard.Domain.Scores;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;

namespace Leaderboard.Application.UnitTests.Handlers;

public sealed class SubmitScoreHandlerTests
{
    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 11, 2, 10, 0, 0, TimeSpan.Zero));
    private readonly IGameRepository _games = Substitute.For<IGameRepository>();
    private readonly IPlayerRepository _players = Substitute.For<IPlayerRepository>();
    private readonly IScoreRepository _scores = Substitute.For<IScoreRepository>();
    private readonly ILeaderboardReadService _leaderboard = Substitute.For<ILeaderboardReadService>();
    private readonly IUnitOfWork _unitOfWork = Substitute.For<IUnitOfWork>();
    private readonly Game _game;
    private readonly Guid _apiKeyId = Guid.CreateVersion7();
    private readonly Guid _playerId = Guid.CreateVersion7();

    public SubmitScoreHandlerTests()
    {
        _game = Game.Create(Guid.CreateVersion7(), "space", "Space", null, ScoreOrder.HigherIsBetter, 0, 1000, _time.GetUtcNow());
        _games.GetByIdAsync(_game.Id, Arg.Any<CancellationToken>()).Returns(_game);
        _players.ExistsAsync(_playerId, Arg.Any<CancellationToken>()).Returns(true);
        _unitOfWork.ExecuteInTransactionAsync(Arg.Any<Func<CancellationToken, Task<bool>>>(), Arg.Any<CancellationToken>())
            .Returns(ci => ci.Arg<Func<CancellationToken, Task<bool>>>()(CancellationToken.None));
    }

    private SubmitScoreCommandHandler Sut(int perMinute = 10) => new(
        _games, _players, _scores, _leaderboard, _unitOfWork,
        Options.Create(new ScoreSubmissionOptions { MaxPerPlayerPerMinute = perMinute }), _time);

    private SubmitScoreCommand Command(long value = 500, Guid? keyGame = null, string nonce = "0123456789abcdef") =>
        new(_game.Id, _apiKeyId, keyGame ?? _game.Id, _playerId, value, nonce, null);

    [Fact]
    public async Task Submit_WithKeyFromAnotherGame_ReturnsGameMismatch() =>
        (await Sut().HandleAsync(Command(keyGame: Guid.CreateVersion7()), CancellationToken.None)).Error.ShouldBe(ApiKeyErrors.GameMismatch);

    [Fact]
    public async Task Submit_OutOfRange_ReturnsBusinessRuleError() =>
        (await Sut().HandleAsync(Command(value: 5000), CancellationToken.None)).Error!.Code.ShouldBe("score.out_of_range");

    [Fact]
    public async Task Submit_ForUnknownPlayer_ReturnsPlayerNotFound()
    {
        _players.ExistsAsync(_playerId, Arg.Any<CancellationToken>()).Returns(false);

        (await Sut().HandleAsync(Command(), CancellationToken.None)).Error.ShouldBe(ScoreErrors.PlayerNotFound);
    }

    [Fact]
    public async Task Submit_OverPerPlayerLimit_ReturnsTooManyRequests()
    {
        _scores.CountSinceAsync(_game.Id, _playerId, _time.GetUtcNow().AddMinutes(-1), Arg.Any<CancellationToken>()).Returns(3);

        (await Sut(perMinute: 3).HandleAsync(Command(), CancellationToken.None)).Error.ShouldBe(ScoreErrors.PlayerRateLimited);
    }

    [Fact]
    public async Task Submit_Accepted_UpsertsEntryWithNormalizedRankKey()
    {
        Score? stored = null;
        _scores.Add(Arg.Do<Score>(s => stored = s));
        _leaderboard.GetPlayerRankAsync(_game.Id, _playerId, Arg.Any<CancellationToken>())
            .Returns(ci => new RankedRow(new LeaderboardRow(_playerId, "ana", 500, -500, _time.GetUtcNow(), stored!.Id), 1));
        _leaderboard.CountPlayersAsync(_game.Id, Arg.Any<CancellationToken>()).Returns(1);

        var result = await Sut().HandleAsync(Command(), CancellationToken.None);

        result.Value.IsPersonalBest.ShouldBeTrue();
        result.Value.Rank.ShouldBe(1);
        result.Value.IsReplay.ShouldBeFalse();
        await _scores.Received(1).UpsertLeaderboardEntryAsync(stored!, -500, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Submit_ImplausibleJump_IsHeldForReviewAndNotRanked()
    {
        _scores.GetBestScoreAsync(_game.Id, _playerId, Arg.Any<CancellationToken>()).Returns(50L);

        var result = await Sut().HandleAsync(Command(value: 900), CancellationToken.None); // 18x the previous best

        result.Value.Status.ShouldBe(ScoreStatus.PendingReview);
        result.Value.IsPersonalBest.ShouldBeFalse();
        _scores.Received(1).Add(Arg.Is<Score>(s => s.Status == ScoreStatus.PendingReview));
        await _scores.DidNotReceive().UpsertLeaderboardEntryAsync(Arg.Any<Score>(), Arg.Any<long>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Submit_WithUsedNonceAndDifferentPayload_ReturnsNonceReused()
    {
        var previous = Score.Submit(_game.Id, _playerId, _apiKeyId, 100, "0123456789abcdef", null, _time.GetUtcNow());
        _scores.GetByNonceAsync(_apiKeyId, "0123456789abcdef", Arg.Any<CancellationToken>()).Returns(previous);

        var result = await Sut().HandleAsync(Command(value: 200), CancellationToken.None);

        result.Error.ShouldBe(ScoreErrors.NonceReused);
        _scores.DidNotReceive().Add(Arg.Any<Score>());
    }

    [Fact]
    public async Task Submit_WithUsedNonceAndSamePayload_ReplaysOriginalResult()
    {
        var previous = Score.Submit(_game.Id, _playerId, _apiKeyId, 500, "0123456789abcdef", null, _time.GetUtcNow());
        _scores.GetByNonceAsync(_apiKeyId, "0123456789abcdef", Arg.Any<CancellationToken>()).Returns(previous);

        var result = await Sut().HandleAsync(Command(), CancellationToken.None);

        result.Value.IsReplay.ShouldBeTrue();
        result.Value.ScoreId.ShouldBe(previous.Id);
        _scores.DidNotReceive().Add(Arg.Any<Score>());
    }
}
