using System.Windows;
using System.Windows.Input;
using DeskPin.Models;
using DeskPin.Services;

namespace DeskPin;

public partial class SettingsWindow : Window
{
    private readonly Func<AppSettings, Task<SettingsApplyResult>> _applySettings;
    private readonly WindowViewMode _preferredViewMode;
    private HotkeySetting? _pendingHotkey;

    public SettingsWindow(
        AppSettings settings,
        Func<AppSettings, Task<SettingsApplyResult>> applySettings)
    {
        InitializeComponent();
        _applySettings = applySettings;
        _preferredViewMode = settings.PreferredViewMode;
        _pendingHotkey = Clone(settings.Hotkey);
        StartWithWindowsCheckBox.IsChecked = settings.StartWithWindows;
        UpdateHotkeyDisplay();
    }

    private void HotkeyBox_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        e.Handled = true;
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        if (key == Key.Escape)
        {
            Keyboard.ClearFocus();
            HotkeyHint.Text = "已取消录入";
            HotkeyHint.Visibility = Visibility.Visible;
            return;
        }

        if (key is Key.Back or Key.Delete)
        {
            _pendingHotkey = null;
            UpdateHotkeyDisplay();
            return;
        }

        if (HotkeyFactory.TryCreate(key, Keyboard.Modifiers, out var setting, out var error))
        {
            _pendingHotkey = setting;
            ErrorText.Text = string.Empty;
            UpdateHotkeyDisplay();
        }
        else if (!string.IsNullOrWhiteSpace(error))
        {
            HotkeyHint.Text = error;
            HotkeyHint.Visibility = Visibility.Visible;
        }
    }

    private void ClearHotkey_Click(object sender, RoutedEventArgs e)
    {
        _pendingHotkey = null;
        ErrorText.Text = string.Empty;
        UpdateHotkeyDisplay();
    }

    private async void Save_Click(object sender, RoutedEventArgs e)
    {
        SaveButton.IsEnabled = false;
        ErrorText.Text = string.Empty;
        var proposed = new AppSettings
        {
            Hotkey = Clone(_pendingHotkey),
            StartWithWindows = StartWithWindowsCheckBox.IsChecked == true,
            PreferredViewMode = _preferredViewMode,
        };

        var result = await _applySettings(proposed);
        if (result.Succeeded)
        {
            DialogResult = true;
            Close();
            return;
        }

        ErrorText.Text = result.Message;
        SaveButton.IsEnabled = true;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    private void CloseWindow_Click(object sender, RoutedEventArgs e) => Close();

    private void UpdateHotkeyDisplay()
    {
        HotkeyBox.Text = _pendingHotkey?.DisplayText ?? "未设置";
        HotkeyHint.Text = _pendingHotkey is null ? "默认未设置快捷键" : string.Empty;
        HotkeyHint.Visibility = _pendingHotkey is null ? Visibility.Visible : Visibility.Collapsed;
    }

    private static HotkeySetting? Clone(HotkeySetting? setting) => setting is null ? null : new HotkeySetting
    {
        Modifiers = setting.Modifiers,
        VirtualKey = setting.VirtualKey,
        DisplayText = setting.DisplayText,
    };
}
