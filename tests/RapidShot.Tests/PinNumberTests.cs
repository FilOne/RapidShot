using System.Drawing;
using System.Drawing.Imaging;
using System.Globalization;
using System.Numerics;
using System.Reflection;
using RapidShot;

internal static class PinNumberTests
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    public static void Run(Action<bool, string> check, string output)
    {
        using (var doc = new ScreenshotDocument(new Bitmap(1000, 600)))
        {
            var first = doc.AddPin(new(100, 100), Color.Red, 48);
            var middle = doc.AddPin(new(300, 100), Color.Blue, 48);
            var last = doc.AddPin(new(500, 100), Color.Green, 48); var lastId = last.Id;
            var lastCenter = Center(last); var color = last.Color; var size = last.PinSize;
            var shape = new Annotation { Tool = EditorTool.Rectangle, Start = new(100, 300), End = new(400, 500) }; doc.Add(shape);
            doc.Remove(middle);
            check(first.PinNumber == 1 && last.PinNumber == 2 && last.Id == lastId && Center(last) == lastCenter && last.Color == color && last.PinSize == size && doc.NextPinNumber == 3,
                "Удаление пина 2 из 1, 2, 3 превращает 3 в 2, сохраняя место, стиль и идентификатор");
            doc.Undo(); check(doc.Items.Take(3).Select(p => p.PinNumber).SequenceEqual(new BigInteger[] { 1, 2, 3 }) && doc.NextPinNumber == 4, "Одна отмена возвращает удалённый пин и все прежние номера");
            doc.Redo(); check(doc.Items.Where(p => p.Tool == EditorTool.Pin).Select(p => p.PinNumber).SequenceEqual(new BigInteger[] { 1, 2 }) && doc.NextPinNumber == 3, "Повтор удаления восстанавливает перенумерацию");
            doc.Remove(doc.Items[^1]); check(doc.NextPinNumber == 3 && doc.Items.Select(p => p.PinNumber).SequenceEqual(new BigInteger[] { 1, 2 }), "Удаление другого объекта не изменяет номера пинов");
            doc.Remove(doc.Items[^1]); check(doc.NextPinNumber == 2, "Удаление последнего пина обновляет следующий номер");
            doc.Remove(doc.Items[0]); check(doc.NextPinNumber == 1 && doc.Items.Count == 0, "Удаление всех пинов возвращает счётчик к единице");
            var pin = doc.AddPin(new(500, 300), Color.Red, 48); var next = doc.AddPin(new(700, 300), Color.Blue, 48); var originalBounds = pin.Bounds;
            var huge = BigInteger.Pow(10, 24) + 7;
            check(doc.SetPinNumber(pin, huge) && pin.PinNumber == huge && next.PinNumber == 2 && doc.NextPinNumber == huge + 1, "Ручное изменение большого номера затрагивает только выбранный пин");
            check(Center(pin) == new PointF(originalBounds.X + originalBounds.Width / 2, originalBounds.Y + originalBounds.Height / 2) && pin.Bounds.Width > originalBounds.Width, "При смене номера метка расширяется вокруг прежнего центра");
            doc.Undo(); check(doc.Items[0].PinNumber == 1 && doc.Items[0].Bounds == originalBounds && doc.NextPinNumber == 3, "Отмена ручного номера возвращает цифру, размер метки и следующий номер");
            doc.Redo(); pin = doc.Items[0]; next = doc.Items[1];
            check(pin.PinNumber == huge && doc.NextPinNumber == huge + 1, "Повтор восстанавливает вручную введённый номер");
            doc.SetPinNumber(pin, 2); check(pin.PinNumber == 2 && next.PinNumber == 2, "Ручной номер может совпадать: остальные метки остаются без изменений");
            check(!doc.SetPinNumber(pin, 0) && !doc.SetPinNumber(pin, -3) && !doc.SetPinNumber(shape, 8) && !doc.SetPinNumber(pin, 2) && doc.Items[0].PinNumber == 2,
                "Некорректный номер, повтор значения и посторонний объект не создают правку");
        }

        using var form = new EditorForm(TrayApplication.CreateDemo(), new AppSettings());
        using var content = form.Controls[0]; content.Font = form.Font; content.BackColor = form.BackColor; content.ForeColor = form.ForeColor;
        form.Controls.Remove(content); content.Dock = DockStyle.None; content.Size = form.ClientSize; content.CreateControl(); content.PerformLayout();
        T Field<T>(string name) => (T)typeof(EditorForm).GetField(name, Private)!.GetValue(form)!;
        var canvas = Field<EditorCanvas>("canvas"); var document = Field<ScreenshotDocument>("document"); var input = Field<TextBox>("pinNumber");
        void Select(EditorTool tool) => typeof(EditorForm).GetMethod("SelectTool", Private)!.Invoke(form, [tool]);
        void Commit() => typeof(EditorForm).GetMethod("CommitPinNumber", Private)!.Invoke(form, null);
        void InputKey(Keys key) => input.GetType().GetMethod("ProcessCmdKey", Private)!.Invoke(input, [new Message(), key]);
        Select(EditorTool.Pin);
        check(!input.Enabled && input.Parent!.Parent == Field<FlowLayoutPanel>("pinControls"), "Поле «Номер пина» находится снизу и ждёт выбора метки");
        using var preview = new Bitmap(canvas.Width, canvas.Height); canvas.DrawToBitmap(preview, canvas.ClientRectangle);
        var bounds = (RectangleF)typeof(EditorCanvas).GetField("imageBounds", Private)!.GetValue(canvas)!;
        Point At(float x, float y) => new((int)Math.Round(bounds.X + x / document.Crop.Width * bounds.Width), (int)Math.Round(bounds.Y + y / document.Crop.Height * bounds.Height));
        void Mouse(string name, Point p) => typeof(EditorCanvas).GetMethod(name, Private)!.Invoke(canvas, [new MouseEventArgs(MouseButtons.Left, 1, p.X, p.Y, 0)]);
        void Click(float x, float y) { var p = At(x, y); Mouse("OnMouseDown", p); Mouse("OnMouseUp", p); }
        Click(250, 250); Click(550, 250); Click(850, 250);
        check(input.Enabled && input.Text == "3", "После постановки поле показывает номер выбранного пина");
        input.Text = "12"; InputKey(Keys.Enter);
        check(document.Items[0].PinNumber == 1 && document.Items[1].PinNumber == 2 && document.Items[2].PinNumber == 12 && Field<Label>("nextPin").Text.Contains("13"), "Ввод через панель меняет один пин и обновляет следующий номер");
        foreach (var invalid in new[] { "", "0", "-1", "1.5", "abc" })
        {
            input.Text = invalid; Commit();
            check(document.Items[^1].PinNumber == 12 && input.Text == "12", $"Поле отклоняет «{invalid}» и восстанавливает прежний номер");
        }
        document.Undo(); check(document.Items[^1].PinNumber == 3 && input.Text == "3", "Некорректный ввод не засоряет историю, отмена обновляет поле номера");
        document.Redo(); check(input.Text == "12", "Повтор обновляет номер в нижней панели");
        input.Text = "900"; InputKey(Keys.Escape);
        check(document.Items[^1].PinNumber == 12 && input.Text == "12", "Esc в поле отменяет ввод номера без изменения пина");
        input.Text = "17"; typeof(Control).GetMethod("OnLeave", Private)!.Invoke(input, [EventArgs.Empty]);
        check(document.Items[^1].PinNumber == 17, "Выход из поля применяет введённый номер");
        document.Undo();
        input.Text = "3"; Commit();
        Select(EditorTool.Select); Click(550, 250);
        check(canvas.Selected?.PinNumber == 2 && input.Text == "2", "Выбор старой метки синхронизирует поле её номера");
        canvas.DeleteSelected(); check(document.Items.Select(p => p.PinNumber).SequenceEqual(new BigInteger[] { 1, 2 }) && document.NextPinNumber == 3, "Delete выполняет перенумерацию через редактор");
        document.Undo(); Select(EditorTool.Eraser);
        var p1 = At(250, 250); var p2 = At(550, 250);
        Mouse("OnMouseDown", p1); Mouse("OnMouseMove", p2); Mouse("OnMouseUp", p2);
        check(document.Items.Count == 1 && document.Items[0].PinNumber == 1 && document.NextPinNumber == 2, "Ластик перенумеровывает пины после удаления нескольких меток одним жестом");
        document.Undo(); check(document.Items.Select(p => p.PinNumber).SequenceEqual(new BigInteger[] { 1, 2, 3 }) && document.NextPinNumber == 4, "Одна отмена ластика возвращает все пины и исходную нумерацию");
        document.Redo(); document.Undo(); Select(EditorTool.Pin);
        input.Text = "21"; Commit();
        using var rendered = new Bitmap(content.Width, content.Height); content.DrawToBitmap(rendered, content.ClientRectangle); rendered.Save(Path.Combine(output, "pin-number-editor-preview.png"), ImageFormat.Png);
    }
    private static PointF Center(Annotation pin) => new((pin.Start.X + pin.End.X) / 2, (pin.Start.Y + pin.End.Y) / 2);
}
