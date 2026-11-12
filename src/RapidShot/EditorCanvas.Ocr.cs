using System.Drawing.Drawing2D;

namespace RapidShot;

internal sealed partial class EditorCanvas
{
    internal const string OcrSelectionHint = "Выделите слова на снимке · Ctrl+C — копировать · Двойной щелчок — исправить";

    private OcrResult? ocrResult;
    private int ocrAnchor = -1, ocrEnd = -1;
    private bool selectingOcrText;
    private InlineTextBox? ocrWordEditor;
    internal bool HasOcrResult => ocrResult != null;
    internal bool IsEditingOcrWord => ocrWordEditor != null;
    internal string SelectedOcrText => ocrResult != null && ocrAnchor >= 0
        ? ocrResult.GetText(Math.Min(ocrAnchor, ocrEnd), Math.Max(ocrAnchor, ocrEnd)) : "";

    internal void SetOcrResult(OcrResult? result)
    {
        if (ocrResult == result) return;
        CloseOcrWordEditor(false);
        EndOcrSelection();
        ocrResult = result;
        ocrAnchor = ocrEnd = -1;
        Cursor = Cursors.Default;
        Invalidate();
    }

    internal void SelectAllOcrWords()
    {
        if (ocrResult == null || ocrResult.Words.Length == 0) return;
        EndOcrSelection();
        ocrAnchor = 0;
        ocrEnd = ocrResult.Words.Length - 1;
        Invalidate();
    }

    private void DrawOcrHighlights(Graphics graphics)
    {
        if (ocrResult == null || IsResizingCapture) return;
        var first = Math.Min(ocrAnchor, ocrEnd);
        var last = Math.Max(ocrAnchor, ocrEnd);
        using var recognized = new SolidBrush(Color.FromArgb(33, Theme.Accent));
        using var selected = new SolidBrush(Color.FromArgb(135, Theme.Accent));
        using var border = new Pen(Theme.Accent, 1 / zoom) { Alignment = PenAlignment.Inset };
        for (var i = 0; i < ocrResult.Words.Length; i++)
        {
            var bounds = ocrResult.Words[i].Bounds;
            if (bounds.IsEmpty) continue;
            bounds.Offset(Document.Crop.Location);
            graphics.FillRectangle(i >= first && i <= last ? selected : recognized, bounds);
            graphics.DrawRectangle(border, bounds.X, bounds.Y, bounds.Width, bounds.Height);
        }
    }

    private int FindOcrWord(Point location, bool nearest = false)
    {
        if (ocrResult == null || (!nearest && !imageBounds.Contains(location))) return -1;
        var point = ToImage(location);
        point.X -= Document.Crop.X;
        point.Y -= Document.Crop.Y;
        var closest = -1;
        var distance = nearest ? float.PositiveInfinity : 4 / (zoom * zoom);
        for (var i = 0; i < ocrResult.Words.Length; i++)
        {
            var bounds = ocrResult.Words[i].Bounds;
            if (bounds.IsEmpty) continue;
            var dx = point.X - Math.Clamp(point.X, bounds.Left, bounds.Right);
            var dy = point.Y - Math.Clamp(point.Y, bounds.Top, bounds.Bottom);
            var squared = dx * dx + dy * dy;
            if (squared == 0) return i;
            if (squared >= distance) continue;
            distance = squared;
            closest = i;
        }
        return closest;
    }

    private void BeginOcrSelection(MouseEventArgs e, int word)
    {
        CloseOcrWordEditor(true);
        ocrAnchor = ocrEnd = word;
        Invalidate();
        if (word < 0) return;
        if (e.Clicks > 1)
        {
            EditOcrWord();
            return;
        }
        selectingOcrText = true;
        Capture = true;
    }

    private void ExtendOcrSelection(Point location)
    {
        var word = FindOcrWord(location, nearest: true);
        if (word < 0 || word == ocrEnd) return;
        ocrEnd = word;
        Invalidate();
    }

    private void EndOcrSelection()
    {
        if (!selectingOcrText) return;
        selectingOcrText = false;
        Capture = false;
    }

    internal void EditOcrWord()
    {
        if (ocrResult == null || ocrEnd < 0 || ocrAnchor != ocrEnd || ocrWordEditor != null) return;
        EndOcrSelection();
        ocrWordEditor = new InlineTextBox
        {
            BorderStyle = BorderStyle.FixedSingle,
            Text = ocrResult.Words[ocrEnd].Text,
            AccessibleName = "Исправление распознанного слова"
        };
        ocrWordEditor.AcceptEdit += () => FinishOcrWordEdit(true);
        ocrWordEditor.CancelEdit += () => FinishOcrWordEdit(false);
        ocrWordEditor.Leave += (_, _) => CloseOcrWordEditor(true);
        ocrWordEditor.TextChanged += (_, _) => PositionOcrWordEditor();
        Controls.Add(ocrWordEditor);
        PositionOcrWordEditor();
        ocrWordEditor.BringToFront();
        ocrWordEditor.Focus();
        ocrWordEditor.SelectAll();
        HintChanged?.Invoke("Enter — сохранить правку · Esc — отменить");
    }

    private void PositionOcrWordEditor()
    {
        if (ocrWordEditor == null || ocrResult == null) return;
        var bounds = ocrResult.Words[ocrEnd].Bounds;
        bounds.Offset(Document.Crop.Location);
        var location = Point.Round(ToScreen(bounds.Location));
        var viewport = Rectangle.Intersect(ClientRectangle, Rectangle.Round(imageBounds));
        if (viewport.IsEmpty) return;
        var textWidth = TextRenderer.MeasureText(ocrWordEditor.Text, ocrWordEditor.Font).Width;
        var width = Math.Min(viewport.Width, Math.Max(96, Math.Max(textWidth + 12, (int)Math.Ceiling(bounds.Width * zoom))));
        var height = Math.Min(viewport.Height, ocrWordEditor.PreferredHeight);
        ocrWordEditor.Bounds = new Rectangle(Math.Clamp(location.X, viewport.Left, viewport.Right - width),
            Math.Clamp(location.Y, viewport.Top, viewport.Bottom - height), width, height);
        ocrWordEditor.BackColor = Theme.Raised;
        ocrWordEditor.ForeColor = Theme.Foreground;
    }

    private void FinishOcrWordEdit(bool commit)
    {
        CloseOcrWordEditor(commit);
        Focus();
    }

    private void CloseOcrWordEditor(bool commit)
    {
        if (ocrWordEditor is not { } editor) return;
        ocrWordEditor = null;
        if (commit && ocrResult != null)
            ocrResult.Words[ocrEnd] = ocrResult.Words[ocrEnd] with { Text = editor.Text.Trim() };
        Controls.Remove(editor);
        editor.Dispose();
        if (!Disposing) HintChanged?.Invoke(OcrSelectionHint);
    }
}
