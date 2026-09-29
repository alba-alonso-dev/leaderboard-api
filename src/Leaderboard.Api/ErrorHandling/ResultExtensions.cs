using Leaderboard.Application.Common;
using Leaderboard.Domain.Common;
using Microsoft.AspNetCore.WebUtilities;

namespace Leaderboard.Api.ErrorHandling;

/// <summary>Single place where use-case errors become HTTP responses (RFC 9457 ProblemDetails).</summary>
public static class ResultExtensions
{
    public static IResult ToHttpResult<T>(this Result<T> result, Func<T, IResult> onSuccess) =>
        result.IsSuccess ? onSuccess(result.Value) : result.Error!.ToProblem();

    public static IResult ToProblem(this Error error)
    {
        var extensions = new Dictionary<string, object?>(StringComparer.Ordinal) { ["code"] = error.Code };

        if (error is ValidationError validation)
        {
            return TypedResults.ValidationProblem(
                validation.Errors, detail: validation.Description, title: "One or more validation errors occurred.", extensions: extensions);
        }

        var status = ToStatusCode(error.Type);
        return TypedResults.Problem(
            detail: error.Description, statusCode: status, title: ReasonPhrases.GetReasonPhrase(status), extensions: extensions);
    }

    public static int ToStatusCode(ErrorType type) => type switch
    {
        ErrorType.Validation => StatusCodes.Status400BadRequest,
        ErrorType.Unauthorized => StatusCodes.Status401Unauthorized,
        ErrorType.Forbidden => StatusCodes.Status403Forbidden,
        ErrorType.NotFound => StatusCodes.Status404NotFound,
        ErrorType.Conflict => StatusCodes.Status409Conflict,
        ErrorType.PreconditionFailed => StatusCodes.Status412PreconditionFailed,
        ErrorType.BusinessRule => StatusCodes.Status422UnprocessableEntity,
        ErrorType.TooManyRequests => StatusCodes.Status429TooManyRequests,
        _ => StatusCodes.Status500InternalServerError,
    };
}
