using System.Drawing.Drawing2D;

namespace RapidShot;

internal sealed partial class EditorCanvas
{
    private enum ObjectHandle { None, Move, TopLeft, Top, TopRight, Right, BottomRight, Bottom, BottomLeft, Left, Start, End }
    private Annotation? transformOriginal;
    private ObjectHandle transformHandle;
    private bool transformChanged;

    private PointF ToScreen(PointF p) => new(imageBounds.X + (p.X - Document.Crop.X) * zoom, imageBounds.Y + (p.Y - Document.Crop.Y) * zoom);
    private IEnumerable<(ObjectHandle Handle, PointF Point)> ObjectHandles()
    {
        if (Selected == null || IsEditingText || Tool is EditorTool.Crop or EditorTool.Eraser || (Selected.Tool == EditorTool.Text && Tool == EditorTool.Text)) yield break;
        var item = Selected;
        if (item.Tool is EditorTool.Arrow or EditorTool.Line)
        {
            yield return (ObjectHandle.Start, ToScreen(item.Start)); yield return (ObjectHandle.End, ToScreen(item.End)); yield break;
        }
        var b = item.GeometryBounds;
        // Give a dot or a perfectly straight pen stroke a usable selection frame.
        if (b.Width < 1) b.Inflate(.5f, 0);
        if (b.Height < 1) b.Inflate(0, .5f);
        var tl = ToScreen(b.Location); var br = ToScreen(new(b.Right, b.Bottom));
        yield return (ObjectHandle.TopLeft, tl); yield return (ObjectHandle.TopRight, new(br.X, tl.Y));
        yield return (ObjectHandle.BottomRight, br); yield return (ObjectHandle.BottomLeft, new(tl.X, br.Y));
        if (item.Tool == EditorTool.Text) yield break; // Text scales uniformly from its corners.
        yield return (ObjectHandle.Top, new((tl.X + br.X) / 2, tl.Y)); yield return (ObjectHandle.Right, new(br.X, (tl.Y + br.Y) / 2));
        yield return (ObjectHandle.Bottom, new((tl.X + br.X) / 2, br.Y)); yield return (ObjectHandle.Left, new(tl.X, (tl.Y + br.Y) / 2));
    }
    private ObjectHandle HitObjectHandle(Point point)
    {
        var closest = ObjectHandle.None; var distance = 75f;
        foreach (var (handle, p) in ObjectHandles())
        {
            var d = (point.X - p.X) * (point.X - p.X) + (point.Y - p.Y) * (point.Y - p.Y);
            if (Math.Abs(point.X - p.X) <= 7 && Math.Abs(point.Y - p.Y) <= 7 && d < distance) { closest = handle; distance = d; }
        }
        return closest;
    }
    private static Cursor ObjectCursor(ObjectHandle handle) => handle switch
    {
        ObjectHandle.TopLeft or ObjectHandle.BottomRight => Cursors.SizeNWSE,
        ObjectHandle.TopRight or ObjectHandle.BottomLeft => Cursors.SizeNESW,
        ObjectHandle.Left or ObjectHandle.Right => Cursors.SizeWE,
        ObjectHandle.Top or ObjectHandle.Bottom => Cursors.SizeNS,
        ObjectHandle.Start or ObjectHandle.End => Cursors.Cross,
        ObjectHandle.Move => Cursors.SizeAll,
        _ => Cursors.Default
    };
    private void DrawObjectSelection(Graphics g)
    {
        if (Selected == null || Tool is EditorTool.Crop or EditorTool.Eraser || IsEditingText) return;
        var b = Selected.GeometryBounds; var tl = ToScreen(b.Location); var br = ToScreen(new(b.Right, b.Bottom));
        using var border = new Pen(Theme.Accent, 1) { DashStyle = DashStyle.Dash };
        if (Selected.Tool is EditorTool.Arrow or EditorTool.Line) g.DrawLine(border, ToScreen(Selected.Start), ToScreen(Selected.End));
        else g.DrawRectangle(border, tl.X, tl.Y, Math.Max(1, br.X - tl.X), Math.Max(1, br.Y - tl.Y));
        using var fill = new SolidBrush(Theme.Foreground); using var outline = new Pen(Theme.Accent, 1.5f);
        foreach (var (_, p) in ObjectHandles())
        {
            var box = new RectangleF(p.X - 3.5f, p.Y - 3.5f, 7, 7);
            g.FillRectangle(fill, box); g.DrawRectangle(outline, box.X, box.Y, box.Width, box.Height);
        }
    }
    private bool StartHandleDrag(MouseEventArgs e)
    {
        if (e.Clicks > 1 && Selected?.Tool == EditorTool.Text) return false;
        var handle = HitObjectHandle(e.Location);
        if (handle == ObjectHandle.None) return false;
        StartObjectTransform(handle, ToImage(e.Location)); return true;
    }
    private void StartObjectTransform(ObjectHandle handle, PointF point)
    {
        if (Selected == null) return;
        lastPenStrokeAt = 0;
        transformOriginal = Selected.Clone(); transformHandle = handle; transformChanged = false;
        ClearTransformPreview(); anchor = point; last = point; dragging = true; Capture = true;
        Cursor = ObjectCursor(handle);
        HintChanged?.Invoke(handle == ObjectHandle.Move ? "Перемещение объекта · Ctrl+Z — отменить." : "Потяните маркер, чтобы изменить размер · Ctrl+Z — отменить.");
    }
    private void MoveObjectTransform(PointF point)
    {
        if (transformOriginal == null || Selected == null || point == last) return;
        var original = transformOriginal; var item = Selected;
        if (!transformChanged) { Document.BeginChange(); transformChanged = true; }
        var dx = point.X - anchor.X; var dy = point.Y - anchor.Y;
        if (transformHandle == ObjectHandle.Move)
        {
            item.Start = original.Start; item.End = original.End; item.Points = [.. original.Points]; item.Offset(dx, dy);
        }
        else if (transformHandle == ObjectHandle.Start) item.Start = new(original.Start.X + dx, original.Start.Y + dy);
        else if (transformHandle == ObjectHandle.End) item.End = new(original.End.X + dx, original.End.Y + dy);
        else if (item.Tool == EditorTool.Text) ResizeText(item, original, dx, dy);
        else
        {
            var b = original.GeometryBounds;
            var left = transformHandle is ObjectHandle.Left or ObjectHandle.TopLeft or ObjectHandle.BottomLeft;
            var right = transformHandle is ObjectHandle.Right or ObjectHandle.TopRight or ObjectHandle.BottomRight;
            var top = transformHandle is ObjectHandle.Top or ObjectHandle.TopLeft or ObjectHandle.TopRight;
            var bottom = transformHandle is ObjectHandle.Bottom or ObjectHandle.BottomLeft or ObjectHandle.BottomRight;
            var l = left ? Math.Min(b.Left + dx, b.Right - 2) : b.Left;
            var r = right ? Math.Max(b.Right + dx, b.Left + 2) : b.Right;
            var t = top ? Math.Min(b.Top + dy, b.Bottom - 2) : b.Top;
            var d = bottom ? Math.Max(b.Bottom + dy, b.Top + 2) : b.Bottom;
            PointF Map(PointF p) => new(b.Width > .001f ? l + (p.X - b.X) * (r - l) / b.Width : (l + r) / 2,
                b.Height > .001f ? t + (p.Y - b.Y) * (d - t) / b.Height : (t + d) / 2);
            item.Start = Map(original.Start); item.End = Map(original.End);
            if (item.Tool == EditorTool.Pen) item.Points = original.Points.Select(Map).ToList();
        }
        last = point; Document.NotifyChanged();
    }
    private void ResizeText(Annotation item, Annotation original, float dx, float dy)
    {
        var b = original.Bounds;
        var left = transformHandle is ObjectHandle.TopLeft or ObjectHandle.BottomLeft;
        var top = transformHandle is ObjectHandle.TopLeft or ObjectHandle.TopRight;
        var vx = left ? -b.Width : b.Width; var vy = top ? -b.Height : b.Height;
        var scale = ((vx + dx) * vx + (vy + dy) * vy) / Math.Max(1, vx * vx + vy * vy);
        item.FontSize = Math.Clamp(original.FontSize * scale, 10, 160);
        item.Start = original.Start; MeasureText(item);
        var size = item.Bounds.Size;
        item.Start = new(left ? b.Right - size.Width : b.Left, top ? b.Bottom - size.Height : b.Top);
        item.End = new(item.Start.X + size.Width, item.Start.Y + size.Height);
    }
    private void FinishObjectTransform()
    {
        if (transformOriginal == null) return;
        transformOriginal = null; transformHandle = ObjectHandle.None;
        ClearTransformPreview(); dragging = false; Capture = false;
        if (transformChanged) { transformChanged = false; SelectionChanged?.Invoke(); }
        Invalidate();
    }
}
