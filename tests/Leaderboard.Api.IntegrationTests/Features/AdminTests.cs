using System.Net;
using Leaderboard.Api.IntegrationTests.Infrastructure;
using Leaderboard.Application.Leaderboards;

namespace Leaderboard.Api.IntegrationTests.Features;

public sealed class AdminTests(ApiFactory factory) : IntegrationTest(factory)
{
    [Fact]
    public async Task InvalidateScore_RecalculatesEntryFromRemainingHistory()
    {
        var game = await Api.CreateGameAsync();
        var ana = await Api.RegisterAsync();
        await Api.SubmitAcceptedAsync(game, ana.Id, 100);
        var cheated = await Api.SubmitAcceptedAsync(game, ana.Id, 999_999);
        var admin = await Api.LoginAsync(ApiFactory.AdminEmail, ApiFactory.AdminPassword);

        var response = await Api.SendAsync(HttpMethod.Delete, $"/api/v1/admin/scores/{cheated.ScoreId}", admin.AccessToken);

        response.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        var rank = await Api.GetAsync<PlayerRankResponse>($"/api/v1/games/{game.Id}/leaderboard/players/{ana.Id}");
        rank.Score.ShouldBe(100);
    }

    [Fact]
    public async Task InvalidateOnlyScore_RemovesPlayerFromLeaderboard()
    {
        var game = await Api.CreateGameAsync();
        var score = await Api.SubmitAcceptedAsync(game, game.Owner.Id, 50);
        var admin = await Api.LoginAsync(ApiFactory.AdminEmail, ApiFactory.AdminPassword);

        await Api.SendAsync(HttpMethod.Delete, $"/api/v1/admin/scores/{score.ScoreId}", admin.AccessToken);

        (await Api.Client.GetAsync($"/api/v1/games/{game.Id}/leaderboard/players/{game.Owner.Id}")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        var page = await Api.GetAsync<Application.Common.PagedResult<LeaderboardEntryResponse>>($"/api/v1/games/{game.Id}/leaderboard");
        page.TotalCount.ShouldBe(0);
    }

    [Fact]
    public async Task InvalidateScore_AsRegularPlayer_IsForbidden()
    {
        var game = await Api.CreateGameAsync();
        var score = await Api.SubmitAcceptedAsync(game, game.Owner.Id, 50);

        await (await Api.SendAsync(HttpMethod.Delete, $"/api/v1/admin/scores/{score.ScoreId}", game.Owner.AccessToken))
            .ShouldBeProblemAsync(HttpStatusCode.Forbidden, "auth.forbidden");
    }
}
