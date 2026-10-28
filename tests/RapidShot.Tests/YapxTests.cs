using System.Net;
using RapidShot;

internal static class YapxTests
{
    public static async Task Run(Action<bool, string> check)
    {
        var result = YapxClient.ParseResponse("""{"status":"success","image_data":{"image_id":123,"hash":"ABC123","src":"https://i.yapx.ru/ABC123.png"}}""", "album");
        check(result.Url == "https://yapx.ru/v/ABC123" && result.DirectUrl != null, "Yapx: публичная и прямая ссылки");
        check(YapxClient.SafeUrl("https://evil.example/test") == null && YapxClient.SafeUrl("javascript:alert(1)") == null, "Yapx: посторонние и небезопасные URL отклоняются");
        check(YapxClient.SafeUrl("https://yapx.ru.evil.example/x") == null && YapxClient.SafeUrl("https://user:pass@yapx.ru/x") == null, "Yapx: проверяется домен и отсутствие credentials в URL");
        using var client = new YapxClient(new HttpClient(new FakeHandler(check)));
        var upload = await client.UploadAsync([137, 80, 78, 71]);
        check(upload.Url == "https://yapx.ru/v/test", "Yapx: полный протокол возвращает ссылку");
        var oversized = false;
        try { await client.UploadAsync(new byte[YapxClient.MaxBytes + 1]); } catch (InvalidOperationException) { oversized = true; }
        check(oversized, "Yapx: размер проверяется до отправки");
        using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
        var cancelledOk = false;
        try { await client.UploadAsync([1], cancelled.Token); } catch (OperationCanceledException) { cancelledOk = true; }
        check(cancelledOk, "Yapx: отмена загрузки");
        foreach (var invalid in new[] { "<html>blocked</html>", "{}", "{\"status\":\"error\"}" })
        {
            var rejected = false;
            try { YapxClient.ParseResponse(invalid, "album"); } catch (InvalidOperationException) { rejected = true; }
            check(rejected, "Yapx: ошибки и неожиданные ответы отклоняются");
        }
    }
    private sealed class FakeHandler(Action<bool, string> check) : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            var url = request.RequestUri!.AbsoluteUri;
            if (request.Method == HttpMethod.Get) return Response("<html>upload page</html>");
            if (url.EndsWith("createPublication")) return Response("{\"album_id\":123,\"hash\":\"album123\"}");
            var body = await request.Content!.ReadAsStringAsync(token);
            check(body.Contains("image/png") && body.Contains("filename="), "Yapx: multipart передаёт PNG");
            check(url == "https://yapx.ru/ajax/media_uploader/saveImage" && body.Contains("new_file[]") && body.Contains("album_id") && body.Contains("123"), "Yapx: файл отправляется в созданный альбом");
            return Response("{\"status\":\"success\",\"image_data\":{\"hash\":\"test\"}}");
        }
        private static HttpResponseMessage Response(string value) => new(HttpStatusCode.OK) { Content = new StringContent(value) };
    }
}
