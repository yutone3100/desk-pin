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

    [Fact]
    public void ExtractedApplicationIconIsOwnedAndConvertible()
    {
        var appHost = Path.Combine(AppContext.BaseDirectory, "DeskPin.exe");
        Assert.True(File.Exists(appHost));
        Assert.True(WindowIconService.TryExtractAssociatedIcon(appHost, out var icon));

        using (icon)
        {
            Assert.False(icon.IsInvalid);
            Assert.NotNull(WindowIconService.CreateImage(icon.DangerousGetHandle()));
        }
    }
}
