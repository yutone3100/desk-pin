using DeskPin.Services;

namespace DeskPin.Tests;

public sealed class WindowEligibilityTests
{
    [Fact]
    public void IncludesVisibleTitledApplicationWindow()
    {
        var included = WindowEligibility.ShouldInclude(
            visible: true,
            cloaked: false,
            title: "记事本",
            className: "Notepad",
            extendedStyle: 0,
            processId: 42,
            ownProcessId: 7);

        Assert.True(included);
    }

    [Theory]
    [InlineData(false, false, "窗口", "App", 0L, 42, 7)]
    [InlineData(true, true, "窗口", "App", 0L, 42, 7)]
    [InlineData(true, false, "", "App", 0L, 42, 7)]
    [InlineData(true, false, "窗口", "Shell_TrayWnd", 0L, 42, 7)]
    [InlineData(true, false, "窗口", "App", 0L, 7, 7)]
    public void ExcludesNonUserWindows(
        bool visible,
        bool cloaked,
        string title,
        string className,
        long style,
        int processId,
        int ownProcessId)
    {
        Assert.False(WindowEligibility.ShouldInclude(
            visible,
            cloaked,
            title,
            className,
            style,
            processId,
            ownProcessId));
    }

    [Fact]
    public void ExcludesToolWindowUnlessMarkedAsApplicationWindow()
    {
        Assert.False(WindowEligibility.ShouldInclude(true, false, "工具", "Tool", 0x80, 42, 7));
        Assert.True(WindowEligibility.ShouldInclude(true, false, "工具", "Tool", 0x80 | 0x40000, 42, 7));
    }
}
