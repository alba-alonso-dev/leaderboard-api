using FluentValidation;
using Leaderboard.Application.Abstractions.Messaging;
using Leaderboard.Application.Abstractions.Persistence;
using Leaderboard.Application.Common;
using Leaderboard.Domain.Common;
using Leaderboard.Domain.Players;
using Leaderboard.Domain.Scores;

namespace Leaderboard.Application.Players;

public sealed record PlayerProfileResponse(Guid Id, string Username, string Email, string Role, DateTimeOffset CreatedAt);

public sealed record GetMyProfileQuery(Guid PlayerId) : IQuery<PlayerProfileResponse>;

internal sealed class GetMyProfileQueryHandler(IPlayerRepository players) : IQueryHandler<GetMyProfileQuery, PlayerProfileResponse>
{
    public async Task<Result<PlayerProfileResponse>> HandleAsync(GetMyProfileQuery query, CancellationToken cancellationToken)
    {
        var player = await players.GetByIdAsync(query.PlayerId, cancellationToken);
        return player is null
            ? PlayerErrors.NotFound
            : new PlayerProfileResponse(player.Id, player.Username, player.Email, player.Role, player.CreatedAt);
    }
}

public sealed record ScoreHistoryItemResponse(Guid ScoreId, Guid GameId, long Value, ScoreStatus Status, DateTimeOffset SubmittedAt);

public sealed record GetMyScoresQuery(Guid PlayerId, Guid? GameId, int Page, int PageSize) : IQuery<PagedResult<ScoreHistoryItemResponse>>;

public sealed class GetMyScoresQueryValidator : AbstractValidator<GetMyScoresQuery>
{
    public GetMyScoresQueryValidator() => this.ValidatePaging(x => x.Page, x => x.PageSize);
}

internal sealed class GetMyScoresQueryHandler(IScoreRepository scores)
    : IQueryHandler<GetMyScoresQuery, PagedResult<ScoreHistoryItemResponse>>
{
    public async Task<Result<PagedResult<ScoreHistoryItemResponse>>> HandleAsync(GetMyScoresQuery query, CancellationToken cancellationToken)
    {
        var page = await scores.ListByPlayerAsync(query.PlayerId, query.GameId, query.Page, query.PageSize, cancellationToken);
        var items = page.Items.Select(s => new ScoreHistoryItemResponse(s.Id, s.GameId, s.Value, s.Status, s.SubmittedAt)).ToList();
        return new PagedResult<ScoreHistoryItemResponse>(items, page.Page, page.PageSize, page.TotalCount);
    }
}

public sealed record GetPlayerQuery(Guid PlayerId) : IQuery<Auth.PlayerResponse>;

internal sealed class GetPlayerQueryHandler(IPlayerRepository players) : IQueryHandler<GetPlayerQuery, Auth.PlayerResponse>
{
    public async Task<Result<Auth.PlayerResponse>> HandleAsync(GetPlayerQuery query, CancellationToken cancellationToken)
    {
        var player = await players.GetByIdAsync(query.PlayerId, cancellationToken);
        return player is null ? PlayerErrors.NotFound : new Auth.PlayerResponse(player.Id, player.Username, player.CreatedAt);
    }
}
