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

        // Shorter side capped for 1080p, 720p, 480p; even sizes for H.264
        static string ScaleFilter(ConvOpts o)
        {
            int side = o.ShortSide;
            if (side <= 0) return "";
            string n = side.ToString(CultureInfo.InvariantCulture);
            return " -vf \"scale='if(gt(iw,ih),-2,trunc(min(" + n + ",iw)/2)*2)':'if(gt(iw,ih),trunc(min(" + n + ",ih)/2)*2,-2)'\"";
        }

        static string FfmpegArgs(string id, ConvOpts o)
        {
            string h264 = "-c:v libx264 -preset veryfast -crf " + o.By(18, 23, 28) + ScaleFilter(o) + " -pix_fmt yuv420p -c:a aac -b:a " + o.By("192k", "160k", "128k");
            switch (id)
            {
                case "mp4": case "mov": return h264 + " -movflags +faststart";
                case "mkv": return h264;
                case "webm": return "-c:v libvpx-vp9 -crf " + o.By(28, 33, 38) + " -b:v 0 -row-mt 1 -deadline good -cpu-used 4" + ScaleFilter(o) + " -c:a libopus -b:a " + o.By("160k", "128k", "96k");
                case "gif":
                    string fps = o.By("15", "12", "10"), wide = o.By("640", "480", "320");
                    return "-an -vf \"fps=" + fps + ",scale='min(" + wide + ",iw)':-2:flags=lanczos,split[a][b];[a]palettegen[p];[b][p]paletteuse\" -loop 0";
                case "mp3": return "-vn -c:a libmp3lame -q:a " + o.By(0, 2, 5);
                case "m4a": return "-vn -c:a aac -b:a " + o.By("256k", "192k", "128k");
                case "wav": return "-vn -c:a pcm_s16le";
                case "flac": return "-vn -c:a flac";
                case "ogg": return "-vn -c:a libvorbis -q:a " + o.By(7, 5, 3);
                case "opus": return "-vn -c:a libopus -b:a " + o.By("192k", "128k", "96k");
                default: throw new NotSupportedException(id);
            }
        }

        // Video and audio: same streams in a new box when they fit, else encoded
        static void Media(ConvJob job, string src, string outPath, string id)
        {
            var o = job.Opts;
            if ((id == "mp4" || id == "mov" || id == "mkv") && o.Res == 0 && o.Quality < 2)
            {
                string v, a;
                Probe(src, out v, out a);
                if (CanRemux(id, v, a))
                {
                    // Instant and lossless; a picky file falls through to a real encode
                    try { Ffmpeg(job, src, outPath, "-map 0:v:0 -map 0:a:0? -c copy" + (id == "mkv" ? "" : " -movflags +faststart" + (v == "hevc" ? " -tag:v hvc1" : ""))); return; }
                    catch (Exception ex)
                    {
                        if (job.Cancelled) return;
                        Log.Write("convert remux: " + ex.Message);
                        TryDelete(outPath);
                    }
                }
            }
            Ffmpeg(job, src, outPath, FfmpegArgs(id, o));
        }

        static bool CanRemux(string id, string v, string a)
        {
            if (v == null) return false;
            if (id == "mkv") return v != "mjpeg" && v != "png";
            bool video = v == "h264" || v == "hevc" || v == "mpeg4" || (id == "mp4" && v == "av1");
            bool audio = a == null || a == "aac" || a == "mp3" || a == "alac" || a == "ac3" || a == "eac3";
            return video && audio;
        }

        // First video and audio codec, as ffmpeg names them
        static void Probe(string src, out string video, out string audio)
        {
            video = audio = null;
            string exe = Converter.Tool("ffmpeg");
            if (exe == null) return;
            try
            {
                using (var p = Launch(exe, "-hide_banner -nostdin -i " + Q(src), false))
                {
                    string err = p.StandardError.ReadToEnd();
                    if (!p.WaitForExit(20000)) { Kill(p); return; }
                    foreach (var line in err.Split('\n'))
                    {
                        if (line.IndexOf("Stream #", StringComparison.Ordinal) < 0 || line.IndexOf("attached pic", StringComparison.Ordinal) >= 0) continue;
                        foreach (var kind in new[] { "Video: ", "Audio: " })
                        {
                            int at = line.IndexOf(kind, StringComparison.Ordinal);
                            if (at < 0) continue;
                            string rest = line.Substring(at + kind.Length);
                            int end = rest.IndexOfAny(new[] { ' ', ',', '(' });
                            string codec = (end < 0 ? rest : rest.Substring(0, end)).Trim();
                            if (kind[0] == 'V' && video == null) video = codec;
                            if (kind[0] == 'A' && audio == null) audio = codec;
                        }
                    }
                }
            }
            catch (Exception ex) { Log.Write("convert probe: " + ex.Message); }
        }

        // Back to back, one MP3; ffmpeg evens out rates and channels
        static void JoinAudio(ConvJob job, string outPath)
        {
            var ins = new StringBuilder();
            var graph = new StringBuilder();
            for (int i = 0; i < job.Files.Count; i++)
            {
                ins.Append(i == 0 ? "" : " ").Append("-i ").Append(Q(job.Files[i]));
                graph.Append('[').Append(i).Append(":a:0]");
            }
            graph.Append("concat=n=").Append(job.Files.Count).Append(":v=0:a=1[out]");
            FfmpegIn(job, ins.ToString(), outPath, "-filter_complex " + Q(graph.ToString()) + " -map [out] -c:a libmp3lame -q:a " + job.Opts.By(0, 2, 5));
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

        static void Ffmpeg(ConvJob job, string src, string outPath, string args) { FfmpegIn(job, "-i " + Q(src), outPath, args); }

        // Inputs come ready, "-i a -i b"; their lengths add up for progress
        static void FfmpegIn(ConvJob job, string inputs, string outPath, string args)
        {
            string exe = Converter.Tool("ffmpeg");
            if (exe == null) throw new FileNotFoundException(Lang.T("ffmpeg tidak ditemukan"));
            string line = "-hide_banner -nostdin -y " + inputs + " " + args + " -progress pipe:1 -nostats " + Q(outPath);
            using (var p = Launch(exe, line, true))
            {
                if (job != null) job.Proc = p;
                double total = 0;
                bool outputs = false;
                var tail = new Queue<string>();
                var err = new Thread(delegate ()
                {
                    string l;
                    while ((l = p.StandardError.ReadLine()) != null)
                    {
                        if (l.StartsWith("Output #", StringComparison.Ordinal)) outputs = true;
                        int at = l.IndexOf("Duration: ");
                        TimeSpan ts;
                        if (at >= 0 && !outputs && TimeSpan.TryParse(l.Substring(at + 10, Math.Min(11, l.Length - at - 10)), CultureInfo.InvariantCulture, out ts)) total += ts.TotalSeconds;
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

        // Slides and documents: through PDF, then a picture per page
        static void OfficeToImages(ConvJob job, string src, string dir, string name, string id)
        {
            string tmp = TempPath(".pdf");
            try
            {
                Office(job, src, tmp, "pdf");
                if (!job.Cancelled) PdfToImages(job, tmp, dir, name, id);
            }
            finally { TryDelete(tmp); }
        }

        // Every page its own PDF, in a folder named after the file
        static void SplitPdf(ConvJob job, string src, string dir, string name)
        {
            if (KindOf(src) != ConvKind.Pdf) throw new NotSupportedException(Path.GetExtension(src));
            string outDir = UniqueDir(dir, name);
            Directory.CreateDirectory(outDir);
            try
            {
                var made = PdfLite.Split(src, i => Path.Combine(outDir, name + "-" + (i + 1) + ".pdf"), k => { if (job.Cancelled) throw new OperationCanceledException(); job.Part = k; job.Ping(); });
                // Windows must read the first one back as a single page
                if (made.Count == 0 || PdfPages(made[0], true) != 1) throw new InvalidDataException(Lang.T("Hasilnya tidak lolos pemeriksaan"));
            }
            catch (Exception) { TryDelete(outDir); throw; }
            if (job.Cancelled) { TryDelete(outDir); return; }
            lock (job.Outputs) job.Outputs.Add(outDir);
        }

        // Images and documents become pages first, then one PDF
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
                    var kind = KindOf(part);
                    if (kind != ConvKind.Pdf)
                    {
                        part = TempPath(".pdf");
                        temps.Add(part);
                        if (kind == ConvKind.Image) ImagesToPdf(job, new List<string> { job.Files[i] }, part, false);
                        else if (kind == ConvKind.Markdown) MarkdownToPdf(job, job.Files[i], part);
                        else Office(job, job.Files[i], part, "pdf");
                        if (job.Cancelled) return;
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
