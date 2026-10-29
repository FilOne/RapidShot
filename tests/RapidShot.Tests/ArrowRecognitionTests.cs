using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Reflection;
using RapidShot;

internal static class ArrowRecognitionTests
{
    private sealed record Sketch(string Name, PointF[][] Strokes, bool DoubleHeaded = false);
    private static readonly PointF[] WavyShaft = [new(50, 140), new(100, 149), new(150, 131), new(200, 155), new(250, 137), new(300, 140)];
    private static readonly PointF[] UnevenHead = [new(281, 129), new(290, 130), new(300, 140), new(287, 156), new(265, 167), new(237, 192)];
    private static Sketch[] Examples() => [
        new("Неравные ветки", [[new(50, 140), new(300, 140), new(278, 129), new(300, 140), new(235, 195)]]),
        new("Неровный стержень", [WavyShaft.Concat(new PointF[] { new(256, 110), new(300, 140), new(255, 173) }).ToArray()]),
        new("Кривые неравные ветки", [WavyShaft, UnevenHead]),
        new("Неточный стык", [WavyShaft[..^1].Append(new PointF(293, 144)).ToArray(), [new(275, 125), new(304, 138), new(285, 164), new(250, 178)]]),
        new("Наконечник сначала", [UnevenHead, WavyShaft]),
        new("Стержень от острия", [WavyShaft.Reverse().ToArray(), UnevenHead]),
        new("Замкнутый треугольник", [[new(50, 140), new(300, 140), new(253, 103), new(244, 179), new(300, 140)]]),
        new("Стержень до основания", [[new(50, 140), new(250, 140)], [new(300, 140), new(250, 104), new(250, 179), new(300, 140)]]),
        new("Широкий наконечник", [[new(50, 140), new(300, 140), new(275, 76), new(300, 140), new(263, 206)]]),
        new("Узкий наконечник", [[new(50, 140), new(300, 140), new(250, 124), new(300, 140), new(245, 153)]]),
        new("Короткий наконечник", [[new(50, 140), new(300, 140), new(284, 130), new(300, 140), new(276, 154)]]),
        new("Два наконечника", [[new(90, 110), new(50, 140), new(94, 172), new(50, 140), new(110, 148), new(170, 129), new(235, 149), new(300, 140), new(277, 127), new(300, 140), new(245, 183)]], true)
    ];
    private static Annotation Stroke(IEnumerable<PointF> points, float width = 4) => new() { Tool = EditorTool.Pen, Points = points.ToList(), Color = Color.Red, Width = width, Opacity = 70 };
    private static FreehandRecognizer.Result? Recognize(PointF[][] paths, float width = 4) => FreehandRecognizer.Recognize(Stroke(paths[^1], width), paths[..^1].Select(p => Stroke(p, width)).ToArray());

    public static void Run(Action<bool, string> check, string output)
    {
        var timer = Stopwatch.StartNew();
        var cases = Examples();
        foreach (var sketch in cases)
        {
            var result = Recognize(sketch.Strokes);
            check(result?.Item.Tool == EditorTool.Arrow && result.Item.DoubleHeaded == sketch.DoubleHeaded && result.PreviousStrokes == sketch.Strokes.Length - 1,
                $"Стрелки: {sketch.Name}");
            check(Math.Abs(result!.Item.End.X - 300) < 8 && Math.Abs(result.Item.Start.X - 50) < 8 && result.Item.Width == 4 && result.Item.Opacity == 70,
                $"Стрелки: {sketch.Name} — сохранены концы, толщина и прозрачность");
        }
        foreach (var degrees in new[] { 30, 65, 120, 180, 235, 300 })
        foreach (var scale in new[] { .4f, 2f })
        {
            var angle = degrees * MathF.PI / 180;
            PointF Transform(PointF p) => new(700 + scale * (p.X * MathF.Cos(angle) - p.Y * MathF.Sin(angle)), 700 + scale * (p.X * MathF.Sin(angle) + p.Y * MathF.Cos(angle)));
            var sketch = cases[2]; var transformed = sketch.Strokes.Select(p => p.Select(Transform).ToArray()).ToArray();
            var result = Recognize(transformed, 4 * scale); var tip = Transform(new(300, 140));
            check(result?.Item.Tool == EditorTool.Arrow && Distance(result.Item.End, tip) < 8 * scale, $"Неровная стрелка: поворот {degrees}°, масштаб {scale}");
        }
        var shaft = WavyShaft; PointF[] a = [new(300, 140), new(280, 128)]; PointF[] b = [new(238, 189), new(270, 160), new(300, 140)];
        foreach (var paths in new[] { new[] { shaft, a, b }, new[] { shaft, b, a }, new[] { a, shaft, b }, new[] { a, b, shaft }, new[] { b, shaft, a }, new[] { b, a, shaft } })
            check(Recognize(paths) is { Item.Tool: EditorTool.Arrow, PreviousStrokes: 2 }, "Три неровных штриха распознаются независимо от порядка рисования");
        for (var seed = 0; seed < 12; seed++)
        {
            var random = new Random(seed);
            var noisyShaft = Enumerable.Range(0, 61).Select(i => new PointF(50 + 250f * i / 60,
                140 + 14 * MathF.Sin(i * MathF.PI / 15) + (i is 0 or 60 ? 0 : (random.NextSingle() - .5f) * 10))).ToArray();
            check(Recognize([noisyShaft, UnevenHead]) is { Item.Tool: EditorTool.Arrow, Item.DoubleHeaded: false }, $"Дрожащий стержень и асимметричный наконечник, пример {seed}");
        }
        var bowedShaft = Enumerable.Range(0, 41).Select(i => new PointF(50 + 250f * i / 40, 140 + 25 * MathF.Sin(MathF.PI * i / 40))).ToArray();
        check(Recognize([bowedShaft.Concat(UnevenHead.Reverse()).ToArray()]) is { Item.Tool: EditorTool.Arrow }, "Плавно искривлённый стержень с неравным наконечником распознаётся одним штрихом");

        PointF[][][] negative = [
            [WavyShaft],
            [[new(50, 140), new(300, 140), new(255, 100)]],
            [[new(50, 140), new(300, 140)], [new(300, 80), new(300, 200)]],
            [[new(50, 50), new(300, 200)], [new(50, 200), new(300, 50)]],
            [[new(50, 50), new(300, 50), new(300, 200), new(50, 200), new(50, 50)]],
            [[new(50, 150), new(100, 50), new(150, 180), new(200, 60), new(250, 180), new(300, 70)]],
            [[new(50, 140), new(130, 140)], UnevenHead],
            [[new(50, 140), new(140, 140), new(140, 240), new(300, 240), new(260, 200), new(300, 240), new(260, 280)]],
        ];
        foreach (var paths in negative) check(Recognize(paths) == null, "Обычный штрих, угол, крест, прямоугольник или зигзаг не превращается в стрелку");
        timer.Stop(); Console.WriteLine($"PERF расширенная проверка набросков: {timer.Elapsed.TotalMilliseconds:F0} ms");
        RenderExamples(cases, output);
        CanvasGesture(check);
    }
    private static void RenderExamples(Sketch[] cases, string output)
    {
        using var image = new Bitmap(840, cases.Length * 100 + 35); using var g = Graphics.FromImage(image); g.Clear(Color.White); g.SmoothingMode = SmoothingMode.AntiAlias;
        using var font = new Font("Segoe UI", 10);
        g.DrawString("Набросок ручкой                         Результат распознавания", font, Brushes.Black, new PointF(155, 4));
        for (var i = 0; i < cases.Length; i++)
        {
            g.DrawString(cases[i].Name, font, Brushes.Black, new RectangleF(8, i * 100 + 50, 135, 55));
            for (var column = 0; column < 2; column++)
            {
                var state = g.Save(); g.TranslateTransform(150 + column * 345, 35 + i * 100); g.ScaleTransform(.9f, .5f); g.TranslateTransform(-25, -60);
                if (column == 0) foreach (var stroke in cases[i].Strokes) Stroke(stroke).Draw(g);
                else Recognize(cases[i].Strokes)?.Item.Draw(g);
                g.Restore(state);
            }
        }
        image.Save(Path.Combine(output, "arrow-recognition-preview.png"), ImageFormat.Png);
    }
    private static void CanvasGesture(Action<bool, string> check)
    {
        using var doc = new ScreenshotDocument(TrayApplication.CreateDemo());
        using var canvas = new EditorCanvas(doc) { Size = new(920, 540), Tool = EditorTool.Pen };
        using var image = new Bitmap(canvas.Width, canvas.Height); canvas.DrawToBitmap(image, canvas.ClientRectangle);
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        var bounds = (RectangleF)typeof(EditorCanvas).GetField("imageBounds", flags)!.GetValue(canvas)!;
        void Mouse(string name, PointF p) => typeof(EditorCanvas).GetMethod(name, flags)!.Invoke(canvas, [new MouseEventArgs(MouseButtons.Left, 1, (int)Math.Round(bounds.X + (p.X + 100) / doc.Crop.Width * bounds.Width), (int)Math.Round(bounds.Y + (p.Y + 100) / doc.Crop.Height * bounds.Height), 0)]);
        foreach (var path in Examples()[2].Strokes)
        {
            Mouse("OnMouseDown", path[0]); foreach (var p in path.Skip(1)) Mouse("OnMouseMove", p); Mouse("OnMouseUp", path[^1]);
        }
        check(doc.Items.Count == 1 && doc.Items[0].Tool == EditorTool.Arrow && canvas.Selected == doc.Items[0], "Реальный жест неровной стрелки создаёт одну выбранную редактируемую стрелку");
        doc.Undo(); check(doc.Items.Count == 1 && doc.Items[0].Tool == EditorTool.Pen, "Отмена распознавания возвращает исходный неровный стержень");
        doc.Redo(); check(doc.Items.Single().Tool == EditorTool.Arrow, "Повтор возвращает распознанную неровную стрелку");
    }
    private static float Distance(PointF a, PointF b) => MathF.Sqrt((a.X - b.X) * (a.X - b.X) + (a.Y - b.Y) * (a.Y - b.Y));
}
