using System.ComponentModel;
using System.Diagnostics;
using DeskPin.Services;

namespace DeskPin.Tests;

public sealed class ElevatedRestartServiceTests
{
    [Fact]
    public void StartUsesRunAsAndParentHandoffArguments()
    {
        ProcessStartInfo? captured = null;

        var result = ElevatedRestartService.Start(
            startInfo =>
            {
                captured = startInfo;
                return true;
            },
            @"C:\Apps\DeskPin.exe",
            4242);

        Assert.True(result.Started);
        Assert.NotNull(captured);
        Assert.Equal(@"C:\Apps\DeskPin.exe", captured.FileName);
        Assert.True(captured.UseShellExecute);
        Assert.Equal("runas", captured.Verb);
        Assert.Equal([ElevatedRestartService.ParentArgument, "4242"], captured.ArgumentList);
    }

    [Fact]
    public void StartReportsUacCancellationWithoutStartingHandoff()
    {
        var result = ElevatedRestartService.Start(
            _ => throw new Win32Exception(1223),
            @"C:\Apps\DeskPin.exe",
            4242);

        Assert.False(result.Started);
        Assert.True(result.Cancelled);
    }

    [Theory]
    [InlineData("--elevated-restart-parent", "123", true, 123)]
    [InlineData("--ELEVATED-RESTART-PARENT", "456", true, 456)]
    [InlineData("--elevated-restart-parent", "invalid", false, 0)]
    public void ParsesInternalParentHandoffArgument(
        string argument,
        string value,
        bool expected,
        int expectedProcessId)
    {
        var parsed = ElevatedRestartService.TryGetParentProcessId(
            ["--background", argument, value],
            out var processId);

        Assert.Equal(expected, parsed);
        Assert.Equal(expectedProcessId, processId);
    }

    [Fact]
    public void ParentWaitHonorsSuccessAndTimeout()
    {
        Assert.True(ElevatedRestartService.WaitForParentExit(
            123,
            TimeSpan.FromSeconds(10),
            (processId, timeout) => processId == 123 && timeout == TimeSpan.FromSeconds(10)));
        Assert.False(ElevatedRestartService.WaitForParentExit(
            123,
            TimeSpan.FromSeconds(10),
            (_, _) => false));
    }
}
