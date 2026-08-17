using System.Runtime.InteropServices;

namespace DeskPin.Services;

internal static class WindowMaximizeHelper
{
    internal static bool TryApplyWorkArea(IntPtr windowHandle, IntPtr minMaxInfoPointer)
    {
        var monitor = NativeMethods.MonitorFromWindow(windowHandle, NativeMethods.MonitorDefaultToNearest);
        if (monitor == IntPtr.Zero)
        {
            return false;
        }

        var monitorInfo = new NativeMethods.MonitorInfo
        {
            Size = Marshal.SizeOf<NativeMethods.MonitorInfo>(),
        };
        if (!NativeMethods.GetMonitorInfo(monitor, ref monitorInfo))
        {
            return false;
        }

        var minMaxInfo = Marshal.PtrToStructure<NativeMethods.MinMaxInfo>(minMaxInfoPointer);
        ApplyWorkArea(ref minMaxInfo, monitorInfo.Monitor, monitorInfo.WorkArea);
        Marshal.StructureToPtr(minMaxInfo, minMaxInfoPointer, false);
        return true;
    }

    internal static void ApplyWorkArea(
        ref NativeMethods.MinMaxInfo minMaxInfo,
        NativeMethods.Rect monitor,
        NativeMethods.Rect workArea)
    {
        minMaxInfo.MaxPosition.X = workArea.Left - monitor.Left;
        minMaxInfo.MaxPosition.Y = workArea.Top - monitor.Top;
        minMaxInfo.MaxSize.X = workArea.Right - workArea.Left;
        minMaxInfo.MaxSize.Y = workArea.Bottom - workArea.Top;
    }
}
