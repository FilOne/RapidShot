using System.Drawing.Drawing2D;

namespace RapidShot;

internal sealed partial class EditorCanvas : Control
{
    public ScreenshotDocument Document { get; }
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public EditorTool Tool
    {
        get => tool;
        set
        {
            if (tool == value) return;
            CommitText(); FinishObjectTransform(); dragging = false; Capture = false; pending = null;
            lastPenStrokeAt = 0;
            tool = value; cropDraft = value == EditorTool.Crop ? Document.Crop : null;
            cropDragEdge = CropEdge.None; CropStateChanged?.Invoke(); Invalidate();
        }
    }
    private EditorTool tool = EditorTool.Pen;
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public bool DoubleHeaded { get; set; }
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public bool RecognizeShapes { get => recognizeShapes; set { recognizeShapes = value; lastPenStrokeAt = 0; } }
    private bool recognizeShapes = true;
    private long lastPenStrokeAt;
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public Color Ink { get; set; } = Color.FromArgb(255, 88, 100);
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public float StrokeWidth { get; set; } = 4;
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public int Opacity { get; set; } = 100;
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public float TextSize { get; set; } = 28;
    public Annotation? Selected { get; private set; }
    public event Action? SelectionChanged;
    public event Action<EditorTool>? ToolActivated;
    public event Action<string>? HintChanged;
    private Bitmap? preview;
    private Annotation? pending;
    private PointF anchor, last;
    private bool dragging, erased;
    private float zoom = 1;
    private RectangleF imageBounds;
    private InlineTextBox? textEditor;
    private Annotation? textOriginal, textDraft;
    private Font? textEditorFont;
    public bool IsEditingText => textEditor != null;

    public EditorCanvas(ScreenshotDocument document)
    {
        Document = document; DoubleBuffered = true; BackColor = Theme.Background;
        SetStyle(ControlStyles.ResizeRedraw | ControlStyles.Selectable, true);
        TabStop = true;
        Document.Changed += Rebuild;
        Rebuild();
    }
    public void Rebuild()
    {
        lastPenStrokeAt = 0;
        previewDirty = true;
        if (Tool == EditorTool.Crop) cropDraft = Document.Crop;
        var previous = Selected;
        if (Selected != null && !Document.Items.Contains(Selected) && Selected != textDraft) Selected = Document.Items.FirstOrDefault(x => x.Id == Selected.Id);
        if (Selected == null && !IsEditingText && Document.Items.LastOrDefault() is { } last && last.Tool == Tool) Selected = last;
        UpdateViewLayout(); PositionTextEditor();
        if (!ReferenceEquals(previous, Selected)) SelectionChanged?.Invoke();
        Invalidate();
    }
    public void ActivateTool(EditorTool newTool)
    {
        CommitEdits(); CancelGesture(false); Tool = newTool;
        if (newTool != EditorTool.Select && Selected?.Tool != newTool) Selected = null;
        if (Selected == null && Document.Items.LastOrDefault() is { } last && last.Tool == newTool) Selected = last;
        SelectionChanged?.Invoke(); Invalidate();
    }
    public void ClearSelection() { Selected = null; SelectionChanged?.Invoke(); Invalidate(); }
    public void DeleteSelected()
    {
        FinishObjectTransform();
        CommitText();
        if (Selected == null) return;
        Document.BeginChange(); Document.Items.Remove(Selected); Selected = null; Document.NotifyChanged(); SelectionChanged?.Invoke();
    }
    public void UpdateSelectedStyle(bool recordUndo = true)
    {
        if (textDraft != null)
        {
            textDraft.Color = Ink; textDraft.Width = StrokeWidth; textDraft.FontSize = TextSize;
            MeasureText(textDraft); PositionTextEditor(); return;
        }
        if (Selected == null) return;
        if (recordUndo) Document.BeginChange();
        Selected.Color = Ink; Selected.Width = StrokeWidth; Selected.FontSize = TextSize;
        if (Annotation.IsStrokeTool(Selected.Tool)) Selected.Opacity = Opacity;
        if (Selected.Tool == EditorTool.Arrow) Selected.DoubleHeaded = DoubleHeaded;
        if (Selected.Tool == EditorTool.Text) MeasureText(Selected);
        Document.NotifyChanged();
    }
    private void UpdateViewLayout()
    {
        var size = Document.Crop.Size;
        zoom = Math.Min((ClientSize.Width - 32f) / size.Width, (ClientSize.Height - 52f) / size.Height);
        zoom = Math.Max(0.01f, Math.Min(zoom, 2));
        imageBounds = new((ClientSize.Width - size.Width * zoom) / 2, (ClientSize.Height - 22 - size.Height * zoom) / 2, size.Width * zoom, size.Height * zoom);
    }
    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e); previewDirty = true; ClearTransformPreview(); UpdateViewLayout(); PositionTextEditor(); Invalidate();
    }
    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        UpdateViewLayout();
        EnsurePreview();
        if (preview == null) return;
        var g = e.Graphics;
        using var shadow = new SolidBrush(Color.FromArgb(12, 14, 20));
        g.FillRectangle(shadow, imageBounds.X + 7, imageBounds.Y + 9, imageBounds.Width, imageBounds.Height);
        g.InterpolationMode = zoom == 1 ? InterpolationMode.NearestNeighbor : InterpolationMode.HighQualityBicubic;
        g.DrawImage(preview, imageBounds);
        var state = g.Save();
        g.SetClip(imageBounds);
        g.TranslateTransform(imageBounds.X, imageBounds.Y); g.ScaleTransform(zoom, zoom); g.TranslateTransform(-Document.Crop.X, -Document.Crop.Y);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        if (pending != null)
        {
            if (pending.Tool == EditorTool.Blur)
            {
                using var fill = new SolidBrush(Color.FromArgb(50, Theme.Accent)); g.FillRectangle(fill, pending.Bounds);
                using var border = new Pen(Theme.Accent, 1.5f / zoom) { DashStyle = DashStyle.Dash }; g.DrawRectangle(border, pending.Bounds.X, pending.Bounds.Y, pending.Bounds.Width, pending.Bounds.Height);
            }
            else pending.Draw(g);
        }
        g.Restore(state);
        DrawObjectSelection(g);
        DrawCropOverlay(g);
        TextRenderer.DrawText(g, $"{Document.Crop.Width} × {Document.Crop.Height} px   ·   {zoom:P0}", Font, new Point((int)imageBounds.X, (int)imageBounds.Bottom + 9), Theme.Muted);
    }
    private PointF ToImage(Point point) => new(Math.Clamp((point.X - imageBounds.X) / zoom + Document.Crop.X, Document.Crop.Left, Document.Crop.Right), Math.Clamp((point.Y - imageBounds.Y) / zoom + Document.Crop.Y, Document.Crop.Top, Document.Crop.Bottom));
    private Annotation? Find(PointF point) => Document.Items.LastOrDefault(x => x.HitTest(point, 5 / zoom));
    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e); Focus();
        if (e.Button != MouseButtons.Left) return;
        if (Tool == EditorTool.Crop) { StartCropDrag(e); return; }
        if (!IsEditingText && StartHandleDrag(e)) return;
        if (!imageBounds.Contains(e.Location)) return;
        var p = ToImage(e.Location); anchor = last = p;
        if (Tool == EditorTool.Text)
        {
            BeginText(p, Find(p) is { Tool: EditorTool.Text } text ? text : null);
            return;
        }
        if (Tool == EditorTool.Select && e.Clicks > 1 && Find(p) is { Tool: EditorTool.Text } existing)
        {
            dragging = false; Capture = false; BeginText(p, existing);
            return;
        }
        dragging = true; Capture = true; erased = false;
        if (Tool == EditorTool.Select)
        {
            Selected = Find(p); SelectionChanged?.Invoke();
            if (Selected != null) StartObjectTransform(ObjectHandle.Move, p);
            Invalidate(); return;
        }
        ClearSelection();
        if (Tool == EditorTool.Eraser) { Erase(p); return; }
        pending = new Annotation { Tool = Tool, Start = p, End = p, Color = Ink, Width = StrokeWidth, Opacity = Opacity, DoubleHeaded = DoubleHeaded, Points = [p] };
    }
    private void MeasureText(Annotation item)
    {
        using var bitmap = new Bitmap(1, 1); using var g = Graphics.FromImage(bitmap);
        using var font = new Font("Segoe UI", item.FontSize, FontStyle.Bold, GraphicsUnit.Pixel);
        var size = g.MeasureString(item.Text, font, int.MaxValue, StringFormat.GenericTypographic);
        item.End = new(item.Start.X + size.Width, item.Start.Y + size.Height);
    }
    private void BeginText(PointF point, Annotation? original)
    {
        CommitText();
        if (tool != EditorTool.Text) { tool = EditorTool.Text; ToolActivated?.Invoke(tool); }
        textOriginal = original;
        textDraft = original?.Clone() ?? new Annotation { Tool = EditorTool.Text, Start = point, FontSize = TextSize, Color = Ink, Width = StrokeWidth };
        Selected = textDraft;
        textEditor = new InlineTextBox { Multiline = true, AcceptsReturn = true, WordWrap = false, BorderStyle = BorderStyle.FixedSingle, BackColor = Color.White, ForeColor = textDraft.Color, Text = textDraft.Text, AccessibleName = "Текст на скриншоте" };
        textEditor.TextChanged += (_, _) =>
        {
            if (textDraft == null || textEditor == null) return;
            textDraft.Text = textEditor.Text; MeasureText(textDraft); PositionTextEditor();
        };
        textEditor.AcceptEdit += CommitText; textEditor.CancelEdit += CancelText;
        textEditor.Leave += (_, _) => CommitText();
        Controls.Add(textEditor); Rebuild(); SelectionChanged?.Invoke();
        textEditor.BringToFront(); textEditor.Focus();
        if (original != null) textEditor.SelectAll();
        HintChanged?.Invoke("Enter — готово · Shift+Enter — новая строка · Esc — отмена");
    }
    private void PositionTextEditor()
    {
        if (textEditor == null || textDraft == null) return;
        var pixelSize = Math.Max(8, textDraft.FontSize * zoom);
        if (textEditorFont == null || Math.Abs(textEditorFont.Size - pixelSize) > 0.05f)
        {
            var previous = textEditorFont;
            textEditorFont = new Font("Segoe UI", pixelSize, FontStyle.Bold, GraphicsUnit.Pixel); textEditor.Font = textEditorFont;
            previous?.Dispose();
        }
        textEditor.ForeColor = textDraft.Color;
        var x = (int)Math.Round(imageBounds.X + (textDraft.Start.X - Document.Crop.X) * zoom);
        var y = (int)Math.Round(imageBounds.Y + (textDraft.Start.Y - Document.Crop.Y) * zoom);
        x = Math.Clamp(x, 0, Math.Max(0, ClientSize.Width - 20)); y = Math.Clamp(y, 0, Math.Max(0, ClientSize.Height - 20));
        var size = TextRenderer.MeasureText(textEditor.Text.Length > 0 ? textEditor.Text : "Текст", textEditor.Font, new Size(int.MaxValue, int.MaxValue), TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix);
        textEditor.Bounds = new Rectangle(x, y, Math.Min(Math.Max(120, size.Width + 14), Math.Max(20, ClientSize.Width - x - 2)), Math.Min(Math.Max(textEditor.Font.Height + 10, size.Height + 10), Math.Max(20, ClientSize.Height - y - 2)));
    }
    public void CommitText()
    {
        if (textEditor == null || textDraft == null) return;
        var original = textOriginal; var draft = textDraft; draft.Text = textEditor.Text;
        CloseTextEditor();
        if (string.IsNullOrWhiteSpace(draft.Text))
        {
            if (original != null && Document.Items.Contains(original)) { Document.BeginChange(); Document.Items.Remove(original); Document.NotifyChanged(); }
            Selected = null;
        }
        else if (original != null && draft.Text == original.Text && draft.Color == original.Color && draft.FontSize == original.FontSize && draft.Width == original.Width)
            Selected = original;
        else
        {
            MeasureText(draft);
            if (original == null) { Selected = draft; Document.Add(draft); }
            else
            {
                var index = Document.Items.IndexOf(original);
                if (index >= 0) { Document.BeginChange(); Document.Items[index] = draft; Selected = draft; Document.NotifyChanged(); }
            }
        }
        Rebuild(); SelectionChanged?.Invoke();
        HintChanged?.Invoke("Текст: щелчок инструментом T или двойной щелчок инструментом выбора — редактировать.");
    }
    private void CancelText()
    {
        if (textEditor == null) return;
        var original = textOriginal; CloseTextEditor(); Selected = original;
        Rebuild(); SelectionChanged?.Invoke(); HintChanged?.Invoke("Изменение текста отменено.");
    }
    private void CloseTextEditor()
    {
        var editor = textEditor; textEditor = null; textDraft = null; textOriginal = null;
        if (editor != null) { Controls.Remove(editor); editor.Dispose(); }
        textEditorFont?.Dispose(); textEditorFont = null;
    }
    private sealed class InlineTextBox : TextBox
    {
        public event Action? AcceptEdit;
        public event Action? CancelEdit;
        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (keyData == Keys.Escape) { CancelEdit?.Invoke(); return true; }
            if (keyData == Keys.Enter || keyData == (Keys.Control | Keys.Enter)) { AcceptEdit?.Invoke(); return true; }
            return base.ProcessCmdKey(ref msg, keyData);
        }
    }
    private void Erase(PointF point)
    {
        var item = Find(point); if (item == null) return;
        if (!erased) { Document.BeginChange(); erased = true; }
        Document.Items.Remove(item); Document.NotifyChanged();
    }
    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (Tool == EditorTool.Crop) { MoveCropDrag(e); return; }
        var handle = IsEditingText ? ObjectHandle.None : HitObjectHandle(e.Location);
        Cursor = ObjectCursor(transformOriginal != null ? transformHandle : handle);
        if (transformOriginal == null && handle == ObjectHandle.None)
            Cursor = Tool == EditorTool.Select ? (Find(ToImage(e.Location)) != null ? Cursors.SizeAll : Cursors.Default) : Tool == EditorTool.Text ? Cursors.IBeam : Cursors.Cross;
        if (!dragging) return;
        var p = ToImage(e.Location);
        if (transformOriginal != null) { MoveObjectTransform(p); return; }
        if (Tool == EditorTool.Eraser) { Erase(p); return; }
        if (pending == null) return;
        pending.End = p;
        if (Tool == EditorTool.Pen && (Math.Abs(p.X - last.X) + Math.Abs(p.Y - last.Y) > 0.6f)) { pending.Points.Add(p); last = p; }
        if (Tool == EditorTool.Blur) HintChanged?.Invoke($"Выделено: {(int)pending.Bounds.Width} × {(int)pending.Bounds.Height} px");
        Invalidate();
    }
    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        if (Tool == EditorTool.Crop) { EndCropDrag(e); return; }
        if (!dragging || e.Button != MouseButtons.Left) return;
        if (transformOriginal != null) { MoveObjectTransform(ToImage(e.Location)); FinishObjectTransform(); return; }
        // Include the release point, even if Windows coalesced the final move event.
        if (pending != null) { pending.End = ToImage(e.Location); if (Tool == EditorTool.Pen) pending.Points.Add(pending.End); }
        dragging = false; Capture = false;
        var item = pending; pending = null;
        if (item != null)
        {
            if (item.Tool == EditorTool.Pen || Math.Abs(item.Start.X - item.End.X) + Math.Abs(item.Start.Y - item.End.Y) > 3)
            {
                var recognized = item.Tool == EditorTool.Pen && RecognizeShapes
                    ? FreehandRecognizer.Recognize(item, Environment.TickCount64 - lastPenStrokeAt < 4000 ? Document.Items : []) : null;
                Selected = recognized?.Item ?? item;
                if (recognized is { PreviousStrokes: > 0 })
                {
                    Document.BeginChange(); Document.Items.RemoveRange(Document.Items.Count - recognized.PreviousStrokes, recognized.PreviousStrokes);
                    Document.Items.Add(Selected); Document.NotifyChanged();
                }
                else Document.Add(Selected);
                lastPenStrokeAt = item.Tool == EditorTool.Pen && RecognizeShapes && recognized == null ? Environment.TickCount64 : 0;
                SelectionChanged?.Invoke();
                if (recognized != null) HintChanged?.Invoke(recognized.Item.Tool == EditorTool.Arrow ? "Рисунок преобразован в стрелку. Параметры и концы можно редактировать." : "Замкнутый контур преобразован в круг. Размер можно менять за маркеры.");
            }
        }
        Invalidate();
    }
    public void CancelGesture(bool clearSelection = true)
    {
        lastPenStrokeAt = 0;
        CancelText();
        FinishObjectTransform();
        CancelCrop();
        pending = null; dragging = false; Capture = false;
        if (clearSelection) ClearSelection(); Invalidate();
    }
    protected override void OnMouseCaptureChanged(EventArgs e)
    {
        base.OnMouseCaptureChanged(e);
        if (!Capture && dragging) { FinishObjectTransform(); dragging = false; cropDragEdge = CropEdge.None; pending = null; Invalidate(); }
    }
    protected override void Dispose(bool disposing)
    {
        if (disposing) { CloseTextEditor(); Document.Changed -= Rebuild; preview?.Dispose(); ClearTransformPreview(); }
        base.Dispose(disposing);
    }
}
