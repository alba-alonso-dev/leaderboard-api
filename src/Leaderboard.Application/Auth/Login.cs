using FluentValidation;
using Leaderboard.Application.Abstractions.Messaging;
using Leaderboard.Application.Abstractions.Persistence;
using Leaderboard.Application.Abstractions.Security;
using Leaderboard.Domain.Common;
using Leaderboard.Domain.Players;

namespace Leaderboard.Application.Auth;

public sealed record LoginCommand(string Email, string Password) : ICommand<TokenResponse>;

public sealed class LoginCommandValidator : AbstractValidator<LoginCommand>
{
    public LoginCommandValidator()
    {
        RuleFor(x => x.Email).NotEmpty().MaximumLength(Player.EmailMaxLength);
        RuleFor(x => x.Password).NotEmpty().MaximumLength(RegisterPlayerCommandValidator.PasswordMaxLength);
    }
}

internal sealed class LoginCommandHandler(
    IPlayerRepository players, IPasswordHasher passwordHasher, TokenIssuer tokenIssuer, IUnitOfWork unitOfWork)
    : ICommandHandler<LoginCommand, TokenResponse>
{
    // Verified when the email does not exist so both failure paths cost the same (no user enumeration by timing).
    private static readonly Player DummyPlayer = Player.Register("dummy", "dummy@example.invalid", DateTimeOffset.UnixEpoch);
    private static string? _dummyHash;

    public async Task<Result<TokenResponse>> HandleAsync(LoginCommand command, CancellationToken cancellationToken)
    {
        var player = await players.GetByEmailAsync(command.Email.Trim().ToLowerInvariant(), cancellationToken);
        if (player is null)
        {
            _dummyHash ??= passwordHasher.Hash(DummyPlayer, "not-a-real-password-1");
            passwordHasher.Verify(DummyPlayer, _dummyHash, command.Password);
            return PlayerErrors.InvalidCredentials;
        }

        if (!passwordHasher.Verify(player, player.PasswordHash, command.Password))
        {
            return PlayerErrors.InvalidCredentials;
        }

        var response = tokenIssuer.Issue(player, familyId: null, out _);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return response;
    }
}
