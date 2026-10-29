using System.Drawing;
using System.Drawing.Imaging;
using System.Net;
using RapidShot;

internal static class Program
{
    private static int checks;
    private static void Check(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
        checks++; Console.WriteLine("PASS " + message);
    }
    [STAThread]
    private static async Task Main(string[] args)
    {
        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        if (args.Contains("--package-assets"))
        {
            var target = Path.Combine(Directory.GetCurrentDirectory(), "installer", "assets"); Directory.CreateDirectory(target);
            using var icon = Theme.CreateIcon(); using var stream = File.Create(Path.Combine(target, "RapidShot.ico")); icon.Save(stream);
            Console.WriteLine("Создан значок установщика: " + target); return;
        }
        if (args.Contains("--google-network")) { await GoogleLensTests.RunNetwork(); return; }
        if (args.Contains("--blur-performance")) { BlurCreationTests.Run(Check); return; }
        if (args.Contains("--capture")) { CaptureTargetTests.RunNative(Check); return; }
        if (args.Contains("--render"))
        {
            RenderUi(); return;
        }
        using (var source = new Bitmap(200, 120))
        {
            using (var g = Graphics.FromImage(source)) g.Clear(Color.White);
            using var doc = new ScreenshotDocument(new Bitmap(source));
            doc.Add(new Annotation { Tool = EditorTool.Rectangle, Start = new(20, 20), End = new(90, 80), Color = Color.Red, Width = 4 });
            using (var render = doc.Render())
            {
                Check(render.GetPixel(20, 40).R > 220 && render.GetPixel(20, 40).G < 50, "Фигура попадает в экспорт");
                Check(render.GetPixel(50, 50).G > 240, "Фигура не закрашивает содержимое");
            }
            doc.CropTo(new(10, 10, 100, 80));
            using (var cropped = doc.Render()) Check(cropped.Size == new Size(100, 80) && cropped.GetPixel(10, 30).G < 50, "Обрезка сохраняет координаты пометок");
            doc.Undo(); Check(doc.Crop == new Rectangle(0, 0, 200, 120), "Отмена обрезки восстанавливает размер");
            doc.Undo(); Check(doc.Items.Count == 0, "Отмена удаляет фигуру");
            doc.Redo(); Check(doc.Items.Count == 1, "Повтор возвращает фигуру");
            doc.BeginChange(); doc.Items[0].Offset(15, 10); doc.NotifyChanged(); doc.Undo();
            Check(doc.Items[0].Start == new PointF(20, 20), "Отмена переноса восстанавливает координаты");
            Check(doc.CanRedo, "Перенос доступен для повтора");
            doc.Add(new Annotation { Tool = EditorTool.Pen, Points = [new(100, 20), new(120, 40)], Start = new(100, 20), End = new(120, 40), Color = Color.Blue, Width = 6 });
            Check(!doc.CanRedo, "Новое действие сбрасывает повтор");
            using var pen = doc.Render(); Check(pen.GetPixel(110, 30).B > 200 && pen.GetPixel(110, 30).R < 50, "Рисование попадает в экспорт");
            Check(doc.Items[1].HitTest(new(110, 30), 2) && !doc.Items[1].HitTest(new(150, 30), 2), "Ластик определяет попадание по штриху");
            Check(!doc.Items[0].HitTest(new(50, 50), 2), "Ластик не захватывает пустой центр фигуры");
            Check(!doc.CropTo(new(-100, -100, 1, 1)), "Пустая обрезка отклоняется");
        }
        using (var source = new Bitmap(100, 60))
        {
            using (var g = Graphics.FromImage(source)) { g.Clear(Color.Black); g.FillRectangle(Brushes.White, 50, 0, 50, 60); }
            using var doc = new ScreenshotDocument(new Bitmap(source));
            doc.Add(new Annotation { Tool = EditorTool.Blur, Start = new(20, 10), End = new(80, 50), Width = 4 });
            using (var blurred = doc.Render())
            {
                var boundary = blurred.GetPixel(49, 30).R;
                Check(boundary > 40 && boundary < 210, "Размытие смешивает пиксели области");
                Check(blurred.GetPixel(49, 5).R == 0 && blurred.GetPixel(90, 30).R == 255, "Размытие ограничено выделением");
            }
            doc.BeginChange(); doc.Items.Clear(); doc.NotifyChanged();
            using (var restored = doc.Render()) Check(restored.GetPixel(49, 30).R == 0, "Удаление размытия восстанавливает исходник");
            doc.Undo(); Check(doc.Items.Count == 1, "Удаление размытия можно отменить");
        }
        foreach (var tool in new[] { EditorTool.Arrow, EditorTool.Line, EditorTool.Ellipse, EditorTool.Text })
        {
            using var doc = new ScreenshotDocument(new Bitmap(240, 160));
            doc.Add(new Annotation { Tool = tool, Start = new(20, 20), End = new(180, 110), Text = "Тест\nRapidShot", Color = Color.Lime, Width = 5 });
            using var output = doc.Render();
            var pixels = 0;
            for (var y = 0; y < output.Height; y++) for (var x = 0; x < output.Width; x++) if (output.GetPixel(x, y).G > 100) pixels++;
            Check(pixels > 100, $"Экспорт инструмента {tool}");
        }
        BlurTests.Run(Check);
        await YapxTests.Run(Check);
        await GoogleLensTests.Run(Check);
        Check(AppSettings.FormatHotkey(6, Keys.S) == "Ctrl + Shift + S", "Формат горячей клавиши");
        using (var first = new HotkeyWindow())
        using (var second = new HotkeyWindow())
        {
            Check(first.Register(7, (int)Keys.F23), "Глобальная клавиша регистрируется в Windows");
            Check(second.Register(7, (int)Keys.F24), "Второе сочетание регистрируется");
            Check(!first.Register(7, (int)Keys.F24), "Занятое сочетание отклоняется");
            Check(!second.Register(7, (int)Keys.F23), "После отказа прежняя клавиша сохраняется");
        }
        Console.WriteLine($"Проверок пройдено: {checks}");
        if (args.Contains("--network"))
        {
            using var synthetic = new Bitmap(32, 32); using (var g = Graphics.FromImage(synthetic)) g.Clear(Color.Teal);
            using var bytes = new MemoryStream(); synthetic.Save(bytes, ImageFormat.Png);
            using var client = new YapxClient(); using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(35));
            try { var upload = await client.UploadAsync(bytes.ToArray(), timeout.Token); Console.WriteLine($"NETWORK Yapx OK: {upload.Url}"); }
            catch (Exception ex) { Console.Error.WriteLine($"NETWORK Yapx FAILED: {ex.Message}"); Environment.ExitCode = 2; }
        }
    }
    private static void RenderUi()
    {
        var output = Path.Combine(Directory.GetCurrentDirectory(), "artifacts"); Directory.CreateDirectory(output);
        void RenderContent(Form form, string name)
        {
            // Detach the view from its hidden form to render controls without desktop interaction.
            using var content = form.Controls[0];
            content.Font = form.Font; content.ForeColor = form.ForeColor; content.BackColor = form.BackColor;
            form.Controls.Remove(content);
            content.Dock = DockStyle.None; content.Location = Point.Empty; content.Size = form.ClientSize;
            content.CreateControl(); content.PerformLayout();
            using var bitmap = new Bitmap(content.Width, content.Height); content.DrawToBitmap(bitmap, content.ClientRectangle);
            bitmap.Save(Path.Combine(output, name), ImageFormat.Png);
        }
        using (var form = new EditorForm(TrayApplication.CreateDemo(), new AppSettings()))
        {
            Check(form.Controls.Count > 0, "Редактор создаёт дерево элементов управления");
            IEnumerable<Control> All(Control c) => c.Controls.Cast<Control>().SelectMany(child => new[] { child }.Concat(All(child)));
            var uploads = All(form).OfType<Button>().Where(x => x.AccessibleName?.StartsWith("Загрузить на ") == true).ToArray();
            Check(uploads.Length == 1 && uploads[0].AccessibleName == "Загрузить на Yapx", "В редакторе только одна кнопка загрузки — Yapx");
            RenderContent(form, "editor-preview.png");
        }
        using (var small = new EditorForm(TrayApplication.CreateDemo(), new AppSettings()) { ClientSize = new(740, 460) })
            RenderContent(small, "editor-small-preview.png");
        using (var cropForm = new EditorForm(TrayApplication.CreateDemo(), new AppSettings()))
        {
            typeof(EditorForm).GetMethod("SelectTool", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.Invoke(cropForm, [EditorTool.Crop]);
            RenderContent(cropForm, "crop-editor-preview.png");
        }
        using (var arrowForm = new EditorForm(TrayApplication.CreateDemo(), new AppSettings()))
        {
            var privateFields = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
            typeof(EditorForm).GetMethod("SelectTool", privateFields)!.Invoke(arrowForm, [EditorTool.Arrow]);
            var arrowCanvas = (EditorCanvas)typeof(EditorForm).GetField("canvas", privateFields)!.GetValue(arrowForm)!;
            var one = (RadioButton)typeof(EditorForm).GetField("arrowOne", privateFields)!.GetValue(arrowForm)!;
            var two = (RadioButton)typeof(EditorForm).GetField("arrowTwo", privateFields)!.GetValue(arrowForm)!;
            two.Checked = true; Check(arrowCanvas.DoubleHeaded, "Настройка «Два» включает два наконечника");
            one.Checked = true; Check(!arrowCanvas.DoubleHeaded, "Настройка «Один» возвращает один наконечник");
            RenderContent(arrowForm, "arrow-editor-preview.png");
        }
        using (var settings = new SettingsDialog(new AppSettings(), _ => null))
        {
            RenderContent(settings, "settings-preview.png");
        }
        // Exercise real canvas gesture handlers without sending input to the user's desktop.
        using var doc = new ScreenshotDocument(TrayApplication.CreateDemo());
        using var canvas = new EditorCanvas(doc) { Size = new(1000, 650) };
        using var paint = new Bitmap(canvas.Width, canvas.Height); canvas.DrawToBitmap(paint, canvas.ClientRectangle);
        var flags = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;
        void Mouse(string method, int x, int y) => typeof(EditorCanvas).GetMethod(method, flags)!.Invoke(canvas, [new MouseEventArgs(MouseButtons.Left, 1, x, y, 0)]);
        canvas.Tool = EditorTool.Rectangle;
        Mouse("OnMouseDown", 100, 100); Mouse("OnMouseMove", 300, 250); Mouse("OnMouseUp", 300, 250);
        Check(doc.Items.Count == 1 && doc.Items[0].Bounds.Width > 200, "Жест создаёт фигуру в координатах снимка");
        canvas.Tool = EditorTool.Eraser;
        Mouse("OnMouseDown", 100, 150); Mouse("OnMouseUp", 100, 150);
        Check(doc.Items.Count == 0, "Жест ластика удаляет пометку");
        doc.Undo(); Check(doc.Items.Count == 1, "Ластик поддерживает отмену");
        InlineTextTests.Run(Check, output);
        CropAndToolTests.Run(Check, output);
        StyleControlsTests.Run(Check, output);
        LiveAnnotationTests.Run(Check, output);
        TransformTests.Run(Check, output);
        ShiftConstraintTests.Run(Check, output);
        FreehandTests.Run(Check, output);
        ArrowRecognitionTests.Run(Check, output);
        EllipseRecognitionTests.Run(Check, output);
        PinTests.Run(Check, output);
        PinNumberTests.Run(Check, output);
        CaptureTargetTests.Run(Check, output);
        BlurCreationTests.Run(Check);
        MinimalInterfaceTests.Run(Check);
        RegionCaptureTests.Run(Check, output);
        ButtonRenderingTests.Run(Check, output);
        Console.WriteLine($"Проверок интерфейса пройдено: {checks}");
        Console.WriteLine("UI preview: " + output);
    }
}
