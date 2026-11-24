using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Runtime.InteropServices;
using System.Text;

namespace Deskmate
{
    // Rewrites, shrinks, and merges PDFs; no outside tools
    static class PdfLite
    {
        // Built output: objects numbered from 1
        class Doc
        {
            public readonly List<object> Objs = new List<object>();
            public int Add(object o) { Objs.Add(o); return Objs.Count; }
            public void Put(int num, object o) { Objs[num - 1] = o; }
        }

        // Deep copy into the new numbering; a queue, not recursion
        class Importer
        {
            readonly PdfFile src;
            readonly Doc doc;
            readonly Queue<KeyValuePair<int, object>> pending = new Queue<KeyValuePair<int, object>>();
            public readonly Dictionary<int, int> Map = new Dictionary<int, int>();

            public Importer(PdfFile src, Doc doc) { this.src = src; this.doc = doc; }

            public object Ref(PdfRef r)
            {
                int n;
                if (Map.TryGetValue(r.Num, out n)) return n == 0 ? (object)new PdfRaw("null") : new PdfRef(n, 0);
                var target = src.Get(r.Num);
                if (target == null) { Map[r.Num] = 0; return new PdfRaw("null"); }
                n = doc.Add(null);
                Map[r.Num] = n;
                pending.Enqueue(new KeyValuePair<int, object>(n, target));
                return new PdfRef(n, 0);
            }

            // Long chains (outlines, page trees) stay off the stack
            public void Drain()
            {
                while (pending.Count > 0)
                {
                    var kv = pending.Dequeue();
                    doc.Put(kv.Key, Copy(kv.Value));
                }
            }

            public object Copy(object v)
            {
                var r = v as PdfRef;
                if (r != null) return Ref(r);
                var d = v as PdfDict;
                if (d != null)
                {
                    var c = new PdfDict();
                    foreach (var kv in d.Items) c.Items.Add(new KeyValuePair<string, object>(kv.Key, Copy(kv.Value)));
                    return c;
                }
                var l = v as List<object>;
                if (l != null)
                {
                    var c = new List<object>(l.Count);
                    foreach (var o in l) c.Add(Copy(o));
                    return c;
                }
                var s = v as PdfStream;
                if (s != null) return new PdfStream { Dict = (PdfDict)Copy(s.Dict), Data = s.Data };
                return v;
            }
        }

        public class Result
        {
            public long Before, After;
            public int Images;
        }

        // Smaller copy: images resampled, objects packed
        public static Result Compress(string src, string dst, int maxSide, long quality, Action<float> progress)
        {
            var file = PdfFile.Open(src);
            if (file.Encrypted) throw new InvalidOperationException(Lang.T("PDF dikunci kata sandi"));
            var doc = new Doc();
            var imp = new Importer(file, doc);
            var root = imp.Copy(file.Trailer.Get("Root"));
            object info = file.Trailer.Has("Info") ? imp.Copy(file.Trailer.Get("Info")) : null;
            imp.Drain();
            var res = new Result { Before = new FileInfo(src).Length };
            // Masks stay exact: never JPEG them
            var masks = new HashSet<int>();
            foreach (var o in doc.Objs)
            {
                var s = o as PdfStream;
                if (s == null) continue;
                foreach (var key in new[] { "SMask", "Mask" }) { var mr = s.Dict.Get(key) as PdfRef; if (mr != null) masks.Add(mr.Num); }
            }
            int total = 0, done = 0;
            foreach (var o in doc.Objs) if (IsImage(o)) total++;
            for (int i = 0; i < doc.Objs.Count; i++)
            {
                var s = doc.Objs[i] as PdfStream;
                if (s == null) continue;
                if (IsImage(s))
                {
                    if (!masks.Contains(i + 1) && Shrink(doc, s, maxSide, quality)) res.Images++;
                    done++;
                    if (progress != null) progress(0.9f * done / Math.Max(1, total));
                }
                else Pack(s);
            }
            Write(dst, doc, root, info, file.Trailer.Get("ID"), file.Version);
            res.After = new FileInfo(dst).Length;
            return res;
        }

        // One PDF from many, pages in order
        public static void Merge(IList<string> files, string dst, Action<float> progress)
        {
            var doc = new Doc();
            int pagesNum = doc.Add(null);
            var kids = new List<object>();
            string version = "1.4";
            for (int f = 0; f < files.Count; f++)
            {
                var file = PdfFile.Open(files[f]);
                if (file.Encrypted) throw new InvalidOperationException(Path.GetFileName(files[f]) + ": " + Lang.T("PDF dikunci kata sandi"));
                if (string.CompareOrdinal(file.Version, version) > 0) version = file.Version;
                var imp = new Importer(file, doc);
                var pages = file.Pages();
                // Old tree nodes point at the new one
                MapTree(file, file.Root.Get("Pages"), imp, pagesNum, pages);
                var rootRef = file.Trailer.Get("Root") as PdfRef;
                if (rootRef != null) imp.Map[rootRef.Num] = 0;
                foreach (var pg in pages)
                {
                    var made = imp.Ref(pg.Key) as PdfRef;
                    if (made == null) continue;
                    imp.Drain();
                    var dict = doc.Objs[made.Num - 1] as PdfDict;
                    if (dict == null) continue;
                    foreach (var key in PdfFile.Inherited)
                        if (!dict.Has(key) && pg.Value.Has(key)) dict.Set(key, imp.Copy(pg.Value.Get(key)));
                    imp.Drain();
                    dict.Set("Parent", new PdfRef(pagesNum, 0));
                    kids.Add(made);
                }
                if (progress != null) progress(0.9f * (f + 1) / files.Count);
            }
            var pagesDict = new PdfDict();
            pagesDict.Set("Type", new PdfName("Pages"));
            pagesDict.Set("Kids", kids);
            pagesDict.Set("Count", new PdfRaw(kids.Count.ToString(CultureInfo.InvariantCulture)));
            doc.Put(pagesNum, pagesDict);
            var cat = new PdfDict();
            cat.Set("Type", new PdfName("Catalog"));
            cat.Set("Pages", new PdfRef(pagesNum, 0));
            int catNum = doc.Add(cat);
            foreach (var o in doc.Objs) { var s = o as PdfStream; if (s != null && !IsImage(s)) Pack(s); }
            Write(dst, doc, new PdfRef(catNum, 0), null, null, version);
        }

        static void MapTree(PdfFile file, object node, Importer imp, int pagesNum, List<KeyValuePair<PdfRef, PdfDict>> pages)
        {
            var leaves = new HashSet<int>();
            foreach (var pg in pages) leaves.Add(pg.Key.Num);
            var stack = new Stack<object>();
            stack.Push(node);
            var seen = new HashSet<int>();
            while (stack.Count > 0)
            {
                var r = stack.Pop() as PdfRef;
                if (r == null || !seen.Add(r.Num) || leaves.Contains(r.Num)) continue;
                imp.Map[r.Num] = pagesNum;
                var d = file.Resolve(r) as PdfDict;
                var kids = d != null ? file.Resolve(d.Get("Kids")) as List<object> : null;
                if (kids != null) foreach (var k in kids) stack.Push(k);
            }
        }

        public static int PageCount(string path)
        {
            var f = PdfFile.Open(path);
            return f.Pages().Count;
        }

        static bool IsImage(object o)
        {
            var s = o as PdfStream;
            if (s == null) return false;
            var st = s.Dict.Get("Subtype") as PdfName;
            return st != null && st.Name == "Image";
        }

        // Plain streams get Flate; metadata stays readable
        static void Pack(PdfStream s)
        {
            if (s.Dict.Has("Filter") || s.Data.Length < 64) return;
            var type = s.Dict.Get("Type") as PdfName;
            if (type != null && (type.Name == "Metadata" || type.Name == "XRef")) return;
            var z = Deflate(s.Data);
            if (z.Length >= s.Data.Length) return;
            s.Data = z;
            s.Dict.Set("Filter", new PdfName("FlateDecode"));
            s.Dict.Remove("DecodeParms");
        }

        // Photos: smaller and JPEG; masks, CMYK, odd ones untouched
        static bool Shrink(Doc doc, PdfStream s, int maxSide, long quality)
        {
            var d = s.Dict;
            if (s.Data.Length < 24 * 1024) return false;
            if (Raw(Deref(doc, d.Get("ImageMask"))) == "true" || d.Has("Decode") || d.Has("Mask")) return false;
            if (PdfLexer.Long(Deref(doc, d.Get("BitsPerComponent"))) != 8) return false;
            var smask = Deref(doc, d.Get("SMask")) as PdfStream;
            if (smask != null && smask.Dict.Has("Matte")) return false;
            int comps = Components(doc, Deref(doc, d.Get("ColorSpace")));
            if (comps != 1 && comps != 3) return false;
            int w = (int)PdfLexer.Long(Deref(doc, d.Get("Width"))), h = (int)PdfLexer.Long(Deref(doc, d.Get("Height")));
            if (w <= 0 || h <= 0 || (long)w * h > 60000000) return false;
            string filter = OnlyFilter(doc, Deref(doc, d.Get("Filter")));
            Bitmap bmp = null;
            try
            {
                if (filter == "DCTDecode") bmp = FromJpeg(s.Data, comps);
                else if (filter == "FlateDecode") bmp = FromFlate(doc, s, w, h, comps);
                if (bmp == null) return false;
                double k = Math.Min(1.0, (double)maxSide / Math.Max(bmp.Width, bmp.Height));
                int nw = Math.Max(1, (int)Math.Round(bmp.Width * k)), nh = Math.Max(1, (int)Math.Round(bmp.Height * k));
                byte[] jpg;
                using (var small = Resize(bmp, nw, nh)) jpg = Jpeg(small, quality);
                // Keep it only if it really saves
                if (jpg.Length > s.Data.Length * 0.9) return false;
                s.Data = jpg;
                d.Set("Filter", new PdfName("DCTDecode"));
                d.Remove("DecodeParms");
                d.Set("Width", new PdfRaw(nw.ToString(CultureInfo.InvariantCulture)));
                d.Set("Height", new PdfRaw(nh.ToString(CultureInfo.InvariantCulture)));
                d.Set("BitsPerComponent", new PdfRaw("8"));
                // Gray went out as RGB JPEG
                if (comps == 1) d.Set("ColorSpace", new PdfName("DeviceRGB"));
                return true;
            }
            catch (Exception ex) { Log.Write("pdf image: " + ex.Message); return false; }
            finally { if (bmp != null) bmp.Dispose(); }
        }

        static object Deref(Doc doc, object v)
        {
            for (int i = 0; i < 16; i++)
            {
                var r = v as PdfRef;
                if (r == null) return v;
                v = r.Num >= 1 && r.Num <= doc.Objs.Count ? doc.Objs[r.Num - 1] : null;
            }
            return null;
        }

        static string Raw(object v) { var r = v as PdfRaw; return r != null ? r.ToString() : ""; }

        static int Components(Doc doc, object cs)
        {
            var n = cs as PdfName;
            if (n != null) return n.Name == "DeviceRGB" || n.Name == "CalRGB" ? 3 : n.Name == "DeviceGray" || n.Name == "CalGray" ? 1 : 0;
            var l = cs as List<object>;
            if (l == null || l.Count < 2) return 0;
            var kind = Deref(doc, l[0]) as PdfName;
            if (kind == null || kind.Name != "ICCBased") return 0;
            var icc = Deref(doc, l[1]) as PdfStream;
            int c = icc != null ? (int)PdfLexer.Long(Deref(doc, icc.Dict.Get("N"))) : 0;
            return c == 1 || c == 3 ? c : 0;
        }

        static string OnlyFilter(Doc doc, object f)
        {
            var n = f as PdfName;
            if (n != null) return n.Name;
            var l = f as List<object>;
            if (l != null && l.Count == 1) { var one = Deref(doc, l[0]) as PdfName; if (one != null) return one.Name; }
            return "";
        }

        static Bitmap FromJpeg(byte[] data, int comps)
        {
            using (var ms = new MemoryStream(data))
            using (var img = Image.FromStream(ms, false, false))
            {
                // CMYK JPEGs keep their own look
                if ((((ImageFlags)img.Flags) & ImageFlags.ColorSpaceCmyk) != 0 || (((ImageFlags)img.Flags) & ImageFlags.ColorSpaceYcck) != 0) return null;
                var bmp = new Bitmap(img.Width, img.Height, PixelFormat.Format24bppRgb);
                using (var g = Graphics.FromImage(bmp)) g.DrawImage(img, 0, 0, img.Width, img.Height);
                return bmp;
            }
        }

        static Bitmap FromFlate(Doc doc, PdfStream s, int w, int h, int comps)
        {
            var raw = Inflate(s.Data);
            if (raw == null) return null;
            var parms = Deref(doc, s.Dict.Get("DecodeParms")) as PdfDict;
            if (parms != null)
            {
                long pred = PdfLexer.Long(Deref(doc, parms.Get("Predictor")));
                if (pred >= 10) raw = Unpng(raw, w * comps, comps);
                else if (pred > 1) return null;
            }
            if (raw == null || raw.Length < (long)w * h * comps) return null;
            var bmp = new Bitmap(w, h, PixelFormat.Format24bppRgb);
            var bd = bmp.LockBits(new Rectangle(0, 0, w, h), ImageLockMode.WriteOnly, PixelFormat.Format24bppRgb);
            try
            {
                var row = new byte[w * 3];
                for (int y = 0; y < h; y++)
                {
                    int src = y * w * comps;
                    for (int x = 0; x < w; x++)
                    {
                        if (comps == 3) { row[x * 3] = raw[src + x * 3 + 2]; row[x * 3 + 1] = raw[src + x * 3 + 1]; row[x * 3 + 2] = raw[src + x * 3]; }
                        else { byte v = raw[src + x]; row[x * 3] = v; row[x * 3 + 1] = v; row[x * 3 + 2] = v; }
                    }
                    Marshal.Copy(row, 0, new IntPtr(bd.Scan0.ToInt64() + (long)y * bd.Stride), row.Length);
                }
            }
            finally { bmp.UnlockBits(bd); }
            return bmp;
        }

        static Bitmap Resize(Bitmap src, int w, int h)
        {
            var b = new Bitmap(w, h, PixelFormat.Format24bppRgb);
            using (var g = Graphics.FromImage(b))
            {
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                g.CompositingQuality = CompositingQuality.HighQuality;
                using (var wrap = new ImageAttributes())
                {
                    wrap.SetWrapMode(WrapMode.TileFlipXY);
                    g.DrawImage(src, new Rectangle(0, 0, w, h), 0, 0, src.Width, src.Height, GraphicsUnit.Pixel, wrap);
                }
            }
            return b;
        }

        public static byte[] Jpeg(Bitmap bmp, long quality)
        {
            ImageCodecInfo codec = null;
            foreach (var c in ImageCodecInfo.GetImageEncoders()) if (c.MimeType == "image/jpeg") codec = c;
            using (var ms = new MemoryStream())
            using (var ep = new EncoderParameters(1))
            {
                ep.Param[0] = new EncoderParameter(System.Drawing.Imaging.Encoder.Quality, quality);
                bmp.Save(ms, codec, ep);
                return ms.ToArray();
            }
        }

        // Zlib

        public static byte[] Inflate(byte[] data)
        {
            if (data.Length < 2) return null;
            // Zlib header; a few files skip it
            int skip = (data[0] & 0x0F) == 8 && ((data[0] << 8) | data[1]) % 31 == 0 ? 2 : 0;
            var outp = new MemoryStream(data.Length * 3);
            try
            {
                using (var z = new DeflateStream(new MemoryStream(data, skip, data.Length - skip), CompressionMode.Decompress))
                {
                    var buf = new byte[65536];
                    int n;
                    while ((n = z.Read(buf, 0, buf.Length)) > 0) outp.Write(buf, 0, n);
                }
            }
            catch (InvalidDataException)
            {
                // Damaged tail: keep what came out
                if (outp.Length == 0) return null;
            }
            return outp.ToArray();
        }

        public static byte[] Deflate(byte[] data)
        {
            using (var outp = new MemoryStream(data.Length / 2 + 64))
            {
                outp.WriteByte(0x78);
                outp.WriteByte(0xDA);
                using (var z = new DeflateStream(outp, CompressionLevel.Optimal, true)) z.Write(data, 0, data.Length);
                uint a = 1, bsum = 0;
                foreach (byte x in data) { a = (a + x) % 65521; bsum = (bsum + a) % 65521; }
                uint adler = (bsum << 16) | a;
                outp.WriteByte((byte)(adler >> 24));
                outp.WriteByte((byte)(adler >> 16));
                outp.WriteByte((byte)(adler >> 8));
                outp.WriteByte((byte)adler);
                return outp.ToArray();
            }
        }

        // PNG row filters back to plain rows
        public static byte[] Unpng(byte[] d, int rowLen, int bpp)
        {
            int rows = d.Length / (rowLen + 1);
            var outp = new byte[rows * rowLen];
            var prev = new byte[rowLen];
            for (int r = 0; r < rows; r++)
            {
                int src = r * (rowLen + 1), dst = r * rowLen;
                byte f = d[src];
                for (int i = 0; i < rowLen; i++)
                {
                    int x = d[src + 1 + i];
                    int left = i >= bpp ? outp[dst + i - bpp] : 0, up = prev[i], ul = i >= bpp ? prev[i - bpp] : 0;
                    switch (f)
                    {
                        case 1: x += left; break;
                        case 2: x += up; break;
                        case 3: x += (left + up) / 2; break;
                        case 4:
                            int pa = Math.Abs(up - ul), pb = Math.Abs(left - ul), pc = Math.Abs(left + up - 2 * ul);
                            x += pa <= pb && pa <= pc ? left : pb <= pc ? up : ul;
                            break;
                    }
                    outp[dst + i] = (byte)x;
                }
                Buffer.BlockCopy(outp, dst, prev, 0, rowLen);
            }
            return outp;
        }

        // Writer: object streams and an xref stream

        static void Write(string path, Doc doc, object root, object info, object id, string version)
        {
            if (string.CompareOrdinal(version, "1.5") < 0) version = "1.5";
            int count = doc.Objs.Count;
            // Plain objects packed 100 to a stream
            var loose = new List<int>();
            for (int n = 1; n <= count; n++) if (!(doc.Objs[n - 1] is PdfStream) && doc.Objs[n - 1] != null) loose.Add(n);
            var where = new Dictionary<int, int[]>();
            var stms = new List<PdfStream>();
            for (int at = 0; at < loose.Count; at += 100)
            {
                int stmNum = count + stms.Count + 1;
                var head = new StringBuilder();
                var body = new MemoryStream();
                int take = Math.Min(100, loose.Count - at);
                for (int k = 0; k < take; k++)
                {
                    int n = loose[at + k];
                    head.Append(n).Append(' ').Append(body.Length).Append(' ');
                    Ser(doc.Objs[n - 1], body);
                    body.WriteByte((byte)'\n');
                    where[n] = new[] { stmNum, k };
                }
                var headBytes = Encoding.ASCII.GetBytes(head.ToString() + "\n");
                var all = new byte[headBytes.Length + body.Length];
                Buffer.BlockCopy(headBytes, 0, all, 0, headBytes.Length);
                Buffer.BlockCopy(body.ToArray(), 0, all, headBytes.Length, (int)body.Length);
                var dict = new PdfDict();
                dict.Set("Type", new PdfName("ObjStm"));
                dict.Set("N", new PdfRaw(take.ToString(CultureInfo.InvariantCulture)));
                dict.Set("First", new PdfRaw(headBytes.Length.ToString(CultureInfo.InvariantCulture)));
                dict.Set("Filter", new PdfName("FlateDecode"));
                stms.Add(new PdfStream { Dict = dict, Data = Deflate(all) });
            }
            int xrefNum = count + stms.Count + 1;
            var offsets = new long[xrefNum + 1];
            using (var fs = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None, 1 << 16))
            {
                Put(fs, "%PDF-" + version + "\n");
                fs.Write(new byte[] { (byte)'%', 0xE2, 0xE3, 0xCF, 0xD3, (byte)'\n' }, 0, 6);
                for (int n = 1; n <= count; n++)
                {
                    var s = doc.Objs[n - 1] as PdfStream;
                    if (s == null) continue;
                    offsets[n] = fs.Position;
                    WriteObj(fs, n, s);
                }
                for (int i = 0; i < stms.Count; i++)
                {
                    offsets[count + i + 1] = fs.Position;
                    WriteObj(fs, count + i + 1, stms[i]);
                }
                // Xref stream: 1 type byte, 4 offset, 2 index
                offsets[xrefNum] = fs.Position;
                var table = new byte[(xrefNum + 1) * 7];
                for (int n = 0; n <= xrefNum; n++)
                {
                    int p = n * 7;
                    int[] packedAt;
                    if (n == 0) { table[p] = 0; Big(table, p + 1, 0, 4); Big(table, p + 5, 65535, 2); }
                    else if (where.TryGetValue(n, out packedAt)) { table[p] = 2; Big(table, p + 1, packedAt[0], 4); Big(table, p + 5, packedAt[1], 2); }
                    else if (offsets[n] > 0) { table[p] = 1; Big(table, p + 1, offsets[n], 4); Big(table, p + 5, 0, 2); }
                    else { table[p] = 0; Big(table, p + 1, 0, 4); Big(table, p + 5, 0, 2); }
                }
                var x = new PdfDict();
                x.Set("Type", new PdfName("XRef"));
                x.Set("Size", new PdfRaw((xrefNum + 1).ToString(CultureInfo.InvariantCulture)));
                x.Set("W", new List<object> { new PdfRaw("1"), new PdfRaw("4"), new PdfRaw("2") });
                x.Set("Root", root);
                if (info != null) x.Set("Info", info);
                if (id is List<object>) x.Set("ID", id);
                x.Set("Filter", new PdfName("FlateDecode"));
                WriteObj(fs, xrefNum, new PdfStream { Dict = x, Data = Deflate(table) });
                Put(fs, "startxref\n" + offsets[xrefNum].ToString(CultureInfo.InvariantCulture) + "\n%%EOF\n");
            }
        }

        static void Big(byte[] t, int at, long v, int width)
        {
            for (int i = width - 1; i >= 0; i--) { t[at + i] = (byte)(v & 0xFF); v >>= 8; }
        }

        static void WriteObj(Stream o, int num, PdfStream s)
        {
            Put(o, num.ToString(CultureInfo.InvariantCulture) + " 0 obj\n");
            s.Dict.Set("Length", new PdfRaw(s.Data.Length.ToString(CultureInfo.InvariantCulture)));
            Ser(s.Dict, o);
            Put(o, "\nstream\n");
            o.Write(s.Data, 0, s.Data.Length);
            Put(o, "\nendstream\nendobj\n");
        }

        static void Put(Stream o, string s)
        {
            var bytes = Encoding.ASCII.GetBytes(s);
            o.Write(bytes, 0, bytes.Length);
        }

        static void Ser(object v, Stream o)
        {
            if (v == null) { Put(o, "null"); return; }
            var n = v as PdfName;
            if (n != null) { var nb = PdfLexer.Latin1.GetBytes("/" + n.Name); o.Write(nb, 0, nb.Length); return; }
            var r = v as PdfRef;
            if (r != null) { Put(o, r.Num.ToString(CultureInfo.InvariantCulture) + " " + r.Gen.ToString(CultureInfo.InvariantCulture) + " R"); return; }
            var raw = v as PdfRaw;
            if (raw != null) { o.Write(raw.Bytes, 0, raw.Bytes.Length); return; }
            var d = v as PdfDict;
            if (d != null)
            {
                Put(o, "<<");
                foreach (var kv in d.Items)
                {
                    var kb = PdfLexer.Latin1.GetBytes("/" + kv.Key + " ");
                    o.Write(kb, 0, kb.Length);
                    Ser(kv.Value, o);
                    Put(o, " ");
                }
                Put(o, ">>");
                return;
            }
            var l = v as List<object>;
            if (l != null)
            {
                Put(o, "[");
                for (int i = 0; i < l.Count; i++)
                {
                    if (i > 0) Put(o, " ");
                    Ser(l[i], o);
                }
                Put(o, "]");
                return;
            }
            var w = v as PdfWord;
            if (w != null) { var wb = PdfLexer.Latin1.GetBytes(w.Text); o.Write(wb, 0, wb.Length); return; }
            // A stream nested in a value cannot be written inline
            Put(o, "null");
        }
    }
}
