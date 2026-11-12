namespace RapidShot;

internal sealed partial class EditorCanvas
{
    private int activeCurvePoint = -1, transformCurveIndex = -1;
    private bool insertingCurvePoint;
    private Guid? curveOwner;
    public bool CanRemoveCurvePoint => Selected is { Tool: EditorTool.Arrow } arrow && curveOwner == arrow.Id && activeCurvePoint >= 0 && activeCurvePoint < arrow.CurvePoints.Count;

    private IEnumerable<(int Index, PointF Position, bool Insert)> CurveHandles()
    {
        if (Selected is not { Tool: EditorTool.Arrow } arrow || Tool is EditorTool.Eraser or EditorTool.Crop) yield break;
        for (var i = 0; i < arrow.CurvePoints.Count; i++) yield return (i, ToScreen(arrow.CurvePoints[i]), false);
        var segments = ArrowGeometry.Segments(arrow);
        for (var i = 0; i < segments.Count; i++) yield return (i, ToScreen(segments[i].At(.5f)), true);
    }
    private (int Index, bool Insert)? HitCurveHandle(Point point)
    {
        foreach (var handle in CurveHandles())
            if (ArrowGeometry.Distance(point, handle.Position) <= (handle.Insert ? 6 : 8)) return (handle.Index, handle.Insert);
        return null;
    }
    private void DrawCurveHandles(Graphics g)
    {
        using var outline = new Pen(Theme.Accent, 1.4f); using var fill = new SolidBrush(Theme.Panel); using var active = new SolidBrush(Theme.Accent);
        foreach (var handle in CurveHandles())
        {
            var p = handle.Position; var size = handle.Insert ? 7 : 9;
            g.FillEllipse(!handle.Insert && curveOwner == Selected?.Id && activeCurvePoint == handle.Index ? active : fill, p.X - size / 2f, p.Y - size / 2f, size, size);
            g.DrawEllipse(outline, p.X - size / 2f, p.Y - size / 2f, size, size);
            if (handle.Insert) { g.DrawLine(outline, p.X - 1.5f, p.Y, p.X + 1.5f, p.Y); g.DrawLine(outline, p.X, p.Y - 1.5f, p.X, p.Y + 1.5f); }
        }
    }
    private bool StartCurveDrag(MouseEventArgs e)
    {
        if (Selected is not { Tool: EditorTool.Arrow } arrow) return false;
        var handle = HitCurveHandle(e.Location);
        if ((handle == null || handle.Value.Insert) && e.Clicks > 1 && arrow.HitTest(ToImage(e.Location), 5 / zoom))
        {
            var nearest = ArrowGeometry.Nearest(arrow, ToImage(e.Location));
            var nodes = new[] { arrow.Start }.Concat(arrow.CurvePoints).Append(arrow.End);
            if (nodes.Any(p => ArrowGeometry.Distance(p, nearest.Point) < 8 / zoom)) return false;
            Document.BeginChange(); arrow.CurvePoints.Insert(nearest.Index, nearest.Point); Document.NotifyChanged();
            activeCurvePoint = nearest.Index; curveOwner = arrow.Id;
            StartObjectTransform(ObjectHandle.Curve, ToImage(e.Location));
            transformCurveIndex = nearest.Index; insertingCurvePoint = false; transformChanged = true;
        }
        else if (handle is { } hit)
        {
            StartObjectTransform(ObjectHandle.Curve, ToImage(e.Location));
            transformCurveIndex = hit.Index; insertingCurvePoint = hit.Insert;
            if (!hit.Insert) { activeCurvePoint = hit.Index; curveOwner = arrow.Id; }
        }
        else return false;
        SelectionChanged?.Invoke();
        HintChanged?.Invoke(Strings["ArrowEditHint"]);
        return true;
    }
    private void MoveCurvePoint(Annotation item, Annotation original, float dx, float dy)
    {
        if (transformCurveIndex < 0) return;
        if (insertingCurvePoint)
        {
            if (Math.Abs(dx) + Math.Abs(dy) < .1f) return;
            var midpoint = ArrowGeometry.Segments(original)[transformCurveIndex].At(.5f);
            item.CurvePoints.Insert(transformCurveIndex, new(midpoint.X + dx, midpoint.Y + dy));
        }
        else
        {
            var p = original.CurvePoints[transformCurveIndex]; item.CurvePoints[transformCurveIndex] = new(p.X + dx, p.Y + dy);
        }
        activeCurvePoint = transformCurveIndex; curveOwner = item.Id;
    }
    public void AddCurvePoint()
    {
        FinishObjectTransform();
        if (Selected is not { Tool: EditorTool.Arrow } arrow) return;
        var segments = ArrowGeometry.Segments(arrow);
        var index = Enumerable.Range(0, segments.Count).MaxBy(i => ArrowGeometry.Distance(segments[i].Start, segments[i].End));
        Document.BeginChange(); arrow.CurvePoints.Insert(index, segments[index].At(.5f));
        activeCurvePoint = index; curveOwner = arrow.Id; Document.NotifyChanged(); SelectionChanged?.Invoke();
        HintChanged?.Invoke(Strings["ArrowPointAdded"]);
    }
    public void RemoveCurvePoint()
    {
        FinishObjectTransform();
        if (!CanRemoveCurvePoint || Selected == null) return;
        Document.BeginChange(); Selected.CurvePoints.RemoveAt(activeCurvePoint); activeCurvePoint = -1;
        Document.NotifyChanged(); SelectionChanged?.Invoke();
    }
    public void StraightenArrow()
    {
        FinishObjectTransform();
        if (Selected is not { Tool: EditorTool.Arrow } arrow || arrow.CurvePoints.Count == 0) return;
        Document.BeginChange(); arrow.CurvePoints.Clear(); activeCurvePoint = -1;
        Document.NotifyChanged(); SelectionChanged?.Invoke();
    }
}
