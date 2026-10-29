namespace RapidShot;

internal static partial class FreehandRecognizer
{
    internal sealed record Result(Annotation Item, int PreviousStrokes);
    public static Result? Recognize(Annotation stroke, IReadOnlyList<Annotation> previous)
    {
        if (stroke.Tool != EditorTool.Pen || stroke.Points.Count < 2) return null;
        if (Ellipse(stroke) is { } ellipse) return new(ellipse, 0);
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
