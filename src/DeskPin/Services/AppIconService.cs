using System.Windows.Media;

namespace DeskPin.Services;

internal static class AppIconService
{
    internal static SafeIconHandle LoadIcon()
    {
        var executablePath = Environment.ProcessPath;
        if (!string.IsNullOrWhiteSpace(executablePath) &&
            WindowIconService.TryExtractAssociatedIcon(executablePath, out var extractedIcon))
        {
            return extractedIcon;
        }

        var fallback = NativeMethods.LoadIcon(IntPtr.Zero, new IntPtr(NativeMethods.IdiApplication));
        return new SafeIconHandle(fallback, ownsHandle: false);
    }

    internal static ImageSource? ToImageSource(SafeIconHandle icon) =>
        icon.IsInvalid ? null : WindowIconService.CreateImage(icon.DangerousGetHandle());
}
