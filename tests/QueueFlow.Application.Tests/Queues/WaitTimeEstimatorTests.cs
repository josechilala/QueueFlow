using QueueFlow.Application.Features.Queues;

namespace QueueFlow.Application.Tests.Queues;

public sealed class WaitTimeEstimatorTests
{
    [Theory]
    [InlineData(4, 12, 2, 24)]
    [InlineData(5, 10, 2, 25)]
    [InlineData(1, 10, 3, 4)]
    [InlineData(4, 12, 0, 48)]
    [InlineData(0, 12, 2, 0)]
    public void CalculatesInitialBlueprintEstimate(int ahead, int average, int attendants, int expected)
    {
        Assert.Equal(expected, WaitTimeEstimator.Calculate(ahead, average, attendants));
    }
}
