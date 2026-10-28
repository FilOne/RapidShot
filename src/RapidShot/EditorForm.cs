using System.Drawing.Imaging;

namespace RapidShot;

internal sealed class EditorForm : Form
{
    private readonly ScreenshotDocument document;
    private readonly EditorCanvas canvas;
    private readonly AppSettings settings;
    private readonly Label status, activeItem;
    private readonly Button undo, redo, save, copy, upload, delete, customColor;
    private readonly Dictionary<EditorTool, ToolGroupButton> tools = [];
    private readonly Dictionary<EditorTool, string> toolNames = [];
    private readonly NumericUpDown fontSize;
    private readonly ParameterSlider width, transparency;
    private readonly Label widthLabel, transparencyLabel;
    private readonly FlowLayoutPanel parameterBar, strokeControls, colorControls, arrowEnds, cropControls;
    private readonly Panel transparencyControl, fontControls, arrowControls;
    private readonly RadioButton arrowOne, arrowTwo;
    private readonly CheckBox recognizeShapes;
    private readonly Button applyCrop, resetCrop;
    private readonly TableLayoutPanel workspace;
    private readonly ToolTip tips = new();
    private bool sliderUndoRecorded, exported, updatingStyle;
    private CancellationTokenSource? uploading;

    public EditorForm(Bitmap image, AppSettings settings)
    {
        this.settings = settings; document = new(image);
        Theme.Apply(this); Font = new Font("Segoe UI", 9.5f);
        Text = "RapidShot — редактор"; ClientSize = new(920, 580); MinimumSize = new(740, 480);
        KeyPreview = true;
        var root = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 5, ColumnCount = 1, Margin = Padding.Empty, Padding = Padding.Empty };
        root.RowStyles.Add(new(SizeType.Absolute, 52)); root.RowStyles.Add(new(SizeType.Percent, 100));
        root.RowStyles.Add(new(SizeType.Absolute, 40)); root.RowStyles.Add(new(SizeType.Absolute, 46)); root.RowStyles.Add(new(SizeType.Absolute, 26));
        var header = new TableLayoutPanel { Dock = DockStyle.Fill, BackColor = Theme.Panel, ColumnCount = 2, Padding = new Padding(12, 6, 10, 6) };
        header.ColumnStyles.Add(new(SizeType.Percent, 100)); header.ColumnStyles.Add(new(SizeType.AutoSize));
        header.Controls.Add(new Label { Text = "RAPID SHOT", Font = new Font("Segoe UI", 13, FontStyle.Bold), ForeColor = Theme.Accent, Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, Margin = Padding.Empty });
        var actions = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, WrapContents = false };
        save = Theme.Button("Сохранить…", (_, _) => SaveImage()); tips.SetToolTip(save, "Сохранить PNG или JPEG · Ctrl+S");
        copy = Theme.Button("В буфер", (_, _) => CopyImage()); tips.SetToolTip(copy, "Скопировать изображение · Ctrl+C");
        upload = Theme.Button("Загрузить на Yapx ↗", async (_, _) => await Upload(), true);
        upload.AccessibleName = "Загрузить на Yapx"; tips.SetToolTip(upload, "Загрузить снимок на Yapx и получить ссылку");
        foreach (var button in new[] { save, copy, upload }) { button.Height = 32; button.Padding = new Padding(8, 2, 8, 2); button.Margin = new Padding(3); }
        actions.Controls.AddRange([save, copy, upload]); header.Controls.Add(actions); root.Controls.Add(header);
        workspace = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 1, Margin = Padding.Empty };
        workspace.ColumnStyles.Add(new(SizeType.Percent, 100));
        canvas = new(document) { Dock = DockStyle.Fill, Margin = Padding.Empty }; workspace.Controls.Add(canvas); root.Controls.Add(workspace);
        var toolBar = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.LeftToRight, WrapContents = false, BackColor = Theme.Panel, Padding = new Padding(8, 2, 8, 2), Margin = Padding.Empty, AutoScroll = true, AccessibleName = "Инструменты" };
        ToolDefinition[][] groups = [
            [new(EditorTool.Select, "↖", "Выбор / перенос", "V")],
            [new(EditorTool.Pen, "✎", "Карандаш", "P"), new(EditorTool.Line, "╱", "Линия", "L"), new(EditorTool.Arrow, "↗", "Стрелка", "A")],
            [new(EditorTool.Rectangle, "▭", "Прямоугольник", "R"), new(EditorTool.Ellipse, "○", "Эллипс", "O")],
            [new(EditorTool.Text, "T", "Текст", "T")],
            [new(EditorTool.Blur, "▦", "Размытие", "B"), new(EditorTool.Eraser, "◇", "Ластик", "E")],
            [new(EditorTool.Crop, "⌗", "Обрезка за края", "C")] ];
        foreach (var group in groups)
        {
            var button = new ToolGroupButton(group); button.ToolChosen += SelectTool;
            foreach (var definition in group) { tools[definition.Tool] = button; toolNames[definition.Tool] = definition.Name; }
            toolBar.Controls.Add(button);
            tips.SetToolTip(button, string.Join("\n", group.Select(d => $"{d.Name} · {d.Shortcut}")) + (group.Length > 1 ? "\nУдерживайте кнопку для выбора · ПКМ — открыть список" : ""));
        }
        toolBar.Controls.Add(new Panel { Width = 1, Height = 28, BackColor = Theme.Raised, Margin = new Padding(8, 4, 8, 4) });
        undo = Theme.Button("↶", (_, _) => Undo());
        redo = Theme.Button("↷", (_, _) => { canvas.CommitText(); canvas.CancelGesture(false); document.Redo(); });
        delete = Theme.Button("×", (_, _) => canvas.DeleteSelected());
        foreach (var button in new[] { undo, redo, delete }) { button.AutoSize = false; button.Size = new(36, 32); button.Padding = Padding.Empty; button.Margin = new Padding(3, 2, 3, 2); button.Font = new Font("Segoe UI", 14); }
        undo.AccessibleName = "Отменить"; redo.AccessibleName = "Повторить"; delete.AccessibleName = "Удалить пометку";
        tips.SetToolTip(undo, "Отменить · Ctrl+Z"); tips.SetToolTip(redo, "Повторить · Ctrl+Shift+Z / Ctrl+Y"); tips.SetToolTip(delete, "Удалить выбранную пометку · Delete");
        toolBar.Controls.AddRange([undo, redo, delete]);
        activeItem = new Label { Size = new(230, 32), TextAlign = ContentAlignment.MiddleLeft, AutoEllipsis = true, ForeColor = Theme.Muted, Margin = new Padding(8, 2, 0, 2) };
        tips.SetToolTip(activeItem, "Параметры изменяют выбранную пометку. Esc — снять выделение."); toolBar.Controls.Add(activeItem); root.Controls.Add(toolBar);
        parameterBar = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false, BackColor = Theme.Panel, Padding = new Padding(8, 3, 8, 3), Margin = Padding.Empty, AutoScroll = true, AccessibleName = "Параметры инструмента" };
        colorControls = new FlowLayoutPanel { Size = new(196, 38), FlowDirection = FlowDirection.TopDown, WrapContents = false, Margin = new Padding(4, 0, 4, 0), AccessibleName = "Цвет" };
        colorControls.Controls.Add(new Label { Text = "Цвет", Height = 14, Width = 190, ForeColor = Theme.Muted, Font = new Font("Segoe UI", 8.5f), Margin = Padding.Empty });
        var palette = new FlowLayoutPanel { Size = new(196, 22), WrapContents = false, Margin = Padding.Empty };
        Color[] colors = [Color.FromArgb(255, 88, 100), Color.FromArgb(255, 175, 78), Color.FromArgb(255, 218, 87), Theme.Accent, Color.FromArgb(90, 162, 255), Color.FromArgb(184, 137, 255), Color.White, Color.FromArgb(26, 30, 40)];
        foreach (var color in colors)
        {
            var swatch = new Button { Size = new(18, 20), BackColor = color, FlatStyle = FlatStyle.Flat, Margin = new Padding(1, 0, 1, 0), Cursor = Cursors.Hand, AccessibleName = $"Цвет {ColorTranslator.ToHtml(color)}" };
            swatch.FlatAppearance.BorderColor = Theme.Muted; swatch.Click += (_, _) => SetColor(color); palette.Controls.Add(swatch);
        }
        customColor = Theme.Button("+", (_, _) => { using var dialog = new ColorDialog { Color = canvas.Ink, FullOpen = true }; if (dialog.ShowDialog(this) == DialogResult.OK) SetColor(dialog.Color); });
        customColor.AutoSize = false; customColor.Size = new(28, 22); customColor.Font = new Font("Segoe UI", 8.5f); customColor.Padding = Padding.Empty; customColor.Margin = new Padding(2, 0, 0, 0); customColor.FlatAppearance.BorderSize = 2;
        customColor.FlatAppearance.BorderColor = canvas.Ink; customColor.AccessibleName = "Другой цвет"; tips.SetToolTip(customColor, "Другой цвет");
        palette.Controls.Add(customColor); colorControls.Controls.Add(palette); parameterBar.Controls.Add(colorControls);
        strokeControls = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = Padding.Empty };
        width = new ParameterSlider { Minimum = 1, Maximum = 32, Value = 4, AccessibleName = "Толщина линии" };
        strokeControls.Controls.Add(ParameterBox("Толщина: 4 px", width, 112, out widthLabel));
        transparency = new ParameterSlider { Minimum = 0, Maximum = 100, Value = 0, AccessibleName = "Прозрачность линии" };
        transparencyControl = ParameterBox("Прозрачность: 0 %", transparency, 140, out transparencyLabel); strokeControls.Controls.Add(transparencyControl);
        width.ValueChanged += (_, _) => { canvas.StrokeWidth = width.Value; UpdateSliderLabels(); ApplySliderStyle(width); };
        transparency.ValueChanged += (_, _) => { canvas.Opacity = 100 - transparency.Value; UpdateSliderLabels(); ApplySliderStyle(transparency); };
        foreach (var slider in new[] { width, transparency }) { slider.InteractionStarted += () => sliderUndoRecorded = false; slider.InteractionEnded += () => sliderUndoRecorded = false; }
        tips.SetToolTip(width, "Толщина линии; для размытия — сила эффекта"); tips.SetToolTip(transparency, "0 % — непрозрачная линия, 100 % — полностью прозрачная"); parameterBar.Controls.Add(strokeControls);
        arrowEnds = new FlowLayoutPanel { Height = 24, Width = 124, WrapContents = false, Margin = Padding.Empty, AccessibleName = "Наконечники стрелки" };
        arrowOne = new RadioButton { Text = "→ Один", Checked = true, AccessibleName = "Один наконечник" };
        arrowTwo = new RadioButton { Text = "↔ Два", AccessibleName = "Два наконечника" };
        foreach (var choice in new[] { arrowOne, arrowTwo }) { choice.Appearance = Appearance.Button; choice.FlatStyle = FlatStyle.Flat; choice.FlatAppearance.BorderSize = 0; choice.TextAlign = ContentAlignment.MiddleCenter; choice.Font = new Font("Segoe UI", 8.5f); choice.Size = new(58, 23); choice.Margin = new Padding(0, 0, 4, 0); }
        arrowOne.CheckedChanged += (_, _) => UpdateArrowChoice();
        arrowTwo.CheckedChanged += (_, _) => { canvas.DoubleHeaded = arrowTwo.Checked; UpdateArrowChoice(); if (!updatingStyle) canvas.UpdateSelectedStyle(); };
        arrowEnds.Controls.AddRange([arrowOne, arrowTwo]); UpdateArrowChoice();
        arrowControls = ParameterBox("Наконечники", arrowEnds, 124, out _); parameterBar.Controls.Add(arrowControls);
        fontSize = new NumericUpDown { Minimum = 10, Maximum = 160, Value = 28, BackColor = Theme.Raised, ForeColor = Theme.Foreground, AccessibleName = "Размер текста" };
        fontSize.ValueChanged += (_, _) => { canvas.TextSize = (float)fontSize.Value; if (!updatingStyle) canvas.UpdateSelectedStyle(); };
        fontControls = ParameterBox("Текст, px", fontSize, 104, out _); parameterBar.Controls.Add(fontControls);
        recognizeShapes = new CheckBox { Text = "Ровные фигуры", Checked = true, Width = 150, Height = 36, Margin = new Padding(8, 0, 4, 0), ForeColor = Theme.Foreground, AccessibleName = "Распознавать круги и стрелки" };
        recognizeShapes.CheckedChanged += (_, _) => canvas.RecognizeShapes = recognizeShapes.Checked;
        tips.SetToolTip(recognizeShapes, "Ручка: замкнутый круг и набросок стрелки превращаются в редактируемые фигуры после отпускания мыши.");
        parameterBar.Controls.Add(recognizeShapes);
        cropControls = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = Padding.Empty, AccessibleName = "Параметры обрезки" };
        cropControls.Controls.Add(new Label { Text = "Потяните край или угол снимка", Width = 220, Height = 32, TextAlign = ContentAlignment.MiddleLeft, ForeColor = Theme.Muted, Margin = new Padding(4, 2, 4, 2) });
        applyCrop = Theme.Button("Обрезать ↵", (_, _) => canvas.CommitCrop(), true); resetCrop = Theme.Button("Сбросить · Esc", (_, _) => canvas.CancelCrop());
        foreach (var button in new[] { applyCrop, resetCrop }) { button.AutoSize = false; button.Size = new(112, 32); button.Padding = Padding.Empty; button.Margin = new Padding(4, 2, 4, 2); }
        cropControls.Controls.AddRange([applyCrop, resetCrop]); parameterBar.Controls.Add(cropControls); root.Controls.Add(parameterBar);
        status = new Label { Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, AutoEllipsis = true, Padding = new Padding(10, 0, 4, 0), BackColor = Theme.Panel, ForeColor = Theme.Muted };
        root.Controls.Add(status); Controls.Add(root);
        canvas.HintChanged += text => status.Text = text; canvas.SelectionChanged += SyncStyle;
        canvas.ToolActivated += tool => { HighlightTool(tool); UpdatePropertyVisibility(); };
        canvas.CropStateChanged += () => { UpdatePropertyVisibility(); UpdateHistory(); };
        document.Changed += () => { exported = false; UpdateHistory(); UpdatePropertyVisibility(); };
        UpdateHistory(); SelectTool(EditorTool.Pen); FormClosing += ClosingEditor;
    }
    private static Panel ParameterBox(string title, Control input, int width, out Label label)
    {
        var panel = new Panel { Size = new(width, 38), Margin = new Padding(4, 0, 4, 0) };
        label = new Label { Text = title, Dock = DockStyle.Top, Height = 14, Font = new Font("Segoe UI", 8.5f), ForeColor = Theme.Muted };
        input.Dock = DockStyle.Bottom; input.Margin = Padding.Empty; panel.Controls.AddRange([input, label]); return panel;
    }
    private EditorTool ParameterTool => canvas.Selected?.Tool ?? canvas.Tool;
    private void SyncStyle()
    {
        if (canvas.Selected is { } selected)
        {
            updatingStyle = true;
            canvas.Ink = selected.Color; customColor.FlatAppearance.BorderColor = selected.Color;
            width.Value = Math.Clamp((int)Math.Round(selected.Width), width.Minimum, width.Maximum);
            if (Annotation.IsStrokeTool(selected.Tool)) { canvas.Opacity = selected.Opacity; transparency.Value = 100 - selected.Opacity; }
            fontSize.Value = Math.Clamp((decimal)selected.FontSize, fontSize.Minimum, fontSize.Maximum);
            if (selected.Tool == EditorTool.Arrow) { canvas.DoubleHeaded = selected.DoubleHeaded; arrowTwo.Checked = selected.DoubleHeaded; arrowOne.Checked = !selected.DoubleHeaded; }
            updatingStyle = false;
        }
        UpdatePropertyVisibility();
    }
    private void SetColor(Color color) { canvas.Ink = color; customColor.FlatAppearance.BorderColor = color; canvas.UpdateSelectedStyle(); }
    private void UpdateSliderLabels()
    {
        widthLabel.Text = ParameterTool == EditorTool.Blur ? $"Размытие: {width.Value}" : $"Толщина: {width.Value} px";
        transparencyLabel.Text = $"Прозрачность: {transparency.Value} %";
    }
    private void ApplySliderStyle(ParameterSlider slider)
    {
        if (updatingStyle) return;
        var hasSelection = canvas.Selected != null && !canvas.IsEditingText;
        canvas.UpdateSelectedStyle(!slider.IsInteracting || !sliderUndoRecorded);
        if (slider.IsInteracting && hasSelection) sliderUndoRecorded = true;
    }
    private void UpdateArrowChoice()
    {
        foreach (var choice in new[] { arrowOne, arrowTwo }) { choice.BackColor = choice.Checked ? Theme.Accent : Theme.Raised; choice.ForeColor = choice.Checked ? Theme.Background : Theme.Foreground; }
    }
    private void UpdateHistory() { undo.Enabled = document.CanUndo || canvas.HasCropChanges; redo.Enabled = document.CanRedo; }
    private void Undo()
    {
        if (canvas.HasCropChanges) { canvas.CancelCrop(); return; }
        canvas.CommitText(); canvas.CancelGesture(false); document.Undo();
    }
    private void UpdatePropertyVisibility()
    {
        var cropping = canvas.Tool == EditorTool.Crop; var context = ParameterTool;
        colorControls.Visible = !cropping && (Annotation.IsStrokeTool(context) || context == EditorTool.Text);
        cropControls.Visible = cropping; arrowControls.Visible = !cropping && context == EditorTool.Arrow;
        fontControls.Visible = !cropping && canvas.Tool == EditorTool.Text;
        var strokes = Annotation.IsStrokeTool(context);
        strokeControls.Visible = !cropping && (strokes || context == EditorTool.Blur); transparencyControl.Visible = strokes;
        recognizeShapes.Visible = !cropping && canvas.Tool == EditorTool.Pen;
        UpdateSliderLabels(); applyCrop.Enabled = resetCrop.Enabled = canvas.HasCropChanges;
        delete.Enabled = canvas.Selected != null && uploading == null;
        activeItem.Text = canvas.Selected is { } selected ? $"{toolNames[selected.Tool]} · выбрана" : $"Инструмент: {toolNames[canvas.Tool]}";
    }
    private void HighlightTool(EditorTool tool)
    {
        foreach (var button in tools.Values.Distinct()) { button.BackColor = Theme.Raised; button.ForeColor = Theme.Foreground; }
        tools[tool].Remember(tool); tools[tool].BackColor = Theme.Accent; tools[tool].ForeColor = Theme.Background;
    }
    private void SelectTool(EditorTool tool)
    {
        canvas.ActivateTool(tool); HighlightTool(tool); UpdatePropertyVisibility();
        status.Text = tool switch
        {
            EditorTool.Select => "Перетащите объект или маркер размера. Двойной щелчок по тексту — редактировать.",
            EditorTool.Text => "Щелчок — ввод или редактирование текста. Enter — готово, Shift+Enter — новая строка.",
            EditorTool.Eraser => "Нажмите или проведите по пометке, чтобы удалить её целиком.",
            EditorTool.Crop => "Потяните край или угол снимка. Enter — обрезать, Esc — сбросить.",
            EditorTool.Blur => "Выделите область для размытия. Маркеры меняют размер, ползунок снизу — силу эффекта.",
            EditorTool.Pen => "Рисуйте: замкнутый круг или стрелка выравниваются при отпускании мыши. Маркеры меняют размер.",
            _ => "Параметры снизу изменяют активную пометку. Esc — снять выделение."
        };
    }
    private bool SaveImage()
    {
        try
        {
            canvas.CommitEdits();
            Directory.CreateDirectory(settings.SaveDirectory);
            using var dialog = new SaveFileDialog { Filter = "Изображение PNG|*.png|Изображение JPEG|*.jpg", DefaultExt = "png", AddExtension = true, FileName = $"RapidShot_{DateTime.Now:yyyy-MM-dd_HH-mm-ss}", InitialDirectory = settings.SaveDirectory, OverwritePrompt = true };
            if (dialog.ShowDialog(this) != DialogResult.OK) return false;
            using var image = document.Render();
            var format = Path.GetExtension(dialog.FileName).Equals(".jpg", StringComparison.OrdinalIgnoreCase) || Path.GetExtension(dialog.FileName).Equals(".jpeg", StringComparison.OrdinalIgnoreCase) ? ImageFormat.Jpeg : ImageFormat.Png;
            // Encode before replacing the destination, so a failed save cannot truncate an existing image.
            using var bytes = new MemoryStream(); image.Save(bytes, format);
            var temporary = dialog.FileName + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try { File.WriteAllBytes(temporary, bytes.ToArray()); File.Move(temporary, dialog.FileName, true); }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
            exported = true; status.Text = $"Сохранено: {dialog.FileName}"; return true;
        }
        catch (Exception ex) { ShowError("Не удалось сохранить снимок", ex); return false; }
    }
    private void CopyImage()
    {
        try
        {
            canvas.CommitEdits();
            using var image = document.Render();
            ImageClipboard.Write(image);
            exported = true; status.Text = "Изображение скопировано в буфер обмена.";
        }
        catch (Exception ex) { ShowError("Не удалось скопировать снимок", ex); }
    }
    private async Task Upload()
    {
        if (uploading != null) { uploading.Cancel(); return; }
        canvas.CommitEdits();
        using var cancellation = new CancellationTokenSource(); uploading = cancellation;
        workspace.Enabled = false;
        canvas.Enabled = false; save.Enabled = copy.Enabled = false;
        foreach (var button in tools.Values.Distinct()) button.Enabled = false;
        parameterBar.Enabled = false; delete.Enabled = false; undo.Enabled = redo.Enabled = false;
        upload.Text = "Отменить"; status.Text = "Загрузка изображения на Yapx…";
        try
        {
            using var image = document.Render();
            if (image.Width > 10000 || image.Height > 10000) throw new InvalidOperationException("Yapx: снимок больше 10000 × 10000 px. Обрежьте снимок.");
            using var stream = new MemoryStream(); image.Save(stream, ImageFormat.Png);
            using var client = new YapxClient();
            var result = await client.UploadAsync(stream.ToArray(), cancellation.Token);
            if (IsDisposed) return;
            exported = true;
            try { Clipboard.SetText(result.Url); status.Text = "Загружено. Ссылка скопирована в буфер обмена."; }
            catch { status.Text = "Загружено. Скопируйте ссылку из открывшегося окна."; }
            using var dialog = new UploadResultDialog(result); dialog.ShowDialog(this);
        }
        catch (OperationCanceledException) { if (!IsDisposed) status.Text = cancellation.IsCancellationRequested ? "Загрузка отменена." : "Сервер не ответил вовремя. Повторите попытку."; }
        catch (Exception ex) { if (!IsDisposed) ShowError("Не удалось загрузить снимок", ex); }
        finally
        {
            uploading = null;
            if (!IsDisposed)
            {
                canvas.Enabled = true; save.Enabled = copy.Enabled = true;
                upload.Text = "Загрузить на Yapx ↗";
                workspace.Enabled = true;
                foreach (var button in tools.Values.Distinct()) button.Enabled = true;
                parameterBar.Enabled = true; UpdatePropertyVisibility(); UpdateHistory();
            }
        }
    }
    private void ShowError(string title, Exception error)
    {
        status.Text = title + ". Снимок остаётся в редакторе.";
        MessageBox.Show(this, error.Message, title, MessageBoxButtons.OK, MessageBoxIcon.Warning);
    }
    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        if (uploading != null) return base.ProcessCmdKey(ref msg, keyData);
        if (canvas.IsEditingText && canvas.ContainsFocus && keyData != (Keys.Control | Keys.S)) return base.ProcessCmdKey(ref msg, keyData);
        if (keyData == (Keys.Control | Keys.S)) { SaveImage(); return true; }
        if (keyData == (Keys.Control | Keys.C)) { CopyImage(); return true; }
        if (keyData == Keys.Enter && canvas.Tool == EditorTool.Crop) { canvas.CommitCrop(); return true; }
        if (keyData == (Keys.Control | Keys.Z)) { Undo(); return true; }
        if (keyData == (Keys.Control | Keys.Shift | Keys.Z) || keyData == (Keys.Control | Keys.Y)) { canvas.CommitText(); canvas.CancelGesture(false); document.Redo(); return true; }
        if (keyData == Keys.Escape) { canvas.CancelGesture(); return true; }
        if (ActiveControl is NumericUpDown || ActiveControl is TextBoxBase) return base.ProcessCmdKey(ref msg, keyData);
        if (keyData == Keys.Delete) { canvas.DeleteSelected(); return true; }
        var tool = keyData switch { Keys.V => EditorTool.Select, Keys.P => EditorTool.Pen, Keys.R => EditorTool.Rectangle, Keys.O => EditorTool.Ellipse, Keys.A => EditorTool.Arrow, Keys.L => EditorTool.Line, Keys.T => EditorTool.Text, Keys.B => EditorTool.Blur, Keys.E => EditorTool.Eraser, Keys.C => EditorTool.Crop, _ => (EditorTool?)null };
        if (tool.HasValue) { SelectTool(tool.Value); return true; }
        return base.ProcessCmdKey(ref msg, keyData);
    }
    private void ClosingEditor(object? sender, FormClosingEventArgs e)
    {
        if (uploading != null) { e.Cancel = true; uploading.Cancel(); return; }
        canvas.CommitEdits();
        if (exported) return;
        var answer = MessageBox.Show(this, "Сохранить снимок перед закрытием редактора?", "RapidShot", MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question);
        if (answer == DialogResult.Cancel || (answer == DialogResult.Yes && !SaveImage())) e.Cancel = true;
    }
    protected override void Dispose(bool disposing)
    {
        if (disposing) { tips.Dispose(); canvas.Dispose(); document.Dispose(); }
        base.Dispose(disposing);
    }
}
