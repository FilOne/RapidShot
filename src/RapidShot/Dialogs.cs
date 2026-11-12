namespace RapidShot;

internal sealed class SettingsDialog : Form
{
    public AppSettings Result { get; private set; }
    private readonly TextBox shortcut;
    private readonly CheckBox ctrl, alt, shift, win, autostart;
    private readonly ComboBox key, mode, appearance, language;
    private readonly TextBox folder;
    private readonly Label feedback;
    private readonly Func<AppSettings, bool?, string?> apply;
    private readonly bool? initialAutostart;
    private readonly UiText Strings;
    private CancellationTokenSource? updateCheck;
    public SettingsDialog(AppSettings current, bool? startWithWindows, Func<AppSettings, bool?, string?> apply, IReadOnlyList<CaptureDisplay>? displays = null)
    {
        Result = current; this.apply = apply; initialAutostart = startWithWindows;
        Strings = UiText.For(current.Language);
        Theme.Apply(this); Text = Strings["SettingsTitle"]; ClientSize = new(590, 620);
        FormBorderStyle = FormBorderStyle.FixedDialog; MaximizeBox = false; MinimizeBox = false;
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(24), ColumnCount = 1, RowCount = 14 };
        layout.Controls.Add(new Label { Text = Strings["SettingsHeading"], AutoSize = true, Font = new Font("Segoe UI", 17, FontStyle.Bold), Margin = new Padding(0, 0, 0, 15) });
        layout.Controls.Add(Theme.Label(Strings["CaptureHotkey"]));
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
        layout.Controls.Add(Theme.Label(Strings["CaptureTarget"]));
        mode = Theme.ComboBox(); mode.Dock = DockStyle.Fill;
        var choices = CaptureTargets.Choices(displays ?? CaptureTargets.GetDisplays(), current.CaptureDeviceName, Strings);
        mode.Items.AddRange(choices);
        mode.SelectedItem = choices.First(x => x.Region == current.CaptureRegion && (x.Region || string.Equals(x.DeviceName ?? "", current.CaptureDeviceName ?? "", StringComparison.OrdinalIgnoreCase)));
        mode.AccessibleName = Strings["CaptureTargetName"]; layout.Controls.Add(mode);
        var interfaceRow = new TableLayoutPanel { AutoSize = true, ColumnCount = 2, RowCount = 2, Dock = DockStyle.Fill, Margin = Padding.Empty };
        interfaceRow.ColumnStyles.Add(new(SizeType.Percent, 50));
        interfaceRow.ColumnStyles.Add(new(SizeType.Percent, 50));
        interfaceRow.Controls.Add(Theme.Label(Strings["Appearance"]), 0, 0);
        interfaceRow.Controls.Add(Theme.Label(Strings["InterfaceLanguage"]), 1, 0);
        appearance = Theme.ComboBox(); appearance.Dock = DockStyle.Fill; appearance.AccessibleName = Strings["ThemeName"];
        appearance.Items.AddRange([Strings["SystemTheme"], Strings["DarkTheme"], Strings["LightTheme"]]); appearance.SelectedIndex = (int)current.ThemeMode;
        language = Theme.ComboBox(); language.Dock = DockStyle.Fill; language.AccessibleName = Strings["InterfaceLanguage"];
        language.Items.AddRange(["Русский", "English", "简体中文"]); language.SelectedIndex = (int)current.Language;
        interfaceRow.Controls.Add(appearance, 0, 1);
        interfaceRow.Controls.Add(language, 1, 1);
        layout.Controls.Add(interfaceRow);
        layout.Controls.Add(Theme.Label(Strings["DefaultSaveFolder"]));
        var folderRow = new TableLayoutPanel { AutoSize = true, ColumnCount = 2, Dock = DockStyle.Fill };
        folderRow.ColumnStyles.Add(new(SizeType.Percent, 100)); folderRow.ColumnStyles.Add(new(SizeType.AutoSize));
        folder = new TextBox { Text = current.SaveDirectory, Dock = DockStyle.Fill, Margin = new Padding(4, 10, 4, 4), BackColor = Theme.Raised, ForeColor = Theme.Foreground };
        folderRow.Controls.Add(folder);
        folderRow.Controls.Add(Theme.Button(Strings["Browse"], (_, _) => { using var dialog = new FolderBrowserDialog { InitialDirectory = folder.Text }; if (dialog.ShowDialog(this) == DialogResult.OK) folder.Text = dialog.SelectedPath; }));
        layout.Controls.Add(folderRow);
        autostart = new CheckBox
        {
            Text = Strings["StartWithWindows"], AccessibleDescription = Strings["AutostartHint"], AutoSize = true,
            CheckState = startWithWindows switch { true => CheckState.Checked, false => CheckState.Unchecked, null => CheckState.Indeterminate },
            Enabled = startWithWindows.HasValue, Margin = new Padding(4, 8, 4, 0)
        };
        layout.Controls.Add(autostart);
        layout.Controls.Add(new Label { Text = Strings["TrayHelp"], AutoSize = true, ForeColor = Theme.Muted, Margin = new Padding(4, 8, 4, 8) });
        layout.Controls.Add(CreateUpdateControls());
        feedback = new Label { AutoSize = true, MaximumSize = new(530, 0), ForeColor = Theme.Error }; layout.Controls.Add(feedback);
        var buttons = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft };
        var save = Theme.Button(Strings["Save"], Save, true);
        var close = Theme.Button(Strings["Cancel"], (_, _) => Close()); buttons.Controls.AddRange([save, close]); layout.Controls.Add(buttons);
        Controls.Add(layout); AcceptButton = save; CancelButton = close;
        Disposed += (_, _) => updateCheck?.Cancel();
    }

    private Control CreateUpdateControls()
    {
        var row = new TableLayoutPanel { AutoSize = true, ColumnCount = 3, RowCount = 1, Dock = DockStyle.Fill, Margin = Padding.Empty };
        row.ColumnStyles.Add(new(SizeType.AutoSize));
        row.ColumnStyles.Add(new(SizeType.Percent, 100));
        row.ColumnStyles.Add(new(SizeType.AutoSize));

        var check = Theme.Button(Strings["CheckForUpdates"]);
        var status = new Label
        {
            Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, AutoEllipsis = true,
            ForeColor = Theme.Muted, Text = Strings.Format("InstalledVersion", UpdateChecker.CurrentVersion)
        };
        Uri? installer = null;
        var download = Theme.Button(Strings["Download"], (_, _) =>
        {
            if (installer == null) return;
            try
            {
                using var browser = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(installer.AbsoluteUri) { UseShellExecute = true });
            }
            catch (System.ComponentModel.Win32Exception)
            {
                status.Text = Strings["BrowserError"];
            }
        });
        download.Visible = false;
        check.Click += async (_, _) =>
        {
            if (updateCheck != null) return;
            using var cancellation = new CancellationTokenSource();
            updateCheck = cancellation;
            check.Enabled = download.Visible = false;
            installer = null;
            status.Text = Strings["CheckingForUpdates"];
            try
            {
                var latest = await UpdateChecker.Default.FindUpdateAsync(cancellation.Token);
                if (IsDisposed) return;
                status.Text = latest == null ? Strings.Format("UpToDate", UpdateChecker.CurrentVersion) : Strings.Format("UpdateAvailable", latest);
                if (latest != null)
                {
                    installer = UpdateChecker.InstallerUrl(latest);
                    download.Visible = true;
                }
            }
            catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { }
            catch (Exception ex) when (ex is HttpRequestException or InvalidDataException or OperationCanceledException)
            {
                if (!IsDisposed) status.Text = Strings["UpdateCheckFailed"];
            }
            finally
            {
                updateCheck = null;
                if (!IsDisposed) check.Enabled = true;
            }
        };
        row.Controls.Add(check, 0, 0);
        row.Controls.Add(status, 1, 0);
        row.Controls.Add(download, 2, 0);
        return row;
    }

    private uint Modifiers() => (ctrl.Checked ? 2u : 0) | (alt.Checked ? 1u : 0) | (shift.Checked ? 4u : 0) | (win.Checked ? 8u : 0);
    private void Save(object? sender, EventArgs e)
    {
        try
        {
            var path = Path.GetFullPath(Environment.ExpandEnvironmentVariables(folder.Text.Trim()));
            var choice = (CaptureChoice)mode.SelectedItem!;
            var settings = new AppSettings
            {
                Modifiers = Modifiers(), Key = (int)(Keys)key.SelectedItem!, CaptureRegion = choice.Region,
                CaptureDeviceName = choice.DeviceName, SaveDirectory = path, ThemeMode = (AppTheme)appearance.SelectedIndex,
                Language = (AppLanguage)language.SelectedIndex
            };
            bool? autostartChange = initialAutostart.HasValue && autostart.Checked != initialAutostart ? autostart.Checked : null;
            var error = apply(settings, autostartChange);
            if (error != null) { feedback.Text = error; return; }
            Result = settings; DialogResult = DialogResult.OK; Close();
        }
        catch (Exception ex) when (ex is IOException or ArgumentException or UnauthorizedAccessException or NotSupportedException) { feedback.Text = ex.Message; }
    }
}

internal sealed class UploadResultDialog : Form
{
    public UploadResultDialog(UploadResult result, UiText strings)
    {
        Theme.Apply(this); Text = strings["UploadResultTitle"]; ClientSize = new(600, 240);
        FormBorderStyle = FormBorderStyle.FixedDialog; MaximizeBox = false; MinimizeBox = false;
        var layout = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, Padding = new Padding(22) };
        layout.Controls.Add(new Label { Text = strings["LinkReady"], Font = new Font("Segoe UI", 20, FontStyle.Bold), AutoSize = true });
        layout.Controls.Add(Theme.Label(strings["ImagePage"], true));
        layout.Controls.Add(new TextBox { Text = result.Url, ReadOnly = true, Width = 540, BackColor = Theme.Raised, ForeColor = Theme.Foreground });
        var actions = new FlowLayoutPanel { AutoSize = true };
        actions.Controls.Add(Theme.Button(strings["CopyLink"], (_, _) => { try { Clipboard.SetText(result.Url); } catch (Exception ex) { MessageBox.Show(this, ex.Message, strings["Clipboard"]); } }, true));
        actions.Controls.Add(Theme.Button(strings["OpenInBrowser"], (_, _) => System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(result.Url) { UseShellExecute = true })));
        layout.Controls.Add(actions);
        var close = Theme.Button(strings["Done"], (_, _) => Close()); layout.Controls.Add(close); CancelButton = close; Controls.Add(layout);
    }
}
