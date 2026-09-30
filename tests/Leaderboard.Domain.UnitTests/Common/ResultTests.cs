using Leaderboard.Domain.Common;

namespace Leaderboard.Domain.UnitTests.Common;

public sealed class ResultTests
{
    private static readonly Error SampleError = Error.NotFound("thing.not_found", "Not found.");

    [Fact]
    public void Success_ExposesValue()
    {
        Result<int> result = 5;

        result.IsSuccess.ShouldBeTrue();
        result.Value.ShouldBe(5);
        result.Error.ShouldBeNull();
    }

    [Fact]
    public void Failure_ExposesErrorAndHidesValue()
    {
        Result<int> result = SampleError;

        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldBe(SampleError);
        Should.Throw<InvalidOperationException>(() => result.Value);
    }
}
