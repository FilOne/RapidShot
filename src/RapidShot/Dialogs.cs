namespace RapidShot;

internal sealed class SettingsDialog : Form
{
    public AppSettings Result { get; private set; }
    private readonly TextBox shortcut;
    private readonly CheckBox ctrl, alt, shift, win;
    private readonly ComboBox key, mode;
    private readonly TextBox folder;
    private readonly Label feedback;
    private readonly Func<AppSettings, string?> apply;
    public SettingsDialog(AppSettings current, Func<AppSettings, string?> apply, IReadOnlyList<CaptureDisplay>? displays = null)
    {
        Result = current; this.apply = apply;
        Theme.Apply(this); Text = "RapidShot — настройки"; ClientSize = new(590, 485);
        FormBorderStyle = FormBorderStyle.FixedDialog; MaximizeBox = false; MinimizeBox = false;
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(24), ColumnCount = 1, RowCount = 11 };
        layout.Controls.Add(new Label { Text = "Быстрее, чем искать кнопку", AutoSize = true, Font = new Font("Segoe UI", 17, FontStyle.Bold), Margin = new Padding(0, 0, 0, 15) });
        layout.Controls.Add(Theme.Label("Горячая клавиша для снимка"));
        var modifiers = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill };
        CheckBox Check(string text, uint flag) => new() { Text = text, Checked = (current.Modifiers & flag) != 0, AutoSize = true, Margin = new Padding(4, 4, 16, 4) };
        ctrl = Check("Ctrl", 2); alt = Check("Alt", 1); shift = Check("Shift", 4); win = Check("Win", 8);
        modifiers.Controls.AddRange([ctrl, alt, shift, win]);
        key = Theme.ComboBox(); key.Width = 140;
        var keys = new List<Keys> { Keys.PrintScreen, Keys.Pause, Keys.Insert, Keys.Home, Keys.End, Keys.PageUp, Keys.PageDown, Keys.Scroll, Keys.Space };
        keys.AddRange(Enumerable.Range((int)Keys.F1, 24).Select(x => (Keys)x));
        keys.AddRange(Enumerable.Range((int)Keys.A, 26).Select(x => (Keys)x));
        keys.AddRange(Enumerable.Range((int)Keys.D0, 10).Select(x => (Keys)x));
        if (!keys.Contains((Keys)current.Key)) keys.Add((Keys)current.Key);
        key.Items.AddRange(keys.Cast<object>().ToArray()); key.SelectedItem = (Keys)current.Key;
        modifiers.Controls.Add(key); layout.Controls.Add(modifiers);
        shortcut = new TextBox { ReadOnly = true, Dock = DockStyle.Fill, BackColor = Theme.Raised, ForeColor = Theme.Accent, BorderStyle = BorderStyle.FixedSingle, Text = current.HotkeyLabel, TabStop = false };
        layout.Controls.Add(shortcut);
        void RefreshLabel(object? sender, EventArgs e) => shortcut.Text = AppSettings.FormatHotkey(Modifiers(), (Keys)(key.SelectedItem ?? Keys.S));
        foreach (var check in new[] { ctrl, alt, shift, win }) check.CheckedChanged += RefreshLabel;
        key.SelectedIndexChanged += RefreshLabel;
        layout.Controls.Add(Theme.Label("Что снимать по горячей клавише"));
        mode = Theme.ComboBox(); mode.Dock = DockStyle.Fill;
        var choices = CaptureTargets.Choices(displays ?? CaptureTargets.GetDisplays(), current.CaptureDeviceName);
        mode.Items.AddRange(choices);
        mode.SelectedItem = choices.First(x => x.Region == current.CaptureRegion && (x.Region || string.Equals(x.DeviceName ?? "", current.CaptureDeviceName ?? "", StringComparison.OrdinalIgnoreCase)));
        mode.AccessibleName = "Экран или область для горячей клавиши"; layout.Controls.Add(mode);
        layout.Controls.Add(Theme.Label("Папка сохранения по умолчанию"));
        var folderRow = new TableLayoutPanel { AutoSize = true, ColumnCount = 2, Dock = DockStyle.Fill };
        folderRow.ColumnStyles.Add(new(SizeType.Percent, 100)); folderRow.ColumnStyles.Add(new(SizeType.AutoSize));
        folder = new TextBox { Text = current.SaveDirectory, Dock = DockStyle.Fill, Margin = new Padding(4, 10, 4, 4), BackColor = Theme.Raised, ForeColor = Theme.Foreground };
        folderRow.Controls.Add(folder);
        folderRow.Controls.Add(Theme.Button("Обзор…", (_, _) => { using var dialog = new FolderBrowserDialog { InitialDirectory = folder.Text }; if (dialog.ShowDialog(this) == DialogResult.OK) folder.Text = dialog.SelectedPath; }));
        layout.Controls.Add(folderRow);
        layout.Controls.Add(new Label { Text = "После запуска приложение находится в трее.\nДвойной щелчок по значку — новый снимок. Выход — через меню трея.", AutoSize = true, ForeColor = Theme.Muted, Margin = new Padding(4, 14, 4, 8) });
        feedback = new Label { AutoSize = true, MaximumSize = new(530, 0), ForeColor = Color.Salmon }; layout.Controls.Add(feedback);
        var buttons = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft };
        var save = Theme.Button("Сохранить", Save, true);
        var close = Theme.Button("Отмена", (_, _) => Close()); buttons.Controls.AddRange([save, close]); layout.Controls.Add(buttons);
        Controls.Add(layout); AcceptButton = save; CancelButton = close;
    }
    private uint Modifiers() => (ctrl.Checked ? 2u : 0) | (alt.Checked ? 1u : 0) | (shift.Checked ? 4u : 0) | (win.Checked ? 8u : 0);
    private void Save(object? sender, EventArgs e)
    {
        try
        {
            var path = Path.GetFullPath(Environment.ExpandEnvironmentVariables(folder.Text.Trim()));
            var choice = (CaptureChoice)mode.SelectedItem!;
            var settings = new AppSettings { Modifiers = Modifiers(), Key = (int)(Keys)key.SelectedItem!, CaptureRegion = choice.Region, CaptureDeviceName = choice.DeviceName, SaveDirectory = path };
            var error = apply(settings);
            if (error != null) { feedback.Text = error; return; }
            Result = settings; DialogResult = DialogResult.OK; Close();
        }
        catch (Exception ex) when (ex is IOException or ArgumentException or UnauthorizedAccessException or NotSupportedException) { feedback.Text = ex.Message; }
    }
}

internal sealed class UploadResultDialog : Form
{
    public UploadResultDialog(UploadResult result)
    {
        Theme.Apply(this); Text = "RapidShot — загружено на Yapx"; ClientSize = new(600, 240);
        FormBorderStyle = FormBorderStyle.FixedDialog; MaximizeBox = false; MinimizeBox = false;
        var layout = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, Padding = new Padding(22) };
        layout.Controls.Add(new Label { Text = "Ссылка готова", Font = new Font("Segoe UI", 20, FontStyle.Bold), AutoSize = true });
        layout.Controls.Add(Theme.Label("Страница изображения", true));
        layout.Controls.Add(new TextBox { Text = result.Url, ReadOnly = true, Width = 540, BackColor = Theme.Raised, ForeColor = Theme.Foreground });
        var actions = new FlowLayoutPanel { AutoSize = true };
        actions.Controls.Add(Theme.Button("Копировать ссылку", (_, _) => { try { Clipboard.SetText(result.Url); } catch (Exception ex) { MessageBox.Show(this, ex.Message, "Буфер обмена"); } }, true));
        actions.Controls.Add(Theme.Button("Открыть в браузере", (_, _) => System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(result.Url) { UseShellExecute = true })));
        layout.Controls.Add(actions);
        var close = Theme.Button("Готово", (_, _) => Close()); layout.Controls.Add(close); CancelButton = close; Controls.Add(layout);
    }
}
