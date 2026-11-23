using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
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
            const int Modes = 7;
            readonly int[][] seq = new int[Modes][];
            readonly double[][] dur = new double[Modes][];

            public int Count { get { return Art.Count; } }

            public static string Folder { get { return Path.Combine(Settings.Dir, "pets"); } }

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

            public static PetSheet BuiltIn(string kind, string color)
            {
                var s = new PetSheet { W = PetArt.Grid, H = PetArt.Grid };
                int[] pal = PetArt.Palette(color);
                for (int c = 0; c < PetArt.Cells; c++) s.Art.Add(PetArt.Paint(kind == "dog" ? PetArt.Dog(c) : PetArt.Cat(c), pal));
                // Idle 1.5 fps, walk 8 fps at base speed, sit 0.8 fps, sleep breathing 0.6 fps
                s.Set(PetMode.Idle, new[] { 0, 1 }, new[] { 1 / 1.5, 1 / 1.5 });
                s.Set(PetMode.Walk, new[] { 2, 3, 4, 5 }, new[] { 0.125, 0.125, 0.125, 0.125 });
                s.Set(PetMode.Sit, new[] { 6, 7 }, new[] { 1.25, 1.25 });
                s.Set(PetMode.Sleep, new[] { 8, 9 }, new[] { 1 / 0.6, 1 / 0.6 });
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

                Dictionary<string, object> root;
                try { root = new JavaScriptSerializer().DeserializeObject(File.ReadAllText(json)) as Dictionary<string, object>; }
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
                int[] image = ReadImage(png, out iw, out ih);
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

            static int[] ReadImage(string png, out int w, out int h)
            {
                try
                {
                    using (var ms = new MemoryStream(File.ReadAllBytes(png)))
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

        // Placeholder pixel animals on a 16 px grid, outlined. Cells: idle 0-1, walk 2-5, sit 6-7, sleep 8-9.
        static class PetArt
        {
            public const int Grid = 16, Cells = 10;
            const byte Fur = 1, Stripe = 2, Dark = 3, Nose = 4, Light = 5;

            // ARGB by index: clear, fur, stripe, outline and eyes, nose, sleep mark
            public static int[] Palette(string color)
            {
                uint fur = 0xFFF0A35E, stripe = 0xFFC9773A, dark = 0xFF2A1E17;
                switch (color)
                {
                    case "brown": fur = 0xFFB98250; stripe = 0xFF8A5A33; break;
                    case "gray": fur = 0xFFAEB0BA; stripe = 0xFF7F8290; break;
                    case "black": fur = 0xFF4C4654; stripe = 0xFF342F3A; dark = 0xFF141017; break;
                    case "cream": fur = 0xFFF4E4C4; stripe = 0xFFD9BF8C; break;
                }
                return new[] { 0, (int)fur, (int)stripe, (int)dark, unchecked((int)0xFFE88A8A), unchecked((int)0xFFF5F1EA) };
            }

            // Opaque or clear pixels only, so the result is already premultiplied
            public static int[] Paint(byte[] grid, int[] palette)
            {
                byte[] g = Outline(grid);
                var px = new int[Grid * Grid];
                for (int i = 0; i < px.Length; i++) px[i] = palette[g[i]];
                return px;
            }

            static readonly int[] LegsA = { 4, 2, 6, 2, 9, 2, 11, 2 }, LegsB = { 3, 2, 7, 1, 8, 2, 12, 1 }, LegsC = { 5, 1, 6, 2, 10, 1, 11, 2 };

            public static byte[] Cat(int c)
            {
                var g = new byte[Grid * Grid];
                switch (c)
                {
                    case 0: case 2: case 4: CatStand(g, LegsA, 0); break;
                    case 1: CatStand(g, LegsA, 1); break;
                    case 3: CatStand(g, LegsB, 1); break;
                    case 5: CatStand(g, LegsC, 1); break;
                    case 6: case 7: CatSit(g, c - 6); break;
                    default: CatSleep(g, c == 9 ? 1 : 0); break;
                }
                return g;
            }

            public static byte[] Dog(int c)
            {
                var g = new byte[Grid * Grid];
                switch (c)
                {
                    case 0: case 2: case 4: DogStand(g, LegsA, 0); break;
                    case 1: DogStand(g, LegsA, 1); break;
                    case 3: DogStand(g, LegsB, 1); break;
                    case 5: DogStand(g, LegsC, 1); break;
                    case 6: case 7: DogSit(g, c - 6); break;
                    default: DogSleep(g, c == 9 ? 1 : 0); break;
                }
                return g;
            }

            // legs: x and height pairs; a height of 1 is a raised paw
            static void CatStand(byte[] g, int[] legs, int tail)
            {
                Box(g, 3, 8, 9, 4, Fur);
                Box(g, 10, 4, 5, 4, Fur);
                Dot(g, 10, 3, Fur); Dot(g, 14, 3, Fur);
                Dot(g, 13, 5, Dark); Dot(g, 14, 6, Nose);
                Dot(g, 12, 4, Stripe); Dot(g, 5, 8, Stripe); Dot(g, 7, 8, Stripe); Dot(g, 9, 8, Stripe);
                Legs(g, legs);
                if (tail == 0) { Dot(g, 2, 8, Fur); Dot(g, 1, 7, Fur); Dot(g, 1, 6, Fur); Dot(g, 2, 5, Fur); }
                else { Dot(g, 2, 9, Fur); Dot(g, 1, 8, Fur); Dot(g, 1, 7, Fur); Dot(g, 1, 6, Fur); }
            }

            static void CatSit(byte[] g, int tail)
            {
                Box(g, 5, 7, 6, 5, Fur);
                Box(g, 4, 10, 2, 4, Fur);
                Box(g, 6, 12, 3, 2, Fur);
                Box(g, 10, 12, 1, 2, Fur);
                Box(g, 9, 3, 5, 4, Fur);
                Dot(g, 9, 2, Fur); Dot(g, 13, 2, Fur);
                Dot(g, 12, 4, Dark); Dot(g, 13, 5, Nose);
                Dot(g, 11, 3, Stripe); Dot(g, 6, 7, Stripe); Dot(g, 8, 7, Stripe);
                Box(g, 1, 13, 3, 1, Fur);
                Dot(g, tail == 0 ? 1 : 2, 12, Fur);
            }

            static void CatSleep(byte[] g, int breath)
            {
                int top = 10 + breath;
                Box(g, 3, top, 9, 14 - top, Fur);
                Box(g, 10, 9, 4, 4, Fur);
                Dot(g, 10, 8, Fur); Dot(g, 13, 8, Fur);
                Box(g, 11, 10, 2, 1, Dark);
                Dot(g, 5, top, Stripe); Dot(g, 7, top, Stripe);
                Box(g, 2, 12, 1, 2, Fur);
                if (breath == 1) Zee(g);
            }

            // Floppy ear, longer snout, patch on the back, tail up and wagging
            static void DogStand(byte[] g, int[] legs, int tail)
            {
                Box(g, 3, 8, 9, 4, Fur);
                Box(g, 10, 4, 4, 4, Fur);
                Box(g, 13, 6, 2, 2, Fur);
                Dot(g, 14, 6, Dark); Dot(g, 12, 5, Dark);
                Box(g, 10, 4, 1, 4, Stripe); Dot(g, 11, 4, Stripe);
                Box(g, 5, 9, 2, 2, Stripe);
                Legs(g, legs);
                if (tail == 0) { Dot(g, 2, 8, Fur); Dot(g, 2, 7, Fur); Dot(g, 1, 6, Fur); Dot(g, 1, 5, Fur); }
                else { Dot(g, 2, 8, Fur); Dot(g, 2, 7, Fur); Dot(g, 2, 6, Fur); Dot(g, 3, 5, Fur); }
            }

            static void DogSit(byte[] g, int tail)
            {
                Box(g, 5, 7, 6, 5, Fur);
                Box(g, 4, 10, 2, 4, Fur);
                Box(g, 6, 12, 3, 2, Fur);
                Box(g, 10, 12, 1, 2, Fur);
                Box(g, 9, 3, 4, 4, Fur);
                Box(g, 12, 5, 2, 2, Fur);
                Dot(g, 13, 5, Dark); Dot(g, 11, 4, Dark);
                Box(g, 9, 3, 1, 4, Stripe); Dot(g, 10, 3, Stripe);
                Box(g, 6, 8, 2, 2, Stripe);
                Box(g, 1, 13, 3, 1, Fur);
                Dot(g, tail == 0 ? 1 : 2, 12, Fur);
            }

            static void DogSleep(byte[] g, int breath)
            {
                int top = 10 + breath;
                Box(g, 3, top, 9, 14 - top, Fur);
                Box(g, 10, 10, 4, 3, Fur);
                Box(g, 13, 11, 2, 2, Fur);
                Dot(g, 14, 11, Dark); Dot(g, 12, 11, Dark);
                Box(g, 10, 10, 1, 3, Stripe);
                Dot(g, 5, top, Stripe); Dot(g, 6, top, Stripe);
                Box(g, 1, 13, 2, 1, Fur);
                if (breath == 1) Zee(g);
            }

            static void Legs(byte[] g, int[] legs)
            {
                for (int i = 0; i < legs.Length; i += 2) Box(g, legs[i], 12, 1, legs[i + 1], Fur);
            }

            static void Zee(byte[] g)
            {
                Box(g, 12, 3, 3, 1, Light);
                Dot(g, 13, 4, Light);
                Box(g, 12, 5, 3, 1, Light);
            }

            static void Box(byte[] g, int x, int y, int w, int h, byte c)
            {
                for (int yy = y; yy < y + h; yy++)
                    for (int xx = x; xx < x + w; xx++) Dot(g, xx, yy, c);
            }

            static void Dot(byte[] g, int x, int y, byte c)
            {
                if (x >= 0 && y >= 0 && x < Grid && y < Grid) g[y * Grid + x] = c;
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
