using Leaderboard.Domain.Common;

namespace Leaderboard.Application.Abstractions.Messaging;

/// <summary>A request that changes state. Handled by exactly one <see cref="ICommandHandler{TCommand, TResponse}"/>.</summary>
public interface ICommand<TResponse>;

/// <summary>A request that only reads state. Handled by exactly one <see cref="IQueryHandler{TQuery, TResponse}"/>.</summary>
public interface IQuery<TResponse>;

public interface ICommandHandler<in TCommand, TResponse>
    where TCommand : ICommand<TResponse>
{
    Task<Result<TResponse>> HandleAsync(TCommand command, CancellationToken cancellationToken);
}

public interface IQueryHandler<in TQuery, TResponse>
    where TQuery : IQuery<TResponse>
{
    Task<Result<TResponse>> HandleAsync(TQuery query, CancellationToken cancellationToken);
}

/// <summary>Return type of commands that produce no value.</summary>
public readonly record struct Unit
{
    public static readonly Unit Value;
}
