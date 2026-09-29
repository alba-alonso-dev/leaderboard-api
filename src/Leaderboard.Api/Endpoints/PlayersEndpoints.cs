using Leaderboard.Api.Authentication;
using Leaderboard.Api.ErrorHandling;
using Leaderboard.Api.Infrastructure;
using Leaderboard.Application.Abstractions.Messaging;
using Leaderboard.Application.Auth;
using Leaderboard.Application.Common;
using Leaderboard.Application.Players;
using Microsoft.AspNetCore.Mvc;

namespace Leaderboard.Api.Endpoints;

public sealed class PlayersEndpoints : IEndpointGroup
{
    public void Map(IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/players").WithTags("Players");

        group.MapGet("/me", GetMeAsync)
            .RequireAuthorization()
            .WithName("GetMyProfile")
            .WithSummary("Returns the authenticated player's profile.")
            .Produces<PlayerProfileResponse>()
            .ProducesProblem(StatusCodes.Status401Unauthorized);

        group.MapGet("/me/scores", GetMyScoresAsync)
            .RequireAuthorization()
            .WithName("GetMyScores")
            .WithSummary("Returns the authenticated player's score history, newest first.")
            .Produces<PagedResult<ScoreHistoryItemResponse>>()
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status401Unauthorized);

        group.MapGet("/{playerId:guid}", GetPlayerAsync)
            .AllowAnonymous()
            .RequireRateLimiting(RateLimitPolicies.PublicRead)
            .WithName("GetPlayer")
            .WithSummary("Returns a player's public profile.")
            .Produces<PlayerResponse>()
            .ProducesProblem(StatusCodes.Status404NotFound);
    }

    private static async Task<IResult> GetMeAsync(
        HttpContext http, [FromServices] IQueryHandler<GetMyProfileQuery, PlayerProfileResponse> handler, CancellationToken ct) =>
        (await handler.HandleAsync(new GetMyProfileQuery(http.User.GetPlayerId()), ct)).ToHttpResult(TypedResults.Ok);

    private static async Task<IResult> GetMyScoresAsync(
        HttpContext http,
        [FromServices] IQueryHandler<GetMyScoresQuery, PagedResult<ScoreHistoryItemResponse>> handler,
        CancellationToken ct,
        Guid? gameId = null,
        int page = 1,
        int pageSize = Paging.DefaultPageSize) =>
        (await handler.HandleAsync(new GetMyScoresQuery(http.User.GetPlayerId(), gameId, page, pageSize), ct)).ToHttpResult(TypedResults.Ok);

    private static async Task<IResult> GetPlayerAsync(
        Guid playerId, [FromServices] IQueryHandler<GetPlayerQuery, PlayerResponse> handler, CancellationToken ct) =>
        (await handler.HandleAsync(new GetPlayerQuery(playerId), ct)).ToHttpResult(TypedResults.Ok);
}
