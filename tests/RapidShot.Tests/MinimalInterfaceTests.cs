using System.Drawing;
using System.Reflection;
using RapidShot;

internal static class MinimalInterfaceTests
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    public static void Run(Action<bool, string> check)
    {
        foreach (var size in new[] { new Size(920, 580), new Size(740, 460) })
        {
            using var form = new EditorForm(TrayApplication.CreateDemo(), new AppSettings()) { ClientSize = size };
            using var content = form.Controls[0]; content.Font = form.Font; content.BackColor = form.BackColor; content.ForeColor = form.ForeColor;
            form.Controls.Remove(content); content.Dock = DockStyle.None; content.Size = form.ClientSize; content.CreateControl(); content.PerformLayout();
            T Field<T>(string name) => (T)typeof(EditorForm).GetField(name, Private)!.GetValue(form)!;
            var bar = Field<FloatingBar>("toolBar"); var parameters = Field<FlowLayoutPanel>("parameterBar"); var canvas = Field<EditorCanvas>("canvas");
            check(bar.Width < content.Width - 20 && Math.Abs(bar.Left + bar.Width / 2 - content.Width / 2) < 3 && bar.Bottom <= parameters.Top,
                "Компактные панели центрированы под снимком и не занимают всю ширину окна");
            check(bar.Controls.OfType<IconButton>().Where(x => x.Visible).All(x => x.Right <= bar.ClientSize.Width - bar.Padding.Right), "Иконки действий и инструментов помещаются в компактном окне");
            var menu = Field<ContextMenuStrip>("captureMenu"); var capture = Field<CaptureSplitButton>("capture");
            check(Field<IconButton>("google").Parent == bar && capture.Parent == bar && menu.Items.Contains(Field<ToolStripMenuItem>("upload")) && menu.Items.Contains(Field<ToolStripMenuItem>("save")),
                "Захват объединяет копирование, сохранение и Yapx, поиск Google остаётся в панели");
            check(canvas.Height > size.Height * .7 && form.WindowState == FormWindowState.Normal, "Снимок занимает основную часть обычного окна редактора");
            typeof(EditorForm).GetMethod("SelectTool", Private)!.Invoke(form, [EditorTool.Arrow]);
            var tools = Field<Dictionary<EditorTool, ToolGroupButton>>("tools");
            check(tools[EditorTool.Arrow].Selected && !tools[EditorTool.Rectangle].Selected && tools[EditorTool.Arrow].Symbol == UiIcon.Arrow,
                "Активный инструмент отмечен тонкой рамкой и правильной иконкой");
        }
    }
}
