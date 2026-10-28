namespace RapidShot;

internal sealed record ToolDefinition(EditorTool Tool, string Icon, string Name, string Shortcut);

internal sealed class ToolGroupButton : Button
{
    private readonly ToolDefinition[] definitions;
    private readonly ContextMenuStrip menu;
    private readonly System.Windows.Forms.Timer holdTimer = new() { Interval = 450 };
    private bool held;
    public EditorTool CurrentTool { get; private set; }
    public event Action<EditorTool>? ToolChosen;
    public ToolGroupButton(params ToolDefinition[] definitions)
    {
        this.definitions = definitions;
        AutoSize = false; Size = new(40, 32); Padding = Padding.Empty; Margin = new Padding(3, 2, 3, 2);
        Font = new Font("Segoe UI", 14); FlatStyle = FlatStyle.Flat; FlatAppearance.BorderSize = 0;
        BackColor = Theme.Raised; ForeColor = Theme.Foreground; Cursor = Cursors.Hand;
        FlatAppearance.MouseOverBackColor = Color.FromArgb(61, 68, 87);
        menu = new ContextMenuStrip { BackColor = Theme.Panel, ForeColor = Theme.Foreground, ShowImageMargin = false, ShowCheckMargin = true };
        foreach (var definition in definitions)
        {
            var item = new ToolStripMenuItem($"{definition.Icon}  {definition.Name}    {definition.Shortcut}") { Tag = definition.Tool };
            item.Click += (_, _) => { held = false; Remember(definition.Tool); ToolChosen?.Invoke(definition.Tool); };
            menu.Items.Add(item);
        }
        if (definitions.Length > 1) ContextMenuStrip = menu;
        holdTimer.Tick += (_, _) => { holdTimer.Stop(); if (Capture && Enabled) { held = true; Capture = false; OpenMenu(); } };
        Remember(definitions[0].Tool);
    }
    public void Remember(EditorTool tool)
    {
        var definition = definitions.First(x => x.Tool == tool);
        CurrentTool = tool; Text = definition.Icon; AccessibleName = definition.Name;
        foreach (ToolStripMenuItem item in menu.Items) item.Checked = (EditorTool)item.Tag! == tool;
        Invalidate();
    }
    private void OpenMenu()
    {
        if (definitions.Length > 1) menu.Show(this, Point.Empty, ToolStripDropDownDirection.AboveRight);
    }
    protected override void OnMouseDown(MouseEventArgs e)
    {
        held = false; base.OnMouseDown(e);
        if (e.Button == MouseButtons.Left && definitions.Length > 1) holdTimer.Start();
    }
    protected override void OnMouseUp(MouseEventArgs e) { holdTimer.Stop(); base.OnMouseUp(e); }
    protected override void OnMouseMove(MouseEventArgs e) { if (!ClientRectangle.Contains(e.Location)) holdTimer.Stop(); base.OnMouseMove(e); }
    protected override void OnClick(EventArgs e)
    {
        if (held) return;
        base.OnClick(e); ToolChosen?.Invoke(CurrentTool);
    }
    protected override void OnMouseCaptureChanged(EventArgs e) { holdTimer.Stop(); base.OnMouseCaptureChanged(e); }
    protected override void OnKeyDown(KeyEventArgs e) { if (e.KeyCode is Keys.Space or Keys.Enter) held = false; base.OnKeyDown(e); }
    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        if (definitions.Length > 1 && (keyData == Keys.Down || keyData == (Keys.Alt | Keys.Down))) { OpenMenu(); return true; }
        return base.ProcessCmdKey(ref msg, keyData);
    }
    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        if (definitions.Length < 2) return;
        using var brush = new SolidBrush(ForeColor);
        e.Graphics.FillPolygon(brush, [new Point(Width - 7, Height - 3), new(Width - 3, Height - 3), new(Width - 3, Height - 7)]);
    }
    protected override void Dispose(bool disposing)
    {
        if (disposing) { holdTimer.Dispose(); menu.Dispose(); }
        base.Dispose(disposing);
    }
}
