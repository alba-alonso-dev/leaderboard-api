using System.Text.Json;
using Leaderboard.Api.Authentication;
using Leaderboard.Api.ErrorHandling;
using Leaderboard.Api.Infrastructure;
using Leaderboard.Application.Abstractions.Messaging;
using Leaderboard.Application.Scores;
using Leaderboard.Domain.Scores;
using Microsoft.AspNetCore.Mvc;

namespace Leaderboard.Api.Endpoints;

/// <param name="Metadata">Optional JSON object with game-specific context (≤ 2 KB).</param>
public sealed record SubmitScoreRequest(Guid PlayerId, long Value, JsonElement? Metadata = null);

public sealed class ScoresEndpoints : IEndpointGroup
{
    public void Map(IEndpointRouteBuilder app)
    {
        app.MapPost("/api/v1/games/{gameId:guid}/scores", SubmitAsync)
            .WithTags("Scores")
            .RequireAuthorization(AuthPolicies.GameServer)
            .RequireRateLimiting(RateLimitPolicies.ScoreSubmit)
            .WithName("SubmitScore")
            .WithSummary("Submits a score from a game server (API key + HMAC signature).")
            .WithDescription(
                "Headers: X-Api-Key (key id), X-Timestamp (unix seconds, ±300 s), X-Nonce (16-64 chars, unique per key), " +
                "X-Signature = Base64(HMAC-SHA256(secret, METHOD\\nPATH\\nTIMESTAMP\\nNONCE\\nhex(SHA256(body)))). " +
                "Re-sending the same nonce returns the original result with 200 (idempotent). " +
                "A personal best more than 10x better than the previous one is stored as PendingReview (202) " +
                "and only counts once a moderator approves it.")
            .Produces<SubmitScoreResponse>(StatusCodes.Status201Created)
            .Produces<SubmitScoreResponse>(StatusCodes.Status202Accepted)
            .Produces<SubmitScoreResponse>()
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity)
            .ProducesProblem(StatusCodes.Status429TooManyRequests);
    }

    private static async Task<IResult> SubmitAsync(
        Guid gameId,
        SubmitScoreRequest request,
        HttpContext http,
        [FromServices] ICommandHandler<SubmitScoreCommand, SubmitScoreResponse> handler,
        CancellationToken ct)
    {
        var user = http.User;
        var metadata = request.Metadata is { ValueKind: not JsonValueKind.Null and not JsonValueKind.Undefined } json ? json.GetRawText() : null;
        var command = new SubmitScoreCommand(
            gameId, user.GetApiKeyId(), user.GetApiKeyGameId(), request.PlayerId, request.Value, user.GetNonce(), metadata);

        return (await handler.HandleAsync(command, ct)).ToHttpResult(result => result switch
        {
            { IsReplay: true } => TypedResults.Ok(result),
            { Status: ScoreStatus.PendingReview } => TypedResults.Accepted((string?)null, result),
            _ => TypedResults.Created((string?)null, result),
        });
    }
}
