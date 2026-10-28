using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.Reflection;
using RapidShot;

internal static class TransformTests
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    public static void Run(Action<bool, string> check, string output)
    {
        using var doc = new ScreenshotDocument(TrayApplication.CreateDemo());
        doc.CropTo(new(100, 80, 1000, 600));
        using var canvas = new EditorCanvas(doc) { Size = new(800, 520), Tool = EditorTool.Select };
        using var bitmap = new Bitmap(canvas.Width, canvas.Height);
        void Paint() => canvas.DrawToBitmap(bitmap, canvas.ClientRectangle);
        T Field<T>(string name) => (T)typeof(EditorCanvas).GetField(name, Private)!.GetValue(canvas)!;
        Point At(PointF p)
        {
            var b = Field<RectangleF>("imageBounds");
            return new((int)Math.Round(b.X + (p.X - doc.Crop.X) * b.Width / doc.Crop.Width), (int)Math.Round(b.Y + (p.Y - doc.Crop.Y) * b.Height / doc.Crop.Height));
        }
        void Mouse(string name, Point p) => typeof(EditorCanvas).GetMethod(name, Private)!.Invoke(canvas, [new MouseEventArgs(MouseButtons.Left, 1, p.X, p.Y, 0)]);
        void Drag(PointF from, PointF to)
        {
            Mouse("OnMouseDown", At(from));
            for (var i = 1; i <= 4; i++) Mouse("OnMouseMove", At(new(from.X + (to.X - from.X) * i / 4, from.Y + (to.Y - from.Y) * i / 4)));
            Mouse("OnMouseUp", At(to));
        }
        Paint();
        foreach (var tool in new[] { EditorTool.Blur, EditorTool.Rectangle, EditorTool.Ellipse, EditorTool.Pen })
        {
            var item = new Annotation { Tool = tool, Start = new(280, 230), End = new(480, 390), Points = [new(280, 230), new(380, 270), new(480, 390)], Width = 5, Opacity = 70 };
            doc.Add(item); canvas.ActivateTool(tool); Paint();
            var oldStart = item.Start; var oldEnd = item.End; var count = doc.Items.Count;
            Drag(item.End, new(560, 450));
            check(item.Start == oldStart && Math.Abs(item.End.X - 560) < 2 && Math.Abs(item.End.Y - 450) < 2 && item.Id == canvas.Selected?.Id && doc.Items.Count == count,
                $"{tool}: маркер меняет размер того же объекта при масштабе и смещённой обрезке");
            if (tool == EditorTool.Pen) check(item.Points[1].X > 410 && item.Points[1].Y > 280 && item.Width == 5, "Масштабирование штриха преобразует его точки, сохраняя толщину");
            doc.Undo(); check(doc.Items[^1].End == oldEnd, $"{tool}: всё перетягивание отменяется одним действием");
            doc.Redo(); check(doc.Items[^1].End != oldEnd && canvas.Selected?.Id == item.Id, $"{tool}: повтор восстанавливает размер и выделение");
            if (tool == EditorTool.Blur) { Paint(); bitmap.Save(Path.Combine(output, "resize-blur-preview.png"), ImageFormat.Png); }
            canvas.ClearSelection(); canvas.Tool = EditorTool.Select;
        }
        var arrow = new Annotation { Tool = EditorTool.Arrow, Start = new(300, 400), End = new(600, 250), DoubleHeaded = true, Opacity = 50, Color = Color.Red };
        doc.Add(arrow); canvas.ActivateTool(EditorTool.Arrow);
        Drag(arrow.Start, new(240, 450));
        check(arrow.Start.X < 242 && arrow.End == new PointF(600, 250) && arrow.DoubleHeaded && arrow.Opacity == 50, "Конец стрелки меняет направление, сохраняя второй конец, наконечники и стиль");
        var line = new Annotation { Tool = EditorTool.Line, Start = new(300, 250), End = new(500, 250) };
        doc.Add(line); canvas.ActivateTool(EditorTool.Line); Drag(line.End, new(550, 330));
        check(line.Start == new PointF(300, 250) && line.End.Y > 328, "Горизонтальная линия изменяется за конец без вырождения рамки");
        canvas.ActivateTool(EditorTool.Text); Mouse("OnMouseDown", At(new(220, 200)));
        canvas.Controls.OfType<TextBox>().Single().Text = "Размер"; canvas.CommitText(); canvas.ActivateTool(EditorTool.Select);
        var text = doc.Items[^1]; var size = text.Bounds.Size; var font = text.FontSize; var position = text.Start;
        Drag(text.End, new(text.End.X + size.Width * .6f, text.End.Y + size.Height * .6f));
        check(text.FontSize > font * 1.5f && text.Start == position && text.Text == "Размер", "Угловой маркер текста равномерно увеличивает шрифт, сохраняя содержимое");
        canvas.ActivateTool(EditorTool.Blur);
        var blur = new Annotation { Tool = EditorTool.Blur, Start = new(320, 300), End = new(500, 440) };
        doc.Add(blur); var originalEnd = blur.End;
        Drag(new(500, 370), new(600, 370));
        check(blur.End.X > originalEnd.X + 95 && blur.Start == new PointF(320, 300) && blur.End.Y == originalEnd.Y, "Боковой маркер меняет только одну сторону области");
        Drag(blur.End, new(310, 290));
        check(blur.Bounds.Width >= 2 && blur.Bounds.Height >= 2, "Маркеры не позволяют схлопнуть область в пустой прямоугольник");
        using var export = doc.Render();
        check(export.Size == doc.Crop.Size, "Экспорт после изменений сохраняет полное разрешение снимка");
        Benchmark(check);
    }
    private static void Benchmark(Action<bool, string> check)
    {
        using var source = new Bitmap(3840, 2160);
        using (var g = Graphics.FromImage(source)) { g.Clear(Color.White); for (var stripe = 0; stripe < 3840; stripe += 32) g.FillRectangle(Brushes.Black, stripe, 0, 16, 2160); }
        using var doc = new ScreenshotDocument(source);
        doc.Add(new Annotation { Tool = EditorTool.Blur, Start = new(200, 200), End = new(2400, 1700), Width = 12 });
        var blur = new Annotation { Tool = EditorTool.Blur, Start = new(700, 500), End = new(2900, 1800), Width = 14 };
        doc.Add(blur);
        doc.Add(new Annotation { Tool = EditorTool.Rectangle, Start = new(600, 400), End = new(2800, 1700), Color = Color.Red });
        using var canvas = new EditorCanvas(doc) { Size = new(920, 520), Tool = EditorTool.Select };
        using var image = new Bitmap(canvas.Width, canvas.Height);
        void Paint() => canvas.DrawToBitmap(image, canvas.ClientRectangle);
        T Field<T>(string name) => (T)typeof(EditorCanvas).GetField(name, Private)!.GetValue(canvas)!;
        void Mouse(string name, int x, int y) => typeof(EditorCanvas).GetMethod(name, Private)!.Invoke(canvas, [new MouseEventArgs(MouseButtons.Left, 1, x, y, 0)]);
        Paint(); var bounds = Field<RectangleF>("imageBounds");
        var x = (int)(bounds.X + bounds.Width * .5f); var y = (int)(bounds.Y + bounds.Height * .5f);
        Mouse("OnMouseDown", x, y); Paint();
        check(canvas.Selected == blur, "Проверка скорости перемещает размытие под более поздней фигурой");
        var before = Field<Bitmap>("preview");
        Mouse("OnMouseMove", x + 1, y + 1); Mouse("OnMouseMove", x + 2, y + 2);
        check(ReferenceEquals(before, Field<Bitmap>("preview")), "Движения мыши объединяются до перерисовки без пересборки полного изображения");
        Paint(); var cache = Field<Bitmap>("transformPreview");
        var frames = new List<double>();
        for (var i = 3; i <= 22; i++)
        {
            var timer = Stopwatch.StartNew(); Mouse("OnMouseMove", x + i, y + i); Paint(); frames.Add(timer.Elapsed.TotalMilliseconds);
        }
        check(ReferenceEquals(cache, Field<Bitmap>("transformPreview")), "При перемещении кэш предыдущих слоёв и размытия используется повторно");
        check((long)Field<Bitmap>("preview").Width * Field<Bitmap>("preview").Height < 500_000, "Предпросмотр 4K ограничен разрешением видимой области");
        Mouse("OnMouseUp", x + 23, y + 24); Paint();
        using (var fresh = doc.RenderPreview(Field<Bitmap>("preview").Size))
        {
            var current = Field<Bitmap>("preview"); var same = true;
            for (var py = 0; py < fresh.Height; py += 11) for (var px = 0; px < fresh.Width; px += 11) same &= fresh.GetPixel(px, py) == current.GetPixel(px, py);
            check(same, "Кэш не меняет порядок перекрывающихся размытия и фигур; точка отпускания учитывается");
        }
        var fullTimer = Stopwatch.StartNew(); using var full = doc.Render(); fullTimer.Stop();
        frames.Sort(); var median = frames[frames.Count / 2];
        Console.WriteLine($"PERF 3840x2160, два размытия: кадр переноса median={median:F1} ms, max={frames.Max():F1} ms; полная сборка={fullTimer.Elapsed.TotalMilliseconds:F1} ms");
        check(median < fullTimer.Elapsed.TotalMilliseconds, "Кадр перемещения быстрее полной сборки 4K-снимка");
        doc.Undo(); check(doc.Items[1].Start == new PointF(700, 500), "Перенос размытия со множеством кадров отменяется одним действием");
    }
}
