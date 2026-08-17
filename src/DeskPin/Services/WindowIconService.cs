using System.Diagnostics;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace DeskPin.Services;

internal static class WindowIconService
{
    private const uint WmGetIcon = 0x007F;
    private const int IconBig = 1;
    private const int IconSmall2 = 2;
    private const int IconSmall = 0;
    private const int GclpHIcon = -14;
    private const int GclpHIconSm = -34;
    private const int RenderSize = 48;

    internal static ReadOnlySpan<int> WindowIconLookupOrder => [IconBig, IconSmall2, IconSmall];
    internal static ReadOnlySpan<int> ClassIconLookupOrder => [GclpHIcon, GclpHIconSm];

    internal static ImageSource? TryGetIcon(IntPtr hWnd, int processId)
    {
        var handle = IntPtr.Zero;
        foreach (var iconType in WindowIconLookupOrder)
        {
            handle = NativeMethods.SendMessage(hWnd, WmGetIcon, new IntPtr(iconType), IntPtr.Zero);
            if (handle != IntPtr.Zero)
            {
                break;
            }
        }

        if (handle == IntPtr.Zero)
        {
            foreach (var classIndex in ClassIconLookupOrder)
            {
                handle = NativeMethods.GetClassLongPtr(hWnd, classIndex);
                if (handle != IntPtr.Zero)
                {
                    break;
                }
            }
        }

        if (handle != IntPtr.Zero)
        {
            return CreateImage(handle);
        }

        try
        {
            using var process = Process.GetProcessById(processId);
            var path = process.MainModule?.FileName;
            if (string.IsNullOrWhiteSpace(path))
            {
                return null;
            }

            if (!TryExtractAssociatedIcon(path, out var icon))
            {
                return null;
            }

            using (icon)
            {
                return CreateImage(icon.DangerousGetHandle());
            }
        }
        catch (Exception exception) when (
            exception is ArgumentException or InvalidOperationException or
            System.ComponentModel.Win32Exception or NotSupportedException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    internal static bool TryExtractAssociatedIcon(string path, out SafeIconHandle icon)
    {
        icon = new SafeIconHandle(IntPtr.Zero);
        var extracted = NativeMethods.ExtractIconEx(
            path,
            0,
            out var largeIcon,
            out var smallIcon,
            1);
        if (extracted == 0 || (largeIcon == IntPtr.Zero && smallIcon == IntPtr.Zero))
        {
            if (largeIcon != IntPtr.Zero)
            {
                NativeMethods.DestroyIcon(largeIcon);
            }

            if (smallIcon != IntPtr.Zero)
            {
                NativeMethods.DestroyIcon(smallIcon);
            }

            return false;
        }

        var selectedIcon = largeIcon != IntPtr.Zero ? largeIcon : smallIcon;
        var unusedIcon = selectedIcon == largeIcon ? smallIcon : largeIcon;
        if (unusedIcon != IntPtr.Zero)
        {
            NativeMethods.DestroyIcon(unusedIcon);
        }

        icon.Dispose();
        icon = new SafeIconHandle(selectedIcon);
        return true;
    }

    internal static ImageSource? CreateImage(IntPtr iconHandle)
    {
        try
        {
            var source = Imaging.CreateBitmapSourceFromHIcon(
                iconHandle,
                Int32Rect.Empty,
                BitmapSizeOptions.FromWidthAndHeight(RenderSize, RenderSize));
            source.Freeze();
            return source;
        }
        catch
        {
            return null;
        }
    }
}
