using System.Globalization;
using System.Text.Json;

namespace RapidShot;

internal sealed class UiText
{
    private static readonly Dictionary<string, string[]> Catalog = LoadCatalog();
    private static readonly UiText Russian = new(0, "ru-RU");
    private static readonly UiText English = new(1, "en-US");
    private static readonly UiText SimplifiedChinese = new(2, "zh-CN");

    private readonly int column;
    private readonly CultureInfo culture;

    private UiText(int column, string cultureName)
    {
        this.column = column;
        culture = CultureInfo.GetCultureInfo(cultureName);
    }

    internal static UiText Current { get; set; } = Russian;

    internal static UiText For(AppLanguage language) => language switch
    {
        AppLanguage.English => English,
        AppLanguage.SimplifiedChinese => SimplifiedChinese,
        _ => Russian
    };

    internal string this[string key] => Catalog[key][column];

    internal string Format(string key, params object?[] values) => string.Format(culture, this[key], values);

    private static Dictionary<string, string[]> LoadCatalog()
    {
        using var stream = typeof(UiText).Assembly.GetManifestResourceStream("RapidShot.UiText.json")
            ?? throw new InvalidOperationException("RapidShot UI translations are missing.");
        return JsonSerializer.Deserialize<Dictionary<string, string[]>>(stream)
            ?? throw new InvalidOperationException("RapidShot UI translations are empty.");
    }
}
