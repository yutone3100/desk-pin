using System.Diagnostics;
using System.Runtime;

namespace DeskPin.Services;

internal sealed class IdleMemoryReclaimer : IDisposable
{
    internal static readonly TimeSpan DefaultDelay = TimeSpan.FromSeconds(15);

    private readonly TimeSpan _delay;
    private readonly Action _reclaim;
    private readonly object _sync = new();
    private CancellationTokenSource? _cancellation;
    private bool _disposed;

    internal IdleMemoryReclaimer()
        : this(DefaultDelay, ReclaimWorkingSet)
    {
    }

    internal IdleMemoryReclaimer(TimeSpan delay, Action reclaim)
    {
        _delay = delay;
        _reclaim = reclaim;
    }

    internal bool IsScheduled
    {
        get
        {
            lock (_sync)
            {
                return _cancellation is not null;
            }
        }
    }

    internal void Schedule()
    {
        CancellationTokenSource cancellation;
        lock (_sync)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            _cancellation?.Cancel();
            _cancellation?.Dispose();
            cancellation = new CancellationTokenSource();
            _cancellation = cancellation;
        }

        _ = ReclaimAfterDelayAsync(cancellation);
    }

    internal void Cancel()
    {
        lock (_sync)
        {
            _cancellation?.Cancel();
            _cancellation?.Dispose();
            _cancellation = null;
        }
    }

    public void Dispose()
    {
        lock (_sync)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _cancellation?.Cancel();
            _cancellation?.Dispose();
            _cancellation = null;
        }
    }

    private async Task ReclaimAfterDelayAsync(CancellationTokenSource cancellation)
    {
        try
        {
            await Task.Delay(_delay, cancellation.Token).ConfigureAwait(false);
            await Task.Run(_reclaim, cancellation.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // Showing the main window cancels the pending idle reclamation.
        }
        catch (Exception exception)
        {
            Debug.WriteLine($"DeskPin idle memory reclamation failed: {exception}");
        }
        finally
        {
            lock (_sync)
            {
                if (ReferenceEquals(_cancellation, cancellation))
                {
                    _cancellation.Dispose();
                    _cancellation = null;
                }
            }
        }
    }

    private static void ReclaimWorkingSet()
    {
        try
        {
            GCSettings.LargeObjectHeapCompactionMode = GCLargeObjectHeapCompactionMode.CompactOnce;
            GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: true, compacting: true);
        }
        catch (Exception exception)
        {
            Debug.WriteLine($"DeskPin compacting collection failed: {exception}");
        }

        if (!NativeMethods.K32EmptyWorkingSet(NativeMethods.GetCurrentProcess()))
        {
            Debug.WriteLine(
                $"DeskPin working set reclamation failed with Win32 error {System.Runtime.InteropServices.Marshal.GetLastPInvokeError()}.");
        }
    }
}
