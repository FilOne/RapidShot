using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Buffers;
using System.Numerics;

namespace RapidShot;

public enum EditorTool { Select, Pen, Rectangle, Ellipse, Arrow, Line, Text, Blur, Eraser, Crop, Pin }

public sealed class Annotation
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public EditorTool Tool { get; set; }
    public Color Color { get; set; } = Color.FromArgb(255, 88, 100);
    public float Width { get; set; } = 4;
    public float FontSize { get; set; } = 28;
    public float EllipseAngle { get; set; }
    public int Opacity { get; set; } = 100;
    public string Text { get; set; } = "";
    public bool DoubleHeaded { get; set; }
    public BigInteger PinNumber { get; set; }
    public float PinSize { get; set; } = 48;
    public PointF Start { get; set; }
    public PointF End { get; set; }
    public List<PointF> Points { get; set; } = [];
    public RectangleF Bounds => RectangleF.FromLTRB(Math.Min(Start.X, End.X), Math.Min(Start.Y, End.Y), Math.Max(Start.X, End.X), Math.Max(Start.Y, End.Y));
    public RectangleF GeometryBounds => Tool == EditorTool.Pen && Points.Count > 0
        ? RectangleF.FromLTRB(Points.Min(p => p.X), Points.Min(p => p.Y), Points.Max(p => p.X), Points.Max(p => p.Y)) : Tool == EditorTool.Ellipse && EllipseAngle != 0 ? EllipseGeometry.Bounds(this) : Bounds;
    public Annotation Clone() => new() { Id = Id, Tool = Tool, Color = Color, Width = Width, FontSize = FontSize, EllipseAngle = EllipseAngle, Opacity = Opacity, Text = Text, DoubleHeaded = DoubleHeaded, PinNumber = PinNumber, PinSize = PinSize, Start = Start, End = End, Points = [.. Points] };
    public static bool IsStrokeTool(EditorTool tool) => tool is EditorTool.Pen or EditorTool.Line or EditorTool.Arrow or EditorTool.Rectangle or EditorTool.Ellipse;
    public void Offset(float dx, float dy)
    {
        Start = new(Start.X + dx, Start.Y + dy);
        End = new(End.X + dx, End.Y + dy);
        for (var i = 0; i < Points.Count; i++) Points[i] = new(Points[i].X + dx, Points[i].Y + dy);
    }
    public RectangleF SelectionBounds
    {
        get
        {
            var result = GeometryBounds;
            result.Inflate(Width / 2 + 3, Width / 2 + 3);
            return result;
        }
    }
    public bool HitTest(PointF point, float tolerance)
    {
        if (Tool == EditorTool.Pin)
        {
            using var pinPath = PinMarker.CreatePath(Bounds);
            using var outline = new Pen(Color.Black, tolerance * 2);
            return pinPath.IsVisible(point) || pinPath.IsOutlineVisible(point, outline);
        }
        if (Tool is EditorTool.Blur or EditorTool.Text) return SelectionBounds.Contains(point);
        if (Tool == EditorTool.Pen)
            return Points.Count == 1 ? Distance(point, Points[0], Points[0]) <= tolerance + Width / 2
                : Enumerable.Range(1, Points.Count - 1).Any(i => Distance(point, Points[i - 1], Points[i]) <= tolerance + Width / 2);
        if (Tool is EditorTool.Line or EditorTool.Arrow) return Distance(point, Start, End) <= tolerance + Width * 2;
        if (Bounds.Width < 1 || Bounds.Height < 1) return false;
        using var path = Tool == EditorTool.Ellipse ? EllipseGeometry.Path(this) : new GraphicsPath();
        if (Tool != EditorTool.Ellipse) path.AddRectangle(Bounds);
        using var pen = new Pen(Color.Black, Width + tolerance * 2);
        return path.IsOutlineVisible(point, pen);
    }
    private static float Distance(PointF p, PointF a, PointF b)
    {
        var dx = b.X - a.X; var dy = b.Y - a.Y;
        var t = dx * dx + dy * dy < 0.001f ? 0 : Math.Clamp(((p.X - a.X) * dx + (p.Y - a.Y) * dy) / (dx * dx + dy * dy), 0, 1);
        return MathF.Sqrt(MathF.Pow(p.X - a.X - t * dx, 2) + MathF.Pow(p.Y - a.Y - t * dy, 2));
    }
    public void Draw(Graphics g)
    {
        var strokeColor = Color.FromArgb((int)Math.Round(Color.A * Math.Clamp(Opacity, 0, 100) / 100f), Color);
        using var pen = new Pen(strokeColor, Width) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round };
        switch (Tool)
        {
            case EditorTool.Pen:
                if (Points.Count > 1) g.DrawLines(pen, Points.ToArray());
                else if (Points.Count == 1) { using var brush = new SolidBrush(strokeColor); g.FillEllipse(brush, Points[0].X - Width / 2, Points[0].Y - Width / 2, Width, Width); }
                break;
            case EditorTool.Rectangle: if (Bounds.Width > 0 && Bounds.Height > 0) g.DrawRectangle(pen, Bounds.X, Bounds.Y, Bounds.Width, Bounds.Height); break;
            case EditorTool.Ellipse:
                if (Bounds.Width > 0 && Bounds.Height > 0) { using var path = EllipseGeometry.Path(this); g.DrawPath(pen, path); }
                break;
            case EditorTool.Arrow:
                using (var cap = new AdjustableArrowCap(4, 5, true)) { pen.CustomEndCap = cap; if (DoubleHeaded) pen.CustomStartCap = cap; g.DrawLine(pen, Start, End); }
                break;
            case EditorTool.Line: g.DrawLine(pen, Start, End); break;
            case EditorTool.Text:
                using (var font = new Font("Segoe UI", FontSize, FontStyle.Bold, GraphicsUnit.Pixel))
                using (var brush = new SolidBrush(Color))
                    g.DrawString(Text, font, brush, Start, StringFormat.GenericTypographic);
                break;
            case EditorTool.Pin: PinMarker.Draw(g, this); break;
        }
    }
}

public sealed class ScreenshotDocument : IDisposable
{
    private sealed record Snapshot(Rectangle Crop, List<Annotation> Items, BigInteger NextPinNumber);
    private readonly List<Snapshot> undo = [];
    private readonly List<Snapshot> redo = [];
    public Bitmap Original { get; }
    public Rectangle Crop { get; private set; }
    public List<Annotation> Items { get; private set; } = [];
    public BigInteger NextPinNumber { get; private set; } = BigInteger.One;
    public bool CanUndo => undo.Count > 0;
    public bool CanRedo => redo.Count > 0;
    public event Action? Changed;
    public ScreenshotDocument(Bitmap bitmap) { Original = bitmap; Crop = new(Point.Empty, bitmap.Size); }
    internal void SetCaptureRegion(Rectangle rectangle)
    {
        if (CanUndo || CanRedo || Items.Count != 0) throw new InvalidOperationException("Область захвата задаётся до редактирования.");
        rectangle.Intersect(new Rectangle(Point.Empty, Original.Size));
        if (rectangle.Width < 2 || rectangle.Height < 2) throw new ArgumentOutOfRangeException(nameof(rectangle));
        // Establish the initial capture, without an undo step exposing the whole desktop.
        Crop = rectangle; NotifyChanged();
    }
    private Snapshot State() => new(Crop, Items.Select(x => x.Clone()).ToList(), NextPinNumber);
    public void BeginChange()
    {
        undo.Add(State());
        if (undo.Count > 60) undo.RemoveAt(0);
        redo.Clear();
    }
    public void NotifyChanged() => Changed?.Invoke();
    public void Add(Annotation item)
    {
        BeginChange(); Items.Add(item);
        if (item.Tool == EditorTool.Pin && item.PinNumber >= NextPinNumber) NextPinNumber = item.PinNumber + BigInteger.One;
        NotifyChanged();
    }
    public Annotation AddPin(PointF point, Color color, float size)
    {
        var item = PinMarker.Create(NextPinNumber, point, color, size, Crop);
        Add(item); return item;
    }
    public bool Remove(Annotation item, bool recordUndo = true)
    {
        if (!Items.Contains(item)) return false;
        if (recordUndo) BeginChange();
        Items.Remove(item);
        if (item.Tool == EditorTool.Pin)
        {
            foreach (var pin in Items.Where(x => x.Tool == EditorTool.Pin && x.PinNumber > item.PinNumber))
                PinMarker.Renumber(pin, pin.PinNumber - BigInteger.One);
            RecalculateNextPinNumber();
        }
        NotifyChanged(); return true;
    }
    public bool SetPinNumber(Annotation item, BigInteger number)
    {
        if (item.Tool != EditorTool.Pin || !Items.Contains(item) || number < BigInteger.One || item.PinNumber == number) return false;
        BeginChange(); PinMarker.Renumber(item, number); RecalculateNextPinNumber(); NotifyChanged(); return true;
    }
    private void RecalculateNextPinNumber() => NextPinNumber = Items.Where(x => x.Tool == EditorTool.Pin)
        .Select(x => x.PinNumber).DefaultIfEmpty(BigInteger.Zero).Max() + BigInteger.One;
    public bool CropTo(Rectangle rectangle)
    {
        rectangle.Intersect(Crop);
        if (rectangle.Width < 2 || rectangle.Height < 2 || rectangle == Crop) return false;
        BeginChange(); Crop = rectangle; NotifyChanged(); return true;
    }
    public void Undo()
    {
        if (!CanUndo) return;
        var previous = undo[^1]; redo.Add(State()); undo.RemoveAt(undo.Count - 1); Restore(previous);
    }
    public void Redo()
    {
        if (!CanRedo) return;
        var next = redo[^1]; undo.Add(State()); redo.RemoveAt(redo.Count - 1); Restore(next);
    }
    private void Restore(Snapshot state) { Crop = state.Crop; Items = state.Items.Select(x => x.Clone()).ToList(); NextPinNumber = state.NextPinNumber; NotifyChanged(); }
    public Bitmap Render(Annotation? excluded = null)
    {
        return RenderPreview(Crop.Size, excluded);
    }
    // Editor previews use the viewport resolution. Export always uses the original crop size.
    internal Bitmap RenderPreview(Size size, Annotation? excluded = null, int endIndex = int.MaxValue)
    {
        var result = new Bitmap(size.Width, size.Height, PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(result))
        {
            if (size != Crop.Size) g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.DrawImage(Original, new Rectangle(Point.Empty, size), Crop, GraphicsUnit.Pixel);
        }
        DrawItems(result, 0, excluded, endIndex);
        return result;
    }
    internal void DrawItems(Bitmap result, int startIndex, Annotation? excluded = null, int endIndex = int.MaxValue)
    {
        var sx = result.Width / (float)Crop.Width; var sy = result.Height / (float)Crop.Height;
        for (var i = startIndex; i < Math.Min(Items.Count, endIndex); i++)
        {
            var item = Items[i];
            if (ReferenceEquals(item, excluded)) continue;
            if (item.Tool == EditorTool.Blur)
            {
                var b = item.Bounds;
                var r = Rectangle.Round(new RectangleF((b.X - Crop.X) * sx, (b.Y - Crop.Y) * sy, b.Width * sx, b.Height * sy));
                var radius = Math.Max(1, (int)Math.Round(Math.Clamp((int)item.Width * 3, 8, 64) * Math.Min(sx, sy)));
                ImageBlur.Apply(result, r, radius);
            }
            else
            {
                using var g = Graphics.FromImage(result);
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
                g.ScaleTransform(sx, sy);
                g.TranslateTransform(-Crop.X, -Crop.Y);
                item.Draw(g);
            }
        }
    }
    public void Dispose() => Original.Dispose();
}

internal static class ImageBlur
{
    public static void Apply(Bitmap image, Rectangle region, int radius)
    {
        region.Intersect(new Rectangle(Point.Empty, image.Size));
        if (region.Width < 1 || region.Height < 1) return;
        var data = image.LockBits(region, ImageLockMode.ReadWrite, PixelFormat.Format32bppArgb);
        var count = checked(region.Width * region.Height * 4);
        var source = ArrayPool<byte>.Shared.Rent(count);
        var target = ArrayPool<byte>.Shared.Rent(count);
        try
        {
            int w = region.Width, h = region.Height;
            for (var y = 0; y < h; y++) Marshal.Copy(data.Scan0 + y * data.Stride, source, y * w * 4, w * 4);
            // Three separable box passes approximate a Gaussian blur in linear time.
            for (var pass = 0; pass < 3; pass++)
            {
                Box(source, target, w, h, radius, true);
                Box(target, source, w, h, radius, false);
            }
            for (var y = 0; y < h; y++) Marshal.Copy(source, y * w * 4, data.Scan0 + y * data.Stride, w * 4);
        }
        finally
        {
            image.UnlockBits(data);
            ArrayPool<byte>.Shared.Return(source); ArrayPool<byte>.Shared.Return(target);
        }
    }
    private static void Box(byte[] input, byte[] output, int w, int h, int radius, bool horizontal)
    {
        int lines = horizontal ? h : w, length = horizontal ? w : h;
        var step = horizontal ? 4 : w * 4;
        var divisor = radius * 2 + 1;
        for (var line = 0; line < lines; line++)
        {
            var offset = horizontal ? line * w * 4 : line * 4;
            int b = 0, g = 0, r = 0, a = 0;
            for (var i = -radius; i <= radius; i++)
            {
                var index = offset + Math.Clamp(i, 0, length - 1) * step;
                b += input[index]; g += input[index + 1]; r += input[index + 2]; a += input[index + 3];
            }
            for (var p = 0; p < length; p++)
            {
                var index = offset + p * step;
                output[index] = (byte)(b / divisor); output[index + 1] = (byte)(g / divisor);
                output[index + 2] = (byte)(r / divisor); output[index + 3] = (byte)(a / divisor);
                var remove = offset + Math.Clamp(p - radius, 0, length - 1) * step;
                var add = offset + Math.Clamp(p + radius + 1, 0, length - 1) * step;
                b += input[add] - input[remove]; g += input[add + 1] - input[remove + 1];
                r += input[add + 2] - input[remove + 2]; a += input[add + 3] - input[remove + 3];
            }
        }
    }
}
