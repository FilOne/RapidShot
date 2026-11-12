namespace RapidShot;

internal sealed partial class EditorCanvas
{
    private readonly System.Windows.Forms.Timer blurHintTimer = new() { Interval = 50 };
    private Size? lastBlurSize;

    private void StartBlurFeedback()
    {
        lastBlurSize = null; blurHintTimer.Start();
    }
    private void PublishBlurSize()
    {
        if (pending?.Tool != EditorTool.Blur) return;
        var bounds = pending.Bounds; var size = new Size((int)bounds.Width, (int)bounds.Height);
        if (lastBlurSize == size) return;
        lastBlurSize = size;
        HintChanged?.Invoke(Strings.Format("SelectionSize", size.Width, size.Height));
    }
    private void StopBlurFeedback(bool publish = false)
    {
        blurHintTimer.Stop();
        if (publish) PublishBlurSize();
        lastBlurSize = null;
    }
}
