using System.Drawing;
using System.Drawing.Imaging;
using System.Reflection;
using RapidShot;

internal static class CaptureActionTests
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    private static T Field<T>(EditorForm form, string name) => (T)typeof(EditorForm).GetField(name, Private)!.GetValue(form)!;
    private static void Select(EditorForm form, EditorTool tool) => typeof(EditorForm).GetMethod("SelectTool", Private)!.Invoke(form, [tool]);
    private static void Mouse(EditorCanvas canvas, string name, Point point) => typeof(EditorCanvas).GetMethod(name, Private)!.Invoke(canvas, [new MouseEventArgs(MouseButtons.Left, 1, point.X, point.Y, 0)]);

    public static void Run(Action<bool, string> check, string output)
    {
        // Test independent native button actions without writing to the clipboard.
        using (var split = new CaptureSplitButton())
        {
            var copied = 0; var opened = 0;
            split.CopyRequested += () => copied++; split.MenuRequested += () => opened++;
            void Click(Button part) => part.GetType().GetMethod("OnClick", Private)!.Invoke(part, [EventArgs.Empty]);
            Click(split.PrimaryAction);
            check(copied == 1 && opened == 0 && split.PrimaryAction.Text == "Захват", "Левая часть «Захват» выполняет копирование без открытия меню");
            Click(split.MenuAction);
            check(copied == 1 && opened == 1 && split.PrimaryAction.Right == split.MenuAction.Left && split.MenuAction.Right == split.Width,
                "Стрелка имеет отдельную область нажатия и открывает только меню");
            foreach (var size in new[] { new Size(124, 34), new Size(186, 51) })
            {
                split.Size = size;
                using var image = new Bitmap(split.Width, split.Height); split.DrawToBitmap(image, split.ClientRectangle);
                check(image.GetPixel(0, 0).ToArgb() == Theme.Panel.ToArgb() && image.GetPixel(image.Width - 1, image.Height - 1).ToArgb() == Theme.Panel.ToArgb(),
                    "Разделённая кнопка сохраняет закрашенные углы при изменении размера");
            }
        }

        using var form = new EditorForm(TrayApplication.CreateDemo(), new AppSettings());
        using var content = form.Controls[0]; content.Font = form.Font; content.BackColor = form.BackColor; content.ForeColor = form.ForeColor;
        form.Controls.Remove(content); content.Dock = DockStyle.None; content.Size = form.ClientSize; content.CreateControl(); content.PerformLayout();
        var menu = Field<ContextMenuStrip>(form, "captureMenu");
        var bar = Field<FloatingBar>(form, "toolBar"); var parameters = Field<FlowLayoutPanel>(form, "parameterBar");
        var capture = Field<CaptureSplitButton>(form, "capture"); var canvas = Field<EditorCanvas>(form, "canvas"); var doc = Field<ScreenshotDocument>(form, "document");
        check(menu.Items.Count == 2 && menu.Items[0] == Field<ToolStripMenuItem>(form, "save") && menu.Items[1] == Field<ToolStripMenuItem>(form, "upload"),
            "Меню стрелки содержит сохранение и загрузку на Yapx");
        check(bar.Controls.OfType<IconButton>().All(b => b.Symbol is not (UiIcon.Save or UiIcon.Copy or UiIcon.Upload)) && capture.PrimaryAction == Field<Button>(form, "copy"),
            "Сохранение, копирование и Yapx не дублируются отдельными кнопками панели");
        using var paint = new Bitmap(content.Width, content.Height); content.DrawToBitmap(paint, content.ClientRectangle);
        paint.Save(Path.Combine(output, "capture-actions-preview.png"), ImageFormat.Png);
        var canvasHeight = canvas.Height;
        Select(form, EditorTool.Select); content.PerformLayout();
        check(!parameters.Visible && canvas.Height == canvasHeight,
            "Без выделения «Выбор» скрывает пустую панель без смещения снимка");
        var item = new Annotation { Tool = EditorTool.Rectangle, Start = new(250, 160), End = new(650, 420), Color = Color.Red };
        doc.Add(item); canvas.DrawToBitmap(paint, canvas.ClientRectangle);
        var bounds = (RectangleF)typeof(EditorCanvas).GetField("imageBounds", Private)!.GetValue(canvas)!;
        var p = Point.Round(new PointF(bounds.X + item.Start.X * bounds.Width / doc.Crop.Width, bounds.Y + 250 * bounds.Height / doc.Crop.Height));
        Mouse(canvas, "OnMouseDown", p); Mouse(canvas, "OnMouseUp", p);
        check(canvas.Selected?.Id == item.Id && parameters.Visible && Field<FlowLayoutPanel>(form, "strokeControls").Visible,
            "После выбора объекта возвращаются параметры его инструмента");
        canvas.ClearSelection(); content.PerformLayout();
        check(!parameters.Visible, "Снятие выделения снова скрывает панель параметров");
        content.DrawToBitmap(paint, content.ClientRectangle); paint.Save(Path.Combine(output, "select-empty-preview.png"), ImageFormat.Png);
        Select(form, EditorTool.Pen);
        check(parameters.Visible, "Переход с пустого выбора на карандаш возвращает параметры рисования");

        using var overlay = new EditorForm(TrayApplication.CreateDemo(), new AppSettings(), new Rectangle(0, 0, 1280, 760));
        typeof(EditorForm).GetMethod("OnMouseDown", Private)!.Invoke(overlay, [new MouseEventArgs(MouseButtons.Left, 1, 180, 100, 0)]);
        typeof(EditorForm).GetMethod("OnMouseUp", Private)!.Invoke(overlay, [new MouseEventArgs(MouseButtons.Left, 1, 1080, 530, 0)]);
        var regionCanvas = Field<EditorCanvas>(overlay, "canvas"); overlay.Controls.Remove(regionCanvas); regionCanvas.Dock = DockStyle.None; regionCanvas.Size = overlay.ClientSize; regionCanvas.CreateControl();
        Select(overlay, EditorTool.Select);
        var regionBar = Field<FloatingBar>(overlay, "toolBar"); var status = Field<Label>(overlay, "status");
        check(!Field<FlowLayoutPanel>(overlay, "parameterBar").Visible && status.Top - regionBar.Bottom == 6,
            "На экране захвата пустая панель выбора исчезает без оставшегося промежутка");
        Select(overlay, EditorTool.Rectangle); regionCanvas.SetInputEnabled(false);
        Mouse(regionCanvas, "OnMouseDown", new(300, 180)); Mouse(regionCanvas, "OnMouseMove", new(450, 300)); Mouse(regionCanvas, "OnMouseUp", new(450, 300));
        check(Field<CaptureSplitButton>(overlay, "capture").MenuAction.Enabled && regionCanvas.Enabled && Field<ScreenshotDocument>(overlay, "document").Items.Count == 0,
            "Блокировка рисования при отправке сохраняет доступ к меню отмены");
        regionCanvas.SetInputEnabled(true); regionCanvas.Dispose();
    }
}
