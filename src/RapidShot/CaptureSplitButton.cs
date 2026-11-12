using System.Drawing.Drawing2D;

namespace RapidShot;

internal sealed class CaptureSplitButton : UserControl
{
    internal Button PrimaryAction { get; }
    internal Button MenuAction { get; }
    internal event Action? CopyRequested;
    internal event Action? MenuRequested;

    public CaptureSplitButton(UiText strings)
    {
        AutoScaleMode = AutoScaleMode.None; Size = new(124, 34);
        Margin = new Padding(1, 0, 1, 0); BackColor = Theme.Panel; TabStop = false;
        AccessibleName = strings["Capture"]; AccessibleRole = AccessibleRole.Grouping;
        PrimaryAction = new Segment(this, false) { Text = strings["Capture"], AccessibleName = strings["CaptureCopy"] };
        MenuAction = new Segment(this, true) { AccessibleName = strings["CaptureActions"] };
        PrimaryAction.Click += (_, _) => CopyRequested?.Invoke();
        MenuAction.Click += (_, _) => MenuRequested?.Invoke();
        Controls.AddRange([PrimaryAction, MenuAction]); ArrangeParts();
    }
    private void ArrangeParts()
    {
        if (PrimaryAction == null) return;
        PrimaryAction.Bounds = new(0, 0, Math.Max(1, Width - 30), Height);
        MenuAction.Bounds = new(Math.Max(1, Width - 30), 0, 30, Height);
        PrimaryAction.Invalidate(); MenuAction.Invalidate();
    }
    protected override void OnResize(EventArgs e) { base.OnResize(e); ArrangeParts(); }

    private sealed class Segment : Button
    {
        private readonly CaptureSplitButton owner;
        private readonly bool dropdown;
        private bool hovered, pressed;
        internal Segment(CaptureSplitButton owner, bool dropdown)
        {
            this.owner = owner; this.dropdown = dropdown;
            FlatStyle = FlatStyle.Flat; FlatAppearance.BorderSize = 0; UseVisualStyleBackColor = false;
            BackColor = Theme.Panel; ForeColor = Theme.Background; Cursor = Cursors.Hand; Margin = Padding.Empty;
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        }
        protected override void OnMouseEnter(EventArgs e) { hovered = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hovered = pressed = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnMouseDown(MouseEventArgs e) { pressed = e.Button == MouseButtons.Left; Invalidate(); base.OnMouseDown(e); }
        protected override void OnMouseUp(MouseEventArgs e) { pressed = false; Invalidate(); base.OnMouseUp(e); }
        protected override void OnMouseCaptureChanged(EventArgs e) { if (!Capture) pressed = false; Invalidate(); base.OnMouseCaptureChanged(e); }
        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if ((keyData == Keys.Down || keyData == (Keys.Alt | Keys.Down)) && owner.MenuAction.Enabled) { owner.MenuRequested?.Invoke(); return true; }
            return base.ProcessCmdKey(ref msg, keyData);
        }
        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics; g.Clear(owner.BackColor);
            var state = g.Save(); g.TranslateTransform(-Left, -Top); g.SmoothingMode = SmoothingMode.AntiAlias;
            using var path = FloatingBar.Rounded(new RectangleF(1, 1, owner.Width - 2, owner.Height - 2), 5);
            var background = !Enabled ? Theme.Raised : pressed ? Color.FromArgb(44, 163, 211) : hovered ? Color.FromArgb(91, 202, 245) : Theme.Accent;
            using var fill = new SolidBrush(background); g.FillPath(fill, path);
            var color = Enabled ? Theme.OnAccent : Theme.Disabled;
            var split = owner.PrimaryAction.Width;
            using var line = new Pen(Color.FromArgb(65, Theme.OnAccent), 1);
            g.DrawLine(line, split + .5f, 6, split + .5f, owner.Height - 6);
            if (dropdown)
            {
                using var arrow = new Pen(color, 1.4f) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round };
                var x = split + Width / 2f; var y = owner.Height / 2f;
                g.DrawLines(arrow, [new PointF(x - 3, y - 1.5f), new(x, y + 1.5f), new(x + 3, y - 1.5f)]);
            }
            else
            {
                UiGlyph.Draw(g, UiIcon.Copy, new(8, (owner.Height - 20) / 2f, 20, 20), color, color);
                TextRenderer.DrawText(g, Text, Font, new Rectangle(32, 0, split - 34, owner.Height), color, TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
            }
            if (Focused && ShowFocusCues) { using var focus = new Pen(Theme.Foreground, 1); g.DrawPath(focus, path); }
            g.Restore(state);
        }
    }
}
