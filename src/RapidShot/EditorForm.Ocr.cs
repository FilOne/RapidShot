namespace RapidShot;

internal sealed partial class EditorForm
{
    private CancellationTokenSource? recognizingText;

    private async Task RecognizeTextAsync()
    {
        if (recognizingText != null || uploading != null) return;

        using var cancellation = new CancellationTokenSource();
        recognizingText = cancellation;
        OcrResult result;
        try
        {
            CommitPinNumber();
            canvas.CommitEdits();
            canvas.SetInputEnabled(false);
            toolBar.Enabled = parameterBar.Enabled = false;
            status.Text = "Распознаём текст… Esc — отмена";
            using var image = document.Render();
            result = await Task.Run(() => OcrService.RecognizeAsync(image, cancellation.Token), cancellation.Token);
            if (IsDisposed) return;
            cancellation.Token.ThrowIfCancellationRequested();
            if (result.Words.Length == 0)
            {
                status.Text = "Текст не найден. Уточните границы области и попробуйте снова.";
                return;
            }
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
            if (!IsDisposed) status.Text = "Распознавание отменено.";
            return;
        }
        catch (Exception ex)
        {
            if (!IsDisposed) ShowError("Не удалось распознать текст", ex);
            return;
        }
        finally
        {
            recognizingText = null;
            if (!IsDisposed)
            {
                canvas.SetInputEnabled(true);
                toolBar.Enabled = parameterBar.Enabled = true;
            }
        }

        ShowOcrResult(result);
    }

    private void ShowOcrResult(OcrResult result)
    {
        canvas.SetOcrResult(result);
        recognizeText.Selected = true;
        HighlightTool(canvas.Tool);
        UpdatePropertyVisibility();
        if (isRegionCapture) status.BackColor = Theme.Panel;
        status.Text = EditorCanvas.OcrSelectionHint;
        canvas.Focus();
    }

    private void HideOcrResult()
    {
        if (!canvas.HasOcrResult) return;
        canvas.SetOcrResult(null);
        recognizeText.Selected = false;
        HighlightTool(canvas.Tool);
        UpdatePropertyVisibility();
        if (isRegionCapture) status.BackColor = Color.Transparent;
        status.Text = "";
    }

    private void CopyOcrText()
    {
        var text = canvas.SelectedOcrText;
        if (text.Length == 0)
        {
            status.Text = "Выделите слова на снимке · Ctrl+A — выделить весь текст";
            return;
        }
        try
        {
            var data = new DataObject();
            data.SetText(text, TextDataFormat.UnicodeText);
            Clipboard.SetDataObject(data, true, 5, 100);
            status.Text = "Текст скопирован в буфер обмена.";
        }
        catch (Exception ex)
        {
            ShowError("Не удалось скопировать текст", ex);
        }
    }
}
