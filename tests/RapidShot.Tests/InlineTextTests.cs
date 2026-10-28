using System.Drawing;
using System.Drawing.Imaging;
using System.Reflection;
using RapidShot;

internal static class InlineTextTests
{
    public static void Run(Action<bool, string> check, string output)
    {
        using var doc = new ScreenshotDocument(TrayApplication.CreateDemo());
        using var canvas = new EditorCanvas(doc) { Size = new(760, 480), Tool = EditorTool.Text };
        using var preview = new Bitmap(canvas.Width, canvas.Height);
        canvas.DrawToBitmap(preview, canvas.ClientRectangle);
        var flags = BindingFlags.Instance | BindingFlags.NonPublic;
        void Click(int count = 1) => typeof(EditorCanvas).GetMethod("OnMouseDown", flags)!.Invoke(canvas, [new MouseEventArgs(MouseButtons.Left, count, 120, 140, 0)]);
        TextBox Editor() => canvas.Controls.OfType<TextBox>().Single();
        void Key(Keys key)
        {
            object[] args = [new Message(), key];
            Editor().GetType().GetMethod("ProcessCmdKey", flags)!.Invoke(Editor(), args);
        }
        Click();
        check(canvas.IsEditingText && doc.Items.Count == 0 && Editor().Parent == canvas, "Текст вводится на холсте без отдельного окна");
        check(Math.Abs(Editor().Left - 120) <= 1 && Math.Abs(Editor().Top - 140) <= 1, "Поле текста появляется в точке щелчка при масштабировании");
        Editor().Text = "Первая строка\r\nВторая строка";
        canvas.DrawToBitmap(preview, canvas.ClientRectangle); preview.Save(Path.Combine(output, "inline-text-preview.png"), ImageFormat.Png);
        Key(Keys.Enter);
        check(!canvas.IsEditingText && doc.Items.Count == 1 && doc.Items[0].Text.Contains("Вторая строка"), "Enter добавляет многострочный текст в документ");
        var originalPosition = doc.Items[0].Start;
        using (var rendered = doc.Render())
        {
            var area = Rectangle.Intersect(Rectangle.Ceiling(doc.Items[0].Bounds), new Rectangle(Point.Empty, rendered.Size));
            var changedPixels = 0;
            for (var y = area.Top; y < area.Bottom; y++)
            for (var x = area.Left; x < area.Right; x++)
                if (rendered.GetPixel(x, y).ToArgb() != doc.Original.GetPixel(x, y).ToArgb()) changedPixels++;
            check(changedPixels > 50, "Введённый текст попадает в экспорт изображения");
        }
        Click();
        check(Editor().Text.Contains("Первая строка") && doc.Items.Count == 1, "Щелчок инструментом текста редактирует существующую надпись");
        Editor().Text = "Изменённый текст"; Key(Keys.Enter);
        check(doc.Items.Count == 1 && doc.Items[0].Text == "Изменённый текст" && doc.Items[0].Start == originalPosition, "Редактирование заменяет текст на прежнем месте");
        doc.Undo(); check(doc.Items[0].Text.Contains("Первая строка"), "Отмена восстанавливает прежний текст");
        doc.Redo(); check(doc.Items[0].Text == "Изменённый текст", "Повтор восстанавливает изменение текста");
        canvas.Tool = EditorTool.Select; Click(2);
        check(canvas.IsEditingText && Editor().Text == "Изменённый текст", "Двойной щелчок инструментом выбора открывает текст");
        Editor().Text = "Не сохранять"; Key(Keys.Escape);
        check(!canvas.IsEditingText && doc.Items[0].Text == "Изменённый текст", "Esc отменяет правку существующего текста");
        canvas.Tool = EditorTool.Text; Click(); Editor().Text = ""; canvas.CommitText();
        check(doc.Items.Count == 0, "Пустой текст удаляет надпись");
        doc.Undo(); check(doc.Items.Count == 1, "Удаление текста поддерживает отмену");
        Click(); Editor().Text = "Новый стиль"; canvas.Ink = Color.Blue; canvas.TextSize = 42; canvas.UpdateSelectedStyle(); canvas.CommitText();
        check(doc.Items[0].FontSize == 42 && doc.Items[0].Color == Color.Blue, "Цвет и размер применяются к редактируемому тексту");
        doc.Undo(); check(doc.Items[0].Text == "Изменённый текст" && doc.Items[0].FontSize == 28, "Текст и стиль отменяются одним действием");
        doc.BeginChange(); doc.Items.Clear(); doc.NotifyChanged();
        Click(); Editor().Text = "Отмена создания"; Key(Keys.Escape);
        check(doc.Items.Count == 0 && !canvas.IsEditingText, "Esc отменяет создание надписи без пустого объекта");
        Click(); Editor().Text = "Сохранить перед экспортом"; canvas.CommitText();
        check(doc.Items.Single().Text == "Сохранить перед экспортом", "Завершение ввода сохраняет текущий текст перед экспортом");
    }
}
