using System.Diagnostics;
using System.Drawing;
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

            using var icon = Icon.ExtractAssociatedIcon(path);
            return icon is null ? null : CreateImage(icon.Handle);
        }
        catch (Exception exception) when (
            exception is ArgumentException or InvalidOperationException or
            System.ComponentModel.Win32Exception or NotSupportedException or UnauthorizedAccessException)
        {
            return null;
        }
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
