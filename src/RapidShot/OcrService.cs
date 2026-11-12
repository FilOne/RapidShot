using System.ComponentModel;
using System.Diagnostics;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Globalization;
using System.Text;

namespace RapidShot;

internal sealed record OcrWord(string Text, RectangleF Bounds, bool StartsLine);

internal sealed record OcrResult(OcrWord[] Words)
{
    internal string GetText(int first, int last)
    {
        var text = new StringBuilder();
        var startsLine = false;
        for (var i = first; i <= last; i++)
        {
            var word = Words[i];
            startsLine |= word.StartsLine;
            if (word.Text.Length == 0) continue;
            if (text.Length != 0)
            {
                if (startsLine) text.AppendLine();
                else text.Append(' ');
            }
            text.Append(word.Text);
            startsLine = false;
        }
        return text.ToString();
    }
}

internal static class OcrService
{
    private const int ImagePadding = 12;

    internal static async Task<OcrResult> RecognizeAsync(Bitmap image, CancellationToken cancellationToken)
    {
        var directory = Path.Combine(AppContext.BaseDirectory, "ocr");
        var executable = Path.Combine(directory, "tesseract.exe");
        if (!File.Exists(executable))
            throw new FileNotFoundException("Не найден модуль OCR. Переустановите RapidShot или восстановите папку ocr рядом с программой.");
        using var bitmap = new MemoryStream();
        var scale = (long)image.Width * image.Height <= 1_000_000 && Math.Max(image.Width, image.Height) <= 4000 ? 2 : 1;
        using (var prepared = new Bitmap(image.Width * scale + ImagePadding * 2, image.Height * scale + ImagePadding * 2, PixelFormat.Format24bppRgb))
        {
            using (var graphics = Graphics.FromImage(prepared))
            {
                graphics.Clear(image.GetPixel(0, 0));
                graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
                graphics.DrawImage(image, new Rectangle(ImagePadding, ImagePadding, image.Width * scale, image.Height * scale));
            }
            prepared.Save(bitmap, ImageFormat.Bmp);
        }
        bitmap.Position = 0;
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
        foreach (var argument in new[] { "stdin", "stdout", "--tessdata-dir", "tessdata", "-l", "rus+eng", "--oem", "1", "--dpi", scale == 2 ? "300" : "150", "-c", "tessedit_create_tsv=1" })
            startInfo.ArgumentList.Add(argument);

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
                await bitmap.CopyToAsync(input.BaseStream, timeout.Token).ConfigureAwait(false);
            }
            catch (IOException ex) { inputError = ex; }

            await process.WaitForExitAsync(timeout.Token).ConfigureAwait(false);
            timeout.Token.ThrowIfCancellationRequested();
            var diagnostics = await errors.ConfigureAwait(false);
            if (process.ExitCode != 0)
                throw new InvalidOperationException(string.IsNullOrWhiteSpace(diagnostics)
                    ? "Модуль OCR завершился с ошибкой."
                    : diagnostics.Trim());
            if (inputError != null)
                throw new IOException("Не удалось передать снимок в модуль OCR.", inputError);
            return ParseResult(await output.ConfigureAwait(false), image.Size, scale);
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

    private static OcrResult ParseResult(string tsv, Size imageSize, int scale)
    {
        var words = new List<OcrWord>();
        var imageBounds = new RectangleF(PointF.Empty, imageSize);
        (string Page, string Block, string Paragraph, string Line)? previousLine = null;
        using var reader = new StringReader(tsv);
        while (reader.ReadLine() is { } row)
        {
            var fields = row.Split('\t', 12);
            if (fields.Length != 12 || fields[0] != "5") continue;
            var word = fields[11].Trim();
            if (word.Length == 0) continue;

            var line = (fields[1], fields[2], fields[3], fields[4]);
            var bounds = RectangleF.Empty;
            if (int.TryParse(fields[6], CultureInfo.InvariantCulture, out var x)
                && int.TryParse(fields[7], CultureInfo.InvariantCulture, out var y)
                && int.TryParse(fields[8], CultureInfo.InvariantCulture, out var width)
                && int.TryParse(fields[9], CultureInfo.InvariantCulture, out var height))
            {
                bounds = new RectangleF((x - ImagePadding) / (float)scale, (y - ImagePadding) / (float)scale,
                    width / (float)scale, height / (float)scale);
                bounds.Intersect(imageBounds);
            }
            words.Add(new OcrWord(word, bounds, previousLine != line));
            previousLine = line;
        }
        return new OcrResult(words.ToArray());
    }
}
