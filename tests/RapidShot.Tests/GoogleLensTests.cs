using System.Drawing;
using System.Drawing.Imaging;
using System.Net;
using RapidShot;

internal static class GoogleLensTests
{
    public static async Task Run(Action<bool, string> check)
    {
        using var client = new GoogleLensClient(new HttpClient(new FakeHandler(check)));
        var url = await client.SearchAsync([1, 2, 3], new(100, 50));
        check(url == "https://www.google.com/search?vsrid=synthetic&udm=26", "Google Lens: загрузка возвращает ссылку на поиск по изображению");
        foreach (var invalid in new[] { "javascript:alert(1)", "http://www.google.com/search", "https://google.com.evil.test/search", "https://user@www.google.com/search", "https://www.google.com:444/search", "https://www.google.com/redirect?q=evil", "https://www.google.com/search?q=unrelated" })
            check(GoogleLensClient.SafeResult(invalid) == null, "Google Lens: небезопасная или посторонняя ссылка отклоняется");
        using var cancelled = new CancellationTokenSource(); cancelled.Cancel(); var rejected = false;
        try { await client.SearchAsync([1], new(1, 1), cancelled.Token); } catch (OperationCanceledException) { rejected = true; }
        check(rejected, "Google Lens: поиск отменяется");
    }
    private sealed class FakeHandler(Action<bool, string> check) : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var body = await request.Content!.ReadAsStringAsync(cancellationToken);
            check(request.Method == HttpMethod.Post && request.RequestUri!.Host == "lens.google.com" && request.RequestUri.AbsolutePath == "/v3/upload", "Google Lens: изображение отправляется непосредственно в Google");
            check(body.Contains("encoded_image") && body.Contains("processed_image_dimensions") && body.Contains("100,50") && body.Contains("image/png"), "Google Lens: форма содержит изображение и его размер");
            var response = new HttpResponseMessage(HttpStatusCode.SeeOther); response.Headers.Location = new("https://www.google.com/search?vsrid=synthetic&udm=26"); return response;
        }
    }
    public static async Task RunNetwork()
    {
        using var sample = new Bitmap(48, 48); using (var g = Graphics.FromImage(sample)) { g.Clear(Color.Teal); g.FillEllipse(Brushes.White, 10, 10, 28, 28); }
        using var bytes = new MemoryStream(); sample.Save(bytes, ImageFormat.Png);
        using var client = new GoogleLensClient(); var url = await client.SearchAsync(bytes.ToArray(), sample.Size);
        Console.WriteLine("NETWORK Google Lens OK: " + url);
    }
}
