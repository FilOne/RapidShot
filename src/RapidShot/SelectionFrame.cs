using System.Drawing.Drawing2D;

namespace RapidShot;

internal static class SelectionFrame
{
    public static void Draw(Graphics g, RectangleF box)
    {
        using var border = new Pen(Color.White, 1) { DashStyle = DashStyle.Dash }; g.DrawRectangle(border, box.X, box.Y, box.Width, box.Height);
        using var handle = new Pen(Color.White, 4) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round };
        const float length = 12;
        g.DrawLines(handle, new PointF[] { new(box.Left, box.Top + length), new(box.Left, box.Top), new(box.Left + length, box.Top) });
        g.DrawLines(handle, new PointF[] { new(box.Right - length, box.Top), new(box.Right, box.Top), new(box.Right, box.Top + length) });
        g.DrawLines(handle, new PointF[] { new(box.Right, box.Bottom - length), new(box.Right, box.Bottom), new(box.Right - length, box.Bottom) });
        g.DrawLines(handle, new PointF[] { new(box.Left + length, box.Bottom), new(box.Left, box.Bottom), new(box.Left, box.Bottom - length) });
        g.DrawLine(handle, box.Left + box.Width / 2 - 5, box.Top, box.Left + box.Width / 2 + 5, box.Top);
        g.DrawLine(handle, box.Left + box.Width / 2 - 5, box.Bottom, box.Left + box.Width / 2 + 5, box.Bottom);
        g.DrawLine(handle, box.Left, box.Top + box.Height / 2 - 5, box.Left, box.Top + box.Height / 2 + 5);
        g.DrawLine(handle, box.Right, box.Top + box.Height / 2 - 5, box.Right, box.Top + box.Height / 2 + 5);
    }
}
