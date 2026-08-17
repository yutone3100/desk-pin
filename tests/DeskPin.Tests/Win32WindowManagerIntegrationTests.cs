using System.Windows.Forms;
using System.Runtime.InteropServices;
using DeskPin.Services;

namespace DeskPin.Tests;

public sealed class Win32WindowManagerIntegrationTests
{
    [Fact]
    public async Task TogglesWithoutMovingOrActivatingAndRestoresOnExit()
    {
        if (!Environment.UserInteractive)
        {
            return;
        }

        using var host = await NativeTestWindow.StartAsync();
        using var manager = new Win32WindowManager(ownProcessId: -1);
        var listed = manager.GetWindows().Single(window => window.Id == host.Handle.ToInt64());
        Assert.False(listed.IsTopmost);
        Assert.NotNull(listed.Icon);
        var repeated = manager.GetWindows().Single(window => window.Id == host.Handle.ToInt64());
        Assert.Same(listed.Icon, repeated.Icon);

        Assert.True(NativeMethods.GetWindowRect(host.Handle, out var beforeRect));
        var beforeForeground = NativeMethods.GetForegroundWindow();
        var pinResult = manager.ToggleTopmost(host.Handle.ToInt64());
        Assert.True(pinResult.Succeeded, pinResult.Message);
        Assert.True(pinResult.IsTopmost);
        Assert.True(NativeMethods.GetWindowRect(host.Handle, out var afterRect));
        Assert.Equal(beforeRect, afterRect);
        Assert.Equal(beforeForeground, NativeMethods.GetForegroundWindow());

        Assert.Equal(1, manager.RestoreWindowsPinnedByDeskPin());
        var restored = manager.GetWindows().Single(window => window.Id == host.Handle.ToInt64());
        Assert.False(restored.IsTopmost);
    }

    [Fact]
    public async Task ClosedWindowReturnsInvalidWindow()
    {
        if (!Environment.UserInteractive)
        {
            return;
        }

        var host = await NativeTestWindow.StartAsync();
        var handle = host.Handle;
        host.Dispose();
        using var manager = new Win32WindowManager(ownProcessId: -1);

        var result = manager.ToggleTopmost(handle.ToInt64());
        var showResult = manager.ShowWindow(handle.ToInt64());
        var closeResult = manager.CloseWindow(handle.ToInt64());

        Assert.False(result.Succeeded);
        Assert.Equal(DeskPin.Models.WindowOperationError.InvalidWindow, result.Error);
        Assert.False(showResult.Succeeded);
        Assert.Equal(DeskPin.Models.WindowOperationError.InvalidWindow, showResult.Error);
        Assert.False(closeResult.Succeeded);
        Assert.Equal(DeskPin.Models.WindowOperationError.InvalidWindow, closeResult.Error);
    }

    [Fact]
    public async Task ShowWindowRestoresMinimizedWindow()
    {
        if (!Environment.UserInteractive)
        {
            return;
        }

        using var host = await NativeTestWindow.StartAsync();
        using var manager = new Win32WindowManager(ownProcessId: -1);
        NativeMethods.ShowWindowAsync(host.Handle, NativeMethods.SwMinimize);
        Assert.True(await WaitUntilAsync(() => NativeMethods.IsIconic(host.Handle)));

        var result = manager.ShowWindow(host.Handle.ToInt64());

        Assert.True(result.Succeeded, result.Message);
        Assert.True(await WaitUntilAsync(() => !NativeMethods.IsIconic(host.Handle)));
        Assert.True(NativeMethods.IsWindowVisible(host.Handle));
    }

    [Fact]
    public async Task CloseWindowPostsNormalCloseAndRestoreSkipsDestroyedHandle()
    {
        if (!Environment.UserInteractive)
        {
            return;
        }

        using var host = await NativeTestWindow.StartAsync();
        using var manager = new Win32WindowManager(ownProcessId: -1);
        var pinResult = manager.ToggleTopmost(host.Handle.ToInt64());
        Assert.True(pinResult.Succeeded, pinResult.Message);

        var closeResult = manager.CloseWindow(host.Handle.ToInt64());

        Assert.True(closeResult.Succeeded, closeResult.Message);
        Assert.True(await WaitUntilAsync(() => !NativeMethods.IsWindow(host.Handle)));
        Assert.Equal(0, manager.RestoreWindowsPinnedByDeskPin());
    }

    [Fact]
    public async Task FallbackBypassesFirstWindowPositionRejection()
    {
        if (!Environment.UserInteractive)
        {
            return;
        }

        using var host = await NativeTestWindow.StartAsync(rejectFirstZOrderChanges: true);
        using var manager = new Win32WindowManager(ownProcessId: -1);

        var pinResult = manager.ToggleTopmost(host.Handle.ToInt64());

        Assert.True(pinResult.Succeeded, pinResult.Message);
        Assert.True(pinResult.IsTopmost);
        Assert.Equal(1, manager.RestoreWindowsPinnedByDeskPin());
        var restored = manager.GetWindows().Single(window => window.Id == host.Handle.ToInt64());
        Assert.False(restored.IsTopmost);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    public async Task LimitedRetrySurvivesTemporaryTopmostResets(int resetCount)
    {
        if (!Environment.UserInteractive)
        {
            return;
        }

        using var host = await NativeTestWindow.StartAsync(resetTopmostChanges: resetCount);
        using var manager = new Win32WindowManager(ownProcessId: -1);

        var pinResult = manager.ToggleTopmost(host.Handle.ToInt64());

        Assert.True(pinResult.Succeeded, pinResult.Message);
        Assert.True((NativeMethods.GetWindowLongPtr(host.Handle, NativeMethods.GwlExStyle).ToInt64() & NativeMethods.WsExTopmost) != 0);
        Assert.Equal(1, manager.RestoreWindowsPinnedByDeskPin());
    }

    [Fact]
    public async Task PermanentTopmostResetStopsAfterBoundedRetries()
    {
        if (!Environment.UserInteractive)
        {
            return;
        }

        using var host = await NativeTestWindow.StartAsync(resetTopmostChanges: int.MaxValue);
        using var manager = new Win32WindowManager(ownProcessId: -1);
        var clock = System.Diagnostics.Stopwatch.StartNew();

        var pinResult = manager.ToggleTopmost(host.Handle.ToInt64());

        Assert.False(pinResult.Succeeded);
        Assert.Equal(DeskPin.Models.WindowOperationError.NativeFailure, pinResult.Error);
        Assert.Contains("持续重置", pinResult.Message);
        Assert.True(clock.Elapsed < TimeSpan.FromSeconds(2));
    }

    [Fact]
    public async Task MaintenanceRepairsLostTopmostAndUnpinStopsMaintenance()
    {
        if (!Environment.UserInteractive)
        {
            return;
        }

        using var host = await NativeTestWindow.StartAsync();
        using var manager = new Win32WindowManager(ownProcessId: -1);
        Assert.True(manager.ToggleTopmost(host.Handle.ToInt64()).Succeeded);
        Assert.True(NativeMethods.SetWindowPos(
            host.Handle,
            NativeMethods.HwndNoTopmost,
            0,
            0,
            0,
            0,
            NativeMethods.SwpNoMove | NativeMethods.SwpNoSize | NativeMethods.SwpNoActivate));

        manager.RunMaintenanceForTests(host.Handle.ToInt64());

        Assert.True((NativeMethods.GetWindowLongPtr(host.Handle, NativeMethods.GwlExStyle).ToInt64() & NativeMethods.WsExTopmost) != 0);
        Assert.True(manager.ToggleTopmost(host.Handle.ToInt64()).Succeeded);
        manager.RunMaintenanceForTests(host.Handle.ToInt64());
        Assert.False((NativeMethods.GetWindowLongPtr(host.Handle, NativeMethods.GwlExStyle).ToInt64() & NativeMethods.WsExTopmost) != 0);
    }

    [Fact]
    public async Task MaintenanceSuspendsAfterThreeFailuresAndWarnsOnce()
    {
        if (!Environment.UserInteractive)
        {
            return;
        }

        using var host = await NativeTestWindow.StartAsync();
        using var manager = new Win32WindowManager(ownProcessId: -1);
        Assert.True(manager.ToggleTopmost(host.Handle.ToInt64()).Succeeded);
        host.SetTopmostResetCount(int.MaxValue);
        Assert.True(NativeMethods.SetWindowPos(
            host.Handle,
            NativeMethods.HwndNoTopmost,
            0,
            0,
            0,
            0,
            NativeMethods.SwpNoMove | NativeMethods.SwpNoSize | NativeMethods.SwpNoActivate));
        var warningCount = 0;
        manager.MaintenanceFailed += (_, _) => warningCount++;

        for (var attempt = 0; attempt < 5; attempt++)
        {
            manager.RunMaintenanceForTests(host.Handle.ToInt64());
        }

        Assert.Equal(1, warningCount);
        Assert.False((NativeMethods.GetWindowLongPtr(host.Handle, NativeMethods.GwlExStyle).ToInt64() & NativeMethods.WsExTopmost) != 0);
    }

    private static async Task<bool> WaitUntilAsync(Func<bool> condition)
    {
        for (var attempt = 0; attempt < 100; attempt++)
        {
            if (condition())
            {
                return true;
            }

            await Task.Delay(20);
        }

        return condition();
    }

    private sealed class NativeTestWindow : IDisposable
    {
        private readonly Thread _thread;
        private readonly Form _form;
        private bool _disposed;

        private NativeTestWindow(Thread thread, Form form, IntPtr handle)
        {
            _thread = thread;
            _form = form;
            Handle = handle;
        }

        public IntPtr Handle { get; }

        public void SetTopmostResetCount(int resetCount) =>
            _form.Invoke(new Action(() => ((PositionResistantForm)_form).SetTopmostResetCount(resetCount)));

        public static async Task<NativeTestWindow> StartAsync(
            bool rejectFirstZOrderChanges = false,
            int resetTopmostChanges = 0)
        {
            var ready = new TaskCompletionSource<(Form Form, IntPtr Handle)>(TaskCreationOptions.RunContinuationsAsynchronously);
            var thread = new Thread(() =>
            {
                var form = new PositionResistantForm(rejectFirstZOrderChanges, resetTopmostChanges)
                {
                    Text = $"DeskPin 集成测试 {Guid.NewGuid():N}",
                    Width = 360,
                    Height = 220,
                    ShowInTaskbar = true,
                };
                form.Shown += (_, _) => ready.TrySetResult((form, form.Handle));
                System.Windows.Forms.Application.Run(form);
            })
            {
                IsBackground = true,
            };
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
            var result = await ready.Task.WaitAsync(TimeSpan.FromSeconds(5));
            return new NativeTestWindow(thread, result.Form, result.Handle);
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            if (!_form.IsDisposed)
            {
                _form.BeginInvoke(new Action(_form.Close));
                _thread.Join(TimeSpan.FromSeconds(5));
            }

            _disposed = true;
        }

        private sealed class PositionResistantForm(
            bool rejectFirstZOrderChanges,
            int resetTopmostChanges) : Form
        {
            private const int WmWindowPosChanging = 0x0046;
            private const int WmWindowPosChanged = 0x0047;
            private bool _rejectTopmost = rejectFirstZOrderChanges;
            private bool _rejectNotTopmost = rejectFirstZOrderChanges;
            private int _resetTopmostChanges = resetTopmostChanges;

            internal void SetTopmostResetCount(int resetCount) => _resetTopmostChanges = resetCount;

            protected override void WndProc(ref Message message)
            {
                if (message.Msg == WmWindowPosChanging && message.LParam != IntPtr.Zero)
                {
                    var position = Marshal.PtrToStructure<WindowPosition>(message.LParam);
                    if (_rejectTopmost && position.InsertAfter == NativeMethods.HwndTopmost)
                    {
                        position.InsertAfter = NativeMethods.HwndNoTopmost;
                        _rejectTopmost = false;
                        Marshal.StructureToPtr(position, message.LParam, false);
                    }
                    else if (_rejectNotTopmost && position.InsertAfter == NativeMethods.HwndNoTopmost)
                    {
                        position.InsertAfter = NativeMethods.HwndTopmost;
                        _rejectNotTopmost = false;
                        Marshal.StructureToPtr(position, message.LParam, false);
                    }
                }

                base.WndProc(ref message);

                if (message.Msg == WmWindowPosChanged &&
                    _resetTopmostChanges > 0 &&
                    (NativeMethods.GetWindowLongPtr(Handle, NativeMethods.GwlExStyle).ToInt64() & NativeMethods.WsExTopmost) != 0)
                {
                    _resetTopmostChanges--;
                    NativeMethods.SetWindowPos(
                        Handle,
                        NativeMethods.HwndNoTopmost,
                        0,
                        0,
                        0,
                        0,
                        NativeMethods.SwpNoMove | NativeMethods.SwpNoSize | NativeMethods.SwpNoActivate);
                }
            }
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct WindowPosition
        {
            internal IntPtr Window;
            internal IntPtr InsertAfter;
            internal int X;
            internal int Y;
            internal int Width;
            internal int Height;
            internal uint Flags;
        }
    }
}
