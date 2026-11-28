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

        // Decoded at the chosen size; icons keep every pixel for their own sizes
        static Bitmap DecodeSized(string src, ConvOpts o)
        {
            var bmp = Decode(src);
            float k = o.ScaleK;
            if (k >= 1f) return bmp;
            try { return Scaled(bmp, Math.Max(1, (int)Math.Round(bmp.Width * k)), Math.Max(1, (int)Math.Round(bmp.Height * k))); }
            finally { bmp.Dispose(); }
        }

        static void ImageTo(ConvJob job, string src, string outPath, string id)
        {
            var o = job.Opts;
            if (id == "webp") { ImageViaFfmpeg(job, src, outPath); return; }
            using (var bmp = id == "ico" ? Decode(src) : DecodeSized(src, o))
            {
                switch (id)
                {
                    case "jpg": File.WriteAllBytes(outPath, Jpeg(bmp, o.By(97L, 92L, 78L))); break;
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
        static void ImageViaFfmpeg(ConvJob job, string src, string outPath)
        {
            string tmp = Path.Combine(Path.GetTempPath(), "deskmate-" + Guid.NewGuid().ToString("N") + ".png");
            try
            {
                using (var bmp = DecodeSized(src, job.Opts)) bmp.Save(tmp, ImageFormat.Png);
                Ffmpeg(null, tmp, outPath, "-c:v libwebp -quality " + job.Opts.By(96, 90, 72));
            }
            finally { TryDelete(tmp); }
        }

        // Pictures stacked top to bottom, centred on the widest
        static void Stitch(ConvJob job, string outPath)
        {
            var parts = new List<Bitmap>();
            try
            {
                for (int i = 0; i < job.Files.Count && !job.Cancelled; i++)
                {
                    job.Index = i;
                    job.Ping();
                    parts.Add(DecodeSized(job.Files[i], job.Opts));
                }
                if (job.Cancelled) return;
                int w = 0;
                long h = 0;
                foreach (var b in parts) { w = Math.Max(w, b.Width); h += b.Height; }
                if (h > 60000 || (long)w * h > 150000000L) throw new InvalidOperationException(Lang.T("Gambar terlalu besar untuk digabung"));
                bool jpg = Path.GetExtension(outPath).Equals(".jpg", StringComparison.OrdinalIgnoreCase);
                using (var sheet = new Bitmap(w, (int)h, PixelFormat.Format32bppArgb))
                {
                    using (var g = Graphics.FromImage(sheet))
                    {
                        g.Clear(jpg ? Color.White : Color.Transparent);
                        int y = 0;
                        foreach (var b in parts)
                        {
                            g.DrawImage(b, new Rectangle((w - b.Width) / 2, y, b.Width, b.Height));
                            y += b.Height;
                        }
                    }
                    if (jpg) File.WriteAllBytes(outPath, Jpeg(sheet, job.Opts.By(97L, 92L, 78L)));
                    else sheet.Save(outPath, ImageFormat.Png);
                }
            }
            finally { foreach (var b in parts) b.Dispose(); }
        }

        // Frames share the first picture's size; ffmpeg picks the palette
        static void AnimatedGif(ConvJob job, string outPath)
        {
            string tmp = Path.Combine(Path.GetTempPath(), "deskmate-gif-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tmp);
            try
            {
                Size frame = Size.Empty;
                for (int i = 0; i < job.Files.Count && !job.Cancelled; i++)
                {
                    job.Index = i;
                    job.Part = 0.5f * i / job.Files.Count;
                    job.Ping();
                    using (var bmp = DecodeSized(job.Files[i], job.Opts))
                    {
                        if (frame.IsEmpty)
                        {
                            // GIFs stay modest: long side by quality
                            int cap = job.Opts.By(1280, 800, 480);
                            double k = Math.Min(1.0, (double)cap / Math.Max(bmp.Width, bmp.Height));
                            frame = new Size(Math.Max(2, (int)(bmp.Width * k) / 2 * 2), Math.Max(2, (int)(bmp.Height * k) / 2 * 2));
                        }
                        using (var f = new Bitmap(frame.Width, frame.Height, PixelFormat.Format24bppRgb))
                        {
                            using (var g = Graphics.FromImage(f))
                            {
                                g.Clear(Color.White);
                                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                                g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                                double k = Math.Min((double)frame.Width / bmp.Width, (double)frame.Height / bmp.Height);
                                float dw = (float)(bmp.Width * k), dh = (float)(bmp.Height * k);
                                g.DrawImage(bmp, new RectangleF((frame.Width - dw) / 2, (frame.Height - dh) / 2, dw, dh));
                            }
                            f.Save(Path.Combine(tmp, "f" + i.ToString("D4", CultureInfo.InvariantCulture) + ".png"), ImageFormat.Png);
                        }
                    }
                }
                if (job.Cancelled) return;
                job.Part = -1;
                job.Ping();
                FfmpegIn(job, "-framerate 2 -i " + Q(Path.Combine(tmp, "f%04d.png")), outPath,
                    "-vf \"split[a][b];[a]palettegen=stats_mode=diff[p];[b][p]paletteuse=dither=sierra2_4a\" -loop 0");
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

        // PDF: one page per image (A4, Letter, or the image's own size), JPEG inside
        static void ImagesToPdf(ConvJob job, List<string> files, string outPath, bool track)
        {
            var o = job.Opts;
            bool fit = o.Page == 2;
            float PageW = o.Page == 1 ? 612f : 595.28f, PageH = o.Page == 1 ? 792f : 841.89f, Margin = fit ? 0f : 28f;
            var pages = new List<byte[]>();
            // Pixels kept, and the size they are laid out at: scaling saves bytes, not page space
            var dims = new List<Size>();
            var shown = new List<Size>();
            for (int i = 0; i < files.Count && !job.Cancelled; i++)
            {
                if (track) { job.Index = i; job.Ping(); }
                using (var full = Decode(files[i]))
                {
                    shown.Add(full.Size);
                    float sk = o.ScaleK;
                    using (var bmp = sk < 1f ? Scaled(full, Math.Max(1, (int)Math.Round(full.Width * sk)), Math.Max(1, (int)Math.Round(full.Height * sk))) : null)
                    {
                        var use = bmp ?? full;
                        pages.Add(Jpeg(use, o.By(95L, 88L, 72L)));
                        dims.Add(use.Size);
                    }
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
                    var d = shown[i];
                    bool wide = d.Width > d.Height;
                    float pw = wide ? PageH : PageW, ph = wide ? PageW : PageH;
                    // Own size: the page is the picture at 96 dpi
                    if (fit) { pw = Math.Max(1f, d.Width * 0.75f); ph = Math.Max(1f, d.Height * 0.75f); }
                    // 96 dpi at most, never blown up
                    float k = Math.Min(0.75f, Math.Min((pw - 2 * Margin) / d.Width, (ph - 2 * Margin) / d.Height));
                    float dw = d.Width * k, dh = d.Height * k;
                    string draw = string.Format(inv, "q {0:0.##} 0 0 {1:0.##} {2:0.##} {3:0.##} cm /Im0 Do Q", dw, dh, (pw - dw) / 2, (ph - dh) / 2);
                    offsets.Add(fs.Position);
                    put(string.Format(inv, "{0} 0 obj\n<< /Type /Page /Parent 2 0 R /MediaBox [0 0 {1:0.##} {2:0.##}] /Resources << /XObject << /Im0 {3} 0 R >> >> /Contents {4} 0 R >>\nendobj\n", pg, pw, ph, im, ct));
                    offsets.Add(fs.Position);
                    put(ct + " 0 obj\n<< /Length " + draw.Length + " >>\nstream\n" + draw + "\nendstream\nendobj\n");
                    offsets.Add(fs.Position);
                    put(im + " 0 obj\n<< /Type /XObject /Subtype /Image /Width " + dims[i].Width + " /Height " + dims[i].Height + " /ColorSpace /DeviceRGB /BitsPerComponent 8 /Filter /DCTDecode /Length " + pages[i].Length + " >>\nstream\n");
                    fs.Write(pages[i], 0, pages[i].Length);
                    put("\nendstream\nendobj\n");
                }
                long xref = fs.Position;
                var sb = new StringBuilder();
                sb.Append("xref\n0 ").Append(objs + 1).Append("\n0000000000 65535 f \n");
                foreach (var off in offsets) sb.Append(off.ToString("D10")).Append(" 00000 n \n");
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
                        // 288, 192, or 96 dpi by quality
                        var opt = new PdfPageRenderOptions { DestinationWidth = (uint)Math.Max(1, page.Size.Width * job.Opts.By(3.0, 2.0, 1.0)) };
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
