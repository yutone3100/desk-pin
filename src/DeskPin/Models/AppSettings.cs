namespace DeskPin.Models;

public sealed class AppSettings
{
    public HotkeySetting? Hotkey { get; set; }
    public bool StartWithWindows { get; set; }
    public WindowViewMode PreferredViewMode { get; set; } = WindowViewMode.Cards;

    public AppSettings Clone() => new()
    {
        Hotkey = Hotkey is null ? null : new HotkeySetting
        {
            Modifiers = Hotkey.Modifiers,
            VirtualKey = Hotkey.VirtualKey,
            DisplayText = Hotkey.DisplayText,
        },
        StartWithWindows = StartWithWindows,
        PreferredViewMode = PreferredViewMode,
    };
}

public sealed class HotkeySetting
{
    public uint Modifiers { get; set; }
    public uint VirtualKey { get; set; }
    public string DisplayText { get; set; } = string.Empty;

    public bool IsValid => Modifiers != 0 && VirtualKey is > 0 and <= 0xFE;
}
