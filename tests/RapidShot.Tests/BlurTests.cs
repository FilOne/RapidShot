using System.Drawing;
using RapidShot;

internal static class BlurTests
{
    public static void Run(Action<bool, string> check)
    {
        var random = new Random(417);
        using var source = new Bitmap(19, 15);
        for (var y = 0; y < source.Height; y++) for (var x = 0; x < source.Width; x++) source.SetPixel(x, y, Color.FromArgb(random.Next(256), random.Next(256), random.Next(256), random.Next(256)));
        foreach (var region in new[] { new Rectangle(3, 2, 11, 9), new Rectangle(-4, 3, 11, 20), new Rectangle(2, 2, 1, 1) })
        foreach (var radius in new[] { 1, 8, 64 })
        {
            using var actual = (Bitmap)source.Clone(); using var expected = (Bitmap)source.Clone();
            var clipped = Rectangle.Intersect(region, new Rectangle(Point.Empty, source.Size));
            var values = new byte[clipped.Width * clipped.Height * 4];
            for (var y = 0; y < clipped.Height; y++) for (var x = 0; x < clipped.Width; x++)
            {
                var p = expected.GetPixel(x + clipped.X, y + clipped.Y); var index = (y * clipped.Width + x) * 4;
                values[index] = p.B; values[index + 1] = p.G; values[index + 2] = p.R; values[index + 3] = p.A;
            }
            for (var pass = 0; pass < 3; pass++)
            foreach (var horizontal in new[] { true, false })
            {
                var next = new byte[values.Length];
                for (var y = 0; y < clipped.Height; y++) for (var x = 0; x < clipped.Width; x++) for (var channel = 0; channel < 4; channel++)
                {
                    var sum = 0;
                    for (var offset = -radius; offset <= radius; offset++)
                    {
                        var px = horizontal ? Math.Clamp(x + offset, 0, clipped.Width - 1) : x;
                        var py = horizontal ? y : Math.Clamp(y + offset, 0, clipped.Height - 1);
                        sum += values[(py * clipped.Width + px) * 4 + channel];
                    }
                    next[(y * clipped.Width + x) * 4 + channel] = (byte)(sum / (2 * radius + 1));
                }
                values = next;
            }
            for (var y = 0; y < clipped.Height; y++) for (var x = 0; x < clipped.Width; x++)
            {
                var index = (y * clipped.Width + x) * 4;
                expected.SetPixel(x + clipped.X, y + clipped.Y, Color.FromArgb(values[index + 3], values[index + 2], values[index + 1], values[index]));
            }
            ImageBlur.Apply(actual, region, radius); var same = true;
            for (var y = 0; y < source.Height; y++) for (var x = 0; x < source.Width; x++) same &= actual.GetPixel(x, y) == expected.GetPixel(x, y);
            check(same, $"Оптимизированное размытие совпадает с эталоном: область {region.Size}, радиус {radius}, включая альфа-канал");
        }
    }
}
