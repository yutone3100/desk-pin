using System.Buffers;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using DeskPin.Models;

namespace DeskPin.Services;

public sealed class Win32WindowManager : IWindowManager
{
    private static readonly int[] TopmostAttemptOffsetsMilliseconds = [0, 30, 80, 180, 380];
    private const int TopmostVerificationAttempts = 3;
    private const int TopmostVerificationDelayMilliseconds = 20;
    private const int MaintenanceCooldownMilliseconds = 250;
    private const int MaximumMaintenanceFailures = 3;
    private const int ReplacementWindowMilliseconds = 2000;
    private readonly int _ownProcessId;
    private readonly object _trackedLock = new();
    private readonly Dictionary<long, TrackedTopmostWindow> _windowsPinnedByDeskPin = [];
    private readonly List<PendingReplacementWindow> _pendingReplacementWindows = [];
    private readonly NativeMethods.WinEventDelegate _foregroundCallback;
    private readonly WindowIconCache _iconCache;
    private readonly List<IntPtr> _windowEventHooks = [];
    private long _lastEligibleWindowId;
    private bool _disposed;

    internal event EventHandler<TopmostMaintenanceFailedEventArgs>? MaintenanceFailed;

    public Win32WindowManager() : this(Environment.ProcessId)
    {
    }

    internal Win32WindowManager(int ownProcessId)
        : this(ownProcessId, new WindowIconCache(WindowIconService.TryGetIcon))
    {
    }

    internal Win32WindowManager(int ownProcessId, WindowIconCache iconCache)
    {
        _ownProcessId = ownProcessId;
        _iconCache = iconCache;
        _foregroundCallback = OnForegroundChanged;
        RegisterWindowEventHook(NativeMethods.EventSystemForeground);
        RegisterWindowEventHook(NativeMethods.EventSystemMinimizeEnd);
        RegisterWindowEventHook(NativeMethods.EventObjectDestroy);
        RegisterWindowEventHook(NativeMethods.EventObjectShow);
        RegisterWindowEventHook(NativeMethods.EventObjectFocus);

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
        var processNames = new Dictionary<int, string>();
        var activeIconKeys = new HashSet<WindowIconCacheKey>();
        NativeMethods.EnumWindows((hWnd, _) =>
        {
            if (TryCreateWindow(hWnd, includeIcon: true, processNames, activeIconKeys, out var window))
            {
                windows.Add(window!);
            }

            return true;
        }, IntPtr.Zero);

        _iconCache.RetainOnly(activeIconKeys);
        ReconcileTrackedWindows();
        windows.Sort(CompareWindows);
        return windows;
    }

    public WindowOperationResult ToggleTopmost(long windowId)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var hWnd = new IntPtr(windowId);
        if (!NativeMethods.IsWindow(hWnd) || !TryCreateWindow(hWnd, includeIcon: false, null, null, out var window))
        {
            return WindowOperationResult.Failure(WindowOperationError.InvalidWindow, "窗口已经关闭或不再可用");
        }

        var desiredTopmost = !window!.IsTopmost;
        if (ProcessElevationService.RequiresElevation(window.ProcessId))
        {
            return WindowOperationResult.Failure(
                WindowOperationError.AccessDenied,
                "目标程序正以管理员身份运行，需要以管理员身份重新启动 DeskPin");
        }

        if (!desiredTopmost)
        {
            StopTrackingWindow(windowId, window.ProcessId);
        }

        var change = TrySetTopmostState(hWnd, desiredTopmost);
        if (!change.Succeeded)
        {
            return CreateTopmostFailure(change);
        }

        if (desiredTopmost)
        {
            TrackWindow(window, GetWindowClassName(hWnd));
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
        TrackedTopmostWindow[] tracked;
        lock (_trackedLock)
        {
            tracked = [.. _windowsPinnedByDeskPin.Values];
            _windowsPinnedByDeskPin.Clear();
            _pendingReplacementWindows.Clear();
        }

        var restored = 0;
        foreach (var window in tracked)
        {
            var hWnd = new IntPtr(window.WindowId);
            if (!HasExpectedIdentity(hWnd, window.ProcessId, window.ClassName) || !IsTopmost(hWnd))
            {
                continue;
            }

            if (TrySetTopmostState(hWnd, desiredTopmost: false).Succeeded)
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
        foreach (var hook in _windowEventHooks)
        {
            NativeMethods.UnhookWinEvent(hook);
        }

        _windowEventHooks.Clear();
        lock (_trackedLock)
        {
            _windowsPinnedByDeskPin.Clear();
            _pendingReplacementWindows.Clear();
        }
        _iconCache.Clear();
    }

    internal void ClearEnumerationCache() => _iconCache.Clear();

    internal void RunMaintenanceForTests(long? windowId = null)
    {
        if (windowId is { } id)
        {
            MaintainTrackedWindow(id, ignoreCooldown: true);
        }
        else
        {
            MaintainTrackedWindows(ignoreCooldown: true);
        }
    }

    private void RegisterWindowEventHook(uint eventId)
    {
        var hook = NativeMethods.SetWinEventHook(
            eventId,
            eventId,
            IntPtr.Zero,
            _foregroundCallback,
            0,
            0,
            NativeMethods.WineventOutOfContext | NativeMethods.WineventSkipOwnProcess);
        if (hook != IntPtr.Zero)
        {
            _windowEventHooks.Add(hook);
        }
    }

    private void TrackWindow(DesktopWindow window, string className)
    {
        var tracked = new TrackedTopmostWindow(
            window.Id,
            window.ProcessId,
            className,
            window.Title);
        lock (_trackedLock)
        {
            _windowsPinnedByDeskPin[window.Id] = tracked;
            _pendingReplacementWindows.RemoveAll(candidate =>
                candidate.Window.ProcessId == window.ProcessId &&
                StringComparer.Ordinal.Equals(candidate.Window.ClassName, className));
        }
    }

    private void StopTrackingWindow(long windowId, int processId)
    {
        lock (_trackedLock)
        {
            if (_windowsPinnedByDeskPin.TryGetValue(windowId, out var tracked) &&
                tracked.ProcessId == processId)
            {
                _windowsPinnedByDeskPin.Remove(windowId);
            }

            _pendingReplacementWindows.RemoveAll(candidate =>
                candidate.Window.WindowId == windowId && candidate.Window.ProcessId == processId);
        }
    }

    private static WindowOperationResult CreateTopmostFailure(TopmostChangeResult change)
    {
        if (change.WindowInvalid)
        {
            return WindowOperationResult.Failure(
                WindowOperationError.InvalidWindow,
                "窗口已经关闭或不再可用");
        }

        if (change.NativeError == 5)
        {
            return WindowOperationResult.Failure(
                WindowOperationError.AccessDenied,
                "权限不足：目标程序可能正以管理员身份运行");
        }

        return WindowOperationResult.Failure(
            WindowOperationError.NativeFailure,
            change.NativeError == 0
                ? "目标程序持续重置置顶状态，DeskPin 已停止重试"
                : new Win32Exception(change.NativeError).Message);
    }

    private void OnForegroundChanged(
        IntPtr hWinEventHook,
        uint eventType,
        IntPtr hWnd,
        int idObject,
        int idChild,
        uint idEventThread,
        uint eventTime)
    {
        if (_disposed)
        {
            return;
        }

        switch (eventType)
        {
            case NativeMethods.EventSystemForeground:
                RememberIfEligible(hWnd);
                TryBindReplacementWindow(hWnd);
                MaintainTrackedWindows();
                break;
            case NativeMethods.EventObjectFocus:
                TryBindReplacementWindow(hWnd);
                MaintainTrackedWindows();
                break;
            case NativeMethods.EventSystemMinimizeEnd:
                TryBindReplacementWindow(hWnd);
                MaintainTrackedWindow(hWnd.ToInt64());
                break;
            case NativeMethods.EventObjectDestroy when
                idObject == NativeMethods.ObjidWindow && idChild == NativeMethods.ChildidSelf:
                HandleDestroyedWindow(hWnd.ToInt64());
                break;
            case NativeMethods.EventObjectShow when
                idObject == NativeMethods.ObjidWindow && idChild == NativeMethods.ChildidSelf:
                TryBindReplacementWindow(hWnd);
                MaintainTrackedWindow(hWnd.ToInt64());
                break;
        }
    }

    private void RememberIfEligible(IntPtr hWnd)
    {
        if (hWnd != IntPtr.Zero && TryCreateWindow(hWnd, includeIcon: false, null, null, out _))
        {
            Interlocked.Exchange(ref _lastEligibleWindowId, hWnd.ToInt64());
        }
    }

    private bool TryCreateWindow(
        IntPtr hWnd,
        bool includeIcon,
        Dictionary<int, string>? processNames,
        HashSet<WindowIconCacheKey>? activeIconKeys,
        out DesktopWindow? window)
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

        var titleBuffer = ArrayPool<char>.Shared.Rent(titleLength + 1);
        var classBuffer = ArrayPool<char>.Shared.Rent(256);
        string title;
        string className;
        try
        {
            var copiedTitleLength = NativeMethods.GetWindowText(hWnd, titleBuffer, titleLength + 1);
            title = copiedTitleLength > 0 ? new string(titleBuffer, 0, copiedTitleLength) : string.Empty;

            var classLength = NativeMethods.GetClassName(hWnd, classBuffer, 256);
            className = classLength > 0 ? new string(classBuffer, 0, classLength) : string.Empty;
        }
        finally
        {
            ArrayPool<char>.Shared.Return(titleBuffer);
            ArrayPool<char>.Shared.Return(classBuffer);
        }
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

        if (processNames is null || !processNames.TryGetValue(processId, out var processName))
        {
            processName = "未知应用";
            try
            {
                using var process = Process.GetProcessById(processId);
                processName = process.ProcessName;
            }
            catch (Exception exception) when (exception is ArgumentException or InvalidOperationException)
            {
                // The process can exit between enumeration and lookup.
            }

            processNames?.Add(processId, processName);
        }

        var iconKey = new WindowIconCacheKey(hWnd.ToInt64(), processId);
        activeIconKeys?.Add(iconKey);

        window = new DesktopWindow(
            hWnd.ToInt64(),
            title.Trim(),
            processName,
            processId,
            (style & NativeMethods.WsExTopmost) != 0,
            includeIcon ? _iconCache.GetOrCreate(hWnd, processId) : null);
        return true;
    }

    private static int CompareWindows(DesktopWindow left, DesktopWindow right)
    {
        var topmostComparison = right.IsTopmost.CompareTo(left.IsTopmost);
        if (topmostComparison != 0)
        {
            return topmostComparison;
        }

        var processComparison = StringComparer.CurrentCultureIgnoreCase.Compare(left.ProcessName, right.ProcessName);
        if (processComparison != 0)
        {
            return processComparison;
        }

        var titleComparison = StringComparer.CurrentCultureIgnoreCase.Compare(left.Title, right.Title);
        return titleComparison != 0 ? titleComparison : left.Id.CompareTo(right.Id);
    }

    private static bool IsTopmost(IntPtr hWnd) =>
        (NativeMethods.GetWindowLongPtr(hWnd, NativeMethods.GwlExStyle).ToInt64() & NativeMethods.WsExTopmost) != 0;

    private static TopmostChangeResult TrySetTopmostState(IntPtr hWnd, bool desiredTopmost)
    {
        var insertAfter = desiredTopmost ? NativeMethods.HwndTopmost : NativeMethods.HwndNoTopmost;
        var baseFlags = NativeMethods.SwpNoMove | NativeMethods.SwpNoSize | NativeMethods.SwpNoActivate;
        var anyNativeCallSucceeded = false;
        var lastNativeError = 0;
        var attemptClock = Stopwatch.StartNew();

        for (var attempt = 0; attempt < TopmostAttemptOffsetsMilliseconds.Length; attempt++)
        {
            var offset = TopmostAttemptOffsetsMilliseconds[attempt];
            var delay = offset - (int)attemptClock.ElapsedMilliseconds;
            if (delay > 0)
            {
                Thread.Sleep(delay);
            }

            if (!NativeMethods.IsWindow(hWnd))
            {
                return new TopmostChangeResult(false, true, 0, anyNativeCallSucceeded);
            }

            var flags = attempt == 0 ? baseFlags : baseFlags | NativeMethods.SwpNoSendChanging;
            if (TrySetWindowPosition(hWnd, insertAfter, flags, out var nativeError))
            {
                anyNativeCallSucceeded = true;
                var verification = WaitForTopmostState(hWnd, desiredTopmost);
                if (verification == TopmostVerificationResult.Matched)
                {
                    return new TopmostChangeResult(true, false, 0, true);
                }

                if (verification == TopmostVerificationResult.InvalidWindow)
                {
                    return new TopmostChangeResult(false, true, 0, true);
                }
            }
            else
            {
                lastNativeError = nativeError;
                if (nativeError == 5)
                {
                    return new TopmostChangeResult(false, false, nativeError, anyNativeCallSucceeded);
                }
            }
        }

        return new TopmostChangeResult(
            false,
            false,
            anyNativeCallSucceeded ? 0 : lastNativeError,
            anyNativeCallSucceeded);
    }

    private static TopmostChangeResult TryMaintainTopmost(IntPtr hWnd)
    {
        if (!NativeMethods.IsWindow(hWnd))
        {
            return new TopmostChangeResult(false, true, 0, false);
        }

        if (!TrySetWindowPosition(
            hWnd,
            NativeMethods.HwndTopmost,
            NativeMethods.SwpNoMove |
            NativeMethods.SwpNoSize |
            NativeMethods.SwpNoActivate |
            NativeMethods.SwpNoSendChanging,
            out var nativeError))
        {
            return new TopmostChangeResult(false, false, nativeError, false);
        }

        var verification = WaitForTopmostState(hWnd, desiredTopmost: true);
        return verification switch
        {
            TopmostVerificationResult.Matched => new TopmostChangeResult(true, false, 0, true),
            TopmostVerificationResult.InvalidWindow => new TopmostChangeResult(false, true, 0, true),
            _ => new TopmostChangeResult(false, false, 0, true),
        };
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

    private static TopmostVerificationResult WaitForTopmostState(IntPtr hWnd, bool desiredTopmost)
    {
        for (var attempt = 0; attempt < TopmostVerificationAttempts; attempt++)
        {
            if (!NativeMethods.IsWindow(hWnd))
            {
                return TopmostVerificationResult.InvalidWindow;
            }

            if (IsTopmost(hWnd) != desiredTopmost)
            {
                return TopmostVerificationResult.Mismatched;
            }

            if (attempt + 1 < TopmostVerificationAttempts)
            {
                Thread.Sleep(TopmostVerificationDelayMilliseconds);
            }
        }

        return TopmostVerificationResult.Matched;
    }

    private void MaintainTrackedWindows(bool ignoreCooldown = false)
    {
        long[] windowIds;
        lock (_trackedLock)
        {
            windowIds = [.. _windowsPinnedByDeskPin.Keys];
        }

        foreach (var windowId in windowIds)
        {
            MaintainTrackedWindow(windowId, ignoreCooldown);
        }
    }

    private void MaintainTrackedWindow(long windowId, bool ignoreCooldown = false)
    {
        TrackedTopmostWindow? tracked;
        lock (_trackedLock)
        {
            _windowsPinnedByDeskPin.TryGetValue(windowId, out tracked);
        }

        if (tracked is null)
        {
            return;
        }

        var hWnd = new IntPtr(windowId);
        if (!HasExpectedIdentity(hWnd, tracked.ProcessId, tracked.ClassName))
        {
            MoveToPendingReplacement(tracked);
            return;
        }

        if (IsTopmost(hWnd))
        {
            lock (_trackedLock)
            {
                if (_windowsPinnedByDeskPin.TryGetValue(windowId, out var current) &&
                    ReferenceEquals(current, tracked))
                {
                    current.ConsecutiveFailures = 0;
                    current.RepairSuspended = false;
                    current.WarningRaised = false;
                }
            }
            return;
        }

        var now = Environment.TickCount64;
        lock (_trackedLock)
        {
            if (!_windowsPinnedByDeskPin.TryGetValue(windowId, out var current) ||
                !ReferenceEquals(current, tracked) ||
                current.RepairSuspended ||
                (!ignoreCooldown && now - current.LastRepairTick < MaintenanceCooldownMilliseconds))
            {
                return;
            }

            current.LastRepairTick = now;
        }

        var change = TryMaintainTopmost(hWnd);
        TopmostMaintenanceFailedEventArgs? warning = null;
        lock (_trackedLock)
        {
            if (!_windowsPinnedByDeskPin.TryGetValue(windowId, out var current) ||
                !ReferenceEquals(current, tracked))
            {
                return;
            }

            if (change.Succeeded)
            {
                current.ConsecutiveFailures = 0;
                return;
            }

            if (change.WindowInvalid)
            {
                _windowsPinnedByDeskPin.Remove(windowId);
                _pendingReplacementWindows.Add(new PendingReplacementWindow(
                    current,
                    Environment.TickCount64 + ReplacementWindowMilliseconds));
                return;
            }

            current.ConsecutiveFailures++;
            if (current.ConsecutiveFailures >= MaximumMaintenanceFailures)
            {
                current.RepairSuspended = true;
                if (!current.WarningRaised)
                {
                    current.WarningRaised = true;
                    warning = new TopmostMaintenanceFailedEventArgs(
                        current.WindowId,
                        current.Title,
                        "目标程序持续重置置顶状态，DeskPin 已暂停维护该窗口");
                }
            }
        }

        if (warning is not null)
        {
            MaintenanceFailed?.Invoke(this, warning);
        }
    }

    private void HandleDestroyedWindow(long windowId)
    {
        TrackedTopmostWindow? tracked = null;
        lock (_trackedLock)
        {
            if (_windowsPinnedByDeskPin.Remove(windowId, out tracked))
            {
                _pendingReplacementWindows.Add(new PendingReplacementWindow(
                    tracked,
                    Environment.TickCount64 + ReplacementWindowMilliseconds));
            }

            PruneExpiredPendingReplacementsNoLock(Environment.TickCount64);
        }
    }

    private void MoveToPendingReplacement(TrackedTopmostWindow tracked)
    {
        lock (_trackedLock)
        {
            if (_windowsPinnedByDeskPin.TryGetValue(tracked.WindowId, out var current) &&
                ReferenceEquals(current, tracked))
            {
                _windowsPinnedByDeskPin.Remove(tracked.WindowId);
                _pendingReplacementWindows.Add(new PendingReplacementWindow(
                    tracked,
                    Environment.TickCount64 + ReplacementWindowMilliseconds));
            }
        }
    }

    private void TryBindReplacementWindow(IntPtr hWnd)
    {
        if (!TryCreateWindow(hWnd, includeIcon: false, null, null, out var candidate))
        {
            return;
        }

        var className = GetWindowClassName(hWnd);
        PendingReplacementWindow? replacement;
        var now = Environment.TickCount64;
        lock (_trackedLock)
        {
            PruneExpiredPendingReplacementsNoLock(now);
            var matches = _pendingReplacementWindows.Where(item =>
                item.Window.ProcessId == candidate!.ProcessId &&
                StringComparer.Ordinal.Equals(item.Window.ClassName, className)).ToArray();
            replacement = matches.Length == 1 ? matches[0] : null;
        }

        if (replacement is null || CountEligibleReplacementCandidates(candidate!.ProcessId, className) != 1)
        {
            return;
        }

        lock (_trackedLock)
        {
            if (!_pendingReplacementWindows.Remove(replacement))
            {
                return;
            }

            replacement.Window.WindowId = hWnd.ToInt64();
            replacement.Window.Title = candidate.Title;
            replacement.Window.LastRepairTick = 0;
            replacement.Window.ConsecutiveFailures = 0;
            replacement.Window.RepairSuspended = false;
            replacement.Window.WarningRaised = false;
            _windowsPinnedByDeskPin[replacement.Window.WindowId] = replacement.Window;
        }
    }

    private int CountEligibleReplacementCandidates(int processId, string className)
    {
        var count = 0;
        NativeMethods.EnumWindows((candidate, _) =>
        {
            if (TryCreateWindow(candidate, includeIcon: false, null, null, out var window) &&
                window!.ProcessId == processId &&
                StringComparer.Ordinal.Equals(GetWindowClassName(candidate), className))
            {
                count++;
            }

            return count < 2;
        }, IntPtr.Zero);
        return count;
    }

    private void ReconcileTrackedWindows()
    {
        TrackedTopmostWindow[] tracked;
        lock (_trackedLock)
        {
            PruneExpiredPendingReplacementsNoLock(Environment.TickCount64);
            tracked = [.. _windowsPinnedByDeskPin.Values];
        }

        foreach (var window in tracked)
        {
            if (!HasExpectedIdentity(new IntPtr(window.WindowId), window.ProcessId, window.ClassName))
            {
                MoveToPendingReplacement(window);
            }
        }
    }

    private void PruneExpiredPendingReplacementsNoLock(long now) =>
        _pendingReplacementWindows.RemoveAll(candidate => candidate.ExpiresAtTick <= now);

    private static bool HasExpectedIdentity(IntPtr hWnd, int processId, string className)
    {
        if (!NativeMethods.IsWindow(hWnd))
        {
            return false;
        }

        NativeMethods.GetWindowThreadProcessId(hWnd, out var actualProcessId);
        return unchecked((int)actualProcessId) == processId &&
            StringComparer.Ordinal.Equals(GetWindowClassName(hWnd), className);
    }

    private static string GetWindowClassName(IntPtr hWnd)
    {
        var buffer = ArrayPool<char>.Shared.Rent(256);
        try
        {
            var length = NativeMethods.GetClassName(hWnd, buffer, 256);
            return length > 0 ? new string(buffer, 0, length) : string.Empty;
        }
        finally
        {
            ArrayPool<char>.Shared.Return(buffer);
        }
    }

    private enum TopmostVerificationResult
    {
        Matched,
        Mismatched,
        InvalidWindow,
    }

    private readonly record struct TopmostChangeResult(
        bool Succeeded,
        bool WindowInvalid,
        int NativeError,
        bool NativeCallSucceeded);

    private sealed class TrackedTopmostWindow(
        long windowId,
        int processId,
        string className,
        string title)
    {
        internal long WindowId { get; set; } = windowId;
        internal int ProcessId { get; } = processId;
        internal string ClassName { get; } = className;
        internal string Title { get; set; } = title;
        internal long LastRepairTick { get; set; }
        internal int ConsecutiveFailures { get; set; }
        internal bool RepairSuspended { get; set; }
        internal bool WarningRaised { get; set; }
    }

    private sealed record PendingReplacementWindow(
        TrackedTopmostWindow Window,
        long ExpiresAtTick);
}

internal sealed class TopmostMaintenanceFailedEventArgs(
    long windowId,
    string title,
    string message) : EventArgs
{
    internal long WindowId { get; } = windowId;
    internal string Title { get; } = title;
    internal string Message { get; } = message;
}
