using System.Diagnostics;
using Leaderboard.Domain.Common;
using Microsoft.Extensions.Logging;

namespace Leaderboard.Application.Abstractions.Messaging;

/// <summary>Logs the outcome and duration of every use case with structured properties.</summary>
internal sealed partial class LoggingCommandDecorator<TCommand, TResponse>(
    ICommandHandler<TCommand, TResponse> inner, ILogger<LoggingCommandDecorator<TCommand, TResponse>> logger)
    : ICommandHandler<TCommand, TResponse>
    where TCommand : ICommand<TResponse>
{
    public async Task<Result<TResponse>> HandleAsync(TCommand command, CancellationToken cancellationToken)
    {
        var started = Stopwatch.GetTimestamp();
        var result = await inner.HandleAsync(command, cancellationToken);
        UseCaseLog.Completed(logger, typeof(TCommand).Name, result, Stopwatch.GetElapsedTime(started));
        return result;
    }
}

internal sealed class LoggingQueryDecorator<TQuery, TResponse>(
    IQueryHandler<TQuery, TResponse> inner, ILogger<LoggingQueryDecorator<TQuery, TResponse>> logger)
    : IQueryHandler<TQuery, TResponse>
    where TQuery : IQuery<TResponse>
{
    public async Task<Result<TResponse>> HandleAsync(TQuery query, CancellationToken cancellationToken)
    {
        var started = Stopwatch.GetTimestamp();
        var result = await inner.HandleAsync(query, cancellationToken);
        UseCaseLog.Completed(logger, typeof(TQuery).Name, result, Stopwatch.GetElapsedTime(started));
        return result;
    }
}

internal static partial class UseCaseLog
{
    public static void Completed(ILogger logger, string useCase, Result result, TimeSpan elapsed)
    {
        if (result.IsSuccess)
        {
            Succeeded(logger, useCase, elapsed.TotalMilliseconds);
        }
        else
        {
            Failed(logger, useCase, result.Error!.Code, elapsed.TotalMilliseconds);
        }
    }

    [LoggerMessage(Level = LogLevel.Debug, Message = "Use case {UseCase} succeeded in {ElapsedMs:0.0} ms")]
    private static partial void Succeeded(ILogger logger, string useCase, double elapsedMs);

    [LoggerMessage(Level = LogLevel.Information, Message = "Use case {UseCase} failed with {ErrorCode} in {ElapsedMs:0.0} ms")]
    private static partial void Failed(ILogger logger, string useCase, string errorCode, double elapsedMs);
}
