using System.Drawing;
using System.Drawing.Imaging;
using System.Reflection;
using RapidShot;

internal static class LiveAnnotationTests
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    public static void Run(Action<bool, string> check, string output)
    {
        using var form = new EditorForm(TrayApplication.CreateDemo(), new AppSettings());
        using var content = form.Controls[0]; content.Font = form.Font; content.BackColor = form.BackColor; content.ForeColor = form.ForeColor;
        form.Controls.Remove(content); content.Dock = DockStyle.None; content.Size = form.ClientSize; content.CreateControl(); content.PerformLayout();
        T Field<T>(string name) => (T)typeof(EditorForm).GetField(name, Private)!.GetValue(form)!;
        void Select(EditorTool tool) => typeof(EditorForm).GetMethod("SelectTool", Private)!.Invoke(form, [tool]);
        var canvas = Field<EditorCanvas>("canvas"); var doc = Field<ScreenshotDocument>("document");
        var one = Field<RadioButton>("arrowOne"); var two = Field<RadioButton>("arrowTwo");
        var width = Field<ParameterSlider>("width"); var opacity = Field<ParameterSlider>("transparency");
        var bar = Field<FlowLayoutPanel>("parameterBar");
        var parameterNames = new[] { "colorControls", "strokeControls", "arrowControls", "fontControls", "cropControls" };
        check(parameterNames.All(name => Field<Control>(name).Parent == bar), "Все параметры инструментов объединены в нижней панели");
        var layout = (TableLayoutPanel)bar.Parent!;
        check(layout.GetPositionFromControl(bar).Row > layout.GetPositionFromControl(Field<Control>("workspace")).Row, "Панель параметров находится под снимком");
        using var paint = new Bitmap(canvas.Width, canvas.Height); canvas.DrawToBitmap(paint, canvas.ClientRectangle);
        var bounds = (RectangleF)typeof(EditorCanvas).GetField("imageBounds", Private)!.GetValue(canvas)!;
        Point At(float x, float y) => new((int)(bounds.Left + bounds.Width * x), (int)(bounds.Top + bounds.Height * y));
        void Mouse(string name, Point p) => typeof(EditorCanvas).GetMethod(name, Private)!.Invoke(canvas, [new MouseEventArgs(MouseButtons.Left, 1, p.X, p.Y, 0)]);
        void Draw(float y)
        {
            var start = At(.25f, y); var end = At(.75f, y);
            Mouse("OnMouseDown", start); Mouse("OnMouseMove", end); Mouse("OnMouseUp", end);
        }
        Select(EditorTool.Arrow); Draw(.35f);
        var arrow = doc.Items.Single(); var id = arrow.Id; var start = arrow.Start; var end = arrow.End;
        check(ReferenceEquals(canvas.Selected, arrow), "Только что созданная стрелка сразу активна для редактирования");
        two.Checked = true;
        check(doc.Items.Count == 1 && ReferenceEquals(doc.Items[0], arrow) && arrow.DoubleHeaded && arrow.Start == start && arrow.End == end, "Переключение на два наконечника меняет ту же стрелку на месте");
        one.Checked = true;
        check(doc.Items.Count == 1 && arrow.Id == id && !arrow.DoubleHeaded, "Переключение обратно не добавляет новую стрелку");
        width.Value = 8; opacity.Value = 30;
        typeof(EditorForm).GetMethod("SetColor", Private)!.Invoke(form, [Color.Blue]);
        check(doc.Items.Count == 1 && arrow.Id == id && arrow.Width == 8 && arrow.Opacity == 70 && arrow.Color == Color.Blue && arrow.Start == start && arrow.End == end, "Цвет, толщина и прозрачность меняют активную стрелку без перерисовывания");
        Select(EditorTool.Select); Select(EditorTool.Arrow); two.Checked = true;
        check(canvas.Selected?.Id == id && doc.Items.Count == 1 && doc.Items[0].DoubleHeaded, "Возврат к инструменту создания сохраняет активную пометку");
        typeof(EditorForm).GetMethod("Undo", Private)!.Invoke(form, null);
        check(doc.Items[0].Id == id && canvas.Selected?.Id == id && !doc.Items[0].DoubleHeaded && one.Checked, "Отмена сохраняет выделение и восстанавливает параметры активной стрелки");
        doc.Redo();
        check(canvas.Selected?.Id == id && doc.Items[0].DoubleHeaded && two.Checked, "Повтор синхронизирует наконечники, сохраняя активную стрелку");
        using (var rendered = new Bitmap(content.Width, content.Height)) { content.DrawToBitmap(rendered, content.ClientRectangle); rendered.Save(Path.Combine(output, "live-arrow-editor-preview.png"), ImageFormat.Png); }
        Draw(.6f); var second = doc.Items[^1]; one.Checked = true;
        check(doc.Items.Count == 2 && canvas.Selected?.Id == second.Id && !second.DoubleHeaded && doc.Items[0].DoubleHeaded, "После второго рисунка параметры меняют только последнюю стрелку");
        Select(EditorTool.Blur);
        var top = At(.3f, .2f); var bottom = At(.55f, .5f);
        Mouse("OnMouseDown", top); Mouse("OnMouseMove", bottom); Mouse("OnMouseUp", bottom);
        var blur = doc.Items[^1]; var blurCount = doc.Items.Count; width.Value = 12;
        check(ReferenceEquals(canvas.Selected, blur) && doc.Items.Count == blurCount && blur.Width == 12, "Сила последнего размытия редактируется без создания новой области");
        Select(EditorTool.Text); Mouse("OnMouseDown", At(.35f, .75f));
        canvas.Controls.OfType<TextBox>().Single().Text = "Редактируемый текст"; canvas.CommitText();
        var text = doc.Items[^1]; var textId = text.Id; var textPosition = text.Start; Field<NumericUpDown>("fontSize").Value = 42;
        check(doc.Items[^1].Id == textId && text.FontSize == 42 && text.Start == textPosition && ReferenceEquals(canvas.Selected, text), "Размер текста меняет последнюю надпись на прежнем месте");
        Select(EditorTool.Crop);
        check(Field<FlowLayoutPanel>("cropControls").Visible && !Field<FlowLayoutPanel>("colorControls").Visible, "Параметры обрезки открываются в той же нижней панели");
    }
}
