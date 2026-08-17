using DeskPin.Models;

namespace DeskPin.Services;

public interface IWindowManager : IDisposable
{
    long? LastEligibleWindowId { get; }
    IReadOnlyList<DesktopWindow> GetWindows();
    WindowOperationResult ToggleTopmost(long windowId);
    WindowOperationResult ToggleLastEligibleWindow();
    WindowActionResult ShowWindow(long windowId);
    WindowActionResult CloseWindow(long windowId);
    int RestoreWindowsPinnedByDeskPin();
}
