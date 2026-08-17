namespace DeskPin.Services;

internal static class WindowEligibility
{
    private static readonly HashSet<string> ExcludedClasses = new(StringComparer.OrdinalIgnoreCase)
    {
        "Progman",
        "WorkerW",
        "Shell_TrayWnd",
        "Shell_SecondaryTrayWnd",
        "DV2ControlHost",
    };

    internal static bool ShouldInclude(
        bool visible,
        bool cloaked,
        string title,
        string className,
        long extendedStyle,
        int processId,
        int ownProcessId)
    {
        if (!visible || cloaked || processId == ownProcessId || string.IsNullOrWhiteSpace(title))
        {
            return false;
        }

        if (ExcludedClasses.Contains(className))
        {
            return false;
        }

        var isToolWindow = (extendedStyle & NativeMethods.WsExToolWindow) != 0;
        var isAppWindow = (extendedStyle & NativeMethods.WsExAppWindow) != 0;
        return !isToolWindow || isAppWindow;
    }
}
