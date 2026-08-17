using System.IO;
using System.Windows;
using System.Windows.Interop;
using DeskPin.Models;
using DeskPin.Services;

namespace DeskPin;

public partial class App : System.Windows.Application
{
    private readonly ISettingsStore _settingsStore = new JsonSettingsStore();
    private readonly IStartupManager _startupManager = new RegistryStartupManager();
    private readonly SemaphoreSlim _settingsSaveGate = new(1, 1);
    private SingleInstanceCoordinator? _singleInstance;
    private IWindowManager? _windowManager;
    private IHotkeyService? _hotkeyService;
    private ThemeService? _themeService;
    private MainWindow? _mainWindow;
    private TrayIconService? _trayIcon;
    private SafeIconHandle? _applicationIcon;
    private AppSettings _settings = new();
    private bool _isExiting;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        if (e.Args.Contains("--uninstall-cleanup", StringComparer.OrdinalIgnoreCase))
        {
            _startupManager.SetEnabled(enabled: false);
            SingleInstanceCoordinator.SignalExit();
            Thread.Sleep(700);
            Shutdown();
            return;
        }

        _singleInstance = new SingleInstanceCoordinator();
        if (!_singleInstance.TryBecomePrimary())
        {
            Shutdown();
            return;
        }

        _singleInstance.ActivationRequested += (_, _) => Dispatcher.BeginInvoke(ShowMainWindow);
        _singleInstance.ExitRequested += (_, _) => Dispatcher.BeginInvoke(RequestExit);
        _themeService = new ThemeService();
        _windowManager = new Win32WindowManager();
        _settings = await _settingsStore.LoadAsync();
        _settings.StartWithWindows = _startupManager.IsEnabled();

        _applicationIcon = AppIconService.LoadIcon();
        _mainWindow = new MainWindow(
            _windowManager,
            ShowSettingsWindow,
            _settings.PreferredViewMode,
            SaveViewPreferenceAsync)
        {
            Icon = AppIconService.ToImageSource(_applicationIcon),
        };
        MainWindow = _mainWindow;
        var handle = new WindowInteropHelper(_mainWindow).EnsureHandle();
        _themeService.ApplyTitleBar(_mainWindow);

        _hotkeyService = new HotkeyService(handle, ToggleLastEligibleWindow);
        var hotkeyError = string.Empty;
        if (_settings.Hotkey is not null)
        {
            _hotkeyService.TryChange(_settings.Hotkey, out hotkeyError);
        }

        CreateTrayIcon();
        if (e.Args.Contains("--background", StringComparer.OrdinalIgnoreCase))
        {
            _mainWindow.Hide();
        }
        else
        {
            ShowMainWindow();
        }

        if (!string.IsNullOrWhiteSpace(hotkeyError))
        {
            ShowTrayMessage("快捷键注册失败", hotkeyError, TrayNotificationKind.Warning);
        }
    }

    public async Task<SettingsApplyResult> ApplySettingsAsync(AppSettings proposed)
    {
        await _settingsSaveGate.WaitAsync();
        try
        {
            if (_hotkeyService is null)
            {
                return SettingsApplyResult.Failure("快捷键服务尚未就绪");
            }

            var previous = _settings.Clone();
            if (!_hotkeyService.TryChange(proposed.Hotkey, out var hotkeyError))
            {
                return SettingsApplyResult.Failure(hotkeyError);
            }

            var startupResult = _startupManager.SetEnabled(proposed.StartWithWindows);
            if (!startupResult.Succeeded)
            {
                _hotkeyService.TryChange(previous.Hotkey, out _);
                return SettingsApplyResult.Failure(startupResult.Message);
            }

            try
            {
                await _settingsStore.SaveAsync(proposed);
                _settings = proposed.Clone();
                return SettingsApplyResult.Success();
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                _startupManager.SetEnabled(previous.StartWithWindows);
                _hotkeyService.TryChange(previous.Hotkey, out _);
                return SettingsApplyResult.Failure($"设置保存失败：{exception.Message}");
            }
        }
        finally
        {
            _settingsSaveGate.Release();
        }
    }

    public AppSettings GetSettingsSnapshot() => _settings.Clone();

    public void RequestExit()
    {
        if (_isExiting)
        {
            return;
        }

        _isExiting = true;
        _windowManager?.RestoreWindowsPinnedByDeskPin();
        _mainWindow?.AllowClose();
        _trayIcon?.Dispose();
        _trayIcon = null;
        _hotkeyService?.Dispose();
        _windowManager?.Dispose();
        _themeService?.Dispose();
        _singleInstance?.Dispose();
        _applicationIcon?.Dispose();
        _mainWindow?.Close();
        Shutdown();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        if (!_isExiting)
        {
            _windowManager?.RestoreWindowsPinnedByDeskPin();
            _trayIcon?.Dispose();
            _hotkeyService?.Dispose();
            _windowManager?.Dispose();
            _themeService?.Dispose();
            _singleInstance?.Dispose();
            _applicationIcon?.Dispose();
        }

        base.OnExit(e);
    }

    private void CreateTrayIcon()
    {
        if (_mainWindow is null || _applicationIcon is null || _applicationIcon.IsInvalid)
        {
            return;
        }

        var handle = new WindowInteropHelper(_mainWindow).Handle;
        _trayIcon = new TrayIconService(
            handle,
            _applicationIcon.DangerousGetHandle(),
            () => Dispatcher.BeginInvoke(ShowMainWindow),
            () => Dispatcher.BeginInvoke(ToggleLastEligibleWindow),
            () => Dispatcher.BeginInvoke(ShowSettingsWindow),
            () => Dispatcher.BeginInvoke(RequestExit));
    }

    private void ShowMainWindow()
    {
        if (_mainWindow is null)
        {
            return;
        }

        _mainWindow.Show();
        if (_mainWindow.WindowState == WindowState.Minimized)
        {
            _mainWindow.WindowState = WindowState.Normal;
        }

        _mainWindow.Activate();
        _mainWindow.Topmost = true;
        _mainWindow.Topmost = false;
        _mainWindow.Focus();
    }

    private void ShowSettingsWindow()
    {
        if (_mainWindow is null)
        {
            return;
        }

        ShowMainWindow();
        var settingsWindow = new SettingsWindow(GetSettingsSnapshot(), ApplySettingsAsync)
        {
            Owner = _mainWindow,
            Icon = _mainWindow.Icon,
        };
        settingsWindow.SourceInitialized += (_, _) => _themeService?.ApplyTitleBar(settingsWindow);
        settingsWindow.ShowDialog();
    }

    private async Task<SettingsApplyResult> SaveViewPreferenceAsync(WindowViewMode viewMode)
    {
        await _settingsSaveGate.WaitAsync();
        try
        {
            var proposed = _settings.Clone();
            proposed.PreferredViewMode = viewMode;
            try
            {
                await _settingsStore.SaveAsync(proposed);
                _settings = proposed;
                return SettingsApplyResult.Success();
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                return SettingsApplyResult.Failure($"视图偏好保存失败：{exception.Message}");
            }
        }
        finally
        {
            _settingsSaveGate.Release();
        }
    }

    private void ToggleLastEligibleWindow()
    {
        if (_windowManager is null)
        {
            return;
        }

        var result = _windowManager.ToggleLastEligibleWindow();
        ShowTrayMessage(
            result.Succeeded ? "DeskPin" : "操作失败",
            result.Message,
            result.Succeeded ? TrayNotificationKind.Info : TrayNotificationKind.Warning);
        _ = _mainWindow?.RefreshAsync();
    }

    private void ShowTrayMessage(string title, string message, TrayNotificationKind icon)
    {
        _trayIcon?.ShowMessage(title, message, icon);
    }
}
