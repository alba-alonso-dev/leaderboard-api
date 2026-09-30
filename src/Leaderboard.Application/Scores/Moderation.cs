using System.Text.Json;
using FluentValidation;
using Leaderboard.Application.Abstractions.Messaging;
using Leaderboard.Application.Abstractions.Persistence;
using Leaderboard.Application.Common;
using Leaderboard.Domain.Common;
using Leaderboard.Domain.Scores;

namespace Leaderboard.Application.Scores;

public sealed record ModerationScoreResponse(
    Guid ScoreId, Guid GameId, Guid PlayerId, long Value, ScoreStatus Status, DateTimeOffset SubmittedAt, JsonElement? Metadata)
{
    public static ModerationScoreResponse From(Score score) => new(
        score.Id, score.GameId, score.PlayerId, score.Value, score.Status, score.SubmittedAt,
        score.Metadata is null ? null : JsonDocument.Parse(score.Metadata).RootElement.Clone());
}

// ---- Review queue -------------------------------------------------------------------------------------------------

/// <summary>Scores by status, oldest first. Defaults to the moderation queue (<see cref="ScoreStatus.PendingReview"/>).</summary>
public sealed record ListScoresByStatusQuery(ScoreStatus Status, Guid? GameId, int Page, int PageSize)
    : IQuery<PagedResult<ModerationScoreResponse>>;

public sealed class ListScoresByStatusQueryValidator : AbstractValidator<ListScoresByStatusQuery>
{
    public ListScoresByStatusQueryValidator()
    {
        RuleFor(x => x.Status).IsInEnum();
        this.ValidatePaging(x => x.Page, x => x.PageSize);
    }
}

internal sealed class ListScoresByStatusQueryHandler(IScoreRepository scores)
    : IQueryHandler<ListScoresByStatusQuery, PagedResult<ModerationScoreResponse>>
{
    public async Task<Result<PagedResult<ModerationScoreResponse>>> HandleAsync(
        ListScoresByStatusQuery query, CancellationToken cancellationToken)
    {
        var page = await scores.ListByStatusAsync(query.Status, query.GameId, query.Page, query.PageSize, cancellationToken);
        var items = page.Items.Select(ModerationScoreResponse.From).ToList();
        return new PagedResult<ModerationScoreResponse>(items, page.Page, page.PageSize, page.TotalCount);
    }
}

// ---- Approve ------------------------------------------------------------------------------------------------------

/// <summary>Moderation: confirms a score held for review. It enters the leaderboard with its original timestamp.</summary>
public sealed record ApproveScoreCommand(Guid ScoreId) : ICommand<Unit>;

internal sealed class ApproveScoreCommandHandler(IScoreRepository scores, IGameRepository games, IUnitOfWork unitOfWork)
    : ICommandHandler<ApproveScoreCommand, Unit>
{
    public async Task<Result<Unit>> HandleAsync(ApproveScoreCommand command, CancellationToken cancellationToken)
    {
        var score = await scores.GetByIdAsync(command.ScoreId, cancellationToken);
        if (score is null)
        {
            return ScoreErrors.NotFound;
        }

        var approved = score.Approve();
        if (approved.IsFailure)
        {
            return approved.Error!;
        }

        var game = await games.GetByIdAsync(score.GameId, cancellationToken)
                   ?? throw new InvalidOperationException($"Score {score.Id} references missing game {score.GameId}.");

        await unitOfWork.ExecuteInTransactionAsync(
            async ct =>
            {
                await unitOfWork.SaveChangesAsync(ct);
                await scores.UpsertLeaderboardEntryAsync(score, game.ToRankKey(score.Value), ct);
                return true;
            },
            cancellationToken);

        return Unit.Value;
    }
}
