namespace RapidShot;

internal sealed partial class EditorForm
{
    private CancellationTokenSource? recognizingText;

    private async Task CopyRecognizedTextAsync()
    {
        if (recognizingText != null || uploading != null) return;

        using var cancellation = new CancellationTokenSource();
        recognizingText = cancellation;
        var closeAfterCopy = false;
        try
        {
            CommitPinNumber();
            canvas.CommitEdits();
            canvas.SetInputEnabled(false);
            toolBar.Enabled = parameterBar.Enabled = false;
            status.Text = "Распознаём текст… Esc — отмена";
            using var image = document.Render();
            var text = await Task.Run(() => OcrService.RecognizeAsync(image, cancellation.Token), cancellation.Token);
            if (IsDisposed) return;
            cancellation.Token.ThrowIfCancellationRequested();
            if (string.IsNullOrWhiteSpace(text))
            {
                status.Text = "Текст не найден. Уточните границы области и попробуйте снова.";
                return;
            }

            var data = new DataObject();
            data.SetText(text, TextDataFormat.UnicodeText);
            Clipboard.SetDataObject(data, true, 5, 100);
            status.Text = "Распознанный текст скопирован в буфер обмена.";
            closeAfterCopy = isRegionCapture;
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
            if (!IsDisposed) status.Text = "Распознавание отменено.";
        }
        catch (Exception ex)
        {
            if (!IsDisposed) ShowError("Не удалось распознать или скопировать текст", ex);
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

        if (closeAfterCopy && !IsDisposed)
        {
            exported = true;
            Close();
        }
    }
}
