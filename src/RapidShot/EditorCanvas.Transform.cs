using System.Drawing.Drawing2D;

namespace RapidShot;

internal sealed partial class EditorCanvas
{
    private enum ObjectHandle { None, Move, TopLeft, Top, TopRight, Right, BottomRight, Bottom, BottomLeft, Left, Start, End, Curve }
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
        var b = EditingMagnifierSource ? item.SourceBounds : item.GeometryBounds;
        // Give a dot or a perfectly straight pen stroke a usable selection frame.
        if (b.Width < 1) b.Inflate(.5f, 0);
        if (b.Height < 1) b.Inflate(0, .5f);
        var tl = ToScreen(b.Location); var br = ToScreen(new(b.Right, b.Bottom));
        yield return (ObjectHandle.TopLeft, tl); yield return (ObjectHandle.TopRight, new(br.X, tl.Y));
        yield return (ObjectHandle.BottomRight, br); yield return (ObjectHandle.BottomLeft, new(tl.X, br.Y));
        if (item.Tool is EditorTool.Text or EditorTool.Pin) yield break; // Text and pins scale uniformly from their corners.
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
        ObjectHandle.Start or ObjectHandle.End or ObjectHandle.Curve => Cursors.Cross,
        ObjectHandle.Move => Cursors.SizeAll,
        _ => Cursors.Default
    };
    private void DrawObjectSelection(Graphics g)
    {
        if (Selected == null || Tool is EditorTool.Crop or EditorTool.Eraser || IsEditingText) return;
        var b = EditingMagnifierSource ? Selected.SourceBounds : Selected.GeometryBounds; var tl = ToScreen(b.Location); var br = ToScreen(new(b.Right, b.Bottom));
        using var border = new Pen(Theme.Accent, 1) { DashStyle = DashStyle.Dash };
        DrawMagnifierSource(g);
        if (Selected.Tool == EditorTool.Arrow)
        {
            using var path = ArrowGeometry.Path(Selected);
            using var matrix = new Matrix(zoom, 0, 0, zoom, imageBounds.X - Document.Crop.X * zoom, imageBounds.Y - Document.Crop.Y * zoom);
            path.Transform(matrix); g.DrawPath(border, path);
        }
        else if (Selected.Tool == EditorTool.Line) g.DrawLine(border, ToScreen(Selected.Start), ToScreen(Selected.End));
        else g.DrawRectangle(border, tl.X, tl.Y, Math.Max(1, br.X - tl.X), Math.Max(1, br.Y - tl.Y));
        using var fill = new SolidBrush(Theme.Foreground); using var outline = new Pen(Theme.Accent, 1.5f);
        foreach (var (_, p) in ObjectHandles())
        {
            var box = new RectangleF(p.X - 3.5f, p.Y - 3.5f, 7, 7);
            g.FillRectangle(fill, box); g.DrawRectangle(outline, box.X, box.Y, box.Width, box.Height);
        }
        DrawCurveHandles(g);
    }
    private bool StartHandleDrag(MouseEventArgs e)
    {
        if (e.Clicks > 1 && Selected?.Tool == EditorTool.Text) return false;
        var handle = HitObjectHandle(e.Location);
        if (handle == ObjectHandle.None) return StartCurveDrag(e);
        StartObjectTransform(handle, ToImage(e.Location)); return true;
    }
    private void StartObjectTransform(ObjectHandle handle, PointF point)
    {
        if (Selected == null) return;
        lastPenStrokeAt = 0;
        transformOriginal = Selected.Clone(); transformHandle = handle; transformChanged = false;
        transformMagnifierSource = EditingMagnifierSource;
        ClearTransformPreview(); anchor = point; last = point; dragging = true; Capture = true;
        pointerPosition = point;
        Cursor = ObjectCursor(handle);
        HintChanged?.Invoke(handle == ObjectHandle.Move ? Strings["MovingHint"] : Strings["ResizeHint"]);
        if (shiftConstraint && handle is not (ObjectHandle.Move or ObjectHandle.Curve)) MoveObjectTransform(point, true);
    }
    private void MoveObjectTransform(PointF point, bool force = false)
    {
        if (transformOriginal == null || Selected == null || (!force && point == last)) return;
        var original = transformOriginal; var item = original.Clone();
        var dx = point.X - anchor.X; var dy = point.Y - anchor.Y;
        if (item.Tool == EditorTool.Magnifier) TransformMagnifier(item, original, dx, dy);
        else if (transformHandle == ObjectHandle.Curve) MoveCurvePoint(item, original, dx, dy);
        else if (transformHandle == ObjectHandle.Move)
        {
            item.Start = original.Start; item.End = original.End; item.Points = [.. original.Points]; item.Offset(dx, dy);
        }
        else if (transformHandle == ObjectHandle.Start) item.Start = new(original.Start.X + dx, original.Start.Y + dy);
        else if (transformHandle == ObjectHandle.End) item.End = new(original.End.X + dx, original.End.Y + dy);
        else if (item.Tool == EditorTool.Text) ResizeText(item, original, dx, dy);
        else if (item.Tool == EditorTool.Pin) ResizePin(item, original, dx, dy);
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
            var target = RectangleF.FromLTRB(l, t, r, d);
            if (shiftConstraint) target = ConstrainResize(b, target, left, right, top, bottom, item.Tool is EditorTool.Rectangle or EditorTool.Ellipse);
            l = target.Left; r = target.Right; t = target.Top; d = target.Bottom;
            PointF Map(PointF p) => new(b.Width > .001f ? l + (p.X - b.X) * (r - l) / b.Width : (l + r) / 2,
                b.Height > .001f ? t + (p.Y - b.Y) * (d - t) / b.Height : (t + d) / 2);
            if (shiftConstraint && item.Tool == EditorTool.Ellipse) { item.EllipseAngle = 0; item.Start = target.Location; item.End = new(target.Right, target.Bottom); }
            else if (item.Tool == EditorTool.Ellipse && original.EllipseAngle != 0) EllipseGeometry.Resize(item, original, target);
            else { item.Start = Map(original.Start); item.End = Map(original.End); }
            if (item.Tool == EditorTool.Pen) item.Points = original.Points.Select(Map).ToList();
        }
        last = point;
        if (Selected.Start == item.Start && Selected.End == item.End && Selected.EllipseAngle == item.EllipseAngle && Selected.FontSize == item.FontSize && Selected.PinSize == item.PinSize && Selected.SourceBounds == item.SourceBounds && Selected.Magnification == item.Magnification && Selected.CurvePoints.SequenceEqual(item.CurvePoints) && Selected.Points.SequenceEqual(item.Points)) return;
        if (!transformChanged) { Document.BeginChange(); transformChanged = true; }
        Selected.Start = item.Start; Selected.End = item.End; Selected.Points = item.Points;
        Selected.CurvePoints = item.CurvePoints; Selected.SourceBounds = item.SourceBounds; Selected.Magnification = item.Magnification;
        Selected.EllipseAngle = item.EllipseAngle; Selected.FontSize = item.FontSize; Selected.PinSize = item.PinSize;
        Document.NotifyChanged();
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
    private void ResizePin(Annotation item, Annotation original, float dx, float dy)
    {
        var b = original.Bounds;
        var left = transformHandle is ObjectHandle.TopLeft or ObjectHandle.BottomLeft;
        var top = transformHandle is ObjectHandle.TopLeft or ObjectHandle.TopRight;
        var vx = left ? -b.Width : b.Width; var vy = top ? -b.Height : b.Height;
        var scale = ((vx + dx) * vx + (vy + dy) * vy) / Math.Max(1, vx * vx + vy * vy);
        var size = Math.Clamp(original.PinSize * scale, 16, 160);
        var dimensions = PinMarker.Measure(item.PinNumber, size); item.PinSize = size;
        item.Start = new(left ? b.Right - dimensions.Width : b.Left, top ? b.Bottom - dimensions.Height : b.Top);
        item.End = new(item.Start.X + dimensions.Width, item.Start.Y + dimensions.Height);
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
