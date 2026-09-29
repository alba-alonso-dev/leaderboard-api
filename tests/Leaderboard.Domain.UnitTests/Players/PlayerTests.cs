using Leaderboard.Domain.Players;

namespace Leaderboard.Domain.UnitTests.Players;

public sealed class PlayerTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 2, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Register_NormalizesEmailAndAssignsPlayerRole()
    {
        var player = Player.Register(" ana_dev ", " Ana@Example.COM ", Now);

        player.Username.ShouldBe("ana_dev");
        player.Email.ShouldBe("ana@example.com");
        player.Role.ShouldBe(PlayerRoles.Player);
        player.IsAdmin.ShouldBeFalse();
        player.CreatedAt.ShouldBe(Now);
        player.Id.Version.ShouldBe(7);
    }

    [Fact]
    public void PromoteToAdmin_GrantsAdminRole()
    {
        var player = Player.Register("root", "root@example.com", Now);

        player.PromoteToAdmin();

        player.IsAdmin.ShouldBeTrue();
    }

    [Fact]
    public void SetPasswordHash_RejectsEmptyHash() =>
        Should.Throw<ArgumentException>(() => Player.Register("ana", "ana@example.com", Now).SetPasswordHash(" "));
}
