using Leaderboard.Api.Authentication;
using Leaderboard.Api.ErrorHandling;
using Leaderboard.Api.Infrastructure;
using Leaderboard.Application.Abstractions.Messaging;
using Leaderboard.Application.Auth;
using Microsoft.AspNetCore.Mvc;

namespace Leaderboard.Api.Endpoints;

public sealed record LogoutRequest(string RefreshToken);

public sealed class AuthEndpoints : IEndpointGroup
{
    public void Map(IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/auth").WithTags("Auth");

        group.MapPost("/register", RegisterAsync)
            .AllowAnonymous()
            .RequireRateLimiting(RateLimitPolicies.Auth)
            .WithName("RegisterPlayer")
            .WithSummary("Registers a new player.")
            .Produces<PlayerResponse>(StatusCodes.Status201Created)
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status429TooManyRequests);

        group.MapPost("/login", LoginAsync)
            .AllowAnonymous()
            .RequireRateLimiting(RateLimitPolicies.Auth)
            .WithName("Login")
            .WithSummary("Exchanges email + password for an access token (JWT, 15 min) and a rotating refresh token.")
            .Produces<TokenResponse>()
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status429TooManyRequests);

        group.MapPost("/refresh", RefreshAsync)
            .AllowAnonymous()
            .RequireRateLimiting(RateLimitPolicies.Auth)
            .WithName("RefreshToken")
            .WithSummary("Rotates the refresh token. Reusing an already rotated token revokes the whole token family.")
            .Produces<TokenResponse>()
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status401Unauthorized);

        group.MapPost("/logout", LogoutAsync)
            .RequireAuthorization()
            .WithName("Logout")
            .WithSummary("Revokes the given refresh token.")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status401Unauthorized);
    }

    private static async Task<IResult> RegisterAsync(
        RegisterPlayerCommand command, [FromServices] ICommandHandler<RegisterPlayerCommand, PlayerResponse> handler, CancellationToken ct) =>
        (await handler.HandleAsync(command, ct)).ToHttpResult(player => TypedResults.Created($"/api/v1/players/{player.Id}", player));

    private static async Task<IResult> LoginAsync(
        LoginCommand command, [FromServices] ICommandHandler<LoginCommand, TokenResponse> handler, CancellationToken ct) =>
        (await handler.HandleAsync(command, ct)).ToHttpResult(TypedResults.Ok);

    private static async Task<IResult> RefreshAsync(
        RefreshTokenCommand command, [FromServices] ICommandHandler<RefreshTokenCommand, TokenResponse> handler, CancellationToken ct) =>
        (await handler.HandleAsync(command, ct)).ToHttpResult(TypedResults.Ok);

    private static async Task<IResult> LogoutAsync(
        LogoutRequest request, HttpContext http, [FromServices] ICommandHandler<LogoutCommand, Unit> handler, CancellationToken ct) =>
        (await handler.HandleAsync(new LogoutCommand(http.User.GetPlayerId(), request.RefreshToken), ct))
            .ToHttpResult(_ => TypedResults.NoContent());
}
