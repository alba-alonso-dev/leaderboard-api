using System.Net.Mime;
using System.Text.Json;
using Leaderboard.Infrastructure.Persistence;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Leaderboard.Api.Infrastructure;

internal static class HealthChecksSetup
{
    private const string ReadyTag = "ready";

    public static IServiceCollection AddApiHealthChecks(this IServiceCollection services)
    {
        services.AddHealthChecks().AddDbContextCheck<AppDbContext>("postgres", tags: [ReadyTag]);
        return services;
    }

    /// <summary>
    /// <c>/health/live</c>: the process answers (no dependencies, for restarts).
    /// <c>/health/ready</c>: dependencies are reachable (for traffic routing).
    /// </summary>
    public static void MapApiHealthChecks(this IEndpointRouteBuilder app)
    {
        app.MapHealthChecks("/health/live", new HealthCheckOptions { Predicate = _ => false, ResponseWriter = WriteAsync })
            .DisableRateLimiting().WithTags("Health");
        app.MapHealthChecks("/health/ready", new HealthCheckOptions { Predicate = c => c.Tags.Contains(ReadyTag), ResponseWriter = WriteAsync })
            .DisableRateLimiting().WithTags("Health");
    }

    private static Task WriteAsync(HttpContext context, HealthReport report)
    {
        context.Response.ContentType = MediaTypeNames.Application.Json;
        var payload = new
        {
            status = report.Status.ToString(),
            totalDurationMs = Math.Round(report.TotalDuration.TotalMilliseconds, 1),
            checks = report.Entries.ToDictionary(
                e => e.Key,
                e => new { status = e.Value.Status.ToString(), durationMs = Math.Round(e.Value.Duration.TotalMilliseconds, 1) }),
        };
        return context.Response.WriteAsync(JsonSerializer.Serialize(payload, JsonSerializerOptions.Web));
    }
}
