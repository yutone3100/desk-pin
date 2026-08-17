using System.Windows.Media;
using DeskPin.Services;

namespace DeskPin.Tests;

public sealed class WindowIconCacheTests
{
    [Fact]
    public void ReusesIconsAndPrunesClosedOrReusedWindows()
    {
        var factoryCalls = 0;
        var cache = new WindowIconCache((_, _) =>
        {
            factoryCalls++;
            var image = new DrawingImage();
            image.Freeze();
            return image;
        });

        var first = cache.GetOrCreate(new IntPtr(100), 10);
        var repeated = cache.GetOrCreate(new IntPtr(100), 10);
        var reusedHandle = cache.GetOrCreate(new IntPtr(100), 11);

        Assert.Same(first, repeated);
        Assert.NotSame(first, reusedHandle);
        Assert.True(first!.IsFrozen);
        Assert.Equal(2, factoryCalls);
        Assert.Equal(2, cache.Count);

        cache.RetainOnly(new HashSet<WindowIconCacheKey> { new(100, 11) });
        Assert.Equal(1, cache.Count);
        cache.Clear();
        Assert.Equal(0, cache.Count);
    }

    [Fact]
    public void DoesNotRetainMissingIcons()
    {
        var factoryCalls = 0;
        var cache = new WindowIconCache((_, _) =>
        {
            factoryCalls++;
            return null;
        });

        Assert.Null(cache.GetOrCreate(new IntPtr(1), 2));
        Assert.Null(cache.GetOrCreate(new IntPtr(1), 2));
        Assert.Equal(2, factoryCalls);
        Assert.Equal(0, cache.Count);
    }
}
