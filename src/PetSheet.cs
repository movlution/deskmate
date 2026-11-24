using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Web.Script.Serialization;

namespace Deskmate
{
    partial class Island
    {
        // Frames and their timing for one pet: built-in placeholder art, or a sprite sheet
        // exported from Aseprite (File > Export Sprite Sheet, JSON data, Array or Hash).
        sealed class PetSheet
        {
            public const int MaxFrames = 128, MaxSide = 256, MaxImage = 4096, MaxArtPx = 1 << 20;
            public static readonly string[] Colors = { "orange", "brown", "gray", "black", "cream" };

            // Art px per frame, the lowest opaque row (it stands on the ground), and the highest (emotes sit above it)
            public int W, H, Base, Top;
            // Premultiplied ARGB, W*H each, facing right
            public readonly List<int[]> Art = new List<int[]>();
            const int Modes = (int)PetMode.Scratch + 1;
            readonly int[][] seq = new int[Modes][];
            readonly double[][] dur = new double[Modes][];

            public int Count { get { return Art.Count; } }

            public static string Folder { get { return Path.Combine(Settings.Dir, "pets"); } }
            public static string CacheFolder { get { return Path.Combine(Settings.Dir, "pet-cache"); } }

            public static string ComputeHashBytes(byte[] jsonBytes, byte[] pngBytes)
            {
                if (jsonBytes == null || pngBytes == null) return "";
                using (var sha = SHA256.Create())
                {
                    sha.TransformBlock(jsonBytes, 0, jsonBytes.Length, null, 0);
                    sha.TransformFinalBlock(pngBytes, 0, pngBytes.Length);
                    var hash = sha.Hash;
                    var sb = new StringBuilder(16);
                    for (int i = 0; i < 8 && i < hash.Length; i++) sb.Append(hash[i].ToString("x2"));
                    return sb.ToString();
                }
            }

            public static string ComputeHash(string dir, out long totalBytes)
            {
                totalBytes = 0;
                if (string.IsNullOrEmpty(dir)) return "";
                string json = Path.Combine(dir, "pet.json"), png = Path.Combine(dir, "sheet.png");
                if (!File.Exists(json) || !File.Exists(png)) return "";
                var ji = new FileInfo(json);
                var pi = new FileInfo(png);
                long jl = ji.Length, pl = pi.Length;
                totalBytes = jl + pl;
                if (jl > (1 << 20) || pl > (8 << 20)) return "";
                // Hashing reads up to 9 MB on the UI thread; do it again only when a file changed
                string stamp = jl + "|" + pl + "|" + ji.LastWriteTimeUtc.Ticks + "|" + pi.LastWriteTimeUtc.Ticks;
                string key = Path.GetFullPath(dir).ToLowerInvariant();
                lock (hashes)
                {
                    KeyValuePair<string, string> hit;
                    if (hashes.TryGetValue(key, out hit) && hit.Key == stamp) return hit.Value;
                }
                string hash = ComputeHashBytes(File.ReadAllBytes(json), File.ReadAllBytes(png));
                lock (hashes)
                {
                    if (hashes.Count >= 32) hashes.Clear();
                    hashes[key] = new KeyValuePair<string, string>(stamp, hash);
                }
                return hash;
            }

            // folder -> (size and write-time stamp, hash)
            static readonly Dictionary<string, KeyValuePair<string, string>> hashes =
                new Dictionary<string, KeyValuePair<string, string>>(StringComparer.Ordinal);

            // Frame shown t seconds into a mode; pace above 1 plays it faster
            public int Frame(PetMode m, double t, double pace)
            {
                int i;
                double left;
                Locate(m, t, pace, out i, out left);
                return seq[(int)m][i];
            }

            // Seconds until the frame changes
            public double NextChange(PetMode m, double t, double pace)
            {
                int i;
                double left;
                Locate(m, t, pace, out i, out left);
                return Math.Max(0.001, left);
            }

            // Frame indices for a mode, for tests and the atlas check
            public int[] Sequence(PetMode m) { return (int[])seq[(int)m].Clone(); }

            // Seconds for one pass through a mode's frames
            public double Length(PetMode m)
            {
                double total = 0;
                foreach (var s in dur[(int)m]) total += s;
                return total;
            }

            void Locate(PetMode m, double t, double pace, out int index, out double left)
            {
                var d = dur[(int)m];
                if (pace <= 0) pace = 1;
                double total = 0;
                foreach (var s in d) total += s / pace;
                t = t > 0 && total > 0 ? t % total : 0;
                for (int i = 0; i < d.Length; i++)
                {
                    double len = d[i] / pace;
                    if (t < len || i == d.Length - 1) { index = i; left = len - t; return; }
                    t -= len;
                }
                index = 0;
                left = 1;
            }

            void Set(PetMode m, int[] frames, double[] seconds)
            {
                seq[(int)m] = frames;
                dur[(int)m] = seconds;
            }

            // Missing poses borrow the nearest one; the ground row comes from every frame
            void Finish()
            {
                if (seq[(int)PetMode.Idle] == null) Set(PetMode.Idle, new[] { seq[(int)PetMode.Walk][0] }, new[] { 1.0 });
                if (seq[(int)PetMode.Sit] == null) Set(PetMode.Sit, seq[(int)PetMode.Idle], dur[(int)PetMode.Idle]);
                if (seq[(int)PetMode.Sleep] == null) Set(PetMode.Sleep, seq[(int)PetMode.Sit], dur[(int)PetMode.Sit]);
                // Reactions are optional tags: joy and surprise borrow standing, the stretch borrows sitting
                if (seq[(int)PetMode.Happy] == null) Set(PetMode.Happy, seq[(int)PetMode.Idle], dur[(int)PetMode.Idle]);
                if (seq[(int)PetMode.Surprised] == null) Set(PetMode.Surprised, seq[(int)PetMode.Idle], dur[(int)PetMode.Idle]);
                if (seq[(int)PetMode.Stretch] == null) Set(PetMode.Stretch, seq[(int)PetMode.Sit], dur[(int)PetMode.Sit]);
                if (seq[(int)PetMode.Held] == null) Set(PetMode.Held, seq[(int)PetMode.Sit], dur[(int)PetMode.Sit]);
                if (seq[(int)PetMode.Scratch] == null) Set(PetMode.Scratch, new[] { seq[(int)PetMode.Surprised][0] }, new[] { 0.4 });
                Top = H - 1;
                foreach (var a in Art)
                    for (int y = 0; y < Top; y++)
                    {
                        bool hit = false;
                        for (int x = 0; x < W && !hit; x++) hit = ((uint)a[y * W + x] >> 24) != 0;
                        if (hit) { Top = y; break; }
                    }
                Base = 0;
                foreach (var a in Art)
                    for (int y = H - 1; y > Base; y--)
                    {
                        bool hit = false;
                        for (int x = 0; x < W && !hit; x++) hit = ((uint)a[y * W + x] >> 24) != 0;
                        if (hit) { Base = y; break; }
                    }
            }

            // Row 0 faces right, row 1 is mirrored; f screen px per art px, nearest neighbour
            public int[] Atlas(int f)
            {
                int cw = W * f, ch = H * f, w = cw * Count;
                var px = new int[w * ch * 2];
                for (int c = 0; c < Count; c++)
                {
                    var a = Art[c];
                    for (int gy = 0; gy < H; gy++)
                        for (int gx = 0; gx < W; gx++)
                        {
                            int v = a[gy * W + gx];
                            if (v == 0) continue;
                            for (int row = 0; row < 2; row++)
                            {
                                int ox = c * cw + (row == 0 ? gx : W - 1 - gx) * f, oy = row * ch + gy * f;
                                for (int y = 0; y < f; y++)
                                    for (int x = 0; x < f; x++) px[(oy + y) * w + ox + x] = v;
                            }
                        }
                }
                return px;
            }

            // Ten built-in sheets at most (2 animals x 5 furs). Sharing one instance means a repeated
            // choice, a friend's identical pet, or a profile update never repaints art or rebuilds an atlas.
            static readonly Dictionary<string, PetSheet> builtIns = new Dictionary<string, PetSheet>(StringComparer.Ordinal);

            public static PetSheet BuiltIn(string kind, string color)
            {
                kind = kind == "dog" ? "dog" : "cat";
                // Unknown furs paint orange, so they share its sheet (and a peer can't grow the cache)
                if (color != "brown" && color != "gray" && color != "black" && color != "cream") color = "orange";
                string key = kind + "|" + color;
                lock (builtIns)
                {
                    PetSheet cached;
                    if (builtIns.TryGetValue(key, out cached)) return cached;
                    cached = PaintBuiltIn(kind, color);
                    builtIns[key] = cached;
                    return cached;
                }
            }

            static PetSheet PaintBuiltIn(string kind, string color)
            {
                var s = new PetSheet { W = PetArt.Grid, H = PetArt.Grid };
                int[] pal = PetArt.Palette(color);
                for (int c = 0; c < PetArt.Cells; c++) s.Art.Add(PetArt.Paint(kind == "dog" ? PetArt.Dog(c) : PetArt.Cat(c), pal));
                // Idle 1.5 fps, walk 8 fps at base speed, sit 0.8 fps, sleep breathing 0.6 fps
                s.Set(PetMode.Idle, new[] { 0, 1 }, new[] { 1 / 1.5, 1 / 1.5 });
                s.Set(PetMode.Walk, new[] { 2, 3, 4, 5 }, new[] { 0.125, 0.125, 0.125, 0.125 });
                s.Set(PetMode.Sit, new[] { 6, 7 }, new[] { 1.25, 1.25 });
                s.Set(PetMode.Sleep, new[] { 8, 9 }, new[] { 1 / 0.6, 1 / 0.6 });
                // Reactions have their own poses: a wagging smile, a startled jump, a long stretch,
                // dangling paws while carried, and a paw swipe that ends on its wind-up
                s.Set(PetMode.Happy, new[] { 10, 11 }, new[] { 0.18, 0.18 });
                s.Set(PetMode.Surprised, new[] { 12, 13 }, new[] { 0.14, 0.4 });
                s.Set(PetMode.Stretch, new[] { 14, 15 }, new[] { 0.45, 0.75 });
                s.Set(PetMode.Held, new[] { 16, 17 }, new[] { 0.25, 0.25 });
                s.Set(PetMode.Scratch, new[] { 18, 19, 18 }, new[] { 0.12, 0.16, 0.12 });
                s.Finish();
                return s;
            }

            // Folders under pets\ holding a pet.json
            public static List<string> Custom()
            {
                var list = new List<string>();
                try
                {
                    if (Directory.Exists(Folder))
                        foreach (var d in Directory.GetDirectories(Folder))
                            if (File.Exists(Path.Combine(d, "pet.json"))) list.Add(Path.GetFileName(d));
                }
                catch (Exception ex) { Log.Write("pets: " + ex.Message); }
                list.Sort(StringComparer.OrdinalIgnoreCase);
                return list;
            }

            // dir holds sheet.png and pet.json. Tags idle, walk, sit, sleep pick the poses; without
            // any tags every frame is the walk. Problems throw InvalidDataException with a Lang id.
            public static PetSheet Load(string dir)
            {
                string json = Path.Combine(dir, "pet.json"), png = Path.Combine(dir, "sheet.png");
                if (!File.Exists(json) || !File.Exists(png)) throw new InvalidDataException("butuh sheet.png dan pet.json");
                if (new FileInfo(json).Length > 1 << 20 || new FileInfo(png).Length > 8 << 20) throw new InvalidDataException("berkas terlalu besar");
                return LoadBytes(File.ReadAllBytes(json), File.ReadAllBytes(png));
            }

            public static PetSheet LoadBytes(byte[] jsonBytes, byte[] pngBytes)
            {
                if (jsonBytes == null || pngBytes == null || jsonBytes.Length == 0 || pngBytes.Length == 0)
                    throw new InvalidDataException("butuh sheet.png dan pet.json");
                if (jsonBytes.Length > 1 << 20 || pngBytes.Length > 8 << 20) throw new InvalidDataException("berkas terlalu besar");

                Dictionary<string, object> root;
                try { root = new JavaScriptSerializer().DeserializeObject(Encoding.UTF8.GetString(jsonBytes)) as Dictionary<string, object>; }
                catch (ArgumentException) { root = null; }
                catch (InvalidOperationException) { root = null; }
                if (root == null) throw new InvalidDataException("pet.json tidak terbaca");

                var frames = new List<Dictionary<string, object>>();
                object fo;
                root.TryGetValue("frames", out fo);
                var arr = fo as object[];
                var hash = fo as Dictionary<string, object>;
                if (arr != null) foreach (var o in arr) { var d = o as Dictionary<string, object>; if (d != null) frames.Add(d); }
                else if (hash != null) foreach (var o in hash.Values) { var d = o as Dictionary<string, object>; if (d != null) frames.Add(d); }
                if (frames.Count == 0 || frames.Count > MaxFrames) throw new InvalidDataException("jumlah frame tidak sesuai");

                int iw, ih;
                int[] image = ReadImageBytes(pngBytes, out iw, out ih);
                var s = new PetSheet();
                var secs = new double[frames.Count];
                for (int i = 0; i < frames.Count; i++)
                {
                    var fr = Obj(frames[i], "frame");
                    if (fr == null) throw new InvalidDataException("pet.json tidak terbaca");
                    int x = Int(fr, "x"), y = Int(fr, "y"), w = Int(fr, "w"), h = Int(fr, "h");
                    // Trimmed exports place the frame inside a larger source size
                    var src = Obj(frames[i], "sourceSize");
                    var off = Obj(frames[i], "spriteSourceSize");
                    int sw = src != null ? Int(src, "w") : w, sh = src != null ? Int(src, "h") : h;
                    int ox = off != null ? Int(off, "x") : 0, oy = off != null ? Int(off, "y") : 0;
                    if (i == 0) { s.W = sw; s.H = sh; }
                    if (sw != s.W || sh != s.H || sw < 1 || sh < 1 || sw > MaxSide || sh > MaxSide) throw new InvalidDataException("ukuran frame tidak sesuai");
                    if (w < 0 || h < 0 || x < 0 || y < 0 || x + w > iw || y + h > ih || ox < 0 || oy < 0 || ox + w > sw || oy + h > sh)
                        throw new InvalidDataException("frame di luar gambar");
                    var art = new int[sw * sh];
                    for (int yy = 0; yy < h; yy++)
                        for (int xx = 0; xx < w; xx++) art[(oy + yy) * sw + ox + xx] = Premultiply(image[(y + yy) * iw + x + xx]);
                    s.Art.Add(art);
                    object ms;
                    double msv = frames[i].TryGetValue("duration", out ms) ? Num(ms, 100) : 100;
                    secs[i] = Math.Max(0.02, Math.Min(10, msv / 1000));
                }
                if ((long)s.W * s.H * s.Count > MaxArtPx) throw new InvalidDataException("berkas terlalu besar");

                object mo, to = null;
                var meta = root.TryGetValue("meta", out mo) ? mo as Dictionary<string, object> : null;
                if (meta != null) meta.TryGetValue("frameTags", out to);
                var tags = to as object[];
                if (tags != null && tags.Length > 0)
                {
                    foreach (var t in tags)
                    {
                        var d = t as Dictionary<string, object>;
                        PetMode m;
                        if (d == null || !ModeOf(Str(d, "name"), out m)) continue;
                        int from = Int(d, "from"), last = Int(d, "to");
                        if (from < 0 || last >= s.Count || from > last) throw new InvalidDataException("frame di luar gambar");
                        var idx = Order(from, last, Str(d, "direction"));
                        var len = new double[idx.Length];
                        for (int k = 0; k < idx.Length; k++) len[k] = secs[idx[k]];
                        s.Set(m, idx, len);
                    }
                    if (s.seq[(int)PetMode.Walk] == null) throw new InvalidDataException("butuh tag walk");
                }
                else
                {
                    var all = new int[s.Count];
                    for (int k = 0; k < all.Length; k++) all[k] = k;
                    s.Set(PetMode.Walk, all, secs);
                }
                s.Finish();
                return s;
            }

            static int[] ReadImageBytes(byte[] pngBytes, out int w, out int h)
            {
                try
                {
                    using (var ms = new MemoryStream(pngBytes))
                    using (var img = new Bitmap(ms))
                    {
                        w = img.Width;
                        h = img.Height;
                        if (w > MaxImage || h > MaxImage) throw new InvalidDataException("berkas terlalu besar");
                        var px = new int[w * h];
                        // LockBits converts any source format to 32 bpp ARGB
                        var data = img.LockBits(new Rectangle(0, 0, w, h), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
                        try { for (int y = 0; y < h; y++) Marshal.Copy(IntPtr.Add(data.Scan0, y * data.Stride), px, y * w, w); }
                        finally { img.UnlockBits(data); }
                        return px;
                    }
                }
                catch (ArgumentException) { throw new InvalidDataException("sheet.png tidak terbaca"); }
                catch (OutOfMemoryException) { throw new InvalidDataException("sheet.png tidak terbaca"); }
                catch (ExternalException) { throw new InvalidDataException("sheet.png tidak terbaca"); }
            }

            static int Premultiply(int argb)
            {
                uint v = (uint)argb, a = v >> 24;
                if (a == 0) return 0;
                if (a == 255) return argb;
                uint r = ((v >> 16) & 255) * a / 255, g = ((v >> 8) & 255) * a / 255, b = (v & 255) * a / 255;
                return (int)((a << 24) | (r << 16) | (g << 8) | b);
            }

            static int[] Order(int from, int to, string direction)
            {
                var list = new List<int>();
                string dir = (direction ?? "").ToLowerInvariant();
                if (dir == "reverse") for (int i = to; i >= from; i--) list.Add(i);
                else
                {
                    for (int i = from; i <= to; i++) list.Add(i);
                    if (dir == "pingpong") for (int i = to - 1; i > from; i--) list.Add(i);
                }
                return list.ToArray();
            }

            static bool ModeOf(string name, out PetMode m)
            {
                switch ((name ?? "").Trim().ToLowerInvariant())
                {
                    case "idle": case "stand": case "diam": m = PetMode.Idle; return true;
                    case "walk": case "run": case "jalan": m = PetMode.Walk; return true;
                    case "sit": case "duduk": m = PetMode.Sit; return true;
                    case "sleep": case "tidur": m = PetMode.Sleep; return true;
                    case "happy": case "senang": m = PetMode.Happy; return true;
                    case "surprised": case "surprise": case "kaget": m = PetMode.Surprised; return true;
                    case "stretch": case "geliat": m = PetMode.Stretch; return true;
                    case "held": m = PetMode.Held; return true;
                    case "scratch": m = PetMode.Scratch; return true;
                }
                m = PetMode.Idle;
                return false;
            }

            static Dictionary<string, object> Obj(Dictionary<string, object> d, string key)
            {
                object o;
                return d.TryGetValue(key, out o) ? o as Dictionary<string, object> : null;
            }

            static string Str(Dictionary<string, object> d, string key)
            {
                object o;
                return d.TryGetValue(key, out o) && o != null ? o.ToString() : "";
            }

            static int Int(Dictionary<string, object> d, string key)
            {
                object o;
                if (!d.TryGetValue(key, out o)) throw new InvalidDataException("pet.json tidak terbaca");
                return (int)Math.Round(Num(o, double.NaN));
            }

            static double Num(object o, double fallback)
            {
                try { return Convert.ToDouble(o, CultureInfo.InvariantCulture); }
                catch (FormatException) { }
                catch (InvalidCastException) { }
                catch (OverflowException) { }
                if (double.IsNaN(fallback)) throw new InvalidDataException("pet.json tidak terbaca");
                return fallback;
            }
        }

        // Pixel animals on a 24 px grid, outlined. Cells: idle 0-1, walk 2-5, sit 6-7, sleep 8-9,
        // happy 10-11, surprised 12-13, stretch 14-15, held 16-17, scratch 18-19.
        static class PetArt
        {
            public const int Grid = 24, Cells = 20;
            const byte Dark = 3;

            // ARGB follows Ink: clear, coat, markings, outline, blush, cream, eyes, light, shadow.
            // A black coat gets amber eyes; dark eyes would vanish into it.
            public static int[] Palette(string color)
            {
                uint fur = 0xFFF0A35E, stripe = 0xFFC9773A, dark = 0xFF2A1E17, eye = 0;
                switch (color)
                {
                    case "brown": fur = 0xFFB98250; stripe = 0xFF8A5A33; break;
                    case "gray": fur = 0xFFAEB0BA; stripe = 0xFF7F8290; break;
                    case "black": fur = 0xFF4C4654; stripe = 0xFF342F3A; dark = 0xFF141017; eye = 0xFFE8C24E; break;
                    case "cream": fur = 0xFFF4E4C4; stripe = 0xFFD9BF8C; break;
                }
                if (eye == 0) eye = dark;
                return new[] { 0, (int)fur, (int)stripe, (int)dark, unchecked((int)0xFFE88A8A),
                    unchecked((int)0xFFF5DFBF), (int)eye, Shade(fur, 0xFFFFE6BC, 46), Shade(stripe, dark, 75) };
            }

            static int Shade(uint from, uint to, int mix)
            {
                uint rgb = 0xFF000000;
                for (int shift = 0; shift < 24; shift += 8)
                    rgb |= (((from >> shift & 255) * (256u - (uint)mix) + (to >> shift & 255) * (uint)mix) >> 8) << shift;
                return (int)rgb;
            }

            // Opaque or clear pixels only, so the result is already premultiplied
            public static int[] Paint(byte[] grid, int[] palette)
            {
                byte[] g = Outline(grid);
                var px = new int[Grid * Grid];
                for (int i = 0; i < px.Length; i++) px[i] = palette[g[i]];
                return px;
            }

            // Draw at native resolution; the letters are palette indices, not RGB values.
            // f coat, s markings, h highlight, b shadow, w muzzle/chest, n blush,
            // e eye, o dark detail. All grounded poses share outline row 19.
            const string Ink = ".fsonwehb";

            static readonly string[] CatHead = {
                "s......h", "fn....nhf", "ffh..hfff", "fhhsfhfff", "fhfsffeff",
                "fffffheff", "sfffwhwww", ".sfwwwwwn", "..bwwwww", "...bbww"
            };
            static readonly string[] DogHead = {
                "..hhh", ".hffffh", "hfffffff", "ssffhffff", "sssfhehff",
                "sbsffefwwo", "sbsffffwww", ".bbfwwwwww", "..bwwwwwo", "...bbww"
            };

            static readonly string[] CatBody = {
                "..hhsfhshff", ".hffsfhshfff", "ffffsfsfffffw",
                "fsfffsffffffw", "fsfffffsfffbw", ".bffffsffbbww", "..bbbbbbbb"
            };
            static readonly string[] DogBody = {
                "..hhhhhhfff", ".hffsssfffff", "fffsssssffffw",
                "fffssbsffffww", "ffffsbsffffbw", ".bfffffffbbww", "..bbbbbbbb"
            };
            static readonly string[] CatTail = {
                "sf...", "hff..", ".fs..", ".hfs.", "..ff.", "...fb", "....b"
            };
            static readonly string[] DogTail = {
                ".h...", "hff..", ".ff..", ".bff.", "..bfb", "...bb"
            };
            static readonly string[] Haunch = {
                "..hhf", ".hffff", "hffffff", "fffsfff", "fffsfbf", "bfffffb", ".bbffww"
            };

            public static byte[] Cat(int c) { return Animal(false, c); }
            public static byte[] Dog(int c) { return Animal(true, c); }

            static byte[] Animal(bool dog, int c)
            {
                var g = new byte[Grid * Grid];
                int phase = c & 1;
                var body = dog ? DogBody : CatBody;
                var head = dog ? DogHead : CatHead;
                var tail = dog ? DogTail : CatTail;

                if (c == 8 || c == 9)
                {
                    Stamp(g, 4, 12 - phase, body);
                    Stamp(g, 3, 16, ".bssss", "sfffffff", ".bbbffww");
                    Stamp(g, 12, 11, dog ? DogDoze : CatDoze);
                    Stamp(g, 15, 18, ".wwww"); // muzzle-level paws
                    if (phase == 1) Stamp(g, 17, 4, "wwww", "..w.", ".w..", "wwww");
                    return g;
                }
                if (c == 6 || c == 7)
                {
                    Stamp(g, 3 + phase, 16, ".hhf", "ffsbfb", ".bbbbfff");
                    Stamp(g, 8, 9, body);
                    Stamp(g, 7, 12, Haunch);
                    Leg(g, 16, 14, 0, 18, false);
                    Stamp(g, 12, 3, head);
                    return g;
                }
                if (c == 14 || c == 15)
                {
                    Stamp(g, 2, 5 - phase, tail);
                    Stamp(g, 5, 9 - phase, body);
                    Leg(g, 7, 14, -1, 18, false);
                    Leg(g, 9, 14, 0, 18, true);
                    Stamp(g, 13, 10, dog ? DogDoze : CatDoze);
                    Stamp(g, 14, 17, "bbbb", ".bwwwwww");
                    return g;
                }
                if (c == 16 || c == 17)
                {
                    Stamp(g, 2 + phase, 10, tail);
                    Stamp(g, 6, 8, body);
                    Leg(g, 7, 13, -1 + phase, 18, false);
                    Leg(g, 10, 13, 0, 17 + phase, true);
                    Leg(g, 14, 13, phase, 18, false);
                    Leg(g, 16, 13, 0, 17 - phase, true);
                    Stamp(g, 12, 3, head);
                    return g;
                }

                bool happy = c == 10 || c == 11;
                bool startled = c == 12 || c == 13;
                bool swipe = c == 18 || c == 19;
                int step = c >= 2 && c <= 5 ? c - 2 : -1;
                int bodyY = startled ? 9 : 10;
                int headY = happy || startled ? 2 : 3;
                Stamp(g, 1 + (phase == 1 && !startled ? 1 : 0), startled ? 4 + phase : happy ? 5 : 7, tail);
                if (startled)
                    Stamp(g, 5, 8, "h.h.h.h.h", "hhshhshhsh");
                Leg(g, 8, bodyY + 4, step == 0 ? -1 : step == 2 ? 1 : 0, step == 1 ? 16 : 18, true);
                Leg(g, 16, bodyY + 4, step == 0 ? 1 : step == 2 ? -1 : 0, step == 3 || happy || swipe ? 16 : 18, true);
                Leg(g, 6, bodyY + 5, step == 1 ? 1 : step == 3 ? -1 : 0, 18, false);
                if (!swipe)
                    Leg(g, 14, bodyY + 5, step == 1 ? -1 : step == 3 ? 1 : 0, happy && phase == 0 ? 17 : 18, false);
                Stamp(g, 5, bodyY, body);
                Stamp(g, 12, headY, head);
                if (happy || c == 1)
                {
                    int eyeX = dog ? 17 : 19, eyeY = headY + (dog ? 4 : 5);
                    Stamp(g, eyeX - 1, eyeY, happy ? "ofo" : "ooo", "fff");
                    if (happy && dog) Stamp(g, 21, headY + 9, "nn");
                }
                if (startled)
                {
                    int eyeX = dog ? 17 : 18, eyeY = headY + (dog ? 4 : 5);
                    Stamp(g, eyeX, eyeY, "we", "ee");
                }
                if (swipe)
                {
                    if (phase == 0) Stamp(g, 14, 13, ".hhffffw", "bffbbb");
                    else Stamp(g, 14, 14, "ff", ".ff", "..ff", "...bww");
                }
                return g;
            }

            static readonly string[] CatDoze = {
                "s.....h", "fh...hff", "fhfffffhh", "sfooffwww",
                ".bfswwwwn", "..bbwwww", "...bbbb"
            };
            static readonly string[] DogDoze = {
                "..hhh", ".hffffh", "ssfffffff", "sbsfooowwo",
                ".bsffffwww", "..bbwwwwwo", "...bbwww"
            };

            static void Leg(byte[] g, int x, int top, int stride, int ground, bool far)
            {
                for (int y = top; y < ground; y++)
                {
                    int dx = y > top ? stride : 0;
                    Stamp(g, x + dx, y, far ? "bs" : "fb");
                }
                Stamp(g, x + stride, ground, far ? "bb" : "ww");
            }

            static void Stamp(byte[] g, int x, int y, params string[] rows)
            {
                for (int j = 0; j < rows.Length; j++)
                    for (int i = 0; i < rows[j].Length; i++)
                        if (rows[j][i] != '.') g[(y + j) * Grid + x + i] = (byte)Ink.IndexOf(rows[j][i]);
            }

            static byte[] Outline(byte[] g)
            {
                var o = (byte[])g.Clone();
                for (int y = 0; y < Grid; y++)
                    for (int x = 0; x < Grid; x++)
                    {
                        if (g[y * Grid + x] != 0) continue;
                        if ((x > 0 && g[y * Grid + x - 1] != 0) || (x < Grid - 1 && g[y * Grid + x + 1] != 0) ||
                            (y > 0 && g[(y - 1) * Grid + x] != 0) || (y < Grid - 1 && g[(y + 1) * Grid + x] != 0))
                            o[y * Grid + x] = Dark;
                    }
                return o;
            }
        }
    }
}
