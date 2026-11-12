namespace RapidShot;

internal sealed class UpdateChecker(HttpClient http)
{
    private const string VersionUrl = "https://raw.githubusercontent.com/FilOne/RapidShot/main/releases/latest.txt";
    internal static readonly UpdateChecker Default = new(new HttpClient
    {
        Timeout = TimeSpan.FromSeconds(10),
        MaxResponseContentBufferSize = 256
    });
    internal static Version CurrentVersion { get; } = new(typeof(UpdateChecker).Assembly.GetName().Version!.ToString(3));

    internal async Task<Version?> FindUpdateAsync(CancellationToken cancellationToken)
    {
        var value = await http.GetStringAsync(VersionUrl, cancellationToken).ConfigureAwait(false);
        if (!Version.TryParse(value.Trim(), out var latest) || latest.Build < 0 || latest.Revision >= 0)
            throw new InvalidDataException(UiText.Current["UpdateVersionReadFailed"]);
        return latest > CurrentVersion ? latest : null;
    }

    internal static Uri InstallerUrl(Version version) =>
        new($"https://github.com/FilOne/RapidShot/raw/refs/heads/main/releases/{version}/RapidShot-Setup-{version}-x64.exe");
}
