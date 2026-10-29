using System.Drawing;
using System.Drawing.Imaging;
using System.Reflection;
using RapidShot;

internal static class ButtonRenderingTests
{
    public static void Run(Action<bool, string> check, string output)
    {
        using var parent = new Panel { Size = new(420, 90), BackColor = Color.FromArgb(15, 22, 33) };
        using var bar = new FloatingBar { AutoSize = false, Size = new(400, 60), Location = new(10, 10) }; parent.Controls.Add(bar);
        using var selected = new ToolGroupButton(new(EditorTool.Rectangle, "▭", "Фигуры", "R"), new(EditorTool.Ellipse, "○", "Эллипс", "O")) { Selected = true };
        using var normal = new IconButton(UiIcon.Camera, "Снимок") { BackColor = Theme.Accent };
        bar.Controls.AddRange([selected, normal]); parent.CreateControl(); bar.PerformLayout();
        var flags = BindingFlags.Instance | BindingFlags.NonPublic;
        foreach (var button in new IconButton[] { selected, normal })
        {
            foreach (var state in new[] { "normal", "hover", "pressed", "disabled" })
            {
                if (state == "hover") typeof(IconButton).GetMethod("OnMouseEnter", flags)!.Invoke(button, [EventArgs.Empty]);
                if (state == "pressed") typeof(IconButton).GetMethod("OnMouseDown", flags)!.Invoke(button, [new MouseEventArgs(MouseButtons.Left, 1, 12, 12, 0)]);
                button.Enabled = state != "disabled";
                using var image = new Bitmap(button.Width, button.Height); button.DrawToBitmap(image, button.ClientRectangle);
                var clean = true;
                foreach (var p in new[] { new Point(0, 0), new Point(image.Width - 1, 0), new Point(0, image.Height - 1), new Point(image.Width - 1, image.Height - 1) })
                    clean &= image.GetPixel(p.X, p.Y).ToArgb() == Theme.Panel.ToArgb();
                check(clean, $"Углы {button.GetType().Name} в состоянии {state} не содержат белых или посторонних пикселей");
            }
        }
        check(bar.Region == null, "Скругления панелей сглаживаются без ступенчатой оконной маски");
        using var render = new Bitmap(parent.Width, parent.Height); parent.DrawToBitmap(render, parent.ClientRectangle);
        check(render.GetPixel(bar.Left, bar.Top).ToArgb() == parent.BackColor.ToArgb(), "Внешние углы панели совпадают с настоящим фоном родителя");
        render.Save(Path.Combine(output, "button-rendering-preview.png"), ImageFormat.Png);
    }
}
