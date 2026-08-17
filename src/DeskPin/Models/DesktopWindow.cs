using System.Windows.Media;

namespace DeskPin.Models;

public sealed record DesktopWindow(
    long Id,
    string Title,
    string ProcessName,
    int ProcessId,
    bool IsTopmost,
    ImageSource? Icon)
{
    public string ActionLabel => IsTopmost ? "取消置顶" : "置顶";
    public string StatusLabel => IsTopmost ? "已置顶" : "普通";
    public string AccessibilityLabel => $"{ProcessName}，{Title}，{StatusLabel}";
}
