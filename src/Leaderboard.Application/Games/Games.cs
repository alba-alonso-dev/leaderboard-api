using FluentValidation;
using Leaderboard.Application.Abstractions.Messaging;
using Leaderboard.Application.Abstractions.Persistence;
using Leaderboard.Application.Common;
using Leaderboard.Domain.Common;
using Leaderboard.Domain.Games;

namespace Leaderboard.Application.Games;

public sealed record GameResponse(
    Guid Id,
    Guid OwnerId,
    string Slug,
    string Name,
    string? Description,
    ScoreOrder ScoreOrder,
    long? MinScore,
    long? MaxScore,
    bool IsArchived,
    DateTimeOffset CreatedAt,
    uint Version)
{
    public static GameResponse From(Game game) => new(
        game.Id, game.OwnerId, game.Slug, game.Name, game.Description, game.ScoreOrder,
        game.MinScore, game.MaxScore, game.IsArchived, game.CreatedAt, game.Version);
}

internal static class GameRules
{
    public const string SlugPattern = "^[a-z0-9]+(?:-[a-z0-9]+)*$";

    public static void ValidateMetadata<T>(
        this AbstractValidator<T> validator, Func<T, string> name, Func<T, string?> description, Func<T, long?> min, Func<T, long?> max)
    {
        validator.RuleFor(x => name(x)).NotEmpty().MaximumLength(Game.NameMaxLength).OverridePropertyName("name");
        validator.RuleFor(x => description(x)).MaximumLength(Game.DescriptionMaxLength).OverridePropertyName("description");
        validator.RuleFor(x => min(x))
            .Must((x, value) => value is null || max(x) is null || value <= max(x))
            .WithMessage("minScore cannot be greater than maxScore.")
            .OverridePropertyName("minScore");
    }

    /// <summary>Loads a game the requester owns. Games owned by others are reported as not found (no BOLA leak).</summary>
    public static async Task<Result<Game>> GetOwnedAsync(
        this IGameRepository games, Guid gameId, Guid requesterId, CancellationToken cancellationToken)
    {
        var game = await games.GetByIdAsync(gameId, cancellationToken);
        return game is null || !game.IsOwnedBy(requesterId) ? GameErrors.NotFound : game;
    }
}

// ---- Create -------------------------------------------------------------------------------------------------------

public sealed record CreateGameCommand(
    Guid OwnerId, string Name, string Slug, string? Description, ScoreOrder ScoreOrder, long? MinScore, long? MaxScore)
    : ICommand<GameResponse>;

public sealed class CreateGameCommandValidator : AbstractValidator<CreateGameCommand>
{
    public CreateGameCommandValidator()
    {
        RuleFor(x => x.Slug)
            .NotEmpty()
            .Length(3, Game.SlugMaxLength)
            .Matches(GameRules.SlugPattern).WithMessage("Slug must be lowercase kebab-case (e.g. 'space-blaster').");
        RuleFor(x => x.ScoreOrder).IsInEnum();
        this.ValidateMetadata(x => x.Name, x => x.Description, x => x.MinScore, x => x.MaxScore);
    }
}

internal sealed class CreateGameCommandHandler(IGameRepository games, IUnitOfWork unitOfWork, TimeProvider timeProvider)
    : ICommandHandler<CreateGameCommand, GameResponse>
{
    public async Task<Result<GameResponse>> HandleAsync(CreateGameCommand command, CancellationToken cancellationToken)
    {
        if (await games.SlugExistsAsync(command.Slug, cancellationToken))
        {
            return GameErrors.SlugTaken;
        }

        var game = Game.Create(
            command.OwnerId, command.Slug, command.Name, command.Description, command.ScoreOrder,
            command.MinScore, command.MaxScore, timeProvider.GetUtcNow());
        games.Add(game);

        try
        {
            await unitOfWork.SaveChangesAsync(cancellationToken);
        }
        catch (UniqueConstraintException)
        {
            return GameErrors.SlugTaken;
        }

        return GameResponse.From(game);
    }
}

// ---- Update -------------------------------------------------------------------------------------------------------

/// <summary>Full replacement of editable metadata. <paramref name="ExpectedVersion"/> comes from <c>If-Match</c>.</summary>
public sealed record UpdateGameCommand(
    Guid GameId, Guid RequesterId, string Name, string? Description, long? MinScore, long? MaxScore, uint? ExpectedVersion)
    : ICommand<GameResponse>;

public sealed class UpdateGameCommandValidator : AbstractValidator<UpdateGameCommand>
{
    public UpdateGameCommandValidator() =>
        this.ValidateMetadata(x => x.Name, x => x.Description, x => x.MinScore, x => x.MaxScore);
}

internal sealed class UpdateGameCommandHandler(IGameRepository games, IUnitOfWork unitOfWork)
    : ICommandHandler<UpdateGameCommand, GameResponse>
{
    public async Task<Result<GameResponse>> HandleAsync(UpdateGameCommand command, CancellationToken cancellationToken)
    {
        var result = await games.GetOwnedAsync(command.GameId, command.RequesterId, cancellationToken);
        if (result.IsFailure)
        {
            return result.Error!;
        }

        var game = result.Value;
        if (command.ExpectedVersion is { } expected && expected != game.Version)
        {
            return GameErrors.ConcurrencyConflict;
        }

        game.Update(command.Name, command.Description, command.MinScore, command.MaxScore);

        try
        {
            await unitOfWork.SaveChangesAsync(cancellationToken);
        }
        catch (ConcurrencyConflictException)
        {
            return GameErrors.ConcurrencyConflict;
        }

        return GameResponse.From(game);
    }
}

// ---- Archive ------------------------------------------------------------------------------------------------------

public sealed record ArchiveGameCommand(Guid GameId, Guid RequesterId) : ICommand<Unit>;

internal sealed class ArchiveGameCommandHandler(IGameRepository games, IUnitOfWork unitOfWork) : ICommandHandler<ArchiveGameCommand, Unit>
{
    public async Task<Result<Unit>> HandleAsync(ArchiveGameCommand command, CancellationToken cancellationToken)
    {
        var result = await games.GetOwnedAsync(command.GameId, command.RequesterId, cancellationToken);
        if (result.IsFailure)
        {
            return result.Error!;
        }

        result.Value.Archive();
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return Unit.Value;
    }
}

// ---- Queries ------------------------------------------------------------------------------------------------------

public sealed record GetGameQuery(Guid GameId) : IQuery<GameResponse>;

internal sealed class GetGameQueryHandler(IGameRepository games) : IQueryHandler<GetGameQuery, GameResponse>
{
    public async Task<Result<GameResponse>> HandleAsync(GetGameQuery query, CancellationToken cancellationToken)
    {
        var game = await games.GetByIdAsync(query.GameId, cancellationToken);
        return game is null ? GameErrors.NotFound : GameResponse.From(game);
    }
}

public sealed record ListGamesQuery(string? Search, int Page, int PageSize) : IQuery<PagedResult<GameResponse>>;

public sealed class ListGamesQueryValidator : AbstractValidator<ListGamesQuery>
{
    public ListGamesQueryValidator()
    {
        this.ValidatePaging(x => x.Page, x => x.PageSize);
        RuleFor(x => x.Search).MaximumLength(Game.NameMaxLength);
    }
}

internal sealed class ListGamesQueryHandler(IGameRepository games) : IQueryHandler<ListGamesQuery, PagedResult<GameResponse>>
{
    public async Task<Result<PagedResult<GameResponse>>> HandleAsync(ListGamesQuery query, CancellationToken cancellationToken)
    {
        var page = await games.ListActiveAsync(query.Search, query.Page, query.PageSize, cancellationToken);
        return new PagedResult<GameResponse>(page.Items.Select(GameResponse.From).ToList(), page.Page, page.PageSize, page.TotalCount);
    }
}
