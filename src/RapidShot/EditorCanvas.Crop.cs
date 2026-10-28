using System.Drawing.Drawing2D;

namespace RapidShot;

internal sealed partial class EditorCanvas
{
    [Flags]
    private enum CropEdge { None = 0, Left = 1, Right = 2, Top = 4, Bottom = 8 }
    private RectangleF? cropDraft;
    private RectangleF cropDragStart;
    private CropEdge cropDragEdge;
    public bool HasCropChanges => cropDraft.HasValue && Rectangle.Round(cropDraft.Value) != Document.Crop;
    public event Action? CropStateChanged;
    private RectangleF CropScreenBounds
    {
        get
        {
            var crop = cropDraft ?? Document.Crop;
            return new(imageBounds.X + (crop.X - Document.Crop.X) * zoom, imageBounds.Y + (crop.Y - Document.Crop.Y) * zoom, crop.Width * zoom, crop.Height * zoom);
        }
    }
    private CropEdge HitCropEdge(Point point)
    {
        var r = CropScreenBounds; const int tolerance = 8;
        var hit = CropEdge.None;
        if (point.Y >= r.Top - tolerance && point.Y <= r.Bottom + tolerance)
        {
            if (Math.Abs(point.X - r.Left) <= tolerance) hit |= CropEdge.Left;
            else if (Math.Abs(point.X - r.Right) <= tolerance) hit |= CropEdge.Right;
        }
        if (point.X >= r.Left - tolerance && point.X <= r.Right + tolerance)
        {
            if (Math.Abs(point.Y - r.Top) <= tolerance) hit |= CropEdge.Top;
            else if (Math.Abs(point.Y - r.Bottom) <= tolerance) hit |= CropEdge.Bottom;
        }
        return hit;
    }
    private static Cursor CropCursor(CropEdge edge) => edge switch
    {
        CropEdge.Left or CropEdge.Right => Cursors.SizeWE,
        CropEdge.Top or CropEdge.Bottom => Cursors.SizeNS,
        CropEdge.Left | CropEdge.Top or CropEdge.Right | CropEdge.Bottom => Cursors.SizeNWSE,
        CropEdge.Right | CropEdge.Top or CropEdge.Left | CropEdge.Bottom => Cursors.SizeNESW,
        _ => Cursors.Default
    };
    private void StartCropDrag(MouseEventArgs e)
    {
        cropDraft ??= Document.Crop;
        if (e.Clicks > 1 && CropScreenBounds.Contains(e.Location)) { CommitCrop(); return; }
        cropDragEdge = HitCropEdge(e.Location);
        if (cropDragEdge == CropEdge.None) { HintChanged?.Invoke("Потяните край или угол снимка. Enter — обрезать, Esc — сбросить."); return; }
        cropDragStart = cropDraft.Value; anchor = ToImage(e.Location); dragging = true; Capture = true;
        Cursor = CropCursor(cropDragEdge);
    }
    private void MoveCropDrag(MouseEventArgs e)
    {
        Cursor = CropCursor(dragging ? cropDragEdge : HitCropEdge(e.Location));
        if (!dragging || cropDragEdge == CropEdge.None) return;
        var p = ToImage(e.Location); var dx = p.X - anchor.X; var dy = p.Y - anchor.Y;
        var r = cropDragStart; var bounds = Document.Crop;
        var left = (cropDragEdge & CropEdge.Left) != 0 ? Math.Clamp(r.Left + dx, bounds.Left, r.Right - 2) : r.Left;
        var right = (cropDragEdge & CropEdge.Right) != 0 ? Math.Clamp(r.Right + dx, r.Left + 2, bounds.Right) : r.Right;
        var top = (cropDragEdge & CropEdge.Top) != 0 ? Math.Clamp(r.Top + dy, bounds.Top, r.Bottom - 2) : r.Top;
        var bottom = (cropDragEdge & CropEdge.Bottom) != 0 ? Math.Clamp(r.Bottom + dy, r.Top + 2, bounds.Bottom) : r.Bottom;
        cropDraft = RectangleF.FromLTRB(left, top, right, bottom);
        CropStateChanged?.Invoke();
        HintChanged?.Invoke($"Обрезка: {(int)Math.Round(right - left)} × {(int)Math.Round(bottom - top)} px · Enter — применить · Esc — сбросить");
        Invalidate();
    }
    private void EndCropDrag(MouseEventArgs e)
    {
        if (e.Button != MouseButtons.Left || !dragging) return;
        MoveCropDrag(e); dragging = false; cropDragEdge = CropEdge.None; Capture = false;
    }
    public bool CommitCrop()
    {
        if (!cropDraft.HasValue) return false;
        var crop = Rectangle.Round(cropDraft.Value);
        dragging = false; cropDragEdge = CropEdge.None; Capture = false;
        var changed = Document.CropTo(crop);
        cropDraft = Tool == EditorTool.Crop ? Document.Crop : null;
        CropStateChanged?.Invoke(); Invalidate();
        if (changed) HintChanged?.Invoke($"Обрезано до {Document.Crop.Width} × {Document.Crop.Height} px. Ctrl+Z — вернуть размер.");
        return changed;
    }
    public void CancelCrop()
    {
        cropDraft = Tool == EditorTool.Crop ? Document.Crop : null;
        cropDragEdge = CropEdge.None; dragging = false; Capture = false;
        CropStateChanged?.Invoke(); Invalidate();
        if (Tool == EditorTool.Crop) HintChanged?.Invoke("Рамка сброшена. Потяните край или угол снимка. Enter — обрезать.");
    }
    public void CommitEdits() { CommitText(); FinishObjectTransform(); CommitCrop(); }
    private void DrawCropOverlay(Graphics g)
    {
        if (Tool != EditorTool.Crop || !cropDraft.HasValue) return;
        var r = CropScreenBounds;
        using var shaded = new Region(imageBounds); shaded.Exclude(r);
        using var shade = new SolidBrush(Color.FromArgb(155, 10, 14, 24)); g.FillRegion(shade, shaded);
        using var border = new Pen(Theme.Accent, 1.5f); g.DrawRectangle(border, r.X, r.Y, r.Width, r.Height);
        using var grid = new Pen(Color.FromArgb(110, Color.White), 1) { DashStyle = DashStyle.Dash };
        for (var i = 1; i <= 2; i++)
        {
            g.DrawLine(grid, r.X + r.Width * i / 3, r.Top, r.X + r.Width * i / 3, r.Bottom);
            g.DrawLine(grid, r.Left, r.Y + r.Height * i / 3, r.Right, r.Y + r.Height * i / 3);
        }
        using var handle = new SolidBrush(Theme.Foreground);
        PointF[] points = [new(r.Left, r.Top), new(r.Right, r.Top), new(r.Right, r.Bottom), new(r.Left, r.Bottom), new(r.Left + r.Width / 2, r.Top), new(r.Left + r.Width / 2, r.Bottom), new(r.Left, r.Top + r.Height / 2), new(r.Right, r.Top + r.Height / 2)];
        foreach (var point in points) { var box = new RectangleF(point.X - 4, point.Y - 4, 8, 8); g.FillRectangle(handle, box); g.DrawRectangle(border, box.X, box.Y, box.Width, box.Height); }
    }
}
