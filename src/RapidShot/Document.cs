using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace RapidShot;

public enum EditorTool { Select, Pen, Rectangle, Ellipse, Arrow, Line, Text, Blur, Eraser, Crop }

public sealed class Annotation
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public EditorTool Tool { get; set; }
    public Color Color { get; set; } = Color.FromArgb(255, 88, 100);
    public float Width { get; set; } = 4;
    public float FontSize { get; set; } = 28;
    public int Opacity { get; set; } = 100;
    public string Text { get; set; } = "";
    public bool DoubleHeaded { get; set; }
    public PointF Start { get; set; }
    public PointF End { get; set; }
    public List<PointF> Points { get; set; } = [];
    public RectangleF Bounds => RectangleF.FromLTRB(Math.Min(Start.X, End.X), Math.Min(Start.Y, End.Y), Math.Max(Start.X, End.X), Math.Max(Start.Y, End.Y));
    public Annotation Clone() => new() { Id = Id, Tool = Tool, Color = Color, Width = Width, FontSize = FontSize, Opacity = Opacity, Text = Text, DoubleHeaded = DoubleHeaded, Start = Start, End = End, Points = [.. Points] };
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
            var result = Tool == EditorTool.Pen && Points.Count > 0
                ? RectangleF.FromLTRB(Points.Min(p => p.X), Points.Min(p => p.Y), Points.Max(p => p.X), Points.Max(p => p.Y)) : Bounds;
            result.Inflate(Width / 2 + 3, Width / 2 + 3);
            return result;
        }
    }
    public bool HitTest(PointF point, float tolerance)
    {
        if (Tool is EditorTool.Blur or EditorTool.Text) return SelectionBounds.Contains(point);
        if (Tool == EditorTool.Pen)
            return Points.Count == 1 ? Distance(point, Points[0], Points[0]) <= tolerance + Width / 2
                : Enumerable.Range(1, Points.Count - 1).Any(i => Distance(point, Points[i - 1], Points[i]) <= tolerance + Width / 2);
        if (Tool is EditorTool.Line or EditorTool.Arrow) return Distance(point, Start, End) <= tolerance + Width * 2;
        using var path = new GraphicsPath();
        if (Bounds.Width < 1 || Bounds.Height < 1) return false;
        if (Tool == EditorTool.Ellipse) path.AddEllipse(Bounds); else path.AddRectangle(Bounds);
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
            case EditorTool.Ellipse: if (Bounds.Width > 0 && Bounds.Height > 0) g.DrawEllipse(pen, Bounds); break;
            case EditorTool.Arrow:
                using (var cap = new AdjustableArrowCap(4, 5, true)) { pen.CustomEndCap = cap; if (DoubleHeaded) pen.CustomStartCap = cap; g.DrawLine(pen, Start, End); }
                break;
            case EditorTool.Line: g.DrawLine(pen, Start, End); break;
            case EditorTool.Text:
                using (var font = new Font("Segoe UI", FontSize, FontStyle.Bold, GraphicsUnit.Pixel))
                using (var brush = new SolidBrush(Color))
                    g.DrawString(Text, font, brush, Start, StringFormat.GenericTypographic);
                break;
        }
    }
}

public sealed class ScreenshotDocument : IDisposable
{
    private sealed record Snapshot(Rectangle Crop, List<Annotation> Items);
    private readonly List<Snapshot> undo = [];
    private readonly List<Snapshot> redo = [];
    public Bitmap Original { get; }
    public Rectangle Crop { get; private set; }
    public List<Annotation> Items { get; private set; } = [];
    public bool CanUndo => undo.Count > 0;
    public bool CanRedo => redo.Count > 0;
    public event Action? Changed;
    public ScreenshotDocument(Bitmap bitmap) { Original = bitmap; Crop = new(Point.Empty, bitmap.Size); }
    private Snapshot State() => new(Crop, Items.Select(x => x.Clone()).ToList());
    public void BeginChange()
    {
        undo.Add(State());
        if (undo.Count > 60) undo.RemoveAt(0);
        redo.Clear();
    }
    public void NotifyChanged() => Changed?.Invoke();
    public void Add(Annotation item) { BeginChange(); Items.Add(item); NotifyChanged(); }
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
    private void Restore(Snapshot state) { Crop = state.Crop; Items = state.Items.Select(x => x.Clone()).ToList(); NotifyChanged(); }
    public Bitmap Render(Annotation? excluded = null)
    {
        // Render only the visible crop; all annotation coordinates stay in the original space.
        var result = new Bitmap(Crop.Width, Crop.Height, PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(result)) g.DrawImage(Original, new Rectangle(Point.Empty, Crop.Size), Crop, GraphicsUnit.Pixel);
        foreach (var item in Items)
        {
            if (ReferenceEquals(item, excluded)) continue;
            if (item.Tool == EditorTool.Blur)
            {
                var r = Rectangle.Round(item.Bounds);
                r.Offset(-Crop.X, -Crop.Y);
                ImageBlur.Apply(result, r, Math.Clamp((int)item.Width * 3, 8, 64));
            }
            else
            {
                using var g = Graphics.FromImage(result);
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
                g.TranslateTransform(-Crop.X, -Crop.Y);
                item.Draw(g);
            }
        }
        return result;
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
        try
        {
            int w = region.Width, h = region.Height;
            var source = new byte[w * h * 4];
            var target = new byte[source.Length];
            for (var y = 0; y < h; y++) Marshal.Copy(data.Scan0 + y * data.Stride, source, y * w * 4, w * 4);
            // Three separable box passes approximate a Gaussian blur in linear time.
            for (var pass = 0; pass < 3; pass++)
            {
                Box(source, target, w, h, radius, true);
                Box(target, source, w, h, radius, false);
            }
            for (var y = 0; y < h; y++) Marshal.Copy(source, y * w * 4, data.Scan0 + y * data.Stride, w * 4);
        }
        finally { image.UnlockBits(data); }
    }
    private static void Box(byte[] input, byte[] output, int w, int h, int radius, bool horizontal)
    {
        int lines = horizontal ? h : w, length = horizontal ? w : h;
        for (var line = 0; line < lines; line++)
        for (var channel = 0; channel < 4; channel++)
        {
            int Index(int p) => (horizontal ? line * w + p : p * w + line) * 4 + channel;
            var sum = 0;
            for (var i = -radius; i <= radius; i++) sum += input[Index(Math.Clamp(i, 0, length - 1))];
            for (var p = 0; p < length; p++)
            {
                output[Index(p)] = (byte)(sum / (radius * 2 + 1));
                sum -= input[Index(Math.Clamp(p - radius, 0, length - 1))];
                sum += input[Index(Math.Clamp(p + radius + 1, 0, length - 1))];
            }
        }
    }
}
