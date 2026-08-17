using DeskPin.Services;

namespace DeskPin.Tests;

public sealed class WindowMaximizeHelperTests
{
    [Theory]
    [InlineData(0, 0, 1920, 1080, 0, 0, 1920, 1040, 0, 0, 1920, 1040)]
    [InlineData(0, 0, 1920, 1080, 0, 40, 1920, 1080, 0, 40, 1920, 1040)]
    [InlineData(0, 0, 1920, 1080, 48, 0, 1920, 1080, 48, 0, 1872, 1080)]
    [InlineData(-1920, 0, 0, 1080, -1920, 0, 0, 1040, 0, 0, 1920, 1040)]
    [InlineData(0, 0, 2560, 1440, 0, 0, 2560, 1440, 0, 0, 2560, 1440)]
    public void AppliesCurrentMonitorWorkArea(
        int monitorLeft,
        int monitorTop,
        int monitorRight,
        int monitorBottom,
        int workLeft,
        int workTop,
        int workRight,
        int workBottom,
        int expectedX,
        int expectedY,
        int expectedWidth,
        int expectedHeight)
    {
        var minMaxInfo = new NativeMethods.MinMaxInfo();

        WindowMaximizeHelper.ApplyWorkArea(
            ref minMaxInfo,
            new NativeMethods.Rect(monitorLeft, monitorTop, monitorRight, monitorBottom),
            new NativeMethods.Rect(workLeft, workTop, workRight, workBottom));

        Assert.Equal(expectedX, minMaxInfo.MaxPosition.X);
        Assert.Equal(expectedY, minMaxInfo.MaxPosition.Y);
        Assert.Equal(expectedWidth, minMaxInfo.MaxSize.X);
        Assert.Equal(expectedHeight, minMaxInfo.MaxSize.Y);
    }
}
