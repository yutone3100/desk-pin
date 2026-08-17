using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;

namespace DeskPin.Services;

internal sealed record ElevatedRestartResult(bool Started, bool Cancelled, string Message)
{
    internal static ElevatedRestartResult Success() => new(true, false, string.Empty);
    internal static ElevatedRestartResult CancelledByUser() => new(false, true, "已取消管理员权限请求");
    internal static ElevatedRestartResult Failure(string message) => new(false, false, message);
}

internal static class ElevatedRestartService
{
    internal const string ParentArgument = "--elevated-restart-parent";
    private const int ErrorCancelled = 1223;

    internal static ElevatedRestartResult Start() => Start(
        static startInfo =>
        {
            using var process = Process.Start(startInfo);
            return process is not null;
        },
        Environment.ProcessPath,
        Environment.ProcessId);

    internal static ElevatedRestartResult Start(
        Func<ProcessStartInfo, bool> processStarter,
        string? executablePath,
        int parentProcessId)
    {
        ArgumentNullException.ThrowIfNull(processStarter);
        if (string.IsNullOrWhiteSpace(executablePath))
        {
            return ElevatedRestartResult.Failure("无法确定 DeskPin 程序路径");
        }

        var startInfo = new ProcessStartInfo
        {
            FileName = executablePath,
            UseShellExecute = true,
            Verb = "runas",
        };
        startInfo.ArgumentList.Add(ParentArgument);
        startInfo.ArgumentList.Add(parentProcessId.ToString(CultureInfo.InvariantCulture));

        try
        {
            return processStarter(startInfo)
                ? ElevatedRestartResult.Success()
                : ElevatedRestartResult.Failure("无法启动管理员 DeskPin 实例");
        }
        catch (Win32Exception exception) when (exception.NativeErrorCode == ErrorCancelled)
        {
            return ElevatedRestartResult.CancelledByUser();
        }
        catch (Exception exception) when (exception is Win32Exception or InvalidOperationException)
        {
            return ElevatedRestartResult.Failure($"管理员重启失败：{exception.Message}");
        }
    }

    internal static bool TryGetParentProcessId(IReadOnlyList<string> arguments, out int processId)
    {
        processId = 0;
        for (var index = 0; index + 1 < arguments.Count; index++)
        {
            if (string.Equals(arguments[index], ParentArgument, StringComparison.OrdinalIgnoreCase) &&
                int.TryParse(arguments[index + 1], NumberStyles.None, CultureInfo.InvariantCulture, out processId) &&
                processId > 0)
            {
                return true;
            }
        }

        processId = 0;
        return false;
    }

    internal static bool WaitForParentExit(int parentProcessId, TimeSpan timeout) =>
        WaitForParentExit(
            parentProcessId,
            timeout,
            static (processId, waitTimeout) =>
            {
                try
                {
                    using var process = Process.GetProcessById(processId);
                    return process.WaitForExit((int)waitTimeout.TotalMilliseconds);
                }
                catch (ArgumentException)
                {
                    return true;
                }
            });

    internal static bool WaitForParentExit(
        int parentProcessId,
        TimeSpan timeout,
        Func<int, TimeSpan, bool> waiter)
    {
        ArgumentNullException.ThrowIfNull(waiter);
        return parentProcessId > 0 && timeout > TimeSpan.Zero && waiter(parentProcessId, timeout);
    }
}
