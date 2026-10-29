using System.Drawing;
using System.Drawing.Imaging;
using System.Numerics;
using System.Reflection;
using RapidShot;

internal static class PinTests
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    public static void Run(Action<bool, string> check, string output)
    {
        using (var doc = new ScreenshotDocument(new Bitmap(800, 500)))
        {
            var first = doc.AddPin(new(100, 100), Color.Red, 48);
            var second = doc.AddPin(new(200, 100), Color.Blue, 48);
            check(first.PinNumber == 1 && second.PinNumber == 2 && doc.NextPinNumber == 3, "Пины нумеруются с единицы и увеличиваются на один");
            check(first.Clone().PinNumber == 1 && first.Clone().PinSize == 48 && first.Clone().Id == first.Id, "Копия пина сохраняет номер, размер и идентификатор");
            doc.BeginChange(); doc.Items.Remove(first); doc.NotifyChanged();
            var third = doc.AddPin(new(300, 100), Color.Green, 48);
            check(second.PinNumber == 2 && third.PinNumber == 3, "Удаление пина не перенумеровывает остальные и не сбрасывает счётчик");
            doc.Undo(); check(doc.NextPinNumber == 3 && doc.Items.Single().PinNumber == 2, "Отмена добавления пина возвращает следующий номер");
            doc.Redo(); check(doc.NextPinNumber == 4 && doc.Items[^1].PinNumber == 3, "Повтор возвращает прежний номер пина и продолжает счётчик");
            doc.Undo(); var replacement = doc.AddPin(new(350, 150), Color.Red, 48);
            check(replacement.PinNumber == 3 && !doc.CanRedo, "Новый пин после отмены использует отменённый номер");
            var large = BigInteger.Pow(10, 30) - 1;
            doc.Add(PinMarker.Create(large, new(400, 300), Color.Red, 48, doc.Crop));
            var next = doc.AddPin(new(400, 400), Color.Blue, 48);
            check(next.PinNumber == large + 1 && doc.NextPinNumber == large + 2, "Нумерация пинов не ограничена разрядностью int или long");
            check(next.Bounds.Width > 300 && next.Bounds.Width > first.Bounds.Width && next.Bounds.Height == first.Bounds.Height, "Многозначная метка расширяется по ширине, сохраняя читаемый размер цифр");
            using var image = doc.Render(); image.Save(Path.Combine(output, "pin-numbers-preview.png"), ImageFormat.Png);
            using var shape = PinMarker.CreatePath(next.Bounds); using var outline = new Pen(Color.Black, 4);
            var labelsInside = true;
            for (var y = (int)next.Bounds.Top; y < next.Bounds.Bottom; y++) for (var x = (int)next.Bounds.Left; x < next.Bounds.Right; x++)
            {
                var p = image.GetPixel(x, y);
                if (p.A > 220 && p.R > 200 && p.G > 200 && p.B > 200 && !shape.IsVisible(x, y) && !shape.IsOutlineVisible(x, y, outline)) labelsInside = false;
            }
            check(labelsInside, "Все цифры длинного номера остаются внутри заливки пина");
        }
        using (var doc = new ScreenshotDocument(new Bitmap(200, 120)))
        {
            check(doc.NextPinNumber == 1, "Новый снимок начинает собственную нумерацию с единицы");
            var edge = doc.AddPin(new(0, 0), Color.Red, 48);
            check(doc.Crop.Contains(Rectangle.Ceiling(edge.Bounds)), "Пин у края помещается внутри снимка");
            var center = new PointF(edge.Bounds.X + edge.Bounds.Width / 2, edge.Bounds.Y + edge.PinSize / 2);
            check(edge.HitTest(center, 2) && !edge.HitTest(new(180, 100), 2), "Выбор и ластик находят залитую часть пина");
        }
        using var form = new EditorForm(TrayApplication.CreateDemo(), new AppSettings());
        using var content = form.Controls[0]; content.Font = form.Font; content.BackColor = form.BackColor; content.ForeColor = form.ForeColor;
        form.Controls.Remove(content); content.Dock = DockStyle.None; content.Size = form.ClientSize; content.CreateControl(); content.PerformLayout();
        T Field<T>(string name) => (T)typeof(EditorForm).GetField(name, Private)!.GetValue(form)!;
        var canvas = Field<EditorCanvas>("canvas"); var document = Field<ScreenshotDocument>("document");
        var tools = Field<Dictionary<EditorTool, ToolGroupButton>>("tools");
        check(tools[EditorTool.Pin].AccessibleName == "Пин" && tools.Values.Count(x => x == tools[EditorTool.Pin]) == 1, "У пина собственная быстрая кнопка, без меню долгого нажатия");
        object[] keyArgs = [new Message(), Keys.N]; typeof(EditorForm).GetMethod("ProcessCmdKey", Private)!.Invoke(form, keyArgs);
        check(canvas.Tool == EditorTool.Pin && Field<FlowLayoutPanel>("pinControls").Visible && Field<NumericUpDown>("pinSize").Visible, "Клавиша N включает пин и открывает его параметры снизу");
        check(!Field<ParameterSlider>("width").Visible && !Field<NumericUpDown>("fontSize").Visible && Field<FlowLayoutPanel>("colorControls").Visible, "Для пина показываются цвет и размер; параметры линий и текста скрыты");
        using var preview = new Bitmap(canvas.Width, canvas.Height); canvas.DrawToBitmap(preview, canvas.ClientRectangle);
        var imageBounds = (RectangleF)typeof(EditorCanvas).GetField("imageBounds", Private)!.GetValue(canvas)!;
        Point At(float x, float y) => new((int)Math.Round(imageBounds.X + x / document.Crop.Width * imageBounds.Width), (int)Math.Round(imageBounds.Y + y / document.Crop.Height * imageBounds.Height));
        void Mouse(string method, Point point, MouseButtons button = MouseButtons.Left) => typeof(EditorCanvas).GetMethod(method, Private)!.Invoke(canvas, [new MouseEventArgs(button, 1, point.X, point.Y, 0)]);
        void Click(float x, float y) { var point = At(x, y); Mouse("OnMouseDown", point); Mouse("OnMouseUp", point); }
        Click(350, 250); Click(650, 350); Click(900, 250);
        check(document.Items.Select(p => p.PinNumber).SequenceEqual(new BigInteger[] { 1, 2, 3 }) && canvas.Selected == document.Items[^1], "Каждый щелчок сразу ставит и выбирает пин со следующим номером");
        Mouse("OnMouseDown", new(1, 1)); Mouse("OnMouseDown", At(600, 300), MouseButtons.Right);
        check(document.Items.Count == 3 && document.NextPinNumber == 4 && Field<Label>("nextPin").Text.Contains('4'), "Щелчок вне снимка и правая кнопка не расходуют номера; панель показывает следующий");
        var selected = document.Items[^1]; var id = selected.Id; var count = document.Items.Count; var number = selected.PinNumber;
        Field<NumericUpDown>("pinSize").Value = 72;
        typeof(EditorForm).GetMethod("SetColor", Private)!.Invoke(form, [Color.Blue]);
        check(document.Items.Count == count && selected.Id == id && selected.PinNumber == number && selected.PinSize == 72 && selected.Color == Color.Blue && document.NextPinNumber == 4,
            "Нижняя панель меняет размер и цвет существующего пина, сохраняя номер и счётчик");
        var originalStart = selected.Start; var end = selected.End;
        Mouse("OnMouseDown", At(end.X, end.Y)); Mouse("OnMouseMove", At(end.X + 40, end.Y + 50)); Mouse("OnMouseUp", At(end.X + 40, end.Y + 50));
        check(selected.PinSize > 100 && selected.Start == originalStart && selected.PinNumber == number && document.Items.Count == count, "Угловой маркер увеличивает тот же пин, сохраняя пропорции");
        document.Undo(); check(document.Items[^1].PinSize == 72 && document.NextPinNumber == 4, "Изменение размера пина отменяется без изменения нумерации");
        document.Redo();
        typeof(EditorForm).GetMethod("SelectTool", Private)!.Invoke(form, [EditorTool.Select]);
        var pin = document.Items[^1]; var start = pin.Start; var centerPoint = At(pin.Bounds.X + pin.Bounds.Width / 2, pin.Bounds.Y + pin.PinSize / 2);
        Mouse("OnMouseDown", centerPoint); Mouse("OnMouseMove", new(centerPoint.X + 20, centerPoint.Y + 20)); Mouse("OnMouseUp", new(centerPoint.X + 20, centerPoint.Y + 20));
        check(pin.Start.X > start.X && pin.Start.Y > start.Y && pin.PinNumber == number && pin.Id == id && document.NextPinNumber == 4, "Выбор / перенос перемещает пин, сохраняя его номер");
        canvas.DeleteSelected(); check(document.Items.Count == 2 && document.NextPinNumber == 4, "Удаление выбранного пина сохраняет следующий номер");
        document.Undo();
        typeof(EditorForm).GetMethod("SelectTool", Private)!.Invoke(form, [EditorTool.Pin]);
        using (var rendered = new Bitmap(content.Width, content.Height)) { content.DrawToBitmap(rendered, content.ClientRectangle); rendered.Save(Path.Combine(output, "pin-editor-preview.png"), ImageFormat.Png); }
        using var export = document.Render();
        var firstPin = document.Items[0]; var area = Rectangle.Round(firstPin.Bounds); var colored = 0; var white = 0;
        for (var y = area.Top + 8; y < area.Top + 40; y++) for (var x = area.Left + 8; x < area.Right - 8; x++)
        {
            var p = export.GetPixel(x, y); if (p.R > 200 && p.G < 150) colored++; if (p.R > 230 && p.G > 230 && p.B > 230) white++;
        }
        check(colored > 100 && white > 10 && export.Size == document.Crop.Size, "Цветная булавка с контрастной цифрой попадает в экспорт полного разрешения");
    }
}
