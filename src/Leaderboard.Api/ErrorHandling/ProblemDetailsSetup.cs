using System.Diagnostics;

namespace Leaderboard.Api.ErrorHandling;

internal static class ProblemDetailsSetup
{
    /// <summary>Every problem response carries <c>traceId</c> (correlates with logs), <c>instance</c> and a stable <c>code</c>.</summary>
    public static IServiceCollection AddApiProblemDetails(this IServiceCollection services)
    {
        services.AddProblemDetails(options => options.CustomizeProblemDetails = context =>
        {
            var problem = context.ProblemDetails;
            problem.Instance ??= context.HttpContext.Request.Path;
            problem.Extensions["traceId"] = Activity.Current?.Id ?? context.HttpContext.TraceIdentifier;
            if (!problem.Extensions.ContainsKey("code"))
            {
                problem.Extensions["code"] = ErrorCodes.ForStatus(problem.Status ?? StatusCodes.Status500InternalServerError);
            }
        });
        services.AddExceptionHandler<GlobalExceptionHandler>();
        return services;
    }
}
