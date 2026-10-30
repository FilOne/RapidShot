using System.Drawing.Drawing2D;

namespace RapidShot;

internal static class ArrowGeometry
{
    internal readonly record struct Segment(PointF Start, PointF Control1, PointF Control2, PointF End)
    {
        internal PointF At(float t)
        {
            var u = 1 - t;
            return new(u * u * u * Start.X + 3 * u * u * t * Control1.X + 3 * u * t * t * Control2.X + t * t * t * End.X,
                u * u * u * Start.Y + 3 * u * u * t * Control1.Y + 3 * u * t * t * Control2.Y + t * t * t * End.Y);
        }
    }
    internal static List<Segment> Segments(Annotation item)
    {
        PointF[] nodes = [item.Start, .. item.CurvePoints, item.End];
        List<Segment> result = [];
        for (var i = 0; i < nodes.Length - 1; i++)
        {
            var a = nodes[i]; var b = nodes[i + 1];
            var before = i > 0 ? nodes[i - 1] : a; var after = i + 2 < nodes.Length ? nodes[i + 2] : b;
            var length = Distance(a, b);
            // Chord-weighted tangents keep unevenly spaced nodes smooth without large overshoots.
            var k1 = length / (3 * Math.Max(.001f, Distance(before, a) + length));
            var k2 = length / (3 * Math.Max(.001f, length + Distance(b, after)));
            result.Add(new(a, new(a.X + (b.X - before.X) * k1, a.Y + (b.Y - before.Y) * k1),
                new(b.X - (after.X - a.X) * k2, b.Y - (after.Y - a.Y) * k2), b));
        }
        return result;
    }
    internal static GraphicsPath Path(Annotation item)
    {
        var path = new GraphicsPath();
        foreach (var segment in Segments(item)) path.AddBezier(segment.Start, segment.Control1, segment.Control2, segment.End);
        return path;
    }
    internal static RectangleF Bounds(Annotation item) { using var path = Path(item); return path.GetBounds(); }
    internal static (int Index, PointF Point, float Distance) Nearest(Annotation item, PointF point)
    {
        var segments = Segments(item); var best = float.MaxValue; var index = 0; var position = item.Start;
        for (var i = 0; i < segments.Count; i++)
        {
            var s = segments[i]; var steps = Math.Clamp((int)(Distance(s.Start, s.Control1) + Distance(s.Control1, s.Control2) + Distance(s.Control2, s.End)) / 4, 16, 256);
            var previous = s.Start;
            for (var step = 1; step <= steps; step++)
            {
                var next = s.At(step / (float)steps); var dx = next.X - previous.X; var dy = next.Y - previous.Y;
                var t = Math.Clamp(((point.X - previous.X) * dx + (point.Y - previous.Y) * dy) / Math.Max(.0001f, dx * dx + dy * dy), 0, 1);
                var p = s.At((step - 1 + t) / steps); var distance = Distance(point, p);
                if (distance < best) { best = distance; index = i; position = p; }
                previous = next;
            }
        }
        return (index, position, best);
    }
    internal static float Distance(PointF a, PointF b) => MathF.Sqrt((a.X - b.X) * (a.X - b.X) + (a.Y - b.Y) * (a.Y - b.Y));
}
