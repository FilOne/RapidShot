using System.Drawing.Drawing2D;

namespace RapidShot;

internal sealed partial class EditorCanvas
{
    private enum MagnifierStage { None, Source, Placement }
    private MagnifierStage magnifierStage;
    private Bitmap? magnifierSample;
    private bool editMagnifierSource, transformMagnifierSource;
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public float MagnifierZoom { get; set; } = 2;
    public bool EditingMagnifierSource => editMagnifierSource && Selected?.Tool == EditorTool.Magnifier;
    public bool HasMagnifierDraft => magnifierStage != MagnifierStage.None;

    public void ToggleMagnifierSource()
    {
        FinishObjectTransform();
        if (Selected?.Tool != EditorTool.Magnifier) return;
        editMagnifierSource = !editMagnifierSource;
        SelectionChanged?.Invoke(); Invalidate();
        HintChanged?.Invoke(editMagnifierSource ? "Источник: перетащите рамку или её маркеры. «Готово» вернёт к увеличенному фрагменту." : "Перетащите увеличенный фрагмент или потяните маркеры. Масштаб и обводка — снизу.");
    }
    public void UpdateMagnifierZoom(bool recordUndo = true)
    {
        if (magnifierStage == MagnifierStage.Placement && pending != null)
        {
            MagnifierGeometry.SetZoom(pending, MagnifierZoom, Document.Crop); Invalidate(); return;
        }
        FinishObjectTransform();
        if (Selected is not { Tool: EditorTool.Magnifier } item) return;
        var updated = item.Clone(); MagnifierGeometry.SetZoom(updated, MagnifierZoom, Document.Crop);
        if (updated.Magnification == item.Magnification) { MagnifierZoom = item.Magnification; SelectionChanged?.Invoke(); return; }
        if (recordUndo) Document.BeginChange();
        item.Start = updated.Start; item.End = updated.End; item.Magnification = updated.Magnification;
        Document.NotifyChanged(); SelectionChanged?.Invoke();
    }
    private void StartMagnifier(PointF p)
    {
        if (magnifierStage == MagnifierStage.Placement)
        {
            PositionMagnifier(p); dragging = true; Capture = true; return;
        }
        editMagnifierSource = false; ClearSelection();
        pending = new Annotation { Tool = EditorTool.Magnifier, Start = p, End = p, Color = Ink, Width = StrokeWidth, Magnification = MagnifierZoom };
        magnifierStage = MagnifierStage.Source; dragging = true; Capture = true;
        CropStateChanged?.Invoke();
        HintChanged?.Invoke("Выделите исходный фрагмент, который нужно увеличить. Затем щёлкните в месте его размещения.");
    }
    private void PositionMagnifier(PointF p)
    {
        if (pending == null) return;
        MagnifierGeometry.SetZoom(pending, pending.Magnification, Document.Crop, p); Invalidate();
    }
    private void EndMagnifier(PointF p)
    {
        if (pending == null) return;
        if (magnifierStage == MagnifierStage.Source)
        {
            pending.End = p; var source = pending.Bounds;
            if (source.Width < 3 || source.Height < 3) { CancelMagnifier(); dragging = false; Capture = false; Invalidate(); return; }
            pending.SourceBounds = source;
            // Sample at preview resolution once; moving the placement preview only draws this patch.
            EnsurePreview();
            if (preview == null) { CancelMagnifier(); dragging = false; Capture = false; return; }
            var sx = preview.Width / (float)Document.Crop.Width; var sy = preview.Height / (float)Document.Crop.Height;
            magnifierSample?.Dispose();
            magnifierSample = MagnifierGeometry.Sample(preview, new((source.X - Document.Crop.X) * sx, (source.Y - Document.Crop.Y) * sy, source.Width * sx, source.Height * sy));
            MagnifierGeometry.SetZoom(pending, MagnifierZoom, Document.Crop, p);
            magnifierStage = MagnifierStage.Placement; dragging = false; Capture = false;
            HintChanged?.Invoke("Щёлкните, где разместить увеличенный фрагмент. Esc — отменить. Масштаб и обводку можно изменить после размещения.");
        }
        else if (magnifierStage == MagnifierStage.Placement)
        {
            PositionMagnifier(p); var item = pending; pending = null;
            magnifierStage = MagnifierStage.None; dragging = false; Capture = false;
            magnifierSample?.Dispose(); magnifierSample = null;
            Selected = item; Document.Add(item); SelectionChanged?.Invoke();
            CropStateChanged?.Invoke();
            HintChanged?.Invoke("Фрагмент добавлен. Масштаб и обводка — снизу; «Источник» позволяет изменить исходную область.");
        }
        Invalidate();
    }
    private void CancelMagnifier()
    {
        var changed = HasMagnifierDraft;
        if (pending?.Tool == EditorTool.Magnifier) pending = null;
        magnifierStage = MagnifierStage.None; magnifierSample?.Dispose(); magnifierSample = null;
        if (changed) CropStateChanged?.Invoke();
    }
    private void DrawMagnifierDraft(Graphics g)
    {
        if (pending?.Tool != EditorTool.Magnifier) return;
        var source = magnifierStage == MagnifierStage.Source ? pending.Bounds : pending.SourceBounds;
        if (magnifierStage == MagnifierStage.Placement && magnifierSample != null) MagnifierGeometry.DrawPatch(g, magnifierSample, pending.Bounds, pending);
        using var fill = new SolidBrush(Color.FromArgb(25, Theme.Accent)); using var border = new Pen(Theme.Accent, 1.5f / zoom) { DashStyle = DashStyle.Dash };
        g.FillRectangle(fill, source); g.DrawRectangle(border, source.X, source.Y, source.Width, source.Height);
    }
    private void DrawMagnifierSource(Graphics g)
    {
        if (!EditingMagnifierSource || Selected == null) return;
        var b = Selected.SourceBounds; var tl = ToScreen(b.Location); var br = ToScreen(new(b.Right, b.Bottom));
        using var fill = new SolidBrush(Color.FromArgb(30, Theme.Accent)); g.FillRectangle(fill, tl.X, tl.Y, br.X - tl.X, br.Y - tl.Y);
        using var line = new Pen(Theme.Accent, 1) { DashStyle = DashStyle.Dash };
        g.DrawLine(line, ToScreen(MagnifierGeometry.Center(b)), ToScreen(MagnifierGeometry.Center(Selected.Bounds)));
        TextRenderer.DrawText(g, "Источник", Font, new Point((int)tl.X, (int)tl.Y - Font.Height - 4), Theme.Accent);
    }
    private void TransformMagnifier(Annotation item, Annotation original, float dx, float dy)
    {
        var b = MagnifierGeometry.MoveInside(transformMagnifierSource ? original.SourceBounds : original.Bounds, Document.Crop);
        if (transformHandle == ObjectHandle.Move)
        {
            if (transformMagnifierSource) MagnifierGeometry.SetSource(item, new(b.X + dx, b.Y + dy, b.Width, b.Height), Document.Crop);
            else
            {
                var bounds = original.Bounds;
                var target = MagnifierGeometry.MoveDestination(new(bounds.X + dx, bounds.Y + dy, bounds.Width, bounds.Height), Document.Crop);
                item.Start = target.Location; item.End = new(target.Right, target.Bottom);
            }
            return;
        }
        var left = transformHandle is ObjectHandle.Left or ObjectHandle.TopLeft or ObjectHandle.BottomLeft;
        var right = transformHandle is ObjectHandle.Right or ObjectHandle.TopRight or ObjectHandle.BottomRight;
        var top = transformHandle is ObjectHandle.Top or ObjectHandle.TopLeft or ObjectHandle.TopRight;
        var bottom = transformHandle is ObjectHandle.Bottom or ObjectHandle.BottomLeft or ObjectHandle.BottomRight;
        var crop = Document.Crop;
        // Source and destination stay inside the current capture, with at least two pixels per side.
        var l = left ? Math.Clamp(b.Left + dx, crop.Left, b.Right - 2) : b.Left;
        var r = right ? Math.Clamp(b.Right + dx, b.Left + 2, crop.Right) : b.Right;
        var t = top ? Math.Clamp(b.Top + dy, crop.Top, b.Bottom - 2) : b.Top;
        var d = bottom ? Math.Clamp(b.Bottom + dy, b.Top + 2, crop.Bottom) : b.Bottom;
        var raw = RectangleF.FromLTRB(l, t, r, d);
        if (transformMagnifierSource)
        {
            if (shiftConstraint) raw = ConstrainResize(b, raw, left, right, top, bottom, false);
            MagnifierGeometry.SetSource(item, raw, crop); return;
        }
        var targetBounds = ConstrainResize(b, raw, left, right, top, bottom, false);
        var factor = Math.Clamp(targetBounds.Width / original.SourceBounds.Width, MagnifierGeometry.MinZoom, MagnifierGeometry.MaxZoom);
        var w = original.SourceBounds.Width * factor; var h = original.SourceBounds.Height * factor;
        var x = left ? b.Right - w : right ? b.Left : b.X + (b.Width - w) / 2;
        var y = top ? b.Bottom - h : bottom ? b.Top : b.Y + (b.Height - h) / 2;
        item.Start = new(x, y); item.End = new(x + w, y + h);
        MagnifierGeometry.SetZoom(item, factor, crop);
    }
}
