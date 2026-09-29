using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Leaderboard.Application.ApiKeys;
using Leaderboard.Application.Auth;
using Leaderboard.Application.Games;
using Leaderboard.Application.Scores;
using Leaderboard.Domain.Games;

namespace Leaderboard.Api.IntegrationTests.Infrastructure;

public sealed record TestPlayer(Guid Id, string Username, string Email, string AccessToken, string RefreshToken);

public sealed record TestGame(GameResponse Game, TestPlayer Owner, IssuedApiKeyResponse ApiKey)
{
    public Guid Id => Game.Id;
}

/// <summary>Typed helpers over the public HTTP contract (what a real client does, nothing more).</summary>
public sealed class ApiDriver(HttpClient client) : IDisposable
{
    public const string DefaultPassword = "Sup3rSecret1";

    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };

    public HttpClient Client { get; } = client;

    public async Task<TestPlayer> RegisterAsync(string? username = null)
    {
        username ??= "p_" + Guid.NewGuid().ToString("N")[..10];
        var email = $"{username}@example.com";

        var register = await Client.PostAsJsonAsync("/api/v1/auth/register", new { username, email, password = DefaultPassword });
        register.StatusCode.ShouldBe(HttpStatusCode.Created, await register.Content.ReadAsStringAsync());
        var player = await register.ReadAsync<PlayerResponse>();

        var tokens = await LoginAsync(email, DefaultPassword);
        return new TestPlayer(player.Id, username, email, tokens.AccessToken, tokens.RefreshToken);
    }

    public async Task<TokenResponse> LoginAsync(string email, string password)
    {
        var response = await Client.PostAsJsonAsync("/api/v1/auth/login", new { email, password });
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        return await response.ReadAsync<TokenResponse>();
    }

    public Task<HttpResponseMessage> SendAsync(HttpMethod method, string url, string? token = null, object? body = null)
    {
        var request = new HttpRequestMessage(method, url);
        if (token is not null)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }

        if (body is not null)
        {
            request.Content = JsonContent.Create(body, options: Json);
        }

        return Client.SendAsync(request);
    }

    public async Task<TestGame> CreateGameAsync(
        TestPlayer? owner = null, ScoreOrder order = ScoreOrder.HigherIsBetter, long? minScore = null, long? maxScore = null)
    {
        owner ??= await RegisterAsync();
        var slug = "g-" + Guid.NewGuid().ToString("N")[..12];

        var created = await SendAsync(HttpMethod.Post, "/api/v1/games", owner.AccessToken,
            new { name = "Game " + slug, slug, scoreOrder = order, minScore, maxScore });
        created.StatusCode.ShouldBe(HttpStatusCode.Created, await created.Content.ReadAsStringAsync());
        var game = await created.ReadAsync<GameResponse>();

        var key = await IssueApiKeyAsync(game.Id, owner);
        return new TestGame(game, owner, key);
    }

    public async Task<IssuedApiKeyResponse> IssueApiKeyAsync(Guid gameId, TestPlayer owner, string name = "server")
    {
        var response = await SendAsync(HttpMethod.Post, $"/api/v1/games/{gameId}/api-keys", owner.AccessToken, new { name });
        response.StatusCode.ShouldBe(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        return await response.ReadAsync<IssuedApiKeyResponse>();
    }

    /// <summary>Signs exactly like a game server would (see samples/submit-score.sh).</summary>
    public Task<HttpResponseMessage> SubmitScoreAsync(
        Guid gameId,
        IssuedApiKeyResponse key,
        Guid playerId,
        long value,
        string? nonce = null,
        object? metadata = null,
        Action<HttpRequestMessage>? tamper = null,
        DateTimeOffset? signedAt = null)
    {
        var path = $"/api/v1/games/{gameId}/scores";
        var body = JsonSerializer.SerializeToUtf8Bytes(new { playerId, value, metadata }, Json);
        var headers = RequestSigning.CreateHeaders(key.KeyId, key.Secret, "POST", path, body, signedAt ?? DateTimeOffset.UtcNow, nonce);

        var request = new HttpRequestMessage(HttpMethod.Post, path) { Content = new ByteArrayContent(body) };
        request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
        foreach (var (name, headerValue) in headers)
        {
            request.Headers.Add(name, headerValue);
        }

        tamper?.Invoke(request);
        return Client.SendAsync(request);
    }

    public async Task<SubmitScoreResponse> SubmitAcceptedAsync(TestGame game, Guid playerId, long value)
    {
        var response = await SubmitScoreAsync(game.Id, game.ApiKey, playerId, value);
        response.StatusCode.ShouldBe(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        return await response.ReadAsync<SubmitScoreResponse>();
    }

    public async Task<T> GetAsync<T>(string url, string? token = null)
    {
        var response = await SendAsync(HttpMethod.Get, url, token);
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        return await response.ReadAsync<T>();
    }

    public void Dispose() => Client.Dispose();
}

public static class HttpResponseExtensions
{
    public static async Task<T> ReadAsync<T>(this HttpResponseMessage response) =>
        (await response.Content.ReadFromJsonAsync<T>(ApiDriver.Json))!;

    /// <summary>Asserts an RFC 9457 problem response with the given status and stable error code.</summary>
    public static async Task<JsonElement> ShouldBeProblemAsync(this HttpResponseMessage response, HttpStatusCode status, string? code = null)
    {
        var body = await response.Content.ReadAsStringAsync();
        response.StatusCode.ShouldBe(status, body);
        response.Content.Headers.ContentType?.MediaType.ShouldBe("application/problem+json");

        var problem = JsonDocument.Parse(body).RootElement.Clone();
        problem.GetProperty("status").GetInt32().ShouldBe((int)status);
        problem.GetProperty("traceId").GetString().ShouldNotBeNullOrWhiteSpace();
        if (code is not null)
        {
            problem.GetProperty("code").GetString().ShouldBe(code);
        }

        return problem;
    }

    public static StringContent AsJson(this string json) => new(json, Encoding.UTF8, "application/json");
}
