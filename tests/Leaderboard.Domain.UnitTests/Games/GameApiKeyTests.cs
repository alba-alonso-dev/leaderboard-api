using Leaderboard.Domain.Games;

namespace Leaderboard.Domain.UnitTests.Games;

public sealed class GameApiKeyTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 2, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Revoke_IsIdempotentAndKeepsFirstRevocationTime()
    {
        var key = GameApiKey.Issue(Guid.CreateVersion7(), "lbk_0123456789abcdef", "prod", [1, 2, 3], Now);

        key.Revoke(Now.AddMinutes(1));
        key.Revoke(Now.AddMinutes(5));

        key.IsActive.ShouldBeFalse();
        key.RevokedAt.ShouldBe(Now.AddMinutes(1));
    }

    [Fact]
    public void MarkUsed_UpdatesLastUsedAt()
    {
        var key = GameApiKey.Issue(Guid.CreateVersion7(), "lbk_0123456789abcdef", "prod", [1], Now);

        key.MarkUsed(Now.AddHours(1));

        key.LastUsedAt.ShouldBe(Now.AddHours(1));
        key.IsActive.ShouldBeTrue();
    }
}
