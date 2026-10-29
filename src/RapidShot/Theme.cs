using System.Drawing.Drawing2D;
using Microsoft.Win32;

namespace RapidShot;

internal static class Theme
{
    private sealed record Palette(bool Dark, Color Background, Color Panel, Color Raised, Color Foreground, Color Muted, Color Disabled, Color Hover, Color Border, Color Separator, Color Error);
    private static readonly Palette DarkPalette = new(true, Color.FromArgb(24, 26, 30), Color.FromArgb(42, 45, 50), Color.FromArgb(55, 59, 65), Color.FromArgb(234, 238, 247), Color.FromArgb(151, 158, 168), Color.FromArgb(103, 109, 118), Color.FromArgb(66, 71, 78), Color.FromArgb(65, 69, 76), Color.FromArgb(74, 78, 85), Color.Salmon);
    private static readonly Palette LightPalette = new(false, Color.FromArgb(242, 245, 249), Color.White, Color.FromArgb(230, 235, 242), Color.FromArgb(28, 35, 46), Color.FromArgb(87, 99, 116), Color.FromArgb(145, 156, 171), Color.FromArgb(218, 233, 245), Color.FromArgb(202, 211, 223), Color.FromArgb(213, 221, 232), Color.FromArgb(180, 35, 45));
    private static Palette palette = SystemIsDark() ? DarkPalette : LightPalette;
    private static readonly List<WeakReference<Form>> forms = [];
    private static readonly List<WeakReference<ContextMenuStrip>> menus = [];
    private static ThemeListener? listener;
    private static System.Windows.Forms.Timer? timer;
    public static AppTheme Mode { get; private set; } = AppTheme.System;
    public static bool IsDark => palette.Dark;
    public static Color Background => palette.Background;
    public static Color Panel => palette.Panel;
    public static Color Raised => palette.Raised;
    public static Color Foreground => palette.Foreground;
    public static Color Muted => palette.Muted;
    public static Color Disabled => palette.Disabled;
    public static Color Hover => palette.Hover;
    public static Color Border => palette.Border;
    public static Color Separator => palette.Separator;
    public static Color Error => palette.Error;
    public static Color OnAccent => DarkPalette.Background;
    public static Color Shadow => IsDark ? Color.FromArgb(12, 14, 20) : Color.FromArgb(40, 30, 44, 64);
    public static readonly Color Accent = Color.FromArgb(67, 190, 241);
    public static void Initialize(AppTheme mode)
    {
        SetMode(mode); SetNativeColorMode();
        listener ??= new ThemeListener();
        timer ??= new System.Windows.Forms.Timer { Interval = 1500 };
        timer.Tick += RefreshSystem; timer.Start();
    }
    public static void Shutdown() { timer?.Dispose(); timer = null; listener?.Dispose(); listener = null; }
    private static bool SystemIsDark()
    {
        try { return Registry.GetValue(@"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize", "AppsUseLightTheme", 1) is int value && value == 0; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException) { return false; }
    }
    private static void RefreshSystem(object? sender = null, EventArgs? e = null) { if (Mode == AppTheme.System) SetMode(AppTheme.System); }
    public static void SetMode(AppTheme mode)
    {
        Mode = Enum.IsDefined(mode) ? mode : AppTheme.System;
        var next = Mode == AppTheme.Dark || Mode == AppTheme.System && SystemIsDark() ? DarkPalette : LightPalette;
        if (ReferenceEquals(palette, next)) return;
        var previous = palette; palette = next; SetNativeColorMode();
        forms.RemoveAll(reference => !reference.TryGetTarget(out var form) || form.IsDisposed);
        foreach (var reference in forms.ToArray()) if (reference.TryGetTarget(out var form) && !form.IsDisposed)
        {
            form.SuspendLayout(); Recolor(form, previous, next); ApplyTitle(form);
            if (form is IThemeAware themed) themed.RefreshTheme();
            form.ResumeLayout(); form.Invalidate(true);
        }
        menus.RemoveAll(reference => !reference.TryGetTarget(out var menu) || menu.IsDisposed);
        foreach (var reference in menus.ToArray()) if (reference.TryGetTarget(out var menu)) StyleMenu(menu);
    }
    private static void SetNativeColorMode()
    {
#pragma warning disable WFO5001
        Application.SetColorMode(IsDark ? SystemColorMode.Dark : SystemColorMode.Classic);
#pragma warning restore WFO5001
    }
    private static Color Remap(Color color, Palette before, Palette after)
    {
        Color[] from = [before.Background, before.Panel, before.Raised, before.Foreground, before.Muted, before.Disabled, before.Hover, before.Border, before.Separator, before.Error];
        Color[] to = [after.Background, after.Panel, after.Raised, after.Foreground, after.Muted, after.Disabled, after.Hover, after.Border, after.Separator, after.Error];
        for (var i = 0; i < from.Length; i++) if (color.ToArgb() == from[i].ToArgb()) return to[i];
        return color;
    }
    private static void Recolor(Control control, Palette before, Palette after)
    {
        if (control is TextBoxBase && control.Parent is EditorCanvas) return; // Annotation ink is content, not a UI color.
        control.BackColor = Remap(control.BackColor, before, after);
        control.ForeColor = control.BackColor == Accent && control is ButtonBase ? OnAccent : Remap(control.ForeColor, before, after);
        if (control is ButtonBase button) button.FlatAppearance.MouseOverBackColor = control.BackColor == Accent ? Color.FromArgb(91, 202, 245) : Hover;
        foreach (Control child in control.Controls) Recolor(child, before, after);
        control.Invalidate();
    }
    public static void Menu(ContextMenuStrip menu) { menus.Add(new(menu)); StyleMenu(menu); }
    private static void StyleMenu(ContextMenuStrip menu)
    {
        menu.BackColor = Panel; menu.ForeColor = Foreground;
        if (menu.Renderer is not MinimalMenuRenderer) menu.Renderer = new MinimalMenuRenderer();
        menu.Invalidate(true);
    }
    private static void ApplyTitle(Form form)
    {
        if (!form.IsHandleCreated) return;
        var dark = IsDark ? 1 : 0; NativeMethods.DwmSetWindowAttribute(form.Handle, 20, ref dark, sizeof(int));
    }
    public static void Apply(Form form)
    {
        form.BackColor = Background; form.ForeColor = Foreground;
        form.Font = new Font("Segoe UI", 10);
        form.Icon = CreateIcon();
        form.StartPosition = FormStartPosition.CenterScreen;
        forms.Add(new(form)); form.HandleCreated += (_, _) => ApplyTitle(form);
        form.VisibleChanged += (_, _) => { if (form.Visible) { SetNativeColorMode(); ApplyTitle(form); } };
    }
    public static Button Button(string text, EventHandler? click = null, bool accent = false)
    {
        var button = new Button { Text = text, AutoSize = true, Height = 36, Padding = new Padding(10, 4, 10, 4), Margin = new Padding(4), FlatStyle = FlatStyle.Flat, BackColor = accent ? Accent : Raised, ForeColor = accent ? OnAccent : Foreground, Cursor = Cursors.Hand, UseVisualStyleBackColor = false };
        button.FlatAppearance.BorderSize = 0;
        button.FlatAppearance.MouseOverBackColor = accent ? Color.FromArgb(91, 202, 245) : Hover;
        if (click != null) button.Click += click;
        return button;
    }
    public static Label Label(string text, bool muted = false) => new() { Text = text, AutoSize = true, ForeColor = muted ? Muted : Foreground, Margin = new Padding(4, 8, 4, 4) };
    public static ComboBox ComboBox()
    {
        var combo = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, FlatStyle = FlatStyle.Flat, BackColor = Raised, ForeColor = Foreground, DrawMode = DrawMode.OwnerDrawFixed, ItemHeight = 24 };
        combo.DrawItem += (_, e) =>
        {
            using var background = new SolidBrush((e.State & DrawItemState.Selected) != 0 ? Hover : Raised);
            e.Graphics.FillRectangle(background, e.Bounds);
            if (e.Index >= 0) TextRenderer.DrawText(e.Graphics, combo.Items[e.Index]?.ToString() ?? "", combo.Font, e.Bounds, Foreground, TextFormatFlags.Left | TextFormatFlags.VerticalCenter);
        };
        return combo;
    }
    public static Icon CreateIcon()
    {
        using var image = new Bitmap(64, 64);
        using (var g = Graphics.FromImage(image))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(Color.Transparent);
            using var bg = new SolidBrush(DarkPalette.Background); g.FillEllipse(bg, 1, 1, 62, 62);
            using var pen = new Pen(Accent, 5) { StartCap = LineCap.Round, EndCap = LineCap.Round };
            g.DrawLines(pen, [new Point(13, 26), new(13, 16), new(25, 16)]);
            g.DrawLines(pen, [new Point(39, 16), new(51, 16), new(51, 26)]);
            g.DrawLines(pen, [new Point(13, 39), new(13, 49), new(25, 49)]);
            g.DrawLines(pen, [new Point(39, 49), new(51, 49), new(51, 39)]);
            using var dot = new SolidBrush(Accent); g.FillEllipse(dot, 24, 24, 16, 16);
        }
        var handle = image.GetHicon();
        try { using var borrowed = Icon.FromHandle(handle); return (Icon)borrowed.Clone(); }
        finally { NativeMethods.DestroyIcon(handle); }
    }
    private sealed class ThemeListener : NativeWindow, IDisposable
    {
        public ThemeListener() => CreateHandle(new CreateParams { Caption = "RapidShot.ThemeListener" });
        protected override void WndProc(ref Message message)
        {
            base.WndProc(ref message);
            if (message.Msg is 0x001A or 0x031A) RefreshSystem();
        }
        public void Dispose() => DestroyHandle();
    }
}

internal interface IThemeAware { void RefreshTheme(); }
