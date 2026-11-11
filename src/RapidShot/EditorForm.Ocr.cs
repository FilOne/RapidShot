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
            ocrResultPanel.Enabled = false;
            status.Text = "Распознаём текст… Esc — отмена";
            using var image = document.Render();
            result = await Task.Run(() => OcrService.RecognizeAsync(image, cancellation.Token), cancellation.Token);
            if (IsDisposed) return;
            cancellation.Token.ThrowIfCancellationRequested();
            if (string.IsNullOrWhiteSpace(result.Text))
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
                ocrResultPanel.Enabled = true;
            }
        }

        ShowOcrResult(result);
    }

    private void ShowOcrResult(OcrResult result)
    {
        ocrResultPanel.TextEditor.Text = result.Text;
        ocrResultPanel.TextEditor.Select(0, 0);
        canvas.SetOcrHighlights(result.Words);
        ocrResultPanel.Visible = true;
        if (isRegionCapture)
        {
            PositionRegionBars();
            ocrResultPanel.BringToFront();
        }
        status.Text = "Текст распознан. Его можно выделить, исправить и скопировать.";
        ocrResultPanel.TextEditor.Focus();
    }

    private void HideOcrResult()
    {
        ocrResultPanel.Visible = false;
        canvas.SetOcrHighlights([]);
        if (isRegionCapture) PositionRegionBars();
        status.Text = "";
        canvas.Focus();
    }

    private void CopyOcrText()
    {
        var editor = ocrResultPanel.TextEditor;
        var text = editor.SelectionLength > 0 ? editor.SelectedText : editor.Text;
        if (text.Length == 0) return;
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

internal sealed class OcrResultPanel : TableLayoutPanel
{
    internal const int PreferredWidth = 520;
    internal TextBox TextEditor { get; }

    internal OcrResultPanel(Action copy, Action close)
    {
        Size = new Size(PreferredWidth, 156);
        MinimumSize = new Size(0, Height);
        Anchor = AnchorStyles.None;
        BackColor = Theme.Panel;
        Padding = new Padding(8);
        Margin = new Padding(12, 3, 12, 3);
        ColumnCount = 3;
        RowCount = 2;
        AccessibleName = "Распознанный текст";
        Visible = false;
        ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        RowStyles.Add(new RowStyle(SizeType.Absolute, 37));
        RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        Controls.Add(new Label
        {
            Text = "Распознанный текст", Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft, ForeColor = Theme.Foreground,
            AutoEllipsis = true
        }, 0, 0);
        var copyButton = Theme.Button("Копировать", (_, _) => copy());
        copyButton.AccessibleName = "Копировать выделенный текст или весь результат";
        copyButton.Height = 30;
        copyButton.Anchor = AnchorStyles.None;
        Controls.Add(copyButton, 1, 0);
        var closeButton = new IconButton(UiIcon.Cancel, "Закрыть распознанный текст") { Anchor = AnchorStyles.None };
        closeButton.Click += (_, _) => close();
        Controls.Add(closeButton, 2, 0);
        TextEditor = new TextBox
        {
            AccessibleName = "Редактор распознанного текста", Dock = DockStyle.Fill, Multiline = true,
            AcceptsReturn = true, ScrollBars = ScrollBars.Vertical, HideSelection = false,
            MaxLength = 0, BackColor = Theme.Background, ForeColor = Theme.Foreground,
            Margin = new Padding(0, 4, 0, 0)
        };
        Controls.Add(TextEditor, 0, 1);
        SetColumnSpan(TextEditor, 3);
    }
}
