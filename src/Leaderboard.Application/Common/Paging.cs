using FluentValidation;

namespace Leaderboard.Application.Common;

public static class Paging
{
    public const int DefaultPageSize = 20;
    public const int MaxPageSize = 100;

    public static int Offset(int page, int pageSize) => (page - 1) * pageSize;
}

/// <summary>Standard envelope for every paginated collection.</summary>
public sealed record PagedResult<T>(IReadOnlyList<T> Items, int Page, int PageSize, long TotalCount)
{
    public int TotalPages => TotalCount == 0 ? 0 : (int)Math.Ceiling(TotalCount / (double)PageSize);

    public bool HasPreviousPage => Page > 1;

    public bool HasNextPage => Page < TotalPages;
}

/// <summary>Shared rules for <c>page</c>/<c>pageSize</c> parameters.</summary>
public static class PagingValidationExtensions
{
    public static void ValidatePaging<T>(this AbstractValidator<T> validator, Func<T, int> page, Func<T, int> pageSize)
    {
        validator.RuleFor(x => page(x)).GreaterThanOrEqualTo(1).OverridePropertyName("page");
        validator.RuleFor(x => pageSize(x)).InclusiveBetween(1, Paging.MaxPageSize).OverridePropertyName("pageSize");
    }
}
