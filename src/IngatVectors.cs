using System;
using System.Collections.Generic;

namespace Deskmate
{
    // Meaning search: several vectors per episode (one per chunk of its text), kept in RAM as int8.
    // A unit vector quantized to int8 costs a quarter of float and keeps cosine within about 0.01, plenty for ranking.
    sealed partial class IngatStore
    {
        // Chunks one episode may have; a long body gets wider chunks rather than more of them
        public const int MaxChunks = 8;

        // Lightweight semantic vector item for facts and conversations
        public struct ItemVecInput
        {
            public long Id;
            public string Hash;
            public float[] Vec;
        }

        void MigrateVectors()
        {
            db.Exec("CREATE TABLE IF NOT EXISTS chunks(episode INTEGER, model TEXT, n INTEGER, vec BLOB, PRIMARY KEY(episode, model, n))");
            db.Exec("CREATE INDEX IF NOT EXISTS chunks_model ON chunks(model)");
            db.Exec("CREATE TABLE IF NOT EXISTS item_vectors(kind TEXT, item_id INTEGER, model TEXT, text_hash TEXT DEFAULT '', vec BLOB, PRIMARY KEY(kind, item_id, model))");
            db.Exec("CREATE INDEX IF NOT EXISTS item_vectors_km ON item_vectors(kind, model)");
            // One vector per episode from before chunking becomes chunk 0
            db.Tx(() =>
            {
                db.Exec("INSERT OR IGNORE INTO chunks(episode,model,n,vec) SELECT episode, model, 0, vec FROM embeddings");
                db.Exec("DELETE FROM embeddings");
            });
        }

        public void SaveVector(long id, string model, float[] vec) { SaveVectors(id, model, new List<float[]> { vec }); }

        public void SaveVectors(long id, string model, List<float[]> vecs)
        {
            db.Tx(() =>
            {
                db.Exec("DELETE FROM chunks WHERE episode=? AND model=?", id, model);
                for (int n = 0; n < vecs.Count && n < MaxChunks; n++)
                {
                    var bytes = new byte[vecs[n].Length * 4];
                    Buffer.BlockCopy(vecs[n], 0, bytes, 0, bytes.Length);
                    db.Exec("INSERT INTO chunks(episode,model,n,vec) VALUES(?,?,?,?)", id, model, n, bytes);
                }
                db.Exec("UPDATE episodes SET embedded=1 WHERE id=?", id);
            });
            bool wantUpdate;
            lock (vecGate) wantUpdate = vecCache.ContainsKey(model);
            if (!wantUpdate) return;
            var span = db.Query("SELECT start, end FROM episodes WHERE id=?", id);
            if (span.Count == 0) return;
            long start = IngatDb.Num(span[0][0]), end = IngatDb.Num(span[0][1]);
            var list = new QChunk[Math.Min(MaxChunks, vecs.Count)];
            for (int n = 0; n < list.Length; n++) list[n] = Quantize(vecs[n]);
            var qep = new QEpisode { Start = start, End = end, Chunks = list };
            lock (vecGate)
            {
                Dictionary<long, QEpisode> rows;
                if (!vecCache.TryGetValue(model, out rows)) return;
                QEpisode old;
                if (rows.TryGetValue(id, out old)) vecBytes -= Bytes(old.Chunks);
                rows[id] = qep;
                vecBytes += Bytes(list);
                if (vecBytes > VecCacheBytes) { vecCache.Remove(model); vecBytes = 0; vecTooBig.Add(model); }
            }
        }

        public bool HasVectors(string model)
        {
            lock (vecGate)
            {
                Dictionary<long, QEpisode> rows;
                if (vecCache.TryGetValue(model, out rows)) return rows.Count > 0;
            }
            return db.Long("SELECT EXISTS(SELECT 1 FROM chunks WHERE model=?)", model) > 0;
        }

        // Embedding failed for good (no server, too long): stop asking
        public void SkipVector(long id) { db.Exec("UPDATE episodes SET embedded=2 WHERE id=?", id); }

        // Exact stored vectors, read from disk: for checks and export, not the question path
        public List<KeyValuePair<long, float[]>> Vectors(string model, DateTime fromUtc, DateTime toUtc)
        {
            var list = new List<KeyValuePair<long, float[]>>();
            foreach (var r in db.Query("SELECT v.episode, v.vec FROM chunks v JOIN episodes e ON e.id=v.episode WHERE v.model=? AND e.end>=? AND e.start<=? ORDER BY v.episode, v.n", model, fromUtc, toUtc))
            {
                var v = Floats(r[1] as byte[]);
                if (v != null) list.Add(new KeyValuePair<long, float[]>(IngatDb.Num(r[0]), v));
            }
            return list;
        }

        // Every episode in range with its best chunk's cosine to the (unit) query, best first
        public List<KeyValuePair<long, double>> Nearest(string model, float[] query, DateTime fromUtc, DateTime toUtc)
        {
            Dictionary<long, int> chunks;
            return Nearest(model, query, fromUtc, toUtc, out chunks);
        }

        // Same search, also reporting the winning chunk index per episode so RelevantExcerpt can jump directly to it
        public List<KeyValuePair<long, double>> Nearest(string model, float[] query, DateTime fromUtc, DateTime toUtc, out Dictionary<long, int> bestChunks)
        {
            var best = new Dictionary<long, double>();
            bestChunks = new Dictionary<long, int>();
            long from = IngatDb.Unix(fromUtc), to = IngatDb.Unix(toUtc);
            bool cached = false;
            var qQuery = Quantize(query);
            lock (vecGate)
            {
                var rows = VecRows(model);
                if (rows != null)
                {
                    cached = true;
                    foreach (var kv in rows)
                    {
                        var ep = kv.Value;
                        if (ep.End < from || ep.Start > to) continue;
                        var chunks = ep.Chunks;
                        double top = double.NegativeInfinity;
                        int winIdx = 0;
                        for (int i = 0; i < chunks.Length; i++)
                        {
                            var c = chunks[i];
                            if (c.Q.Length != qQuery.Q.Length) continue;
                            double s = DotQ8(qQuery.Q, qQuery.Scale, c.Q, c.Scale);
                            if (s > top) { top = s; winIdx = i; }
                        }
                        if (top > double.NegativeInfinity) { best[kv.Key] = top; bestChunks[kv.Key] = winIdx; }
                    }
                }
            }
            if (!cached)
                foreach (var r in db.Query("SELECT v.episode, v.n, v.vec FROM chunks v JOIN episodes e ON e.id=v.episode WHERE v.model=? AND e.end>=? AND e.start<=?", model, fromUtc, toUtc))
                {
                    var v = Floats(r[2] as byte[]);
                    if (v == null || v.Length != query.Length) continue;
                    long id = IngatDb.Num(r[0]);
                    int n = (int)IngatDb.Num(r[1]);
                    double s = Dot(query, v), cur;
                    if (!best.TryGetValue(id, out cur) || s > cur) { best[id] = s; bestChunks[id] = n; }
                }
            var list = new List<KeyValuePair<long, double>>(best);
            list.Sort((a, b) => b.Value.CompareTo(a.Value));
            return list;
        }

        // Fast 4-way unrolled integer MAC (int8 x int8) avoids per-dimension int->float conversions across thousands of vectors
        public static double DotQ8(sbyte[] a, float aScale, sbyte[] b, float bScale)
        {
            int n = Math.Min(a.Length, b.Length), i = 0;
            int s0 = 0, s1 = 0, s2 = 0, s3 = 0;
            for (; i <= n - 4; i += 4)
            {
                s0 += a[i] * b[i];
                s1 += a[i + 1] * b[i + 1];
                s2 += a[i + 2] * b[i + 2];
                s3 += a[i + 3] * b[i + 3];
            }
            int dot = s0 + s1 + s2 + s3;
            for (; i < n; i++) dot += a[i] * b[i];
            return dot * ((double)aScale * bScale);
        }

        // Semantic vectors for long-term facts ("fact") and conversation digests ("conv")

        readonly Dictionary<string, Dictionary<long, QChunk>> itemVecCache = new Dictionary<string, Dictionary<long, QChunk>>(StringComparer.Ordinal);

        public static string ItemTextHash(string text)
        {
            return Json.Sha256(text ?? "").Substring(0, 16);
        }

        public void SaveItemVectors(string kind, string model, IList<ItemVecInput> items)
        {
            if (items == null || items.Count == 0 || string.IsNullOrEmpty(model)) return;
            db.Tx(() =>
            {
                for (int i = 0; i < items.Count; i++)
                {
                    var it = items[i];
                    if (it.Vec == null || it.Vec.Length == 0) continue;
                    var bytes = new byte[it.Vec.Length * 4];
                    Buffer.BlockCopy(it.Vec, 0, bytes, 0, bytes.Length);
                    db.Exec("INSERT OR REPLACE INTO item_vectors(kind,item_id,model,text_hash,vec) VALUES(?,?,?,?,?)",
                        kind, it.Id, model, it.Hash ?? "", bytes);
                }
            });
            string key = kind + "|" + model;
            lock (vecGate)
            {
                Dictionary<long, QChunk> rows;
                if (!itemVecCache.TryGetValue(key, out rows)) return;
                for (int i = 0; i < items.Count; i++)
                {
                    var it = items[i];
                    if (it.Vec != null && it.Vec.Length > 0) rows[it.Id] = Quantize(it.Vec);
                }
            }
        }

        public void DeleteItemVector(string kind, long itemId)
        {
            db.Exec("DELETE FROM item_vectors WHERE kind=? AND item_id=?", kind, itemId);
            string prefix = kind + "|";
            lock (vecGate)
            {
                foreach (var kv in itemVecCache)
                    if (kv.Key.StartsWith(prefix, StringComparison.Ordinal)) kv.Value.Remove(itemId);
            }
        }

        public List<KeyValuePair<long, string>> FactsNeedingVectors(string model, int limit)
        {
            var list = new List<KeyValuePair<long, string>>();
            if (string.IsNullOrEmpty(model)) return list;
            foreach (var r in db.Query("SELECT f.id, f.text, COALESCE(v.text_hash,'') FROM facts f LEFT JOIN item_vectors v ON v.kind='fact' AND v.item_id=f.id AND v.model=? WHERE f.status='active' ORDER BY f.last DESC", model))
            {
                string text = IngatDb.Str(r[1]);
                if (text.Length < 3) continue;
                if (IngatDb.Str(r[2]) != ItemTextHash(text))
                {
                    list.Add(new KeyValuePair<long, string>(IngatDb.Num(r[0]), text));
                    if (list.Count >= limit) break;
                }
            }
            return list;
        }

        public List<KeyValuePair<long, string>> ConversationsNeedingVectors(string model, int limit)
        {
            var list = new List<KeyValuePair<long, string>>();
            if (string.IsNullOrEmpty(model)) return list;
            foreach (var r in db.Query("SELECT c.id, c.name, c.app, c.topic, c.digest, COALESCE(v.text_hash,'') FROM conversations c LEFT JOIN item_vectors v ON v.kind='conv' AND v.item_id=c.id AND v.model=? WHERE c.digest<>'' ORDER BY c.last DESC LIMIT 200", model))
            {
                string text = (IngatDb.Str(r[1]) + " (" + IngatDb.Str(r[2]) + ") " + IngatDb.Str(r[3]) + "\n" + IngatDb.Str(r[4])).Trim();
                if (text.Length < 4) continue;
                if (IngatDb.Str(r[5]) != ItemTextHash(text))
                {
                    list.Add(new KeyValuePair<long, string>(IngatDb.Num(r[0]), text));
                    if (list.Count >= limit) break;
                }
            }
            return list;
        }

        Dictionary<long, QChunk> ItemVecRows(string kind, string model)
        {
            string key = kind + "|" + model;
            Dictionary<long, QChunk> rows;
            if (itemVecCache.TryGetValue(key, out rows)) return rows;
            rows = new Dictionary<long, QChunk>();
            foreach (var r in db.Query("SELECT item_id, vec FROM item_vectors WHERE kind=? AND model=?", kind, model))
            {
                var v = Floats(r[1] as byte[]);
                if (v != null && v.Length > 0) rows[IngatDb.Num(r[0])] = Quantize(v);
            }
            itemVecCache[key] = rows;
            return rows;
        }

        public bool HasItemVectors(string model)
        {
            if (string.IsNullOrEmpty(model)) return false;
            lock (vecGate)
            {
                Dictionary<long, QChunk> f, c;
                if (itemVecCache.TryGetValue("fact|" + model, out f) && f.Count > 0) return true;
                if (itemVecCache.TryGetValue("conv|" + model, out c) && c.Count > 0) return true;
            }
            return db.Long("SELECT EXISTS(SELECT 1 FROM item_vectors WHERE model=?)", model) > 0;
        }

        // Top semantic matches for facts or conversations using fast int8 x int8 cosine similarity
        public List<KeyValuePair<long, double>> NearestItems(string kind, string model, float[] query, int limit, double minScore = 0.25)
        {
            var hits = new List<KeyValuePair<long, double>>();
            if (query == null || query.Length == 0 || string.IsNullOrEmpty(model)) return hits;
            var qQuery = Quantize(query);
            lock (vecGate)
            {
                var rows = ItemVecRows(kind, model);
                foreach (var kv in rows)
                {
                    var c = kv.Value;
                    if (c.Q.Length != qQuery.Q.Length) continue;
                    double s = DotQ8(qQuery.Q, qQuery.Scale, c.Q, c.Scale);
                    if (s >= minScore) hits.Add(new KeyValuePair<long, double>(kv.Key, s));
                }
            }
            hits.Sort((a, b) => b.Value.CompareTo(a.Value));
            if (hits.Count > limit) hits.RemoveRange(limit, hits.Count - limit);
            return hits;
        }

        static float[] Floats(byte[] b)
        {
            if (b == null || b.Length < 4) return null;
            var v = new float[b.Length / 4];
            Buffer.BlockCopy(b, 0, v, 0, v.Length * 4);
            return v;
        }

        // Four independent sums let the CPU overlap the multiplies (Vector<T> is not in .NET Framework without a package)
        public static double Dot(float[] a, float[] b)
        {
            int n = Math.Min(a.Length, b.Length), i = 0;
            float s0 = 0, s1 = 0, s2 = 0, s3 = 0;
            for (; i <= n - 4; i += 4)
            {
                s0 += a[i] * b[i];
                s1 += a[i + 1] * b[i + 1];
                s2 += a[i + 2] * b[i + 2];
                s3 += a[i + 3] * b[i + 3];
            }
            double dot = (double)s0 + s1 + s2 + s3;
            for (; i < n; i++) dot += a[i] * b[i];
            return dot;
        }

        // Cache: int8 per dimension plus one scale; past the cap it steps aside and reads go to SQLite
        const long VecCacheBytes = 64L * 1024 * 1024;
        sealed class QChunk { public sbyte[] Q; public float Scale; }
        sealed class QEpisode { public long Start, End; public QChunk[] Chunks; }
        readonly object vecGate = new object();
        readonly Dictionary<string, Dictionary<long, QEpisode>> vecCache = new Dictionary<string, Dictionary<long, QEpisode>>();
        readonly HashSet<string> vecTooBig = new HashSet<string>();
        long vecBytes;

        static long Bytes(QChunk[] list)
        {
            long n = 32;
            foreach (var c in list) n += c.Q.Length + 24;
            return n;
        }

        public static sbyte[] QuantizeForCheck(float[] v, out float scale)
        {
            var c = Quantize(v);
            scale = c.Scale;
            return c.Q;
        }

        static QChunk Quantize(float[] v)
        {
            float max = 0;
            foreach (var x in v) { float a = Math.Abs(x); if (a > max) max = a; }
            float scale = max > 0 ? max / 127f : 1f;
            var q = new sbyte[v.Length];
            for (int i = 0; i < v.Length; i++)
            {
                int k = (int)Math.Round(v[i] / scale);
                q[i] = (sbyte)(k > 127 ? 127 : k < -127 ? -127 : k);
            }
            return new QChunk { Q = q, Scale = scale };
        }

        Dictionary<long, QEpisode> VecRows(string model)
        {
            Dictionary<long, QEpisode> rows;
            if (vecCache.TryGetValue(model, out rows)) return rows;
            if (vecTooBig.Contains(model)) return null;
            var grouped = new Dictionary<long, List<QChunk>>();
            var spans = new Dictionary<long, long[]>();
            long bytes = vecBytes;
            foreach (var r in db.Query("SELECT v.episode, v.vec, e.start, e.end FROM chunks v JOIN episodes e ON e.id=v.episode WHERE v.model=? ORDER BY v.episode, v.n", model))
            {
                var v = Floats(r[1] as byte[]);
                if (v == null) continue;
                bytes += v.Length + 24;
                if (bytes > VecCacheBytes) { vecTooBig.Add(model); return null; }
                long id = IngatDb.Num(r[0]);
                List<QChunk> list;
                if (!grouped.TryGetValue(id, out list))
                {
                    grouped[id] = list = new List<QChunk>();
                    spans[id] = new[] { IngatDb.Num(r[2]), IngatDb.Num(r[3]) };
                    bytes += 32;
                }
                list.Add(Quantize(v));
            }
            rows = new Dictionary<long, QEpisode>(grouped.Count);
            foreach (var kv in grouped)
            {
                var sp = spans[kv.Key];
                rows[kv.Key] = new QEpisode { Start = sp[0], End = sp[1], Chunks = kv.Value.ToArray() };
            }
            vecCache[model] = rows;
            vecBytes = bytes;
            return rows;
        }

        public void WarmVectors(string model) { lock (vecGate) VecRows(model); }

        // Rows were deleted: read again on next use
        void VecDrop()
        {
            lock (vecGate) { vecCache.Clear(); itemVecCache.Clear(); vecTooBig.Clear(); vecBytes = 0; }
        }

        // An episode's span changed: keep its time filter right
        void VecMoved(long id)
        {
            bool any;
            lock (vecGate) any = vecCache.Count > 0;
            if (!any) return;
            var span = db.Query("SELECT start, end FROM episodes WHERE id=?", id);
            lock (vecGate)
            {
                foreach (var rows in vecCache.Values)
                {
                    QEpisode ep;
                    if (!rows.TryGetValue(id, out ep)) continue;
                    if (span.Count == 0) { rows.Remove(id); vecBytes -= Bytes(ep.Chunks); continue; }
                    ep.Start = IngatDb.Num(span[0][0]);
                    ep.End = IngatDb.Num(span[0][1]);
                }
            }
        }
    }
}
