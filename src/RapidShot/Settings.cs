using System.Text.Json;

namespace RapidShot;

public enum AppTheme { System, Dark, Light }
public enum AppLanguage { Russian, English, SimplifiedChinese }

public sealed class AppSettings
{
    public uint Modifiers { get; set; } = 2 | 4;
    public int Key { get; set; } = (int)Keys.S;
    public bool CaptureRegion { get; set; } = true;
    public string? CaptureDeviceName { get; set; }
    public AppTheme ThemeMode { get; set; } = AppTheme.System;
    public AppLanguage Language { get; set; } = AppLanguage.Russian;
    public string SaveDirectory { get; set; } = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), "RapidShot");
    public static string SettingsPath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "RapidShot", "settings.json");
    public string HotkeyLabel => FormatHotkey(Modifiers, (Keys)Key);

    public static string FormatHotkey(uint modifiers, Keys key)
    {
        var parts = new List<string>();
        if ((modifiers & 2) != 0) parts.Add("Ctrl");
        if ((modifiers & 1) != 0) parts.Add("Alt");
        if ((modifiers & 4) != 0) parts.Add("Shift");
        if ((modifiers & 8) != 0) parts.Add("Win");
        parts.Add(key == Keys.PrintScreen ? "Print Screen" : key.ToString());
        return string.Join(" + ", parts);
    }

    public static AppSettings Load()
    {
        try
        {
            if (!File.Exists(SettingsPath)) return new();
            var settings = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(SettingsPath)) ?? new();
            if (!Enum.IsDefined((Keys)settings.Key) || settings.Key == 0 || settings.Modifiers > 15) return new();
            if (string.IsNullOrWhiteSpace(settings.SaveDirectory)) settings.SaveDirectory = new AppSettings().SaveDirectory;
            if (!Enum.IsDefined(settings.ThemeMode)) settings.ThemeMode = AppTheme.System;
            if (!Enum.IsDefined(settings.Language)) settings.Language = AppLanguage.Russian;
            return settings;
        }
        catch (Exception e) when (e is IOException or JsonException or UnauthorizedAccessException) { return new(); }
    }

    public void Save()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath)!);
        var temporary = SettingsPath + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
        File.Move(temporary, SettingsPath, true);
    }
}
