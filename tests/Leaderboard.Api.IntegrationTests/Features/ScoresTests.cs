using System.Net;
using Leaderboard.Api.IntegrationTests.Infrastructure;
using Leaderboard.Application.Leaderboards;
using Leaderboard.Application.Scores;

namespace Leaderboard.Api.IntegrationTests.Features;

public sealed class ScoresTests(ApiFactory factory) : IntegrationTest(factory)
{
    [Fact]
    public async Task Submit_NewPersonalBest_UpdatesRank()
    {
        var game = await Api.CreateGameAsync(maxScore: 1_000_000);
        var ana = await Api.RegisterAsync();
        await Api.SubmitAcceptedAsync(game, ana.Id, 5_000);

        var result = await Api.SubmitAcceptedAsync(game, ana.Id, 7_200);

        result.IsPersonalBest.ShouldBeTrue();
        result.BestScore.ShouldBe(7_200);
        result.Rank.ShouldBe(1);
        result.TotalPlayers.ShouldBe(1);
    }

    [Fact]
    public async Task Submit_WorseScore_IsStoredButKeepsBest()
    {
        var game = await Api.CreateGameAsync();
        var ana = await Api.RegisterAsync();
        await Api.SubmitAcceptedAsync(game, ana.Id, 5_000);

        var result = await Api.SubmitAcceptedAsync(game, ana.Id, 3_000);

        result.IsPersonalBest.ShouldBeFalse();
        result.BestScore.ShouldBe(5_000);
        (await Factory.QueryScalarAsync<long>($"SELECT COUNT(*) FROM scores WHERE player_id = '{ana.Id}'")).ShouldBe(2);
        (await Factory.QueryScalarAsync<int>($"SELECT submissions_count FROM leaderboard_entries WHERE player_id = '{ana.Id}'")).ShouldBe(2);
    }

    [Fact]
    public async Task Submit_OutOfRange_ReturnsUnprocessable()
    {
        var game = await Api.CreateGameAsync(minScore: 0, maxScore: 1_000_000);

        await (await Api.SubmitScoreAsync(game.Id, game.ApiKey, game.Owner.Id, 2_000_000))
            .ShouldBeProblemAsync(HttpStatusCode.UnprocessableEntity, "score.out_of_range");
    }

    [Fact]
    public async Task Submit_ForUnknownPlayer_ReturnsUnprocessable()
    {
        var game = await Api.CreateGameAsync();

        await (await Api.SubmitScoreAsync(game.Id, game.ApiKey, Guid.CreateVersion7(), 10))
            .ShouldBeProblemAsync(HttpStatusCode.UnprocessableEntity, "player.not_found_for_score");
    }

    [Fact]
    public async Task Submit_InvalidMetadata_ReturnsValidationProblem()
    {
        var game = await Api.CreateGameAsync();

        var response = await Api.SubmitScoreAsync(game.Id, game.ApiKey, game.Owner.Id, 10, metadata: new[] { 1, 2 });

        var problem = await response.ShouldBeProblemAsync(HttpStatusCode.BadRequest, "validation.failed");
        problem.GetProperty("errors").TryGetProperty("metadata", out _).ShouldBeTrue();
    }

    [Fact]
    public async Task Submit_SameNonceTwice_IsIdempotent()
    {
        var game = await Api.CreateGameAsync();
        const string nonce = "b1946ac92492d2347c6235b4d2611184";

        var first = await Api.SubmitScoreAsync(game.Id, game.ApiKey, game.Owner.Id, 42, nonce);
        var retry = await Api.SubmitScoreAsync(game.Id, game.ApiKey, game.Owner.Id, 42, nonce);

        first.StatusCode.ShouldBe(HttpStatusCode.Created);
        retry.StatusCode.ShouldBe(HttpStatusCode.OK);
        var original = await first.ReadAsync<SubmitScoreResponse>();
        var replayed = await retry.ReadAsync<SubmitScoreResponse>();
        replayed.ScoreId.ShouldBe(original.ScoreId);
        replayed.IsReplay.ShouldBeTrue();
        (await Factory.QueryScalarAsync<long>($"SELECT COUNT(*) FROM scores WHERE nonce = '{nonce}'")).ShouldBe(1);
    }

    [Fact]
    public async Task Submit_ReusedNonceWithDifferentValue_ReturnsConflict()
    {
        var game = await Api.CreateGameAsync();
        const string nonce = "c1946ac92492d2347c6235b4d2611184";
        await Api.SubmitScoreAsync(game.Id, game.ApiKey, game.Owner.Id, 42, nonce);

        await (await Api.SubmitScoreAsync(game.Id, game.ApiKey, game.Owner.Id, 99_999, nonce))
            .ShouldBeProblemAsync(HttpStatusCode.Conflict, "score.nonce_reused");
    }

    [Fact]
    public async Task Submit_WithTamperedSignature_IsRejectedAndNothingStored()
    {
        var game = await Api.CreateGameAsync();

        var response = await Api.SubmitScoreAsync(game.Id, game.ApiKey, game.Owner.Id, 10, tamper: request =>
        {
            request.Headers.Remove("X-Signature");
            request.Headers.Add("X-Signature", Convert.ToBase64String(new byte[32]));
        });

        await response.ShouldBeProblemAsync(HttpStatusCode.Unauthorized, "apikey.invalid_signature");
        (await Factory.QueryScalarAsync<long>("SELECT COUNT(*) FROM scores")).ShouldBe(0);
    }

    [Fact]
    public async Task Submit_WithTamperedBody_IsRejected()
    {
        var game = await Api.CreateGameAsync();

        var response = await Api.SubmitScoreAsync(game.Id, game.ApiKey, game.Owner.Id, 10, tamper: request =>
        {
            var forged = $$"""{"playerId":"{{game.Owner.Id}}","value":999999}""";
            request.Content = forged.AsJson();
        });

        await response.ShouldBeProblemAsync(HttpStatusCode.Unauthorized, "apikey.invalid_signature");
    }

    [Fact]
    public async Task Submit_WithStaleTimestamp_IsRejected()
    {
        var game = await Api.CreateGameAsync();

        var response = await Api.SubmitScoreAsync(game.Id, game.ApiKey, game.Owner.Id, 10, signedAt: DateTimeOffset.UtcNow.AddMinutes(-10));

        await response.ShouldBeProblemAsync(HttpStatusCode.Unauthorized, "apikey.stale_request");
    }

    [Fact]
    public async Task Submit_WithoutSignatureHeaders_IsRejected()
    {
        var game = await Api.CreateGameAsync();

        var response = await Api.Client.PostAsync($"/api/v1/games/{game.Id}/scores", $$"""{"playerId":"{{game.Owner.Id}}","value":1}""".AsJson());

        await response.ShouldBeProblemAsync(HttpStatusCode.Unauthorized, "apikey.missing");
    }

    [Fact]
    public async Task Submit_WithKeyOfAnotherGame_IsForbidden()
    {
        var game = await Api.CreateGameAsync();
        var other = await Api.CreateGameAsync();

        await (await Api.SubmitScoreAsync(game.Id, other.ApiKey, game.Owner.Id, 10)).ShouldBeProblemAsync(HttpStatusCode.Forbidden, "apikey.game_mismatch");
    }

    [Fact]
    public async Task Submit_WithPlayerJwt_IsRejected()
    {
        var game = await Api.CreateGameAsync();

        var response = await Api.SendAsync(HttpMethod.Post, $"/api/v1/games/{game.Id}/scores", game.Owner.AccessToken,
            new { playerId = game.Owner.Id, value = 1 });

        await response.ShouldBeProblemAsync(HttpStatusCode.Unauthorized, "apikey.missing");
    }

    [Fact]
    public async Task Submit_ConcurrentSubmissionsOfSamePlayer_KeepConsistentBest()
    {
        var game = await Api.CreateGameAsync();
        var ana = await Api.RegisterAsync();
        var values = Enumerable.Range(1, 40).Select(i => (long)i * 10).ToArray();

        var responses = await Task.WhenAll(values.Select(v => Api.SubmitScoreAsync(game.Id, game.ApiKey, ana.Id, v)));

        responses.ShouldAllBe(r => r.StatusCode == HttpStatusCode.Created);
        var rank = await Api.GetAsync<PlayerRankResponse>($"/api/v1/games/{game.Id}/leaderboard/players/{ana.Id}");
        rank.Score.ShouldBe(values.Max());
        (await Factory.QueryScalarAsync<int>($"SELECT submissions_count FROM leaderboard_entries WHERE player_id = '{ana.Id}'")).ShouldBe(values.Length);
    }

    [Fact]
    public async Task MyScores_ReturnsHistoryNewestFirst()
    {
        var game = await Api.CreateGameAsync();
        var ana = await Api.RegisterAsync();
        await Api.SubmitAcceptedAsync(game, ana.Id, 1);
        await Api.SubmitAcceptedAsync(game, ana.Id, 2);

        var history = await Api.GetAsync<Application.Common.PagedResult<Application.Players.ScoreHistoryItemResponse>>(
            $"/api/v1/players/me/scores?gameId={game.Id}", ana.AccessToken);

        history.TotalCount.ShouldBe(2);
        history.Items.Select(s => s.Value).ShouldBe([2L, 1L]);
    }
}
