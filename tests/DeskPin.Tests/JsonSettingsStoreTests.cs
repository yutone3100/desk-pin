using DeskPin.Models;
using DeskPin.Services;

namespace DeskPin.Tests;

public sealed class JsonSettingsStoreTests
{
    [Fact]
    public async Task RoundTripsSettings()
    {
        var directory = Path.Combine(Path.GetTempPath(), "DeskPin.Tests", Guid.NewGuid().ToString("N"));
        var path = Path.Combine(directory, "settings.json");
        try
        {
            var store = new JsonSettingsStore(path);
            var expected = new AppSettings
            {
                StartWithWindows = true,
                PreferredViewMode = WindowViewMode.List,
                Hotkey = new HotkeySetting
                {
                    Modifiers = 3,
                    VirtualKey = 84,
                    DisplayText = "Ctrl + Alt + T",
                },
            };

            await store.SaveAsync(expected);
            var actual = await store.LoadAsync();

            Assert.True(actual.StartWithWindows);
            Assert.Equal(WindowViewMode.List, actual.PreferredViewMode);
            Assert.NotNull(actual.Hotkey);
            Assert.Equal(expected.Hotkey.DisplayText, actual.Hotkey.DisplayText);
            Assert.Equal(expected.Hotkey.Modifiers, actual.Hotkey.Modifiers);
            Assert.Equal(expected.Hotkey.VirtualKey, actual.Hotkey.VirtualKey);
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }

    [Fact]
    public async Task InvalidJsonReturnsDefaults()
    {
        var directory = Path.Combine(Path.GetTempPath(), "DeskPin.Tests", Guid.NewGuid().ToString("N"));
        var path = Path.Combine(directory, "settings.json");
        Directory.CreateDirectory(directory);
        try
        {
            await File.WriteAllTextAsync(path, "{invalid");
            var settings = await new JsonSettingsStore(path).LoadAsync();
            Assert.False(settings.StartWithWindows);
            Assert.Null(settings.Hotkey);
            Assert.Equal(WindowViewMode.Cards, settings.PreferredViewMode);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task LegacyJsonWithoutViewModeDefaultsToCards()
    {
        var directory = Path.Combine(Path.GetTempPath(), "DeskPin.Tests", Guid.NewGuid().ToString("N"));
        var path = Path.Combine(directory, "settings.json");
        Directory.CreateDirectory(directory);
        try
        {
            await File.WriteAllTextAsync(path, "{\"StartWithWindows\":true}");

            var settings = await new JsonSettingsStore(path).LoadAsync();

            Assert.True(settings.StartWithWindows);
            Assert.Equal(WindowViewMode.Cards, settings.PreferredViewMode);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void ClonePreservesViewMode()
    {
        var clone = new AppSettings { PreferredViewMode = WindowViewMode.List }.Clone();

        Assert.Equal(WindowViewMode.List, clone.PreferredViewMode);
    }
}
