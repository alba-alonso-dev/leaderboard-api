using FluentValidation;
using Leaderboard.Application.Abstractions.Messaging;
using Leaderboard.Application.Abstractions.Persistence;
using Leaderboard.Application.Common;
using Leaderboard.Domain.Common;
using Leaderboard.Domain.Games;
using Leaderboard.Domain.Scores;

namespace Leaderboard.Application.Leaderboards;

public sealed record LeaderboardEntryResponse(long Rank, Guid PlayerId, string Username, long Score, DateTimeOffset AchievedAt);

public sealed record PlayerRankResponse(
    Guid PlayerId, string Username, long Rank, long Score, DateTimeOffset AchievedAt, long TotalPlayers, double Percentile);

public sealed record AroundEntryResponse(long Rank, Guid PlayerId, string Username, long Score, DateTimeOffset AchievedAt, bool IsTarget);

public sealed record AroundPlayerResponse(PlayerRankResponse Target, IReadOnlyList<AroundEntryResponse> Entries);

internal static class LeaderboardMapping
{
    public static LeaderboardEntryResponse ToResponse(this LeaderboardRow row, long rank) =>
        new(rank, row.PlayerId, row.Username, row.BestScore, row.AchievedAt);

    /// <summary>"Top X %": 100 for the leader, approaching 0 for the last player. Rounded to one decimal.</summary>
    public static double Percentile(long rank, long total) =>
        total == 0 ? 0 : Math.Round(100.0 * (1 - ((rank - 1) / (double)total)), 1, MidpointRounding.AwayFromZero);

    public static async Task<Result> EnsureGameExistsAsync(this IGameRepository games, Guid gameId, CancellationToken ct) =>
        await games.ExistsAsync(gameId, ct) ? Result.Success() : GameErrors.NotFound;
}

// ---- Top N --------------------------------------------------------------------------------------------------------

public sealed record GetTopQuery(Guid GameId, int N) : IQuery<IReadOnlyList<LeaderboardEntryResponse>>;

public sealed class GetTopQueryValidator : AbstractValidator<GetTopQuery>
{
    public const int MaxN = 100;

    public GetTopQueryValidator() => RuleFor(x => x.N).InclusiveBetween(1, MaxN).OverridePropertyName("n");
}

internal sealed class GetTopQueryHandler(IGameRepository games, ILeaderboardReadService leaderboard)
    : IQueryHandler<GetTopQuery, IReadOnlyList<LeaderboardEntryResponse>>
{
    public async Task<Result<IReadOnlyList<LeaderboardEntryResponse>>> HandleAsync(GetTopQuery query, CancellationToken cancellationToken)
    {
        var exists = await games.EnsureGameExistsAsync(query.GameId, cancellationToken);
        if (exists.IsFailure)
        {
            return exists.Error!;
        }

        var rows = await leaderboard.GetRangeAsync(query.GameId, 0, query.N, cancellationToken);
        return rows.Select((row, i) => row.ToResponse(i + 1L)).ToList();
    }
}

// ---- Page ---------------------------------------------------------------------------------------------------------

public sealed record GetLeaderboardPageQuery(Guid GameId, int Page, int PageSize) : IQuery<PagedResult<LeaderboardEntryResponse>>;

public sealed class GetLeaderboardPageQueryValidator : AbstractValidator<GetLeaderboardPageQuery>
{
    public GetLeaderboardPageQueryValidator() => this.ValidatePaging(x => x.Page, x => x.PageSize);
}

internal sealed class GetLeaderboardPageQueryHandler(IGameRepository games, ILeaderboardReadService leaderboard)
    : IQueryHandler<GetLeaderboardPageQuery, PagedResult<LeaderboardEntryResponse>>
{
    public async Task<Result<PagedResult<LeaderboardEntryResponse>>> HandleAsync(
        GetLeaderboardPageQuery query, CancellationToken cancellationToken)
    {
        var exists = await games.EnsureGameExistsAsync(query.GameId, cancellationToken);
        if (exists.IsFailure)
        {
            return exists.Error!;
        }

        var offset = Paging.Offset(query.Page, query.PageSize);
        var rows = await leaderboard.GetRangeAsync(query.GameId, offset, query.PageSize, cancellationToken);
        var total = await leaderboard.CountPlayersAsync(query.GameId, cancellationToken);

        var items = rows.Select((row, i) => row.ToResponse(offset + i + 1L)).ToList();
        return new PagedResult<LeaderboardEntryResponse>(items, query.Page, query.PageSize, total);
    }
}

// ---- Absolute position --------------------------------------------------------------------------------------------

public sealed record GetPlayerRankQuery(Guid GameId, Guid PlayerId) : IQuery<PlayerRankResponse>;

internal sealed class GetPlayerRankQueryHandler(IGameRepository games, ILeaderboardReadService leaderboard)
    : IQueryHandler<GetPlayerRankQuery, PlayerRankResponse>
{
    public async Task<Result<PlayerRankResponse>> HandleAsync(GetPlayerRankQuery query, CancellationToken cancellationToken)
    {
        var exists = await games.EnsureGameExistsAsync(query.GameId, cancellationToken);
        if (exists.IsFailure)
        {
            return exists.Error!;
        }

        var ranked = await leaderboard.GetPlayerRankAsync(query.GameId, query.PlayerId, cancellationToken);
        if (ranked is null)
        {
            return ScoreErrors.EntryNotFound;
        }

        var total = await leaderboard.CountPlayersAsync(query.GameId, cancellationToken);
        return ToRankResponse(ranked, total);
    }

    internal static PlayerRankResponse ToRankResponse(RankedRow ranked, long total) => new(
        ranked.Row.PlayerId, ranked.Row.Username, ranked.Rank, ranked.Row.BestScore, ranked.Row.AchievedAt,
        total, LeaderboardMapping.Percentile(ranked.Rank, total));
}

// ---- Relative position --------------------------------------------------------------------------------------------

public sealed record GetAroundPlayerQuery(Guid GameId, Guid PlayerId, int Range) : IQuery<AroundPlayerResponse>;

public sealed class GetAroundPlayerQueryValidator : AbstractValidator<GetAroundPlayerQuery>
{
    public const int MaxRange = 25;

    public GetAroundPlayerQueryValidator() => RuleFor(x => x.Range).InclusiveBetween(1, MaxRange).OverridePropertyName("range");
}

internal sealed class GetAroundPlayerQueryHandler(IGameRepository games, ILeaderboardReadService leaderboard)
    : IQueryHandler<GetAroundPlayerQuery, AroundPlayerResponse>
{
    public async Task<Result<AroundPlayerResponse>> HandleAsync(GetAroundPlayerQuery query, CancellationToken cancellationToken)
    {
        var exists = await games.EnsureGameExistsAsync(query.GameId, cancellationToken);
        if (exists.IsFailure)
        {
            return exists.Error!;
        }

        var target = await leaderboard.GetPlayerRankAsync(query.GameId, query.PlayerId, cancellationToken);
        if (target is null)
        {
            return ScoreErrors.EntryNotFound;
        }

        // Two keyset queries on the ranking index instead of numbering the whole table.
        var above = await leaderboard.GetAboveAsync(query.GameId, target.Row, query.Range, cancellationToken);
        var below = await leaderboard.GetBelowAsync(query.GameId, target.Row, query.Range, cancellationToken);
        var total = await leaderboard.CountPlayersAsync(query.GameId, cancellationToken);

        var entries = new List<AroundEntryResponse>(above.Count + 1 + below.Count);
        entries.AddRange(above.Select((row, i) => ToEntry(row, target.Rank - above.Count + i, isTarget: false)));
        entries.Add(ToEntry(target.Row, target.Rank, isTarget: true));
        entries.AddRange(below.Select((row, i) => ToEntry(row, target.Rank + i + 1, isTarget: false)));

        return new AroundPlayerResponse(GetPlayerRankQueryHandler.ToRankResponse(target, total), entries);
    }

    private static AroundEntryResponse ToEntry(LeaderboardRow row, long rank, bool isTarget) =>
        new(rank, row.PlayerId, row.Username, row.BestScore, row.AchievedAt, isTarget);
}
