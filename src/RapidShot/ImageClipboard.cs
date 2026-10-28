using System.Drawing.Imaging;

namespace RapidShot;

internal static class ImageClipboard
{
    public static void Write(Bitmap image)
    {
        using var png = new MemoryStream(); image.Save(png, ImageFormat.Png); png.Position = 0;
        var data = new DataObject(); data.SetData(DataFormats.Bitmap, true, image); data.SetData("PNG", false, png);
        Clipboard.SetDataObject(data, true, 5, 100);
    }
}
