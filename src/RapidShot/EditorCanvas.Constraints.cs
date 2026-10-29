namespace RapidShot;

internal sealed partial class EditorCanvas
{
    private readonly Func<Keys> readModifiers;
    private bool shiftConstraint;
    private PointF pointerPosition;

    private bool ReadShiftConstraint()
    {
        var next = (readModifiers() & Keys.Shift) != 0; var changed = next != shiftConstraint;
        shiftConstraint = next; return changed;
    }
    private void RefreshShiftConstraint()
    {
        if (!ReadShiftConstraint() || !dragging) return;
        if (transformOriginal != null) MoveObjectTransform(pointerPosition, true);
        else if (pending != null) { pending.End = DrawingEnd(pointerPosition); Invalidate(); }
    }
    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.KeyCode is Keys.ShiftKey or Keys.LShiftKey or Keys.RShiftKey) RefreshShiftConstraint();
    }
    protected override void OnKeyUp(KeyEventArgs e)
    {
        base.OnKeyUp(e);
        if (e.KeyCode is Keys.ShiftKey or Keys.LShiftKey or Keys.RShiftKey) RefreshShiftConstraint();
    }
    private PointF DrawingEnd(PointF point)
    {
        if (!shiftConstraint || pending?.Tool is not (EditorTool.Rectangle or EditorTool.Ellipse)) return point;
        var dx = point.X - anchor.X; var dy = point.Y - anchor.Y;
        var sx = dx < 0 ? -1 : 1; var sy = dy < 0 ? -1 : 1;
        var size = Math.Max(Math.Abs(dx), Math.Abs(dy));
        var maxX = sx > 0 ? Document.Crop.Right - anchor.X : anchor.X - Document.Crop.Left;
        var maxY = sy > 0 ? Document.Crop.Bottom - anchor.Y : anchor.Y - Document.Crop.Top;
        size = Math.Min(size, Math.Min(maxX, maxY));
        return new(anchor.X + size * sx, anchor.Y + size * sy);
    }
    private RectangleF ConstrainResize(RectangleF original, RectangleF raw, bool left, bool right, bool top, bool bottom, bool square)
    {
        if (original.Width < .001f || original.Height < .001f) return raw;
        var horizontal = left || right; var vertical = top || bottom;
        var ratio = square ? 1 : original.Width / original.Height;
        float w, h;
        if (horizontal && vertical)
        {
            h = (raw.Width * ratio + raw.Height) / (ratio * ratio + 1); w = h * ratio;
        }
        else if (horizontal) { w = raw.Width; h = w / ratio; }
        else { h = raw.Height; w = h * ratio; }
        var cx = original.X + original.Width / 2; var cy = original.Y + original.Height / 2;
        var fixedX = left ? original.Right : right ? original.Left : cx;
        var fixedY = top ? original.Bottom : bottom ? original.Top : cy;
        var crop = Document.Crop;
        var maxW = left ? fixedX - crop.Left : right ? crop.Right - fixedX : 2 * Math.Min(cx - crop.Left, crop.Right - cx);
        var maxH = top ? fixedY - crop.Top : bottom ? crop.Bottom - fixedY : 2 * Math.Min(cy - crop.Top, crop.Bottom - cy);
        var minH = Math.Max(2, 2 / ratio); var maxHeight = Math.Max(minH, Math.Min(maxH, maxW / ratio));
        h = Math.Clamp(h, minH, maxHeight); w = h * ratio;
        var x = left ? fixedX - w : right ? fixedX : cx - w / 2;
        var y = top ? fixedY - h : bottom ? fixedY : cy - h / 2;
        return new(x, y, w, h);
    }
}
