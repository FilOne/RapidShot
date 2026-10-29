using System.Drawing.Drawing2D;

namespace RapidShot;

internal static class EllipseGeometry
{
    public static GraphicsPath Path(Annotation item)
    {
        var path = new GraphicsPath();
        if (item.Bounds.Width <= 0 || item.Bounds.Height <= 0) return path;
        path.AddEllipse(item.Bounds);
        if (item.EllipseAngle != 0)
        {
            using var rotation = new Matrix(); rotation.RotateAt(item.EllipseAngle, Center(item)); path.Transform(rotation);
        }
        return path;
    }
    public static PointF Center(Annotation item) => new(item.Bounds.X + item.Bounds.Width / 2, item.Bounds.Y + item.Bounds.Height / 2);
    private static (double XX, double YY, double XY) Covariance(Annotation item)
    {
        var angle = item.EllipseAngle * Math.PI / 180; var cos = Math.Cos(angle); var sin = Math.Sin(angle);
        var a = Math.Pow(item.Bounds.Width / 2, 2); var b = Math.Pow(item.Bounds.Height / 2, 2);
        return (a * cos * cos + b * sin * sin, a * sin * sin + b * cos * cos, (a - b) * cos * sin);
    }
    public static RectangleF Bounds(Annotation item)
    {
        var (xx, yy, _) = Covariance(item); var center = Center(item); var w = (float)Math.Sqrt(xx); var h = (float)Math.Sqrt(yy);
        return new(center.X - w, center.Y - h, w * 2, h * 2);
    }
    public static void Resize(Annotation item, Annotation original, RectangleF target)
    {
        var bounds = Bounds(original); var sx = target.Width / bounds.Width; var sy = target.Height / bounds.Height;
        var (xx, yy, xy) = Covariance(original); xx *= sx * sx; yy *= sy * sy; xy *= sx * sy;
        var delta = Math.Sqrt((xx - yy) * (xx - yy) + 4 * xy * xy);
        var a = (float)Math.Sqrt((xx + yy + delta) / 2); var b = (float)Math.Sqrt(Math.Max(.0001, (xx + yy - delta) / 2));
        var center = new PointF(target.X + target.Width / 2, target.Y + target.Height / 2);
        item.Start = new(center.X - a, center.Y - b); item.End = new(center.X + a, center.Y + b);
        item.EllipseAngle = (float)(.5 * Math.Atan2(2 * xy, xx - yy) * 180 / Math.PI);
    }
}
