using System.Drawing.Drawing2D;
using System.Drawing.Imaging;

namespace RapidShot;

internal static class MagnifierGeometry
{
    internal const float MinZoom = 1, MaxZoom = 8;
    internal static RectangleF MoveInside(RectangleF area, Rectangle crop)
    {
        var w = Math.Min(area.Width, crop.Width); var h = Math.Min(area.Height, crop.Height);
        return new(Math.Clamp(area.X, crop.Left, crop.Right - w), Math.Clamp(area.Y, crop.Top, crop.Bottom - h), w, h);
    }
    internal static PointF Center(RectangleF area) => new(area.X + area.Width / 2, area.Y + area.Height / 2);
    internal static RectangleF MoveDestination(RectangleF area, Rectangle crop)
    {
        // A previously placed patch may be larger than a later crop. Keep its scale
        // and aspect ratio; clipping to the screenshot is handled by the renderer.
        var x = Math.Clamp(area.X, Math.Min(crop.Left, crop.Right - area.Width), Math.Max(crop.Left, crop.Right - area.Width));
        var y = Math.Clamp(area.Y, Math.Min(crop.Top, crop.Bottom - area.Height), Math.Max(crop.Top, crop.Bottom - area.Height));
        return new(x, y, area.Width, area.Height);
    }
    internal static void SetZoom(Annotation item, float value, Rectangle crop, PointF? center = null)
    {
        var source = item.SourceBounds;
        if (source.Width < 1 || source.Height < 1) return;
        item.Magnification = Math.Clamp(value, MinZoom, Math.Min(MaxZoom, Math.Max(MinZoom, Math.Min(crop.Width / source.Width, crop.Height / source.Height))));
        var p = center ?? Center(item.Bounds);
        var w = source.Width * item.Magnification; var h = source.Height * item.Magnification;
        var target = MoveDestination(new(p.X - w / 2, p.Y - h / 2, w, h), crop);
        item.Start = target.Location; item.End = new(target.Right, target.Bottom);
    }
    internal static void SetSource(Annotation item, RectangleF source, Rectangle crop)
    {
        item.SourceBounds = MoveInside(source, crop); SetZoom(item, item.Magnification, crop);
    }
    internal static Bitmap Sample(Bitmap image, RectangleF source)
    {
        var patch = new Bitmap(Math.Max(1, (int)Math.Ceiling(source.Width)), Math.Max(1, (int)Math.Ceiling(source.Height)), PixelFormat.Format32bppArgb);
        using var g = Graphics.FromImage(patch);
        g.InterpolationMode = InterpolationMode.HighQualityBicubic; g.PixelOffsetMode = PixelOffsetMode.HighQuality;
        using var attributes = new ImageAttributes(); attributes.SetWrapMode(WrapMode.TileFlipXY);
        g.DrawImage(image, new Rectangle(Point.Empty, patch.Size), source.X, source.Y, source.Width, source.Height, GraphicsUnit.Pixel, attributes);
        return patch;
    }
    internal static void DrawPatch(Graphics g, Bitmap patch, RectangleF target, Annotation item, float outlineScale = 1)
    {
        if (target.Width < 1 || target.Height < 1) return;
        var state = g.Save();
        g.InterpolationMode = InterpolationMode.HighQualityBicubic; g.PixelOffsetMode = PixelOffsetMode.HighQuality;
        using var attributes = new ImageAttributes(); attributes.SetWrapMode(WrapMode.TileFlipXY);
        PointF[] corners = [target.Location, new(target.Right, target.Top), new(target.Left, target.Bottom)];
        g.DrawImage(patch, corners, new RectangleF(PointF.Empty, patch.Size), GraphicsUnit.Pixel, attributes);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        // Keep the entire stroke outside the patch, so increasing its width never covers image pixels.
        var width = Math.Max(1, item.Width * outlineScale);
        using var border = new Pen(item.Color, width);
        g.SetClip(target, CombineMode.Exclude);
        g.DrawRectangle(border, target.X - width / 2, target.Y - width / 2, target.Width + width, target.Height + width);
        g.Restore(state);
    }
    internal static void Draw(Bitmap result, Annotation item, RectangleF crop, Bitmap? sourceOverride = null)
    {
        var sx = result.Width / (float)crop.Width; var sy = result.Height / (float)crop.Height;
        var source = item.SourceBounds;
        if (source.Width < 1 || source.Height < 1) return;
        var sample = new RectangleF((source.X - crop.X) * sx, (source.Y - crop.Y) * sy, source.Width * sx, source.Height * sy);
        // Copy before drawing: overlapping source/destination areas never feed back into themselves.
        using var sampled = sourceOverride == null ? Sample(result, sample) : null;
        var patch = sourceOverride ?? sampled!;
        using var g = Graphics.FromImage(result);
        var b = item.Bounds;
        DrawPatch(g, patch, new((b.X - crop.X) * sx, (b.Y - crop.Y) * sy, b.Width * sx, b.Height * sy), item, Math.Min(sx, sy));
    }
}
