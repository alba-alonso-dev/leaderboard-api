using FluentValidation;
using Leaderboard.Application.Abstractions;
using Leaderboard.Application.Abstractions.Messaging;
using Leaderboard.Application.Abstractions.Persistence;
using Leaderboard.Domain.Common;
using Leaderboard.Domain.Games;
using Leaderboard.Domain.Scores;
using Microsoft.Extensions.Options;

namespace Leaderboard.Application.Scores;

/// <param name="ApiKeyGameId">Game the authenticated API key belongs to (must match <paramref name="GameId"/>).</param>
public sealed record SubmitScoreCommand(
    Guid GameId, Guid ApiKeyId, Guid ApiKeyGameId, Guid PlayerId, long Value, string Nonce, string? Metadata)
    : ICommand<SubmitScoreResponse>;

/// <param name="IsReplay">True when the same nonce was already processed; the original outcome is returned (idempotency).</param>
public sealed record SubmitScoreResponse(
    Guid ScoreId, long Value, ScoreStatus Status, bool IsPersonalBest, long BestScore, long Rank, long TotalPlayers, bool IsReplay);

public sealed class SubmitScoreCommandValidator : AbstractValidator<SubmitScoreCommand>
{
    public SubmitScoreCommandValidator()
    {
        RuleFor(x => x.PlayerId).NotEmpty();
        RuleFor(x => x.Value).GreaterThan(long.MinValue);
        RuleFor(x => x.Nonce).NotEmpty().Length(Score.NonceMinLength, Score.NonceMaxLength);
        RuleFor(x => x.Metadata)
            .MaximumLength(Score.MetadataMaxLength).WithMessage($"Metadata cannot exceed {Score.MetadataMaxLength} characters.")
            .Must(BeJsonObject).WithMessage("Metadata must be a JSON object.")
            .When(x => x.Metadata is not null);
    }

    private static bool BeJsonObject(string? json)
    {
        try
        {
            using var document = System.Text.Json.JsonDocument.Parse(json!);
            return document.RootElement.ValueKind == System.Text.Json.JsonValueKind.Object;
        }
        catch (System.Text.Json.JsonException)
        {
            return false;
        }
    }
}

internal sealed class SubmitScoreCommandHandler(
    IGameRepository games,
    IPlayerRepository players,
    IScoreRepository scores,
    ILeaderboardReadService leaderboard,
    IUnitOfWork unitOfWork,
    IOptions<ScoreSubmissionOptions> options,
    TimeProvider timeProvider)
    : ICommandHandler<SubmitScoreCommand, SubmitScoreResponse>
{
    public async Task<Result<SubmitScoreResponse>> HandleAsync(SubmitScoreCommand command, CancellationToken cancellationToken)
    {
        if (command.ApiKeyGameId != command.GameId)
        {
            return ApiKeyErrors.GameMismatch;
        }

        var previous = await scores.GetByNonceAsync(command.ApiKeyId, command.Nonce, cancellationToken);
        if (previous is not null)
        {
            return await ReplayAsync(previous, command, cancellationToken);
        }

        var game = await games.GetByIdAsync(command.GameId, cancellationToken);
        if (game is null)
        {
            return GameErrors.NotFound;
        }

        var accepted = game.EnsureAcceptsScore(command.Value);
        if (accepted.IsFailure)
        {
            return accepted.Error!;
        }

        if (!await players.ExistsAsync(command.PlayerId, cancellationToken))
        {
            return ScoreErrors.PlayerNotFound;
        }

        var now = timeProvider.GetUtcNow();
        var limit = options.Value.MaxPerPlayerPerMinute;
        if (limit > 0 && await scores.CountSinceAsync(game.Id, command.PlayerId, now.AddMinutes(-1), cancellationToken) >= limit)
        {
            return ScoreErrors.PlayerRateLimited;
        }

        var score = Score.Submit(game.Id, command.PlayerId, command.ApiKeyId, command.Value, command.Nonce, command.Metadata, now);
        try
        {
            await unitOfWork.ExecuteInTransactionAsync(
                async ct =>
                {
                    scores.Add(score);
                    await unitOfWork.SaveChangesAsync(ct);
                    await scores.UpsertLeaderboardEntryAsync(score, game.ToRankKey(score.Value), ct);
                    return true;
                },
                cancellationToken);
        }
        catch (UniqueConstraintException ex) when (ex.ConstraintName == UniqueConstraints.ScoreNonce)
        {
            // A concurrent request with the same nonce won the race: answer like a replay.
            var winner = await scores.GetByNonceAsync(command.ApiKeyId, command.Nonce, cancellationToken);
            return winner is null ? throw new InvalidOperationException("Nonce conflict without stored score.", ex)
                                  : await ReplayAsync(winner, command, cancellationToken);
        }

        return await BuildResponseAsync(score, isReplay: false, cancellationToken);
    }

    private async Task<Result<SubmitScoreResponse>> ReplayAsync(Score previous, SubmitScoreCommand command, CancellationToken ct)
    {
        if (previous.PlayerId != command.PlayerId || previous.Value != command.Value || previous.GameId != command.GameId)
        {
            return ScoreErrors.NonceReused;
        }

        return await BuildResponseAsync(previous, isReplay: true, ct);
    }

    private async Task<SubmitScoreResponse> BuildResponseAsync(Score score, bool isReplay, CancellationToken ct)
    {
        var ranked = await leaderboard.GetPlayerRankAsync(score.GameId, score.PlayerId, ct);
        var total = await leaderboard.CountPlayersAsync(score.GameId, ct);

        return new SubmitScoreResponse(
            score.Id,
            score.Value,
            score.Status,
            IsPersonalBest: ranked?.Row.ScoreId == score.Id,
            BestScore: ranked?.Row.BestScore ?? score.Value,
            Rank: ranked?.Rank ?? 0,
            TotalPlayers: total,
            isReplay);
    }
}
