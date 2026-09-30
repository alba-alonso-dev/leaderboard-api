using FluentValidation;
using Leaderboard.Application.Abstractions.Messaging;
using Leaderboard.Application.Abstractions.Persistence;
using Leaderboard.Application.Abstractions.Security;
using Leaderboard.Domain.Common;
using Leaderboard.Domain.Players;

namespace Leaderboard.Application.Auth;

public sealed record RefreshTokenCommand(string RefreshToken) : ICommand<TokenResponse>;

public sealed class RefreshTokenCommandValidator : AbstractValidator<RefreshTokenCommand>
{
    public RefreshTokenCommandValidator() => RuleFor(x => x.RefreshToken).NotEmpty().MaximumLength(256);
}

internal sealed class RefreshTokenCommandHandler(
    IRefreshTokenRepository refreshTokens,
    IPlayerRepository players,
    ITokenService tokenService,
    TokenIssuer tokenIssuer,
    IUnitOfWork unitOfWork,
    TimeProvider timeProvider)
    : ICommandHandler<RefreshTokenCommand, TokenResponse>
{
    public async Task<Result<TokenResponse>> HandleAsync(RefreshTokenCommand command, CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();
        var current = await refreshTokens.GetByHashAsync(tokenService.HashRefreshToken(command.RefreshToken), cancellationToken);
        if (current is null)
        {
            return PlayerErrors.InvalidRefreshToken;
        }

        if (!current.IsActive(now))
        {
            if (current.WasRotated)
            {
                // A rotated token was presented again: someone else holds the chain. Revoke all of it.
                await refreshTokens.RevokeFamilyAsync(current.FamilyId, now, cancellationToken);
            }

            return PlayerErrors.InvalidRefreshToken;
        }

        var player = await players.GetByIdAsync(current.PlayerId, cancellationToken);
        if (player is null)
        {
            return PlayerErrors.InvalidRefreshToken;
        }

        var response = tokenIssuer.Issue(player, current.FamilyId, out var replacement);
        current.Revoke(now, replacement.Id);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return response;
    }
}

public sealed record LogoutCommand(Guid PlayerId, string RefreshToken) : ICommand<Unit>;

public sealed class LogoutCommandValidator : AbstractValidator<LogoutCommand>
{
    public LogoutCommandValidator() => RuleFor(x => x.RefreshToken).NotEmpty().MaximumLength(256);
}

/// <summary>Idempotent: unknown or foreign tokens are ignored so the endpoint never leaks token validity.</summary>
internal sealed class LogoutCommandHandler(
    IRefreshTokenRepository refreshTokens, ITokenService tokenService, IUnitOfWork unitOfWork, TimeProvider timeProvider)
    : ICommandHandler<LogoutCommand, Unit>
{
    public async Task<Result<Unit>> HandleAsync(LogoutCommand command, CancellationToken cancellationToken)
    {
        var token = await refreshTokens.GetByHashAsync(tokenService.HashRefreshToken(command.RefreshToken), cancellationToken);
        if (token is not null && token.PlayerId == command.PlayerId)
        {
            token.Revoke(timeProvider.GetUtcNow());
            await unitOfWork.SaveChangesAsync(cancellationToken);
        }

        return Unit.Value;
    }
}
