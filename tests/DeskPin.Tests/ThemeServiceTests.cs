using System.Windows;
using System.Windows.Media;
using DeskPin.Services;
using MediaColor = System.Windows.Media.Color;
using WpfSystemColors = System.Windows.SystemColors;

namespace DeskPin.Tests;

public sealed class ThemeServiceTests
{
    [Theory]
    [InlineData(false, false, 255, 255, 255, 23, 33, 60)]
    [InlineData(false, true, 21, 26, 42, 247, 249, 255)]
    public void ContextMenuPaletteFollowsLightAndDarkThemes(
        bool highContrast,
        bool isDark,
        byte backgroundRed,
        byte backgroundGreen,
        byte backgroundBlue,
        byte textRed,
        byte textGreen,
        byte textBlue)
    {
        var resources = new ResourceDictionary();

        ThemeService.ApplyPalette(resources, highContrast, isDark);

        Assert.Equal(
            MediaColor.FromRgb(backgroundRed, backgroundGreen, backgroundBlue),
            GetColor(resources, "ContextMenuBackgroundBrush"));
        Assert.Equal(
            MediaColor.FromRgb(textRed, textGreen, textBlue),
            GetColor(resources, "ContextMenuTextBrush"));
    }

    [Fact]
    public void ContextMenuPaletteUsesSystemColorsInHighContrastMode()
    {
        var resources = new ResourceDictionary();

        ThemeService.ApplyPalette(resources, highContrast: true, isDark: false);

        Assert.Equal(WpfSystemColors.MenuColor, GetColor(resources, "ContextMenuBackgroundBrush"));
        Assert.Equal(WpfSystemColors.MenuTextColor, GetColor(resources, "ContextMenuTextBrush"));
        Assert.Equal(WpfSystemColors.HighlightColor, GetColor(resources, "ContextMenuHoverBrush"));
    }

    private static MediaColor GetColor(ResourceDictionary resources, string key) =>
        Assert.IsType<SolidColorBrush>(resources[key]).Color;
}
