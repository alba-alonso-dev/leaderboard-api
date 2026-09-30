using FluentValidation;
using Leaderboard.Application.Abstractions.Messaging;
using Leaderboard.Application.Abstractions.Persistence;
using Leaderboard.Application.Abstractions.Security;
using Leaderboard.Domain.Common;
using Leaderboard.Domain.Players;

namespace Leaderboard.Application.Auth;

public sealed record RegisterPlayerCommand(string Username, string Email, string Password) : ICommand<PlayerResponse>;

public sealed class RegisterPlayerCommandValidator : AbstractValidator<RegisterPlayerCommand>
{
    public const int PasswordMinLength = 10;
    public const int PasswordMaxLength = 128;

    public RegisterPlayerCommandValidator()
    {
        RuleFor(x => x.Username)
            .NotEmpty()
            .Length(Player.UsernameMinLength, Player.UsernameMaxLength)
            .Matches("^[a-zA-Z0-9_-]+$").WithMessage("Username can only contain letters, digits, '_' and '-'.");

        RuleFor(x => x.Email).NotEmpty().EmailAddress().MaximumLength(Player.EmailMaxLength);

        RuleFor(x => x.Password)
            .NotEmpty()
            .Length(PasswordMinLength, PasswordMaxLength)
            .Must(p => p.Any(char.IsLetter) && p.Any(char.IsDigit))
            .WithMessage("Password must contain at least one letter and one digit.");
    }
}

internal sealed class RegisterPlayerCommandHandler(
    IPlayerRepository players, IPasswordHasher passwordHasher, IUnitOfWork unitOfWork, TimeProvider timeProvider)
    : ICommandHandler<RegisterPlayerCommand, PlayerResponse>
{
    public async Task<Result<PlayerResponse>> HandleAsync(RegisterPlayerCommand command, CancellationToken cancellationToken)
    {
        if (await players.EmailExistsAsync(command.Email.Trim().ToLowerInvariant(), cancellationToken))
        {
            return PlayerErrors.EmailTaken;
        }

        if (await players.UsernameExistsAsync(command.Username.Trim(), cancellationToken))
        {
            return PlayerErrors.UsernameTaken;
        }

        var player = Player.Register(command.Username, command.Email, timeProvider.GetUtcNow());
        player.SetPasswordHash(passwordHasher.Hash(player, command.Password));
        players.Add(player);

        try
        {
            await unitOfWork.SaveChangesAsync(cancellationToken);
        }
        catch (UniqueConstraintException ex)
        {
            // Lost a race against a concurrent registration with the same email/username.
            return ex.ConstraintName == UniqueConstraints.PlayerUsername ? PlayerErrors.UsernameTaken : PlayerErrors.EmailTaken;
        }

        return new PlayerResponse(player.Id, player.Username, player.CreatedAt);
    }
}
