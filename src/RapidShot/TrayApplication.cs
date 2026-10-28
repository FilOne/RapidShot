using System.Drawing.Drawing2D;

namespace RapidShot;

internal sealed class TrayApplication : ApplicationContext
{
    private AppSettings settings = AppSettings.Load();
    private readonly NotifyIcon tray;
    private readonly HotkeyWindow hotkey = new();
    private readonly List<EditorForm> editors = [];
    private readonly ToolStripMenuItem captureItem;
    private SettingsDialog? settingsDialog;
    private bool capturing;
    private bool hotkeyRegistered;
    public TrayApplication(string[] args)
    {
        var menu = new ContextMenuStrip();
        captureItem = new("Снимок области", null, async (_, _) => await Capture(true));
        menu.Items.Add(captureItem);
        menu.Items.Add("Снимок всех экранов", null, async (_, _) => await Capture(false));
        menu.Items.Add("Открыть изображение…", null, (_, _) => OpenImage());
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Настройки…", null, (_, _) => ShowSettings());
        menu.Items.Add("Папка скриншотов", null, (_, _) => OpenFolder());
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Выход", null, (_, _) => Exit());
        tray = new NotifyIcon { Icon = Theme.CreateIcon(), Text = "RapidShot", ContextMenuStrip = menu, Visible = true };
        tray.DoubleClick += async (_, _) => await Capture(settings.CaptureRegion);
        hotkey.Pressed += async () => await Capture(settings.CaptureRegion);
        hotkeyRegistered = hotkey.Register(settings.Modifiers, settings.Key);
        UpdateTray();
        var timer = new System.Windows.Forms.Timer { Interval = 600 };
        timer.Tick += (_, _) =>
        {
            timer.Stop(); timer.Dispose();
            if (args.Contains("--demo")) ShowEditor(CreateDemo());
            else if (args.Contains("--settings")) ShowSettings();
            if (hotkeyRegistered) tray.ShowBalloonTip(4000, "RapidShot готов", $"Снимок: {settings.HotkeyLabel}. Настройки и выход — в меню значка.", ToolTipIcon.Info);
            else
            {
                MessageBox.Show($"Не удалось зарегистрировать {settings.HotkeyLabel}. Возможно, сочетание занято другой программой. Выберите другое сочетание в настройках. Снимки через меню трея доступны.", "RapidShot", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                ShowSettings();
            }
        };
        timer.Start();
    }
    private void UpdateTray()
    {
        var text = $"RapidShot · {settings.HotkeyLabel}";
        tray.Text = text.Length > 63 ? "RapidShot" : text;
        captureItem.Text = "Снимок области" + (settings.CaptureRegion ? $"    {settings.HotkeyLabel}" : "");
    }
    private async Task Capture(bool region)
    {
        if (capturing || settingsDialog is { IsDisposed: false } || editors.Any(x => x.Modal)) return;
        // Do not take the desktop while a text/save/upload dialog belonging to an editor is open.
        if (Application.OpenForms.Cast<Form>().Any(x => x.Modal)) return;
        capturing = true;
        var visible = editors.Where(x => !x.IsDisposed && x.Visible).ToArray();
        foreach (var editor in visible) editor.Hide();
        Bitmap? image = null;
        try
        {
            await Task.Delay(180);
            image = CaptureService.Capture(region);
        }
        catch (Exception ex) { MessageBox.Show(ex.Message, "Не удалось сделать снимок", MessageBoxButtons.OK, MessageBoxIcon.Warning); }
        finally
        {
            foreach (var editor in visible) if (!editor.IsDisposed) editor.Show();
            capturing = false;
        }
        if (image != null) ShowEditor(image);
    }
    private void ShowEditor(Bitmap image)
    {
        try
        {
            var editor = new EditorForm(image, settings);
            editors.Add(editor); editor.FormClosed += (_, _) => editors.Remove(editor);
            editor.Show(); editor.Activate();
        }
        catch { image.Dispose(); throw; }
    }
    private void OpenImage()
    {
        using var dialog = new OpenFileDialog { Filter = "Изображения|*.png;*.jpg;*.jpeg;*.bmp;*.gif", Title = "Открыть в RapidShot" };
        if (dialog.ShowDialog() != DialogResult.OK) return;
        try { using var source = new Bitmap(dialog.FileName); ShowEditor(new Bitmap(source)); }
        catch (Exception ex) { MessageBox.Show(ex.Message, "Не удалось открыть изображение", MessageBoxButtons.OK, MessageBoxIcon.Warning); }
    }
    private void ShowSettings()
    {
        if (settingsDialog is { IsDisposed: false }) { settingsDialog.Activate(); return; }
        hotkey.Suspended = true;
        try
        {
            using var dialog = new SettingsDialog(settings, candidate =>
            {
                var changed = candidate.Modifiers != settings.Modifiers || candidate.Key != settings.Key;
                if ((changed || !hotkeyRegistered) && !hotkey.Register(candidate.Modifiers, candidate.Key)) return "Сочетание занято или недоступно. Выберите другую клавишу.";
                try { candidate.Save(); }
                catch (Exception ex)
                {
                    if (changed) hotkey.Register(settings.Modifiers, settings.Key);
                    return "Не удалось сохранить настройки: " + ex.Message;
                }
                settings = candidate; hotkeyRegistered = true; UpdateTray(); return null;
            });
            settingsDialog = dialog; dialog.ShowDialog();
        }
        finally { settingsDialog = null; hotkey.Suspended = false; }
    }
    private void OpenFolder()
    {
        try
        {
            Directory.CreateDirectory(settings.SaveDirectory);
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(settings.SaveDirectory) { UseShellExecute = true });
        }
        catch (Exception ex) { MessageBox.Show(ex.Message, "Папка скриншотов"); }
    }
    private void Exit()
    {
        foreach (var editor in editors.ToArray()) { editor.Close(); if (!editor.IsDisposed) return; }
        tray.Visible = false; ExitThread();
    }
    protected override void Dispose(bool disposing)
    {
        if (disposing) { hotkey.Dispose(); tray.Visible = false; tray.Icon?.Dispose(); tray.ContextMenuStrip?.Dispose(); tray.Dispose(); }
        base.Dispose(disposing);
    }
    internal static Bitmap CreateDemo()
    {
        var image = new Bitmap(1280, 760);
        using var g = Graphics.FromImage(image); g.SmoothingMode = SmoothingMode.AntiAlias;
        g.Clear(Color.FromArgb(238, 242, 248));
        using var navy = new SolidBrush(Color.FromArgb(27, 39, 58)); g.FillRectangle(navy, 0, 0, 250, 760);
        using var white = new SolidBrush(Color.White); using var muted = new SolidBrush(Color.FromArgb(140, 158, 183));
        using var title = new Font("Segoe UI", 28, FontStyle.Bold); using var text = new Font("Segoe UI", 15); using var small = new Font("Segoe UI", 12);
        g.DrawString("WHITEHILL", new Font("Segoe UI", 20, FontStyle.Bold), white, 26, 38);
        g.DrawString("Рабочее пространство", small, muted, 26, 88);
        g.DrawString("Обзор\n\nПроекты\n\nЗадачи\n\nКоманда", text, white, 28, 164);
        g.DrawString("Обзор проекта", title, navy, 298, 55);
        g.DrawString("Всё важное — на одном экране", text, muted, 302, 108);
        using var accent = new SolidBrush(Color.FromArgb(48, 158, 137));
        for (var i = 0; i < 3; i++)
        {
            var x = 302 + i * 304; g.FillRectangle(white, x, 180, 276, 145);
            g.DrawString(new[] { "Задачи", "Завершено", "В работе" }[i], text, muted, x + 22, 201);
            g.DrawString(new[] { "128", "96", "32" }[i], title, navy, x + 20, 246);
        }
        g.FillRectangle(white, 302, 366, 884, 314);
        g.DrawString("Активность за неделю", text, navy, 326, 388);
        for (var i = 0; i < 7; i++)
        {
            var height = new[] { 74, 125, 103, 167, 143, 188, 162 }[i];
            g.FillRectangle(accent, 340 + i * 116, 630 - height, 60, height);
            g.DrawString(new[] { "Пн", "Вт", "Ср", "Чт", "Пт", "Сб", "Вс" }[i], small, muted, 355 + i * 116, 640);
        }
        return image;
    }
}
