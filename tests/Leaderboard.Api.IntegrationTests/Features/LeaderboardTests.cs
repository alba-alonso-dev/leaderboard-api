using System.Net;
using Leaderboard.Api.IntegrationTests.Infrastructure;
using Leaderboard.Application.Common;
using Leaderboard.Application.Leaderboards;
using Leaderboard.Domain.Games;

namespace Leaderboard.Api.IntegrationTests.Features;

public sealed class LeaderboardTests(ApiFactory factory) : IntegrationTest(factory)
{
    /// <summary>Creates players and submits their scores in order (earlier submission wins ties).</summary>
    private async Task<(TestGame Game, Dictionary<string, TestPlayer> Players)> SeedAsync(
        ScoreOrder order, params (string Name, long Score)[] entries)
    {
        var game = await Api.CreateGameAsync(order: order);
        var players = new Dictionary<string, TestPlayer>();
        foreach (var (name, score) in entries)
        {
            if (!players.TryGetValue(name, out var player))
            {
                player = await Api.RegisterAsync(name + "_" + Guid.NewGuid().ToString("N")[..6]);
                players[name] = player;
            }

            await Api.SubmitAcceptedAsync(game, player.Id, score);
        }

        return (game, players);
    }

    [Fact]
    public async Task Top_OrdersByBestScoreAndBreaksTiesByAchievementTime()
    {
        var (game, players) = await SeedAsync(ScoreOrder.HigherIsBetter, ("ana", 900), ("bob", 900), ("carla", 1200), ("dani", 100));

        var top = await Api.GetAsync<List<LeaderboardEntryResponse>>($"/api/v1/games/{game.Id}/leaderboard/top?n=3");

        top.Select(e => e.PlayerId).ShouldBe([players["carla"].Id, players["ana"].Id, players["bob"].Id]);
        top.Select(e => e.Rank).ShouldBe([1L, 2L, 3L]);
    }

    [Fact]
    public async Task Top_ForLowerIsBetterGame_PutsLowestFirst()
    {
        var (game, players) = await SeedAsync(ScoreOrder.LowerIsBetter, ("ana", 61_000), ("bob", 59_000), ("ana", 58_000));

        var top = await Api.GetAsync<List<LeaderboardEntryResponse>>($"/api/v1/games/{game.Id}/leaderboard/top?n=2");

        top[0].PlayerId.ShouldBe(players["ana"].Id);
        top[0].Score.ShouldBe(58_000);
        top[1].PlayerId.ShouldBe(players["bob"].Id);
    }

    [Theory]
    [InlineData("top?n=0", "n")]
    [InlineData("top?n=101", "n")]
    [InlineData("?page=0", "page")]
    [InlineData("?pageSize=500", "pageSize")]
    public async Task InvalidParameters_ReturnValidationProblem(string query, string field)
    {
        var game = await Api.CreateGameAsync();

        var problem = await (await Api.Client.GetAsync($"/api/v1/games/{game.Id}/leaderboard/{query}"))
            .ShouldBeProblemAsync(HttpStatusCode.BadRequest, "validation.failed");

        problem.GetProperty("errors").TryGetProperty(field, out _).ShouldBeTrue();
    }

    [Fact]
    public async Task Page_ReturnsRanksContinuingAcrossPages()
    {
        var (game, _) = await SeedAsync(ScoreOrder.HigherIsBetter, ("a", 50), ("b", 40), ("c", 30), ("d", 20), ("e", 10));

        var page = await Api.GetAsync<PagedResult<LeaderboardEntryResponse>>($"/api/v1/games/{game.Id}/leaderboard?page=2&pageSize=2");

        page.TotalCount.ShouldBe(5);
        page.TotalPages.ShouldBe(3);
        page.HasPreviousPage.ShouldBeTrue();
        page.HasNextPage.ShouldBeTrue();
        page.Items.Select(i => (i.Rank, i.Score)).ShouldBe([(3L, 30L), (4L, 20L)]);
    }

    [Fact]
    public async Task PlayerRank_ReturnsAbsolutePositionAndPercentile()
    {
        var (game, players) = await SeedAsync(ScoreOrder.HigherIsBetter, ("a", 50), ("b", 40), ("c", 30), ("d", 20));

        var rank = await Api.GetAsync<PlayerRankResponse>($"/api/v1/games/{game.Id}/leaderboard/players/{players["b"].Id}");

        rank.Rank.ShouldBe(2);
        rank.Score.ShouldBe(40);
        rank.TotalPlayers.ShouldBe(4);
        rank.Percentile.ShouldBe(75.0);
    }

    [Fact]
    public async Task Around_ReturnsNeighboursWithTargetInTheMiddle()
    {
        var (game, players) = await SeedAsync(ScoreOrder.HigherIsBetter, ("a", 70), ("b", 60), ("c", 50), ("d", 40), ("e", 30), ("f", 20), ("g", 10));

        var around = await Api.GetAsync<AroundPlayerResponse>($"/api/v1/games/{game.Id}/leaderboard/players/{players["d"].Id}/around?range=2");

        around.Target.Rank.ShouldBe(4);
        around.Entries.Select(e => e.Rank).ShouldBe([2L, 3L, 4L, 5L, 6L]);
        around.Entries.Select(e => e.Score).ShouldBe([60L, 50L, 40L, 30L, 20L]);
        around.Entries.Single(e => e.IsTarget).PlayerId.ShouldBe(players["d"].Id);
    }

    [Fact]
    public async Task Around_AtTheTop_IsTruncated()
    {
        var (game, players) = await SeedAsync(ScoreOrder.HigherIsBetter, ("a", 70), ("b", 60), ("c", 50), ("d", 40));

        var around = await Api.GetAsync<AroundPlayerResponse>($"/api/v1/games/{game.Id}/leaderboard/players/{players["a"].Id}/around?range=2");

        around.Entries.Select(e => e.Rank).ShouldBe([1L, 2L, 3L]);
        around.Entries[0].IsTarget.ShouldBeTrue();
    }

    [Fact]
    public async Task Around_WithTiesAtTheBottom_UsesTieBreakConsistently()
    {
        var (game, players) = await SeedAsync(ScoreOrder.HigherIsBetter, ("a", 10), ("b", 10), ("c", 10));

        var around = await Api.GetAsync<AroundPlayerResponse>($"/api/v1/games/{game.Id}/leaderboard/players/{players["c"].Id}/around?range=5");

        around.Entries.Select(e => e.PlayerId).ShouldBe([players["a"].Id, players["b"].Id, players["c"].Id]);
        around.Target.Rank.ShouldBe(3);
    }

    [Fact]
    public async Task PlayerWithoutScores_ReturnsNotFound()
    {
        var game = await Api.CreateGameAsync();

        await (await Api.Client.GetAsync($"/api/v1/games/{game.Id}/leaderboard/players/{game.Owner.Id}"))
            .ShouldBeProblemAsync(HttpStatusCode.NotFound, "leaderboard.entry_not_found");
    }

    [Fact]
    public async Task UnknownGame_ReturnsNotFound() =>
        await (await Api.Client.GetAsync($"/api/v1/games/{Guid.CreateVersion7()}/leaderboard/top"))
            .ShouldBeProblemAsync(HttpStatusCode.NotFound, "game.not_found");

    [Fact]
    public async Task Me_ReturnsSameAsPublicPosition()
    {
        var (game, players) = await SeedAsync(ScoreOrder.HigherIsBetter, ("a", 70), ("b", 60));

        var me = await Api.GetAsync<PlayerRankResponse>($"/api/v1/games/{game.Id}/leaderboard/me", players["b"].AccessToken);

        me.Rank.ShouldBe(2);
        me.PlayerId.ShouldBe(players["b"].Id);
    }

    [Fact]
    public async Task PublicEntries_DoNotExposeEmails()
    {
        var (game, _) = await SeedAsync(ScoreOrder.HigherIsBetter, ("a", 70));

        var body = await (await Api.Client.GetAsync($"/api/v1/games/{game.Id}/leaderboard")).Content.ReadAsStringAsync();

        body.ShouldNotContain("@");
    }
}
