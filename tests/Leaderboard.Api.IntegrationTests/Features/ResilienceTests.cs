using System.Net;
using System.Net.Http.Json;
using Leaderboard.Api.IntegrationTests.Infrastructure;

namespace Leaderboard.Api.IntegrationTests.Features;

/// <summary>Host with production-like limits enabled (the shared fixture disables them).</summary>
public sealed class RateLimitedApiFactory : ApiFactory
{
    protected override IReadOnlyDictionary<string, string?> Overrides => new Dictionary<string, string?>
    {
        ["RateLimiting:Enabled"] = "true",
        ["RateLimiting:AuthPerMinute"] = "5",
    };
}

/// <summary>Per-player submission limit enabled; HTTP rate limiting stays off so setup calls are not throttled.</summary>
public sealed class PlayerLimitApiFactory : ApiFactory
{
    protected override IReadOnlyDictionary<string, string?> Overrides => new Dictionary<string, string?>
    {
        ["Scores:MaxPerPlayerPerMinute"] = "3",
    };
}

public sealed class RateLimitingTests(RateLimitedApiFactory factory) : IClassFixture<RateLimitedApiFactory>
{
    [Fact]
    public async Task Login_IsLimitedPerClientWithRetryAfter()
    {
        using var client = factory.CreateClient();
        var responses = new List<HttpResponseMessage>();
        for (var i = 0; i < 6; i++)
        {
            responses.Add(await client.PostAsJsonAsync("/api/v1/auth/login", new { email = "x@example.com", password = "Whatever123" }));
        }

        responses.Take(5).ShouldAllBe(r => r.StatusCode == HttpStatusCode.Unauthorized);
        var limited = responses[5];
        await limited.ShouldBeProblemAsync(HttpStatusCode.TooManyRequests, "rate_limit.exceeded");
        limited.Headers.RetryAfter.ShouldNotBeNull();
    }
}

public sealed class PlayerLimitTests(PlayerLimitApiFactory factory) : IClassFixture<PlayerLimitApiFactory>
{
    [Fact]
    public async Task Scores_AreLimitedPerPlayerAndGame()
    {
        using var api = new ApiDriver(factory.CreateClient());
        var game = await api.CreateGameAsync();

        for (var i = 1; i <= 3; i++)
        {
            (await api.SubmitScoreAsync(game.Id, game.ApiKey, game.Owner.Id, i)).StatusCode.ShouldBe(HttpStatusCode.Created);
        }

        await (await api.SubmitScoreAsync(game.Id, game.ApiKey, game.Owner.Id, 4))
            .ShouldBeProblemAsync(HttpStatusCode.TooManyRequests, "rate_limit.player_exceeded");
    }
}

/// <summary>Own database container so it can be stopped without affecting other tests.</summary>
public sealed class DatabaseOutageFactory : ApiFactory;

public sealed class DatabaseOutageTests(DatabaseOutageFactory factory) : IClassFixture<DatabaseOutageFactory>
{
    [Fact]
    public async Task Ready_Fails_WhileLive_StaysUp_WhenDatabaseIsDown()
    {
        using var client = factory.CreateClient();
        (await client.GetAsync("/health/ready")).StatusCode.ShouldBe(HttpStatusCode.OK);

        await factory.StopDatabaseAsync();

        (await client.GetAsync("/health/ready")).StatusCode.ShouldBe(HttpStatusCode.ServiceUnavailable);
        (await client.GetAsync("/health/live")).StatusCode.ShouldBe(HttpStatusCode.OK);
    }
}
