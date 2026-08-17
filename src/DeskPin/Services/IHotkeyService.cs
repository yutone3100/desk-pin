using DeskPin.Models;

namespace DeskPin.Services;

public interface IHotkeyService : IDisposable
{
    HotkeySetting? Current { get; }
    bool TryChange(HotkeySetting? setting, out string errorMessage);
}
