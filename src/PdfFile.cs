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
    class PdfFile
    {
        readonly byte[] b;
        readonly Dictionary<int, long> offsets = new Dictionary<int, long>();
        readonly Dictionary<int, int[]> packed = new Dictionary<int, int[]>();
        readonly HashSet<int> freed = new HashSet<int>();
        readonly Dictionary<int, object> cache = new Dictionary<int, object>();
        readonly Dictionary<int, KeyValuePair<byte[], int[]>> objStms = new Dictionary<int, KeyValuePair<byte[], int[]>>();
        Dictionary<int, long> scanned;
        int headerAt;
        public PdfDict Trailer;
        public string Version = "1.4";

        public PdfFile(byte[] data)
        {
            b = data;
            headerAt = Find(b, "%PDF-", 0, Math.Min(b.Length, 1024));
            if (headerAt < 0) throw new InvalidDataException(Lang.T("Bukan berkas PDF"));
            Version = Encoding.ASCII.GetString(b, headerAt + 5, 3);
            try { LoadXref(); }
            catch (Exception ex) { Log.Write("pdf xref: " + ex.Message); Trailer = null; }
            // Broken or missing xref: rebuild from the objects
            if (Trailer == null || !(Resolve(Trailer.Get("Root")) is PdfDict)) Rebuild();
            if (Trailer == null || !(Resolve(Trailer.Get("Root")) is PdfDict)) throw new InvalidDataException(Lang.T("PDF tidak bisa dibaca"));
            var cat = (PdfDict)Resolve(Trailer.Get("Root"));
            var v = cat.Get("Version") as PdfName;
            if (v != null && string.CompareOrdinal(v.Name, Version) > 0) Version = v.Name;
        }

        public static PdfFile Open(string path) { return new PdfFile(File.ReadAllBytes(path)); }

        public bool Encrypted { get { return Trailer.Has("Encrypt"); } }

        public PdfDict Root { get { return (PdfDict)Resolve(Trailer.Get("Root")); } }

        // Xref

        void LoadXref()
        {
            int sx = FindLast(b, "startxref");
            if (sx < 0) throw new InvalidDataException("startxref");
            var lx = new PdfLexer(b, sx + 9);
            long pos = lx.Int();
            var seen = new HashSet<long>();
            while (pos > 0 && seen.Add(pos))
            {
                long at = pos < b.Length && Starts((int)pos) ? pos : pos + headerAt;
                if (at >= b.Length) break;
                var t = new PdfLexer(b, (int)at);
                t.Skip();
                PdfDict tr = t.Peek("xref") ? ReadTable(t) : ReadXrefStream((int)at);
                if (Trailer == null) Trailer = tr;
                // Hybrid files keep a stream beside the table
                long xs = PdfLexer.Long(tr.Get("XRefStm"));
                if (xs > 0 && seen.Add(xs)) ReadXrefStream((int)(xs + (Starts((int)xs) ? 0 : headerAt)));
                pos = PdfLexer.Long(tr.Get("Prev"));
            }
        }

        bool Starts(int pos)
        {
            var t = new PdfLexer(b, pos);
            t.Skip();
            return t.Pos < b.Length && (t.Peek("xref") || char.IsDigit((char)b[t.Pos]));
        }

        PdfDict ReadTable(PdfLexer t)
        {
            t.Pos += 4;
            while (true)
            {
                t.Skip();
                if (t.Peek("trailer")) { t.Pos += 7; return (PdfDict)t.Value(); }
                int start = (int)t.Int(), count = (int)t.Int();
                for (int i = 0; i < count; i++)
                {
                    long off = t.Int();
                    t.Int();
                    string kind = t.Word();
                    Note(start + i, kind == "n" ? off : -1, null);
                }
            }
        }

        PdfDict ReadXrefStream(int at)
        {
            int num, gen;
            var s = ReadAt(at, out num, out gen) as PdfStream;
            if (s == null) throw new InvalidDataException("xref stream");
            var data = Decode(s);
            if (data == null) throw new InvalidDataException("xref filter");
            var w = s.Dict.Get("W") as List<object>;
            int w0 = (int)PdfLexer.Long(w[0]), w1 = (int)PdfLexer.Long(w[1]), w2 = (int)PdfLexer.Long(w[2]);
            var index = s.Dict.Get("Index") as List<object>;
            if (index == null) index = new List<object> { new PdfRaw("0"), s.Dict.Get("Size") };
            int p = 0, row = w0 + w1 + w2;
            for (int k = 0; k + 1 < index.Count; k += 2)
            {
                int first = (int)PdfLexer.Long(index[k]), count = (int)PdfLexer.Long(index[k + 1]);
                for (int i = 0; i < count && p + row <= data.Length; i++, p += row)
                {
                    long type = w0 == 0 ? 1 : Field(data, p, w0), f1 = Field(data, p + w0, w1), f2 = Field(data, p + w0 + w1, w2);
                    if (type == 1) Note(first + i, f1, null);
                    else if (type == 2) Note(first + i, -1, new[] { (int)f1, (int)f2 });
                    else Note(first + i, -1, null);
                }
            }
            return s.Dict;
        }

        static long Field(byte[] d, int at, int width)
        {
            long v = 0;
            for (int i = 0; i < width; i++) v = (v << 8) | d[at + i];
            return v;
        }

        // Newest section wins: older entries never override
        void Note(int num, long off, int[] inStm)
        {
            if (offsets.ContainsKey(num) || packed.ContainsKey(num) || freed.Contains(num)) return;
            if (inStm != null) packed[num] = inStm;
            else if (off > 0) offsets[num] = off;
            else freed.Add(num);
        }

        // Every "n g obj" in the file; the last one wins
        Dictionary<int, long> Scan()
        {
            if (scanned != null) return scanned;
            scanned = new Dictionary<int, long>();
            for (int i = Find(b, "obj", 0, b.Length); i >= 0; i = Find(b, "obj", i + 3, b.Length))
            {
                int j = i - 1;
                while (j >= 0 && PdfLexer.White(b[j])) j--;
                int ge = j;
                while (j >= 0 && char.IsDigit((char)b[j])) j--;
                if (j == ge) continue;
                int gs = j + 1;
                while (j >= 0 && PdfLexer.White(b[j])) j--;
                int ne = j;
                while (j >= 0 && char.IsDigit((char)b[j])) j--;
                if (j == ne || ne - j > 9) continue;
                if (j >= 0 && !PdfLexer.White(b[j]) && !PdfLexer.Delim(b[j])) continue;
                int num = int.Parse(Encoding.ASCII.GetString(b, j + 1, ne - j), CultureInfo.InvariantCulture);
                scanned[num] = j + 1;
            }
            return scanned;
        }

        void Rebuild()
        {
            offsets.Clear();
            packed.Clear();
            freed.Clear();
            cache.Clear();
            foreach (var kv in Scan()) offsets[kv.Key] = kv.Value;
            PdfDict found = null;
            // Last trailer, else an xref stream, else the catalog
            int tr = FindLast(b, "trailer");
            if (tr >= 0) try { var t = new PdfLexer(b, tr + 7); found = t.Value() as PdfDict; } catch (Exception) { }
            foreach (var kv in offsets)
            {
                if (found != null && Resolve(found.Get("Root")) is PdfDict) break;
                PdfDict d = null;
                try { var o = Get(kv.Key); var st = o as PdfStream; d = st != null ? st.Dict : o as PdfDict; } catch (Exception) { }
                if (d == null) continue;
                var type = d.Get("Type") as PdfName;
                if (type != null && type.Name == "XRef" && d.Has("Root")) found = d;
                else if (type != null && type.Name == "Catalog") { found = new PdfDict(); found.Set("Root", new PdfRef(kv.Key, 0)); }
            }
            Trailer = found;
        }

        // Objects

        public object Get(int num)
        {
            object v;
            if (cache.TryGetValue(num, out v)) return v;
            cache[num] = null;
            v = null;
            long off;
            int[] at;
            try
            {
                if (offsets.TryGetValue(num, out off)) v = Load(num, off);
                else if (packed.TryGetValue(num, out at)) v = Packed(at[0], at[1], num);
            }
            catch (Exception ex) { Log.Write("pdf obj " + num + ": " + ex.Message); v = null; }
            // Bad offset: the scan knows better
            if (v == null && Scan().TryGetValue(num, out off) && (!offsets.ContainsKey(num) || offsets[num] != off))
                try { v = Load(num, off); } catch (Exception) { }
            cache[num] = v;
            return v;
        }

        object Load(int num, long off)
        {
            foreach (long at in new[] { off, off + headerAt })
            {
                if (at < 0 || at >= b.Length) continue;
                int n, g;
                try
                {
                    var o = ReadAt((int)at, out n, out g);
                    if (n == num) return o;
                }
                catch (Exception) { }
                if (headerAt == 0) break;
            }
            return null;
        }

        public object Resolve(object v)
        {
            for (int hops = 0; hops < 32; hops++)
            {
                var r = v as PdfRef;
                if (r == null) return v;
                v = Get(r.Num);
            }
            return null;
        }

        object ReadAt(int at, out int num, out int gen)
        {
            var t = new PdfLexer(b, at);
            num = (int)t.Int();
            gen = (int)t.Int();
            t.Expect("obj");
            object v = t.Value();
            t.Skip();
            var dict = v as PdfDict;
            if (dict == null || !t.Peek("stream")) return v;
            t.Pos += 6;
            if (t.Pos < b.Length && b[t.Pos] == '\r') t.Pos++;
            if (t.Pos < b.Length && b[t.Pos] == '\n') t.Pos++;
            int start = t.Pos;
            long len = -1;
            var lv = dict.Get("Length");
            // Lengths may sit in another object
            if (lv is PdfRef) { var lr = (PdfRef)lv; if (lr.Num != num) lv = Get(lr.Num); }
            if (lv is PdfRaw) len = PdfLexer.Long(lv);
            int end = start + (int)Math.Max(0, len);
            if (len < 0 || end > b.Length || !EndsAt(end))
            {
                end = Find(b, "endstream", start, b.Length);
                if (end < 0) throw new InvalidDataException("endstream");
                while (end > start && (b[end - 1] == '\n' || b[end - 1] == '\r')) end--;
            }
            var data = new byte[end - start];
            Buffer.BlockCopy(b, start, data, 0, data.Length);
            return new PdfStream { Dict = dict, Data = data };
        }

        bool EndsAt(int end)
        {
            var t = new PdfLexer(b, end);
            t.Skip();
            return t.Peek("endstream");
        }

        object Packed(int stmNum, int index, int num)
        {
            KeyValuePair<byte[], int[]> entry;
            if (!objStms.TryGetValue(stmNum, out entry))
            {
                var s = Get(stmNum) as PdfStream;
                if (s == null) return null;
                var data = Decode(s);
                if (data == null) return null;
                int n = (int)PdfLexer.Long(s.Dict.Get("N")), first = (int)PdfLexer.Long(s.Dict.Get("First"));
                var head = new PdfLexer(data, 0);
                var pairs = new int[n * 2 + 1];
                for (int i = 0; i < n; i++) { pairs[i * 2] = (int)head.Int(); pairs[i * 2 + 1] = (int)head.Int() + first; }
                pairs[n * 2] = data.Length;
                entry = new KeyValuePair<byte[], int[]>(data, pairs);
                objStms[stmNum] = entry;
            }
            var pr = entry.Value;
            int count = (pr.Length - 1) / 2;
            int k = index >= 0 && index < count && pr[index * 2] == num ? index : -1;
            for (int i = 0; k < 0 && i < count; i++) if (pr[i * 2] == num) k = i;
            if (k < 0) return null;
            return new PdfLexer(entry.Key, pr[k * 2 + 1]).Value();
        }

        // Pages in order, with what they inherit
        public List<KeyValuePair<PdfRef, PdfDict>> Pages()
        {
            var list = new List<KeyValuePair<PdfRef, PdfDict>>();
            var seen = new HashSet<int>();
            Walk(Root.Get("Pages"), new PdfDict(), list, seen, 0);
            return list;
        }

        void Walk(object node, PdfDict inherit, List<KeyValuePair<PdfRef, PdfDict>> list, HashSet<int> seen, int depth)
        {
            var r = node as PdfRef;
            if (r == null || depth > 64 || !seen.Add(r.Num)) return;
            var d = Resolve(r) as PdfDict;
            if (d == null) return;
            var mine = new PdfDict();
            foreach (var kv in inherit.Items) mine.Set(kv.Key, kv.Value);
            foreach (var key in Inherited) if (d.Has(key)) mine.Set(key, d.Get(key));
            var kids = Resolve(d.Get("Kids")) as List<object>;
            var type = d.Get("Type") as PdfName;
            if (kids != null && (type == null || type.Name != "Page")) { foreach (var k in kids) Walk(k, mine, list, seen, depth + 1); return; }
            list.Add(new KeyValuePair<PdfRef, PdfDict>(r, mine));
        }

        public static readonly string[] Inherited = { "Resources", "MediaBox", "CropBox", "Rotate" };

        // Filters

        public byte[] Decode(PdfStream s)
        {
            var filter = Resolve(s.Dict.Get("Filter"));
            var parms = Resolve(s.Dict.Get("DecodeParms"));
            var names = new List<string>();
            if (filter is PdfName) names.Add(((PdfName)filter).Name);
            else if (filter is List<object>) foreach (var f in (List<object>)filter) { var n = Resolve(f) as PdfName; if (n != null) names.Add(n.Name); }
            byte[] data = s.Data;
            for (int i = 0; i < names.Count; i++)
            {
                if (names[i] != "FlateDecode" && names[i] != "Fl") return null;
                var p = parms is List<object> ? Resolve(((List<object>)parms)[Math.Min(i, ((List<object>)parms).Count - 1)]) as PdfDict : parms as PdfDict;
                data = PdfLite.Inflate(data);
                if (data == null) return null;
                if (p != null) data = Unpredict(data, p);
                if (data == null) return null;
            }
            return data;
        }

        // PNG predictors, as used by xref and object streams
        byte[] Unpredict(byte[] d, PdfDict p)
        {
            long pred = PdfLexer.Long(Resolve(p.Get("Predictor")));
            if (pred < 10) return pred <= 1 ? d : null;
            int colors = Math.Max(1, (int)PdfLexer.Long(Resolve(p.Get("Colors")), 1));
            int bpc = Math.Max(1, (int)PdfLexer.Long(Resolve(p.Get("BitsPerComponent")), 8));
            int cols = Math.Max(1, (int)PdfLexer.Long(Resolve(p.Get("Columns")), 1));
            return PdfLite.Unpng(d, (cols * colors * bpc + 7) / 8, Math.Max(1, colors * bpc / 8));
        }

        // Bytes

        public static int Find(byte[] d, string s, int from, int to)
        {
            to = Math.Min(to, d.Length);
            for (int i = Math.Max(0, from); i <= to - s.Length; i++)
            {
                int k = 0;
                while (k < s.Length && d[i + k] == s[k]) k++;
                if (k == s.Length) return i;
            }
            return -1;
        }

        static int FindLast(byte[] d, string s)
        {
            for (int i = d.Length - s.Length; i >= 0; i--)
            {
                int k = 0;
                while (k < s.Length && d[i + k] == s[k]) k++;
                if (k == s.Length) return i;
            }
            return -1;
        }
    }

}
