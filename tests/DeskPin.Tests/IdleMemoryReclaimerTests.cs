using DeskPin.Services;

namespace DeskPin.Tests;

public sealed class IdleMemoryReclaimerTests
{
    [Fact]
    public async Task RunsOnceForLatestSchedule()
    {
        var calls = 0;
        using var reclaimer = new IdleMemoryReclaimer(
            TimeSpan.FromMilliseconds(30),
            () => Interlocked.Increment(ref calls));

        reclaimer.Schedule();
        reclaimer.Schedule();
        await Task.Delay(150);

        Assert.Equal(1, Volatile.Read(ref calls));
    }

    [Fact]
    public async Task CancelPreventsReclamationAndDisposeIsRepeatable()
    {
        var calls = 0;
        var reclaimer = new IdleMemoryReclaimer(
            TimeSpan.FromMilliseconds(50),
            () => Interlocked.Increment(ref calls));

        reclaimer.Schedule();
        reclaimer.Cancel();
        await Task.Delay(120);
        reclaimer.Dispose();
        reclaimer.Dispose();

        Assert.Equal(0, Volatile.Read(ref calls));
    }
}
