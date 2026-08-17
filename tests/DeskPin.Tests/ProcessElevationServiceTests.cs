using DeskPin.Services;

namespace DeskPin.Tests;

public sealed class ProcessElevationServiceTests
{
    [Fact]
    public void CurrentProcessElevationCanBeQueried()
    {
        Assert.True(ProcessElevationService.TryIsElevated(Environment.ProcessId, out _));
    }

    [Fact]
    public void MissingProcessElevationCannotBeQueried()
    {
        Assert.False(ProcessElevationService.TryIsElevated(int.MaxValue, out var isElevated));
        Assert.False(isElevated);
    }
}
