using Microsoft.Win32;

namespace RapidShot;

internal sealed class AutostartService(string registryPath = AutostartService.RunKeyPath)
{
    internal const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string EntryName = "RapidShot";
    internal bool IsEnabled => !string.IsNullOrWhiteSpace(RegisteredCommand);

    internal string? RegisteredCommand
    {
        get
        {
            using var key = Registry.CurrentUser.OpenSubKey(registryPath);
            return key?.GetValue(EntryName, null, RegistryValueOptions.DoNotExpandEnvironmentNames) as string;
        }
        set
        {
            if (value == null)
            {
                using var key = Registry.CurrentUser.OpenSubKey(registryPath, writable: true);
                key?.DeleteValue(EntryName, throwOnMissingValue: false);
            }
            else
            {
                using var key = Registry.CurrentUser.CreateSubKey(registryPath, writable: true);
                key.SetValue(EntryName, value, RegistryValueKind.String);
            }
        }
    }
    internal void SetEnabled(bool enabled) => RegisteredCommand = enabled
        ? CreateCommand(Environment.ProcessPath ?? throw new IOException(UiText.Current["AutostartPathMissing"])) : null;
    internal static string CreateCommand(string executable)
    {
        if (Path.GetFileNameWithoutExtension(executable).Equals("dotnet", StringComparison.OrdinalIgnoreCase))
            throw new IOException(UiText.Current["AutostartRequiresExe"]);
        var command = $"\"{executable}\" --autostart";
        if (command.Length > 260) throw new IOException(UiText.Current["AutostartPathTooLong"]);
        return command;
    }
}
