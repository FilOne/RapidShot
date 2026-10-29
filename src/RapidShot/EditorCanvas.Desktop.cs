namespace RapidShot;

internal sealed partial class EditorCanvas
{
    private bool desktopCoordinates;
    private Bitmap? desktopBackdrop;
    private Rectangle? captureResizeDraft;
    private Rectangle captureResizeStart;
    private Point captureResizeAnchor;
    private CropEdge captureResizeEdge;
    internal bool IsResizingCapture => captureResizeEdge != CropEdge.None;
    internal RectangleF DisplayedCaptureBounds => captureResizeDraft ?? (Tool == EditorTool.Crop ? CropScreenBounds : imageBounds);
    internal void UseDesktopCoordinates()
    {
        desktopCoordinates = true;
        if (Tool == EditorTool.Crop) Tool = EditorTool.Pen;
        desktopBackdrop = new Bitmap(Document.Original.Width, Document.Original.Height);
        using (var g = Graphics.FromImage(desktopBackdrop))
        {
            g.DrawImageUnscaled(Document.Original, Point.Empty);
            using var shade = new SolidBrush(Color.FromArgb(155, 10, 14, 24)); g.FillRectangle(shade, new Rectangle(Point.Empty, desktopBackdrop.Size));
        }
        Rebuild();
    }
    protected override void OnPaintBackground(PaintEventArgs e)
    {
        if (!desktopCoordinates) base.OnPaintBackground(e);
    }
    private bool StartCaptureResize(MouseEventArgs e)
    {
        if (!desktopCoordinates || Tool == EditorTool.Crop) return false;
        var edge = HitCropEdge(e.Location);
        if (edge == CropEdge.None) return false;
        CommitText(); FinishObjectTransform();
        captureResizeStart = Document.Crop; captureResizeDraft = Document.Crop;
        captureResizeAnchor = e.Location; captureResizeEdge = edge; Capture = true;
        Cursor = CropCursor(edge); Invalidate(); return true;
    }
    private void MoveCaptureResize(Point point)
    {
        if (!IsResizingCapture) return;
        var dx = point.X - captureResizeAnchor.X; var dy = point.Y - captureResizeAnchor.Y;
        var r = captureResizeStart; var size = Document.Original.Size;
        var left = (captureResizeEdge & CropEdge.Left) != 0 ? Math.Clamp(r.Left + dx, 0, r.Right - 2) : r.Left;
        var right = (captureResizeEdge & CropEdge.Right) != 0 ? Math.Clamp(r.Right + dx, r.Left + 2, size.Width) : r.Right;
        var top = (captureResizeEdge & CropEdge.Top) != 0 ? Math.Clamp(r.Top + dy, 0, r.Bottom - 2) : r.Top;
        var bottom = (captureResizeEdge & CropEdge.Bottom) != 0 ? Math.Clamp(r.Bottom + dy, r.Top + 2, size.Height) : r.Bottom;
        var next = Rectangle.FromLTRB(left, top, right, bottom);
        if (next == captureResizeDraft) return;
        captureResizeDraft = next; CropStateChanged?.Invoke(); Invalidate();
    }
    private bool CommitCaptureResize()
    {
        if (!captureResizeDraft.HasValue) return false;
        var area = captureResizeDraft.Value;
        captureResizeDraft = null; captureResizeEdge = CropEdge.None; Capture = false;
        var changed = Document.ResizeCaptureRegion(area);
        CropStateChanged?.Invoke(); Invalidate();
        if (changed) HintChanged?.Invoke($"Область: {area.Width} × {area.Height} px · Ctrl+Z — вернуть размер");
        return changed;
    }
    internal bool CancelCaptureResize()
    {
        if (!captureResizeDraft.HasValue) return false;
        captureResizeDraft = null; captureResizeEdge = CropEdge.None; Capture = false;
        CropStateChanged?.Invoke(); Invalidate(); return true;
    }
}
