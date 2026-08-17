using Microsoft.Win32.SafeHandles;

namespace DeskPin.Services;

internal sealed class SafeIconHandle : SafeHandleZeroOrMinusOneIsInvalid
{
    internal SafeIconHandle(IntPtr handle, bool ownsHandle = true)
        : base(ownsHandle)
    {
        SetHandle(handle);
    }

    protected override bool ReleaseHandle() => NativeMethods.DestroyIcon(handle);
}
