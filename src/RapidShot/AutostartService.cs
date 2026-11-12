using Microsoft.Win32;

namespace RapidShot;

internal sealed class AutostartService(string registryPath = AutostartService.RunKeyPath)
{
    internal const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string EntryName = "RapidShot";
    internal bool IsEnabled
    {
        get
        {
            using var key = Registry.CurrentUser.OpenSubKey(registryPath);
            return key?.GetValue(EntryName) is string command && !string.IsNullOrWhiteSpace(command);
        }
    }
    internal void SetEnabled(bool enabled)
    {
        if (enabled)
        {
            var command = CreateCommand(Environment.ProcessPath ?? throw new IOException(UiText.Current["AutostartPathMissing"]));
            using var key = Registry.CurrentUser.CreateSubKey(registryPath, writable: true);
            key.SetValue(EntryName, command, RegistryValueKind.String);
        }
        else
        {
            using var key = Registry.CurrentUser.OpenSubKey(registryPath, writable: true);
            key?.DeleteValue(EntryName, throwOnMissingValue: false);
        }
    }
    internal static string CreateCommand(string executable)
    {
        if (Path.GetFileNameWithoutExtension(executable).Equals("dotnet", StringComparison.OrdinalIgnoreCase))
            throw new IOException(UiText.Current["AutostartRequiresExe"]);
        var command = $"\"{executable}\" --autostart";
        if (command.Length > 260) throw new IOException(UiText.Current["AutostartPathTooLong"]);
        return command;
    }
}
