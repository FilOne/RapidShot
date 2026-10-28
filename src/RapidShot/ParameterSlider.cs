namespace RapidShot;

internal sealed class ParameterSlider : TrackBar
{
    public bool IsInteracting { get; private set; }
    public event Action? InteractionStarted;
    public event Action? InteractionEnded;
    public ParameterSlider()
    {
        AutoSize = false; Height = 20; TickStyle = TickStyle.None; BackColor = Theme.Panel;
        SmallChange = 1; LargeChange = 2; Margin = Padding.Empty;
    }
    private void BeginInteraction()
    {
        if (IsInteracting) return;
        IsInteracting = true; InteractionStarted?.Invoke();
    }
    private void EndInteraction()
    {
        if (!IsInteracting) return;
        IsInteracting = false; InteractionEnded?.Invoke();
    }
    protected override void WndProc(ref Message m)
    {
        var key = (Keys)(m.WParam.ToInt64() & 0xFFFF);
        if (m.Msg == 0x0201 || (m.Msg == 0x0100 && key is Keys.Left or Keys.Right or Keys.Up or Keys.Down or Keys.Home or Keys.End or Keys.PageUp or Keys.PageDown)) BeginInteraction();
        base.WndProc(ref m);
        if (m.Msg is 0x0202 or 0x0101) EndInteraction();
    }
    protected override void OnMouseCaptureChanged(EventArgs e) { base.OnMouseCaptureChanged(e); if (!Capture) EndInteraction(); }
}
