using System.Drawing;
using System.Drawing.Imaging;
using System.Reflection;
using RapidShot;

internal static class FreehandTests
{
    public static void Run(Action<bool, string> check, string output)
    {
        Annotation Stroke(IEnumerable<PointF> points) => new() { Tool = EditorTool.Pen, Points = points.ToList(), Color = Color.Blue, Width = 5, Opacity = 65 };
        PointF[] Circle(float sweep = MathF.Tau, int turns = 1) => Enumerable.Range(0, 101 * turns).Select(i =>
        {
            var angle = sweep * i / 100; var radius = 80 + 3 * MathF.Sin(angle * 7) + 2 * MathF.Cos(angle * 3);
            return new PointF(250 + radius * MathF.Cos(angle), 230 + radius * MathF.Sin(angle));
        }).ToArray();
        var circleStroke = Stroke(Circle()); var circle = FreehandRecognizer.Recognize(circleStroke, [])?.Item;
        check(circle?.Tool == EditorTool.Ellipse && Math.Abs(circle.Bounds.Width - circle.Bounds.Height) < .001f, "Неровный замкнутый контур ручки распознаётся как точный круг");
        check(circle?.Id == circleStroke.Id && circle.Color == Color.Blue && circle.Width == 5 && circle.Opacity == 65, "Распознавание сохраняет идентификатор, цвет, толщину и прозрачность");
        check(FreehandRecognizer.Recognize(Stroke(Circle(MathF.PI * 1.65f)), []) == null, "Незамкнутая дуга остаётся свободным рисунком");
        check(FreehandRecognizer.Recognize(Stroke(Circle(turns: 2)), []) == null, "Повторные петли не принимаются за один круг");
        var square = new List<PointF>();
        PointF[] corners = [new(100, 100), new(300, 100), new(300, 300), new(100, 300), new(100, 100)];
        for (var i = 1; i < corners.Length; i++) for (var n = 0; n < 25; n++) square.Add(new(corners[i - 1].X + (corners[i].X - corners[i - 1].X) * n / 25, corners[i - 1].Y + (corners[i].Y - corners[i - 1].Y) * n / 25));
        square.Add(corners[^1]);
        check(FreehandRecognizer.Recognize(Stroke(square), []) == null, "Квадрат не превращается в круг");
        PointF[] arrowPath = [new(100, 200), new(160, 203), new(220, 198), new(290, 202), new(350, 200), new(300, 160), new(350, 200), new(300, 240)];
        foreach (var direction in new[] { 0, 1, 2, 3 })
        {
            PointF Rotate(PointF p) => direction switch { 1 => new(500 - p.X, p.Y), 2 => new(p.Y, p.X), 3 => new(p.Y, 500 - p.X), _ => p };
            var arrow = FreehandRecognizer.Recognize(Stroke(arrowPath.Select(Rotate)), [])?.Item;
            var tip = Rotate(arrowPath[4]);
            check(arrow?.Tool == EditorTool.Arrow && Math.Abs(arrow.End.X - tip.X) < 7 && Math.Abs(arrow.End.Y - tip.Y) < 7, $"Набросок стрелки распознаётся в направлении {direction}");
        }
        var shaft = Stroke([new(100, 200), new(350, 200)]);
        var head = Stroke([new(300, 160), new(350, 200), new(300, 240)]);
        check(FreehandRecognizer.Recognize(head, [shaft]) is { PreviousStrokes: 1, Item.Tool: EditorTool.Arrow }, "Стрелка распознаётся из отдельного стержня и V-образного наконечника");
        var firstWing = Stroke([new(350, 200), new(300, 160)]); var secondWing = Stroke([new(350, 200), new(300, 240)]);
        check(FreehandRecognizer.Recognize(secondWing, [shaft, firstWing]) is { PreviousStrokes: 2, Item.Tool: EditorTool.Arrow }, "Стрелка распознаётся из трёх последовательных штрихов");
        check(FreehandRecognizer.Recognize(firstWing, [shaft]) == null, "Одна ветка без второй не принимается за стрелку");
        var mismatched = shaft.Clone(); mismatched.Color = Color.Red;
        check(FreehandRecognizer.Recognize(head, [mismatched]) == null, "Разные стили штрихов не объединяются в стрелку");
        check(FreehandRecognizer.Recognize(Stroke([new(100, 200), new(300, 250), new(140, 110), new(320, 310), new(180, 240)]), []) == null, "Произвольный зигзаг остаётся рисунком");
        using var doc = new ScreenshotDocument(TrayApplication.CreateDemo());
        using var canvas = new EditorCanvas(doc) { Size = new(920, 540), Tool = EditorTool.Pen, Ink = Color.Blue, Opacity = 65, StrokeWidth = 5 };
        using var image = new Bitmap(canvas.Width, canvas.Height); canvas.DrawToBitmap(image, canvas.ClientRectangle);
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        var bounds = (RectangleF)typeof(EditorCanvas).GetField("imageBounds", flags)!.GetValue(canvas)!;
        void Mouse(string name, PointF p) => typeof(EditorCanvas).GetMethod(name, flags)!.Invoke(canvas, [new MouseEventArgs(MouseButtons.Left, 1, (int)Math.Round(bounds.X + p.X / doc.Crop.Width * bounds.Width), (int)Math.Round(bounds.Y + p.Y / doc.Crop.Height * bounds.Height), 0)]);
        void Draw(IReadOnlyList<PointF> points)
        {
            Mouse("OnMouseDown", points[0]); foreach (var p in points.Skip(1)) Mouse("OnMouseMove", p); Mouse("OnMouseUp", points[^1]);
        }
        Draw(Circle());
        check(doc.Items.Single().Tool == EditorTool.Ellipse && canvas.Selected == doc.Items[0], "Отпускание мыши добавляет распознанный круг как активный объект");
        doc.Undo(); check(doc.Items.Count == 0, "Создание распознанной фигуры отменяется одним действием");
        doc.Redo(); canvas.RecognizeShapes = false; Draw(Circle().Select(p => new PointF(p.X + 300, p.Y)).ToArray());
        check(doc.Items[^1].Tool == EditorTool.Pen, "Параметр ручки позволяет отключить выравнивание фигур");
        canvas.RecognizeShapes = true;
        Draw([new(650, 400), new(1000, 400)]); Draw([new(940, 350), new(1000, 400), new(940, 450)]);
        check(doc.Items.Count == 3 && doc.Items[^1].Tool == EditorTool.Arrow && canvas.Selected == doc.Items[^1], "Два штриха на холсте объединяются в один редактируемый объект стрелки");
        canvas.DoubleHeaded = true; canvas.UpdateSelectedStyle();
        check(doc.Items[^1].DoubleHeaded, "Распознанной стрелке можно добавить второй наконечник нижней панелью");
        doc.Undo(); doc.Undo();
        check(doc.Items.Count == 3 && doc.Items[^1].Tool == EditorTool.Pen, "Отмена объединения возвращает исходный стержень стрелки");
        doc.Redo();
        canvas.DrawToBitmap(image, canvas.ClientRectangle); image.Save(Path.Combine(output, "recognized-shapes-preview.png"), ImageFormat.Png);
        using var form = new EditorForm(TrayApplication.CreateDemo(), new AppSettings());
        var toggle = (CheckBox)typeof(EditorForm).GetField("recognizeShapes", flags)!.GetValue(form)!;
        var formCanvas = (EditorCanvas)typeof(EditorForm).GetField("canvas", flags)!.GetValue(form)!;
        check(toggle.Checked && toggle.Parent!.AccessibleName == "Параметры инструмента", "Переключатель распознавания включён по умолчанию и находится в нижней панели");
        toggle.Checked = false; check(!formCanvas.RecognizeShapes, "Переключатель панели управляет распознаванием ручки");
    }
}
