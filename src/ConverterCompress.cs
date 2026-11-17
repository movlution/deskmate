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
        // Compress

        // Smaller copy beside the original
        static void Compress(ConvJob job, string src, ConvKind kind, string dir, string name)
        {
            string stem = name + " (" + Lang.T("kompres") + ")";
            switch (kind)
            {
                case ConvKind.Pdf:
                    string pdf = UniqueFile(dir, stem, ".pdf");
                    Shrunk(job, src, pdf, delegate
                    {
                        // About 150 dpi on a full page
                        PdfLite.Compress(src, pdf, 1800, 68, k => { if (job.Cancelled) throw new OperationCanceledException(); job.Part = k; job.Ping(); });
                        Verify(src, pdf);
                    });
                    break;
                case ConvKind.Image:
                    using (var bmp = Decode(src))
                    {
                        // Transparency stays PNG; photos become JPEG
                        bool alpha = HasAlpha(bmp);
                        string img = UniqueFile(dir, stem, alpha ? ".png" : ".jpg");
                        Shrunk(job, src, img, delegate
                        {
                            double k = Math.Min(1.0, 2560.0 / Math.Max(bmp.Width, bmp.Height));
                            using (var small = Scaled(bmp, Math.Max(1, (int)Math.Round(bmp.Width * k)), Math.Max(1, (int)Math.Round(bmp.Height * k))))
                            {
                                if (alpha) small.Save(img, ImageFormat.Png);
                                else File.WriteAllBytes(img, Jpeg(small, 78));
                            }
                        });
                    }
                    break;
                case ConvKind.Video:
                    string mp4 = UniqueFile(dir, stem, ".mp4");
                    // Long side at most 1080p's width, even sizes for H.264
                    Shrunk(job, src, mp4, delegate
                    {
                        Ffmpeg(job, src, mp4, "-c:v libx264 -preset medium -crf 28 -vf \"scale='if(gt(iw,ih),trunc(min(1920,iw)/2)*2,-2)':'if(gt(iw,ih),-2,trunc(min(1920,ih)/2)*2)'\" -pix_fmt yuv420p -c:a aac -b:a 128k -movflags +faststart");
                    });
                    break;
                case ConvKind.Audio:
                    string mp3 = UniqueFile(dir, stem, ".mp3");
                    Shrunk(job, src, mp3, delegate { Ffmpeg(job, src, mp3, "-vn -c:a libmp3lame -b:a 128k"); });
                    break;
                default:
                    throw new NotSupportedException(Lang.T("Berkas ini tidak bisa dikompres"));
            }
        }

        // Kept only when clearly smaller; the original is never touched
        static void Shrunk(ConvJob job, string src, string outPath, Action work)
        {
            try { work(); }
            catch (Exception) { TryDelete(outPath); throw; }
            if (job.Cancelled) { TryDelete(outPath); return; }
            long before = new FileInfo(src).Length, after = File.Exists(outPath) ? new FileInfo(outPath).Length : 0;
            if (after == 0 || after > before * 0.97) { TryDelete(outPath); job.Small++; return; }
            job.BytesIn += before;
            job.BytesOut += after;
            lock (job.Outputs) job.Outputs.Add(outPath);
        }

        static bool HasAlpha(Bitmap bmp)
        {
            if (!Image.IsAlphaPixelFormat(bmp.PixelFormat)) return false;
            var bd = bmp.LockBits(new Rectangle(0, 0, bmp.Width, bmp.Height), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
            try
            {
                var row = new byte[bmp.Width * 4];
                for (int y = 0; y < bmp.Height; y++)
                {
                    Marshal.Copy(new IntPtr(bd.Scan0.ToInt64() + (long)y * bd.Stride), row, 0, row.Length);
                    for (int x = 3; x < row.Length; x += 4) if (row[x] != 255) return true;
                }
            }
            finally { bmp.UnlockBits(bd); }
            return false;
        }

        static Bitmap Scaled(Bitmap src, int w, int h)
        {
            var b = new Bitmap(w, h, PixelFormat.Format32bppArgb);
            using (var g = Graphics.FromImage(b))
            using (var wrap = new ImageAttributes())
            {
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                g.CompositingMode = CompositingMode.SourceCopy;
                wrap.SetWrapMode(WrapMode.TileFlipXY);
                g.DrawImage(src, new Rectangle(0, 0, w, h), 0, 0, src.Width, src.Height, GraphicsUnit.Pixel, wrap);
            }
            return b;
        }

        // PDF to Word

        // DOCX: Word if installed, else LibreOffice's PDF import.
        // TXT comes out of that DOCX: their own text export skips text boxes
        static void PdfToWord(ConvJob job, string src, string outPath, string id)
        {
            string docx = id == "txt" ? TempPath(".docx") : outPath;
            try
            {
                if (WordExe() != null) WordSave(job, src, docx);
                else Office(job, src, docx, "docx:MS Word 2007 XML", "writer_pdf_import");
                if (id != "txt" || job.Cancelled) return;
                string text = DocxText(docx);
                // A scan has no text layer: read the pages instead
                if (text.Trim().Length == 0 && Ocr.Available) text = OcrPdf(job, src) ?? "";
                if (job.Cancelled) return;
                if (text.Trim().Length == 0) throw new InvalidOperationException(Lang.T("PDF ini tidak berisi teks, mungkin hasil scan"));
                File.WriteAllText(outPath, Lines(text), new UTF8Encoding(false));
            }
            finally { if (id == "txt") TryDelete(docx); }
        }

        // Paragraphs, tabs, and breaks, text boxes included
        static string DocxText(string path)
        {
            const string Ns = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";
            var sb = new StringBuilder();
            using (var z = ZipFile.OpenRead(path))
            {
                var entry = z.GetEntry("word/document.xml");
                if (entry == null) return "";
                using (var r = System.Xml.XmlReader.Create(entry.Open()))
                {
                    bool inText = false, more = r.Read();
                    for (; more; more = r.Read())
                    {
                        // Text boxes come twice: skip the old VML copy
                        while (more && r.NodeType == System.Xml.XmlNodeType.Element && r.LocalName == "Fallback" && r.NamespaceURI == "http://schemas.openxmlformats.org/markup-compatibility/2006")
                        {
                            r.Skip();
                            more = !r.EOF;
                        }
                        if (!more) break;
                        if (r.NodeType == System.Xml.XmlNodeType.Element && r.NamespaceURI == Ns)
                        {
                            if (r.LocalName == "t") inText = !r.IsEmptyElement;
                            else if (r.LocalName == "tab") sb.Append('\t');
                            else if (r.LocalName == "br" || r.LocalName == "cr") sb.Append('\n');
                        }
                        else if (r.NodeType == System.Xml.XmlNodeType.EndElement && r.NamespaceURI == Ns)
                        {
                            if (r.LocalName == "t") inText = false;
                            else if (r.LocalName == "p") sb.Append('\n');
                        }
                        else if (inText && (r.NodeType == System.Xml.XmlNodeType.Text || r.NodeType == System.Xml.XmlNodeType.SignificantWhitespace || r.NodeType == System.Xml.XmlNodeType.Whitespace)) sb.Append(r.Value);
                    }
                }
            }
            return sb.ToString();
        }

        // Windows line ends, no runs of blank lines
        static string Lines(string text)
        {
            text = text.Replace("\r\n", "\n").Replace('\r', '\n');
            while (text.Contains("\n\n\n")) text = text.Replace("\n\n\n", "\n\n");
            return text.Trim().Replace("\n", "\r\n") + "\r\n";
        }

        // OCR

        // Words in pictures and scans, read by Windows
        static void OcrText(ConvJob job, string src, ConvKind kind, string dir, string name)
        {
            string outPath = UniqueFile(dir, name, ".txt");
            Guard(job, outPath, delegate
            {
                string text = kind == ConvKind.Pdf ? OcrPdf(job, src) : Ocr.Read(src);
                if (job.Cancelled) return;
                if (text == null) throw new InvalidOperationException(Lang.T("Bahasa OCR Windows belum terpasang"));
                if (text.Trim().Length == 0) throw new InvalidOperationException(Lang.T("Tidak ada teks yang terbaca"));
                File.WriteAllText(outPath, Lines(text), new UTF8Encoding(false));
            });
        }

        // Page by page at about 190 dpi
        static string OcrPdf(ConvJob job, string src)
        {
            InMemoryRandomAccessStream held;
            var doc = LoadPdf(src, out held);
            using (held)
            {
                var sb = new StringBuilder();
                for (uint i = 0; i < doc.PageCount && !job.Cancelled; i++)
                {
                    job.Part = (float)i / doc.PageCount;
                    job.Ping();
                    using (var page = doc.GetPage(i))
                    using (var ms = new InMemoryRandomAccessStream())
                    {
                        W(page.RenderToStreamAsync(ms, new PdfPageRenderOptions { DestinationWidth = (uint)Math.Max(1, page.Size.Width * 2) }));
                        ms.Seek(0);
                        string text = Ocr.Read(ms);
                        if (text == null) return null;
                        if (text.Trim().Length > 0) sb.Append(text).Append("\n\n");
                    }
                }
                return sb.ToString();
            }
        }

        // Word on its own thread; no window, no prompts, no macros
        static void WordSave(ConvJob job, string src, string outPath)
        {
            Exception error = null;
            Process mine = null;
            var before = new HashSet<int>();
            foreach (var p in Process.GetProcessesByName("WINWORD")) { before.Add(p.Id); p.Dispose(); }
            var t = new Thread(delegate ()
            {
                object word = null;
                try
                {
                    word = Activator.CreateInstance(Type.GetTypeFromProgID("Word.Application", true));
                    foreach (var p in Process.GetProcessesByName("WINWORD")) { if (mine == null && !before.Contains(p.Id)) mine = p; else p.Dispose(); }
                    Com(word, "Visible", BindingFlags.SetProperty, false);
                    Com(word, "DisplayAlerts", BindingFlags.SetProperty, 0);
                    Com(word, "AutomationSecurity", BindingFlags.SetProperty, 3);
                    var docs = Com(word, "Documents", BindingFlags.GetProperty);
                    var doc = ComNamed(docs, "Open", new object[] { src, false, true, false }, "FileName", "ConfirmConversions", "ReadOnly", "AddToRecentFiles");
                    try
                    {
                        // 16: DOCX; a background save must land first
                        ComNamed(doc, "SaveAs2", new object[] { outPath, 16 }, "FileName", "FileFormat");
                        for (int i = 0; i < 600 && Convert.ToInt32(Com(word, "BackgroundSavingStatus", BindingFlags.GetProperty)) > 0; i++) Thread.Sleep(100);
                    }
                    finally
                    {
                        ComNamed(doc, "Close", new object[] { false }, "SaveChanges");
                        Marshal.ReleaseComObject(doc);
                        Marshal.ReleaseComObject(docs);
                    }
                }
                catch (Exception ex) { error = ex is TargetInvocationException && ex.InnerException != null ? ex.InnerException : ex; }
                finally
                {
                    // Quit only a Word this job started
                    if (word != null)
                    {
                        try { if (mine != null) ComNamed(word, "Quit", new object[] { false }, "SaveChanges"); } catch (Exception) { }
                        try { Marshal.ReleaseComObject(word); } catch (Exception) { }
                    }
                }
            }) { IsBackground = true, Name = "word" };
            t.SetApartmentState(ApartmentState.STA);
            t.Start();
            var started = DateTime.UtcNow;
            while (!t.Join(250))
            {
                if (!job.Cancelled && (DateTime.UtcNow - started).TotalMinutes < 4) continue;
                // Stuck or cancelled: end only our Word
                if (mine != null) try { mine.Kill(); } catch (Exception) { }
                t.Join(5000);
                if (mine != null) mine.Dispose();
                if (job.Cancelled) return;
                throw new TimeoutException(Lang.T("Word terlalu lama"));
            }
            if (mine != null) mine.Dispose();
            if (error != null) throw error;
            if (!File.Exists(outPath)) throw new InvalidOperationException(Lang.T("Word tidak menghasilkan berkas"));
        }

        static object Com(object target, string name, BindingFlags how, params object[] args)
        {
            return target.GetType().InvokeMember(name, how, null, target, args);
        }

        static object ComNamed(object target, string name, object[] args, params string[] names)
        {
            return target.GetType().InvokeMember(name, BindingFlags.InvokeMethod, null, target, args, null, null, names);
        }
    }
}
