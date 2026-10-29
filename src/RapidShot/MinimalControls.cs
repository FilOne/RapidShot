using System.Drawing.Drawing2D;

namespace RapidShot;

internal enum UiIcon { None, Select, Pen, Line, Arrow, Rectangle, Ellipse, Text, Blur, Eraser, Crop, Pin, Save, Copy, Upload, Search, Undo, Redo, Delete, Camera, Color, Cancel }

internal class IconButton : Button
{
    private bool hovered, pressed, selected;
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public UiIcon Symbol { get; set; }
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public bool ShowText { get; set; }
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public bool Selected { get => selected; set { selected = value; Invalidate(); } }
    public IconButton(UiIcon symbol = UiIcon.None, string name = "")
    {
        Symbol = symbol; AccessibleName = name; Text = name;
        AutoSize = false; Size = new(34, 34); Margin = new Padding(1, 0, 1, 0); Padding = Padding.Empty;
        FlatStyle = FlatStyle.Flat; FlatAppearance.BorderSize = 0; UseVisualStyleBackColor = false;
        BackColor = Theme.Panel; ForeColor = Theme.Foreground; Cursor = Cursors.Hand;
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
    }
    protected override void OnMouseEnter(EventArgs e) { hovered = true; Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { hovered = pressed = false; Invalidate(); base.OnMouseLeave(e); }
    protected override void OnMouseDown(MouseEventArgs e) { pressed = e.Button == MouseButtons.Left; Invalidate(); base.OnMouseDown(e); }
    protected override void OnMouseUp(MouseEventArgs e) { pressed = false; Invalidate(); base.OnMouseUp(e); }
    protected override void OnPaintBackground(PaintEventArgs e)
    {
        // Paint every pixel, including the corners outside the rounded button.
        // The native flat-button background/focus border can leave light pixels there.
        e.Graphics.Clear(Parent?.BackColor is { A: 255 } background ? background : Theme.Panel);
    }
    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        // ButtonBase may skip OnPaintBackground for WM_PRINTCLIENT. Clear here
        // as well so native repaints and bitmap rendering produce opaque corners.
        g.Clear(Parent?.BackColor is { A: 255 } background ? background : Theme.Panel);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        using var path = FloatingBar.Rounded(new RectangleF(1, 1, Width - 2, Height - 2), 5);
        using var fill = new SolidBrush(pressed ? Theme.Raised : hovered || selected ? Color.FromArgb(66, 71, 78) : BackColor); g.FillPath(fill, path);
        if (selected || Focused && ShowFocusCues) { using var outline = new Pen(Theme.Accent, 1); g.DrawPath(outline, path); }
        var color = Enabled ? ForeColor : Theme.Disabled;
        var iconX = ShowText ? 8 : (Width - 20f) / 2;
        UiGlyph.Draw(g, Symbol, new(iconX, (Height - 20f) / 2, 20, 20), color, FlatAppearance.BorderColor);
        if (ShowText) TextRenderer.DrawText(g, Text, Font, new Rectangle(33, 0, Width - 36, Height), color, TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
    }
}

internal sealed class FloatingBar : FlowLayoutPanel
{
    public FloatingBar()
    {
        AutoSize = true; AutoSizeMode = AutoSizeMode.GrowAndShrink; Anchor = AnchorStyles.None;
        WrapContents = false; BackColor = Theme.Panel; Padding = new Padding(8, 6, 8, 6); Margin = new Padding(12, 3, 12, 3);
        DoubleBuffered = true;
    }
    internal static GraphicsPath Rounded(RectangleF box, float radius)
    {
        var p = new GraphicsPath(); var diameter = Math.Min(radius * 2, Math.Min(box.Width, box.Height));
        if (diameter <= 0) return p;
        p.AddArc(box.Left, box.Top, diameter, diameter, 180, 90); p.AddArc(box.Right - diameter, box.Top, diameter, diameter, 270, 90);
        p.AddArc(box.Right - diameter, box.Bottom - diameter, diameter, diameter, 0, 90); p.AddArc(box.Left, box.Bottom - diameter, diameter, diameter, 90, 90); p.CloseFigure(); return p;
    }
    protected override void OnPaintBackground(PaintEventArgs e)
    {
        // A native window Region has hard, un-antialiased edges. Blend the rounded
        // panel into its real parent instead, also when floating over the desktop.
        if (Parent is { } parent)
        {
            var state = e.Graphics.Save(); e.Graphics.TranslateTransform(-Left, -Top);
            using var args = new PaintEventArgs(e.Graphics, new Rectangle(Location, Size));
            InvokePaintBackground(parent, args); InvokePaint(parent, args); e.Graphics.Restore(state);
        }
        else e.Graphics.Clear(Theme.Background);
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        using var path = Rounded(new RectangleF(.5f, .5f, Width - 1, Height - 1), 8);
        using var fill = new SolidBrush(BackColor); e.Graphics.FillPath(fill, path);
    }
    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e); e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        using var path = Rounded(new RectangleF(.5f, .5f, Width - 1, Height - 1), 8);
        using var pen = new Pen(Color.FromArgb(65, 69, 76), 1); e.Graphics.DrawPath(pen, path);
    }
}

internal static class UiGlyph
{
    public static Bitmap CreateImage(UiIcon icon, Color color, int size = 20)
    {
        var image = new Bitmap(size, size, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        using var g = Graphics.FromImage(image); g.Clear(Color.Transparent); g.SmoothingMode = SmoothingMode.AntiAlias;
        Draw(g, icon, new RectangleF(0, 0, size, size), color, color); return image;
    }
    public static UiIcon ForTool(EditorTool tool) => Enum.TryParse<UiIcon>(tool.ToString(), out var icon) ? icon : UiIcon.None;
    public static void Draw(Graphics g, UiIcon icon, RectangleF box, Color color, Color accent)
    {
        var state = g.Save(); g.TranslateTransform(box.X, box.Y); g.ScaleTransform(box.Width / 20, box.Height / 20);
        using var p = new Pen(color, 1.35f) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round };
        using var brush = new SolidBrush(color);
        void Lines(params PointF[] points) => g.DrawLines(p, points);
        switch (icon)
        {
            case UiIcon.Select: Lines(new(4, 3), new(16, 10), new(10, 11), new(7, 17), new(4, 3)); break;
            case UiIcon.Pen: Lines(new(4, 15), new(5, 11), new(13, 3), new(17, 7), new(9, 15), new(4, 15)); Lines(new(11, 5), new(15, 9)); g.DrawLine(p, 4, 18, 15, 18); break;
            case UiIcon.Line: g.DrawLine(p, 4, 4, 16, 16); break;
            case UiIcon.Arrow: Lines(new(4, 16), new(16, 4), new(16, 11)); g.DrawLine(p, 16, 4, 9, 4); break;
            case UiIcon.Rectangle: g.DrawRectangle(p, 3, 4, 14, 12); break;
            case UiIcon.Ellipse: g.DrawEllipse(p, 3, 3, 14, 14); break;
            case UiIcon.Text: Lines(new(3, 5), new(3, 3), new(17, 3), new(17, 5)); g.DrawLine(p, 10, 3, 10, 17); g.DrawLine(p, 7, 17, 13, 17); break;
            case UiIcon.Blur:
                for (var x = 3; x <= 15; x += 4) for (var y = 3; y <= 15; y += 4) { using var dot = new SolidBrush(Color.FromArgb((x + y) % 3 == 0 ? 100 : 210, color)); g.FillRectangle(dot, x, y, 2, 2); } break;
            case UiIcon.Eraser: Lines(new(3, 11), new(11, 3), new(17, 9), new(9, 17), new(6, 17), new(3, 14), new(3, 11)); g.DrawLine(p, 8, 6, 14, 12); g.DrawLine(p, 8, 17, 17, 17); break;
            case UiIcon.Crop: Lines(new(5, 2), new(5, 15), new(18, 15)); Lines(new(2, 5), new(15, 5), new(15, 18)); break;
            case UiIcon.Pin: PinMarker.Draw(g, new Annotation { Tool = EditorTool.Pin, PinNumber = 1, PinSize = 14, Color = color, Start = new(3, 1), End = new(17, 18.5f) }); break;
            case UiIcon.Save: Lines(new(3, 3), new(14, 3), new(17, 6), new(17, 17), new(3, 17), new(3, 3)); g.DrawRectangle(p, 6, 3, 7, 5); g.DrawRectangle(p, 6, 12, 8, 5); break;
            case UiIcon.Copy: g.DrawRectangle(p, 7, 7, 10, 10); Lines(new(4, 13), new(3, 13), new(3, 3), new(13, 3), new(13, 4)); break;
            case UiIcon.Upload: Lines(new(3, 12), new(3, 17), new(17, 17), new(17, 12)); Lines(new(6, 7), new(10, 3), new(14, 7)); g.DrawLine(p, 10, 3, 10, 13); break;
            case UiIcon.Search: g.DrawEllipse(p, 3, 3, 10, 10); g.DrawLine(p, 12, 12, 17, 17); g.FillEllipse(brush, 6.5f, 6.5f, 3, 3); break;
            case UiIcon.Undo:
            case UiIcon.Redo:
                if (icon == UiIcon.Redo) { g.TranslateTransform(20, 0); g.ScaleTransform(-1, 1); }
                using (var curve = new GraphicsPath())
                {
                    curve.AddLine(4, 8, 10, 8); curve.AddBezier(10, 8, 14, 8, 17, 10, 17, 14); curve.AddLine(17, 14, 17, 16);
                    g.DrawPath(p, curve);
                }
                Lines(new(8, 4), new(4, 8), new(8, 12)); break;
            case UiIcon.Delete: g.DrawLine(p, 3, 5, 17, 5); Lines(new(5, 5), new(6, 17), new(14, 17), new(15, 5)); Lines(new(7, 5), new(7, 2), new(13, 2), new(13, 5)); g.DrawLine(p, 8, 8, 8, 14); g.DrawLine(p, 12, 8, 12, 14); break;
            case UiIcon.Camera: Lines(new(3, 6), new(6, 6), new(7, 3), new(13, 3), new(14, 6), new(17, 6), new(17, 16), new(3, 16), new(3, 6)); g.DrawEllipse(p, 7, 7, 6, 6); break;
            case UiIcon.Color: using (var ink = new SolidBrush(accent)) { g.FillEllipse(ink, 3, 3, 14, 14); g.DrawEllipse(p, 3, 3, 14, 14); } break;
            case UiIcon.Cancel: g.DrawLine(p, 5, 5, 15, 15); g.DrawLine(p, 15, 5, 5, 15); break;
        }
        g.Restore(state);
    }
}
