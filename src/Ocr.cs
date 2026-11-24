using System;
using System.IO;
using System.Text;
using Windows.Graphics.Imaging;
using Windows.Media.Ocr;
using Windows.Storage;
using Windows.Storage.Streams;

namespace Deskmate
{
    // Windows OCR on this machine; nothing leaves it
    static class Ocr
    {
        // Language list only; no model loaded
        public static bool Available
        {
            get
            {
                try { return OcrEngine.AvailableRecognizerLanguages.Count > 0; }
                catch (Exception ex) { Log.Write("ocr: " + ex.Message); return false; }
            }
        }

        // Profile languages first, else any installed
        static OcrEngine Engine()
        {
            var engine = OcrEngine.TryCreateFromUserProfileLanguages();
            if (engine == null)
                foreach (var lang in OcrEngine.AvailableRecognizerLanguages) { engine = OcrEngine.TryCreateFromLanguage(lang); if (engine != null) break; }
            return engine;
        }

        // Off the UI thread; the engine goes with the call
        public static string Read(string path)
        {
            var file = Converter.W(StorageFile.GetFileFromPathAsync(Path.GetFullPath(path)));
            using (var s = Converter.W(file.OpenAsync(FileAccessMode.Read))) return Read(s);
        }

        // Any encoded picture: photos, rendered PDF pages
        public static string Read(IRandomAccessStream s)
        {
            var engine = Engine();
            if (engine == null) return null;
            var dec = Converter.W(BitmapDecoder.CreateAsync(s));
            uint w = dec.PixelWidth, h = dec.PixelHeight, max = OcrEngine.MaxImageDimension;
            var t = new BitmapTransform();
            // Oversized images shrink to fit the engine
            if (w > max || h > max)
            {
                double k = (double)max / Math.Max(w, h);
                t.ScaledWidth = (uint)(w * k);
                t.ScaledHeight = (uint)(h * k);
            }
            using (var bmp = Converter.W(dec.GetSoftwareBitmapAsync(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied, t, ExifOrientationMode.RespectExifOrientation, ColorManagementMode.DoNotColorManage)))
            {
                var result = Converter.W(engine.RecognizeAsync(bmp));
                var sb = new StringBuilder();
                foreach (var line in result.Lines) sb.AppendLine(line.Text);
                return sb.ToString().TrimEnd();
            }
        }

        // Lines with where they sit across the picture: "[62-95] text", 0 = left edge, 100 = right edge.
        // Chat apps put the user's own bubbles toward the right, so position carries who said it.
        public static string ReadPlaced(string path)
        {
            var engine = Engine();
            if (engine == null) return null;
            var file = Converter.W(StorageFile.GetFileFromPathAsync(Path.GetFullPath(path)));
            using (var s = Converter.W(file.OpenAsync(FileAccessMode.Read)))
            {
                var dec = Converter.W(BitmapDecoder.CreateAsync(s));
                uint w = dec.PixelWidth, h = dec.PixelHeight, max = OcrEngine.MaxImageDimension;
                var t = new BitmapTransform();
                double k = 1;
                if (w > max || h > max)
                {
                    k = (double)max / Math.Max(w, h);
                    t.ScaledWidth = (uint)(w * k);
                    t.ScaledHeight = (uint)(h * k);
                }
                double width = Math.Max(1, w * k);
                using (var bmp = Converter.W(dec.GetSoftwareBitmapAsync(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied, t, ExifOrientationMode.RespectExifOrientation, ColorManagementMode.DoNotColorManage)))
                {
                    var result = Converter.W(engine.RecognizeAsync(bmp));
                    var sb = new StringBuilder();
                    foreach (var line in result.Lines)
                    {
                        double x0 = double.MaxValue, x1 = 0;
                        foreach (var word in line.Words) { x0 = Math.Min(x0, word.BoundingRect.X); x1 = Math.Max(x1, word.BoundingRect.X + word.BoundingRect.Width); }
                        if (x0 == double.MaxValue) continue;
                        sb.Append('[').Append((int)(x0 * 100 / width)).Append('-').Append((int)(x1 * 100 / width)).Append("] ").AppendLine(line.Text);
                    }
                    return sb.ToString().TrimEnd();
                }
            }
        }
    }
}
