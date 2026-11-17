using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using Windows.Data.Pdf;
using IAsyncAction = Windows.Foundation.IAsyncAction;
using AsyncStatus = Windows.Foundation.AsyncStatus;
using Windows.Graphics.Imaging;
using Windows.Storage;
using Windows.Storage.Streams;
using WinBitmapDecoder = Windows.Graphics.Imaging.BitmapDecoder;

namespace Deskmate
{
    static partial class Converter
    {
        // Images: WIC decode keeps EXIF rotation

        static Bitmap Decode(string path)
        {
            try
            {
                var file = W(StorageFile.GetFileFromPathAsync(Path.GetFullPath(path)));
                using (var s = W(file.OpenAsync(FileAccessMode.Read)))
                {
                    var dec = W(WinBitmapDecoder.CreateAsync(s));
                    var px = W(dec.GetPixelDataAsync(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied, new BitmapTransform(),
                        ExifOrientationMode.RespectExifOrientation, ColorManagementMode.ColorManageToSRgb));
                    byte[] data = px.DetachPixelData();
                    int w = (int)dec.OrientedPixelWidth, h = (int)dec.OrientedPixelHeight;
                    var bmp = new Bitmap(w, h, PixelFormat.Format32bppPArgb);
                    var bd = bmp.LockBits(new Rectangle(0, 0, w, h), ImageLockMode.WriteOnly, PixelFormat.Format32bppPArgb);
                    try { for (int y = 0; y < h; y++) Marshal.Copy(data, y * w * 4, new IntPtr(bd.Scan0.ToInt64() + (long)y * bd.Stride), w * 4); }
                    finally { bmp.UnlockBits(bd); }
                    return bmp;
                }
            }
            catch (Exception ex)
            {
                Log.Write("decode wic: " + ex.Message);
                // GDI+ as a fallback, no file lock
                using (var ms = new MemoryStream(File.ReadAllBytes(path)))
                using (var img = Image.FromStream(ms))
                    return new Bitmap(img);
            }
        }

        // JPG and BMP have no alpha: white under it
        static Bitmap Flatten(Bitmap src)
        {
            var b = new Bitmap(src.Width, src.Height, PixelFormat.Format24bppRgb);
            using (var g = Graphics.FromImage(b))
            {
                g.Clear(Color.White);
                g.DrawImage(src, 0, 0, src.Width, src.Height);
            }
            return b;
        }

        static ImageCodecInfo Codec(string mime)
        {
            foreach (var c in ImageCodecInfo.GetImageEncoders()) if (c.MimeType == mime) return c;
            return null;
        }

        static byte[] Jpeg(Bitmap src, long quality)
        {
            using (var flat = Flatten(src))
            using (var ms = new MemoryStream())
            using (var ep = new EncoderParameters(1))
            {
                ep.Param[0] = new EncoderParameter(System.Drawing.Imaging.Encoder.Quality, quality);
                flat.Save(ms, Codec("image/jpeg"), ep);
                return ms.ToArray();
            }
        }

        static void ImageTo(string src, string outPath, string id)
        {
            if (id == "webp") { ImageViaFfmpeg(src, outPath); return; }
            using (var bmp = Decode(src))
            {
                switch (id)
                {
                    case "jpg": File.WriteAllBytes(outPath, Jpeg(bmp, 92)); break;
                    case "png": bmp.Save(outPath, ImageFormat.Png); break;
                    case "bmp": using (var flat = Flatten(bmp)) flat.Save(outPath, ImageFormat.Bmp); break;
                    case "tiff": bmp.Save(outPath, ImageFormat.Tiff); break;
                    case "gif": bmp.Save(outPath, ImageFormat.Gif); break;
                    case "ico": WriteIco(bmp, outPath); break;
                    default: throw new NotSupportedException(id);
                }
            }
        }

        // WIC has no WebP encoder
        static void ImageViaFfmpeg(string src, string outPath)
        {
            string tmp = Path.Combine(Path.GetTempPath(), "deskmate-" + Guid.NewGuid().ToString("N") + ".png");
            try
            {
                using (var bmp = Decode(src)) bmp.Save(tmp, ImageFormat.Png);
                Ffmpeg(null, tmp, outPath, "-c:v libwebp -quality 90");
            }
            finally { TryDelete(tmp); }
        }

        // PNG-in-ICO, Vista and later
        static void WriteIco(Bitmap src, string outPath)
        {
            var sizes = new List<int>();
            int big = Math.Max(src.Width, src.Height);
            foreach (int s in new[] { 256, 64, 48, 32, 16 }) if (s <= big || s == 16) sizes.Add(s);
            var images = new List<byte[]>();
            foreach (int s in sizes)
                using (var sq = new Bitmap(s, s, PixelFormat.Format32bppArgb))
                {
                    using (var g = Graphics.FromImage(sq))
                    {
                        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                        g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                        float k = (float)s / big;
                        float w = src.Width * k, h = src.Height * k;
                        g.DrawImage(src, new RectangleF((s - w) / 2, (s - h) / 2, w, h));
                    }
                    using (var ms = new MemoryStream()) { sq.Save(ms, ImageFormat.Png); images.Add(ms.ToArray()); }
                }
            using (var fs = File.Create(outPath))
            using (var bw = new BinaryWriter(fs))
            {
                bw.Write((short)0); bw.Write((short)1); bw.Write((short)sizes.Count);
                int offset = 6 + 16 * sizes.Count;
                for (int i = 0; i < sizes.Count; i++)
                {
                    byte dim = (byte)(sizes[i] >= 256 ? 0 : sizes[i]);
                    bw.Write(dim); bw.Write(dim); bw.Write((byte)0); bw.Write((byte)0);
                    bw.Write((short)1); bw.Write((short)32);
                    bw.Write(images[i].Length); bw.Write(offset);
                    offset += images[i].Length;
                }
                foreach (var img in images) bw.Write(img);
            }
        }

        // PDF: one A4 page per image, JPEG inside
        static void ImagesToPdf(ConvJob job, List<string> files, string outPath, bool track)
        {
            const float PageW = 595.28f, PageH = 841.89f, Margin = 28f;
            var pages = new List<byte[]>();
            var dims = new List<Size>();
            for (int i = 0; i < files.Count && !job.Cancelled; i++)
            {
                if (track) { job.Index = i; job.Ping(); }
                using (var bmp = Decode(files[i]))
                {
                    pages.Add(Jpeg(bmp, 88));
                    dims.Add(bmp.Size);
                }
            }
            if (job.Cancelled) return;
            var inv = CultureInfo.InvariantCulture;
            using (var fs = File.Create(outPath))
            {
                var offsets = new List<long>();
                Action<string> put = s => { var b = Encoding.ASCII.GetBytes(s); fs.Write(b, 0, b.Length); };
                put("%PDF-1.4\n%âã\n");
                int n = pages.Count, objs = 2 + n * 3;
                var kids = new StringBuilder();
                for (int i = 0; i < n; i++) kids.Append(3 + i * 3).Append(" 0 R ");
                offsets.Add(fs.Position); put("1 0 obj\n<< /Type /Catalog /Pages 2 0 R >>\nendobj\n");
                offsets.Add(fs.Position); put("2 0 obj\n<< /Type /Pages /Kids [" + kids + "] /Count " + n + " >>\nendobj\n");
                for (int i = 0; i < n; i++)
                {
                    int pg = 3 + i * 3, ct = pg + 1, im = pg + 2;
                    var d = dims[i];
                    bool wide = d.Width > d.Height;
                    float pw = wide ? PageH : PageW, ph = wide ? PageW : PageH;
                    // 96 dpi at most, never blown up
                    float k = Math.Min(0.75f, Math.Min((pw - 2 * Margin) / d.Width, (ph - 2 * Margin) / d.Height));
                    float dw = d.Width * k, dh = d.Height * k;
                    string draw = string.Format(inv, "q {0:0.##} 0 0 {1:0.##} {2:0.##} {3:0.##} cm /Im0 Do Q", dw, dh, (pw - dw) / 2, (ph - dh) / 2);
                    offsets.Add(fs.Position);
                    put(string.Format(inv, "{0} 0 obj\n<< /Type /Page /Parent 2 0 R /MediaBox [0 0 {1:0.##} {2:0.##}] /Resources << /XObject << /Im0 {3} 0 R >> >> /Contents {4} 0 R >>\nendobj\n", pg, pw, ph, im, ct));
                    offsets.Add(fs.Position);
                    put(ct + " 0 obj\n<< /Length " + draw.Length + " >>\nstream\n" + draw + "\nendstream\nendobj\n");
                    offsets.Add(fs.Position);
                    put(im + " 0 obj\n<< /Type /XObject /Subtype /Image /Width " + d.Width + " /Height " + d.Height + " /ColorSpace /DeviceRGB /BitsPerComponent 8 /Filter /DCTDecode /Length " + pages[i].Length + " >>\nstream\n");
                    fs.Write(pages[i], 0, pages[i].Length);
                    put("\nendstream\nendobj\n");
                }
                long xref = fs.Position;
                var sb = new StringBuilder();
                sb.Append("xref\n0 ").Append(objs + 1).Append("\n0000000000 65535 f \n");
                foreach (var o in offsets) sb.Append(o.ToString("D10")).Append(" 00000 n \n");
                sb.Append("trailer\n<< /Size ").Append(objs + 1).Append(" /Root 1 0 R >>\nstartxref\n").Append(xref).Append("\n%%EOF\n");
                put(sb.ToString());
            }
        }

        // From memory: no file handle left behind
        static PdfDocument LoadPdf(string path, out InMemoryRandomAccessStream ms)
        {
            ms = new InMemoryRandomAccessStream();
            using (var w = new DataWriter(ms.GetOutputStreamAt(0)))
            {
                w.WriteBytes(File.ReadAllBytes(path));
                W(w.StoreAsync());
                W(w.FlushAsync());
                w.DetachStream();
            }
            return W(PdfDocument.LoadFromStreamAsync(ms));
        }

        // Windows' PDF reader counts; optionally draws page one
        static uint PdfPages(string path, bool render)
        {
            InMemoryRandomAccessStream ms;
            var doc = LoadPdf(path, out ms);
            using (ms)
            {
                if (render && doc.PageCount > 0)
                    using (var page = doc.GetPage(0))
                    using (var img = new InMemoryRandomAccessStream())
                        W(page.RenderToStreamAsync(img, new PdfPageRenderOptions { DestinationWidth = 160 }));
                return doc.PageCount;
            }
        }

        // A rewritten PDF must open, page for page
        static void Verify(string src, string made)
        {
            if (PdfPages(made, true) != PdfPages(src, false)) throw new InvalidDataException(Lang.T("Hasilnya tidak lolos pemeriksaan"));
        }

        // PDF pages: Windows renders them
        static void PdfToImages(ConvJob job, string src, string dir, string name, string id)
        {
            InMemoryRandomAccessStream held;
            var doc = LoadPdf(src, out held);
            using (held)
            {
                PdfPagesOut(job, doc, dir, name, id);
            }
        }

        static void PdfPagesOut(ConvJob job, PdfDocument doc, string dir, string name, string id)
        {
            if (doc.IsPasswordProtected) throw new InvalidOperationException(Lang.T("PDF dikunci kata sandi"));
            uint count = doc.PageCount;
            string outDir = count > 1 ? UniqueDir(dir, name) : dir;
            if (count > 1) Directory.CreateDirectory(outDir);
            var made = new List<string>();
            try
            {
                for (uint i = 0; i < count && !job.Cancelled; i++)
                {
                    job.Part = (float)i / count;
                    job.Ping();
                    string outPath = count > 1 ? Path.Combine(outDir, name + "-" + (i + 1) + "." + id) : UniqueFile(dir, name, "." + id);
                    using (var page = doc.GetPage(i))
                    using (var ms = new InMemoryRandomAccessStream())
                    {
                        // 192 dpi: sharp text, sane size
                        var opt = new PdfPageRenderOptions { DestinationWidth = (uint)Math.Max(1, page.Size.Width * 2) };
                        if (id == "jpg") opt.BitmapEncoderId = BitmapEncoder.JpegEncoderId;
                        W(page.RenderToStreamAsync(ms, opt));
                        File.WriteAllBytes(outPath, ReadAll(ms));
                    }
                    made.Add(outPath);
                }
            }
            catch (Exception)
            {
                foreach (var m in made) TryDelete(m);
                if (count > 1) TryDelete(outDir);
                throw;
            }
            if (job.Cancelled) { foreach (var m in made) TryDelete(m); if (count > 1) TryDelete(outDir); return; }
            lock (job.Outputs) job.Outputs.Add(count > 1 ? outDir : made[0]);
        }

        // Archives

        static void Zip(ConvJob job, string outPath)
        {
            var entries = new List<KeyValuePair<string, string>>();
            foreach (var f in job.Files)
            {
                string clean = f.TrimEnd('\\');
                if (Directory.Exists(clean))
                {
                    string root = Path.GetDirectoryName(clean);
                    foreach (var sub in Directory.GetFiles(clean, "*", SearchOption.AllDirectories))
                        entries.Add(new KeyValuePair<string, string>(sub, sub.Substring(root.Length).TrimStart('\\').Replace('\\', '/')));
                }
                else entries.Add(new KeyValuePair<string, string>(clean, Path.GetFileName(clean)));
            }
            var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            using (var z = ZipFile.Open(outPath, ZipArchiveMode.Create))
                for (int i = 0; i < entries.Count && !job.Cancelled; i++)
                {
                    job.Part = (float)i / Math.Max(1, entries.Count);
                    job.Ping();
                    string entry = entries[i].Value;
                    for (int n = 1; !used.Add(entry); n++)
                        entry = Path.GetFileNameWithoutExtension(entries[i].Value) + " (" + n + ")" + Path.GetExtension(entries[i].Value);
                    z.CreateEntryFromFile(entries[i].Key, entry, CompressionLevel.Optimal);
                }
        }

        // Entries stay inside the target folder
        static void Unzip(ConvJob job, string src, string dir, string name)
        {
            string outDir = UniqueDir(dir, name);
            string root = Path.GetFullPath(outDir) + Path.DirectorySeparatorChar;
            Directory.CreateDirectory(outDir);
            try
            {
                using (var z = ZipFile.OpenRead(src))
                {
                    int i = 0, n = Math.Max(1, z.Entries.Count);
                    foreach (var e in z.Entries)
                    {
                        if (job.Cancelled) break;
                        job.Part = (float)i++ / n;
                        job.Ping();
                        string full = Path.GetFullPath(Path.Combine(outDir, e.FullName));
                        if (!full.StartsWith(root, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException(Lang.T("ZIP berisi jalur di luar folder"));
                        if (e.FullName.EndsWith("/")) { Directory.CreateDirectory(full); continue; }
                        Directory.CreateDirectory(Path.GetDirectoryName(full));
                        e.ExtractToFile(full, false);
                    }
                }
            }
            catch (Exception) { TryDelete(outDir); throw; }
            if (job.Cancelled) { TryDelete(outDir); return; }
            lock (job.Outputs) job.Outputs.Add(outDir);
        }
    }
}
