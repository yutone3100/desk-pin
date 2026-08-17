using System.Windows.Input;
using DeskPin.Services;

namespace DeskPin.Tests;

public sealed class HotkeyFactoryTests
{
    [Fact]
    public void CreatesModifiedShortcut()
    {
        var created = HotkeyFactory.TryCreate(
            Key.T,
            ModifierKeys.Control | ModifierKeys.Alt,
            out var setting,
            out var error);

        Assert.True(created, error);
        Assert.NotNull(setting);
        Assert.Equal(0x0003u, setting.Modifiers);
        Assert.Equal("Ctrl + Alt + T", setting.DisplayText);
    }

    [Fact]
    public void RejectsUnmodifiedKey()
    {
        Assert.False(HotkeyFactory.TryCreate(Key.T, ModifierKeys.None, out var setting, out _));
        Assert.Null(setting);
    }

    [Fact]
    public void RejectsModifierOnlyKey()
    {
        Assert.False(HotkeyFactory.TryCreate(Key.LeftCtrl, ModifierKeys.Control, out _, out _));
    }
}
