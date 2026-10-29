using System.Net;
using System.Net.Http.Headers;
using System.Text.RegularExpressions;

namespace RapidShot;

internal sealed class GoogleLensClient : IDisposable
{
    private readonly HttpClient http;
    public GoogleLensClient(HttpClient? client = null)
    {
        http = client ?? new HttpClient(new HttpClientHandler { AllowAutoRedirect = false, AutomaticDecompression = DecompressionMethods.All }) { Timeout = TimeSpan.FromSeconds(45) };
        http.DefaultRequestHeaders.UserAgent.ParseAdd("RapidShot/1.15");
    }
    public async Task<string> SearchAsync(byte[] png, Size dimensions, CancellationToken token = default)
    {
        token.ThrowIfCancellationRequested();
        using var form = new MultipartFormDataContent();
        var image = new ByteArrayContent(png); image.Headers.ContentType = new MediaTypeHeaderValue("image/png");
        form.Add(image, "encoded_image", "RapidShot.png"); form.Add(new StringContent($"{dimensions.Width},{dimensions.Height}"), "processed_image_dimensions");
        using var request = new HttpRequestMessage(HttpMethod.Post, $"https://lens.google.com/v3/upload?ep=ccm&st={DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}") { Content = form };
        // Keep this anonymous upload session opted out of optional Google cookies.
        request.Headers.TryAddWithoutValidation("Cookie", "SOCS=CAI");
        using var response = await http.SendAsync(request, token);
        if ((int)response.StatusCode is >= 300 and < 400 && response.Headers.Location is { } redirect && SafeResult(new Uri(request.RequestUri!, redirect).AbsoluteUri) is { } location) return location;
        if (response.IsSuccessStatusCode)
        {
            var html = WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync(token)).Replace("\\u0026", "&").Replace("\\u003d", "=").Replace("\\/", "/");
            foreach (Match match in Regex.Matches(html, "https://(?:lens\\.google\\.com|www\\.google\\.com)/(?:search|v3/search)[^\\s\"'<>]+", RegexOptions.None, TimeSpan.FromSeconds(1)))
                if (SafeResult(match.Value) is { } result) return result;
        }
        throw new InvalidOperationException("Google Lens не вернул ссылку на результаты. Сервис может быть недоступен или требовать проверку в браузере. Снимок остаётся в редакторе.");
    }
    internal static string? SafeResult(string? url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme != "https" || !uri.IsDefaultPort || uri.UserInfo.Length != 0) return null;
        if (uri.Host is not ("lens.google.com" or "www.google.com" or "google.com")) return null;
        if (uri.AbsolutePath is not ("/search" or "/v3/search")) return null;
        var parameters = uri.Query.TrimStart('?').Split('&');
        return parameters.Any(p => p.StartsWith("vsrid=", StringComparison.Ordinal) || p.StartsWith("p=", StringComparison.Ordinal) || p.StartsWith("tbs=sbi", StringComparison.Ordinal)) ? uri.AbsoluteUri : null;
    }
    public void Dispose() => http.Dispose();
}
