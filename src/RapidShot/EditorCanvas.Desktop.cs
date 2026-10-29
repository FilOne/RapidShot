namespace RapidShot;

internal sealed partial class EditorCanvas
{
    private bool desktopCoordinates;
    private Bitmap? desktopBackdrop;
    internal RectangleF DisplayedCaptureBounds => Tool == EditorTool.Crop ? CropScreenBounds : imageBounds;
    internal void UseDesktopCoordinates()
    {
        desktopCoordinates = true;
        desktopBackdrop = new Bitmap(Document.Original.Width, Document.Original.Height);
        using (var g = Graphics.FromImage(desktopBackdrop))
        {
            g.DrawImageUnscaled(Document.Original, Point.Empty);
            using var shade = new SolidBrush(Color.FromArgb(155, 10, 14, 24)); g.FillRectangle(shade, new Rectangle(Point.Empty, desktopBackdrop.Size));
        }
        Rebuild();
    }
    protected override void OnPaintBackground(PaintEventArgs e)
    {
        if (!desktopCoordinates) base.OnPaintBackground(e);
    }
}
