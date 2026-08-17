using DeskPin.Services;

namespace DeskPin.Tests;

public sealed class TrayIconServiceTests
{
    [Theory]
    [InlineData(NativeMethods.WmLeftButtonDoubleClick, 1)]
    [InlineData(NativeMethods.WmContextMenu, 2)]
    [InlineData(NativeMethods.WmRightButtonUp, 2)]
    [InlineData(0, 0)]
    public void CallbackMessagesMapToExpectedActions(int message, int expected)
    {
        Assert.Equal((TrayIconAction)expected, TrayIconService.ResolveCallbackMessage(message));
    }

    [Theory]
    [InlineData(1001, 1)]
    [InlineData(1002, 2)]
    [InlineData(1003, 3)]
    [InlineData(1004, 4)]
    [InlineData(0, 0)]
    public void MenuCommandIdsMapToExpectedCommands(int commandId, int expected)
    {
        Assert.Equal((TrayMenuCommand)expected, TrayIconService.ResolveMenuCommand(commandId));
    }

    [Fact]
    public void EmptySafeIconHandleCanBeDisposedMoreThanOnce()
    {
        var handle = new SafeIconHandle(IntPtr.Zero);

        handle.Dispose();
        handle.Dispose();

        Assert.True(handle.IsClosed);
    }
}
