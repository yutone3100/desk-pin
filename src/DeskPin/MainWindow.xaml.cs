using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using DeskPin.Models;
using DeskPin.Services;
using DeskPin.ViewModels;

namespace DeskPin;

public partial class MainWindow : Window
{
    private readonly Action _openSettings;
    private readonly Action _requestElevatedRestart;
    private readonly Func<WindowViewMode, Task<SettingsApplyResult>> _saveViewPreference;
    private readonly MainViewModel _viewModel;
    private readonly DispatcherTimer _refreshTimer;
    private readonly IdleMemoryReclaimer _memoryReclaimer;
    private HwndSource? _windowSource;
    private bool _contentActive;
    private bool _allowClose;

    public MainWindow(
        IWindowManager windowManager,
        Action openSettings,
        Action requestElevatedRestart,
        WindowViewMode initialViewMode,
        Func<WindowViewMode, Task<SettingsApplyResult>> saveViewPreference)
    {
        InitializeComponent();
        _openSettings = openSettings;
        _requestElevatedRestart = requestElevatedRestart;
        _saveViewPreference = saveViewPreference;
        _viewModel = new MainViewModel(windowManager, initialViewMode);
        DataContext = _viewModel;
        WindowShadowService.Attach(this);
        _refreshTimer = new DispatcherTimer(TimeSpan.FromMilliseconds(1500), DispatcherPriority.Background, OnRefreshTimer, Dispatcher);
        _refreshTimer.Stop();
        _memoryReclaimer = new IdleMemoryReclaimer();
        _viewModel.Suspend();
        SourceInitialized += OnSourceInitialized;
        Loaded += OnLoaded;
        Closing += OnClosing;
        Closed += OnClosed;
    }

    public Task RefreshAsync() => _contentActive ? _viewModel.RefreshAsync() : Task.CompletedTask;

    internal bool IsContentActive => _contentActive;
    internal bool IsRefreshTimerEnabled => _refreshTimer.IsEnabled;
    internal bool IsMemoryReclamationScheduled => _memoryReclaimer.IsScheduled;

    internal T? FindContentElement<T>(string name) where T : FrameworkElement =>
        FindNamedVisual<T>(MainContentHost, name);

    internal void ActivateContent()
    {
        _memoryReclaimer.Cancel();
        if (!_contentActive)
        {
            _contentActive = true;
            _viewModel.Resume();
            MainContentHost.ContentTemplate = (DataTemplate)Resources["MainContentTemplate"];
            MainContentHost.Content = _viewModel;
        }

        if (IsLoaded && WindowState != WindowState.Minimized)
        {
            _refreshTimer.Start();
            _ = _viewModel.RefreshAsync();
        }
    }

    internal void EnterBackgroundMode()
    {
        if (_contentActive)
        {
            SuspendContent();
        }
        else
        {
            _refreshTimer.Stop();
            _viewModel.Suspend();
            _memoryReclaimer.Schedule();
        }

        Hide();
    }

    public void AllowClose()
    {
        _allowClose = true;
        _refreshTimer.Stop();
        _memoryReclaimer.Dispose();
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (_contentActive)
        {
            _refreshTimer.Start();
            await _viewModel.RefreshAsync();
        }
    }

    private void OnClosing(object? sender, CancelEventArgs e)
    {
        if (_allowClose)
        {
            return;
        }

        e.Cancel = true;
        SuspendContent();
        Hide();
    }

    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        var handle = new WindowInteropHelper(this).Handle;
        _windowSource = HwndSource.FromHwnd(handle);
        _windowSource?.AddHook(WindowMessageHook);
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        _windowSource?.RemoveHook(WindowMessageHook);
        _windowSource = null;
    }

    private static IntPtr WindowMessageHook(
        IntPtr hWnd,
        int message,
        IntPtr wParam,
        IntPtr lParam,
        ref bool handled)
    {
        if (message == NativeMethods.WmGetMinMaxInfo)
        {
            handled = WindowMaximizeHelper.TryApplyWorkArea(hWnd, lParam);
        }

        return IntPtr.Zero;
    }

    private async void OnRefreshTimer(object? sender, EventArgs e) => await _viewModel.RefreshAsync();

    private async void ToggleWindow_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: DesktopWindow window })
        {
            return;
        }

        var result = await _viewModel.ToggleAsync(window);
        if (!result.Succeeded)
        {
            if (result.Error == WindowOperationError.AccessDenied)
            {
                _requestElevatedRestart();
                return;
            }

            System.Windows.MessageBox.Show(this, result.Message, "DeskPin", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void ShowWindowMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if (GetContextWindow(sender) is not { } window)
        {
            return;
        }

        ShowWindowActionError(_viewModel.ShowWindow(window));
    }

    private void CloseWindowMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if (GetContextWindow(sender) is not { } window)
        {
            return;
        }

        ShowWindowActionError(_viewModel.CloseWindow(window));
    }

    private static DesktopWindow? GetContextWindow(object sender) =>
        (sender as FrameworkElement)?.DataContext as DesktopWindow;

    private void ShowWindowActionError(WindowActionResult result)
    {
        if (!result.Succeeded)
        {
            System.Windows.MessageBox.Show(this, result.Message, "DeskPin", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void ShowAll_Click(object sender, RoutedEventArgs e) => _viewModel.OnlyTopmost = false;

    private void ShowPinned_Click(object sender, RoutedEventArgs e) => _viewModel.OnlyTopmost = true;

    private async void CardsView_Click(object sender, RoutedEventArgs e) =>
        await ChangeViewModeAsync(WindowViewMode.Cards);

    private async void ListView_Click(object sender, RoutedEventArgs e) =>
        await ChangeViewModeAsync(WindowViewMode.List);

    private async Task ChangeViewModeAsync(WindowViewMode viewMode)
    {
        if (!_viewModel.SetViewMode(viewMode))
        {
            return;
        }

        var result = await _saveViewPreference(viewMode);
        if (!result.Succeeded)
        {
            _viewModel.SetViewPreferenceError(result.Message);
        }
    }

    private void Minimize_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

    private void Maximize_Click(object sender, RoutedEventArgs e) =>
        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;

    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    private void Window_StateChanged(object sender, EventArgs e)
    {
        var maximized = WindowState == WindowState.Maximized;
        if (FindContentElement<Border>("WindowFrame") is { } frame)
        {
            frame.CornerRadius = maximized ? new CornerRadius(0) : new CornerRadius(11);
        }

        if (FindContentElement<Button>("MaximizeButton") is { } maximizeButton)
        {
            maximizeButton.Content = maximized ? "\uE923" : "\uE922";
            maximizeButton.ToolTip = maximized ? "还原" : "最大化";
        }

        if (!_contentActive)
        {
            return;
        }

        if (WindowState == WindowState.Minimized)
        {
            _refreshTimer.Stop();
        }
        else
        {
            _refreshTimer.Start();
            _ = _viewModel.RefreshAsync();
        }
    }

    private void Settings_Click(object sender, RoutedEventArgs e) => _openSettings();

    private void SuspendContent()
    {
        if (!_contentActive)
        {
            return;
        }

        _contentActive = false;
        _refreshTimer.Stop();
        _viewModel.Suspend();
        MainContentHost.Content = null;
        MainContentHost.ContentTemplate = null;
        _memoryReclaimer.Schedule();
    }

    private static T? FindNamedVisual<T>(DependencyObject root, string name) where T : FrameworkElement
    {
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
        {
            var child = VisualTreeHelper.GetChild(root, index);
            if (child is T match && match.Name == name)
            {
                return match;
            }

            if (FindNamedVisual<T>(child, name) is { } descendant)
            {
                return descendant;
            }
        }

        return null;
    }
}
