using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media.Imaging;
using DeskPin.Services;

namespace DeskPin.Utilities;

internal static class AppIconFactory
{
    internal static Icon CreateIcon()
    {
        using var bitmap = new Bitmap(64, 64);
        using (var graphics = Graphics.FromImage(bitmap))
        {
            graphics.SmoothingMode = SmoothingMode.AntiAlias;
            graphics.Clear(System.Drawing.Color.Transparent);
            using var background = new SolidBrush(System.Drawing.Color.FromArgb(45, 108, 223));
            graphics.FillEllipse(background, 3, 3, 58, 58);
            using var pen = new System.Drawing.Pen(System.Drawing.Color.White, 5)
            {
                StartCap = LineCap.Round,
                EndCap = LineCap.Round,
            };
            graphics.DrawLine(pen, 22, 18, 45, 41);
            graphics.DrawLine(pen, 39, 15, 19, 35);
            graphics.DrawLine(pen, 18, 46, 31, 33);
        }

        var iconHandle = bitmap.GetHicon();
        try
        {
            using var temporary = Icon.FromHandle(iconHandle);
            return (Icon)temporary.Clone();
        }
        finally
        {
            NativeMethods.DestroyIcon(iconHandle);
        }
    }

    internal static System.Windows.Media.ImageSource ToImageSource(Icon icon)
    {
        var source = Imaging.CreateBitmapSourceFromHIcon(
            icon.Handle,
            Int32Rect.Empty,
            BitmapSizeOptions.FromEmptyOptions());
        source.Freeze();
        return source;
    }
}
