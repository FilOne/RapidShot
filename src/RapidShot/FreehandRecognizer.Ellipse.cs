namespace RapidShot;

internal static partial class FreehandRecognizer
{
    private static Annotation? Ellipse(Annotation stroke)
    {
        var bounds = stroke.GeometryBounds;
        if (stroke.Points.Count < 8 || Math.Min(bounds.Width, bounds.Height) < Math.Max(12, stroke.Width * 2.5f)) return null;
        var points = Resample(stroke.Points);
        var origin = new PointF(bounds.X + bounds.Width / 2, bounds.Y + bounds.Height / 2);
        var scale = Math.Max(bounds.Width, bounds.Height) / 2;
        var matrix = new double[5, 6];
        foreach (var p in points)
        {
            double x = (p.X - origin.X) / scale, y = (p.Y - origin.Y) / scale;
            double[] row = [x * x, x * y, y * y, x, y];
            for (var i = 0; i < 5; i++)
            {
                for (var j = 0; j < 5; j++) matrix[i, j] += row[i] * row[j];
                matrix[i, 5] += row[i];
            }
        }
        // Fit a conic to the entire contour instead of relying on its most extreme pixels.
        var coefficients = SolveConic(matrix);
        if (coefficients == null) return null;
        var a = coefficients[0]; var b = coefficients[1]; var c = coefficients[2]; var d = coefficients[3]; var e = coefficients[4];
        var determinant = 4 * a * c - b * b;
        if (a <= 0 || c <= 0 || determinant <= 1e-8) return null;
        var cx = (b * e - 2 * c * d) / determinant; var cy = (b * d - 2 * a * e) / determinant;
        var k = 1 - (a * cx * cx + b * cx * cy + c * cy * cy + d * cx + e * cy);
        var delta = Math.Sqrt((a - c) * (a - c) + b * b);
        var low = (a + c - delta) / 2; var high = (a + c + delta) / 2;
        if (k <= 0 || low <= 1e-8) return null;
        var rx = Math.Sqrt(k / low) * scale; var ry = Math.Sqrt(k / high) * scale;
        if (!double.IsFinite(rx) || rx / ry > 10 || ry < Math.Max(5, stroke.Width) || rx > scale * 1.5) return null;
        var angle = .5 * Math.Atan2(b, a - c) + Math.PI / 2; var cos = Math.Cos(angle); var sin = Math.Sin(angle);
        var center = new PointF((float)(origin.X + cx * scale), (float)(origin.Y + cy * scale));
        var normalized = points.Select(p =>
        {
            var x = p.X - center.X; var y = p.Y - center.Y;
            return new PointF((float)((x * cos + y * sin) / rx), (float)((-x * sin + y * cos) / ry));
        }).ToList();
        if (Distance(normalized[0], normalized[^1]) > .48f) return null;
        var errors = normalized.Select(p => Math.Sqrt(p.X * p.X + p.Y * p.Y) - 1).ToArray();
        if (Math.Sqrt(errors.Average(x => x * x)) > .15 || errors.Max(x => Math.Abs(x)) > .36) return null;
        var turn = 0d; var travel = 0d;
        for (var i = 1; i < normalized.Count; i++)
        {
            var difference = Math.Atan2(normalized[i].Y, normalized[i].X) - Math.Atan2(normalized[i - 1].Y, normalized[i - 1].X);
            difference = Math.Atan2(Math.Sin(difference), Math.Cos(difference)); turn += difference; travel += Math.Abs(difference);
        }
        if (Math.Abs(turn) < Math.PI * 1.78 || Math.Abs(turn) > Math.PI * 2.28 || travel > Math.Abs(turn) + 1.1 || Length(normalized) > 10) return null;
        var contour = Simplify(normalized, .065f);
        // Reject straight-sided polygons even when their radial error resembles a rough circle.
        var straightSides = 0;
        for (var i = 1; i < contour.Count; i++) if (Distance(contour[i - 1], contour[i]) > 1.1f) straightSides++;
        if (straightSides >= 3) return null;
        var result = stroke.Clone(); result.Tool = EditorTool.Ellipse; result.Points.Clear();
        if (rx / ry <= 1.22) { rx = ry = (rx + ry) / 2; angle = 0; }
        var degrees = (float)(angle * 180 / Math.PI);
        while (degrees >= 90) degrees -= 180;
        while (degrees < -90) degrees += 180;
        if (Math.Abs(degrees) < 3) degrees = 0;
        if (Math.Abs(Math.Abs(degrees) - 90) < 3) { (rx, ry) = (ry, rx); degrees = 0; }
        result.EllipseAngle = degrees;
        result.Start = new(center.X - (float)rx, center.Y - (float)ry); result.End = new(center.X + (float)rx, center.Y + (float)ry);
        return result;
    }

    private static double[]? SolveConic(double[,] matrix)
    {
        for (var column = 0; column < 5; column++)
        {
            var pivot = column;
            for (var row = column + 1; row < 5; row++) if (Math.Abs(matrix[row, column]) > Math.Abs(matrix[pivot, column])) pivot = row;
            if (Math.Abs(matrix[pivot, column]) < 1e-9) return null;
            for (var j = column; j <= 5; j++) (matrix[column, j], matrix[pivot, j]) = (matrix[pivot, j], matrix[column, j]);
            var divisor = matrix[column, column];
            for (var j = column; j <= 5; j++) matrix[column, j] /= divisor;
            for (var row = 0; row < 5; row++)
            {
                if (row == column) continue;
                var factor = matrix[row, column];
                for (var j = column; j <= 5; j++) matrix[row, j] -= factor * matrix[column, j];
            }
        }
        return Enumerable.Range(0, 5).Select(i => matrix[i, 5]).ToArray();
    }
}
