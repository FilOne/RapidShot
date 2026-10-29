using System.Drawing;
using System.Drawing.Imaging;
using System.Reflection;
using RapidShot;

internal static class ShiftConstraintTests
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    private sealed class View : IDisposable
    {
        public ScreenshotDocument Doc { get; } = new(TrayApplication.CreateDemo());
        public EditorCanvas Canvas { get; }
        private Keys modifiers;
        public View(EditorTool tool)
        {
            Doc.CropTo(new(100, 80, 1000, 600));
            Canvas = new(Doc, () => modifiers) { Size = new(800, 520), Tool = tool };
            Paint();
        }
        public T Field<T>(string name) => (T)typeof(EditorCanvas).GetField(name, Private)!.GetValue(Canvas)!;
        public void Paint(string? file = null)
        {
            using var image = new Bitmap(Canvas.Width, Canvas.Height); Canvas.DrawToBitmap(image, Canvas.ClientRectangle);
            if (file != null) image.Save(file, ImageFormat.Png);
        }
        public void Mouse(string name, PointF p)
        {
            var b = Field<RectangleF>("imageBounds");
            var x = (int)Math.Round(b.X + (p.X - Doc.Crop.X) * b.Width / Doc.Crop.Width);
            var y = (int)Math.Round(b.Y + (p.Y - Doc.Crop.Y) * b.Height / Doc.Crop.Height);
            typeof(EditorCanvas).GetMethod(name, Private)!.Invoke(Canvas, [new MouseEventArgs(MouseButtons.Left, 1, x, y, 0)]);
        }
        public void Shift(bool pressed)
        {
            modifiers = pressed ? Keys.Shift : Keys.None;
            typeof(EditorCanvas).GetMethod(pressed ? "OnKeyDown" : "OnKeyUp", Private)!.Invoke(Canvas, [new KeyEventArgs(Keys.ShiftKey | modifiers)]);
        }
        public void Dispose() { Canvas.Dispose(); Doc.Dispose(); }
    }
    private static Annotation Shape(EditorTool tool) => new() { Tool = tool, Start = new(300, 220), End = new(540, 340), Color = Color.Blue, Width = 5, Opacity = 60 };
    private static bool Square(RectangleF b) => Math.Abs(b.Width - b.Height) < .01 && b.Width >= 2;
    private static bool Near(float a, float b) => Math.Abs(a - b) < 2;

    public static void Run(Action<bool, string> check, string output)
    {
        foreach (var tool in new[] { EditorTool.Rectangle, EditorTool.Ellipse })
        {
            using var view = new View(tool);
            view.Mouse("OnMouseDown", new(250, 200)); view.Mouse("OnMouseMove", new(410, 260));
            var pending = view.Field<Annotation>("pending");
            check(!Square(pending.Bounds), $"{tool}: обычное рисование позволяет свободные пропорции");
            view.Shift(true);
            check(Square(pending.Bounds) && view.Doc.Items.Count == 0, $"{tool}: нажатие Shift сразу выравнивает черновик без движения мыши");
            view.Shift(false); check(!Square(pending.Bounds), $"{tool}: отпускание Shift сразу возвращает свободные пропорции черновика");
            view.Shift(true); view.Mouse("OnMouseUp", new(410, 260));
            check(Square(view.Doc.Items.Single().Bounds), $"{tool}: отпускание мыши сохраняет точный квадрат или круг");
            view.Doc.Undo(); check(view.Doc.Items.Count == 0, $"{tool}: создание с Shift отменяется одним действием");
        }
        foreach (var tool in new[] { EditorTool.Rectangle, EditorTool.Ellipse })
        {
            using var view = new View(tool); var item = Shape(tool); view.Doc.Add(item); var original = item.Bounds; var id = item.Id;
            view.Mouse("OnMouseDown", item.End); view.Mouse("OnMouseMove", new(600, 370));
            check(!Square(item.Bounds), $"{tool}: обычный размер изменяется свободно");
            view.Shift(true);
            check(Square(item.Bounds) && item.Start == original.Location && item.Id == id && item.Color == Color.Blue && item.Opacity == 60, $"{tool}: Shift немедленно выравнивает тот же объект вокруг противоположного угла");
            view.Shift(false); check(Near(item.Bounds.Width, 300) && Near(item.Bounds.Height, 150), $"{tool}: отпускание Shift возвращает текущую свободную рамку");
            view.Shift(true); view.Mouse("OnMouseMove", new(420, 260));
            check(Square(item.Bounds) && item.Bounds.Width < 100 && item.Start == original.Location, $"{tool}: Shift равномерно уменьшает объект за угол");
            view.Mouse("OnMouseUp", new(420, 260)); view.Shift(false);
            var small = item.Bounds; view.Doc.Undo();
            check(view.Doc.Items.Single().Bounds == original && view.Doc.Items[0].Id == id, $"{tool}: смена Shift во время жеста отменяется вместе со всем изменением размера");
            view.Doc.Redo(); check(view.Doc.Items[0].Bounds == small && Square(small), $"{tool}: повтор возвращает равномерно уменьшенный объект");
        }
        using (var view = new View(EditorTool.Rectangle))
        {
            var item = Shape(EditorTool.Rectangle); view.Doc.Add(item); var original = item.Bounds;
            view.Shift(true); check(item.Bounds == original, "Shift вне жеста не меняет выбранный объект и историю");
            view.Mouse("OnMouseDown", item.End);
            check(Square(item.Bounds) && Near(item.Bounds.Width, 180), "Заранее зажатый Shift сразу выравнивает объект при захвате маркера");
            view.Mouse("OnMouseUp", new(original.Right, original.Bottom));
            view.Doc.Undo(); check(view.Doc.Items[0].Bounds == original, "Даже выравнивание без движения мыши имеет одну отменяемую правку");
        }
        PointF[] handles = [new(300, 220), new(540, 220), new(540, 340), new(300, 340), new(300, 280), new(540, 280), new(420, 220), new(420, 340)];
        for (var index = 0; index < handles.Length; index++)
        {
            using var view = new View(EditorTool.Rectangle); var item = Shape(EditorTool.Rectangle); view.Doc.Add(item);
            view.Shift(true); var point = handles[index]; view.Mouse("OnMouseDown", point); view.Mouse("OnMouseMove", new(point.X + 25, point.Y + 20)); view.Mouse("OnMouseUp", new(point.X + 25, point.Y + 20));
            check(Square(item.Bounds), $"Маркер {index + 1}: Shift сохраняет равные стороны при угловом и боковом изменении");
        }
        using (var view = new View(EditorTool.Ellipse))
        {
            var item = Shape(EditorTool.Ellipse); item.EllipseAngle = 35; view.Doc.Add(item); var original = item.Clone(); var bounds = item.GeometryBounds;
            view.Mouse("OnMouseDown", new(bounds.Right, bounds.Bottom)); view.Shift(true);
            check(Square(item.Bounds) && item.EllipseAngle == 0, "Наклонный эллипс с Shift становится настоящим кругом, а не квадратной рамкой вокруг овала");
            view.Mouse("OnMouseMove", new(bounds.Right - 60, bounds.Bottom - 20)); view.Mouse("OnMouseUp", new(bounds.Right - 60, bounds.Bottom - 20));
            view.Paint(Path.Combine(output, "shift-circle-preview.png"));
            view.Doc.Undo(); check(view.Doc.Items[0].Bounds == original.Bounds && view.Doc.Items[0].EllipseAngle == original.EllipseAngle, "Отмена выравнивания возвращает размеры и наклон исходного эллипса");
        }
        using (var view = new View(EditorTool.Blur))
        {
            var item = Shape(EditorTool.Blur); view.Doc.Add(item); view.Shift(true);
            view.Mouse("OnMouseDown", item.End); view.Mouse("OnMouseMove", new(650, 400)); view.Mouse("OnMouseUp", new(650, 400));
            check(Math.Abs(item.Bounds.Width / item.Bounds.Height - 2) < .01, "Для области размытия Shift сохраняет исходное соотношение сторон");
        }
        using (var view = new View(EditorTool.Rectangle))
        {
            view.Shift(true); view.Mouse("OnMouseDown", new(150, 130)); view.Mouse("OnMouseMove", new(1090, 675)); view.Mouse("OnMouseUp", new(1090, 675));
            var item = view.Doc.Items.Single(); check(Square(item.Bounds) && item.Bounds.Right <= view.Doc.Crop.Right && item.Bounds.Bottom <= view.Doc.Crop.Bottom, "Квадрат с Shift ограничивается размером обрезанного снимка");
            view.Canvas.ClearSelection(); view.Mouse("OnMouseDown", new(900, 580)); view.Mouse("OnMouseMove", new(700, 530)); view.Mouse("OnMouseUp", new(700, 530));
            check(Square(view.Doc.Items[^1].Bounds), "Shift работает при рисовании из правого нижнего угла в левый верхний");
        }
    }
}
