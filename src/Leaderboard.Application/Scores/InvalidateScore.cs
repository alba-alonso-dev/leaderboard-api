using Leaderboard.Application.Abstractions.Messaging;
using Leaderboard.Application.Abstractions.Persistence;
using Leaderboard.Domain.Common;
using Leaderboard.Domain.Scores;

namespace Leaderboard.Application.Scores;

/// <summary>Moderation: marks a score as rejected and rebuilds the player's leaderboard entry from the remaining history.</summary>
public sealed record InvalidateScoreCommand(Guid ScoreId) : ICommand<Unit>;

internal sealed class InvalidateScoreCommandHandler(IScoreRepository scores, IGameRepository games, IUnitOfWork unitOfWork)
    : ICommandHandler<InvalidateScoreCommand, Unit>
{
    public async Task<Result<Unit>> HandleAsync(InvalidateScoreCommand command, CancellationToken cancellationToken)
    {
        var score = await scores.GetByIdAsync(command.ScoreId, cancellationToken);
        if (score is null)
        {
            return ScoreErrors.NotFound;
        }

        if (score.Status == ScoreStatus.Rejected)
        {
            return Unit.Value;
        }

        var game = await games.GetByIdAsync(score.GameId, cancellationToken)
                   ?? throw new InvalidOperationException($"Score {score.Id} references missing game {score.GameId}.");

        await unitOfWork.ExecuteInTransactionAsync(
            async ct =>
            {
                score.Reject();
                await unitOfWork.SaveChangesAsync(ct);
                await scores.RecalculateLeaderboardEntryAsync(game, score.PlayerId, ct);
                return true;
            },
            cancellationToken);

        return Unit.Value;
    }
}
