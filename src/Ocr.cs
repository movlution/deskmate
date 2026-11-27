using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
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
        static readonly object engineGate = new object();
        static readonly object recognizeGate = new object();
        static OcrEngine cachedEngine;
        static bool engineChecked;
        static int availState = -1; // -1 unknown, 0 false, 1 true

        static OcrResult Recognize(OcrEngine engine, SoftwareBitmap bmp)
        {
            lock (recognizeGate)
            {
                try { return Converter.W(engine.RecognizeAsync(bmp)); }
                catch (Exception) { ResetEngine(); throw; }
            }
        }

        // Language list only; cached after first WinRT probe
        public static bool Available
        {
            get
            {
                int st = availState;
                if (st >= 0) return st == 1;
                try
                {
                    bool ok = OcrEngine.AvailableRecognizerLanguages.Count > 0;
                    availState = ok ? 1 : 0;
                    return ok;
                }
                catch (Exception ex) { Log.Write("ocr: " + ex.Message); return false; }
            }
        }

        // Profile languages first, else any installed; cached so repeated OCR calls skip WinRT re-init
        static OcrEngine Engine()
        {
            lock (engineGate)
            {
                if (engineChecked) return cachedEngine;
                var engine = OcrEngine.TryCreateFromUserProfileLanguages();
                if (engine == null)
                    foreach (var lang in OcrEngine.AvailableRecognizerLanguages) { engine = OcrEngine.TryCreateFromLanguage(lang); if (engine != null) break; }
                cachedEngine = engine;
                engineChecked = true;
                availState = engine != null ? 1 : 0;
                return engine;
            }
        }

        static void ResetEngine()
        {
            lock (engineGate) { cachedEngine = null; engineChecked = false; availState = -1; }
        }

        // Off the UI thread; the engine goes with the call
        public static string Read(string path)
        {
            var file = Converter.W(StorageFile.GetFileFromPathAsync(Path.GetFullPath(path)));
            using (var s = Converter.W(file.OpenAsync(FileAccessMode.Read))) return Read(s);
        }

        // Direct LockBits -> SoftwareBitmap (Bgra8, Alpha=Ignore): no BMP/PNG encode/decode or temp file
        public static string Read(Bitmap bitmap)
        {
            if (bitmap == null) return null;
            var engine = Engine();
            if (engine == null) return null;
            double width;
            using (var swBmp = ToSoftwareBitmap(bitmap, out width))
            {
                if (swBmp == null) return null;
                var result = Recognize(engine, swBmp);
                var sb = new StringBuilder();
                foreach (var line in result.Lines) sb.AppendLine(line.Text);
                return sb.ToString().TrimEnd();
            }
        }

        static SoftwareBitmap ToSoftwareBitmap(Bitmap bitmap, out double width)
        {
            width = 1;
            int w = bitmap.Width, h = bitmap.Height;
            if (w < 4 || h < 4) return null;
            int max = (int)Math.Min(2560u, OcrEngine.MaxImageDimension);
            Bitmap scaled = null;
            Bitmap src = bitmap;
            try
            {
                if (w > max || h > max)
                {
                    double k = (double)max / Math.Max(w, h);
                    int sw = Math.Max(4, (int)Math.Round(w * k));
                    int sh = Math.Max(4, (int)Math.Round(h * k));
                    scaled = new Bitmap(sw, sh, PixelFormat.Format32bppRgb);
                    using (var g = Graphics.FromImage(scaled))
                    {
                        g.InterpolationMode = InterpolationMode.HighQualityBilinear;
                        g.PixelOffsetMode = PixelOffsetMode.Half;
                        g.DrawImage(bitmap, new Rectangle(0, 0, sw, sh), new Rectangle(0, 0, w, h), GraphicsUnit.Pixel);
                    }
                    src = scaled;
                    w = sw;
                    h = sh;
                }
                width = Math.Max(1, w);
                var data = src.LockBits(new Rectangle(0, 0, w, h), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
                try
                {
                    int rowBytes = w * 4;
                    byte[] raw = new byte[rowBytes * h];
                    if (data.Stride == rowBytes)
                    {
                        Marshal.Copy(data.Scan0, raw, 0, raw.Length);
                    }
                    else
                    {
                        long baseAddr = data.Scan0.ToInt64();
                        for (int y = 0; y < h; y++)
                            Marshal.Copy(new IntPtr(baseAddr + (long)y * data.Stride), raw, y * rowBytes, rowBytes);
                    }
                    IBuffer buf;
                    using (var dw = new DataWriter())
                    {
                        dw.WriteBytes(raw);
                        buf = dw.DetachBuffer();
                    }
                    return SoftwareBitmap.CreateCopyFromBuffer(buf, BitmapPixelFormat.Bgra8, w, h, BitmapAlphaMode.Ignore);
                }
                finally { src.UnlockBits(data); }
            }
            finally { if (scaled != null) scaled.Dispose(); }
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
                var result = Recognize(engine, bmp);
                var sb = new StringBuilder();
                foreach (var line in result.Lines) sb.AppendLine(line.Text);
                return sb.ToString().TrimEnd();
            }
        }

        // Lines with where they sit across the picture: "[62-95] text", 0 = left edge, 100 = right edge.
        // Chat apps put the user's own bubbles toward the right, so position carries who said it.
        public static string ReadPlaced(string path)
        {
            var file = Converter.W(StorageFile.GetFileFromPathAsync(Path.GetFullPath(path)));
            using (var s = Converter.W(file.OpenAsync(FileAccessMode.Read))) return ReadPlaced(s);
        }

        public static string ReadPlaced(Bitmap bitmap)
        {
            if (bitmap == null) return null;
            var engine = Engine();
            if (engine == null) return null;
            double width;
            using (var swBmp = ToSoftwareBitmap(bitmap, out width))
            {
                if (swBmp == null) return null;
                return FormatPlaced(Recognize(engine, swBmp), width);
            }
        }

        public static string ReadPlaced(IRandomAccessStream s)
        {
            var engine = Engine();
            if (engine == null) return null;
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
                return FormatPlaced(Recognize(engine, bmp), width);
            }
        }

        struct OcrPlacedLine { public double Top, X0, X1; public string Text; }

        static string FormatPlaced(OcrResult result, double width)
        {
            var items = new System.Collections.Generic.List<OcrPlacedLine>(result.Lines.Count);
            foreach (var line in result.Lines)
            {
                double x0 = double.MaxValue, x1 = 0, y0 = double.MaxValue;
                foreach (var word in line.Words)
                {
                    var br = word.BoundingRect;
                    if (br.X < x0) x0 = br.X;
                    if (br.X + br.Width > x1) x1 = br.X + br.Width;
                    if (br.Y < y0) y0 = br.Y;
                }
                if (x0 == double.MaxValue) continue;
                items.Add(new OcrPlacedLine { Top = y0, X0 = x0, X1 = x1, Text = line.Text });
            }
            // Sort top-to-bottom (row-banded) then left-to-right for stable reading order in chats and multi-column layouts
            items.Sort((a, b) =>
            {
                int ra = (int)(a.Top / 8.0), rb = (int)(b.Top / 8.0);
                return ra != rb ? ra.CompareTo(rb) : a.X0.CompareTo(b.X0);
            });
            var sb = new StringBuilder();
            for (int i = 0; i < items.Count; i++)
            {
                var it = items[i];
                int p0 = Math.Max(0, Math.Min(100, (int)(it.X0 * 100 / width)));
                int p1 = Math.Max(0, Math.Min(100, (int)(it.X1 * 100 / width)));
                sb.Append('[').Append(p0).Append('-').Append(p1).Append("] ").AppendLine(it.Text);
            }
            return sb.ToString().TrimEnd();
        }
    }
}
