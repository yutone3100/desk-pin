using System.Windows.Media;

namespace DeskPin.Services;

internal readonly record struct WindowIconCacheKey(long WindowId, int ProcessId);

internal sealed class WindowIconCache
{
    private readonly Func<IntPtr, int, ImageSource?> _factory;
    private readonly Dictionary<WindowIconCacheKey, ImageSource> _icons = [];
    private readonly object _sync = new();

    internal WindowIconCache(Func<IntPtr, int, ImageSource?> factory)
    {
        _factory = factory;
    }

    internal int Count
    {
        get
        {
            lock (_sync)
            {
                return _icons.Count;
            }
        }
    }

    internal ImageSource? GetOrCreate(IntPtr windowHandle, int processId)
    {
        var key = new WindowIconCacheKey(windowHandle.ToInt64(), processId);
        lock (_sync)
        {
            if (_icons.TryGetValue(key, out var cached))
            {
                return cached;
            }

            var created = _factory(windowHandle, processId);
            if (created is not null)
            {
                _icons.Add(key, created);
            }

            return created;
        }
    }

    internal void RetainOnly(IReadOnlySet<WindowIconCacheKey> activeKeys)
    {
        lock (_sync)
        {
            List<WindowIconCacheKey>? expiredKeys = null;
            foreach (var key in _icons.Keys)
            {
                if (!activeKeys.Contains(key))
                {
                    (expiredKeys ??= []).Add(key);
                }
            }

            if (expiredKeys is null)
            {
                return;
            }

            foreach (var key in expiredKeys)
            {
                _icons.Remove(key);
            }
        }
    }

    internal void Clear()
    {
        lock (_sync)
        {
            _icons.Clear();
        }
    }
}
