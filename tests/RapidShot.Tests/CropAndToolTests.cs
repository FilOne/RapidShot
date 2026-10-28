using System.Drawing;
using System.Drawing.Imaging;
using System.Reflection;
using RapidShot;

internal static class CropAndToolTests
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    public static void Run(Action<bool, string> check, string output)
    {
        using (var group = new ToolGroupButton(new(EditorTool.Pen, "✎", "Карандаш", "P"), new(EditorTool.Line, "╱", "Линия", "L"), new(EditorTool.Arrow, "↗", "Стрелка", "A")))
        {
            var selected = new List<EditorTool>(); group.ToolChosen += selected.Add;
            group.ContextMenuStrip!.Items[2].PerformClick(); group.PerformClick();
            check(selected.SequenceEqual(new[] { EditorTool.Arrow, EditorTool.Arrow }) && group.CurrentTool == EditorTool.Arrow, "Группа запоминает выбор из списка и выбирает его коротким щелчком");
            group.Remember(EditorTool.Line);
            check(group.Text == "╱" && ((ToolStripMenuItem)group.ContextMenuStrip.Items[1]).Checked, "Кнопка и отметка списка отражают выбранный инструмент");
            var opened = 0;
            group.ContextMenuStrip.Opening += (_, e) => { opened++; e.Cancel = true; };
            typeof(ToolGroupButton).GetMethod("OnMouseDown", Private)!.Invoke(group, [new MouseEventArgs(MouseButtons.Left, 1, 10, 10, 0)]);
            group.Capture = true;
            var holdTimer = (System.Windows.Forms.Timer)typeof(ToolGroupButton).GetField("holdTimer", Private)!.GetValue(group)!;
            typeof(System.Windows.Forms.Timer).GetMethod("OnTick", Private)!.Invoke(holdTimer, [EventArgs.Empty]);
            group.PerformClick(); group.Capture = false;
            check(opened == 1 && selected.Count == 2, "Долгое нажатие раскрывает список без лишнего выбора текущего инструмента");
        }
        using var doc = new ScreenshotDocument(TrayApplication.CreateDemo());
        using var canvas = new EditorCanvas(doc) { Size = new(760, 480) };
        using var preview = new Bitmap(canvas.Width, canvas.Height);
        void Paint() => canvas.DrawToBitmap(preview, canvas.ClientRectangle);
        RectangleF Bounds() => (RectangleF)typeof(EditorCanvas).GetField("imageBounds", Private)!.GetValue(canvas)!;
        RectangleF CropBounds() => (RectangleF)typeof(EditorCanvas).GetProperty("CropScreenBounds", Private)!.GetValue(canvas)!;
        void Mouse(string name, Point point) => typeof(EditorCanvas).GetMethod(name, Private)!.Invoke(canvas, [new MouseEventArgs(MouseButtons.Left, 1, point.X, point.Y, 0)]);
        Point PointAt(float x, float y) => new((int)Math.Round(x), (int)Math.Round(y));
        void Drag(Point start, Point end) { Mouse("OnMouseDown", start); Mouse("OnMouseMove", end); Mouse("OnMouseUp", end); }
        Paint(); canvas.Tool = EditorTool.Crop;
        var full = doc.Crop; var b = Bounds();
        Drag(PointAt(b.Left + 50, b.Top + 50), PointAt(b.Left + 150, b.Top + 150));
        check(!canvas.HasCropChanges && doc.Crop == full, "Обрезка не создаёт выделение внутри снимка");
        Drag(PointAt(b.Left, b.Top + b.Height / 2), PointAt(b.Left + 80, b.Top + b.Height / 2));
        check(canvas.HasCropChanges && doc.Crop == full, "Перетягивание края создаёт предварительную обрезку без изменения документа");
        Paint(); preview.Save(Path.Combine(output, "crop-preview.png"), ImageFormat.Png);
        var r = CropBounds();
        Drag(PointAt(r.Left, r.Top + r.Height / 2), PointAt(b.Left - 100, r.Top + r.Height / 2));
        check(!canvas.HasCropChanges, "До применения край можно вернуть к исходной границе");
        Drag(PointAt(b.Right, b.Top), PointAt(b.Right - 80, b.Top + 60));
        check(canvas.HasCropChanges && doc.Crop == full, "Угол меняет сразу две границы");
        check(canvas.CommitCrop() && doc.Crop.Width < full.Width && doc.Crop.Height < full.Height, "Подтверждение применяет обрезку за края");
        var cropped = doc.Crop;
        doc.Undo(); check(doc.Crop == full, "Обрезка за края поддерживает отмену");
        doc.Redo(); check(doc.Crop == cropped, "Обрезка за края поддерживает повтор");
        Paint(); b = Bounds();
        Drag(PointAt(b.Right, b.Top + b.Height / 2), PointAt(b.Right - 40, b.Top + b.Height / 2)); canvas.CancelCrop();
        check(!canvas.HasCropChanges && doc.Crop == cropped, "Esc/сброс отменяет только предварительную обрезку");
        doc.Undo(); Paint(); b = Bounds();
        Drag(PointAt(b.Left, b.Top + b.Height / 2), PointAt(b.Right + 50, b.Top + b.Height / 2)); canvas.CommitCrop();
        check(doc.Crop.Width == 2, "Границы не пересекаются: минимальный размер — два пикселя");
        doc.Undo(); Paint(); b = Bounds();
        Drag(PointAt(b.Right, b.Bottom), PointAt(b.Right + 100, b.Bottom + 100));
        check(!canvas.HasCropChanges, "Рамка обрезки не выходит за исходный снимок");
        Drag(PointAt(b.Left, b.Top + b.Height / 2), PointAt(b.Left + 50, b.Top + b.Height / 2));
        var draft = Rectangle.Round((RectangleF)typeof(EditorCanvas).GetField("cropDraft", Private)!.GetValue(canvas)!);
        canvas.Size = new(620, 400); canvas.CommitEdits();
        check(doc.Crop == draft, "Изменение окна не меняет пиксельные координаты обрезки перед экспортом");
        doc.Undo(); canvas.Tool = EditorTool.Select;
        var arrow = new Annotation { Tool = EditorTool.Arrow, Start = new(50, 100), End = new(220, 100), Width = 4, Color = Color.Red };
        doc.Add(arrow); Paint(); b = Bounds();
        var middle = PointAt(b.Left + 130f / doc.Crop.Width * b.Width, b.Top + 100f / doc.Crop.Height * b.Height);
        Mouse("OnMouseDown", middle); Mouse("OnMouseUp", middle);
        canvas.DoubleHeaded = true; canvas.UpdateSelectedStyle();
        check(doc.Items.Single().DoubleHeaded, "Выбранную стрелку можно сделать двусторонней");
        doc.Undo(); check(!doc.Items.Single().DoubleHeaded, "Отмена восстанавливает один наконечник");
        doc.Redo(); check(doc.Items.Single().DoubleHeaded, "Повтор сохраняет два наконечника");
        using var single = new Bitmap(260, 180); using var both = new Bitmap(260, 180);
        using (var g = Graphics.FromImage(single)) { g.Clear(Color.White); arrow.DoubleHeaded = false; arrow.Draw(g); }
        using (var g = Graphics.FromImage(both)) { g.Clear(Color.White); arrow.DoubleHeaded = true; arrow.Draw(g); }
        int CapPixels(Bitmap image)
        {
            var count = 0;
            for (var y = 80; y < 120; y++) for (var x = 45; x < 80; x++)
                if (Math.Abs(y - 100) > 4 && image.GetPixel(x, y).G < 100) count++;
            return count;
        }
        check(CapPixels(both) > CapPixels(single) + 10, "Второй наконечник попадает в экспорт изображения");
        both.Save(Path.Combine(output, "double-arrow-preview.png"), ImageFormat.Png);
    }
}
