using System.Globalization;
using Leaderboard.Api.Authentication;
using Leaderboard.Api.ErrorHandling;
using Leaderboard.Api.Infrastructure;
using Leaderboard.Application.Abstractions.Messaging;
using Leaderboard.Application.ApiKeys;
using Leaderboard.Application.Common;
using Leaderboard.Application.Games;
using Leaderboard.Domain.Games;
using Microsoft.AspNetCore.Mvc;

namespace Leaderboard.Api.Endpoints;

public sealed record CreateGameRequest(
    string Name, string Slug, string? Description, ScoreOrder ScoreOrder = ScoreOrder.HigherIsBetter, long? MinScore = null, long? MaxScore = null);

public sealed record UpdateGameRequest(string Name, string? Description, long? MinScore, long? MaxScore);

public sealed record IssueApiKeyRequest(string Name);

public sealed class GamesEndpoints : IEndpointGroup
{
    public void Map(IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/games").WithTags("Games");

        group.MapPost("/", CreateAsync)
            .RequireAuthorization()
            .WithName("CreateGame")
            .WithSummary("Registers a game. The authenticated player becomes its owner.")
            .Produces<GameResponse>(StatusCodes.Status201Created)
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status409Conflict);

        group.MapGet("/", ListAsync)
            .AllowAnonymous()
            .RequireRateLimiting(RateLimitPolicies.PublicRead)
            .WithName("ListGames")
            .WithSummary("Lists active games, optionally filtered by name or slug.")
            .Produces<PagedResult<GameResponse>>()
            .ProducesValidationProblem();

        group.MapGet("/{gameId:guid}", GetAsync)
            .AllowAnonymous()
            .RequireRateLimiting(RateLimitPolicies.PublicRead)
            .WithName("GetGame")
            .WithSummary("Returns a game. The ETag header carries its version for conditional updates.")
            .Produces<GameResponse>()
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapPut("/{gameId:guid}", UpdateAsync)
            .RequireAuthorization()
            .WithName("UpdateGame")
            .WithSummary("Replaces the editable metadata of a game you own. Send If-Match with the ETag to avoid lost updates.")
            .Produces<GameResponse>()
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status412PreconditionFailed);

        group.MapDelete("/{gameId:guid}", ArchiveAsync)
            .RequireAuthorization()
            .WithName("ArchiveGame")
            .WithSummary("Archives a game you own. It stops accepting scores; its leaderboard stays readable.")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status404NotFound);

        var keys = group.MapGroup("/{gameId:guid}/api-keys").WithTags("API Keys").RequireAuthorization();

        keys.MapPost("/", IssueKeyAsync)
            .WithName("IssueApiKey")
            .WithSummary("Issues a server credential for a game you own. The secret is shown only in this response.")
            .Produces<IssuedApiKeyResponse>(StatusCodes.Status201Created)
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        keys.MapGet("/", ListKeysAsync)
            .WithName("ListApiKeys")
            .WithSummary("Lists the API keys of a game you own (never includes secrets).")
            .Produces<IReadOnlyList<ApiKeyResponse>>()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status404NotFound);

        keys.MapDelete("/{apiKeyId:guid}", RevokeKeyAsync)
            .WithName("RevokeApiKey")
            .WithSummary("Revokes an API key. Requests signed with it are rejected immediately.")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status404NotFound);
    }

    private static async Task<IResult> CreateAsync(
        CreateGameRequest request, HttpContext http, [FromServices] ICommandHandler<CreateGameCommand, GameResponse> handler, CancellationToken ct)
    {
        var command = new CreateGameCommand(
            http.User.GetPlayerId(), request.Name, request.Slug, request.Description, request.ScoreOrder, request.MinScore, request.MaxScore);
        return (await handler.HandleAsync(command, ct)).ToHttpResult(game =>
        {
            SetETag(http, game);
            return TypedResults.Created($"/api/v1/games/{game.Id}", game);
        });
    }

    private static async Task<IResult> ListAsync(
        [FromServices] IQueryHandler<ListGamesQuery, PagedResult<GameResponse>> handler,
        CancellationToken ct,
        string? search = null,
        int page = 1,
        int pageSize = Paging.DefaultPageSize) =>
        (await handler.HandleAsync(new ListGamesQuery(search, page, pageSize), ct)).ToHttpResult(TypedResults.Ok);

    private static async Task<IResult> GetAsync(
        Guid gameId, HttpContext http, [FromServices] IQueryHandler<GetGameQuery, GameResponse> handler, CancellationToken ct) =>
        (await handler.HandleAsync(new GetGameQuery(gameId), ct)).ToHttpResult(game =>
        {
            SetETag(http, game);
            return TypedResults.Ok(game);
        });

    private static async Task<IResult> UpdateAsync(
        Guid gameId,
        UpdateGameRequest request,
        HttpContext http,
        [FromServices] ICommandHandler<UpdateGameCommand, GameResponse> handler,
        CancellationToken ct)
    {
        var command = new UpdateGameCommand(
            gameId, http.User.GetPlayerId(), request.Name, request.Description, request.MinScore, request.MaxScore, ParseIfMatch(http));
        return (await handler.HandleAsync(command, ct)).ToHttpResult(game =>
        {
            SetETag(http, game);
            return TypedResults.Ok(game);
        });
    }

    private static async Task<IResult> ArchiveAsync(
        Guid gameId, HttpContext http, [FromServices] ICommandHandler<ArchiveGameCommand, Unit> handler, CancellationToken ct) =>
        (await handler.HandleAsync(new ArchiveGameCommand(gameId, http.User.GetPlayerId()), ct)).ToHttpResult(_ => TypedResults.NoContent());

    private static async Task<IResult> IssueKeyAsync(
        Guid gameId,
        IssueApiKeyRequest request,
        HttpContext http,
        [FromServices] ICommandHandler<IssueApiKeyCommand, IssuedApiKeyResponse> handler,
        CancellationToken ct) =>
        (await handler.HandleAsync(new IssueApiKeyCommand(gameId, http.User.GetPlayerId(), request.Name), ct))
            .ToHttpResult(key => TypedResults.Created($"/api/v1/games/{gameId}/api-keys/{key.Id}", key));

    private static async Task<IResult> ListKeysAsync(
        Guid gameId, HttpContext http, [FromServices] IQueryHandler<ListApiKeysQuery, IReadOnlyList<ApiKeyResponse>> handler, CancellationToken ct) =>
        (await handler.HandleAsync(new ListApiKeysQuery(gameId, http.User.GetPlayerId()), ct)).ToHttpResult(TypedResults.Ok);

    private static async Task<IResult> RevokeKeyAsync(
        Guid gameId, Guid apiKeyId, HttpContext http, [FromServices] ICommandHandler<RevokeApiKeyCommand, Unit> handler, CancellationToken ct) =>
        (await handler.HandleAsync(new RevokeApiKeyCommand(gameId, http.User.GetPlayerId(), apiKeyId), ct))
            .ToHttpResult(_ => TypedResults.NoContent());

    private static void SetETag(HttpContext http, GameResponse game) =>
        http.Response.Headers.ETag = $"\"{game.Version.ToString(CultureInfo.InvariantCulture)}\"";

    private static uint? ParseIfMatch(HttpContext http)
    {
        var value = http.Request.Headers.IfMatch.ToString().Trim();
        if (value.StartsWith("W/", StringComparison.Ordinal))
        {
            value = value[2..];
        }

        return uint.TryParse(value.Trim('"'), NumberStyles.None, CultureInfo.InvariantCulture, out var version) ? version : null;
    }
}
