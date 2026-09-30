using System.Net;
using Leaderboard.Api.IntegrationTests.Infrastructure;
using Leaderboard.Application.ApiKeys;

namespace Leaderboard.Api.IntegrationTests.Features;

public sealed class ApiKeysTests(ApiFactory factory) : IntegrationTest(factory)
{
    [Fact]
    public async Task Issue_ReturnsSecretOnce_ListNeverShowsIt()
    {
        var game = await Api.CreateGameAsync();

        game.ApiKey.KeyId.ShouldStartWith("lbk_");
        game.ApiKey.Secret.ShouldStartWith("lbs_");

        var list = await Api.SendAsync(HttpMethod.Get, $"/api/v1/games/{game.Id}/api-keys", game.Owner.AccessToken);
        var body = await list.Content.ReadAsStringAsync();
        body.ShouldContain(game.ApiKey.KeyId);
        body.ShouldNotContain(game.ApiKey.Secret);
        body.ShouldNotContain("secret", Case.Insensitive);
    }

    [Fact]
    public async Task Secret_IsEncryptedAtRest()
    {
        var game = await Api.CreateGameAsync();

        var stored = await Factory.QueryScalarAsync<string>(
            $"SELECT encode(secret_ciphertext, 'escape') FROM game_api_keys WHERE key_id = '{game.ApiKey.KeyId}'");

        stored.ShouldNotBeNull().ShouldNotContain(game.ApiKey.Secret);
    }

    [Fact]
    public async Task Revoke_RejectsSubsequentSubmissions()
    {
        var game = await Api.CreateGameAsync();
        (await Api.SubmitScoreAsync(game.Id, game.ApiKey, game.Owner.Id, 1)).StatusCode.ShouldBe(HttpStatusCode.Created);

        var revoke = await Api.SendAsync(HttpMethod.Delete, $"/api/v1/games/{game.Id}/api-keys/{game.ApiKey.Id}", game.Owner.AccessToken);
        revoke.StatusCode.ShouldBe(HttpStatusCode.NoContent);

        await (await Api.SubmitScoreAsync(game.Id, game.ApiKey, game.Owner.Id, 2)).ShouldBeProblemAsync(HttpStatusCode.Unauthorized, "apikey.invalid");
        var keys = await Api.GetAsync<List<ApiKeyResponse>>($"/api/v1/games/{game.Id}/api-keys", game.Owner.AccessToken);
        keys.Single().IsActive.ShouldBeFalse();
    }

    [Fact]
    public async Task Rotation_NewKeyWorksWhileOldIsRevoked()
    {
        var game = await Api.CreateGameAsync();
        var newKey = await Api.IssueApiKeyAsync(game.Id, game.Owner, "rotated");

        await Api.SendAsync(HttpMethod.Delete, $"/api/v1/games/{game.Id}/api-keys/{game.ApiKey.Id}", game.Owner.AccessToken);

        (await Api.SubmitScoreAsync(game.Id, newKey, game.Owner.Id, 5)).StatusCode.ShouldBe(HttpStatusCode.Created);
        (await Api.SubmitScoreAsync(game.Id, game.ApiKey, game.Owner.Id, 5)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Issue_BeyondFiveActiveKeys_ReturnsConflict()
    {
        var game = await Api.CreateGameAsync(); // 1 key
        for (var i = 0; i < 4; i++)
        {
            await Api.IssueApiKeyAsync(game.Id, game.Owner, $"k{i}");
        }

        var response = await Api.SendAsync(HttpMethod.Post, $"/api/v1/games/{game.Id}/api-keys", game.Owner.AccessToken, new { name = "sixth" });

        await response.ShouldBeProblemAsync(HttpStatusCode.Conflict, "apikey.limit_reached");
    }

    [Fact]
    public async Task ManageKeys_OfForeignGame_ReturnsNotFound()
    {
        var game = await Api.CreateGameAsync();
        var intruder = await Api.RegisterAsync();

        await (await Api.SendAsync(HttpMethod.Post, $"/api/v1/games/{game.Id}/api-keys", intruder.AccessToken, new { name = "x" }))
            .ShouldBeProblemAsync(HttpStatusCode.NotFound, "game.not_found");
        await (await Api.SendAsync(HttpMethod.Get, $"/api/v1/games/{game.Id}/api-keys", intruder.AccessToken))
            .ShouldBeProblemAsync(HttpStatusCode.NotFound);
    }
}
