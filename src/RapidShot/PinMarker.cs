using System.Drawing.Drawing2D;
using System.Globalization;
using System.Numerics;

namespace RapidShot;

internal static class PinMarker
{
    public static Annotation Create(BigInteger number, PointF point, Color color, float size, Rectangle crop)
    {
        var item = new Annotation { Tool = EditorTool.Pin, PinNumber = number, Color = color, PinSize = size };
        var dimensions = Measure(number, size);
        var x = Math.Clamp(point.X - dimensions.Width / 2, crop.Left, Math.Max(crop.Left, crop.Right - dimensions.Width));
        var y = Math.Clamp(point.Y - dimensions.Height / 2, crop.Top, Math.Max(crop.Top, crop.Bottom - dimensions.Height));
        item.Start = new(x, y); item.End = new(x + dimensions.Width, y + dimensions.Height); return item;
    }
    private static string Label(BigInteger number) => number.ToString(CultureInfo.InvariantCulture);
    public static SizeF Measure(BigInteger number, float size)
    {
        using var bitmap = new Bitmap(1, 1); using var g = Graphics.FromImage(bitmap);
        using var font = new Font("Segoe UI", size * .54f, FontStyle.Bold, GraphicsUnit.Pixel);
        var text = g.MeasureString(Label(number), font, int.MaxValue, StringFormat.GenericTypographic);
        return new(Math.Max(size, text.Width + size * .4f), size * 1.25f);
    }
    public static void Resize(Annotation item, float size)
    {
        var center = new PointF((item.Start.X + item.End.X) / 2, (item.Start.Y + item.End.Y) / 2);
        var dimensions = Measure(item.PinNumber, size); item.PinSize = size;
        item.Start = new(center.X - dimensions.Width / 2, center.Y - dimensions.Height / 2);
        item.End = new(item.Start.X + dimensions.Width, item.Start.Y + dimensions.Height);
    }
    public static void Renumber(Annotation item, BigInteger number)
    {
        item.PinNumber = number; Resize(item, item.PinSize);
    }
    public static GraphicsPath CreatePath(RectangleF b)
    {
        var path = new GraphicsPath();
        if (b.Width <= 0 || b.Height <= 0) return path;
        var h = b.Height / 1.25f; var cx = b.X + b.Width / 2; var radius = h * .4f;
        path.AddArc(b.Left, b.Top, radius * 2, radius * 2, 180, 90);
        path.AddLine(b.Left + radius, b.Top, b.Right - radius, b.Top);
        path.AddArc(b.Right - radius * 2, b.Top, radius * 2, radius * 2, 270, 90);
        path.AddLine(b.Right, b.Top + radius, b.Right, b.Top + h - radius);
        path.AddArc(b.Right - radius * 2, b.Top + h - radius * 2, radius * 2, radius * 2, 0, 90);
        path.AddLine(b.Right - radius, b.Top + h, cx + .1f * h, b.Top + h);
        path.AddLine(cx + .1f * h, b.Top + h, cx, b.Bottom);
        path.AddLine(cx, b.Bottom, cx - .1f * h, b.Top + h);
        path.AddLine(cx - .1f * h, b.Top + h, b.Left + radius, b.Top + h);
        path.AddArc(b.Left, b.Top + h - radius * 2, radius * 2, radius * 2, 90, 90);
        path.AddLine(b.Left, b.Top + h - radius, b.Left, b.Top + radius);
        path.CloseFigure(); return path;
    }
    public static void Draw(Graphics g, Annotation item)
    {
        var b = item.Bounds;
        if (b.Width <= 0 || b.Height <= 0) return;
        using var path = CreatePath(b); using var fill = new SolidBrush(item.Color);
        using var outline = new Pen(Color.FromArgb(235, Color.White), Math.Max(1, item.PinSize / 32));
        g.FillPath(fill, path); g.DrawPath(outline, path);
        var luminance = item.Color.R * .299f + item.Color.G * .587f + item.Color.B * .114f;
        using var label = new SolidBrush(luminance > 160 ? Color.FromArgb(24, 28, 38) : Color.White);
        using var font = new Font("Segoe UI", item.PinSize * .54f, FontStyle.Bold, GraphicsUnit.Pixel);
        using var format = (StringFormat)StringFormat.GenericTypographic.Clone();
        format.Alignment = StringAlignment.Center; format.LineAlignment = StringAlignment.Center; format.FormatFlags |= StringFormatFlags.NoWrap;
        g.DrawString(Label(item.PinNumber), font, label, new RectangleF(b.X, b.Y, b.Width, b.Height / 1.25f), format);
    }
}
