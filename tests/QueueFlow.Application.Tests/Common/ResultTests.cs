using QueueFlow.Application.Common;

namespace QueueFlow.Application.Tests.Common;

public sealed class ResultTests
{
    [Fact]
    public void FailureExposesItsError()
    {
        var error = new Error("test.failure", "Expected failure.");

        var result = Result.Failure(error);

        Assert.True(result.IsFailure);
        Assert.Equal(error, result.Error);
    }

    [Fact]
    public void ValueWhenResultFailedThrowsInvalidOperationException()
    {
        var result = Result.Failure<string>(new Error("test.failure", "Expected failure."));

        Assert.Throws<InvalidOperationException>(() => result.Value);
    }
}
