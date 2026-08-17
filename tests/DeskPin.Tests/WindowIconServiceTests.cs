using System.Drawing;
using DeskPin.Services;

namespace DeskPin.Tests;

public sealed class WindowIconServiceTests
{
    [Fact]
    public void LookupOrderPrefersLargeWindowAndClassIcons()
    {
        Assert.Equal([1, 2, 0], WindowIconService.WindowIconLookupOrder.ToArray());
        Assert.Equal([-14, -34], WindowIconService.ClassIconLookupOrder.ToArray());
    }

    [Fact]
    public void CreatesHighResolutionFrozenImageSource()
    {
        var source = WindowIconService.CreateImage(SystemIcons.Application.Handle);

        Assert.NotNull(source);
        Assert.Equal(48, source.Width);
        Assert.Equal(48, source.Height);
        Assert.True(source.IsFrozen);
    }
}
