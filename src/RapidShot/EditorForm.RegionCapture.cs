using System.Drawing.Drawing2D;

namespace RapidShot;

internal sealed partial class EditorForm
{
    private bool isRegionCapture, regionEditing, selectingRegion, positioningRegionBars;
    private Rectangle captureDesktopBounds, regionSelection;
    private Point regionAnchor;
    private IconButton? closeRegion;

    private void InitializeRegionCapture(Rectangle bounds, Control root)
    {
        isRegionCapture = true; captureDesktopBounds = bounds;
        AutoScaleMode = AutoScaleMode.None; MinimumSize = Size.Empty;
        FormBorderStyle = FormBorderStyle.None; ShowInTaskbar = false; TopMost = true;
        StartPosition = FormStartPosition.Manual; Bounds = bounds;
        DoubleBuffered = true; Cursor = Cursors.Cross;
        root.Visible = false;
        // Reuse this form and its editing controls. Selecting a region never
        // closes the capture window or creates another top-level editor.
        canvas.Parent = this; canvas.Dock = DockStyle.Fill; canvas.Visible = false;
        foreach (var bar in new[] { toolBar, parameterBar })
        {
            bar.Parent = canvas; bar.Anchor = AnchorStyles.Top | AnchorStyles.Left;
            bar.Margin = Padding.Empty; bar.Visible = false; bar.WrapContents = true;
            bar.SizeChanged += (_, _) => PositionRegionBars();
        }
        status.Parent = canvas; status.Dock = DockStyle.None; status.Visible = false;
        status.BackColor = Color.Transparent; status.Padding = new Padding(8, 0, 8, 0);
        closeRegion = new IconButton(UiIcon.Cancel, Strings["CloseCapture"]);
        closeRegion.Click += (_, _) => Close(); toolBar.Controls.Add(closeRegion);
        tips.SetToolTip(closeRegion, Strings["CloseCaptureHint"]);
        canvas.CropStateChanged += PositionRegionBars;
        document.Changed += PositionRegionBars;
        SizeChanged += (_, _) => PositionRegionBars();
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (!isRegionCapture || regionEditing) return;
        if (e.Button == MouseButtons.Right) { Close(); return; }
        if (e.Button != MouseButtons.Left) return;
        regionAnchor = ClampRegionPoint(e.Location); regionSelection = Rectangle.Empty;
        selectingRegion = true; Capture = true; Invalidate();
    }
    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (!isRegionCapture || regionEditing || !selectingRegion) return;
        UpdateRegionSelection(e.Location); Invalidate();
    }
    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        if (!isRegionCapture || regionEditing || !selectingRegion || e.Button != MouseButtons.Left) return;
        UpdateRegionSelection(e.Location); selectingRegion = false; Capture = false;
        if (regionSelection.Width < 2 || regionSelection.Height < 2) { Invalidate(); return; }
        BeginRegionEditing();
    }
    protected override void OnMouseCaptureChanged(EventArgs e)
    {
        base.OnMouseCaptureChanged(e);
        if (selectingRegion && !Capture) { selectingRegion = false; regionSelection = Rectangle.Empty; Invalidate(); }
    }
    private Point ClampRegionPoint(Point p) => new(Math.Clamp(p.X, 0, document.Original.Width), Math.Clamp(p.Y, 0, document.Original.Height));
    private void UpdateRegionSelection(Point point)
    {
        var p = ClampRegionPoint(point);
        regionSelection = Rectangle.FromLTRB(Math.Min(regionAnchor.X, p.X), Math.Min(regionAnchor.Y, p.Y), Math.Max(regionAnchor.X, p.X), Math.Max(regionAnchor.Y, p.Y));
    }
    private void BeginRegionEditing()
    {
        document.SetCaptureRegion(regionSelection);
        canvas.UseDesktopCoordinates(); regionEditing = true; Cursor = Cursors.Default;
        canvas.Visible = true; toolBar.Visible = status.Visible = true; parameterBar.Visible = hasToolParameters;
        canvas.BringToFront(); toolBar.BringToFront(); parameterBar.BringToFront(); status.BringToFront();
        PositionRegionBars(); canvas.Focus(); Invalidate();
    }
    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        if (!isRegionCapture || regionEditing) return;
        var g = e.Graphics; g.DrawImageUnscaled(document.Original, Point.Empty);
        using var shade = new SolidBrush(Color.FromArgb(155, 10, 14, 24));
        using var outside = new Region(ClientRectangle);
        if (!regionSelection.IsEmpty) outside.Exclude(regionSelection);
        g.FillRegion(shade, outside);
        if (!regionSelection.IsEmpty) SelectionFrame.Draw(g, regionSelection);
        NativeMethods.GetCursorPos(out var cursor);
        var screen = Screen.FromPoint(cursor).Bounds; screen.Offset(-captureDesktopBounds.X, -captureDesktopBounds.Y);
        var help = regionSelection.Width > 0 ? Strings.Format("CaptureDragHint", regionSelection.Width, regionSelection.Height) : Strings["CaptureStartHint"];
        using var font = new Font("Segoe UI", 11);
        var size = TextRenderer.MeasureText(help, font);
        var box = new Rectangle(screen.X + (screen.Width - size.Width) / 2 - 16, screen.Y + 24, size.Width + 32, 40);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        using var path = FloatingBar.Rounded(box, 8); using var background = new SolidBrush(Theme.Panel); g.FillPath(background, path);
        TextRenderer.DrawText(g, help, font, box, Theme.Foreground, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
    }
    private void PositionRegionBars()
    {
        if (!regionEditing || positioningRegionBars) return;
        positioningRegionBars = true;
        try
        {
            var selection = Rectangle.Round(canvas.DisplayedCaptureBounds);
            var screenRect = selection; screenRect.Offset(captureDesktopBounds.Location);
            var screen = Screen.FromRectangle(screenRect).Bounds; screen.Offset(-captureDesktopBounds.X, -captureDesktopBounds.Y);
            screen.Intersect(canvas.ClientRectangle);
            var maxWidth = Math.Max(1, screen.Width - 24);
            foreach (var bar in new[] { toolBar, parameterBar })
            {
                var maximum = new Size(maxWidth, 0);
                if (bar.MaximumSize != maximum) { bar.MaximumSize = maximum; bar.PerformLayout(); }
                var preferred = bar.GetPreferredSize(new Size(maxWidth, 0));
                if (bar.Size != preferred) bar.Size = preferred;
            }
            var width = hasToolParameters ? Math.Max(toolBar.Width, parameterBar.Width) : toolBar.Width;
            List<Size> sizes = [toolBar.Size];
            if (hasToolParameters) sizes.Add(parameterBar.Size);
            sizes.Add(new Size(Math.Min(maxWidth, width), 22));
            var rows = RegionPanelPlacement.Place(selection, screen, sizes.ToArray());
            toolBar.Location = rows[0].Location;
            if (hasToolParameters) parameterBar.Location = rows[1].Location;
            status.Bounds = rows[^1];
        }
        finally { positioningRegionBars = false; }
    }
}

internal static class RegionPanelPlacement
{
    internal static Rectangle[] Place(Rectangle selection, Rectangle screen, Size[] rows)
    {
        const int gap = 6, margin = 12;
        var totalHeight = rows.Sum(row => row.Height) + gap * (rows.Length - 1);
        var y = selection.Bottom + margin;
        if (y + totalHeight > screen.Bottom - margin)
        {
            var above = selection.Top - margin - totalHeight;
            if (above >= screen.Top + margin) y = above;
            else if (selection.Top - screen.Top > screen.Bottom - selection.Bottom) y = screen.Top + margin;
            else y = screen.Bottom - margin - totalHeight;
        }
        y = Math.Clamp(y, screen.Top + margin, Math.Max(screen.Top + margin, screen.Bottom - margin - totalHeight));
        var result = new Rectangle[rows.Length];
        for (var i = 0; i < rows.Length; i++)
        {
            var width = Math.Min(rows[i].Width, Math.Max(1, screen.Width - margin * 2));
            var x = Math.Clamp(selection.Left + (selection.Width - width) / 2, screen.Left + margin, Math.Max(screen.Left + margin, screen.Right - margin - width));
            result[i] = new(x, y, width, rows[i].Height); y += rows[i].Height + gap;
        }
        return result;
    }
}
