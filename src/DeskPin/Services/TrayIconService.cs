using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows.Interop;

namespace DeskPin.Services;

internal enum TrayNotificationKind
{
    Info,
    Warning,
}

internal enum TrayIconAction
{
    None,
    Open,
    ShowMenu,
}

internal enum TrayMenuCommand
{
    None,
    Open,
    ToggleLastWindow,
    Settings,
    Exit,
}

internal sealed class TrayIconService : IDisposable
{
    private const uint IconId = 1;
    private const int OpenCommandId = 1001;
    private const int ToggleCommandId = 1002;
    private const int SettingsCommandId = 1003;
    private const int ExitCommandId = 1004;

    private readonly IntPtr _windowHandle;
    private readonly IntPtr _iconHandle;
    private readonly HwndSource _source;
    private readonly Action _open;
    private readonly Action _toggleLastWindow;
    private readonly Action _openSettings;
    private readonly Action _exit;
    private readonly uint _taskbarCreatedMessage;
    private bool _iconAdded;
    private bool _disposed;

    internal TrayIconService(
        IntPtr windowHandle,
        IntPtr iconHandle,
        Action open,
        Action toggleLastWindow,
        Action openSettings,
        Action exit)
    {
        if (windowHandle == IntPtr.Zero)
        {
            throw new ArgumentException("托盘图标需要有效的窗口句柄", nameof(windowHandle));
        }

        if (iconHandle == IntPtr.Zero)
        {
            throw new ArgumentException("托盘图标需要有效的图标句柄", nameof(iconHandle));
        }

        _windowHandle = windowHandle;
        _iconHandle = iconHandle;
        _open = open;
        _toggleLastWindow = toggleLastWindow;
        _openSettings = openSettings;
        _exit = exit;
        _source = HwndSource.FromHwnd(windowHandle)
            ?? throw new InvalidOperationException("无法建立托盘图标消息通道");
        _taskbarCreatedMessage = NativeMethods.RegisterWindowMessage("TaskbarCreated");
        _source.AddHook(WindowMessageHook);

        try
        {
            AddIcon();
        }
        catch
        {
            _source.RemoveHook(WindowMessageHook);
            throw;
        }
    }

    internal void ShowMessage(string title, string message, TrayNotificationKind kind)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var data = CreateData(NativeMethods.NifInfo);
        data.InfoTitle = Truncate(title, NativeMethods.NotifyIconInfoTitleLength - 1);
        data.Info = Truncate(message, NativeMethods.NotifyIconInfoLength - 1);
        data.TimeoutOrVersion = 2500;
        data.InfoFlags = kind == TrayNotificationKind.Warning
            ? NativeMethods.NiifWarning
            : NativeMethods.NiifInfo;
        NativeMethods.ShellNotifyIcon(NativeMethods.NimModify, ref data);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        if (_iconAdded)
        {
            var data = CreateData(0);
            NativeMethods.ShellNotifyIcon(NativeMethods.NimDelete, ref data);
            _iconAdded = false;
        }

        _source.RemoveHook(WindowMessageHook);
    }

    internal static TrayIconAction ResolveCallbackMessage(int message) => message switch
    {
        NativeMethods.WmLeftButtonDoubleClick => TrayIconAction.Open,
        NativeMethods.WmContextMenu or NativeMethods.WmRightButtonUp => TrayIconAction.ShowMenu,
        _ => TrayIconAction.None,
    };

    internal static TrayMenuCommand ResolveMenuCommand(int commandId) => commandId switch
    {
        OpenCommandId => TrayMenuCommand.Open,
        ToggleCommandId => TrayMenuCommand.ToggleLastWindow,
        SettingsCommandId => TrayMenuCommand.Settings,
        ExitCommandId => TrayMenuCommand.Exit,
        _ => TrayMenuCommand.None,
    };

    private void AddIcon()
    {
        var data = CreateData(
            NativeMethods.NifMessage |
            NativeMethods.NifIcon |
            NativeMethods.NifTip |
            NativeMethods.NifShowTip);
        data.CallbackMessage = NativeMethods.WmDeskPinTrayIcon;
        data.IconHandle = _iconHandle;
        data.ToolTip = "DeskPin - 窗口置顶助手";

        if (!NativeMethods.ShellNotifyIcon(NativeMethods.NimAdd, ref data))
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), "无法创建 DeskPin 托盘图标");
        }

        _iconAdded = true;
        data.TimeoutOrVersion = NativeMethods.NotifyIconVersion4;
        NativeMethods.ShellNotifyIcon(NativeMethods.NimSetVersion, ref data);
    }

    private IntPtr WindowMessageHook(
        IntPtr hWnd,
        int message,
        IntPtr wParam,
        IntPtr lParam,
        ref bool handled)
    {
        if ((uint)message == _taskbarCreatedMessage)
        {
            _iconAdded = false;
            AddIcon();
            return IntPtr.Zero;
        }

        if (message != NativeMethods.WmDeskPinTrayIcon)
        {
            return IntPtr.Zero;
        }

        var callbackMessage = unchecked((int)(lParam.ToInt64() & 0xFFFF));
        switch (ResolveCallbackMessage(callbackMessage))
        {
            case TrayIconAction.Open:
                handled = true;
                _open();
                break;
            case TrayIconAction.ShowMenu:
                handled = true;
                ShowContextMenu();
                break;
        }

        return IntPtr.Zero;
    }

    private void ShowContextMenu()
    {
        var menu = NativeMethods.CreatePopupMenu();
        if (menu == IntPtr.Zero)
        {
            return;
        }

        try
        {
            NativeMethods.AppendMenu(menu, NativeMethods.MfString, OpenCommandId, "打开 DeskPin");
            NativeMethods.AppendMenu(menu, NativeMethods.MfString, ToggleCommandId, "切换最近窗口置顶");
            NativeMethods.AppendMenu(menu, NativeMethods.MfSeparator, 0, null);
            NativeMethods.AppendMenu(menu, NativeMethods.MfString, SettingsCommandId, "设置");
            NativeMethods.AppendMenu(menu, NativeMethods.MfString, ExitCommandId, "退出");

            if (!NativeMethods.GetCursorPos(out var cursor))
            {
                return;
            }

            NativeMethods.SetForegroundWindow(_windowHandle);
            var commandId = NativeMethods.TrackPopupMenu(
                menu,
                NativeMethods.TpmRightButton | NativeMethods.TpmReturnCommand,
                cursor.X,
                cursor.Y,
                0,
                _windowHandle,
                IntPtr.Zero);
            ExecuteCommand(ResolveMenuCommand(commandId));
            NativeMethods.PostMessage(
                _windowHandle,
                NativeMethods.WmNull,
                IntPtr.Zero,
                IntPtr.Zero);
        }
        finally
        {
            NativeMethods.DestroyMenu(menu);
        }
    }

    private void ExecuteCommand(TrayMenuCommand command)
    {
        switch (command)
        {
            case TrayMenuCommand.Open:
                _open();
                break;
            case TrayMenuCommand.ToggleLastWindow:
                _toggleLastWindow();
                break;
            case TrayMenuCommand.Settings:
                _openSettings();
                break;
            case TrayMenuCommand.Exit:
                _exit();
                break;
        }
    }

    private NativeMethods.NotifyIconData CreateData(uint flags) => new()
    {
        Size = (uint)Marshal.SizeOf<NativeMethods.NotifyIconData>(),
        WindowHandle = _windowHandle,
        Id = IconId,
        Flags = flags,
        ToolTip = string.Empty,
        Info = string.Empty,
        InfoTitle = string.Empty,
    };

    private static string Truncate(string value, int maximumLength) =>
        value.Length <= maximumLength ? value : value[..maximumLength];
}
