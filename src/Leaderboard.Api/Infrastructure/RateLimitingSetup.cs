using System.Globalization;
using System.Threading.RateLimiting;
using Leaderboard.Api.Authentication;
using Leaderboard.Api.ErrorHandling;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace Leaderboard.Api.Infrastructure;

public sealed class RateLimitingOptions
{
    public const string SectionName = "RateLimiting";

    public bool Enabled { get; set; } = true;

    /// <summary>Register/login/refresh attempts per IP per minute (brute-force protection).</summary>
    public int AuthPerMinute { get; set; } = 5;

    /// <summary>Sustained score submissions per API key per minute (token bucket refill rate).</summary>
    public int ScoreSubmitPerMinute { get; set; } = 60;

    /// <summary>Burst allowed per API key above the sustained rate.</summary>
    public int ScoreSubmitBurst { get; set; } = 20;

    /// <summary>Anonymous leaderboard reads per IP per minute.</summary>
    public int PublicReadPerMinute { get; set; } = 120;
}

public static class RateLimitPolicies
{
    public const string Auth = "auth";
    public const string ScoreSubmit = "score-submit";
    public const string PublicRead = "public-read";
}

internal static class RateLimitingSetup
{
    public static IServiceCollection AddApiRateLimiting(this IServiceCollection services, IConfiguration configuration)
    {
        var options = configuration.GetSection(RateLimitingOptions.SectionName).Get<RateLimitingOptions>() ?? new RateLimitingOptions();

        services.AddRateLimiter(limiter =>
        {
            limiter.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            limiter.OnRejected = WriteRejectionAsync;

            limiter.AddPolicy(RateLimitPolicies.Auth, context => !options.Enabled
                ? RateLimitPartition.GetNoLimiter("disabled")
                : RateLimitPartition.GetFixedWindowLimiter(ClientIp(context), _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = options.AuthPerMinute,
                    Window = TimeSpan.FromMinutes(1),
                }));

            // Runs after authorization, so the API key id claim is available for partitioning.
            limiter.AddPolicy(RateLimitPolicies.ScoreSubmit, context => !options.Enabled
                ? RateLimitPartition.GetNoLimiter("disabled")
                : RateLimitPartition.GetTokenBucketLimiter(
                    context.User.FindFirst(LeaderboardClaims.ApiKeyId)?.Value ?? ClientIp(context),
                    _ => new TokenBucketRateLimiterOptions
                    {
                        TokenLimit = options.ScoreSubmitBurst,
                        TokensPerPeriod = Math.Max(1, options.ScoreSubmitPerMinute / 6),
                        ReplenishmentPeriod = TimeSpan.FromSeconds(10),
                    }));

            limiter.AddPolicy(RateLimitPolicies.PublicRead, context => !options.Enabled
                ? RateLimitPartition.GetNoLimiter("disabled")
                : RateLimitPartition.GetSlidingWindowLimiter(ClientIp(context), _ => new SlidingWindowRateLimiterOptions
                {
                    PermitLimit = options.PublicReadPerMinute,
                    Window = TimeSpan.FromMinutes(1),
                    SegmentsPerWindow = 6,
                }));
        });

        return services;
    }

    private static string ClientIp(HttpContext context) => context.Connection.RemoteIpAddress?.ToString() ?? "unknown";

    private static async ValueTask WriteRejectionAsync(OnRejectedContext context, CancellationToken cancellationToken)
    {
        var http = context.HttpContext;
        if (context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter))
        {
            http.Response.Headers.RetryAfter = ((int)Math.Ceiling(retryAfter.TotalSeconds)).ToString(CultureInfo.InvariantCulture);
        }

        var problemDetails = http.RequestServices.GetRequiredService<IProblemDetailsService>();
        await problemDetails.WriteAsync(new ProblemDetailsContext
        {
            HttpContext = http,
            ProblemDetails = new ProblemDetails
            {
                Type = "https://tools.ietf.org/html/rfc6585#section-4",
                Status = StatusCodes.Status429TooManyRequests,
                Title = "Too Many Requests",
                Detail = "Rate limit exceeded. Retry after the time indicated by the Retry-After header.",
                Extensions = { ["code"] = ErrorCodes.RateLimited },
            },
        });
    }
}
