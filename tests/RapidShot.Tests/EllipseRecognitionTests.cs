using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Reflection;
using RapidShot;

internal static class EllipseRecognitionTests
{
    private sealed record Sketch(string Name, float A, float B, float Angle, float Noise, float Sweep = MathF.Tau, bool Reverse = false, bool UnevenSpeed = false);
    private static Sketch[] Cases() => [
        new("Неровный круг", 80, 80, 0, .16f),
        new("Почти круг", 84, 78, 0, .08f),
        new("Круг с зазором", 80, 80, 0, .12f, MathF.Tau - .25f),
        new("Круг с перехлёстом", 80, 80, 0, .12f, MathF.Tau + .3f),
        new("Обратное направление", 80, 80, 0, .14f, Reverse: true),
        new("Неравномерное рисование", 80, 80, 0, .12f, UnevenSpeed: true),
        new("Горизонтальный эллипс", 120, 55, 0, .1f),
        new("Вертикальный эллипс", 120, 50, 90, .1f),
        new("Наклонный эллипс", 120, 55, 35, .1f),
        new("Эллипс против часовой", 120, 55, -42, .12f, Reverse: true),
        new("Узкий эллипс", 160, 22, 20, .06f),
        new("Эллипс с зазором", 120, 55, 35, .1f, MathF.Tau - .25f)
    ];
    private static Annotation Draw(Sketch sketch, float size = 1, PointF? center = null)
    {
        var c = center ?? new PointF(250, 210); var rotation = sketch.Angle * MathF.PI / 180;
        var points = Enumerable.Range(0, 161).Select(i =>
        {
            var t = i / 160f; if (sketch.UnevenSpeed) t = MathF.Pow(t, 1.8f);
            var angle = t * sketch.Sweep * (sketch.Reverse ? -1 : 1);
            var wobble = 1 + sketch.Noise * MathF.Sin(angle * 5) + sketch.Noise * .35f * MathF.Cos(angle * 9);
            var x = sketch.A * size * MathF.Cos(angle) * wobble; var y = sketch.B * size * MathF.Sin(angle) * wobble;
            return new PointF(c.X + x * MathF.Cos(rotation) - y * MathF.Sin(rotation), c.Y + x * MathF.Sin(rotation) + y * MathF.Cos(rotation));
        }).ToList();
        return new Annotation { Tool = EditorTool.Pen, Points = points, Start = points[0], End = points[^1], Width = 3 * size, Color = Color.Red, Opacity = 65 };
    }
    public static void Run(Action<bool, string> check, string output)
    {
        var cases = Cases();
        foreach (var sketch in cases)
        {
            var stroke = Draw(sketch); var result = FreehandRecognizer.Recognize(stroke, [])?.Item;
            check(result?.Tool == EditorTool.Ellipse, $"Распознавание овального контура: {sketch.Name}");
            if (sketch.A == sketch.B || sketch.A / sketch.B < 1.22f) check(Math.Abs(result!.Bounds.Width - result.Bounds.Height) < .01f, "Округлый набросок становится точным кругом");
            else check(Math.Max(result!.Bounds.Width, result.Bounds.Height) / Math.Min(result.Bounds.Width, result.Bounds.Height) > 1.6f, "Вытянутый набросок сохраняет пропорции эллипса");
            check(result!.Id == stroke.Id && result.Color == stroke.Color && result.Width == stroke.Width && result.Opacity == stroke.Opacity && Distance(EllipseGeometry.Center(result), new(250, 210)) < 10,
                "Круг/эллипс сохраняет центр, стиль и идентификатор наброска");
        }
        foreach (var scale in new[] { .35f, 3f })
        {
            var result = FreehandRecognizer.Recognize(Draw(cases[8], scale, new(-300, 450)), [])?.Item;
            check(result?.Tool == EditorTool.Ellipse && Distance(EllipseGeometry.Center(result), new(-300, 450)) < 10 * scale, "Эллипс распознаётся независимо от размера и положения");
        }
        foreach (var sketch in new[] { cases[0] with { Sweep = MathF.PI * 1.6f }, cases[8] with { Sweep = MathF.PI * 1.6f }, cases[0] with { Sweep = MathF.Tau * 2 }, cases[8] with { Sweep = MathF.Tau * 2 } })
            check(FreehandRecognizer.Recognize(Draw(sketch), []) == null, "Открытая дуга и несколько витков не принимаются за круг/эллипс");
        foreach (var polygon in new PointF[][] {
            [new(100,100), new(300,100),new(300,300),new(100,300),new(100,100)],
            [new(100,180),new(400,180),new(400,250),new(100,250),new(100,180)],
            [new(250,100),new(370,300),new(130,300),new(250,100)] })
        {
            var points = new List<PointF>();
            for (var i = 1; i < polygon.Length; i++) for (var j = 0; j < 30; j++) points.Add(new(polygon[i - 1].X + (polygon[i].X - polygon[i - 1].X) * j / 30, polygon[i - 1].Y + (polygon[i].Y - polygon[i - 1].Y) * j / 30));
            points.Add(polygon[^1]);
            check(FreehandRecognizer.Recognize(new Annotation { Tool = EditorTool.Pen, Points = points }, []) == null, "Прямоугольник и треугольник остаются исходным рисунком");
        }
        var tilted = FreehandRecognizer.Recognize(Draw(cases[8]), [])!.Item;
        check(Math.Abs(tilted.EllipseAngle - 35) < 6 && tilted.Clone().EllipseAngle == tilted.EllipseAngle, "Наклон эллипса сохраняется при распознавании и копировании");
        var angle = tilted.EllipseAngle * MathF.PI / 180; var center = EllipseGeometry.Center(tilted); var radius = tilted.Bounds.Width / 2;
        var tip = new PointF(center.X + radius * MathF.Cos(angle), center.Y + radius * MathF.Sin(angle));
        check(tilted.HitTest(tip, 2) && !tilted.HitTest(center, 2), "Выбор и ластик учитывают наклон контура эллипса");
        var target = tilted.GeometryBounds; target.Width *= 1.5f; target.Height *= .7f;
        var resized = tilted.Clone(); EllipseGeometry.Resize(resized, tilted, target);
        check(Near(resized.GeometryBounds, target), "Изменение размера наклонного эллипса точно следует рамке выделения");
        Render(cases, output); Canvas(check);
    }
    private static void Canvas(Action<bool, string> check)
    {
        using var doc = new ScreenshotDocument(TrayApplication.CreateDemo()); using var canvas = new EditorCanvas(doc) { Tool = EditorTool.Pen, Size = new(900, 580), Ink = Color.Red };
        using var image = new Bitmap(canvas.Width, canvas.Height); canvas.DrawToBitmap(image, canvas.ClientRectangle);
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        var bounds = (RectangleF)typeof(EditorCanvas).GetField("imageBounds", flags)!.GetValue(canvas)!;
        Point At(PointF p) => new((int)Math.Round(bounds.X + p.X / doc.Crop.Width * bounds.Width), (int)Math.Round(bounds.Y + p.Y / doc.Crop.Height * bounds.Height));
        void Mouse(string name, PointF p) { var screen = At(p); typeof(EditorCanvas).GetMethod(name, flags)!.Invoke(canvas, [new MouseEventArgs(MouseButtons.Left, 1, screen.X, screen.Y, 0)]); }
        var sketch = Draw(Cases()[8], center: new(550, 350));
        Mouse("OnMouseDown", sketch.Points[0]); foreach (var p in sketch.Points.Skip(1)) Mouse("OnMouseMove", p); Mouse("OnMouseUp", sketch.Points[^1]);
        var ellipse = doc.Items.Single(); var id = ellipse.Id; var angle = ellipse.EllipseAngle;
        check(ellipse.Tool == EditorTool.Ellipse && canvas.Selected == ellipse && Math.Abs(angle) > 20, "Жест ручкой создаёт один выбранный наклонный эллипс");
        var b = ellipse.GeometryBounds;
        Mouse("OnMouseDown", new(b.Right, b.Bottom)); Mouse("OnMouseMove", new(b.Right + 50, b.Bottom + 30)); Mouse("OnMouseUp", new(b.Right + 50, b.Bottom + 30));
        check(ellipse.GeometryBounds.Width > b.Width + 45 && ellipse.Id == id, "Маркеры редактируют размер распознанного эллипса на месте");
        doc.Undo(); check(Near(doc.Items.Single().GeometryBounds, b) && doc.Items[0].EllipseAngle == angle, "Отмена размера возвращает геометрию и угол эллипса");
        doc.Undo(); check(doc.Items.Count == 0, "Создание эллипса отменяется одним действием"); doc.Redo();
        using var rendered = doc.Render(); check(rendered.GetPixel(550, 350).ToArgb() == doc.Original.GetPixel(550, 350).ToArgb(), "Экспорт эллипса не закрашивает его центр");
    }
    private static void Render(Sketch[] cases, string output)
    {
        using var image = new Bitmap(840, cases.Length * 190 + 40); using var g = Graphics.FromImage(image); g.Clear(Color.White); g.SmoothingMode = SmoothingMode.AntiAlias;
        using var font = new Font("Segoe UI", 10); g.DrawString("Набросок ручкой                         Круг или эллипс", font, Brushes.Black, 160, 5);
        for (var i = 0; i < cases.Length; i++)
        {
            g.DrawString(cases[i].Name, font, Brushes.Black, new RectangleF(8, i * 190 + 65, 135, 75));
            for (var column = 0; column < 2; column++)
            {
                var state = g.Save(); g.TranslateTransform(170 + column * 335, 40 + i * 190); g.ScaleTransform(.8f, .8f); g.TranslateTransform(-80, -100);
                var stroke = Draw(cases[i]); if (column == 0) stroke.Draw(g); else FreehandRecognizer.Recognize(stroke, [])?.Item.Draw(g);
                g.Restore(state);
            }
        }
        image.Save(Path.Combine(output, "ellipse-recognition-preview.png"), ImageFormat.Png);
    }
    private static bool Near(RectangleF a, RectangleF b) => Math.Abs(a.X - b.X) < .1 && Math.Abs(a.Y - b.Y) < .1 && Math.Abs(a.Width - b.Width) < .1 && Math.Abs(a.Height - b.Height) < .1;
    private static float Distance(PointF a, PointF b) => MathF.Sqrt((a.X - b.X) * (a.X - b.X) + (a.Y - b.Y) * (a.Y - b.Y));
}
