namespace DeskPin.Services;

internal static class ProcessElevationService
{
    internal static bool RequiresElevation(int targetProcessId) =>
        TryIsElevated(Environment.ProcessId, out var currentIsElevated) &&
        TryIsElevated(targetProcessId, out var targetIsElevated) &&
        !currentIsElevated &&
        targetIsElevated;

    internal static bool IsCurrentProcessElevated() =>
        TryIsElevated(Environment.ProcessId, out var isElevated) && isElevated;

    internal static bool TryIsElevated(int processId, out bool isElevated)
    {
        isElevated = false;
        var processHandle = NativeMethods.OpenProcess(
            NativeMethods.ProcessQueryLimitedInformation,
            inheritHandle: false,
            processId);
        if (processHandle == IntPtr.Zero)
        {
            return false;
        }

        try
        {
            if (!NativeMethods.OpenProcessToken(
                    processHandle,
                    NativeMethods.TokenQuery,
                    out var tokenHandle))
            {
                return false;
            }

            try
            {
                if (!NativeMethods.GetTokenInformation(
                        tokenHandle,
                        NativeMethods.TokenElevationInformation,
                        out var elevation,
                        System.Runtime.InteropServices.Marshal.SizeOf<NativeMethods.TokenElevation>(),
                        out _))
                {
                    return false;
                }

                isElevated = elevation.IsElevated != 0;
                return true;
            }
            finally
            {
                NativeMethods.CloseHandle(tokenHandle);
            }
        }
        finally
        {
            NativeMethods.CloseHandle(processHandle);
        }
    }
}
