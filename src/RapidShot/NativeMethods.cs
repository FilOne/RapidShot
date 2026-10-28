using System.Runtime.InteropServices;

namespace RapidShot;

internal static class NativeMethods
{
    [DllImport("user32.dll", SetLastError = true)]
    internal static extern bool RegisterHotKey(IntPtr handle, int id, uint modifiers, uint key);
    [DllImport("user32.dll")]
    internal static extern bool UnregisterHotKey(IntPtr handle, int id);
    [DllImport("user32.dll")]
    internal static extern bool DestroyIcon(IntPtr handle);
    [DllImport("user32.dll")]
    internal static extern bool GetCursorPos(out Point point);
    [DllImport("dwmapi.dll")]
    internal static extern int DwmSetWindowAttribute(IntPtr handle, int attribute, ref int value, int size);
}

internal sealed class HotkeyWindow : NativeWindow, IDisposable
{
    private int activeId;
    private int nextId = 100;
    public event Action? Pressed;
    public bool Suspended { get; set; }
    public HotkeyWindow() => CreateHandle(new CreateParams { Caption = "RapidShot.Hotkey", Parent = new IntPtr(-3) });
    public bool Register(uint modifiers, int key)
    {
        // Register the replacement before releasing the old binding, so failure preserves it.
        var replacement = nextId++;
        if (!NativeMethods.RegisterHotKey(Handle, replacement, modifiers | 0x4000, (uint)key)) return false;
        if (activeId != 0) NativeMethods.UnregisterHotKey(Handle, activeId);
        activeId = replacement;
        return true;
    }
    protected override void WndProc(ref Message m)
    {
        if (m.Msg == 0x0312 && m.WParam.ToInt32() == activeId && !Suspended) Pressed?.Invoke();
        base.WndProc(ref m);
    }
    public void Dispose()
    {
        if (activeId != 0) NativeMethods.UnregisterHotKey(Handle, activeId);
        DestroyHandle();
    }
}
