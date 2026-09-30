using System.Net;
using System.Text.Json;
using Leaderboard.Api.IntegrationTests.Infrastructure;
using Leaderboard.Application.Abstractions.Persistence;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Serilog.Core;
using Serilog.Events;

namespace Leaderboard.Api.IntegrationTests.Features;

public sealed class OperationalTests(ApiFactory factory) : IntegrationTest(factory)
{
    [Theory]
    [InlineData("/health/live")]
    [InlineData("/health/ready")]
    public async Task HealthChecks_AreHealthy(string url)
    {
        var response = await Api.Client.GetAsync(url);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.GetProperty("status").GetString().ShouldBe("Healthy");
    }

    [Fact]
    public async Task OpenApiDocument_IsGeneratedWithBothSecuritySchemes()
    {
        var response = await Api.Client.GetAsync("/openapi/v1.json");

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
        var schemes = document.GetProperty("components").GetProperty("securitySchemes");
        schemes.TryGetProperty("Bearer", out _).ShouldBeTrue();
        schemes.TryGetProperty("ApiKeyHmac", out _).ShouldBeTrue();
        document.GetProperty("paths").TryGetProperty("/api/v1/games/{gameId}/scores", out _).ShouldBeTrue();
    }

    [Fact]
    public async Task SwaggerUi_IsServed() =>
        (await Api.Client.GetAsync("/swagger/index.html")).StatusCode.ShouldBe(HttpStatusCode.OK);

    [Fact]
    public async Task UnknownRoute_ReturnsProblemDetails() =>
        await (await Api.Client.GetAsync("/api/v1/does-not-exist")).ShouldBeProblemAsync(HttpStatusCode.NotFound, "resource.not_found");

    [Fact]
    public async Task Responses_IncludeSecurityHeaders()
    {
        var response = await Api.Client.GetAsync("/health/live");

        response.Headers.GetValues("X-Content-Type-Options").ShouldBe(["nosniff"]);
        response.Headers.GetValues("X-Frame-Options").ShouldBe(["DENY"]);
    }

    [Fact]
    public async Task UnhandledException_Returns500WithTraceIdThatAppearsInLogs()
    {
        var games = Substitute.For<IGameRepository>();
        games.GetByIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns<Task<Leaderboard.Domain.Games.Game?>>(_ =>
            throw new InvalidOperationException("boom: database exploded"));
        var logs = new CapturingSink();
        using var client = Factory
            .WithWebHostBuilder(b => b.ConfigureTestServices(s => s.AddScoped(_ => games).AddSingleton<ILogEventSink>(logs)))
            .CreateClient();

        var response = await client.GetAsync($"/api/v1/games/{Guid.CreateVersion7()}");

        var problem = await response.ShouldBeProblemAsync(HttpStatusCode.InternalServerError, "server.unexpected");
        (await response.Content.ReadAsStringAsync()).ShouldNotContain("boom");
        var traceId = problem.GetProperty("traceId").GetString()!.Split('-')[1];
        logs.Events.ShouldContain(e =>
            e.Level == LogEventLevel.Error && e.Exception != null && e.TraceId.ToString() == traceId);
    }

    /// <summary>Picked up by <c>ReadFrom.Services</c> in Program.cs.</summary>
    private sealed class CapturingSink : ILogEventSink
    {
        private readonly System.Collections.Concurrent.ConcurrentQueue<LogEvent> _events = new();

        public IReadOnlyCollection<LogEvent> Events => _events;

        public void Emit(LogEvent logEvent) => _events.Enqueue(logEvent);
    }
}
