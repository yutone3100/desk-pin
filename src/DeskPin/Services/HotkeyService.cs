using System.ComponentModel;
using System.Windows.Interop;
using DeskPin.Models;

namespace DeskPin.Services;

public sealed class HotkeyService : IHotkeyService
{
    private const int HotkeyId = 0xD351;
    private readonly IntPtr _windowHandle;
    private readonly HwndSource _source;
    private readonly Action _onPressed;
    private bool _registered;
    private bool _disposed;

    public HotkeyService(IntPtr windowHandle, Action onPressed)
    {
        _windowHandle = windowHandle;
        _onPressed = onPressed;
        _source = HwndSource.FromHwnd(windowHandle)
            ?? throw new InvalidOperationException("无法建立快捷键消息通道");
        _source.AddHook(WindowMessageHook);
    }

    public HotkeySetting? Current { get; private set; }

    public bool TryChange(HotkeySetting? setting, out string errorMessage)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var previous = Current;
        if (_registered)
        {
            NativeMethods.UnregisterHotKey(_windowHandle, HotkeyId);
            _registered = false;
        }

        if (setting is null)
        {
            Current = null;
            errorMessage = string.Empty;
            return true;
        }

        if (!setting.IsValid)
        {
            RestorePrevious(previous);
            errorMessage = "快捷键必须包含至少一个修饰键和一个普通按键";
            return false;
        }

        if (!NativeMethods.RegisterHotKey(
            _windowHandle,
            HotkeyId,
            setting.Modifiers | NativeMethods.ModNoRepeat,
            setting.VirtualKey))
        {
            var error = new Win32Exception().Message;
            RestorePrevious(previous);
            errorMessage = $"快捷键已被其他程序占用或不可用：{error}";
            return false;
        }

        _registered = true;
        Current = Clone(setting);
        errorMessage = string.Empty;
        return true;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        if (_registered)
        {
            NativeMethods.UnregisterHotKey(_windowHandle, HotkeyId);
        }

        _source.RemoveHook(WindowMessageHook);
        _disposed = true;
    }

    private IntPtr WindowMessageHook(IntPtr hWnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (message == NativeMethods.WmHotkey && wParam.ToInt32() == HotkeyId)
        {
            handled = true;
            _onPressed();
        }

        return IntPtr.Zero;
    }

    private void RestorePrevious(HotkeySetting? previous)
    {
        Current = null;
        if (previous is null)
        {
            return;
        }

        if (NativeMethods.RegisterHotKey(
            _windowHandle,
            HotkeyId,
            previous.Modifiers | NativeMethods.ModNoRepeat,
            previous.VirtualKey))
        {
            _registered = true;
            Current = Clone(previous);
        }
    }

    private static HotkeySetting Clone(HotkeySetting setting) => new()
    {
        Modifiers = setting.Modifiers,
        VirtualKey = setting.VirtualKey,
        DisplayText = setting.DisplayText,
    };
}
