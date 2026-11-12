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
            status.Text = Strings["OcrRunning"];
            using var image = document.Render();
            result = await Task.Run(() => OcrService.RecognizeAsync(image, cancellation.Token), cancellation.Token);
            if (IsDisposed) return;
            cancellation.Token.ThrowIfCancellationRequested();
            if (result.Words.Length == 0)
            {
                status.Text = Strings["OcrNoText"];
                return;
            }
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
            if (!IsDisposed) status.Text = Strings["OcrCanceled"];
            return;
        }
        catch (Exception ex)
        {
            if (!IsDisposed) ShowError(Strings["OcrFailed"], ex);
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
        status.Text = canvas.OcrSelectionHint;
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
            status.Text = Strings["OcrSelectAllHint"];
            return;
        }
        try
        {
            var data = new DataObject();
            data.SetText(text, TextDataFormat.UnicodeText);
            Clipboard.SetDataObject(data, true, 5, 100);
            status.Text = Strings["OcrCopied"];
        }
        catch (Exception ex)
        {
            ShowError(Strings["OcrCopyFailed"], ex);
        }
    }
}
