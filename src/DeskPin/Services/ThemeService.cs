using Microsoft.Win32;
using System.ComponentModel;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using MediaColor = System.Windows.Media.Color;
using WpfSystemColors = System.Windows.SystemColors;

namespace DeskPin.Services;

public sealed class ThemeService : IDisposable
{
    private const string PersonalizeKey = @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";
    private const uint DwmwaUseImmersiveDarkMode = 20;
    private bool _disposed;

    public ThemeService()
    {
        SystemEvents.UserPreferenceChanged += OnUserPreferenceChanged;
        SystemParameters.StaticPropertyChanged += OnSystemParametersChanged;
        Apply();
    }

    public void Apply()
    {
        var application = System.Windows.Application.Current;
        if (application is null)
        {
            return;
        }

        if (!application.Dispatcher.CheckAccess())
        {
            application.Dispatcher.Invoke(Apply);
            return;
        }

        var highContrast = SystemParameters.HighContrast;
        var isDark = !highContrast && IsDarkMode();
        var resources = application.Resources;

        ApplyPalette(resources, highContrast, isDark);

        foreach (Window window in application.Windows)
        {
            ApplyTitleBar(window, isDark);
            WindowShadowService.Refresh(window);
        }
    }

    internal static void ApplyPalette(ResourceDictionary resources, bool highContrast, bool isDark)
    {
        ArgumentNullException.ThrowIfNull(resources);

        SetBrush(resources, "WindowBackgroundBrush", highContrast ? WpfSystemColors.WindowColor : isDark ? MediaColor.FromRgb(16, 18, 24) : MediaColor.FromRgb(238, 242, 247));
        SetBrush(resources, "ContentBackgroundBrush", highContrast ? WpfSystemColors.WindowColor : isDark ? MediaColor.FromRgb(22, 25, 33) : MediaColor.FromRgb(247, 249, 252));
        SetBrush(resources, "SidebarBrush", highContrast ? WpfSystemColors.WindowColor : isDark ? MediaColor.FromRgb(12, 16, 31) : MediaColor.FromRgb(21, 30, 63));
        SetBrush(resources, "SidebarHoverBrush", highContrast ? WpfSystemColors.HighlightColor : isDark ? MediaColor.FromRgb(31, 39, 65) : MediaColor.FromRgb(32, 43, 82));
        SetBrush(resources, "SidebarActiveBrush", highContrast ? WpfSystemColors.HighlightColor : isDark ? MediaColor.FromRgb(38, 48, 78) : MediaColor.FromRgb(38, 52, 95));
        SetBrush(resources, "SidebarTextBrush", highContrast ? WpfSystemColors.WindowTextColor : MediaColor.FromRgb(247, 249, 255));
        SetBrush(resources, "SidebarMutedBrush", highContrast ? WpfSystemColors.GrayTextColor : MediaColor.FromRgb(152, 165, 199));
        SetBrush(resources, "CardBrush", highContrast ? WpfSystemColors.ControlColor : isDark ? MediaColor.FromRgb(30, 34, 44) : System.Windows.Media.Colors.White);
        SetBrush(resources, "CardHoverBrush", highContrast ? WpfSystemColors.HighlightColor : isDark ? MediaColor.FromRgb(36, 42, 55) : MediaColor.FromRgb(248, 250, 255));
        SetBrush(resources, "InputBrush", highContrast ? WpfSystemColors.WindowColor : isDark ? MediaColor.FromRgb(25, 29, 38) : System.Windows.Media.Colors.White);
        SetBrush(resources, "TextPrimaryBrush", highContrast ? WpfSystemColors.WindowTextColor : isDark ? MediaColor.FromRgb(242, 245, 251) : MediaColor.FromRgb(23, 33, 60));
        SetBrush(resources, "TextSecondaryBrush", highContrast ? WpfSystemColors.GrayTextColor : isDark ? MediaColor.FromRgb(174, 183, 201) : MediaColor.FromRgb(104, 115, 138));
        SetBrush(resources, "TextTertiaryBrush", highContrast ? WpfSystemColors.GrayTextColor : isDark ? MediaColor.FromRgb(127, 139, 161) : MediaColor.FromRgb(152, 162, 181));
        SetBrush(resources, "BorderBrush", highContrast ? WpfSystemColors.WindowTextColor : isDark ? MediaColor.FromRgb(53, 60, 75) : MediaColor.FromRgb(223, 229, 239));
        SetBrush(resources, "AccentBrush", highContrast ? WpfSystemColors.HighlightColor : MediaColor.FromRgb(53, 109, 243));
        SetBrush(resources, "AccentHoverBrush", highContrast ? WpfSystemColors.HotTrackColor : MediaColor.FromRgb(40, 95, 217));
        SetBrush(resources, "AccentSoftBrush", highContrast ? WpfSystemColors.HighlightColor : isDark ? MediaColor.FromRgb(35, 52, 88) : MediaColor.FromRgb(237, 243, 255));
        SetBrush(resources, "SuccessBrush", highContrast ? WpfSystemColors.HighlightColor : isDark ? MediaColor.FromRgb(77, 199, 151) : MediaColor.FromRgb(22, 132, 91));
        SetBrush(resources, "SuccessSoftBrush", highContrast ? WpfSystemColors.HighlightColor : isDark ? MediaColor.FromRgb(28, 70, 56) : MediaColor.FromRgb(234, 248, 242));
        SetBrush(resources, "DangerBrush", highContrast ? WpfSystemColors.HotTrackColor : isDark ? MediaColor.FromRgb(255, 125, 125) : MediaColor.FromRgb(209, 67, 67));
        SetBrush(resources, "DangerSoftBrush", highContrast ? WpfSystemColors.HighlightColor : isDark ? MediaColor.FromRgb(78, 38, 42) : MediaColor.FromRgb(255, 240, 240));
        SetBrush(resources, "ChromeButtonHoverBrush", highContrast ? WpfSystemColors.HighlightColor : isDark ? MediaColor.FromRgb(44, 49, 62) : MediaColor.FromRgb(233, 237, 245));
        SetBrush(resources, "SelectionBrush", highContrast ? WpfSystemColors.HighlightColor : isDark ? MediaColor.FromRgb(45, 68, 112) : MediaColor.FromRgb(221, 232, 255));
        SetBrush(resources, "ContextMenuBackgroundBrush", highContrast ? WpfSystemColors.MenuColor : isDark ? MediaColor.FromRgb(21, 26, 42) : System.Windows.Media.Colors.White);
        SetBrush(resources, "ContextMenuTextBrush", highContrast ? WpfSystemColors.MenuTextColor : isDark ? MediaColor.FromRgb(247, 249, 255) : MediaColor.FromRgb(23, 33, 60));
        SetBrush(resources, "ContextMenuHoverBrush", highContrast ? WpfSystemColors.HighlightColor : isDark ? MediaColor.FromRgb(36, 45, 72) : MediaColor.FromRgb(237, 243, 255));
        SetBrush(resources, "ContextMenuSeparatorBrush", highContrast ? WpfSystemColors.MenuTextColor : isDark ? MediaColor.FromRgb(61, 70, 91) : MediaColor.FromRgb(223, 229, 239));
    }

    public void ApplyTitleBar(Window window, bool? darkOverride = null)
    {
        var handle = new WindowInteropHelper(window).Handle;
        if (handle == IntPtr.Zero)
        {
            return;
        }

        var useDark = darkOverride ?? (!SystemParameters.HighContrast && IsDarkMode());
        var value = useDark ? 1 : 0;
        NativeMethods.DwmSetWindowAttribute(handle, DwmwaUseImmersiveDarkMode, ref value, sizeof(int));
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        SystemEvents.UserPreferenceChanged -= OnUserPreferenceChanged;
        SystemParameters.StaticPropertyChanged -= OnSystemParametersChanged;
        _disposed = true;
    }

    private static bool IsDarkMode()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(PersonalizeKey);
            return key?.GetValue("AppsUseLightTheme") is int value && value == 0;
        }
        catch
        {
            return false;
        }
    }

    private static void SetBrush(ResourceDictionary resources, string key, MediaColor color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        resources[key] = brush;
    }

    private void OnUserPreferenceChanged(object sender, UserPreferenceChangedEventArgs e) => Apply();
    private void OnSystemParametersChanged(object? sender, PropertyChangedEventArgs e) => Apply();
}
