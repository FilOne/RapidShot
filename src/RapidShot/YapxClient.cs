using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace RapidShot;

public sealed record UploadResult(string Url, string? DirectUrl);

public sealed class YapxClient : IDisposable
{
    public const int MaxBytes = 3 * 1024 * 1024;
    private readonly HttpClient http;
    public YapxClient(HttpClient? client = null)
    {
        http = client ?? new HttpClient(new HttpClientHandler { CookieContainer = new CookieContainer(), AutomaticDecompression = DecompressionMethods.All }) { Timeout = TimeSpan.FromSeconds(60) };
        http.DefaultRequestHeaders.UserAgent.ParseAdd("RapidShot/1.9");
    }
    public async Task<UploadResult> UploadAsync(byte[] png, CancellationToken cancellation = default)
    {
        if (png.Length > MaxBytes) throw new InvalidOperationException("Yapx: файл превышает гостевой лимит 3 МБ. Обрежьте снимок или сохраните его на диск.");
        cancellation.ThrowIfCancellationRequested();
        await Send(HttpMethod.Get, "https://yapx.ru/upload", null, cancellation);
        using var create = new FormUrlEncodedContent([]);
        var publication = await Send(HttpMethod.Post, "https://yapx.ru/ajax/media_uploader/createPublication", create, cancellation);
        using var json = ReadJson(publication);
        var albumId = Field(json.RootElement, "album_id");
        var albumHash = Field(json.RootElement, "hash");
        if (string.IsNullOrEmpty(albumId) || !ValidHash(albumHash)) throw Unexpected();
        using var form = new MultipartFormDataContent();
        var image = new ByteArrayContent(png); image.Headers.ContentType = new MediaTypeHeaderValue("image/png");
        form.Add(image, "new_file[]", $"RapidShot_{DateTime.Now:yyyy-MM-dd_HH-mm-ss}.png");
        form.Add(new StringContent(albumId), "album_id"); form.Add(new StringContent(Guid.NewGuid().ToString("N")[..10]), "image_id");
        var response = await Send(HttpMethod.Post, "https://yapx.ru/ajax/media_uploader/saveImage", form, cancellation);
        return ParseResponse(response, albumHash!);
    }
    private async Task<string> Send(HttpMethod method, string url, HttpContent? content, CancellationToken cancellation)
    {
        using var request = new HttpRequestMessage(method, url) { Content = content };
        request.Headers.Referrer = new Uri("https://yapx.ru/upload");
        if (method == HttpMethod.Post) { request.Headers.Add("X-Requested-With", "XMLHttpRequest"); request.Headers.Accept.Add(new("application/json")); }
        using var response = await http.SendAsync(request, cancellation);
        if (response.StatusCode == HttpStatusCode.TooManyRequests) throw new InvalidOperationException("Yapx ограничил частоту загрузок. Повторите позже.");
        if (!response.IsSuccessStatusCode) throw new InvalidOperationException($"Yapx вернул ошибку HTTP {(int)response.StatusCode}. Снимок остаётся в редакторе.");
        return await response.Content.ReadAsStringAsync(cancellation);
    }
    private static JsonDocument ReadJson(string response)
    {
        try { return JsonDocument.Parse(response); }
        catch (JsonException) { throw Unexpected(); }
    }
    private static InvalidOperationException Unexpected() => new("Yapx вернул неожиданный ответ. Сервис мог изменить загрузчик, временно недоступен или требует проверку в браузере.");
    private static string? Field(JsonElement element, string name) => element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var field) && field.ValueKind is JsonValueKind.String or JsonValueKind.Number ? field.ToString() : null;
    private static bool ValidHash(string? value) => value != null && Regex.IsMatch(value, @"\A[A-Za-z0-9_-]{1,100}\z", RegexOptions.None, TimeSpan.FromSeconds(1));
    public static string? SafeUrl(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        if (!Uri.TryCreate(new Uri("https://yapx.ru/"), WebUtility.HtmlDecode(value), out var uri) || uri.Scheme != "https" || uri.UserInfo.Length > 0 || !uri.IsDefaultPort) return null;
        return uri.Host.Equals("yapx.ru", StringComparison.OrdinalIgnoreCase) || uri.Host.EndsWith(".yapx.ru", StringComparison.OrdinalIgnoreCase) ? uri.AbsoluteUri : null;
    }
    public static UploadResult ParseResponse(string response, string albumHash)
    {
        using var json = ReadJson(response); var root = json.RootElement;
        if (root.ValueKind != JsonValueKind.Object || Field(root, "status") == "error" || !root.TryGetProperty("image_data", out var image) || !ValidHash(Field(image, "hash")) || !ValidHash(albumHash)) throw Unexpected();
        var hash = Field(image, "hash");
        return new($"https://yapx.ru/v/{hash}", SafeUrl(Field(image, "src")) ?? SafeUrl(Field(image, "image_url")));
    }
    public void Dispose() => http.Dispose();
}
