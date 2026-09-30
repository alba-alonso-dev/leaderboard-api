using Leaderboard.Api.Authentication;
using Leaderboard.Api.ErrorHandling;
using Leaderboard.Application.Abstractions.Messaging;
using Leaderboard.Application.Common;
using Leaderboard.Application.Scores;
using Leaderboard.Domain.Scores;
using Microsoft.AspNetCore.Mvc;

namespace Leaderboard.Api.Endpoints;

public sealed class AdminEndpoints : IEndpointGroup
{
    public void Map(IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/admin/scores").WithTags("Admin").RequireAuthorization(AuthPolicies.Admin);

        group.MapGet("/", ListAsync)
            .WithName("ListScoresForModeration")
            .WithSummary("Moderation queue: scores by status (default PendingReview), oldest first.")
            .Produces<PagedResult<ModerationScoreResponse>>()
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden);

        group.MapPost("/{scoreId:guid}/approve", ApproveAsync)
            .WithName("ApproveScore")
            .WithSummary("Moderation: accepts a score held for review; it enters the leaderboard.")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        group.MapDelete("/{scoreId:guid}", InvalidateScoreAsync)
            .WithName("InvalidateScore")
            .WithSummary("Moderation: rejects a score and recalculates the player's leaderboard entry.")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound);
    }

    private static async Task<IResult> ListAsync(
        [FromServices] IQueryHandler<ListScoresByStatusQuery, PagedResult<ModerationScoreResponse>> handler,
        CancellationToken ct,
        ScoreStatus status = ScoreStatus.PendingReview,
        Guid? gameId = null,
        int page = 1,
        int pageSize = Paging.DefaultPageSize) =>
        (await handler.HandleAsync(new ListScoresByStatusQuery(status, gameId, page, pageSize), ct)).ToHttpResult(TypedResults.Ok);

    private static async Task<IResult> ApproveAsync(
        Guid scoreId, [FromServices] ICommandHandler<ApproveScoreCommand, Unit> handler, CancellationToken ct) =>
        (await handler.HandleAsync(new ApproveScoreCommand(scoreId), ct)).ToHttpResult(_ => TypedResults.NoContent());

    private static async Task<IResult> InvalidateScoreAsync(
        Guid scoreId, [FromServices] ICommandHandler<InvalidateScoreCommand, Unit> handler, CancellationToken ct) =>
        (await handler.HandleAsync(new InvalidateScoreCommand(scoreId), ct)).ToHttpResult(_ => TypedResults.NoContent());
}
