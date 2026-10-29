using System.Drawing;
using System.Drawing.Imaging;
using System.Reflection;
using RapidShot;

internal static class MenuIconTests
{
    public static void Run(Action<bool, string> check, string output)
    {
        using var form = new EditorForm(TrayApplication.CreateDemo(), new AppSettings());
        var menu = (ContextMenuStrip)typeof(EditorForm).GetField("captureMenu", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(form)!;
        foreach (ToolStripMenuItem item in menu.Items)
        {
            check(item.Image is Bitmap, $"Пункт «{item.Text}» содержит изображение иконки");
            var icon = (Bitmap)item.Image!; var ink = 0; var transparent = 0;
            for (var y = 0; y < icon.Height; y++) for (var x = 0; x < icon.Width; x++)
                if (icon.GetPixel(x, y).A > 30) ink++; else transparent++;
            check(ink > 20 && transparent > icon.Width * icon.Height / 2 && icon.GetPixel(0, 0).A == 0,
                $"Иконка «{item.Text}» имеет прозрачный фон вместо белого квадрата");
        }
        check(menu.Renderer is MinimalMenuRenderer renderer && renderer.ColorTable.ImageMarginGradientBegin == Theme.Panel && renderer.ColorTable.ImageMarginGradientEnd == Theme.Panel,
            "Фон поля иконок меню соответствует тёмной панели");
        menu.PerformLayout(); menu.Size = menu.GetPreferredSize(Size.Empty);
        using var painted = new Bitmap(menu.Width, menu.Height); menu.DrawToBitmap(painted, menu.ClientRectangle); painted.Save(Path.Combine(output, "capture-menu-preview.png"), ImageFormat.Png);

        using var atlas = new Bitmap(240, 134); using var g = Graphics.FromImage(atlas); g.Clear(Theme.Panel); g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        for (var row = 0; row < 2; row++)
        {
            var color = row == 0 ? Theme.Foreground : Theme.Disabled;
            UiGlyph.Draw(g, UiIcon.Undo, new RectangleF(16, 14 + row * 60, 20, 20), color, color);
            UiGlyph.Draw(g, UiIcon.Redo, new RectangleF(56, 14 + row * 60, 20, 20), color, color);
            UiGlyph.Draw(g, UiIcon.Undo, new RectangleF(104, 8 + row * 60, 40, 40), color, color);
            UiGlyph.Draw(g, UiIcon.Redo, new RectangleF(168, 8 + row * 60, 40, 40), color, color);
        }
        atlas.Save(Path.Combine(output, "history-icons-preview.png"), ImageFormat.Png);
    }
}
