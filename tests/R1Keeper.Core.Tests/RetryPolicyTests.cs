using R1Keeper.Core;

namespace R1Keeper.Core.Tests;

public sealed class RetryPolicyTests
{
    [Theory]
    [InlineData(1, 0)]
    [InlineData(2, 0)]
    [InlineData(3, 0)]   // the first three retries are free: one failure is expected after a quick off/on
    [InlineData(4, 5)]
    [InlineData(5, 10)]
    [InlineData(6, 20)]
    [InlineData(7, 30)]
    [InlineData(50, 30)] // capped at 30 s
    public void DelayAfterFailure(int failures, int seconds) =>
        Assert.Equal(TimeSpan.FromSeconds(seconds), new RetryPolicy().DelayAfterFailure(failures));

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void DelayAfterFailure_NoFailures_IsZero(int failures) =>
        Assert.Equal(TimeSpan.Zero, new RetryPolicy().DelayAfterFailure(failures));
}
