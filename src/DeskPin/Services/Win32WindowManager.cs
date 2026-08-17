using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using DeskPin.Models;

namespace DeskPin.Services;

public sealed class Win32WindowManager : IWindowManager
{
    private const int TopmostVerificationAttempts = 3;
    private const int TopmostVerificationDelayMilliseconds = 20;
    private readonly int _ownProcessId;
    private readonly object _trackedLock = new();
    private readonly HashSet<long> _windowsPinnedByDeskPin = [];
    private readonly NativeMethods.WinEventDelegate _foregroundCallback;
    private IntPtr _foregroundHook;
    private long _lastEligibleWindowId;
    private bool _disposed;

    public Win32WindowManager() : this(Environment.ProcessId)
    {
    }

    internal Win32WindowManager(int ownProcessId)
    {
        _ownProcessId = ownProcessId;
        _foregroundCallback = OnForegroundChanged;
        _foregroundHook = NativeMethods.SetWinEventHook(
            NativeMethods.EventSystemForeground,
            NativeMethods.EventSystemForeground,
            IntPtr.Zero,
            _foregroundCallback,
            0,
            0,
            NativeMethods.WineventOutOfContext | NativeMethods.WineventSkipOwnProcess);

        RememberIfEligible(NativeMethods.GetForegroundWindow());
    }

    public long? LastEligibleWindowId
    {
        get
        {
            var value = Interlocked.Read(ref _lastEligibleWindowId);
            return value == 0 ? null : value;
        }
    }

    public IReadOnlyList<DesktopWindow> GetWindows()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var windows = new List<DesktopWindow>();
        NativeMethods.EnumWindows((hWnd, _) =>
        {
            if (TryCreateWindow(hWnd, includeIcon: true, out var window))
            {
                windows.Add(window!);
            }

            return true;
        }, IntPtr.Zero);

        return windows
            .OrderByDescending(window => window.IsTopmost)
            .ThenBy(window => window.ProcessName, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(window => window.Title, StringComparer.CurrentCultureIgnoreCase)
            .ToArray();
    }

    public WindowOperationResult ToggleTopmost(long windowId)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var hWnd = new IntPtr(windowId);
        if (!NativeMethods.IsWindow(hWnd) || !TryCreateWindow(hWnd, includeIcon: false, out var window))
        {
            return WindowOperationResult.Failure(WindowOperationError.InvalidWindow, "窗口已经关闭或不再可用");
        }

        var desiredTopmost = !window!.IsTopmost;
        if (!TrySetTopmostState(hWnd, desiredTopmost, out var error))
        {
            return error == 5
                ? WindowOperationResult.Failure(
                    WindowOperationError.AccessDenied,
                    "权限不足：请以管理员身份重新启动 DeskPin 后再试")
                : error != 0
                    ? WindowOperationResult.Failure(
                        WindowOperationError.NativeFailure,
                        new Win32Exception(error).Message)
                : WindowOperationResult.Failure(
                    WindowOperationError.NativeFailure,
                    "目标程序阻止了置顶状态更改");
        }

        lock (_trackedLock)
        {
            if (desiredTopmost)
            {
                _windowsPinnedByDeskPin.Add(windowId);
            }
            else
            {
                _windowsPinnedByDeskPin.Remove(windowId);
            }
        }

        return WindowOperationResult.Success(desiredTopmost);
    }

    public WindowOperationResult ToggleLastEligibleWindow()
    {
        var windowId = LastEligibleWindowId;
        return windowId is null
            ? WindowOperationResult.Failure(WindowOperationError.InvalidWindow, "没有可操作的前台窗口")
            : ToggleTopmost(windowId.Value);
    }

    public WindowActionResult ShowWindow(long windowId)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var hWnd = new IntPtr(windowId);
        if (!NativeMethods.IsWindow(hWnd))
        {
            return WindowActionResult.Failure(
                WindowOperationError.InvalidWindow,
                "窗口已经关闭或不再可用");
        }

        NativeMethods.ShowWindowAsync(
            hWnd,
            NativeMethods.IsIconic(hWnd) ? NativeMethods.SwRestore : NativeMethods.SwShow);
        NativeMethods.SetForegroundWindow(hWnd);
        return WindowActionResult.Success("窗口已显示");
    }

    public WindowActionResult CloseWindow(long windowId)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var hWnd = new IntPtr(windowId);
        if (!NativeMethods.IsWindow(hWnd))
        {
            return WindowActionResult.Failure(
                WindowOperationError.InvalidWindow,
                "窗口已经关闭或不再可用");
        }

        Marshal.SetLastPInvokeError(0);
        if (NativeMethods.PostMessage(hWnd, NativeMethods.WmClose, IntPtr.Zero, IntPtr.Zero))
        {
            return WindowActionResult.Success("已发送关闭请求");
        }

        var error = Marshal.GetLastPInvokeError();
        return error == 5
            ? WindowActionResult.Failure(
                WindowOperationError.AccessDenied,
                "权限不足：无法关闭以更高权限运行的窗口")
            : WindowActionResult.Failure(
                WindowOperationError.NativeFailure,
                error == 0 ? "无法向目标窗口发送关闭请求" : new Win32Exception(error).Message);
    }

    public int RestoreWindowsPinnedByDeskPin()
    {
        long[] tracked;
        lock (_trackedLock)
        {
            tracked = [.. _windowsPinnedByDeskPin];
            _windowsPinnedByDeskPin.Clear();
        }

        var restored = 0;
        foreach (var windowId in tracked)
        {
            var hWnd = new IntPtr(windowId);
            if (!NativeMethods.IsWindow(hWnd) || !IsTopmost(hWnd))
            {
                continue;
            }

            if (TrySetTopmostState(hWnd, desiredTopmost: false, out _))
            {
                restored++;
            }
        }

        return restored;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        if (_foregroundHook != IntPtr.Zero)
        {
            NativeMethods.UnhookWinEvent(_foregroundHook);
            _foregroundHook = IntPtr.Zero;
        }
    }

    private void OnForegroundChanged(
        IntPtr hWinEventHook,
        uint eventType,
        IntPtr hWnd,
        int idObject,
        int idChild,
        uint idEventThread,
        uint eventTime) => RememberIfEligible(hWnd);

    private void RememberIfEligible(IntPtr hWnd)
    {
        if (hWnd != IntPtr.Zero && TryCreateWindow(hWnd, includeIcon: false, out _))
        {
            Interlocked.Exchange(ref _lastEligibleWindowId, hWnd.ToInt64());
        }
    }

    private bool TryCreateWindow(IntPtr hWnd, bool includeIcon, out DesktopWindow? window)
    {
        window = null;
        if (!NativeMethods.IsWindowVisible(hWnd))
        {
            return false;
        }

        var titleLength = NativeMethods.GetWindowTextLength(hWnd);
        if (titleLength <= 0)
        {
            return false;
        }

        var titleBuffer = new char[titleLength + 1];
        var copiedTitleLength = NativeMethods.GetWindowText(hWnd, titleBuffer, titleBuffer.Length);
        var title = copiedTitleLength > 0 ? new string(titleBuffer, 0, copiedTitleLength) : string.Empty;

        var classBuffer = new char[256];
        var classLength = NativeMethods.GetClassName(hWnd, classBuffer, classBuffer.Length);
        var className = classLength > 0 ? new string(classBuffer, 0, classLength) : string.Empty;
        var style = NativeMethods.GetWindowLongPtr(hWnd, NativeMethods.GwlExStyle).ToInt64();
        NativeMethods.GetWindowThreadProcessId(hWnd, out var processIdValue);
        var processId = unchecked((int)processIdValue);
        var cloaked = NativeMethods.DwmGetWindowAttribute(
            hWnd,
            NativeMethods.DwmwaCloaked,
            out var cloakedValue,
            sizeof(int)) == 0 && cloakedValue != 0;

        if (!WindowEligibility.ShouldInclude(
            visible: true,
            cloaked,
            title,
            className,
            style,
            processId,
            _ownProcessId))
        {
            return false;
        }

        var processName = "未知应用";
        try
        {
            using var process = Process.GetProcessById(processId);
            processName = process.ProcessName;
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException)
        {
            // The process can exit between enumeration and lookup.
        }

        window = new DesktopWindow(
            hWnd.ToInt64(),
            title.Trim(),
            processName,
            processId,
            (style & NativeMethods.WsExTopmost) != 0,
            includeIcon ? WindowIconService.TryGetIcon(hWnd, processId) : null);
        return true;
    }

    private static bool IsTopmost(IntPtr hWnd) =>
        (NativeMethods.GetWindowLongPtr(hWnd, NativeMethods.GwlExStyle).ToInt64() & NativeMethods.WsExTopmost) != 0;

    private static bool TrySetTopmostState(IntPtr hWnd, bool desiredTopmost, out int nativeError)
    {
        var insertAfter = desiredTopmost ? NativeMethods.HwndTopmost : NativeMethods.HwndNoTopmost;
        var baseFlags = NativeMethods.SwpNoMove | NativeMethods.SwpNoSize | NativeMethods.SwpNoActivate;
        if (!TrySetWindowPosition(hWnd, insertAfter, baseFlags, out nativeError))
        {
            return false;
        }

        if (WaitForTopmostState(hWnd, desiredTopmost))
        {
            return true;
        }

        if (!TrySetWindowPosition(
            hWnd,
            insertAfter,
            baseFlags | NativeMethods.SwpNoSendChanging,
            out nativeError))
        {
            return false;
        }

        var verified = WaitForTopmostState(hWnd, desiredTopmost);
        if (!verified)
        {
            nativeError = 0;
        }

        return verified;
    }

    private static bool TrySetWindowPosition(
        IntPtr hWnd,
        IntPtr insertAfter,
        uint flags,
        out int nativeError)
    {
        Marshal.SetLastPInvokeError(0);
        var changed = NativeMethods.SetWindowPos(hWnd, insertAfter, 0, 0, 0, 0, flags);
        nativeError = changed ? 0 : Marshal.GetLastPInvokeError();
        return changed;
    }

    private static bool WaitForTopmostState(IntPtr hWnd, bool desiredTopmost)
    {
        for (var attempt = 0; attempt < TopmostVerificationAttempts; attempt++)
        {
            if (!NativeMethods.IsWindow(hWnd))
            {
                return false;
            }

            if (IsTopmost(hWnd) == desiredTopmost)
            {
                return true;
            }

            if (attempt + 1 < TopmostVerificationAttempts)
            {
                Thread.Sleep(TopmostVerificationDelayMilliseconds);
            }
        }

        return false;
    }
}
