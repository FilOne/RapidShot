namespace RapidShot;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        ApplicationConfiguration.Initialize();
        var settings = AppSettings.Load();
        UiText.Current = UiText.For(settings.Language);
        using var singleInstance = new Mutex(true, "Local\\RapidShot.Desktop.v1", out var first);
        if (!first)
        {
            if (!args.Contains("--autostart", StringComparer.OrdinalIgnoreCase))
                MessageBox.Show(UiText.Current["AlreadyRunning"], "RapidShot");
            return;
        }
        Application.ThreadException += (_, e) => MessageBox.Show(e.Exception.Message, UiText.Current["ApplicationError"], MessageBoxButtons.OK, MessageBoxIcon.Error);
        Theme.Initialize(settings.ThemeMode);
        try { CaptureFeedback.Initialize(); using var context = new TrayApplication(args, settings); Application.Run(context); }
        finally { CaptureFeedback.Shutdown(); Theme.Shutdown(); }
    }
}
