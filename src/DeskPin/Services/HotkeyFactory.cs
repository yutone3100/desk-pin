using System.Windows.Input;
using DeskPin.Models;

namespace DeskPin.Services;

internal static class HotkeyFactory
{
    internal static bool TryCreate(Key key, ModifierKeys modifiers, out HotkeySetting? setting, out string error)
    {
        setting = null;
        error = string.Empty;
        if (key is Key.LeftAlt or Key.RightAlt or Key.LeftCtrl or Key.RightCtrl or
            Key.LeftShift or Key.RightShift or Key.LWin or Key.RWin or Key.None or Key.System)
        {
            error = "请同时按下修饰键和一个普通按键";
            return false;
        }

        var nativeModifiers = ToNativeModifiers(modifiers);
        var virtualKey = KeyInterop.VirtualKeyFromKey(key);
        if (nativeModifiers == 0 || virtualKey <= 0)
        {
            error = "快捷键必须包含 Ctrl、Alt、Shift 或 Win 中的至少一个";
            return false;
        }

        setting = new HotkeySetting
        {
            Modifiers = nativeModifiers,
            VirtualKey = unchecked((uint)virtualKey),
            DisplayText = Format(key, modifiers),
        };
        return true;
    }

    internal static uint ToNativeModifiers(ModifierKeys modifiers)
    {
        uint result = 0;
        if (modifiers.HasFlag(ModifierKeys.Alt)) result |= 0x0001;
        if (modifiers.HasFlag(ModifierKeys.Control)) result |= 0x0002;
        if (modifiers.HasFlag(ModifierKeys.Shift)) result |= 0x0004;
        if (modifiers.HasFlag(ModifierKeys.Windows)) result |= 0x0008;
        return result;
    }

    private static string Format(Key key, ModifierKeys modifiers)
    {
        var parts = new List<string>(5);
        if (modifiers.HasFlag(ModifierKeys.Control)) parts.Add("Ctrl");
        if (modifiers.HasFlag(ModifierKeys.Alt)) parts.Add("Alt");
        if (modifiers.HasFlag(ModifierKeys.Shift)) parts.Add("Shift");
        if (modifiers.HasFlag(ModifierKeys.Windows)) parts.Add("Win");
        parts.Add(FormatKey(key));
        return string.Join(" + ", parts);
    }

    private static string FormatKey(Key key) => key switch
    {
        >= Key.D0 and <= Key.D9 => ((int)(key - Key.D0)).ToString(),
        Key.OemPlus => "+",
        Key.OemMinus => "-",
        Key.OemComma => ",",
        Key.OemPeriod => ".",
        _ => key.ToString(),
    };
}
