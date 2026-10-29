namespace RapidShot;

internal static partial class FreehandRecognizer
{
    private sealed record ArrowHead(PointF Tip, PointF Left, PointF Right);

    private static Annotation? Arrow(IReadOnlyList<Annotation> strokes)
    {
        var paths = strokes.Select(x => Resample(x.Points)).Where(x => x.Count > 1).ToArray();
        if (paths.Length != strokes.Count) return null;
        var all = paths.SelectMany(x => x).ToArray();
        var extent = Math.Max(all.Max(p => p.X) - all.Min(p => p.X), all.Max(p => p.Y) - all.Min(p => p.Y));
        if (extent < Math.Max(24, strokes[^1].Width * 4)) return null;
        var vertices = paths.SelectMany(p => Simplify(p, Math.Max(1.2f, extent * .018f))).Distinct().ToArray();
        if (vertices.Length is < 4 or > 64) return null;
        // A wavy shaft may simplify into many short segments. Fit its entire span instead.
        var axes = new List<(PointF A, PointF B, float Length)>();
        for (var i = 0; i < vertices.Length; i++) for (var j = i + 1; j < vertices.Length; j++)
        {
            var length = Distance(vertices[i], vertices[j]);
            if (length >= extent * .65f) axes.Add((vertices[i], vertices[j], length));
        }
        foreach (var axis in axes.OrderByDescending(x => x.Length).Take(48))
        foreach (var (start, tip) in new[] { (axis.A, axis.B), (axis.B, axis.A) })
        {
            var length = axis.Length;
            var head = FindArrowHead(paths, start, tip, length, strokes[^1].Width);
            if (head == null) continue;
            var otherHead = FindArrowHead(paths, tip, start, length, strokes[^1].Width);
            if (!FitsArrow(paths, all, start, tip, head, otherHead, length, strokes[^1].Width)) continue;
            var result = strokes[^1].Clone(); result.Tool = EditorTool.Arrow;
            result.Start = start; result.End = tip; result.DoubleHeaded = otherHead != null; result.Points.Clear();
            return result;
        }
        return null;
    }

    private static ArrowHead? FindArrowHead(IReadOnlyList<List<PointF>> paths, PointF tail, PointF tip, float length, float width)
    {
        var ux = (tip.X - tail.X) / length; var uy = (tip.Y - tail.Y) / length;
        // Small junctions preserve short heads; larger ones tolerate imperfect joins and gaps.
        foreach (var scale in new[] { .025f, .05f, .08f })
        {
            var radius = Math.Max(2.5f, Math.Max(Math.Min(width, length * .04f), length * scale));
            PointF? left = null, right = null; var leftSide = 0f; var rightSide = 0f;
            foreach (var path in paths)
            {
                var i = 0;
                while (i < path.Count)
                {
                    if (Distance(path[i], tip) <= radius) { i++; continue; }
                    var first = i;
                    while (i < path.Count && Distance(path[i], tip) > radius) i++;
                    var last = i - 1;
                    if (first == 0 && i == path.Count) continue; // No connection to this tip.
                    var furthest = 0f;
                    for (var n = first; n <= last; n++) furthest = Math.Max(furthest, Distance(path[n], tip));
                    if (furthest > length * .68f) continue; // The shaft is not a head branch.
                    for (var n = first; n <= last; n++)
                    {
                        var p = path[n]; var dx = p.X - tip.X; var dy = p.Y - tip.Y;
                        var back = -(dx * ux + dy * uy); var side = dx * -uy + dy * ux;
                        if (back < length * .02f || back > length * .58f || Math.Abs(side) > length * .55f
                            || Math.Abs(side) < Math.Max(width * .8f, length * .025f) || Math.Abs(side) < back * .22f) continue;
                        if (side > leftSide) { leftSide = side; left = p; }
                        if (side < rightSide) { rightSide = side; right = p; }
                    }
                }
            }
            if (left.HasValue && right.HasValue) return new(tip, left.Value, right.Value);
        }
        return null;
    }

    private static bool FitsArrow(IReadOnlyList<List<PointF>> paths, IReadOnlyList<PointF> all, PointF start, PointF tip,
        ArrowHead head, ArrowHead? otherHead, float length, float width)
    {
        var ux = (tip.X - start.X) / length; var uy = (tip.Y - start.Y) / length;
        float Projection(PointF p) => (p.X - start.X) * ux + (p.Y - start.Y) * uy;
        var projection = all.Select(Projection).Order().ToArray();
        if (projection[0] < -length * .12f || projection[^1] > length * 1.12f) return false;
        for (var i = 1; i < projection.Length; i++) if (projection[i] - projection[i - 1] > length * .13f) return false;
        if (!paths.Any(p => p.Max(Projection) - p.Min(Projection) > length * .5f)) return false;
        var squaredError = 0d;
        foreach (var point in all)
        {
            var error = Math.Min(SegmentDistance(point, start, tip), HeadDistance(point, head));
            if (otherHead != null) error = Math.Min(error, HeadDistance(point, otherHead));
            if (error > Math.Max(width * 1.5f, length * .14f)) return false;
            squaredError += error * error;
        }
        if (Math.Sqrt(squaredError / all.Count) > Math.Max(width, length * .065f)) return false;
        var headLength = Distance(tip, head.Left) + Distance(tip, head.Right);
        if (otherHead != null) headLength += Distance(start, otherHead.Left) + Distance(start, otherHead.Right);
        return paths.Sum(Length) < length * 1.6f + headLength * 3;
    }

    private static float HeadDistance(PointF p, ArrowHead head)
    {
        // Also accept a closed triangular head or a head with curved, uneven sides.
        static float Cross(PointF a, PointF b, PointF c) => (b.X - a.X) * (c.Y - a.Y) - (b.Y - a.Y) * (c.X - a.X);
        var a = Cross(head.Tip, head.Left, p); var b = Cross(head.Left, head.Right, p); var c = Cross(head.Right, head.Tip, p);
        if ((a >= 0 && b >= 0 && c >= 0) || (a <= 0 && b <= 0 && c <= 0)) return 0;
        return Math.Min(SegmentDistance(p, head.Tip, head.Left), Math.Min(SegmentDistance(p, head.Tip, head.Right), SegmentDistance(p, head.Left, head.Right)));
    }
}
