using Microsoft.Win32;
using System.IO;

namespace DeskPin.Services;

public sealed class RegistryStartupManager : IStartupManager
{
    internal const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    internal const string ValueName = "DeskPin";
    private readonly string _executablePath;

    public RegistryStartupManager(string? executablePath = null)
    {
        _executablePath = executablePath ?? Environment.ProcessPath
            ?? throw new InvalidOperationException("无法确定 DeskPin 可执行文件路径");
    }

    public bool IsEnabled()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: false);
        return key?.GetValue(ValueName) is string value &&
            value.Contains(_executablePath, StringComparison.OrdinalIgnoreCase);
    }

    public StartupOperationResult SetEnabled(bool enabled)
    {
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(RunKeyPath, writable: true);
            if (enabled)
            {
                key.SetValue(ValueName, $"\"{_executablePath}\" --background", RegistryValueKind.String);
            }
            else
            {
                key.DeleteValue(ValueName, throwOnMissingValue: false);
            }

            return StartupOperationResult.Success();
        }
        catch (Exception exception) when (exception is UnauthorizedAccessException or IOException)
        {
            return StartupOperationResult.Failure($"无法更新开机启动设置：{exception.Message}");
        }
    }
}
