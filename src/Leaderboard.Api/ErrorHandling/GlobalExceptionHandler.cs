using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;

namespace Leaderboard.Api.ErrorHandling;

/// <summary>Last line of defense: logs unexpected exceptions and returns a 500 ProblemDetails without internals (outside Development).</summary>
internal sealed partial class GlobalExceptionHandler(
    IProblemDetailsService problemDetailsService, IHostEnvironment environment, ILogger<GlobalExceptionHandler> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        if (exception is OperationCanceledException && httpContext.RequestAborted.IsCancellationRequested)
        {
            ClientClosedRequest(logger, httpContext.Request.Path);
            httpContext.Response.StatusCode = StatusCodes.Status499ClientClosedRequest;
            return true;
        }

        if (exception is BadHttpRequestException badRequest)
        {
            return await WriteAsync(httpContext, exception, badRequest.StatusCode, "Bad request", badRequest.Message);
        }

        UnhandledException(logger, exception, httpContext.Request.Method, httpContext.Request.Path);
        return await WriteAsync(
            httpContext, exception, StatusCodes.Status500InternalServerError, "An unexpected error occurred.",
            environment.IsDevelopment() ? exception.ToString() : null);
    }

    private ValueTask<bool> WriteAsync(HttpContext httpContext, Exception exception, int status, string title, string? detail)
    {
        httpContext.Response.StatusCode = status;
        return problemDetailsService.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = httpContext,
            Exception = exception,
            ProblemDetails = new ProblemDetails { Status = status, Title = title, Detail = detail },
        });
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "Unhandled exception processing {Method} {Path}")]
    private static partial void UnhandledException(ILogger logger, Exception exception, string method, string path);

    [LoggerMessage(Level = LogLevel.Information, Message = "Client closed the request {Path}")]
    private static partial void ClientClosedRequest(ILogger logger, string path);
}
