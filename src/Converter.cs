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
    enum ConvKind { Image, Video, Audio, Doc, Slides, Sheet, Pdf, Markdown, Html, Zip, Folder, Other }

    class ConvTarget
    {
        public string Id, Label;
        // Outside tools this target needs
        public string[] Needs = new string[0];
        // Snapshot of Ready when the box opens
        public bool Ok = true;
        public bool Ready { get { foreach (var n in Needs) if (Converter.Tool(n) == null) return false; return true; } }
    }

    // Choices that shape every output; defaults match the old behaviour
    class ConvOpts
    {
        // 0 best, 1 balanced, 2 small
        public int Quality = 1;
        // Images: 0 original, 1 75%, 2 50%, 3 25%
        public int Scale;
        // Images to PDF: 0 A4, 1 Letter, 2 page fits the image
        public int Page;
        // Video: 0 original, 1 1080p, 2 720p, 3 480p
        public int Res;
        // Empty: beside the source
        public string OutDir = "";

        static readonly float[] scales = { 1f, 0.75f, 0.5f, 0.25f };
        static readonly int[] sides = { 0, 1080, 720, 480 };
        public float ScaleK { get { return scales[Math.Max(0, Math.Min(3, Scale))]; } }
        public int ShortSide { get { return sides[Math.Max(0, Math.Min(3, Res))]; } }
        // Pick by quality: best, balanced, small
        public T By<T>(T best, T balanced, T small) { return Quality == 0 ? best : Quality >= 2 ? small : balanced; }
    }

    class ConvJob
    {
        public List<string> Files;
        public ConvTarget Target;
        public ConvOpts Opts = new ConvOpts();
        public volatile bool Cancelled, Finished;
        public volatile int Index;
        // Compress: files already as small as they get, and the bytes saved
        public int Small;
        public long BytesIn, BytesOut;
        // Current file, 0..1; below 0 unknown
        public volatile float Part = -1;
        public readonly List<string> Outputs = new List<string>();
        public readonly List<string> Errors = new List<string>();
        public Process Proc;
        public event Action Changed;
        public void Ping() { var c = Changed; if (c != null) c(); }

        public float Overall
        {
            get
            {
                int n = Math.Max(1, Files.Count);
                // One output from many inputs: Part is the whole job
                bool single = Target != null && Converter.Combines(Target.Id);
                if (single) return Math.Max(0f, Math.Min(1f, Part >= 0 ? Part : (float)Index / n));
                return Math.Min(1f, (Index + Math.Max(0f, Part)) / n);
            }
        }
    }

    // Converts beside source; tools never bundled
    static partial class Converter
    {
        static readonly Dictionary<string, ConvKind> kinds = new Dictionary<string, ConvKind>(StringComparer.OrdinalIgnoreCase);

        static Converter()
        {
            Map(ConvKind.Image, "jpg jpeg jfif png bmp gif tif tiff ico webp heic heif avif jxr wdp");
            Map(ConvKind.Video, "mp4 m4v mov mkv webm avi wmv flv 3gp mpg mpeg ts mts m2ts");
            Map(ConvKind.Audio, "mp3 wav m4a aac flac ogg oga opus wma aif aiff amr");
            Map(ConvKind.Doc, "docx doc odt rtf");
            Map(ConvKind.Slides, "pptx ppt odp");
            Map(ConvKind.Sheet, "xlsx xls ods csv");
            Map(ConvKind.Pdf, "pdf");
            Map(ConvKind.Markdown, "md markdown");
            Map(ConvKind.Html, "html htm");
            Map(ConvKind.Zip, "zip");
        }

        static void Map(ConvKind k, string exts) { foreach (var e in exts.Split(' ')) kinds["." + e] = k; }

        public static ConvKind KindOf(string path)
        {
            if (Directory.Exists(path)) return ConvKind.Folder;
            ConvKind k;
            return kinds.TryGetValue(Path.GetExtension(path), out k) ? k : ConvKind.Other;
        }

        // Same family counts as same format
        static string Norm(string ext)
        {
            ext = ext.TrimStart('.').ToLowerInvariant();
            switch (ext)
            {
                case "jpeg": case "jfif": return "jpg";
                case "tif": return "tiff";
                case "htm": return "html";
                case "markdown": return "md";
                case "oga": return "ogg";
                default: return ext;
            }
        }

        static ConvTarget T(string id, string label, params string[] needs) { return new ConvTarget { Id = id, Label = label, Needs = needs }; }

        static List<ConvTarget> For(ConvKind k)
        {
            var l = new List<ConvTarget>();
            switch (k)
            {
                case ConvKind.Image:
                    l.Add(T("jpg", "JPG")); l.Add(T("png", "PNG")); l.Add(T("webp", "WEBP", "ffmpeg")); l.Add(T("bmp", "BMP"));
                    l.Add(T("tiff", "TIFF")); l.Add(T("gif", "GIF")); l.Add(T("ico", "ICO")); l.Add(T("pdf", "PDF"));
                    l.Add(T("ocr", Lang.T("Teks (OCR)"), "ocr")); l.Add(T("min", Lang.T("Kompres")));
                    break;
                case ConvKind.Video:
                    l.Add(T("mp4", "MP4", "ffmpeg")); l.Add(T("webm", "WEBM", "ffmpeg")); l.Add(T("mov", "MOV", "ffmpeg")); l.Add(T("mkv", "MKV", "ffmpeg"));
                    l.Add(T("gif", "GIF", "ffmpeg")); l.Add(T("mp3", "MP3", "ffmpeg")); l.Add(T("m4a", "M4A", "ffmpeg")); l.Add(T("wav", "WAV", "ffmpeg"));
                    l.Add(T("min", Lang.T("Kompres"), "ffmpeg"));
                    break;
                case ConvKind.Audio:
                    l.Add(T("mp3", "MP3", "ffmpeg")); l.Add(T("m4a", "M4A", "ffmpeg")); l.Add(T("wav", "WAV", "ffmpeg")); l.Add(T("flac", "FLAC", "ffmpeg"));
                    l.Add(T("ogg", "OGG", "ffmpeg")); l.Add(T("opus", "OPUS", "ffmpeg"));
                    l.Add(T("min", Lang.T("Kompres"), "ffmpeg"));
                    break;
                case ConvKind.Doc:
                    l.Add(T("pdf", "PDF", "soffice")); l.Add(T("docx", "DOCX", "soffice")); l.Add(T("odt", "ODT", "soffice")); l.Add(T("rtf", "RTF", "soffice"));
                    l.Add(T("txt", "TXT", "soffice")); l.Add(T("html", "HTML", "soffice")); l.Add(T("png", "PNG", "soffice")); l.Add(T("jpg", "JPG", "soffice"));
                    break;
                case ConvKind.Slides:
                    l.Add(T("pdf", "PDF", "soffice")); l.Add(T("pptx", "PPTX", "soffice")); l.Add(T("odp", "ODP", "soffice")); l.Add(T("png", "PNG", "soffice")); l.Add(T("jpg", "JPG", "soffice"));
                    break;
                case ConvKind.Sheet:
                    l.Add(T("pdf", "PDF", "soffice")); l.Add(T("xlsx", "XLSX", "soffice")); l.Add(T("ods", "ODS", "soffice")); l.Add(T("csv", "CSV", "soffice")); l.Add(T("html", "HTML", "soffice"));
                    break;
                case ConvKind.Pdf:
                    l.Add(T("docx", "DOCX", "pdfword")); l.Add(T("txt", "TXT", "pdfword")); l.Add(T("png", "PNG")); l.Add(T("jpg", "JPG"));
                    l.Add(T("ocr", Lang.T("Teks (OCR)"), "ocr")); l.Add(T("min", Lang.T("Kompres"))); l.Add(T("split", Lang.T("Pisah halaman")));
                    break;
                case ConvKind.Markdown:
                    l.Add(T("docx", "DOCX", "pandoc")); l.Add(T("html", "HTML", "pandoc")); l.Add(T("pdf", "PDF", "pandoc", "soffice"));
                    break;
                case ConvKind.Html:
                    l.Add(T("pdf", "PDF", "soffice")); l.Add(T("docx", "DOCX", "pandoc")); l.Add(T("md", "MD", "pandoc"));
                    break;
                case ConvKind.Zip:
                    l.Add(T("unzip", Lang.T("Ekstrak")));
                    break;
            }
            return l;
        }

        // Many inputs, one output
        public static bool Combines(string id)
        {
            switch (id)
            {
                case "zip": case "pdf1": case "gifanim": case "stitch": case "audiocat": return true;
                default: return false;
            }
        }

        // Tools a file needs on its way into a merged PDF
        static string[] PdfNeeds(ConvKind k)
        {
            switch (k)
            {
                case ConvKind.Doc: case ConvKind.Slides: case ConvKind.Sheet: case ConvKind.Html: return new[] { "soffice" };
                case ConvKind.Markdown: return new[] { "pandoc", "soffice" };
                default: return new string[0];
            }
        }

        // Formats every dropped file can become
        public static List<ConvTarget> Targets(IList<string> files)
        {
            List<ConvTarget> common = null;
            bool paged = files.Count > 0, still = false, images = files.Count > 0, audio = files.Count > 0;
            var pdfNeeds = new List<string>();
            foreach (var f in files)
            {
                var k = KindOf(f);
                if (k != ConvKind.Image) images = false;
                if (k != ConvKind.Audio) audio = false;
                // Pictures, PDFs, and anything LibreOffice prints can share one PDF
                if (k != ConvKind.Image && k != ConvKind.Pdf && PdfNeeds(k).Length == 0) paged = false;
                foreach (var n in PdfNeeds(k)) if (!pdfNeeds.Contains(n)) pdfNeeds.Add(n);
                // Animations and icons lose frames when squeezed
                string ext = Norm(Path.GetExtension(f));
                if (ext == "gif" || ext == "ico") still = true;
                var mine = For(k);
                if (common == null) common = mine;
                else common = common.FindAll(t => mine.Exists(m => m.Id == t.Id));
            }
            if (common == null) common = new List<ConvTarget>();
            // Hidden when all files already match
            common.RemoveAll(t => AllAre(files, t.Id));
            if (still) common.RemoveAll(t => t.Id == "min");
            bool many = files.Count > 1;
            // Many files: say plainly that each one gets its own PDF
            if (many) foreach (var t in common) if (t.Id == "pdf") t.Label = Lang.T("PDF per berkas");
            if (paged && many) common.Add(T("pdf1", Lang.T("Gabung jadi satu PDF"), pdfNeeds.ToArray()));
            if (images && many)
            {
                common.Add(T("stitch", Lang.T("Gabung gambar")));
                common.Add(T("gifanim", Lang.T("GIF animasi"), "ffmpeg"));
            }
            if (audio && many) common.Add(T("audiocat", Lang.T("Gabung audio"), "ffmpeg"));
            bool oneZip = files.Count == 1 && KindOf(files[0]) == ConvKind.Zip;
            if (!oneZip) common.Add(T("zip", "ZIP"));
            return common;
        }

        // Explorer's order: page2 before page10
        [DllImport("shlwapi.dll", CharSet = CharSet.Unicode)]
        static extern int StrCmpLogicalW(string a, string b);

        public static int CompareNatural(string a, string b) { return StrCmpLogicalW(a ?? "", b ?? ""); }

        static bool AllAre(IList<string> files, string id)
        {
            foreach (var f in files) if (Norm(Path.GetExtension(f)) != id) return false;
            return true;
        }

        // "*.jpg;*.png" for a file picker
        public static string Patterns(ConvKind[] want)
        {
            var list = new List<string>();
            foreach (var kv in kinds) if (Array.IndexOf(want, kv.Value) >= 0) list.Add("*" + kv.Key);
            return string.Join(";", list.ToArray());
        }

        public static string KindName(IList<string> files)
        {
            if (files.Count == 0) return "";
            var k = KindOf(files[0]);
            foreach (var f in files) if (KindOf(f) != k) return Lang.T("Campuran");
            switch (k)
            {
                case ConvKind.Image: return Lang.T("Gambar");
                case ConvKind.Video: return "Video";
                case ConvKind.Audio: return "Audio";
                case ConvKind.Doc: return Lang.T("Dokumen");
                case ConvKind.Slides: return Lang.T("Presentasi");
                case ConvKind.Sheet: return Lang.T("Spreadsheet");
                case ConvKind.Pdf: return "PDF";
                case ConvKind.Markdown: return "Markdown";
                case ConvKind.Html: return "HTML";
                case ConvKind.Zip: return "ZIP";
                case ConvKind.Folder: return "Folder";
                default: return Lang.T("Berkas");
            }
        }

        // Absolute PATH entries, known installs only

        static readonly Dictionary<string, string> tools = new Dictionary<string, string>();
        static DateTime toolsUtc = DateTime.MinValue;

        public static string ToolName(string id)
        {
            switch (id)
            {
                case "soffice": return "LibreOffice";
                case "pandoc": return "Pandoc";
                case "pdfword": return Lang.T("Word atau LibreOffice");
                case "ocr": return Lang.T("bahasa OCR Windows");
                default: return "ffmpeg";
            }
        }

        // Word's COM server, from its registration
        static string WordExe()
        {
            try
            {
                using (var progid = Microsoft.Win32.Registry.ClassesRoot.OpenSubKey(@"Word.Application\CLSID"))
                {
                    string clsid = progid != null ? progid.GetValue("") as string : null;
                    if (string.IsNullOrEmpty(clsid)) return null;
                    using (var server = Microsoft.Win32.Registry.ClassesRoot.OpenSubKey(@"CLSID\" + clsid + @"\LocalServer32"))
                    {
                        string cmd = server != null ? server.GetValue("") as string : null;
                        if (string.IsNullOrEmpty(cmd)) return null;
                        cmd = cmd.Trim();
                        string exe = cmd.StartsWith("\"") ? cmd.Substring(1, Math.Max(0, cmd.IndexOf('"', 1) - 1)) : cmd.Split(' ')[0];
                        return File.Exists(exe) ? exe : null;
                    }
                }
            }
            catch (Exception) { return null; }
        }

        public static string Tool(string id)
        {
            lock (tools)
            {
                if ((DateTime.UtcNow - toolsUtc).TotalSeconds > 30) { tools.Clear(); toolsUtc = DateTime.UtcNow; }
                string path;
                if (!tools.TryGetValue(id, out path)) { path = FindTool(id); tools[id] = path; }
                return path;
            }
        }

        static string FindTool(string id)
        {
            // PDF to Word: Word reads PDFs best; LibreOffice will do
            if (id == "pdfword") return WordExe() ?? FindTool("soffice");
            // OCR is Windows' own: ready once a language is installed
            if (id == "ocr") return Ocr.Available ? "windows" : null;
            string exe = id + ".exe";
            var dirs = new List<string>();
            foreach (var d in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(';'))
            {
                string t = d.Trim().Trim('"');
                if (t.Length > 2 && Path.IsPathRooted(t) && !t.StartsWith(@"\\")) dirs.Add(t);
            }
            string pf = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
            string pf86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
            string local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            if (id == "soffice") { dirs.Add(Path.Combine(pf, @"LibreOffice\program")); dirs.Add(Path.Combine(pf86, @"LibreOffice\program")); }
            if (id == "pandoc") { dirs.Add(Path.Combine(local, "Pandoc")); dirs.Add(Path.Combine(pf, "Pandoc")); }
            if (id == "ffmpeg") dirs.Add(Path.Combine(local, @"Microsoft\WinGet\Links"));
            foreach (var d in dirs)
            {
                try
                {
                    string full = Path.Combine(d, exe);
                    if (File.Exists(full)) return full;
                }
                catch (Exception) { }
            }
            return null;
        }

        // Job

        public static void Start(ConvJob job)
        {
            var t = new Thread(delegate () { Run(job); }) { IsBackground = true, Name = "convert" };
            t.SetApartmentState(ApartmentState.MTA);
            t.Start();
        }

        static void Run(ConvJob job)
        {
            try
            {
                string id = job.Target.Id;
                if (Combines(id))
                {
                    string first = job.Files[0].TrimEnd('\\');
                    string dir = OutDir(job, first);
                    string name = job.Files.Count == 1 ? Path.GetFileNameWithoutExtension(first) : new DirectoryInfo(Path.GetDirectoryName(first)).Name;
                    if (string.IsNullOrEmpty(name)) name = "Deskmate";
                    string outPath = UniqueFile(dir, name, CombinedExt(job));
                    Guard(job, outPath, delegate
                    {
                        switch (id)
                        {
                            case "zip": Zip(job, outPath); break;
                            case "stitch": Stitch(job, outPath); break;
                            case "gifanim": AnimatedGif(job, outPath); break;
                            case "audiocat": JoinAudio(job, outPath); break;
                            default:
                                if (job.Files.TrueForAll(f => KindOf(f) == ConvKind.Image)) ImagesToPdf(job, job.Files, outPath, true);
                                else MergePdfs(job, outPath);
                                break;
                        }
                    });
                }
                else
                {
                    for (int i = 0; i < job.Files.Count && !job.Cancelled; i++)
                    {
                        job.Index = i;
                        job.Part = -1;
                        job.Ping();
                        string src = job.Files[i];
                        try { One(job, src, id); }
                        catch (Exception ex)
                        {
                            if (job.Cancelled) break;
                            Log.Write("convert " + Path.GetFileName(src) + ": " + ex.Message);
                            lock (job.Errors) job.Errors.Add(Path.GetFileName(src) + ": " + Short(ex));
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                if (!job.Cancelled)
                {
                    Log.Write("convert: " + ex.Message);
                    lock (job.Errors) job.Errors.Add(Short(ex));
                }
            }
            job.Index = job.Files.Count;
            job.Part = 0;
            job.Finished = true;
            job.Ping();
        }

        static string Short(Exception ex)
        {
            if (ex is AggregateException && ex.InnerException != null) ex = ex.InnerException;
            string m = ex.Message.Replace("\r", " ").Replace("\n", " ").Trim();
            return m.Length > 140 ? m.Substring(0, 140) + "…" : m;
        }

        // Partial output goes on failure
        static void Guard(ConvJob job, string outPath, Action work)
        {
            try { work(); }
            catch (Exception)
            {
                TryDelete(outPath);
                throw;
            }
            if (job.Cancelled) { TryDelete(outPath); return; }
            lock (job.Outputs) job.Outputs.Add(outPath);
        }

        static string CombinedExt(ConvJob job)
        {
            switch (job.Target.Id)
            {
                case "zip": return ".zip";
                case "gifanim": return ".gif";
                case "audiocat": return ".mp3";
                // Lossless unless every piece was a JPEG already
                case "stitch": return job.Files.TrueForAll(f => Norm(Path.GetExtension(f)) == "jpg") ? ".jpg" : ".png";
                default: return ".pdf";
            }
        }

        // The chosen folder, else beside the source
        static string OutDir(ConvJob job, string src)
        {
            string d = job.Opts.OutDir;
            if (!string.IsNullOrEmpty(d))
            {
                if (!Directory.Exists(d)) throw new DirectoryNotFoundException(Lang.T("Folder hasil tidak ada"));
                return d;
            }
            return Path.GetDirectoryName(src.TrimEnd('\\'));
        }

        static void One(ConvJob job, string src, string id)
        {
            var kind = KindOf(src);
            string dir = OutDir(job, src);
            string name = Path.GetFileNameWithoutExtension(src.TrimEnd('\\'));
            if (kind == ConvKind.Zip && id == "unzip") { Unzip(job, src, dir, name); return; }
            if (id == "min") { Compress(job, src, kind, dir, name); return; }
            if (id == "ocr") { OcrText(job, src, kind, dir, name); return; }
            if (id == "split") { SplitPdf(job, src, dir, name); return; }
            if (kind == ConvKind.Pdf && (id == "png" || id == "jpg")) { PdfToImages(job, src, dir, name, id); return; }
            if ((kind == ConvKind.Slides || kind == ConvKind.Doc) && (id == "png" || id == "jpg")) { OfficeToImages(job, src, dir, name, id); return; }
            // Already that format: nothing to do
            if (Norm(Path.GetExtension(src)) == id) return;
            string outPath = UniqueFile(dir, name, "." + id);
            Guard(job, outPath, delegate
            {
                switch (kind)
                {
                    case ConvKind.Pdf:
                        PdfToWord(job, src, outPath, id);
                        break;
                    case ConvKind.Image:
                        if (id == "pdf") ImagesToPdf(job, new List<string> { src }, outPath, false);
                        else ImageTo(job, src, outPath, id);
                        break;
                    case ConvKind.Video:
                    case ConvKind.Audio:
                        Media(job, src, outPath, id);
                        break;
                    case ConvKind.Markdown:
                    case ConvKind.Html:
                        if (id == "pdf" && kind == ConvKind.Markdown) MarkdownToPdf(job, src, outPath);
                        else if (id == "pdf") Office(job, src, outPath, "pdf");
                        else Pandoc(job, src, outPath, kind == ConvKind.Markdown ? "markdown" : "html", id == "md" ? "gfm" : id);
                        break;
                    default:
                        Office(job, src, outPath, OfficeFilter(id));
                        break;
                }
            });
        }

        // Names

        public static string UniqueFile(string dir, string name, string ext)
        {
            string p = Path.Combine(dir, name + ext);
            for (int i = 1; File.Exists(p) || Directory.Exists(p); i++) p = Path.Combine(dir, name + " (" + i + ")" + ext);
            return p;
        }

        static string UniqueDir(string dir, string name) { return UniqueFile(dir, name, ""); }

        static void TryDelete(string p)
        {
            try
            {
                if (File.Exists(p)) File.Delete(p);
                else if (Directory.Exists(p)) Directory.Delete(p, true);
            }
            catch (Exception) { }
        }

        // WinRT awaits without Windows.winmd
        internal static T W<T>(Windows.Foundation.IAsyncOperation<T> op)
        {
            using (var done = new ManualResetEvent(false))
            {
                op.Completed = delegate { try { done.Set(); } catch (ObjectDisposedException) { } };
                if (op.Status == AsyncStatus.Started && !done.WaitOne(120000)) { op.Cancel(); throw new TimeoutException("WinRT"); }
            }
            if (op.Status != AsyncStatus.Completed) throw op.ErrorCode ?? new InvalidOperationException("WinRT " + op.Status);
            return op.GetResults();
        }

        internal static void W(IAsyncAction op)
        {
            using (var done = new ManualResetEvent(false))
            {
                op.Completed = delegate { try { done.Set(); } catch (ObjectDisposedException) { } };
                if (op.Status == AsyncStatus.Started && !done.WaitOne(120000)) { op.Cancel(); throw new TimeoutException("WinRT"); }
            }
            if (op.Status != AsyncStatus.Completed) throw op.ErrorCode ?? new InvalidOperationException("WinRT " + op.Status);
        }

        static byte[] ReadAll(InMemoryRandomAccessStream ms)
        {
            using (var reader = new DataReader(ms.GetInputStreamAt(0)))
            {
                uint n = W(reader.LoadAsync((uint)ms.Size));
                var bytes = new byte[n];
                reader.ReadBytes(bytes);
                return bytes;
            }
        }
    }
}
