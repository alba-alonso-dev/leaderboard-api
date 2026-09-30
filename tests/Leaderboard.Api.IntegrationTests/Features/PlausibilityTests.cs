using System.Net;
using Leaderboard.Api.IntegrationTests.Infrastructure;
using Leaderboard.Application.Common;
using Leaderboard.Application.Leaderboards;
using Leaderboard.Application.Scores;
using Leaderboard.Domain.Scores;

namespace Leaderboard.Api.IntegrationTests.Features;

/// <summary>Host with the RF-32 plausibility heuristic enabled (10x improvement threshold).</summary>
public sealed class PlausibilityApiFactory : ApiFactory
{
    protected override IReadOnlyDictionary<string, string?> Overrides => new Dictionary<string, string?>
    {
        ["Scores:MaxImprovementFactor"] = "10",
    };
}

public sealed class PlausibilityTests(PlausibilityApiFactory factory) : IClassFixture<PlausibilityApiFactory>, IAsyncLifetime
{
    private readonly ApiDriver _api = new(factory.CreateClient());

    public async ValueTask InitializeAsync() => await factory.ResetDatabaseAsync();

    public ValueTask DisposeAsync()
    {
        _api.Dispose();
        return ValueTask.CompletedTask;
    }

    private async Task<string> AdminTokenAsync() =>
        (await _api.LoginAsync(ApiFactory.AdminEmail, ApiFactory.AdminPassword)).AccessToken;

    [Fact]
    public async Task ImplausibleJump_IsHeldForReview_UntilAnAdminApprovesIt()
    {
        var game = await _api.CreateGameAsync();
        var ana = await _api.RegisterAsync();
        await _api.SubmitAcceptedAsync(game, ana.Id, 100);

        var suspicious = await _api.SubmitScoreAsync(game.Id, game.ApiKey, ana.Id, 5_000);

        suspicious.StatusCode.ShouldBe(HttpStatusCode.Accepted);
        var held = await suspicious.ReadAsync<SubmitScoreResponse>();
        held.Status.ShouldBe(ScoreStatus.PendingReview);
        held.BestScore.ShouldBe(100);
        (await _api.GetAsync<PlayerRankResponse>($"/api/v1/games/{game.Id}/leaderboard/players/{ana.Id}")).Score.ShouldBe(100);

        var admin = await AdminTokenAsync();
        var queue = await _api.GetAsync<PagedResult<ModerationScoreResponse>>($"/api/v1/admin/scores?gameId={game.Id}", admin);
        queue.Items.Single().ScoreId.ShouldBe(held.ScoreId);

        var approve = await _api.SendAsync(HttpMethod.Post, $"/api/v1/admin/scores/{held.ScoreId}/approve", admin);
        approve.StatusCode.ShouldBe(HttpStatusCode.NoContent);

        (await _api.GetAsync<PlayerRankResponse>($"/api/v1/games/{game.Id}/leaderboard/players/{ana.Id}")).Score.ShouldBe(5_000);
        (await _api.GetAsync<PagedResult<ModerationScoreResponse>>($"/api/v1/admin/scores?gameId={game.Id}", admin)).TotalCount.ShouldBe(0);
    }

    [Fact]
    public async Task Approve_ScoreThatIsNotPending_ReturnsConflict()
    {
        var game = await _api.CreateGameAsync();
        var accepted = await _api.SubmitAcceptedAsync(game, game.Owner.Id, 10);

        var response = await _api.SendAsync(HttpMethod.Post, $"/api/v1/admin/scores/{accepted.ScoreId}/approve", await AdminTokenAsync());

        await response.ShouldBeProblemAsync(HttpStatusCode.Conflict, "score.not_pending");
    }

    [Fact]
    public async Task FirstScoreAndReasonableImprovements_AreAcceptedDirectly()
    {
        var game = await _api.CreateGameAsync();
        var ana = await _api.RegisterAsync();

        (await _api.SubmitAcceptedAsync(game, ana.Id, 1_000_000)).Status.ShouldBe(ScoreStatus.Accepted); // no baseline yet
        (await _api.SubmitAcceptedAsync(game, ana.Id, 9_000_000)).Status.ShouldBe(ScoreStatus.Accepted); // 9x
    }

    [Fact]
    public async Task ReplayOfHeldScore_ReturnsOriginalOutcome()
    {
        var game = await _api.CreateGameAsync();
        await _api.SubmitAcceptedAsync(game, game.Owner.Id, 10);
        const string nonce = "d1946ac92492d2347c6235b4d2611184";

        (await _api.SubmitScoreAsync(game.Id, game.ApiKey, game.Owner.Id, 999, nonce)).StatusCode.ShouldBe(HttpStatusCode.Accepted);
        var replay = await _api.SubmitScoreAsync(game.Id, game.ApiKey, game.Owner.Id, 999, nonce);

        replay.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await replay.ReadAsync<SubmitScoreResponse>()).Status.ShouldBe(ScoreStatus.PendingReview);
    }

    [Fact]
    public async Task ModerationQueue_RequiresAdmin()
    {
        var player = await _api.RegisterAsync();

        await (await _api.SendAsync(HttpMethod.Get, "/api/v1/admin/scores", player.AccessToken))
            .ShouldBeProblemAsync(HttpStatusCode.Forbidden, "auth.forbidden");
    }
}
