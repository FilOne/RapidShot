using System.Drawing;
using System.Drawing.Imaging;
using System.Reflection;
using RapidShot;

internal static class RegionCaptureTests
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    private static T Field<T>(object target, string name) => (T)typeof(EditorForm).GetField(name, Private)!.GetValue(target)!;
    private static void Mouse(Control control, string name, int x, int y) => control.GetType().GetMethod(name, Private)!.Invoke(control, [new MouseEventArgs(MouseButtons.Left, 1, x, y, 0)]);

    public static void Run(Action<bool, string> check, string output)
    {
        using var form = new EditorForm(TrayApplication.CreateDemo(), new AppSettings(), new Rectangle(0, 0, 1280, 760));
        var handle = form.Handle;
        var before = Application.OpenForms.Count;
        Mouse(form, "OnMouseDown", 180, 100); Mouse(form, "OnMouseMove", 1080, 530); Mouse(form, "OnMouseUp", 1080, 530);
        var canvas = Field<EditorCanvas>(form, "canvas"); var doc = Field<ScreenshotDocument>(form, "document");
        check(form.Handle == handle && Application.OpenForms.Count == before && form.DialogResult == DialogResult.None,
            "Отпускание рамки сохраняет то же окно захвата без отдельного редактора");
        check(form.FormBorderStyle == FormBorderStyle.None && !form.ShowInTaskbar && form.Bounds == new Rectangle(0, 0, 1280, 760),
            "Захват области остаётся поверх рабочего стола без заголовка и кнопки в панели задач");
        check(doc.Crop == new Rectangle(180, 100, 900, 430) && !doc.CanUndo, "Первое выделение задаёт исходную область без отмены до полного рабочего стола");
        // Render only detached controls: no desktop input, foreground window or clipboard changes.
        form.Controls.Remove(canvas); canvas.Dock = DockStyle.None; canvas.Size = form.ClientSize;
        canvas.Font = form.Font; canvas.CreateControl(); canvas.PerformLayout();
        typeof(EditorForm).GetMethod("PositionRegionBars", Private)!.Invoke(form, null);
        var bar = Field<FloatingBar>(form, "toolBar"); var parameters = Field<FlowLayoutPanel>(form, "parameterBar");
        var tools = Field<Dictionary<EditorTool, ToolGroupButton>>(form, "tools");
        check(!tools.ContainsKey(EditorTool.Crop) && !bar.Controls.OfType<ToolGroupButton>().Any(button => button.Symbol == UiIcon.Crop),
            "В режиме области на панели нет инструмента обрезки");
        typeof(EditorForm).GetMethod("SelectTool", Private)!.Invoke(form, [EditorTool.Crop]);
        var message = Message.Create(IntPtr.Zero, 0, IntPtr.Zero, IntPtr.Zero);
        typeof(EditorForm).GetMethod("ProcessCmdKey", Private)!.Invoke(form, [message, Keys.C]);
        canvas.ActivateTool(EditorTool.Crop); canvas.Tool = EditorTool.Crop;
        check(canvas.Tool == EditorTool.Pen && !Field<FlowLayoutPanel>(form, "cropControls").Visible,
            "Клавиша C и смена инструмента не включают обрезку в режиме области");
        check(bar.Parent == canvas && parameters.Parent == canvas && bar.Top > doc.Crop.Bottom && bar.Bottom < parameters.Top,
            "Инструменты и параметры находятся непосредственно под выделенной областью");
        check(bar.Right <= canvas.Width && bar.Left >= 0 && parameters.Bottom <= canvas.Height, "Обе панели целиком видны на экране");
        using var preview = new Bitmap(canvas.Width, canvas.Height); canvas.DrawToBitmap(preview, canvas.ClientRectangle);
        check(preview.GetPixel(800, 300).ToArgb() == doc.Original.GetPixel(800, 300).ToArgb()
            && preview.GetPixel(120, 300).GetBrightness() < doc.Original.GetPixel(120, 300).GetBrightness(), "Область отображается в исходном масштабе, рабочий стол снаружи затемнён");
        canvas.Tool = EditorTool.Rectangle; canvas.Ink = Color.Red;
        Mouse(canvas, "OnMouseDown", 300, 180); Mouse(canvas, "OnMouseMove", 480, 350); Mouse(canvas, "OnMouseUp", 480, 350);
        check(doc.Items.Count == 1 && doc.Items[0].Start == new PointF(300, 180) && doc.Items[0].End == new PointF(480, 350), "Пометки не смещаются и не масштабируются после выбора области");
        using (var exported = doc.Render())
        {
            check(exported.Size == doc.Crop.Size && exported.GetPixel(120, 140).R > 220 && exported.GetPixel(120, 140).G < 40,
                "Экспорт содержит только выбранную область и пометки в правильных координатах");
        }
        doc.Undo(); check(doc.Items.Count == 0 && doc.Crop == new Rectangle(180, 100, 900, 430) && !doc.CanUndo, "Отмена первой пометки сохраняет исходную область захвата"); doc.Redo();
        canvas.Tool = EditorTool.Text; Mouse(canvas, "OnMouseDown", 600, 220);
        var input = canvas.Controls.OfType<TextBox>().Single(); input.Text = "Прямо на экране"; canvas.CommitText();
        check(doc.Items.Last().Tool == EditorTool.Text && doc.Items.Last().Start == new PointF(600, 220), "Ввод текста на экране захвата остаётся встроенным в снимок");
        canvas.DrawToBitmap(preview, canvas.ClientRectangle); preview.Save(Path.Combine(output, "region-editor-preview.png"), ImageFormat.Png);
        canvas.Tool = EditorTool.Select; Mouse(canvas, "OnMouseDown", 1080, 315); Mouse(canvas, "OnMouseMove", 1000, 315); Mouse(canvas, "OnMouseUp", 1000, 315);
        check(doc.Crop == new Rectangle(180, 100, 820, 430) && canvas.DisplayedCaptureBounds == doc.Crop, "Изменение рамки области доступно без инструмента обрезки и сохраняет положение пикселей");
        doc.Undo(); check(doc.Crop == new Rectangle(180, 100, 900, 430), "Отмена изменения области возвращает исходную рамку на экране");

        var rows = new[] { new Size(700, 46), new Size(550, 50), new Size(700, 22) };
        var below = RegionPanelPlacement.Place(new(500, 200, 900, 400), new(0, 0, 1920, 1080), rows);
        check(below[0].Top > 600 && below[^1].Bottom < 1080, "При наличии места панели располагаются под снимком");
        var above = RegionPanelPlacement.Place(new(500, 700, 900, 370), new(0, 0, 1920, 1080), rows);
        check(above[^1].Bottom < 700 && above[0].Top > 0, "У нижнего края панели перемещаются над снимком");
        var negative = RegionPanelPlacement.Place(new(-1880, -900, 100, 100), new(-1920, -1080, 1920, 1080), rows);
        check(negative.All(r => new Rectangle(-1920, -1080, 1920, 1080).Contains(r)), "Панели учитывают мониторы с отрицательными координатами и узкое выделение");
        var full = RegionPanelPlacement.Place(new(0, 0, 1280, 760), new(0, 0, 1280, 760), rows);
        check(full.All(r => new Rectangle(0, 0, 1280, 760).Contains(r)), "При выделении до краёв панели остаются доступны внутри экрана");
        Mouse(form, "OnMouseDown", 30, 30); Mouse(form, "OnMouseUp", 90, 90);
        check(doc.Crop == new Rectangle(180, 100, 900, 430), "После начала редактирования форма не начинает новое выделение");

        using var regular = new EditorForm(TrayApplication.CreateDemo(), new AppSettings());
        check(regular.FormBorderStyle == FormBorderStyle.Sizable && regular.ShowInTaskbar, "Полный снимок по-прежнему использует обычное изменяемое окно редактора");
        var fullTools = Field<Dictionary<EditorTool, ToolGroupButton>>(regular, "tools");
        typeof(EditorForm).GetMethod("SelectTool", Private)!.Invoke(regular, [EditorTool.Crop]);
        check(fullTools.ContainsKey(EditorTool.Crop) && Field<EditorCanvas>(regular, "canvas").Tool == EditorTool.Crop,
            "Редактор полного снимка сохраняет кнопку и инструмент обрезки");
        canvas.Dispose();

        using var small = new EditorForm(new Bitmap(640, 480), new AppSettings(), new Rectangle(0, 0, 640, 480));
        Mouse(small, "OnMouseDown", 600, 430); Mouse(small, "OnMouseUp", 40, 30);
        var smallCanvas = Field<EditorCanvas>(small, "canvas"); var smallBar = Field<FloatingBar>(small, "toolBar");
        small.Controls.Remove(smallCanvas); smallCanvas.Dock = DockStyle.None; smallCanvas.Size = small.ClientSize; smallCanvas.CreateControl(); smallCanvas.PerformLayout();
        typeof(EditorForm).GetMethod("PositionRegionBars", Private)!.Invoke(small, null);
        check(Field<ScreenshotDocument>(small, "document").Crop == new Rectangle(40, 30, 560, 400), "Выделение в обратном направлении использует финальную точку отпускания мыши");
        check(smallBar.Width <= 616 && smallBar.Controls.OfType<IconButton>().All(b => b.Bottom <= smallBar.Height && b.Right <= smallBar.Width),
            "На узком экране кнопки переносятся на следующую строку без обрезки");
        smallCanvas.Dispose();

        using var cancelled = new EditorForm(new Bitmap(640, 480), new AppSettings(), new Rectangle(0, 0, 640, 480));
        Mouse(cancelled, "OnMouseDown", 30, 30); Mouse(cancelled, "OnMouseUp", 30, 30);
        check(!Field<bool>(cancelled, "regionEditing") && !Field<ScreenshotDocument>(cancelled, "document").CanUndo,
            "Щелчок без области оставляет режим выделения и не создаёт редактор");
        var closing = new FormClosingEventArgs(CloseReason.UserClosing, false);
        typeof(EditorForm).GetMethod("ClosingEditor", Private)!.Invoke(cancelled, [cancelled, closing]);
        check(!closing.Cancel, "Отмена захвата до выбора области не предлагает сохранять весь рабочий стол");
    }
}
