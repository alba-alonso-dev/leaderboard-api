using Leaderboard.Api.Authentication;
using Leaderboard.Api.ErrorHandling;
using Leaderboard.Application.Abstractions.Messaging;
using Leaderboard.Application.Scores;
using Microsoft.AspNetCore.Mvc;

namespace Leaderboard.Api.Endpoints;

public sealed class AdminEndpoints : IEndpointGroup
{
    public void Map(IEndpointRouteBuilder app)
    {
        app.MapDelete("/api/v1/admin/scores/{scoreId:guid}", InvalidateScoreAsync)
            .WithTags("Admin")
            .RequireAuthorization(AuthPolicies.Admin)
            .WithName("InvalidateScore")
            .WithSummary("Moderation: rejects a score and recalculates the player's leaderboard entry.")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound);
    }

    private static async Task<IResult> InvalidateScoreAsync(
        Guid scoreId, [FromServices] ICommandHandler<InvalidateScoreCommand, Unit> handler, CancellationToken ct) =>
        (await handler.HandleAsync(new InvalidateScoreCommand(scoreId), ct)).ToHttpResult(_ => TypedResults.NoContent());
}
