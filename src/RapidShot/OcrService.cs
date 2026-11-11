using System.ComponentModel;
using System.Diagnostics;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Text;

namespace RapidShot;

internal static class OcrService
{
    private const string MissingRuntime = "Для распознавания текста установите Microsoft Visual C++ Redistributable (x64).";

    internal static async Task<string> RecognizeAsync(Bitmap image, CancellationToken cancellationToken)
    {
        var directory = Path.Combine(AppContext.BaseDirectory, "ocr");
        var executable = Path.Combine(directory, "tesseract.exe");
        if (!File.Exists(executable))
            throw new FileNotFoundException("Не найден модуль OCR. Переустановите RapidShot или восстановите папку ocr рядом с программой.");
        foreach (var library in new[] { "msvcp140.dll", "vcruntime140.dll", "vcruntime140_1.dll" })
        {
            if (!File.Exists(Path.Combine(Environment.SystemDirectory, library)) && !File.Exists(Path.Combine(directory, library)))
                throw new InvalidOperationException(MissingRuntime);
        }

        using var png = new MemoryStream();
        var scale = (long)image.Width * image.Height <= 1_000_000 && Math.Max(image.Width, image.Height) <= 4000 ? 2 : 1;
        using (var prepared = new Bitmap(image.Width * scale + 24, image.Height * scale + 24, PixelFormat.Format24bppRgb))
        {
            using (var graphics = Graphics.FromImage(prepared))
            {
                graphics.Clear(image.GetPixel(0, 0));
                graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
                graphics.DrawImage(image, new Rectangle(12, 12, image.Width * scale, image.Height * scale));
            }
            prepared.Save(png, ImageFormat.Png);
        }
        png.Position = 0;
        cancellationToken.ThrowIfCancellationRequested();

        var startInfo = new ProcessStartInfo(executable)
        {
            WorkingDirectory = directory,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardInputEncoding = new UTF8Encoding(false),
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };
        foreach (var argument in new[] { "stdin", "stdout", "--tessdata-dir", "tessdata", "-l", "rus+eng", "--dpi", scale == 2 ? "300" : "150" })
            startInfo.ArgumentList.Add(argument);
        startInfo.Environment["OMP_THREAD_LIMIT"] = "2";

        using var process = Process.Start(startInfo) ?? throw new InvalidOperationException("Не удалось запустить модуль OCR.");
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(30));
        using var stop = timeout.Token.Register(() =>
        {
            try { process.Kill(); }
            catch (Exception ex) when (ex is InvalidOperationException or Win32Exception) { }
        });

        using var stdout = process.StandardOutput;
        using var stderr = process.StandardError;
        var output = stdout.ReadToEndAsync();
        var errors = stderr.ReadToEndAsync();
        try
        {
            IOException? inputError = null;
            try
            {
                using var input = process.StandardInput;
                await png.CopyToAsync(input.BaseStream, timeout.Token).ConfigureAwait(false);
            }
            catch (IOException ex) { inputError = ex; }

            await process.WaitForExitAsync(timeout.Token).ConfigureAwait(false);
            timeout.Token.ThrowIfCancellationRequested();
            var diagnostics = await errors.ConfigureAwait(false);
            if (process.ExitCode is unchecked((int)0xC0000135) or unchecked((int)0xC000007B))
                throw new InvalidOperationException(MissingRuntime);
            if (process.ExitCode != 0)
                throw new InvalidOperationException(string.IsNullOrWhiteSpace(diagnostics)
                    ? "Модуль OCR завершился с ошибкой."
                    : diagnostics.Trim());
            if (inputError != null)
                throw new IOException("Не удалось передать снимок в модуль OCR.", inputError);
            return (await output.ConfigureAwait(false)).Trim().ReplaceLineEndings();
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new TimeoutException("Распознавание заняло больше 30 секунд. Попробуйте выбрать меньшую область.");
        }
        finally
        {
            if (!process.HasExited) timeout.Cancel();
        }
    }
}
