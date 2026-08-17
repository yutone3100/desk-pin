using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;

namespace DeskPin.Services;

internal static class WindowShadowService
{
    private static readonly ConditionalWeakTable<Window, ShadowController> Controllers = new();

    internal static void Attach(Window window)
    {
        ArgumentNullException.ThrowIfNull(window);
        Controllers.GetValue(window, static owner => new ShadowController(owner));
    }

    internal static void Refresh(Window window)
    {
        if (Controllers.TryGetValue(window, out var controller))
        {
            controller.QueueRefresh();
        }
    }

    internal static bool IsAttached(Window window) => Controllers.TryGetValue(window, out _);

    private sealed class ShadowController
    {
        private readonly Window _window;
        private HwndSource? _source;
        private IntPtr _handle;
        private bool _refreshQueued;
        private bool _disposed;

        internal ShadowController(Window window)
        {
            _window = window;
            _window.SourceInitialized += OnSourceInitialized;
            _window.StateChanged += OnStateChanged;
            _window.Activated += OnActivated;
            _window.Closed += OnClosed;

            if (new WindowInteropHelper(window).Handle != IntPtr.Zero)
            {
                InitializeSource();
            }
        }

        internal void QueueRefresh()
        {
            if (_disposed || _refreshQueued || _window.WindowState != WindowState.Normal)
            {
                return;
            }

            _refreshQueued = true;
            _window.Dispatcher.BeginInvoke(() =>
            {
                _refreshQueued = false;
                ApplyShadow();
            }, DispatcherPriority.Loaded);
        }

        private void OnSourceInitialized(object? sender, EventArgs e) => InitializeSource();

        private void InitializeSource()
        {
            if (_disposed || _source is not null)
            {
                return;
            }

            _handle = new WindowInteropHelper(_window).Handle;
            if (_handle == IntPtr.Zero)
            {
                return;
            }

            _source = HwndSource.FromHwnd(_handle);
            _source?.AddHook(WindowMessageHook);
            QueueRefresh();
        }

        private void OnStateChanged(object? sender, EventArgs e)
        {
            if (_window.WindowState == WindowState.Normal)
            {
                QueueRefresh();
            }
        }

        private void OnActivated(object? sender, EventArgs e) => QueueRefresh();

        private IntPtr WindowMessageHook(
            IntPtr hWnd,
            int message,
            IntPtr wParam,
            IntPtr lParam,
            ref bool handled)
        {
            if (message is NativeMethods.WmDwmCompositionChanged or NativeMethods.WmThemeChanged)
            {
                QueueRefresh();
            }

            return IntPtr.Zero;
        }

        private void ApplyShadow()
        {
            if (_disposed || _handle == IntPtr.Zero || _window.WindowState != WindowState.Normal)
            {
                return;
            }

            if (NativeMethods.DwmIsCompositionEnabled(out var compositionEnabled) != 0 || !compositionEnabled)
            {
                return;
            }

            var renderingPolicy = NativeMethods.DwmNcRenderingEnabled;
            if (NativeMethods.DwmSetWindowAttribute(
                    _handle,
                    NativeMethods.DwmwaNcRenderingPolicy,
                    ref renderingPolicy,
                    sizeof(int)) != 0)
            {
                return;
            }

            var borderColor = NativeMethods.DwmColorNone;
            if (NativeMethods.DwmSetWindowAttribute(
                    _handle,
                    NativeMethods.DwmwaBorderColor,
                    ref borderColor,
                    sizeof(int)) != 0)
            {
                RemoveExtendedFrame();
                return;
            }

            NativeMethods.SetWindowPos(
                _handle,
                IntPtr.Zero,
                0,
                0,
                0,
                0,
                NativeMethods.SwpNoMove |
                NativeMethods.SwpNoSize |
                NativeMethods.SwpNoZOrder |
                NativeMethods.SwpNoActivate |
                NativeMethods.SwpFrameChanged);

            var margins = new NativeMethods.Margins
            {
                LeftWidth = 1,
                RightWidth = 1,
                TopHeight = 1,
                BottomHeight = 1,
            };
            NativeMethods.DwmExtendFrameIntoClientArea(_handle, ref margins);

            borderColor = NativeMethods.DwmColorNone;
            NativeMethods.DwmSetWindowAttribute(
                _handle,
                NativeMethods.DwmwaBorderColor,
                ref borderColor,
                sizeof(int));
        }

        private void RemoveExtendedFrame()
        {
            var margins = new NativeMethods.Margins();
            NativeMethods.DwmExtendFrameIntoClientArea(_handle, ref margins);
        }

        private void OnClosed(object? sender, EventArgs e)
        {
            if (_disposed)
            {
                return;
            }

            _source?.RemoveHook(WindowMessageHook);
            _source = null;
            _window.SourceInitialized -= OnSourceInitialized;
            _window.StateChanged -= OnStateChanged;
            _window.Activated -= OnActivated;
            _window.Closed -= OnClosed;
            _disposed = true;
            Controllers.Remove(_window);
        }
    }
}
