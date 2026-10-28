namespace RapidShot;

internal sealed partial class EditorCanvas
{
    private bool previewDirty = true;
    private Bitmap? transformPreview;
    private int transformIndex;

    private void ClearTransformPreview() { transformPreview?.Dispose(); transformPreview = null; }

    private void EnsurePreview()
    {
        // Coalesce mouse events: expensive work happens once per paint, at displayed resolution.
        var scale = Math.Min(1, zoom);
        var size = new Size(Math.Max(1, (int)Math.Round(Document.Crop.Width * scale)), Math.Max(1, (int)Math.Round(Document.Crop.Height * scale)));
        if (!previewDirty && preview?.Size == size) return;
        Bitmap next;
        if (transformOriginal != null && Selected != null)
        {
            if (transformPreview?.Size != size) ClearTransformPreview();
            if (transformPreview == null)
            {
                transformIndex = Document.Items.IndexOf(Selected);
                transformPreview = Document.RenderPreview(size, endIndex: transformIndex);
            }
            next = (Bitmap)transformPreview.Clone();
            Document.DrawItems(next, transformIndex);
        }
        else next = Document.RenderPreview(size, textOriginal);
        preview?.Dispose(); preview = next; previewDirty = false;
    }
}
