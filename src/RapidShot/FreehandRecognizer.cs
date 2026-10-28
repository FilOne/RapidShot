namespace RapidShot;

internal static class FreehandRecognizer
{
    internal sealed record Result(Annotation Item, int PreviousStrokes);
    public static Result? Recognize(Annotation stroke, IReadOnlyList<Annotation> previous)
    {
        if (stroke.Tool != EditorTool.Pen || stroke.Points.Count < 2) return null;
        if (Circle(stroke) is { } circle) return new(circle, 0);
        if (Arrow([stroke]) is { } arrow) return new(arrow, 0);
        // A shaft and a V-shaped head may be drawn with two or three consecutive strokes.
        for (var count = 1; count <= Math.Min(2, previous.Count); count++)
        {
            var group = previous.Skip(previous.Count - count).Append(stroke).ToArray();
            if (group.Any(x => x.Tool != EditorTool.Pen || x.Color != stroke.Color || x.Width != stroke.Width || x.Opacity != stroke.Opacity)) break;
            if (Arrow(group) is { } combined) return new(combined, count);
        }
        return null;
    }
    private static Annotation? Circle(Annotation stroke)
    {
        var b = stroke.GeometryBounds;
        if (stroke.Points.Count < 8 || Math.Min(b.Width, b.Height) < Math.Max(18, stroke.Width * 4) || b.Width / b.Height is < .75f or > 1.33f) return null;
        var center = new PointF(b.X + b.Width / 2, b.Y + b.Height / 2);
        var points = Resample(stroke.Points);
        var radius = points.Average(p => Distance(p, center));
        if (Distance(points[0], points[^1]) > radius * .35f) return null;
        var errors = points.Select(p => (Distance(p, center) - radius) / radius).ToArray();
        if (Math.Sqrt(errors.Average(e => e * e)) > .095 || errors.Max(e => Math.Abs(e)) > .23) return null;
        var length = Length(points);
        if (length < Math.Tau * radius * .8 || length > Math.Tau * radius * 1.3) return null;
        var turn = 0d; var travel = 0d;
        for (var i = 1; i < points.Count; i++)
        {
            var angle = Math.Atan2(points[i].Y - center.Y, points[i].X - center.X) - Math.Atan2(points[i - 1].Y - center.Y, points[i - 1].X - center.X);
            angle = Math.Atan2(Math.Sin(angle), Math.Cos(angle)); turn += angle; travel += Math.Abs(angle);
        }
        if (Math.Abs(turn) < Math.PI * 1.75 || travel > Math.Abs(turn) + .65) return null;
        var result = stroke.Clone(); result.Tool = EditorTool.Ellipse; result.Points.Clear();
        result.Start = new(center.X - radius, center.Y - radius); result.End = new(center.X + radius, center.Y + radius);
        return result;
    }
    private static Annotation? Arrow(IReadOnlyList<Annotation> strokes)
    {
        var paths = strokes.Select(x => Resample(x.Points)).Where(x => x.Count > 1).ToArray();
        if (paths.Length != strokes.Count) return null;
        var all = paths.SelectMany(x => x).ToArray();
        var extent = Math.Max(all.Max(p => p.X) - all.Min(p => p.X), all.Max(p => p.Y) - all.Min(p => p.Y));
        var segments = new List<(PointF A, PointF B)>();
        foreach (var path in paths)
        {
            var simplified = Simplify(path, Math.Max(1.5f, extent * .035f));
            for (var i = 1; i < simplified.Count; i++) segments.Add((simplified[i - 1], simplified[i]));
        }
        if (segments.Count is < 3 or > 14) return null;
        var shaft = segments.MaxBy(s => Distance(s.A, s.B));
        foreach (var (start, tip) in new[] { shaft, (shaft.B, shaft.A) })
        {
            var length = Distance(start, tip);
            if (length < Math.Max(35, strokes[^1].Width * 6)) continue;
            var ux = (tip.X - start.X) / length; var uy = (tip.Y - start.Y) / length;
            var wings = new List<(PointF Point, float Along, float Side)>();
            foreach (var (a, b) in segments)
            {
                PointF? wing = Distance(a, tip) < length * .09f ? b : Distance(b, tip) < length * .09f ? a : null;
                if (!wing.HasValue) continue;
                var dx = wing.Value.X - tip.X; var dy = wing.Value.Y - tip.Y;
                var along = -(dx * ux + dy * uy); var side = dx * -uy + dy * ux;
                if (along > length * .08f && along < length * .45f && Math.Abs(side) > length * .07f && Math.Abs(side) < length * .4f)
                    wings.Add((wing.Value, along, side));
            }
            foreach (var first in wings.Where(w => w.Side > 0))
            foreach (var second in wings.Where(w => w.Side < 0))
            {
                if (first.Along / second.Along is < .45f or > 2.2f || Math.Abs(first.Side / second.Side) is < .4f or > 2.5f) continue;
                var tolerance = Math.Max(2.5f, length * .055f);
                if (all.Any(p => Math.Min(SegmentDistance(p, start, tip), Math.Min(SegmentDistance(p, tip, first.Point), SegmentDistance(p, tip, second.Point))) > tolerance)) continue;
                if (paths.Sum(Length) > length + (Distance(tip, first.Point) + Distance(tip, second.Point)) * 2.6f) continue;
                var result = strokes[^1].Clone(); result.Tool = EditorTool.Arrow;
                result.Start = start; result.End = tip; result.DoubleHeaded = false; result.Points.Clear();
                return result;
            }
        }
        return null;
    }
    private static List<PointF> Resample(IReadOnlyList<PointF> points)
    {
        if (points.Count < 2) return [.. points];
        var length = Length(points); if (length < .01f) return [points[0]];
        var result = new List<PointF> { points[0] }; var step = length / 128; var covered = 0f; var next = step;
        for (var i = 1; i < points.Count; i++)
        {
            var a = points[i - 1]; var b = points[i]; var segment = Distance(a, b);
            while (next <= covered + segment && result.Count < 129)
            {
                var t = segment < .001f ? 0 : (next - covered) / segment;
                result.Add(new(a.X + (b.X - a.X) * t, a.Y + (b.Y - a.Y) * t)); next += step;
            }
            covered += segment;
        }
        result.Add(points[^1]); return result;
    }
    private static List<PointF> Simplify(IReadOnlyList<PointF> points, float tolerance)
    {
        var kept = new SortedSet<int> { 0, points.Count - 1 }; var stack = new Stack<(int Start, int End)>(); stack.Push((0, points.Count - 1));
        while (stack.TryPop(out var pair))
        {
            var furthest = -1; var distance = tolerance;
            for (var i = pair.Start + 1; i < pair.End; i++)
            {
                var d = SegmentDistance(points[i], points[pair.Start], points[pair.End]);
                if (d > distance) { distance = d; furthest = i; }
            }
            if (furthest < 0) continue;
            kept.Add(furthest); stack.Push((pair.Start, furthest)); stack.Push((furthest, pair.End));
        }
        return kept.Select(i => points[i]).ToList();
    }
    private static float Length(IReadOnlyList<PointF> points)
    {
        var length = 0f; for (var i = 1; i < points.Count; i++) length += Distance(points[i - 1], points[i]); return length;
    }
    private static float Distance(PointF a, PointF b) => MathF.Sqrt((a.X - b.X) * (a.X - b.X) + (a.Y - b.Y) * (a.Y - b.Y));
    private static float SegmentDistance(PointF p, PointF a, PointF b)
    {
        var dx = b.X - a.X; var dy = b.Y - a.Y;
        var t = Math.Clamp(((p.X - a.X) * dx + (p.Y - a.Y) * dy) / Math.Max(.001f, dx * dx + dy * dy), 0, 1);
        return Distance(p, new(a.X + dx * t, a.Y + dy * t));
    }
}
