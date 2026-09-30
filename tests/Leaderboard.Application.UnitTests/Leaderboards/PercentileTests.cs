using Leaderboard.Application.Common;
using Leaderboard.Application.Leaderboards;

namespace Leaderboard.Application.UnitTests.Leaderboards;

public sealed class PercentileTests
{
    [Theory]
    [InlineData(1, 1000, 100.0)]
    [InlineData(42, 1000, 95.9)]
    [InlineData(1000, 1000, 0.1)]
    [InlineData(2, 2, 50.0)]
    [InlineData(1, 0, 0.0)]
    public void Percentile_IsTopXPercent(long rank, long total, double expected) =>
        LeaderboardMapping.Percentile(rank, total).ShouldBe(expected);

    [Theory]
    [InlineData(0, 20, 0, false)]
    [InlineData(41, 20, 3, true)]
    [InlineData(40, 20, 2, true)]
    public void PagedResult_ComputesPages(long total, int pageSize, int expectedPages, bool hasNext)
    {
        var page = new PagedResult<int>([], 1, pageSize, total);

        page.TotalPages.ShouldBe(expectedPages);
        page.HasNextPage.ShouldBe(hasNext);
        page.HasPreviousPage.ShouldBeFalse();
    }
}
