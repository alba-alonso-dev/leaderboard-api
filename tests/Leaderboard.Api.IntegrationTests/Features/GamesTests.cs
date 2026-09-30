using System.Net;
using Leaderboard.Api.IntegrationTests.Infrastructure;
using Leaderboard.Application.Common;
using Leaderboard.Application.Games;
using Leaderboard.Domain.Games;

namespace Leaderboard.Api.IntegrationTests.Features;

public sealed class GamesTests(ApiFactory factory) : IntegrationTest(factory)
{
    [Fact]
    public async Task Create_ReturnsCreatedWithLocationAndETag()
    {
        var owner = await Api.RegisterAsync();

        var response = await Api.SendAsync(HttpMethod.Post, "/api/v1/games", owner.AccessToken,
            new { name = "Space Blaster", slug = "space-blaster", scoreOrder = ScoreOrder.HigherIsBetter, minScore = 0, maxScore = 1_000_000 });

        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        var game = await response.ReadAsync<GameResponse>();
        game.OwnerId.ShouldBe(owner.Id);
        response.Headers.Location!.ToString().ShouldBe($"/api/v1/games/{game.Id}");
        response.Headers.ETag!.Tag.ShouldBe($"\"{game.Version}\"");
    }

    [Fact]
    public async Task Create_WithoutToken_ReturnsUnauthorized() =>
        await (await Api.SendAsync(HttpMethod.Post, "/api/v1/games", body: new { name = "X", slug = "xyz" }))
            .ShouldBeProblemAsync(HttpStatusCode.Unauthorized);

    [Fact]
    public async Task Create_DuplicateSlug_ReturnsConflict()
    {
        var owner = await Api.RegisterAsync();
        await Api.SendAsync(HttpMethod.Post, "/api/v1/games", owner.AccessToken, new { name = "A", slug = "same-slug" });

        var response = await Api.SendAsync(HttpMethod.Post, "/api/v1/games", owner.AccessToken, new { name = "B", slug = "same-slug" });

        await response.ShouldBeProblemAsync(HttpStatusCode.Conflict, "game.slug_taken");
    }

    [Fact]
    public async Task List_ReturnsOnlyActiveGamesMatchingSearch()
    {
        var owner = await Api.RegisterAsync();
        await Api.SendAsync(HttpMethod.Post, "/api/v1/games", owner.AccessToken, new { name = "Space Blaster", slug = "space-blaster" });
        await Api.SendAsync(HttpMethod.Post, "/api/v1/games", owner.AccessToken, new { name = "Kart Racer", slug = "kart-racer" });
        var archived = await (await Api.SendAsync(HttpMethod.Post, "/api/v1/games", owner.AccessToken,
            new { name = "Space Old", slug = "space-old" })).ReadAsync<GameResponse>();
        await Api.SendAsync(HttpMethod.Delete, $"/api/v1/games/{archived.Id}", owner.AccessToken);

        var page = await Api.GetAsync<PagedResult<GameResponse>>("/api/v1/games?search=space&page=1&pageSize=10");

        page.TotalCount.ShouldBe(1);
        page.Items.Single().Slug.ShouldBe("space-blaster");
    }

    [Fact]
    public async Task Get_UnknownGame_ReturnsNotFound() =>
        await (await Api.Client.GetAsync($"/api/v1/games/{Guid.CreateVersion7()}")).ShouldBeProblemAsync(HttpStatusCode.NotFound, "game.not_found");

    [Fact]
    public async Task Update_ByOwner_ChangesMetadata()
    {
        var game = await Api.CreateGameAsync();

        var response = await Api.SendAsync(HttpMethod.Put, $"/api/v1/games/{game.Id}", game.Owner.AccessToken,
            new { name = "Renamed", description = "desc", minScore = 0, maxScore = 50 });

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var updated = await response.ReadAsync<GameResponse>();
        updated.Name.ShouldBe("Renamed");
        updated.MaxScore.ShouldBe(50);
        updated.Version.ShouldNotBe(game.Game.Version);
    }

    [Fact]
    public async Task Update_ByAnotherPlayer_ReturnsNotFound()
    {
        var game = await Api.CreateGameAsync();
        var intruder = await Api.RegisterAsync();

        var response = await Api.SendAsync(HttpMethod.Put, $"/api/v1/games/{game.Id}", intruder.AccessToken, new { name = "Hijacked" });

        await response.ShouldBeProblemAsync(HttpStatusCode.NotFound, "game.not_found");
    }

    [Fact]
    public async Task Update_WithStaleIfMatch_ReturnsPreconditionFailed()
    {
        var game = await Api.CreateGameAsync();
        var request = new HttpRequestMessage(HttpMethod.Put, $"/api/v1/games/{game.Id}")
        {
            Content = System.Net.Http.Json.JsonContent.Create(new { name = "Renamed" }),
        };
        request.Headers.Authorization = new("Bearer", game.Owner.AccessToken);
        request.Headers.IfMatch.Add(new($"\"{game.Game.Version + 1}\""));

        var response = await Api.Client.SendAsync(request);

        await response.ShouldBeProblemAsync(HttpStatusCode.PreconditionFailed, "concurrency.conflict");
    }

    [Fact]
    public async Task Archive_StopsAcceptingScoresButKeepsLeaderboard()
    {
        var game = await Api.CreateGameAsync();
        await Api.SubmitAcceptedAsync(game, game.Owner.Id, 10);

        (await Api.SendAsync(HttpMethod.Delete, $"/api/v1/games/{game.Id}", game.Owner.AccessToken)).StatusCode.ShouldBe(HttpStatusCode.NoContent);

        await (await Api.SubmitScoreAsync(game.Id, game.ApiKey, game.Owner.Id, 20)).ShouldBeProblemAsync(HttpStatusCode.Conflict, "game.archived");
        (await Api.Client.GetAsync($"/api/v1/games/{game.Id}/leaderboard/top")).StatusCode.ShouldBe(HttpStatusCode.OK);
    }
}
