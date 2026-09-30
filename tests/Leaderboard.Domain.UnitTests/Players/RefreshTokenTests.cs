using Leaderboard.Domain.Players;

namespace Leaderboard.Domain.UnitTests.Players;

public sealed class RefreshTokenTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 2, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Issue_WithoutFamily_StartsANewFamily()
    {
        var token = RefreshToken.Issue(Guid.CreateVersion7(), [1], Now, TimeSpan.FromDays(7));

        token.FamilyId.ShouldNotBe(Guid.Empty);
        token.ExpiresAt.ShouldBe(Now.AddDays(7));
        token.IsActive(Now).ShouldBeTrue();
    }

    [Fact]
    public void IsActive_FalseAfterExpiration()
    {
        var token = RefreshToken.Issue(Guid.CreateVersion7(), [1], Now, TimeSpan.FromDays(7));

        token.IsActive(Now.AddDays(7)).ShouldBeFalse();
    }

    [Fact]
    public void Revoke_WithReplacement_MarksTokenAsRotated()
    {
        var family = Guid.CreateVersion7();
        var token = RefreshToken.Issue(Guid.CreateVersion7(), [1], Now, TimeSpan.FromDays(7), family);
        var replacement = RefreshToken.Issue(token.PlayerId, [2], Now, TimeSpan.FromDays(7), family);

        token.Revoke(Now, replacement.Id);

        token.IsActive(Now).ShouldBeFalse();
        token.WasRotated.ShouldBeTrue();
        token.ReplacedById.ShouldBe(replacement.Id);
        replacement.FamilyId.ShouldBe(family);
    }

    [Fact]
    public void Revoke_WithoutReplacement_IsNotARotation()
    {
        var token = RefreshToken.Issue(Guid.CreateVersion7(), [1], Now, TimeSpan.FromDays(7));

        token.Revoke(Now);

        token.WasRotated.ShouldBeFalse();
    }
}
