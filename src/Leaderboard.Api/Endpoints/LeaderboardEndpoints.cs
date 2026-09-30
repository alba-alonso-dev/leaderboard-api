using Leaderboard.Api.Authentication;
using Leaderboard.Api.ErrorHandling;
using Leaderboard.Api.Infrastructure;
using Leaderboard.Application.Abstractions.Messaging;
using Leaderboard.Application.Common;
using Leaderboard.Application.Leaderboards;
using Microsoft.AspNetCore.Mvc;

namespace Leaderboard.Api.Endpoints;

public sealed class LeaderboardEndpoints : IEndpointGroup
{
    public void Map(IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/games/{gameId:guid}/leaderboard")
            .WithTags("Leaderboard")
            .RequireRateLimiting(RateLimitPolicies.PublicRead);

        group.MapGet("/", GetPageAsync)
            .AllowAnonymous()
            .WithName("GetLeaderboard")
            .WithSummary("Returns a page of the ranking (best score per player; ties go to who achieved it first).")
            .Produces<PagedResult<LeaderboardEntryResponse>>()
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapGet("/top", GetTopAsync)
            .AllowAnonymous()
            .WithName("GetTopScores")
            .WithSummary("Returns the top N players (1-100, default 10).")
            .Produces<IReadOnlyList<LeaderboardEntryResponse>>()
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapGet("/players/{playerId:guid}", GetPlayerRankAsync)
            .AllowAnonymous()
            .WithName("GetPlayerRank")
            .WithSummary("Returns the absolute position of a player: rank, best score, total players and percentile.")
            .Produces<PlayerRankResponse>()
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapGet("/players/{playerId:guid}/around", GetAroundAsync)
            .AllowAnonymous()
            .WithName("GetAroundPlayer")
            .WithSummary("Returns the relative position of a player: `range` players above and below (1-25, default 5).")
            .Produces<AroundPlayerResponse>()
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapGet("/me", GetMyRankAsync)
            .RequireAuthorization()
            .WithName("GetMyRank")
            .WithSummary("Returns the authenticated player's position in the game.")
            .Produces<PlayerRankResponse>()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status404NotFound);
    }

    private static async Task<IResult> GetPageAsync(
        Guid gameId,
        [FromServices] IQueryHandler<GetLeaderboardPageQuery, PagedResult<LeaderboardEntryResponse>> handler,
        CancellationToken ct,
        int page = 1,
        int pageSize = Paging.DefaultPageSize) =>
        (await handler.HandleAsync(new GetLeaderboardPageQuery(gameId, page, pageSize), ct)).ToHttpResult(TypedResults.Ok);

    private static async Task<IResult> GetTopAsync(
        Guid gameId, [FromServices] IQueryHandler<GetTopQuery, IReadOnlyList<LeaderboardEntryResponse>> handler, CancellationToken ct, int n = 10) =>
        (await handler.HandleAsync(new GetTopQuery(gameId, n), ct)).ToHttpResult(TypedResults.Ok);

    private static async Task<IResult> GetPlayerRankAsync(
        Guid gameId, Guid playerId, [FromServices] IQueryHandler<GetPlayerRankQuery, PlayerRankResponse> handler, CancellationToken ct) =>
        (await handler.HandleAsync(new GetPlayerRankQuery(gameId, playerId), ct)).ToHttpResult(TypedResults.Ok);

    private static async Task<IResult> GetAroundAsync(
        Guid gameId, Guid playerId, [FromServices] IQueryHandler<GetAroundPlayerQuery, AroundPlayerResponse> handler, CancellationToken ct, int range = 5) =>
        (await handler.HandleAsync(new GetAroundPlayerQuery(gameId, playerId, range), ct)).ToHttpResult(TypedResults.Ok);

    private static async Task<IResult> GetMyRankAsync(
        Guid gameId, HttpContext http, [FromServices] IQueryHandler<GetPlayerRankQuery, PlayerRankResponse> handler, CancellationToken ct) =>
        (await handler.HandleAsync(new GetPlayerRankQuery(gameId, http.User.GetPlayerId()), ct)).ToHttpResult(TypedResults.Ok);
}
