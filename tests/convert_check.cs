using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Text;
using System.Threading;

namespace Deskmate
{
    // Runs real conversions on generated files: separate vs merged PDFs, split, stitch, options
    public static class ConvertCheck
    {
        static void Assert(bool condition, string message)
        {
            if (!condition) throw new Exception(message);
        }

        static string Png(string dir, string name, int w, int h, Color c)
        {
            string path = Path.Combine(dir, name);
            using (var b = new Bitmap(w, h))
            {
                using (var g = Graphics.FromImage(b)) g.Clear(c);
                b.Save(path, ImageFormat.Png);
            }
            return path;
        }

        static ConvJob Run(List<string> files, string id, ConvOpts opts)
        {
            var target = Converter.Targets(files).Find(t => t.Id == id);
            Assert(target != null, "Target " + id + " should be offered");
            var job = new ConvJob { Files = files, Target = target, Opts = opts ?? new ConvOpts() };
            Converter.Start(job);
            var until = DateTime.UtcNow.AddMinutes(2);
            while (!job.Finished && DateTime.UtcNow < until) Thread.Sleep(50);
            Assert(job.Finished, id + " did not finish");
            Assert(job.Errors.Count == 0, id + " failed: " + string.Join("; ", job.Errors.ToArray()));
            return job;
        }

        static int PdfPages(string path) { return PdfLite.PageCount(path); }

        public static string Run()
        {
            string root = Path.Combine(Path.GetTempPath(), "deskmate-convert-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            var done = new List<string>();
            try
            {
                string a = Png(root, "page2.png", 400, 300, Color.Red);
                string b = Png(root, "page10.png", 200, 500, Color.Blue);
                var two = new List<string> { a, b };

                // Natural order: page2 before page10
                Assert(Converter.CompareNatural("page2.png", "page10.png") < 0, "Natural sort should put page2 first");

                var targets = Converter.Targets(two);
                var pdf = targets.Find(t => t.Id == "pdf");
                Assert(pdf != null && pdf.Label != "PDF", "Many images: the per-file PDF chip should say so");
                foreach (var id in new[] { "pdf1", "stitch", "gifanim", "zip" })
                    Assert(targets.Exists(t => t.Id == id), "Many images should offer " + id);
                Assert(!Converter.Targets(new List<string> { a }).Exists(t => t.Id == "pdf1" || t.Id == "stitch"), "One image has nothing to combine");
                done.Add("targets");

                // Separate: one PDF per picture
                var sep = Run(two, "pdf", null);
                Assert(sep.Outputs.Count == 2, "PDF per file should make two PDFs, made " + sep.Outputs.Count);
                foreach (var o in sep.Outputs) Assert(PdfPages(o) == 1, "Each separate PDF should hold one page");
                done.Add("pdf per file");

                // Combined, page sized to the picture
                var merged = Run(two, "pdf1", new ConvOpts { Page = 2 });
                Assert(merged.Outputs.Count == 1 && PdfPages(merged.Outputs[0]) == 2, "Combined PDF should hold both pages");
                string text = Encoding.ASCII.GetString(File.ReadAllBytes(merged.Outputs[0]));
                Assert(text.Contains("/MediaBox [0 0 300 225]"), "Fit-image page should match the picture at 96 dpi");
                done.Add("combine pdf");

                // Letter pages
                var letter = Run(new List<string> { a }, "pdf", new ConvOpts { Page = 1, OutDir = Directory.CreateDirectory(Path.Combine(root, "letter")).FullName });
                string lt = Encoding.ASCII.GetString(File.ReadAllBytes(letter.Outputs[0]));
                Assert(lt.Contains("/MediaBox [0 0 792 612]"), "Wide picture on Letter should be landscape");
                Assert(Path.GetDirectoryName(letter.Outputs[0]).EndsWith("letter"), "Output folder option should be honoured");
                done.Add("letter + output folder");

                // Split back apart
                var split = Run(new List<string> { merged.Outputs[0] }, "split", null);
                Assert(split.Outputs.Count == 1 && Directory.Exists(split.Outputs[0]), "Split should make a folder");
                var pieces = Directory.GetFiles(split.Outputs[0], "*.pdf");
                Assert(pieces.Length == 2, "Split should make two PDFs");
                foreach (var piece in pieces) Assert(PdfPages(piece) == 1, "Each split PDF should be one page");
                done.Add("split");

                // Merge a picture with a PDF
                var mix = Run(new List<string> { a, merged.Outputs[0] }, "pdf1", null);
                Assert(PdfPages(mix.Outputs[0]) == 3, "Picture plus two-page PDF should merge to three pages");
                done.Add("merge mixed");

                // Stitch: stacked, widest wins
                var st = Run(two, "stitch", null);
                using (var img = Image.FromFile(st.Outputs[0])) Assert(img.Width == 400 && img.Height == 800, "Stitched image should be 400x800, got " + img.Width + "x" + img.Height);
                done.Add("stitch");

                // Scaled JPG
                var half = Run(new List<string> { a }, "jpg", new ConvOpts { Scale = 2 });
                using (var img = Image.FromFile(half.Outputs[0])) Assert(img.Width == 200 && img.Height == 150, "50% should halve the picture");
                done.Add("scale");

                if (Converter.Tool("ffmpeg") != null)
                {
                    var gif = Run(two, "gifanim", null);
                    using (var img = Image.FromFile(gif.Outputs[0]))
                        Assert(img.GetFrameCount(new FrameDimension(img.FrameDimensionsList[0])) == 2, "Animated GIF should have two frames");
                    done.Add("animated gif");

                    string ff = Converter.Tool("ffmpeg");
                    string w1 = Path.Combine(root, "tone1.wav"), w2 = Path.Combine(root, "tone2.wav"), mov = Path.Combine(root, "clip.mov");
                    Ff(ff, "-f lavfi -i sine=frequency=440:duration=1 -ar 44100 \"" + w1 + "\"");
                    Ff(ff, "-f lavfi -i sine=frequency=660:duration=1 -ar 22050 -ac 2 \"" + w2 + "\"");
                    Ff(ff, "-f lavfi -i testsrc=size=320x240:rate=10:duration=1 -f lavfi -i sine=duration=1 -c:v libx264 -pix_fmt yuv420p -c:a aac -shortest \"" + mov + "\"");
                    var joined = Run(new List<string> { w1, w2 }, "audiocat", null);
                    Assert(joined.Outputs.Count == 1 && new FileInfo(joined.Outputs[0]).Length > 1000, "Joined audio should exist");
                    done.Add("join audio");

                    var sw = Stopwatch.StartNew();
                    var mp4 = Run(new List<string> { mov }, "mp4", null);
                    Assert(new FileInfo(mp4.Outputs[0]).Length > 1000, "MOV to MP4 should make a file");
                    done.Add("mov->mp4 " + sw.ElapsedMilliseconds + "ms");

                    var small = Run(new List<string> { mov }, "mp4", new ConvOpts { Res = 3, Quality = 2, OutDir = Directory.CreateDirectory(Path.Combine(root, "small")).FullName });
                    Assert(File.Exists(small.Outputs[0]), "Re-encoded MP4 should exist");
                    done.Add("mp4 480p");
                }
                return "Convert: " + string.Join(", ", done.ToArray());
            }
            finally
            {
                try { Directory.Delete(root, true); } catch (Exception) { }
            }
        }

        static void Ff(string exe, string args)
        {
            using (var p = Process.Start(new ProcessStartInfo(exe, "-hide_banner -nostdin -y " + args) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardError = true }))
            {
                p.StandardError.ReadToEnd();
                p.WaitForExit();
                Assert(p.ExitCode == 0, "ffmpeg fixture failed: " + args);
            }
        }
    }
}
