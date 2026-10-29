using System.Collections;
using System.Drawing;
using System.Drawing.Imaging;
using System.Reflection;
using RapidShot;

internal static class CaptureResizeTests
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    private static void Mouse(EditorCanvas canvas, string method, Point point) => typeof(EditorCanvas).GetMethod(method, Private)!.Invoke(canvas, [new MouseEventArgs(MouseButtons.Left, 1, point.X, point.Y, 0)]);
    private static int UndoCount(ScreenshotDocument doc) => ((ICollection)typeof(ScreenshotDocument).GetField("undo", Private)!.GetValue(doc)!).Count;
    public static void Run(Action<bool, string> check, string output)
    {
        Rectangle initial = new(180, 100, 800, 420);
        (Point Start, Point End, Rectangle Expected, EditorTool Tool)[] cases = [
            (new(180, 100), new(120, 60), new(120, 60, 860, 460), EditorTool.Pen),
            (new(580, 100), new(580, 60), new(180, 60, 800, 460), EditorTool.Select),
            (new(980, 100), new(1040, 60), new(180, 60, 860, 460), EditorTool.Text),
            (new(980, 310), new(1040, 310), new(180, 100, 860, 420), EditorTool.Blur),
            (new(980, 520), new(1040, 580), new(180, 100, 860, 480), EditorTool.Rectangle),
            (new(580, 520), new(580, 580), new(180, 100, 800, 480), EditorTool.Arrow),
            (new(180, 520), new(120, 580), new(120, 100, 860, 480), EditorTool.Line),
            (new(180, 310), new(120, 310), new(120, 100, 860, 420), EditorTool.Eraser) ];
        foreach (var fixture in cases)
        {
            using var doc = new ScreenshotDocument(TrayApplication.CreateDemo()); doc.SetCaptureRegion(initial);
            var annotation = new Annotation { Tool = EditorTool.Rectangle, Start = new(400, 200), End = new(620, 350), Width = 5, Color = Color.Red }; doc.Add(annotation);
            using var canvas = new EditorCanvas(doc) { Size = new(1280, 760), Tool = fixture.Tool }; canvas.UseDesktopCoordinates();
            using var paint = new Bitmap(canvas.Width, canvas.Height); canvas.DrawToBitmap(paint, canvas.ClientRectangle);
            var undoBefore = UndoCount(doc);
            var cached = typeof(EditorCanvas).GetField("preview", Private)!.GetValue(canvas);
            Mouse(canvas, "OnMouseDown", fixture.Start);
            Mouse(canvas, "OnMouseMove", fixture.End);
            canvas.DrawToBitmap(paint, canvas.ClientRectangle);
            check(canvas.IsResizingCapture && doc.Crop == initial && canvas.DisplayedCaptureBounds == fixture.Expected && ReferenceEquals(cached, typeof(EditorCanvas).GetField("preview", Private)!.GetValue(canvas)),
                $"Рамка {fixture.Tool}: край/угол расширяет предварительную область без пересоздания снимка");
            Mouse(canvas, "OnMouseUp", fixture.End);
            check(doc.Crop == fixture.Expected && UndoCount(doc) == undoBefore + 1 && doc.Items.Count == 1 && annotation.Start == new PointF(400, 200),
                $"Рамка {fixture.Tool}: отпускание меняет область одним действием, сохраняя пометки");
            doc.Undo(); var restored = doc.Crop == initial; doc.Redo();
            check(restored && doc.Crop == fixture.Expected, $"Рамка {fixture.Tool}: отмена и повтор восстанавливают размеры");
        }

        using var document = new ScreenshotDocument(TrayApplication.CreateDemo()); document.SetCaptureRegion(initial);
        using var view = new EditorCanvas(document) { Size = new(1280, 760), Tool = EditorTool.Select }; view.UseDesktopCoordinates();
        void Drag(Point start, Point end) { Mouse(view, "OnMouseDown", start); Mouse(view, "OnMouseMove", end); Mouse(view, "OnMouseUp", end); }
        Drag(new(180, 100), new(-500, -500));
        check(document.Crop == new Rectangle(0, 0, 980, 520), "Расширение рамки ограничено левым и верхним краями рабочего стола");
        Drag(new(980, 520), new(5000, 5000));
        check(document.Crop == new Rectangle(0, 0, 1280, 760), "Расширение рамки ограничено правым и нижним краями рабочего стола");
        document.Undo(); document.Undo();
        Drag(new(180, 310), new(2000, 310));
        check(document.Crop.Width == 2 && document.Crop.Right == initial.Right, "Край рамки не пересекает противоположный: минимум два пикселя"); document.Undo();
        var count = UndoCount(document);
        Mouse(view, "OnMouseDown", new(180, 100)); Mouse(view, "OnMouseMove", new(120, 60)); view.CancelGesture(false);
        check(document.Crop == initial && !view.IsResizingCapture && UndoCount(document) == count, "Отмена перетягивания возвращает рамку без записи в историю");
        Mouse(view, "OnMouseDown", new(980, 520)); Mouse(view, "OnMouseMove", new(1100, 600)); view.Capture = false;
        typeof(EditorCanvas).GetMethod("OnMouseCaptureChanged", Private)!.Invoke(view, [EventArgs.Empty]);
        check(document.Crop == initial && !view.IsResizingCapture, "Потеря захвата мыши отменяет незавершённое изменение области");
        Mouse(view, "OnMouseDown", new(180, 100)); Mouse(view, "OnMouseUp", new(180, 100));
        check(UndoCount(document) == count, "Щелчок по маркеру без перемещения не создаёт лишнюю отмену");
        Mouse(view, "OnMouseDown", new(180, 100)); Mouse(view, "OnMouseMove", new(120, 60)); view.ActivateTool(EditorTool.Pen);
        check(document.Crop == initial && !view.IsResizingCapture, "Смена инструмента отменяет незавершённое перетягивание рамки");
        var item = new Annotation { Tool = EditorTool.Rectangle, Start = new(400, 200), End = new(620, 350), Width = 5, Color = Color.Red }; document.Add(item);
        Drag(new(980, 520), new(1100, 600));
        using (var rendered = document.Render())
            check(rendered.Size == new Size(920, 500) && rendered.GetPixel(220, 140).G < 50 && rendered.GetPixel(850, 450).ToArgb() == document.Original.GetPixel(1030, 550).ToArgb(),
                "Экспорт включает расширенную область и сохраняет пиксельные координаты пометок");

        using var form = new EditorForm(TrayApplication.CreateDemo(), new AppSettings(), new Rectangle(0, 0, 1280, 760));
        T Field<T>(string name) => (T)typeof(EditorForm).GetField(name, Private)!.GetValue(form)!;
        typeof(EditorForm).GetMethod("OnMouseDown", Private)!.Invoke(form, [new MouseEventArgs(MouseButtons.Left, 1, 180, 100, 0)]);
        typeof(EditorForm).GetMethod("OnMouseUp", Private)!.Invoke(form, [new MouseEventArgs(MouseButtons.Left, 1, 980, 520, 0)]);
        var canvasRegion = Field<EditorCanvas>("canvas"); form.Controls.Remove(canvasRegion); canvasRegion.Dock = DockStyle.None; canvasRegion.Size = form.ClientSize; canvasRegion.Font = form.Font; canvasRegion.CreateControl();
        Mouse(canvasRegion, "OnMouseDown", new(980, 520)); Mouse(canvasRegion, "OnMouseMove", new(1100, 570));
        var bar = Field<FloatingBar>("toolBar");
        check(bar.Top == 582 && Math.Abs(bar.Left + bar.Width / 2 - 640) <= 1, "Панель следует за новой рамкой во время изменения области");
        using var preview = new Bitmap(canvasRegion.Width, canvasRegion.Height); canvasRegion.DrawToBitmap(preview, canvasRegion.ClientRectangle); preview.Save(Path.Combine(output, "capture-resize-preview.png"), ImageFormat.Png);
        var message = Message.Create(IntPtr.Zero, 0, IntPtr.Zero, IntPtr.Zero);
        typeof(EditorForm).GetMethod("ProcessCmdKey", Private)!.Invoke(form, [message, Keys.Escape]);
        check(!canvasRegion.IsResizingCapture && Field<ScreenshotDocument>("document").Crop == initial, "Esc отменяет изменение рамки, оставляя редактор открытым");
        Mouse(canvasRegion, "OnMouseDown", new(980, 520)); Mouse(canvasRegion, "OnMouseMove", new(1100, 570));
        typeof(EditorForm).GetMethod("Undo", Private)!.Invoke(form, null);
        check(!canvasRegion.IsResizingCapture && !Field<ScreenshotDocument>("document").CanUndo, "Ctrl+Z сначала отменяет перетягивание рамки");
        canvasRegion.Dispose();
    }
}
