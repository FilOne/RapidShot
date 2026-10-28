using System.Drawing;
using System.Drawing.Imaging;
using System.Reflection;
using RapidShot;

internal static class StyleControlsTests
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    public static void Run(Action<bool, string> check, string output)
    {
        using (var source = new Bitmap(260, 180))
        {
            using (var g = Graphics.FromImage(source)) g.Clear(Color.White);
            using var doc = new ScreenshotDocument(new Bitmap(source));
            var line = new Annotation { Tool = EditorTool.Line, Start = new(20, 80), End = new(230, 80), Color = Color.Red, Width = 8, Opacity = 50 };
            doc.Add(line);
            using (var render = doc.Render())
            {
                var pixel = render.GetPixel(100, 80);
                check(pixel.R == 255 && pixel.G is >= 120 and <= 135, "Полупрозрачная линия смешивается с исходным изображением в экспорте");
                render.Save(Path.Combine(output, "opacity-preview.png"), ImageFormat.Png);
            }
            check(line.Clone().Opacity == 50, "История сохраняет прозрачность пометки");
            line.Opacity = 0;
            using (var render = doc.Render()) check(render.GetPixel(100, 80).ToArgb() == Color.White.ToArgb(), "Прозрачность 100 % полностью скрывает линию");
            line.Opacity = 100;
            using (var render = doc.Render()) check(render.GetPixel(100, 80).G == 0, "Прозрачность 0 % даёт непрозрачную линию");
        }
        using var form = new EditorForm(TrayApplication.CreateDemo(), new AppSettings());
        using var content = form.Controls[0]; content.Font = form.Font; content.BackColor = form.BackColor; content.ForeColor = form.ForeColor;
        form.Controls.Remove(content); content.Dock = DockStyle.None; content.Size = form.ClientSize; content.CreateControl(); content.PerformLayout();
        T Field<T>(string name) => (T)typeof(EditorForm).GetField(name, Private)!.GetValue(form)!;
        void Select(EditorTool tool) => typeof(EditorForm).GetMethod("SelectTool", Private)!.Invoke(form, [tool]);
        var font = Field<NumericUpDown>("fontSize"); var canvas = Field<EditorCanvas>("canvas"); var document = Field<ScreenshotDocument>("document");
        var width = Field<ParameterSlider>("width"); var transparency = Field<ParameterSlider>("transparency");
        check(!font.Visible, "Размер текста скрыт при рисовании");
        Select(EditorTool.Text); check(font.Visible, "Размер текста появляется с инструментом «Текст»");
        Select(EditorTool.Rectangle); check(!font.Visible, "Размер текста скрывается при выборе другого инструмента");
        check(width.Parent!.Parent!.Parent!.AccessibleName == "Параметры инструмента" && transparency.Parent!.Parent!.Parent!.AccessibleName == "Параметры инструмента", "Оба ползунка расположены в нижней панели параметров");
        transparency.Value = 40; width.Value = 9;
        check(canvas.Opacity == 60 && canvas.StrokeWidth == 9, "Ползунки передают толщину и прозрачность в инструмент");
        Select(EditorTool.Line);
        void Mouse(string name, int x, int y) => typeof(EditorCanvas).GetMethod(name, Private)!.Invoke(canvas, [new MouseEventArgs(MouseButtons.Left, 1, x, y, 0)]);
        using var paint = new Bitmap(canvas.Width, canvas.Height); canvas.DrawToBitmap(paint, canvas.ClientRectangle);
        var bounds = (RectangleF)typeof(EditorCanvas).GetField("imageBounds", Private)!.GetValue(canvas)!;
        var x1 = (int)(bounds.Left + bounds.Width * .2f); var x2 = (int)(bounds.Left + bounds.Width * .7f); var y = (int)(bounds.Top + bounds.Height * .35f); var middle = (x1 + x2) / 2;
        Mouse("OnMouseDown", x1, y); Mouse("OnMouseMove", x2, y); Mouse("OnMouseUp", x2, y);
        check(document.Items.Single().Opacity == 60 && document.Items[0].Width == 9, "Новая линия получает выбранные параметры");
        Select(EditorTool.Select); Mouse("OnMouseDown", middle, y); Mouse("OnMouseUp", middle, y);
        typeof(ParameterSlider).GetMethod("BeginInteraction", Private)!.Invoke(transparency, null);
        transparency.Value = 50; transparency.Value = 70; transparency.Value = 80;
        typeof(ParameterSlider).GetMethod("EndInteraction", Private)!.Invoke(transparency, null);
        check(document.Items.Single().Opacity == 20, "Прозрачность выбранной линии меняется ползунком");
        document.Undo(); check(document.Items.Single().Opacity == 60, "Одно перетягивание ползунка отменяется одним действием");
        Mouse("OnMouseDown", middle, y); Mouse("OnMouseUp", middle, y);
        typeof(EditorForm).GetMethod("SetColor", Private)!.Invoke(form, [Color.Blue]);
        check(document.Items.Single().Opacity == 60 && document.Items[0].Color == Color.Blue, "Изменение цвета сохраняет прозрачность линии");
        using var bitmap = new Bitmap(content.Width, content.Height); content.DrawToBitmap(bitmap, content.ClientRectangle);
        bitmap.Save(Path.Combine(output, "style-controls-preview.png"), ImageFormat.Png);
    }
}
