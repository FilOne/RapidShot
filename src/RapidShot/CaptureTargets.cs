namespace RapidShot;

internal sealed record CaptureDisplay(string DeviceName, Rectangle Bounds, bool Primary, int Number)
{
    public string Label => $"Экран {Number} — {Bounds.Width} × {Bounds.Height}" + (Primary ? " (основной)" : "");
}

internal sealed record CaptureChoice(string Label, bool Region, string? DeviceName = null)
{
    public override string ToString() => Label;
}

internal static class CaptureTargets
{
    public static CaptureDisplay[] GetDisplays() => Screen.AllScreens.Select((screen, index) =>
    {
        var suffix = new string(screen.DeviceName.Reverse().TakeWhile(char.IsDigit).Reverse().ToArray());
        var number = int.TryParse(suffix, out var value) ? value : index + 1;
        return new CaptureDisplay(screen.DeviceName, screen.Bounds, screen.Primary, number);
    }).OrderBy(x => x.Number).ToArray();

    public static Rectangle ResolveBounds(IReadOnlyList<CaptureDisplay> displays, string? deviceName)
    {
        if (displays.Count == 0) throw new InvalidOperationException("Нет доступных экранов для снимка.");
        if (!string.IsNullOrEmpty(deviceName))
            return displays.FirstOrDefault(x => string.Equals(x.DeviceName, deviceName, StringComparison.OrdinalIgnoreCase))?.Bounds
                ?? throw new InvalidOperationException("Выбранный экран отключён. Выберите другой экран в меню трея или настройках.");
        return displays.Select(x => x.Bounds).Aggregate(Rectangle.Union);
    }

    public static CaptureChoice[] Choices(IReadOnlyList<CaptureDisplay> displays, string? savedDevice)
    {
        var choices = new List<CaptureChoice> { new("Выделенную область", true), new("Все экраны целиком", false) };
        choices.AddRange(displays.Select(x => new CaptureChoice(x.Label, false, x.DeviceName)));
        if (!string.IsNullOrEmpty(savedDevice) && !displays.Any(x => string.Equals(x.DeviceName, savedDevice, StringComparison.OrdinalIgnoreCase)))
            choices.Add(new($"Отключённый экран ({savedDevice})", false, savedDevice));
        return choices.ToArray();
    }

    public static ToolStripMenuItem[] MenuItems(IReadOnlyList<CaptureDisplay> displays, Action<string> capture) => displays.Select(display =>
    {
        var item = new ToolStripMenuItem(display.Label) { Tag = display.DeviceName, ToolTipText = $"{display.DeviceName} · X: {display.Bounds.X}, Y: {display.Bounds.Y}" };
        item.Click += (_, _) => capture(display.DeviceName); return item;
    }).ToArray();
}
