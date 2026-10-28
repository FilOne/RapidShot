using System.Drawing.Drawing2D;
using System.Drawing.Imaging;

namespace RapidShot;

internal static class CaptureService
{
    public static Bitmap? Capture(bool region)
    {
        var bounds = SystemInformation.VirtualScreen;
        var image = new Bitmap(bounds.Width, bounds.Height, PixelFormat.Format32bppArgb);
        var ownsImage = true;
        try
        {
            using (var g = Graphics.FromImage(image)) g.CopyFromScreen(bounds.Location, Point.Empty, bounds.Size, CopyPixelOperation.SourceCopy);
            if (!region) { ownsImage = false; return image; }
            using var overlay = new RegionOverlay(image, bounds);
            return overlay.ShowDialog() == DialogResult.OK ? image.Clone(overlay.Selection, PixelFormat.Format32bppArgb) : null;
        }
        finally { if (ownsImage) image.Dispose(); }
    }
}

internal sealed class RegionOverlay : Form
{
    private readonly Bitmap desktop;
    private Point anchor;
    private bool selecting;
    public Rectangle Selection { get; private set; }
    public RegionOverlay(Bitmap image, Rectangle bounds)
    {
        desktop = image; AutoScaleMode = AutoScaleMode.None;
        FormBorderStyle = FormBorderStyle.None; ShowInTaskbar = false; TopMost = true;
        StartPosition = FormStartPosition.Manual; Bounds = bounds;
        DoubleBuffered = true; Cursor = Cursors.Cross; KeyPreview = true;
        KeyDown += (_, e) => { if (e.KeyCode == Keys.Escape) { DialogResult = DialogResult.Cancel; Close(); } };
        MouseDown += (_, e) =>
        {
            if (e.Button == MouseButtons.Right) { DialogResult = DialogResult.Cancel; Close(); return; }
            if (e.Button != MouseButtons.Left) return;
            selecting = true; anchor = e.Location; Selection = Rectangle.Empty; Capture = true; Invalidate();
        };
        MouseMove += (_, e) =>
        {
            if (!selecting) return;
            var p = new Point(Math.Clamp(e.X, 0, desktop.Width), Math.Clamp(e.Y, 0, desktop.Height));
            Selection = Rectangle.FromLTRB(Math.Min(anchor.X, p.X), Math.Min(anchor.Y, p.Y), Math.Max(anchor.X, p.X), Math.Max(anchor.Y, p.Y));
            Invalidate();
        };
        MouseUp += (_, e) =>
        {
            if (!selecting || e.Button != MouseButtons.Left) return;
            var p = new Point(Math.Clamp(e.X, 0, desktop.Width), Math.Clamp(e.Y, 0, desktop.Height));
            Selection = Rectangle.FromLTRB(Math.Min(anchor.X, p.X), Math.Min(anchor.Y, p.Y), Math.Max(anchor.X, p.X), Math.Max(anchor.Y, p.Y));
            selecting = false; Capture = false;
            if (Selection.Width < 2 || Selection.Height < 2) { Invalidate(); return; }
            DialogResult = DialogResult.OK; Close();
        };
    }
    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.DrawImageUnscaled(desktop, Point.Empty);
        using var shade = new SolidBrush(Color.FromArgb(155, 10, 14, 24));
        using var region = new Region(ClientRectangle);
        if (Selection.Width > 0 && Selection.Height > 0) region.Exclude(Selection);
        g.FillRegion(shade, region);
        if (!Selection.IsEmpty)
        {
            using var pen = new Pen(Theme.Accent, 2) { DashStyle = DashStyle.Dash };
            g.DrawRectangle(pen, Selection);
        }
        // Place help on the monitor under the pointer, including negative monitor coordinates.
        NativeMethods.GetCursorPos(out var cursor);
        var screen = Screen.FromPoint(cursor).Bounds;
        var origin = PointToClient(screen.Location);
        var help = Selection.Width > 0 ? $"{Selection.Width} × {Selection.Height} px   ·   Отпустите мышь для захвата   ·   Esc — отмена" : "RAPID SHOT   /   Выделите область мышью   ·   Esc — отмена";
        using var font = new Font("Segoe UI", 12, FontStyle.Bold);
        var size = g.MeasureString(help, font);
        var box = new RectangleF(origin.X + (screen.Width - size.Width) / 2 - 18, origin.Y + 28, size.Width + 36, 48);
        using var background = new SolidBrush(Theme.Background); g.FillRectangle(background, box);
        using var text = new SolidBrush(Theme.Foreground); g.DrawString(help, font, text, box.X + 18, box.Y + 13);
    }
}
