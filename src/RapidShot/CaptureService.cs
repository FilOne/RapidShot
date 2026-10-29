using System.Drawing.Imaging;

namespace RapidShot;

internal static class CaptureService
{
    public static Bitmap Capture(string? deviceName = null) => Capture(CaptureTargets.ResolveBounds(CaptureTargets.GetDisplays(), deviceName));

    public static Bitmap Capture(Rectangle bounds)
    {
        var image = new Bitmap(bounds.Width, bounds.Height, PixelFormat.Format32bppArgb);
        try
        {
            using var g = Graphics.FromImage(image);
            g.CopyFromScreen(bounds.Location, Point.Empty, bounds.Size, CopyPixelOperation.SourceCopy);
            return image;
        }
        catch { image.Dispose(); throw; }
    }
}
