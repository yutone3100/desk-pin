namespace DeskPin.Services;

public sealed class SingleInstanceCoordinator : IDisposable
{
    private const string MutexName = @"Local\DeskPin.Singleton.8F75AF1C";
    private const string ActivationEventName = @"Local\DeskPin.Activate.8F75AF1C";
    private const string ExitEventName = @"Local\DeskPin.Exit.8F75AF1C";
    private Mutex? _mutex;
    private EventWaitHandle? _activationEvent;
    private EventWaitHandle? _exitEvent;
    private CancellationTokenSource? _cancellation;
    private Task? _listenerTask;
    private bool _ownsMutex;

    public event EventHandler? ActivationRequested;
    public event EventHandler? ExitRequested;

    public bool TryBecomePrimary()
    {
        _mutex = new Mutex(initiallyOwned: true, MutexName, out var createdNew);
        if (!createdNew)
        {
            using var activationEvent = new EventWaitHandle(false, EventResetMode.AutoReset, ActivationEventName);
            activationEvent.Set();
            _mutex.Dispose();
            _mutex = null;
            return false;
        }

        _ownsMutex = true;
        _activationEvent = new EventWaitHandle(false, EventResetMode.AutoReset, ActivationEventName);
        _exitEvent = new EventWaitHandle(false, EventResetMode.AutoReset, ExitEventName);
        _cancellation = new CancellationTokenSource();
        _listenerTask = ListenAsync(_cancellation.Token);
        return true;
    }

    public static void SignalExit()
    {
        using var exitEvent = new EventWaitHandle(false, EventResetMode.AutoReset, ExitEventName);
        exitEvent.Set();
    }

    public void Dispose()
    {
        _cancellation?.Cancel();
        try
        {
            _listenerTask?.Wait(TimeSpan.FromSeconds(1));
        }
        catch (AggregateException)
        {
            // Cancellation can race with application shutdown.
        }

        _activationEvent?.Dispose();
        _exitEvent?.Dispose();
        _cancellation?.Dispose();
        if (_ownsMutex)
        {
            _mutex?.ReleaseMutex();
        }

        _mutex?.Dispose();
        _ownsMutex = false;
    }

    private async Task ListenAsync(CancellationToken cancellationToken)
    {
        await Task.Run(() =>
        {
            var activationEvent = _activationEvent
                ?? throw new InvalidOperationException("激活事件尚未初始化");
            var exitEvent = _exitEvent
                ?? throw new InvalidOperationException("退出事件尚未初始化");
            var waitHandles = new WaitHandle[] { activationEvent, exitEvent, cancellationToken.WaitHandle };
            while (true)
            {
                switch (WaitHandle.WaitAny(waitHandles))
                {
                    case 0:
                        ActivationRequested?.Invoke(this, EventArgs.Empty);
                        break;
                    case 1:
                        ExitRequested?.Invoke(this, EventArgs.Empty);
                        break;
                    default:
                        return;
                }
            }
        }).ConfigureAwait(false);
    }
}
