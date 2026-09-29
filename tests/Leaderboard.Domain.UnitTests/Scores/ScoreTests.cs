using Leaderboard.Domain.Common;
using Leaderboard.Domain.Scores;

namespace Leaderboard.Domain.UnitTests.Scores;

public sealed class ScoreTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 2, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Submit_CreatesAcceptedScore()
    {
        var score = Score.Submit(Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(), 42, "nonce-0123456789", """{"level":1}""", Now);

        score.Status.ShouldBe(ScoreStatus.Accepted);
        score.Value.ShouldBe(42);
        score.SubmittedAt.ShouldBe(Now);
    }

    [Fact]
    public void Submit_WhenMetadataTooLarge_Throws() =>
        Should.Throw<DomainException>(() => Score.Submit(
            Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(), 1, "nonce-0123456789",
            new string('x', Score.MetadataMaxLength + 1), Now));

    [Fact]
    public void Reject_ChangesStatus()
    {
        var score = Score.Submit(Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(), 1, "nonce-0123456789", null, Now);

        score.Reject();

        score.Status.ShouldBe(ScoreStatus.Rejected);
    }
}
