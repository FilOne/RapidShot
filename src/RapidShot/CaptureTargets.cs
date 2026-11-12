namespace RapidShot;

internal sealed record CaptureDisplay(string DeviceName, Rectangle Bounds, bool Primary, int Number)
{
    internal string Label(UiText strings) => strings.Format("Display", Number, Bounds.Width, Bounds.Height) + (Primary ? strings["PrimaryDisplaySuffix"] : "");
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
        if (displays.Count == 0) throw new InvalidOperationException(UiText.Current["NoDisplays"]);
        if (!string.IsNullOrEmpty(deviceName))
            return displays.FirstOrDefault(x => string.Equals(x.DeviceName, deviceName, StringComparison.OrdinalIgnoreCase))?.Bounds
                ?? throw new InvalidOperationException(UiText.Current["DisplayUnavailable"]);
        return displays.Select(x => x.Bounds).Aggregate(Rectangle.Union);
    }

    public static CaptureChoice[] Choices(IReadOnlyList<CaptureDisplay> displays, string? savedDevice, UiText strings)
    {
        var choices = new List<CaptureChoice> { new(strings["SelectedRegion"], true), new(strings["AllDisplays"], false) };
        choices.AddRange(displays.Select(x => new CaptureChoice(x.Label(strings), false, x.DeviceName)));
        if (!string.IsNullOrEmpty(savedDevice) && !displays.Any(x => string.Equals(x.DeviceName, savedDevice, StringComparison.OrdinalIgnoreCase)))
            choices.Add(new(strings.Format("DisconnectedDisplay", savedDevice), false, savedDevice));
        return choices.ToArray();
    }

    public static ToolStripMenuItem[] MenuItems(IReadOnlyList<CaptureDisplay> displays, Action<string> capture) => displays.Select(display =>
    {
        var item = new ToolStripMenuItem(display.Label(UiText.Current)) { Tag = display.DeviceName, ToolTipText = $"{display.DeviceName} · X: {display.Bounds.X}, Y: {display.Bounds.Y}" };
        item.Click += (_, _) => capture(display.DeviceName); return item;
    }).ToArray();
}
