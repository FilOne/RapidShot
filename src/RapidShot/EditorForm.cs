using System.Drawing.Imaging;
using System.Globalization;
using System.Numerics;

namespace RapidShot;

internal sealed partial class EditorForm : Form, IThemeAware
{
    private readonly ScreenshotDocument document;
    private readonly EditorCanvas canvas;
    private readonly AppSettings settings;
    private readonly UiText Strings;
    private readonly Label status, activeItem;
    private readonly Button undo, redo, copy, delete, customColor;
    private readonly IconButton recognizeText;
    private readonly ToolStripMenuItem save, upload;
    private readonly CaptureSplitButton capture;
    private readonly FloatingBar toolBar;
    private readonly ContextMenuStrip captureMenu = new(), colorMenu = new();
    private readonly Dictionary<EditorTool, ToolGroupButton> tools = [];
    private readonly Dictionary<EditorTool, string> toolNames = [];
    private readonly NumericUpDown fontSize, pinSize;
    private readonly PinNumberBox pinNumber;
    private Guid? pinNumberId;
    private readonly ParameterSlider width, transparency, magnifierZoom;
    private readonly Label widthLabel, transparencyLabel, magnifierZoomLabel;
    private readonly FlowLayoutPanel parameterBar, strokeControls, colorControls, arrowEnds, cropControls, pinControls, curveControls, magnifierControls;
    private readonly Label nextPin;
    private readonly Panel transparencyControl, fontControls, arrowControls;
    private readonly RadioButton arrowOne, arrowTwo;
    private readonly CheckBox recognizeShapes;
    private readonly Button applyCrop, resetCrop;
    private readonly Button addCurvePoint, removeCurvePoint, straightenArrow, magnifierSource;
    private readonly TableLayoutPanel workspace;
    private readonly ToolTip tips = new();
    private bool sliderUndoRecorded, exported, updatingStyle, hasToolParameters;
    private CancellationTokenSource? uploading;

    public EditorForm(Bitmap image, AppSettings settings, Rectangle? regionBounds = null)
    {
        this.settings = settings; document = new(image);
        Strings = UiText.For(settings.Language);
        Theme.Apply(this); Font = new Font("Segoe UI", 9.5f);
        Text = "RapidShot"; ClientSize = new(920, 580); MinimumSize = new(740, 480);
        KeyPreview = true;
        var root = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 4, ColumnCount = 1, Margin = Padding.Empty, Padding = Padding.Empty };
        root.RowStyles.Add(new(SizeType.Percent, 100)); root.RowStyles.Add(new(SizeType.Absolute, 52)); root.RowStyles.Add(new(SizeType.Absolute, 56));
        root.RowStyles.Add(new(SizeType.Absolute, 20));
        root.ColumnStyles.Add(new(SizeType.Percent, 100));
        captureMenu.Renderer = new MinimalMenuRenderer(); captureMenu.ImageScalingSize = new Size(20, 20);
        save = new ToolStripMenuItem(Strings["SaveAs"]) { ShortcutKeyDisplayString = "Ctrl+S", ImageScaling = ToolStripItemImageScaling.None }; save.Click += (_, _) => SaveAndFinish();
        upload = new ToolStripMenuItem(Strings["UploadToYapx"]) { AccessibleName = Strings["UploadToYapx"], ImageScaling = ToolStripItemImageScaling.None }; upload.Click += async (_, _) => await Upload();
        SetMenuIcon(save, UiIcon.Save); SetMenuIcon(upload, UiIcon.Upload);
        captureMenu.Items.AddRange([save, upload]);
        workspace = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 1, Margin = Padding.Empty };
        workspace.ColumnStyles.Add(new(SizeType.Percent, 100));
        canvas = new(document) { Strings = Strings, Dock = DockStyle.Fill, Margin = Padding.Empty }; workspace.Controls.Add(canvas); root.Controls.Add(workspace);
        toolBar = new FloatingBar { AccessibleName = Strings["Tools"] };
        capture = new CaptureSplitButton(Strings); copy = capture.PrimaryAction;
        capture.CopyRequested += CopyImage; capture.MenuRequested += ShowCaptureMenu;
        tips.SetToolTip(copy, Strings["CopyImageHint"]);
        tips.SetToolTip(capture.MenuAction, Strings["CaptureMenuHint"]);
        recognizeText = new IconButton(UiIcon.Ocr, Strings["RecognizeText"]) { Text = "OCR", ShowText = true, Width = 74 };
        recognizeText.Click += async (_, _) => await RecognizeTextAsync();
        tips.SetToolTip(recognizeText, Strings["RecognizeTextHint"]);
        toolBar.Controls.AddRange([capture, recognizeText, Separator()]);
        ToolDefinition[][] groups = [
            [new(EditorTool.Select, "↖", Strings["SelectTool"], "V")],
            [new(EditorTool.Pen, "✎", Strings["PenTool"], "P"), new(EditorTool.Line, "╱", Strings["LineTool"], "L"), new(EditorTool.Arrow, "↗", Strings["ArrowTool"], "A")],
            [new(EditorTool.Rectangle, "▭", Strings["RectangleTool"], "R"), new(EditorTool.Ellipse, "○", Strings["EllipseTool"], "O")],
            [new(EditorTool.Text, "T", Strings["TextTool"], "T")],
            [new(EditorTool.Pin, "1", Strings["PinTool"], "N")],
            [new(EditorTool.Magnifier, "+", Strings["MagnifierTool"], "M")],
            [new(EditorTool.Blur, "▦", Strings["BlurTool"], "B"), new(EditorTool.Eraser, "◇", Strings["EraserTool"], "E")],
            [new(EditorTool.Crop, "⌗", Strings["CropTool"], "C")] ];
        foreach (var group in groups)
        {
            if (regionBounds.HasValue && group.Any(definition => definition.Tool == EditorTool.Crop)) continue;
            var button = new ToolGroupButton(group); button.ToolChosen += SelectTool;
            foreach (var definition in group) { tools[definition.Tool] = button; toolNames[definition.Tool] = definition.Name; }
            toolBar.Controls.Add(button);
            tips.SetToolTip(button, string.Join("\n", group.Select(d => $"{d.Name} · {d.Shortcut}")) + (group.Length > 1 ? Strings["ToolGroupHint"] : ""));
        }
        toolBar.Controls.Add(Separator());
        undo = new IconButton(UiIcon.Undo, Strings["Undo"]); undo.Click += (_, _) => Undo();
        redo = new IconButton(UiIcon.Redo, Strings["Redo"]); redo.Click += (_, _) => { canvas.CommitText(); canvas.CancelGesture(false); document.Redo(); };
        delete = new IconButton(UiIcon.Delete, Strings["DeleteAnnotation"]); delete.Click += (_, _) => canvas.DeleteSelected();
        undo.AccessibleName = Strings["Undo"]; redo.AccessibleName = Strings["Redo"]; delete.AccessibleName = Strings["DeleteAnnotation"];
        tips.SetToolTip(undo, Strings["UndoHint"]); tips.SetToolTip(redo, Strings["RedoHint"]); tips.SetToolTip(delete, Strings["DeleteAnnotationHint"]);
        toolBar.Controls.AddRange([undo, redo, delete]);
        activeItem = new Label { Visible = false };
        tips.SetToolTip(activeItem, Strings["SelectionOptionsHint"]); toolBar.Controls.Add(activeItem); root.Controls.Add(toolBar);
        parameterBar = new FloatingBar { AccessibleName = Strings["ToolOptions"] };
        colorControls = new FlowLayoutPanel { Size = new(98, 38), WrapContents = false, Margin = new Padding(0, 0, 4, 0), AccessibleName = Strings["Color"] };
        Color[] colors = [Color.FromArgb(255, 88, 100), Color.FromArgb(255, 175, 78), Color.FromArgb(255, 218, 87), Theme.Accent, Color.FromArgb(90, 162, 255), Color.FromArgb(184, 137, 255), Color.White, Color.FromArgb(26, 30, 40)];
        foreach (var color in colors)
        {
            var swatch = new ToolStripMenuItem(ColorTranslator.ToHtml(color));
            var swatchImage = new Bitmap(16, 16); using (var graphics = Graphics.FromImage(swatchImage)) { graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias; using var ink = new SolidBrush(color); graphics.FillEllipse(ink, 1, 1, 14, 14); }
            swatch.Image = swatchImage; swatch.Click += (_, _) => SetColor(color); colorMenu.Items.Add(swatch);
        }
        colorMenu.Items.Add(new ToolStripSeparator()); colorMenu.Items.Add(Strings["CustomColorMenu"], null, (_, _) => { using var dialog = new ColorDialog { Color = canvas.Ink, FullOpen = true }; if (dialog.ShowDialog(this) == DialogResult.OK) SetColor(dialog.Color); });
        colorMenu.BackColor = captureMenu.BackColor = Theme.Panel; colorMenu.ForeColor = captureMenu.ForeColor = Theme.Foreground;
        Theme.Menu(captureMenu); Theme.Menu(colorMenu);
        customColor = new IconButton(UiIcon.Color, Strings["ColorPicker"]) { ShowText = true, Width = 94, Height = 36 };
        customColor.Click += (_, _) => colorMenu.Show(customColor, Point.Empty, ToolStripDropDownDirection.AboveRight);
        customColor.FlatAppearance.BorderColor = canvas.Ink; customColor.AccessibleName = Strings["CustomColor"]; tips.SetToolTip(customColor, Strings["CustomColor"]);
        colorControls.Controls.Add(customColor); parameterBar.Controls.Add(colorControls);
        strokeControls = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = Padding.Empty };
        width = new ParameterSlider { Minimum = 1, Maximum = 32, Value = 4, AccessibleName = Strings["StrokeWidth"] };
        strokeControls.Controls.Add(ParameterBox(Strings["DefaultStrokeWidth"], width, 112, out widthLabel));
        transparency = new ParameterSlider { Minimum = 0, Maximum = 100, Value = 0, AccessibleName = Strings["StrokeTransparency"] };
        transparencyControl = ParameterBox(Strings["DefaultTransparency"], transparency, 140, out transparencyLabel); strokeControls.Controls.Add(transparencyControl);
        width.ValueChanged += (_, _) => { canvas.StrokeWidth = width.Value; UpdateSliderLabels(); ApplySliderStyle(width); };
        transparency.ValueChanged += (_, _) => { canvas.Opacity = 100 - transparency.Value; UpdateSliderLabels(); ApplySliderStyle(transparency); };
        foreach (var slider in new[] { width, transparency }) { slider.InteractionStarted += () => sliderUndoRecorded = false; slider.InteractionEnded += () => sliderUndoRecorded = false; }
        tips.SetToolTip(width, Strings["StrokeWidthHint"]); tips.SetToolTip(transparency, Strings["TransparencyHint"]); parameterBar.Controls.Add(strokeControls);
        arrowEnds = new FlowLayoutPanel { Height = 24, Width = 124, WrapContents = false, Margin = Padding.Empty, AccessibleName = Strings["Arrowheads"] };
        arrowOne = new RadioButton { Text = Strings["SingleArrowButton"], Checked = true, AccessibleName = Strings["SingleArrowhead"] };
        arrowTwo = new RadioButton { Text = Strings["DoubleArrowButton"], AccessibleName = Strings["DoubleArrowhead"] };
        foreach (var choice in new[] { arrowOne, arrowTwo }) { choice.Appearance = Appearance.Button; choice.FlatStyle = FlatStyle.Flat; choice.FlatAppearance.BorderSize = 0; choice.TextAlign = ContentAlignment.MiddleCenter; choice.Font = new Font("Segoe UI", 8.5f); choice.Size = new(58, 23); choice.Margin = new Padding(0, 0, 4, 0); }
        arrowOne.CheckedChanged += (_, _) => UpdateArrowChoice();
        arrowTwo.CheckedChanged += (_, _) => { canvas.DoubleHeaded = arrowTwo.Checked; UpdateArrowChoice(); if (!updatingStyle) canvas.UpdateSelectedStyle(); };
        arrowEnds.Controls.AddRange([arrowOne, arrowTwo]); UpdateArrowChoice();
        arrowControls = ParameterBox(Strings["ArrowheadOptions"], arrowEnds, 124, out _); parameterBar.Controls.Add(arrowControls);
        curveControls = new FlowLayoutPanel { Size = new(158, 38), WrapContents = false, Margin = Padding.Empty, Padding = new Padding(0, 7, 0, 0), AccessibleName = Strings["ArrowCurve"] };
        addCurvePoint = Theme.Button("+", (_, _) => canvas.AddCurvePoint());
        removeCurvePoint = Theme.Button("−", (_, _) => canvas.RemoveCurvePoint());
        straightenArrow = Theme.Button(Strings["StraightArrow"], (_, _) => canvas.StraightenArrow());
        foreach (var button in new[] { addCurvePoint, removeCurvePoint, straightenArrow }) { button.AutoSize = false; button.Size = new(button == straightenArrow ? 66 : 40, 26); button.Padding = Padding.Empty; button.Margin = new Padding(2, 0, 2, 0); }
        addCurvePoint.AccessibleName = Strings["AddCurvePoint"]; removeCurvePoint.AccessibleName = Strings["DeleteCurvePoint"]; straightenArrow.AccessibleName = Strings["StraightenArrow"];
        tips.SetToolTip(addCurvePoint, Strings["AddCurvePointHint"]);
        tips.SetToolTip(removeCurvePoint, Strings["DeleteCurvePoint"]); tips.SetToolTip(straightenArrow, Strings["StraightenArrowHint"]);
        curveControls.Controls.AddRange([addCurvePoint, removeCurvePoint, straightenArrow]); parameterBar.Controls.Add(curveControls);
        magnifierControls = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = Padding.Empty, AccessibleName = Strings["MagnifierOptions"] };
        magnifierZoom = new ParameterSlider { Minimum = 100, Maximum = 800, Value = 200, AccessibleName = Strings["MagnifierZoom"] };
        magnifierControls.Controls.Add(ParameterBox(Strings["DefaultZoom"], magnifierZoom, 136, out magnifierZoomLabel));
        magnifierZoom.ValueChanged += (_, _) =>
        {
            magnifierZoomLabel.Text = Strings.Format("ZoomLevel", magnifierZoom.Value / 100f);
            canvas.MagnifierZoom = magnifierZoom.Value / 100f;
            if (updatingStyle) return;
            canvas.UpdateMagnifierZoom(!magnifierZoom.IsInteracting || !sliderUndoRecorded);
            if (magnifierZoom.IsInteracting && canvas.Selected?.Tool == EditorTool.Magnifier) sliderUndoRecorded = true;
        };
        magnifierZoom.InteractionStarted += () => sliderUndoRecorded = false; magnifierZoom.InteractionEnded += () => sliderUndoRecorded = false;
        tips.SetToolTip(magnifierZoom, Strings["MagnifierZoomHint"]);
        magnifierSource = Theme.Button(Strings["Source"], (_, _) => canvas.ToggleMagnifierSource());
        magnifierSource.AutoSize = false; magnifierSource.Size = new(96, 30); magnifierSource.Padding = Padding.Empty; magnifierSource.Margin = new Padding(4, 4, 4, 0); magnifierSource.AccessibleName = Strings["EditMagnifierSource"];
        tips.SetToolTip(magnifierSource, Strings["EditMagnifierSourceHint"]);
        magnifierControls.Controls.Add(magnifierSource); parameterBar.Controls.Add(magnifierControls);
        fontSize = new NumericUpDown { Minimum = 10, Maximum = 160, Value = 28, BackColor = Theme.Raised, ForeColor = Theme.Foreground, AccessibleName = Strings["TextSize"] };
        fontSize.ValueChanged += (_, _) => { canvas.TextSize = (float)fontSize.Value; if (!updatingStyle) canvas.UpdateSelectedStyle(); };
        fontControls = ParameterBox(Strings["TextSizePixels"], fontSize, 104, out _); parameterBar.Controls.Add(fontControls);
        pinControls = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = Padding.Empty, AccessibleName = Strings["PinOptions"] };
        pinSize = new NumericUpDown { Minimum = 16, Maximum = 160, Value = 48, BackColor = Theme.Raised, ForeColor = Theme.Foreground, AccessibleName = Strings["PinSize"] };
        pinSize.ValueChanged += (_, _) => { canvas.PinSize = (float)pinSize.Value; if (!updatingStyle) canvas.UpdateSelectedStyle(); };
        pinControls.Controls.Add(ParameterBox(Strings["PinSizePixels"], pinSize, 112, out _));
        pinNumber = new PinNumberBox { BackColor = Theme.Raised, ForeColor = Theme.Foreground, AccessibleName = Strings["PinNumber"], Text = "", MaxLength = 0 };
        pinNumber.AcceptEdit += () => { CommitPinNumber(); canvas.Focus(); };
        pinNumber.CancelEdit += () => { SyncPinNumber(true); canvas.Focus(); };
        pinNumber.Leave += (_, _) => CommitPinNumber();
        tips.SetToolTip(pinNumber, Strings["PinNumberHint"]);
        pinControls.Controls.Add(ParameterBox(Strings["PinNumber"], pinNumber, 120, out _));
        nextPin = new Label { Size = new(176, 36), TextAlign = ContentAlignment.MiddleLeft, AutoEllipsis = true, ForeColor = Theme.Muted, Margin = new Padding(8, 0, 4, 0), AccessibleName = Strings["NextPinNumber"] };
        pinControls.Controls.Add(nextPin); parameterBar.Controls.Add(pinControls);
        recognizeShapes = new CheckBox { Text = Strings["RecognizeShapes"], Checked = true, Width = 150, Height = 36, Margin = new Padding(8, 0, 4, 0), ForeColor = Theme.Foreground, AccessibleName = Strings["RecognizeShapesName"] };
        recognizeShapes.CheckedChanged += (_, _) => canvas.RecognizeShapes = recognizeShapes.Checked;
        tips.SetToolTip(recognizeShapes, Strings["RecognizeShapesHint"]);
        parameterBar.Controls.Add(recognizeShapes);
        cropControls = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = Padding.Empty, AccessibleName = Strings["CropOptions"] };
        cropControls.Controls.Add(new Label { Text = Strings["CropDragEdge"], Width = 220, Height = 32, TextAlign = ContentAlignment.MiddleLeft, ForeColor = Theme.Muted, Margin = new Padding(4, 2, 4, 2) });
        applyCrop = Theme.Button(Strings["ApplyCrop"], (_, _) => canvas.CommitCrop(), true); resetCrop = Theme.Button(Strings["ResetCrop"], (_, _) => canvas.CancelCrop());
        foreach (var button in new[] { applyCrop, resetCrop }) { button.AutoSize = false; button.Size = new(112, 32); button.Padding = Padding.Empty; button.Margin = new Padding(4, 2, 4, 2); }
        cropControls.Controls.AddRange([applyCrop, resetCrop]); parameterBar.Controls.Add(cropControls); root.Controls.Add(parameterBar, 0, 2);
        status = new Label { Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleCenter, AutoEllipsis = true, Font = new Font("Segoe UI", 8), Padding = new Padding(10, 0, 4, 0), BackColor = Theme.Background, ForeColor = Theme.Muted };
        root.Controls.Add(status, 0, 3); Controls.Add(root);
        canvas.HintChanged += text => status.Text = text; canvas.SelectionChanged += SyncStyle;
        canvas.ToolActivated += tool => { HighlightTool(tool); UpdatePropertyVisibility(); };
        canvas.CropStateChanged += () => { UpdatePropertyVisibility(); UpdateHistory(); };
        document.Changed += () => { exported = false; HideOcrResult(); UpdateHistory(); UpdatePropertyVisibility(); };
        UpdateHistory(); SelectTool(EditorTool.Pen); FormClosing += ClosingEditor;
        void FitBars() { toolBar.MaximumSize = new Size(Math.Max(300, ClientSize.Width - 24), 46); parameterBar.MaximumSize = new Size(Math.Max(300, ClientSize.Width - 24), 50); }
        SizeChanged += (_, _) => { if (!isRegionCapture) FitBars(); }; FitBars();
        if (regionBounds.HasValue) InitializeRegionCapture(regionBounds.Value, root);
    }
    private static Control Separator() => new Panel { Width = 1, Height = 24, BackColor = Theme.Separator, Margin = new Padding(6, 5, 6, 5) };
    void IThemeAware.RefreshTheme()
    {
        SetMenuIcon(save, UiIcon.Save); SetMenuIcon(upload, uploading == null ? UiIcon.Upload : UiIcon.Cancel);
        HighlightTool(canvas.Tool); UpdateArrowChoice(); canvas.RefreshTheme();
    }
    private void ShowCaptureMenu()
    {
        captureMenu.Show(capture.MenuAction, Point.Empty, ToolStripDropDownDirection.AboveRight);
    }
    private static void SetMenuIcon(ToolStripMenuItem item, UiIcon icon)
    {
        var previous = item.Image; item.Image = UiGlyph.CreateImage(icon, Theme.Foreground); previous?.Dispose();
    }
    private static Panel ParameterBox(string title, Control input, int width, out Label label)
    {
        var panel = new Panel { Size = new(width, 38), Margin = new Padding(4, 0, 4, 0) };
        label = new Label { Text = title, Dock = DockStyle.Top, Height = 14, Font = new Font("Segoe UI", 8.5f), ForeColor = Theme.Muted };
        input.Dock = DockStyle.Bottom; input.Margin = Padding.Empty; panel.Controls.AddRange([input, label]); return panel;
    }
    private EditorTool ParameterTool => canvas.Selected?.Tool ?? canvas.Tool;
    private void SyncPinNumber(bool force = false)
    {
        var selected = canvas.Selected is { Tool: EditorTool.Pin } pin ? pin : null;
        pinNumber.Enabled = selected != null;
        if (force || !pinNumber.Focused || pinNumberId != selected?.Id)
            pinNumber.Text = selected?.PinNumber.ToString(CultureInfo.InvariantCulture) ?? "";
        pinNumberId = selected?.Id;
    }
    private void CommitPinNumber()
    {
        if (pinNumberId == null || document.Items.FirstOrDefault(x => x.Id == pinNumberId && x.Tool == EditorTool.Pin) is not { } pin) return;
        if (!BigInteger.TryParse(pinNumber.Text.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out var number) || number < BigInteger.One)
        {
            SyncPinNumber(true); status.Text = Strings["InvalidPinNumber"]; return;
        }
        document.SetPinNumber(pin, number); SyncPinNumber(true);
    }
    private sealed class PinNumberBox : TextBox
    {
        public event Action? AcceptEdit;
        public event Action? CancelEdit;
        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (keyData == Keys.Enter) { AcceptEdit?.Invoke(); return true; }
            if (keyData == Keys.Escape) { CancelEdit?.Invoke(); return true; }
            return base.ProcessCmdKey(ref msg, keyData);
        }
    }
    private void SyncStyle()
    {
        if (canvas.Selected is { } selected)
        {
            updatingStyle = true;
            canvas.Ink = selected.Color; customColor.FlatAppearance.BorderColor = selected.Color;
            width.Value = Math.Clamp((int)Math.Round(selected.Width), width.Minimum, width.Maximum);
            if (Annotation.IsStrokeTool(selected.Tool)) { canvas.Opacity = selected.Opacity; transparency.Value = 100 - selected.Opacity; }
            fontSize.Value = Math.Clamp((decimal)selected.FontSize, fontSize.Minimum, fontSize.Maximum);
            if (selected.Tool == EditorTool.Pin) { canvas.PinSize = selected.PinSize; pinSize.Value = Math.Clamp((decimal)selected.PinSize, pinSize.Minimum, pinSize.Maximum); }
            if (selected.Tool == EditorTool.Arrow) { canvas.DoubleHeaded = selected.DoubleHeaded; arrowTwo.Checked = selected.DoubleHeaded; arrowOne.Checked = !selected.DoubleHeaded; }
            if (selected.Tool == EditorTool.Magnifier) { canvas.MagnifierZoom = selected.Magnification; magnifierZoom.Value = Math.Clamp((int)Math.Round(selected.Magnification * 100), magnifierZoom.Minimum, magnifierZoom.Maximum); }
            updatingStyle = false;
        }
        UpdatePropertyVisibility();
    }
    private void SetColor(Color color) { canvas.Ink = color; customColor.FlatAppearance.BorderColor = color; canvas.UpdateSelectedStyle(); }
    private void UpdateSliderLabels()
    {
        widthLabel.Text = ParameterTool == EditorTool.Blur ? Strings.Format("BlurStrength", width.Value) : ParameterTool == EditorTool.Magnifier ? Strings.Format("OutlineWidth", width.Value) : Strings.Format("WidthPixels", width.Value);
        transparencyLabel.Text = Strings.Format("TransparencyPercent", transparency.Value);
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
        foreach (var choice in new[] { arrowOne, arrowTwo }) { choice.BackColor = choice.Checked ? Theme.Accent : Theme.Raised; choice.ForeColor = choice.Checked ? Theme.OnAccent : Theme.Foreground; }
    }
    private void UpdateHistory() { undo.Enabled = document.CanUndo || canvas.HasCropChanges || canvas.IsResizingCapture || canvas.HasMagnifierDraft; redo.Enabled = document.CanRedo; }
    private void Undo()
    {
        if (canvas.CancelCaptureResize()) return;
        if (canvas.HasMagnifierDraft) { canvas.CancelGesture(false); return; }
        if (canvas.HasCropChanges) { canvas.CancelCrop(); return; }
        canvas.CommitText(); canvas.CancelGesture(false); document.Undo();
    }
    private void UpdatePropertyVisibility()
    {
        var cropping = canvas.Tool == EditorTool.Crop; var context = ParameterTool;
        colorControls.Visible = !cropping && (Annotation.IsStrokeTool(context) || context is EditorTool.Text or EditorTool.Pin or EditorTool.Magnifier);
        cropControls.Visible = cropping; arrowControls.Visible = !cropping && context == EditorTool.Arrow;
        curveControls.Visible = !cropping && context == EditorTool.Arrow;
        addCurvePoint.Enabled = canvas.Selected?.Tool == EditorTool.Arrow; removeCurvePoint.Enabled = canvas.CanRemoveCurvePoint;
        straightenArrow.Enabled = canvas.Selected is { Tool: EditorTool.Arrow, CurvePoints.Count: > 0 };
        magnifierControls.Visible = !cropping && context == EditorTool.Magnifier;
        magnifierSource.Enabled = canvas.Selected?.Tool == EditorTool.Magnifier;
        magnifierSource.Text = canvas.EditingMagnifierSource ? Strings["Done"] : Strings["Source"];
        fontControls.Visible = !cropping && canvas.Tool == EditorTool.Text;
        var strokes = Annotation.IsStrokeTool(context);
        strokeControls.Visible = !cropping && (strokes || context is EditorTool.Blur or EditorTool.Magnifier); transparencyControl.Visible = strokes;
        recognizeShapes.Visible = !cropping && canvas.Tool == EditorTool.Pen;
        pinControls.Visible = !cropping && context == EditorTool.Pin;
        hasToolParameters = !canvas.HasOcrResult && (cropping || strokes || context is EditorTool.Blur or EditorTool.Text or EditorTool.Pin or EditorTool.Magnifier || canvas.Tool == EditorTool.Text || canvas.Tool == EditorTool.Pen);
        parameterBar.Visible = hasToolParameters && (!isRegionCapture || regionEditing);
        // Keep the normal editor's viewport stable when selection parameters
        // appear during a drag. The desktop overlay has fixed image coordinates.
        if (isRegionCapture) PositionRegionBars();
        SyncPinNumber();
        nextPin.Text = Strings.Format("NextPin", document.NextPinNumber);
        tips.SetToolTip(nextPin, nextPin.Text);
        UpdateSliderLabels(); applyCrop.Enabled = resetCrop.Enabled = canvas.HasCropChanges;
        delete.Enabled = canvas.Selected != null && uploading == null;
        activeItem.Text = canvas.Selected is { Tool: EditorTool.Pin } pin ? Strings.Format("SelectedPin", pin.PinNumber) : canvas.Selected is { } selected ? Strings.Format("SelectedTool", toolNames[selected.Tool]) : Strings.Format("ActiveTool", toolNames[canvas.Tool]);
    }
    private void HighlightTool(EditorTool tool)
    {
        foreach (var button in tools.Values.Distinct()) { button.Selected = false; button.BackColor = Theme.Panel; button.ForeColor = Theme.Foreground; }
        tools[tool].Remember(tool); tools[tool].Selected = !canvas.HasOcrResult;
    }
    private void SelectTool(EditorTool tool)
    {
        if (!tools.ContainsKey(tool)) return;
        HideOcrResult();
        canvas.ActivateTool(tool); HighlightTool(tool); UpdatePropertyVisibility();
        status.Text = tool switch
        {
            EditorTool.Select => Strings["SelectToolHint"],
            EditorTool.Text => Strings["TextToolHint"],
            EditorTool.Eraser => Strings["EraserToolHint"],
            EditorTool.Crop => Strings["CropHint"],
            EditorTool.Blur => Strings["BlurToolHint"],
            EditorTool.Pen => Strings["PenToolHint"],
            EditorTool.Pin => Strings["PinToolHint"],
            EditorTool.Rectangle => Strings["RectangleToolHint"],
            EditorTool.Ellipse => Strings["EllipseToolHint"],
            EditorTool.Arrow => Strings["ArrowToolHint"],
            EditorTool.Magnifier => Strings["MagnifierToolHint"],
            _ => Strings["ToolOptionsHint"]
        };
    }
    private bool SaveImage()
    {
        try
        {
            CommitPinNumber(); canvas.CommitEdits();
            Directory.CreateDirectory(settings.SaveDirectory);
            using var dialog = new SaveFileDialog { Filter = Strings["SaveImageFilter"], DefaultExt = "png", AddExtension = true, FileName = $"RapidShot_{DateTime.Now:yyyy-MM-dd_HH-mm-ss}", InitialDirectory = settings.SaveDirectory, OverwritePrompt = true };
            if (dialog.ShowDialog(this) != DialogResult.OK) return false;
            using var image = document.Render();
            var format = Path.GetExtension(dialog.FileName).Equals(".jpg", StringComparison.OrdinalIgnoreCase) || Path.GetExtension(dialog.FileName).Equals(".jpeg", StringComparison.OrdinalIgnoreCase) ? ImageFormat.Jpeg : ImageFormat.Png;
            // Encode before replacing the destination, so a failed save cannot truncate an existing image.
            using var bytes = new MemoryStream(); image.Save(bytes, format);
            var temporary = dialog.FileName + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try { File.WriteAllBytes(temporary, bytes.ToArray()); File.Move(temporary, dialog.FileName, true); }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
            exported = true; status.Text = Strings.Format("SavedImage", dialog.FileName); return true;
        }
        catch (Exception ex) { ShowError(Strings["SaveImageFailed"], ex); return false; }
    }
    private void SaveAndFinish() { if (SaveImage() && isRegionCapture) Close(); }
    private void CopyImage()
    {
        try
        {
            CommitPinNumber(); canvas.CommitEdits();
            using var image = document.Render();
            ImageClipboard.Write(image);
            exported = true; status.Text = Strings["ImageCopied"];
            CaptureFeedback.Play();
            if (isRegionCapture) Close();
        }
        catch (Exception ex) { ShowError(Strings["CopyImageFailed"], ex); }
    }
    private async Task Upload()
    {
        if (uploading != null) { uploading.Cancel(); return; }
        CommitPinNumber(); canvas.CommitEdits();
        using var cancellation = new CancellationTokenSource(); uploading = cancellation;
        var completed = false;
        workspace.Enabled = false;
        canvas.SetInputEnabled(false); save.Enabled = copy.Enabled = recognizeText.Enabled = false;
        foreach (var button in tools.Values.Distinct()) button.Enabled = false;
        parameterBar.Enabled = false; delete.Enabled = false; undo.Enabled = redo.Enabled = false;
        upload.Text = Strings["CancelUpload"]; SetMenuIcon(upload, UiIcon.Cancel); status.Text = Strings["UploadingImage"];
        try
        {
            using var image = document.Render();
            if (image.Width > 10000 || image.Height > 10000) throw new InvalidOperationException(Strings["YapxImageTooLarge"]);
            using var stream = new MemoryStream(); image.Save(stream, ImageFormat.Png);
            using var client = new YapxClient();
            var result = await client.UploadAsync(stream.ToArray(), cancellation.Token);
            if (IsDisposed) return;
            exported = true;
            try { Clipboard.SetText(result.Url); status.Text = Strings["UploadedLinkCopied"]; }
            catch { status.Text = Strings["UploadedCopyLink"]; }
            using var dialog = new UploadResultDialog(result, Strings); dialog.ShowDialog(this);
            completed = true;
        }
        catch (OperationCanceledException) { if (!IsDisposed) status.Text = cancellation.IsCancellationRequested ? Strings["UploadCanceled"] : Strings["UploadTimeout"]; }
        catch (Exception ex) { if (!IsDisposed) ShowError(Strings["UploadFailed"], ex); }
        finally
        {
            uploading = null;
            if (!IsDisposed)
            {
                canvas.SetInputEnabled(true); save.Enabled = copy.Enabled = recognizeText.Enabled = true;
                upload.Text = Strings["UploadToYapx"]; SetMenuIcon(upload, UiIcon.Upload);
                workspace.Enabled = true;
                foreach (var button in tools.Values.Distinct()) button.Enabled = true;
                parameterBar.Enabled = true; UpdatePropertyVisibility(); UpdateHistory();
            }
        }
        if (isRegionCapture && completed) Close();
    }
    private void ShowError(string title, Exception error)
    {
        status.Text = title + Strings["ScreenshotKeptSuffix"];
        MessageBox.Show(this, error.Message, title, MessageBoxButtons.OK, MessageBoxIcon.Warning);
    }
    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        if (isRegionCapture && !regionEditing)
        {
            if (keyData == Keys.Escape) Close();
            return true;
        }
        if (recognizingText != null)
        {
            if (keyData == Keys.Escape) recognizingText.Cancel();
            return true;
        }
        if (uploading != null) return base.ProcessCmdKey(ref msg, keyData);
        if (canvas.IsEditingOcrWord && canvas.ContainsFocus) return base.ProcessCmdKey(ref msg, keyData);
        if (canvas.HasOcrResult)
        {
            if (keyData == (Keys.Control | Keys.C)) { CopyOcrText(); return true; }
            if (keyData == (Keys.Control | Keys.A)) { canvas.SelectAllOcrWords(); return true; }
            if (keyData == Keys.F2) { canvas.EditOcrWord(); return true; }
            if (keyData == Keys.Escape)
            {
                if (!canvas.CancelCaptureResize()) HideOcrResult();
                return true;
            }
        }
        if (pinNumber.Focused)
        {
            if (keyData == (Keys.Control | Keys.S)) CommitPinNumber();
            else return base.ProcessCmdKey(ref msg, keyData);
        }
        if (canvas.IsEditingText && canvas.ContainsFocus && keyData != (Keys.Control | Keys.S)) return base.ProcessCmdKey(ref msg, keyData);
        if (keyData == (Keys.Control | Keys.S)) { SaveAndFinish(); return true; }
        if (keyData == (Keys.Control | Keys.C)) { CopyImage(); return true; }
        if (keyData == Keys.Enter && canvas.Tool == EditorTool.Crop) { canvas.CommitCrop(); return true; }
        if (keyData == (Keys.Control | Keys.Z)) { Undo(); return true; }
        if (keyData == (Keys.Control | Keys.Shift | Keys.Z) || keyData == (Keys.Control | Keys.Y)) { canvas.CommitText(); canvas.CancelGesture(false); document.Redo(); return true; }
        if (keyData == Keys.Escape)
        {
            if (canvas.CancelCaptureResize()) return true;
            if (canvas.HasMagnifierDraft) { canvas.CancelGesture(false); return true; }
            if (isRegionCapture && !canvas.HasCropChanges && canvas.Selected == null) Close();
            else canvas.CancelGesture();
            return true;
        }
        if (ActiveControl is NumericUpDown || ActiveControl is TextBoxBase) return base.ProcessCmdKey(ref msg, keyData);
        if (keyData == Keys.Delete) { canvas.DeleteSelected(); return true; }
        var tool = keyData switch { Keys.V => EditorTool.Select, Keys.P => EditorTool.Pen, Keys.R => EditorTool.Rectangle, Keys.O => EditorTool.Ellipse, Keys.A => EditorTool.Arrow, Keys.L => EditorTool.Line, Keys.T => EditorTool.Text, Keys.N => EditorTool.Pin, Keys.M => EditorTool.Magnifier, Keys.B => EditorTool.Blur, Keys.E => EditorTool.Eraser, Keys.C => EditorTool.Crop, _ => (EditorTool?)null };
        if (tool.HasValue) { SelectTool(tool.Value); return true; }
        return base.ProcessCmdKey(ref msg, keyData);
    }
    private void ClosingEditor(object? sender, FormClosingEventArgs e)
    {
        if (isRegionCapture && !regionEditing) return;
        if (recognizingText != null) { e.Cancel = true; recognizingText.Cancel(); return; }
        if (uploading != null) { e.Cancel = true; uploading.Cancel(); return; }
        CommitPinNumber(); canvas.CommitEdits();
        if (exported) return;
        var answer = MessageBox.Show(this, Strings["SaveBeforeClosing"], "RapidShot", MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question);
        if (answer == DialogResult.Cancel || (answer == DialogResult.Yes && !SaveImage())) e.Cancel = true;
    }
    protected override void Dispose(bool disposing)
    {
        if (disposing) recognizingText?.Cancel();
        if (disposing) { tips.Dispose(); foreach (ToolStripItem item in captureMenu.Items) item.Image?.Dispose(); captureMenu.Dispose(); foreach (ToolStripItem item in colorMenu.Items) item.Image?.Dispose(); colorMenu.Dispose(); canvas.Dispose(); document.Dispose(); }
        base.Dispose(disposing);
    }
}
