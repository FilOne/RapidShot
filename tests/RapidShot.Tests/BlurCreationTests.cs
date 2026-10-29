using System.Diagnostics;
using System.Drawing;
using System.Reflection;
using RapidShot;

internal static class BlurCreationTests
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    public static void Run(Action<bool, string> check)
    {
        foreach (var size in new[] { new Size(920, 580), new Size(1920, 1080) })
        {
            using var source = new Bitmap(3840, 2160);
            using (var g = Graphics.FromImage(source))
            {
                g.Clear(Color.White); for (var x = 0; x < source.Width; x += 32) g.FillRectangle(Brushes.DarkSlateBlue, x, 0, 16, source.Height);
            }
            using var form = new EditorForm(source, new AppSettings()) { ClientSize = size };
            using var content = form.Controls[0]; content.Font = form.Font; content.BackColor = form.BackColor; content.ForeColor = form.ForeColor;
            form.Controls.Remove(content); content.Dock = DockStyle.None; content.Size = form.ClientSize; content.CreateControl(); content.PerformLayout();
            var canvas = (EditorCanvas)typeof(EditorForm).GetField("canvas", Private)!.GetValue(form)!;
            var document = (ScreenshotDocument)typeof(EditorForm).GetField("document", Private)!.GetValue(form)!;
            typeof(EditorForm).GetMethod("SelectTool", Private)!.Invoke(form, [EditorTool.Blur]);
            using var paint = new Bitmap(canvas.Width, canvas.Height);
            void Paint() => canvas.DrawToBitmap(paint, canvas.ClientRectangle);
            T Field<T>(string name) => (T)typeof(EditorCanvas).GetField(name, Private)!.GetValue(canvas)!;
            void Mouse(string name, Point p) => typeof(EditorCanvas).GetMethod(name, Private)!.Invoke(canvas, [new MouseEventArgs(MouseButtons.Left, 1, p.X, p.Y, 0)]);
            Paint(); var image = Field<RectangleF>("imageBounds"); var preview = Field<Bitmap>("preview");
            Point At(float x, float y) => new((int)Math.Round(image.X + image.Width * x), (int)Math.Round(image.Y + image.Height * y));
            var layouts = 0; var changes = 0; var hints = 0; var latestHint = "";
            content.Layout += (_, _) => layouts++; document.Changed += () => changes++; canvas.HintChanged += text => { hints++; latestHint = text; };
            var timer = Field<System.Windows.Forms.Timer>("blurHintTimer");
            void Tick() => typeof(System.Windows.Forms.Timer).GetMethod("OnTick", Private)!.Invoke(timer, [EventArgs.Empty]);
            Mouse("OnMouseDown", At(.1f, .1f)); Paint();
            var layoutsAtStart = layouts; var hintsAtStart = hints;
            var input = Stopwatch.StartNew();
            for (var i = 0; i < 500; i++) Mouse("OnMouseMove", At(.5f + i % 50 / 150f, .5f + i % 75 / 210f));
            input.Stop();
            check(layouts == layoutsAtStart && hints == hintsAtStart, "Поток движений рамки не вызывает обновление строки состояния и переразметку окна");
            var pending = Field<Annotation>("pending");
            Tick(); var afterTick = hints; var afterTickLayouts = layouts;
            check(afterTick == hintsAtStart + 1 && latestHint == $"Выделено: {(int)pending.Bounds.Width} × {(int)pending.Bounds.Height} px", "Таймер публикует только последний актуальный размер области размытия");
            Tick(); check(hints == afterTick && layouts == afterTickLayouts, "Неизменившийся размер не обновляет интерфейс повторно");
            var frames = new List<double>();
            for (var i = 0; i < 40; i++)
            {
                var frameTimer = Stopwatch.StartNew(); Mouse("OnMouseMove", At(.5f + i / 100f, .5f + i / 100f)); Paint(); frames.Add(frameTimer.Elapsed.TotalMilliseconds);
            }
            check(ReferenceEquals(preview, Field<Bitmap>("preview")) && changes == 0 && document.Items.Count == 0,
                "Создание рамки размытия не пересчитывает документ или готовый предпросмотр до отпускания мыши");
            var release = Stopwatch.StartNew(); Mouse("OnMouseUp", At(.9f, .9f)); Paint(); release.Stop();
            var added = document.Items.Single(); var finalHints = hints;
            check(added.Tool == EditorTool.Blur && changes == 1 && !timer.Enabled && latestHint == $"Выделено: {(int)added.Bounds.Width} × {(int)added.Bounds.Height} px", "Отпускание мыши фиксирует точную область и её окончательный размер");
            Tick(); check(hints == finalHints, "После отпускания мыши отложенные подсказки прекращаются");
            frames.Sort();
            Console.WriteLine($"PERF рамка размытия {size.Width}x{size.Height}: 500 событий={input.Elapsed.TotalMilliseconds:F1} ms; кадр median={frames[frames.Count / 2]:F1} ms, max={frames.Max():F1} ms; отпускание={release.Elapsed.TotalMilliseconds:F1} ms; layouts={layouts}, hints={hints}");
            document.Undo(); check(document.Items.Count == 0, "Создание размытия с отложенной подсказкой отменяется одним действием");
            document.Redo(); check(document.Items.Single().Id == added.Id, "Повтор восстанавливает ту же область размытия");
            canvas.ClearSelection(); Mouse("OnMouseDown", At(.15f, .15f)); Mouse("OnMouseMove", At(.4f, .4f)); canvas.CancelGesture();
            var cancelledHints = hints; Tick();
            check(!timer.Enabled && hints == cancelledHints && document.Items.Count == 1, "Отмена рамки останавливает таймер и не добавляет размытие");
            Mouse("OnMouseDown", At(.15f, .15f)); Mouse("OnMouseMove", At(.4f, .4f)); canvas.ActivateTool(EditorTool.Rectangle);
            cancelledHints = hints; Tick(); check(!timer.Enabled && hints == cancelledHints, "Смена инструмента не оставляет работающих подсказок размытия");
            canvas.ActivateTool(EditorTool.Blur); canvas.ClearSelection();
            Mouse("OnMouseDown", At(.15f, .15f)); Mouse("OnMouseMove", At(.4f, .4f)); canvas.Capture = false;
            typeof(EditorCanvas).GetMethod("OnMouseCaptureChanged", Private)!.Invoke(canvas, [EventArgs.Empty]);
            cancelledHints = hints; Tick(); check(!timer.Enabled && hints == cancelledHints, "Потеря захвата мыши прекращает обновления незавершённой рамки");
        }
    }
}
