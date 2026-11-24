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
        // Outside tools

        static string Q(string arg)
        {
            if (arg.Length > 0 && arg.IndexOfAny(new[] { ' ', '\t', '"' }) < 0) return arg;
            var sb = new StringBuilder("\"");
            int slashes = 0;
            foreach (char c in arg)
            {
                if (c == '\\') { slashes++; continue; }
                if (c == '"') sb.Append('\\', slashes * 2 + 1);
                else sb.Append('\\', slashes);
                slashes = 0;
                sb.Append(c);
            }
            sb.Append('\\', slashes * 2).Append('"');
            return sb.ToString();
        }

        static string FfmpegArgs(string id)
        {
            switch (id)
            {
                case "mp4": return "-c:v libx264 -preset veryfast -crf 23 -pix_fmt yuv420p -c:a aac -b:a 160k -movflags +faststart";
                case "webm": return "-c:v libvpx-vp9 -crf 33 -b:v 0 -row-mt 1 -deadline good -cpu-used 4 -c:a libopus -b:a 128k";
                case "gif": return "-an -vf \"fps=12,scale='min(480,iw)':-2:flags=lanczos,split[a][b];[a]palettegen[p];[b][p]paletteuse\" -loop 0";
                case "mp3": return "-vn -c:a libmp3lame -q:a 2";
                case "m4a": return "-vn -c:a aac -b:a 192k";
                case "wav": return "-vn -c:a pcm_s16le";
                case "flac": return "-vn -c:a flac";
                case "ogg": return "-vn -c:a libvorbis -q:a 5";
                default: throw new NotSupportedException(id);
            }
        }

        static Process Launch(string exe, string args, bool readOut)
        {
            var psi = new ProcessStartInfo(exe, args)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = readOut,
                RedirectStandardError = true,
                StandardErrorEncoding = Encoding.UTF8,
                WorkingDirectory = Path.GetTempPath()
            };
            if (readOut) psi.StandardOutputEncoding = Encoding.UTF8;
            var p = Process.Start(psi);
            try { p.PriorityClass = ProcessPriorityClass.BelowNormal; } catch (Exception) { }
            return p;
        }

        // Ends the tool and its children
        public static void Kill(Process p)
        {
            if (p == null) return;
            try
            {
                if (p.HasExited) return;
                using (var k = Process.Start(new ProcessStartInfo("taskkill.exe", "/T /F /PID " + p.Id) { UseShellExecute = false, CreateNoWindow = true })) k.WaitForExit(5000);
            }
            catch (Exception ex) { Log.Write("convert kill: " + ex.Message); }
        }

        static void Ffmpeg(ConvJob job, string src, string outPath, string args)
        {
            string exe = Converter.Tool("ffmpeg");
            if (exe == null) throw new FileNotFoundException(Lang.T("ffmpeg tidak ditemukan"));
            string line = "-hide_banner -nostdin -y -i " + Q(src) + " " + args + " -progress pipe:1 -nostats " + Q(outPath);
            using (var p = Launch(exe, line, true))
            {
                if (job != null) job.Proc = p;
                double total = 0;
                var tail = new Queue<string>();
                var err = new Thread(delegate ()
                {
                    string l;
                    while ((l = p.StandardError.ReadLine()) != null)
                    {
                        int at = l.IndexOf("Duration: ");
                        TimeSpan ts;
                        if (at >= 0 && total == 0 && TimeSpan.TryParse(l.Substring(at + 10, Math.Min(11, l.Length - at - 10)), CultureInfo.InvariantCulture, out ts)) total = ts.TotalSeconds;
                        lock (tail) { tail.Enqueue(l); if (tail.Count > 4) tail.Dequeue(); }
                    }
                }) { IsBackground = true };
                err.Start();
                string o;
                while ((o = p.StandardOutput.ReadLine()) != null)
                {
                    if (job == null || total <= 0 || !o.StartsWith("out_time_us=")) continue;
                    long us;
                    if (long.TryParse(o.Substring(12), out us) && us > 0) { job.Part = (float)Math.Min(1, us / 1e6 / total); job.Ping(); }
                }
                p.WaitForExit();
                err.Join(2000);
                if (job != null) job.Proc = null;
                if (job != null && job.Cancelled) return;
                if (p.ExitCode != 0) { lock (tail) throw new InvalidOperationException(tail.Count > 0 ? tail.ToArray()[tail.Count - 1] : "ffmpeg " + p.ExitCode); }
            }
        }

        static string OfficeFilter(string id)
        {
            switch (id)
            {
                case "txt": return "txt:Text (encoded):UTF8";
                case "csv": return "csv:Text - txt - csv (StarCalc):44,34,76";
                case "rtf": return "rtf:Rich Text Format";
                default: return id;
            }
        }

        // LibreOffice: own profile, temp output
        static void Office(ConvJob job, string src, string outPath, string filter, string infilter = null)
        {
            string exe = Converter.Tool("soffice");
            if (exe == null) throw new FileNotFoundException(Lang.T("LibreOffice tidak ditemukan"));
            string tmp = Path.Combine(Path.GetTempPath(), "deskmate-ubah-" + Guid.NewGuid().ToString("N"));
            string profile = new Uri(Path.Combine(Path.GetTempPath(), "deskmate-lo-profile")).AbsoluteUri;
            Directory.CreateDirectory(tmp);
            try
            {
                string line = "--headless --norestore --nolockcheck --nodefault --nologo " + Q("-env:UserInstallation=" + profile) +
                    (infilter != null ? " --infilter=" + Q(infilter) : "") + " --convert-to " + Q(filter) + " --outdir " + Q(tmp) + " " + Q(src);
                using (var p = Launch(exe, line, true))
                {
                    job.Proc = p;
                    p.BeginOutputReadLine();
                    p.BeginErrorReadLine();
                    if (!p.WaitForExit(180000)) { Kill(p); throw new TimeoutException(Lang.T("LibreOffice terlalu lama")); }
                    job.Proc = null;
                    if (job.Cancelled) return;
                }
                var made = Directory.GetFiles(tmp);
                if (made.Length == 0) throw new InvalidOperationException(Lang.T("LibreOffice tidak menghasilkan berkas"));
                File.Move(made[0], outPath);
            }
            finally { TryDelete(tmp); }
        }

        static void Pandoc(ConvJob job, string src, string outPath, string from, string to)
        {
            string exe = Converter.Tool("pandoc");
            if (exe == null) throw new FileNotFoundException(Lang.T("Pandoc tidak ditemukan"));
            string line = "-f " + from + " -t " + to + (to == "html" ? " -s --metadata pagetitle=" + Q(Path.GetFileNameWithoutExtension(src)) : "") + " -o " + Q(outPath) + " " + Q(src);
            using (var p = Launch(exe, line, false))
            {
                job.Proc = p;
                string err = p.StandardError.ReadToEnd();
                if (!p.WaitForExit(120000)) { Kill(p); throw new TimeoutException("Pandoc"); }
                job.Proc = null;
                if (job.Cancelled) return;
                if (p.ExitCode != 0) throw new InvalidOperationException(err.Trim().Length > 0 ? err.Trim().Split('\n')[0] : "pandoc " + p.ExitCode);
            }
        }

        static void MarkdownToPdf(ConvJob job, string src, string outPath)
        {
            string tmp = TempPath(".docx");
            try
            {
                Pandoc(job, src, tmp, "markdown", "docx");
                if (!job.Cancelled) Office(job, tmp, outPath, "pdf");
            }
            finally { TryDelete(tmp); }
        }

        static string TempPath(string ext) { return Path.Combine(Path.GetTempPath(), "deskmate-" + Guid.NewGuid().ToString("N") + ext); }

        // Slides: through PDF, then a picture per slide
        static void SlidesToImages(ConvJob job, string src, string dir, string name, string id)
        {
            string tmp = TempPath(".pdf");
            try
            {
                Office(job, src, tmp, "pdf");
                if (!job.Cancelled) PdfToImages(job, tmp, dir, name, id);
            }
            finally { TryDelete(tmp); }
        }

        // Images become pages first, then one PDF
        static void MergePdfs(ConvJob job, string outPath)
        {
            var parts = new List<string>();
            var temps = new List<string>();
            uint pages = 0;
            try
            {
                for (int i = 0; i < job.Files.Count && !job.Cancelled; i++)
                {
                    job.Index = i;
                    job.Ping();
                    string part = job.Files[i];
                    if (KindOf(part) == ConvKind.Image)
                    {
                        part = TempPath(".pdf");
                        temps.Add(part);
                        ImagesToPdf(job, new List<string> { job.Files[i] }, part, false);
                    }
                    parts.Add(part);
                    pages += PdfPages(part, false);
                }
                if (job.Cancelled) return;
                PdfLite.Merge(parts, outPath, k => { if (job.Cancelled) throw new OperationCanceledException(); job.Part = k; job.Ping(); });
                if (PdfPages(outPath, true) != pages) throw new InvalidDataException(Lang.T("Hasilnya tidak lolos pemeriksaan"));
            }
            finally { foreach (var t in temps) TryDelete(t); }
        }
    }
}
