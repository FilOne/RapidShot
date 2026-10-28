using System.Drawing.Drawing2D;

namespace RapidShot;

internal static class Theme
{
    public static readonly Color Background = Color.FromArgb(23, 26, 34);
    public static readonly Color Panel = Color.FromArgb(32, 36, 47);
    public static readonly Color Raised = Color.FromArgb(44, 49, 63);
    public static readonly Color Foreground = Color.FromArgb(234, 238, 247);
    public static readonly Color Muted = Color.FromArgb(150, 160, 181);
    public static readonly Color Accent = Color.FromArgb(116, 214, 189);
    public static void Apply(Form form)
    {
        form.BackColor = Background; form.ForeColor = Foreground;
        form.Font = new Font("Segoe UI", 10);
        form.Icon = CreateIcon();
        form.StartPosition = FormStartPosition.CenterScreen;
        form.HandleCreated += (_, _) => { var dark = 1; NativeMethods.DwmSetWindowAttribute(form.Handle, 20, ref dark, sizeof(int)); };
    }
    public static Button Button(string text, EventHandler? click = null, bool accent = false)
    {
        var button = new Button { Text = text, AutoSize = true, Height = 36, Padding = new Padding(10, 4, 10, 4), Margin = new Padding(4), FlatStyle = FlatStyle.Flat, BackColor = accent ? Accent : Raised, ForeColor = accent ? Background : Foreground, Cursor = Cursors.Hand, UseVisualStyleBackColor = false };
        button.FlatAppearance.BorderSize = 0;
        button.FlatAppearance.MouseOverBackColor = accent ? Color.FromArgb(148, 232, 211) : Color.FromArgb(61, 68, 87);
        if (click != null) button.Click += click;
        return button;
    }
    public static Label Label(string text, bool muted = false) => new() { Text = text, AutoSize = true, ForeColor = muted ? Muted : Foreground, Margin = new Padding(4, 8, 4, 4) };
    public static ComboBox ComboBox()
    {
        var combo = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, FlatStyle = FlatStyle.Flat, BackColor = Raised, ForeColor = Foreground, DrawMode = DrawMode.OwnerDrawFixed, ItemHeight = 24 };
        combo.DrawItem += (_, e) =>
        {
            using var background = new SolidBrush((e.State & DrawItemState.Selected) != 0 ? Color.FromArgb(61, 68, 87) : Raised);
            e.Graphics.FillRectangle(background, e.Bounds);
            if (e.Index >= 0) TextRenderer.DrawText(e.Graphics, combo.Items[e.Index]?.ToString() ?? "", combo.Font, e.Bounds, Foreground, TextFormatFlags.Left | TextFormatFlags.VerticalCenter);
        };
        return combo;
    }
    public static Icon CreateIcon()
    {
        using var image = new Bitmap(64, 64);
        using (var g = Graphics.FromImage(image))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(Color.Transparent);
            using var bg = new SolidBrush(Background); g.FillEllipse(bg, 1, 1, 62, 62);
            using var pen = new Pen(Accent, 5) { StartCap = LineCap.Round, EndCap = LineCap.Round };
            g.DrawLines(pen, [new Point(13, 26), new(13, 16), new(25, 16)]);
            g.DrawLines(pen, [new Point(39, 16), new(51, 16), new(51, 26)]);
            g.DrawLines(pen, [new Point(13, 39), new(13, 49), new(25, 49)]);
            g.DrawLines(pen, [new Point(39, 49), new(51, 49), new(51, 39)]);
            using var dot = new SolidBrush(Accent); g.FillEllipse(dot, 24, 24, 16, 16);
        }
        var handle = image.GetHicon();
        try { using var borrowed = Icon.FromHandle(handle); return (Icon)borrowed.Clone(); }
        finally { NativeMethods.DestroyIcon(handle); }
    }
}
