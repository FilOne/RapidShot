using System.Drawing;
using System.Drawing.Imaging;
using System.Reflection;
using System.Text.Json;
using RapidShot;

internal static class CaptureTargetTests
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    private static CaptureDisplay[] Displays() => [
        new("DISPLAY1", new(0, 0, 1920, 1080), true, 1),
        new("DISPLAY2", new(-1280, 0, 1280, 1024), false, 2),
        new("DISPLAY3", new(0, -1200, 1920, 1200), false, 3)
    ];
    public static void Run(Action<bool, string> check, string output)
    {
        var displays = Displays();
        check(CaptureTargets.ResolveBounds(displays, null) == new Rectangle(-1280, -1200, 3200, 2280), "Все экраны объединяются с учётом отрицательных координат и разных разрешений");
        check(CaptureTargets.ResolveBounds(displays, "DISPLAY2") == displays[1].Bounds && CaptureTargets.ResolveBounds(displays, "display3") == displays[2].Bounds, "Выбранный экран определяется по устройству, включая экран слева и сверху");
        check(displays[0].Label.Contains("1920 × 1080") && displays[0].Label.Contains("основной") && !displays[1].Label.Contains("основной"), "В меню экранов видны номер, разрешение и основной монитор");
        var choices = CaptureTargets.Choices(displays, null);
        check(choices.Length == 5 && choices[0].Region && !choices[1].Region && choices[1].DeviceName == null && choices[3].DeviceName == "DISPLAY2", "Настройки предлагают область, все экраны и каждый монитор отдельно");
        var selected = ""; var items = CaptureTargets.MenuItems(displays, device => selected = device);
        try
        {
            items[1].PerformClick(); check(selected == "DISPLAY2" && (string)items[1].Tag! == "DISPLAY2", "Щелчок по пункту монитора передаёт именно выбранное устройство");
        }
        finally { foreach (var item in items) item.Dispose(); }
        var missing = false;
        try { CaptureTargets.ResolveBounds(displays, "DISCONNECTED"); } catch (InvalidOperationException) { missing = true; }
        check(missing, "Отключённый монитор не заменяется незаметно снимком другого экрана");
        var unavailable = CaptureTargets.Choices(displays, "DISCONNECTED");
        check(unavailable[^1].DeviceName == "DISCONNECTED" && unavailable[^1].Label.Contains("Отключённый"), "Настройки сохраняют выбор временно отключённого монитора");
        var settings = new AppSettings { CaptureRegion = false, CaptureDeviceName = "DISPLAY2" };
        var restored = JsonSerializer.Deserialize<AppSettings>(JsonSerializer.Serialize(settings))!;
        check(restored.CaptureDeviceName == "DISPLAY2" && !restored.CaptureRegion, "Выбранный полный экран сохраняется в настройках");
        var legacy = JsonSerializer.Deserialize<AppSettings>("{\"CaptureRegion\":false}")!;
        check(legacy.CaptureDeviceName == null, "Старые настройки полного захвата продолжают означать все экраны");
        using (var form = new SettingsDialog(settings, candidate => { restored = candidate; return null; }, displays))
        {
            var mode = (ComboBox)typeof(SettingsDialog).GetField("mode", Private)!.GetValue(form)!;
            check(mode.SelectedItem is CaptureChoice { DeviceName: "DISPLAY2", Region: false }, "Настройки открываются с ранее выбранным монитором");
            mode.SelectedItem = mode.Items.Cast<CaptureChoice>().Single(x => x.DeviceName == "DISPLAY3");
            typeof(SettingsDialog).GetMethod("Save", Private)!.Invoke(form, [null, EventArgs.Empty]);
            check(restored.CaptureDeviceName == "DISPLAY3" && !restored.CaptureRegion, "Сохранение настроек передаёт новый экран для горячей клавиши");
        }
        using (var single = new SettingsDialog(new AppSettings { CaptureRegion = false, CaptureDeviceName = "" }, _ => null, [displays[0]]))
        {
            var mode = (ComboBox)typeof(SettingsDialog).GetField("mode", Private)!.GetValue(single)!;
            check(mode.SelectedItem is CaptureChoice { DeviceName: null, Region: false }, "Пустое имя экрана в настройках совместимо с захватом всех мониторов");
        }
        using (var form = new SettingsDialog(settings, _ => null, displays))
        {
            using var content = form.Controls[0]; content.Font = form.Font; content.ForeColor = form.ForeColor; content.BackColor = form.BackColor;
            form.Controls.Remove(content); content.Dock = DockStyle.None; content.Size = form.ClientSize; content.CreateControl(); content.PerformLayout();
            using var image = new Bitmap(content.Width, content.Height); content.DrawToBitmap(image, content.ClientRectangle);
            image.Save(Path.Combine(output, "monitor-settings-preview.png"), ImageFormat.Png);
        }
    }
    public static void RunNative(Action<bool, string> check)
    {
        var displays = CaptureTargets.GetDisplays();
        Console.WriteLine($"Подключено экранов: {displays.Length}. Проверка размеров снимков в памяти, без сохранения пикселей.");
        foreach (var display in displays)
        {
            using var image = CaptureService.Capture(false, display.DeviceName);
            check(image?.Size == display.Bounds.Size, $"Снимок {display.Label} имеет размер именно этого монитора");
        }
    }
}
