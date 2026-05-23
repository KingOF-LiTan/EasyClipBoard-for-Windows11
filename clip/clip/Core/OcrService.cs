using System;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Threading.Tasks;
using Windows.Foundation;
using Windows.Graphics.Imaging;
using Windows.Media.Ocr;
using Windows.Storage.Streams;

namespace clip.Core;

/// <summary>
/// Wraps Windows.Media.Ocr.
/// Requires the corresponding Windows OCR language pack to be installed
/// (Settings → Time &amp; Language → Language → Options → Handwriting).
/// </summary>
public static class OcrService
{
    private static OcrEngine? _engine;

    // Windows OCR works best at 200+ DPI. Upscale images smaller than this.
    private const int MinDimension = 800;

    private static OcrEngine? GetEngine()
    {
        if (_engine != null) return _engine;

        var available = OcrEngine.AvailableRecognizerLanguages;
        System.Diagnostics.Debug.WriteLine($"[OCR] Available language packs: {available.Count}");
        foreach (var l in available)
            System.Diagnostics.Debug.WriteLine($"[OCR]   {l.DisplayName}");

        _engine = OcrEngine.TryCreateFromUserProfileLanguages();

        if (_engine == null && available.Count > 0)
        {
            // Fallback: first available language (might differ from UI language)
            _engine = OcrEngine.TryCreateFromLanguage(available[0]);
        }

        if (_engine != null)
            System.Diagnostics.Debug.WriteLine($"[OCR] Using: {_engine.RecognizerLanguage?.DisplayName}");
        else
            System.Diagnostics.Debug.WriteLine("[OCR] No engine — install a Windows OCR language pack");

        return _engine;
    }

    /// <summary>
    /// Extracts text from PNG bytes. Returns null when no text found or on error.
    /// </summary>
    public static async Task<string?> ExtractTextAsync(byte[] pngBytes)
    {
        try
        {
            var engine = GetEngine();
            if (engine == null) return null;

            SoftwareBitmap bitmap;
            using (var stream = new InMemoryRandomAccessStream())
            {
                await stream.WriteAsync(pngBytes.AsBuffer());
                stream.Seek(0);

                var decoder = await BitmapDecoder.CreateAsync(stream);

                uint w = decoder.PixelWidth;
                uint h = decoder.PixelHeight;

                // Upscale small images: Windows OCR loses accuracy on low-res captures
                if (w < MinDimension || h < MinDimension)
                {
                    double scale = Math.Max((double)MinDimension / w, (double)MinDimension / h);
                    scale = Math.Min(scale, 4.0); // Cap at 4× to avoid huge memory usage

                    var transform = new BitmapTransform
                    {
                        ScaledWidth  = (uint)Math.Round(w * scale),
                        ScaledHeight = (uint)Math.Round(h * scale),
                        InterpolationMode = BitmapInterpolationMode.Fant // High-quality
                    };
                    bitmap = await decoder.GetSoftwareBitmapAsync(
                        BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied,
                        transform, ExifOrientationMode.IgnoreExifOrientation,
                        ColorManagementMode.DoNotColorManage);
                }
                else
                {
                    bitmap = await decoder.GetSoftwareBitmapAsync(
                        BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied);
                }
            }

            var result = await engine.RecognizeAsync(bitmap);
            bitmap.Dispose();

            var text = result.Text?.Trim();
            System.Diagnostics.Debug.WriteLine($"[OCR] Result: {text?.Length ?? 0} chars");
            return string.IsNullOrWhiteSpace(text) ? null : text;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[OCR] Error: {ex.Message}");
            return null;
        }
    }
}
