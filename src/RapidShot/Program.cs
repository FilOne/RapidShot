namespace RapidShot;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        ApplicationConfiguration.Initialize();
        using var singleInstance = new Mutex(true, "Local\\RapidShot.Desktop.v1", out var first);
        if (!first)
        {
            MessageBox.Show("RapidShot уже запущен. Нажмите на значок в трее или используйте горячую клавишу.", "RapidShot");
            return;
        }
        Application.ThreadException += (_, e) => MessageBox.Show(e.Exception.Message, "RapidShot — ошибка", MessageBoxButtons.OK, MessageBoxIcon.Error);
        Theme.Initialize(AppSettings.Load().ThemeMode);
        try { using var context = new TrayApplication(args); Application.Run(context); }
        finally { Theme.Shutdown(); }
    }
}
