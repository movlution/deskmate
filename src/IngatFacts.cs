using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace Deskmate
{
    // One change the model asks for when a new day is folded into long-term memory
    sealed class IngatFactOp
    {
        public string Op = "", Text = "", Kind = "";
        public long Id;
    }

    // Long-term facts: each one keeps where it came from, how often it held, and how sure we are.
    // A day recap changes them through small operations (add, update, reinforce, archive), never a full rewrite,
    // so one bad answer cannot wipe what was learned. Unconfirmed facts fade on a spaced-repetition curve.
    sealed partial class IngatStore
    {
        public static readonly string[] FactKinds = { "project", "tool", "habit", "meeting", "preference", "person", "other" };

        // Active facts kept at most; the weakest are archived past it
        public const int FactCap = 80;
        // Per consolidation: a confused answer can archive or add only this many
        public const int MaxFactArchives = 5, MaxFactAdds = 10;
        const double NewConfidence = 0.6, MaxConfidence = 0.95, ArchiveBelow = 0.2, BaseHalfLifeDays = 30;
        const int MaxSources = 30;

        sealed class FactRow
        {
            public long Id, Hits, First, Last;
            public string Text, Kind, Sources, Status;
            public double Confidence, Strength;
        }

        // Older databases: new columns, a search index, and first/last taken from the old timestamp
        void MigrateFacts()
        {
            foreach (var col in new[]
            {
                "kind TEXT DEFAULT ''", "confidence REAL DEFAULT 0.6", "hits INTEGER DEFAULT 1", "first INTEGER DEFAULT 0",
                "last INTEGER DEFAULT 0", "sources TEXT DEFAULT ''", "status TEXT DEFAULT 'active'",
            })
            {
                try { db.Exec("ALTER TABLE facts ADD COLUMN " + col); }
                catch (InvalidOperationException) { }
            }
            db.Exec("CREATE VIRTUAL TABLE IF NOT EXISTS facts_fts USING fts5(text, tokenize='unicode61 remove_diacritics 2')");
            db.Tx(() =>
            {
                db.Exec("UPDATE facts SET first=COALESCE(updated,0) WHERE first=0 OR first IS NULL");
                db.Exec("UPDATE facts SET last=COALESCE(updated,0) WHERE last=0 OR last IS NULL");
                db.Exec("INSERT INTO facts_fts(rowid,text) SELECT id, text FROM facts WHERE status='active' AND id NOT IN (SELECT rowid FROM facts_fts)");
            });
        }

        static double Dbl(object o) { return o is double ? (double)o : o is long ? (long)o : 0; }

        // Strength halves every half-life; each confirmation stretches the half-life, like spaced repetition
        static double Strength(double confidence, long hits, long lastUnix, DateTime nowUtc)
        {
            double ageDays = Math.Max(0, (IngatDb.Unix(nowUtc) - lastUnix) / 86400.0);
            double halfLife = BaseHalfLifeDays * (1 + Math.Log(1 + Math.Max(1, hits)));
            return confidence * Math.Pow(0.5, ageDays / halfLife);
        }

        List<FactRow> FactRows(string where, params object[] args)
        {
            var list = new List<FactRow>();
            var now = DateTime.UtcNow;
            foreach (var r in db.Query("SELECT id, text, kind, confidence, hits, first, last, sources, status FROM facts WHERE " + where, args))
            {
                var f = new FactRow
                {
                    Id = IngatDb.Num(r[0]), Text = IngatDb.Str(r[1]), Kind = IngatDb.Str(r[2]), Confidence = Dbl(r[3]),
                    Hits = Math.Max(1, IngatDb.Num(r[4])), First = IngatDb.Num(r[5]), Last = IngatDb.Num(r[6]),
                    Sources = IngatDb.Str(r[7]), Status = IngatDb.Str(r[8]),
                };
                f.Strength = Strength(f.Confidence, f.Hits, f.Last, now);
                list.Add(f);
            }
            return list;
        }

        List<FactRow> ActiveFacts()
        {
            var list = FactRows("status='active'");
            list.Sort((a, b) => b.Strength.CompareTo(a.Strength));
            return list;
        }

        // Strongest first: the profile as it stands
        public List<string> Facts()
        {
            var list = new List<string>();
            foreach (var f in ActiveFacts()) list.Add(f.Text);
            return list;
        }

        public List<Dictionary<string, object>> FactDetails()
        {
            var list = new List<Dictionary<string, object>>();
            foreach (var f in ActiveFacts())
                list.Add(new Dictionary<string, object>
                {
                    { "id", f.Id }, { "text", f.Text }, { "kind", f.Kind }, { "confidence", Math.Round(f.Confidence, 2) },
                    { "strength", Math.Round(f.Strength, 2) }, { "seen", f.Hits }, { "first", Local(f.First) }, { "last", Local(f.Last) },
                    { "days", f.Sources.Split(new[] { '|' }, StringSplitOptions.RemoveEmptyEntries) },
                });
            return list;
        }

        // Ids of active facts matching the text, best first
        List<long> FactMatches(string text)
        {
            var ids = new List<long>();
            string fts = FtsQuery(text);
            if (fts.Length == 0) return ids;
            foreach (var r in db.Query("SELECT f.rowid FROM facts_fts f JOIN facts x ON x.id=f.rowid WHERE facts_fts MATCH ? AND x.status='active' ORDER BY bm25(facts_fts) LIMIT 40", fts))
                ids.Add(IngatDb.Num(r[0]));
            return ids;
        }

        // What a prompt needs: a few core facts always, then the ones about the question (FTS5 + semantic vectors), then the strongest of the rest
        public List<string> FactsFor(string question, int k)
        {
            return FactsFor(question, k, "", null);
        }

        public List<string> FactsFor(string question, int k, string embedModel, float[] queryVec)
        {
            var active = ActiveFacts();
            var byId = new Dictionary<long, FactRow>();
            foreach (var f in active) byId[f.Id] = f;
            var picked = new List<FactRow>();
            var taken = new HashSet<long>();
            for (int i = 0; i < active.Count && picked.Count < Math.Min(3, k); i++) { picked.Add(active[i]); taken.Add(active[i].Id); }
            var scores = new Dictionary<long, double>();
            var matches = FactMatches(question);
            for (int i = 0; i < matches.Count; i++)
            {
                FactRow f;
                if (!byId.TryGetValue(matches[i], out f) || taken.Contains(f.Id)) continue;
                scores[f.Id] = f.Strength + 1.5 / (1 + i);
            }
            if (queryVec != null && !string.IsNullOrEmpty(embedModel))
            {
                var sem = NearestItems("fact", embedModel, queryVec, 12, 0.28);
                for (int i = 0; i < sem.Count; i++)
                {
                    FactRow f;
                    if (!byId.TryGetValue(sem[i].Key, out f) || taken.Contains(f.Id)) continue;
                    double cur;
                    scores.TryGetValue(f.Id, out cur);
                    scores[f.Id] = (cur > 0 ? cur : f.Strength) + sem[i].Value * 1.4;
                }
            }
            var related = new List<KeyValuePair<FactRow, double>>(scores.Count);
            foreach (var kv in scores) related.Add(new KeyValuePair<FactRow, double>(byId[kv.Key], kv.Value));
            related.Sort((a, b) => b.Value.CompareTo(a.Value));
            foreach (var kv in related) { if (picked.Count >= k) break; picked.Add(kv.Key); taken.Add(kv.Key.Id); }
            foreach (var f in active) { if (picked.Count >= k) break; if (taken.Add(f.Id)) picked.Add(f); }
            var list = new List<string>();
            foreach (var f in picked) list.Add(f.Text);
            return list;
        }

        // The facts a consolidation may change, with their ids: those the recap touches first
        public List<Dictionary<string, object>> FactsToConsolidate(string recap, int max)
        {
            var active = ActiveFacts();
            var order = new List<FactRow>();
            var seen = new HashSet<long>();
            var byId = new Dictionary<long, FactRow>();
            foreach (var f in active) byId[f.Id] = f;
            foreach (var id in FactMatches(recap)) { FactRow f; if (byId.TryGetValue(id, out f) && seen.Add(id)) order.Add(f); }
            foreach (var f in active) if (seen.Add(f.Id)) order.Add(f);
            var list = new List<Dictionary<string, object>>();
            for (int i = 0; i < order.Count && i < max; i++)
                list.Add(new Dictionary<string, object> { { "id", order[i].Id }, { "text", order[i].Text }, { "seen", order[i].Hits }, { "last", Local(order[i].Last) } });
            return list;
        }

        static readonly Regex WordRx = new Regex(@"[\p{L}\p{N}]{3,}", RegexOptions.Compiled);

        static HashSet<string> FactWords(string text)
        {
            var set = new HashSet<string>(StringComparer.Ordinal);
            foreach (Match m in WordRx.Matches((text ?? "").ToLowerInvariant())) if (!IngatTime.Stop(m.Value)) set.Add(IdRoot(m.Value));
            return set;
        }

        // Share of words two facts have in common (Jaccard)
        public static double FactOverlap(string a, string b)
        {
            var x = FactWords(a);
            var y = FactWords(b);
            if (x.Count == 0 || y.Count == 0) return 0;
            int both = 0;
            foreach (var w in x) if (y.Contains(w)) both++;
            return both / (double)(x.Count + y.Count - both);
        }

        // The same fact in other words: most words shared, and only one side adds any.
        // When both sides have words the other lacks ("Senin" vs "Kamis", "Unity" vs "Godot"), a detail changed: not the same fact.
        public static bool SameFact(string a, string b)
        {
            var x = FactWords(a);
            var y = FactWords(b);
            if (x.Count == 0 || y.Count == 0) return false;
            int both = 0;
            foreach (var w in x) if (y.Contains(w)) both++;
            if (both / (double)(x.Count + y.Count - both) < 0.6) return false;
            return both == x.Count || both == y.Count;
        }

        // The active fact other than one that already says the same, or null
        static FactRow SameAs(List<FactRow> facts, string text, long except, bool activeOnly)
        {
            FactRow same = null;
            double best = 0;
            foreach (var x in facts)
            {
                if (x.Id == except || (activeOnly && x.Status != "active") || !SameFact(x.Text, text)) continue;
                double o = FactOverlap(x.Text, text);
                if (o > best) { best = o; same = x; }
            }
            return same;
        }

        static string AddSource(string sources, string day)
        {
            if (string.IsNullOrEmpty(day)) return sources ?? "";
            var list = new List<string>((sources ?? "").Split(new[] { '|' }, StringSplitOptions.RemoveEmptyEntries));
            if (!list.Contains(day)) list.Add(day);
            list.Sort(StringComparer.Ordinal);
            if (list.Count > MaxSources) list.RemoveRange(0, list.Count - MaxSources);
            return list.Count == 0 ? "" : "|" + string.Join("|", list.ToArray()) + "|";
        }

        static string CleanFact(string text) { return IngatPrivacy.Redact(Fmt.OneLine(text ?? "", 200)).Trim(); }

        static string CleanKind(string kind)
        {
            kind = (kind ?? "").Trim().ToLowerInvariant();
            return Array.IndexOf(FactKinds, kind) >= 0 ? kind : "other";
        }

        void Reinforce(FactRow f, string day, DateTime nowUtc)
        {
            db.Exec("UPDATE facts SET hits=hits+1, confidence=MIN(?, confidence+0.1), last=?, updated=?, sources=?, status='active' WHERE id=?",
                MaxConfidence, nowUtc, nowUtc, AddSource(f.Sources, day), f.Id);
            if (f.Status != "active") db.Exec("INSERT INTO facts_fts(rowid,text) VALUES(?,?)", f.Id, f.Text);
            f.Status = "active";
        }

        void Archive(long id)
        {
            db.Exec("UPDATE facts SET status='archived' WHERE id=?", id);
            db.Exec("DELETE FROM facts_fts WHERE rowid=?", id);
            DeleteItemVector("fact", id);
        }

        // Folds one day's operations into memory; returns what changed: added, updated, reinforced, archived
        public int[] ApplyFactOps(List<IngatFactOp> ops, string day)
        {
            var counts = new int[4];
            var now = DateTime.UtcNow;
            db.Tx(() =>
            {
                var all = FactRows("1=1");
                var byId = new Dictionary<long, FactRow>();
                foreach (var f in all) byId[f.Id] = f;
                var touched = new HashSet<long>();
                foreach (var op in ops)
                {
                    string kind = (op.Op ?? "").Trim().ToLowerInvariant();
                    FactRow f;
                    if (kind == "add")
                    {
                        string text = CleanFact(op.Text);
                        if (text.Length <= 3 || counts[0] >= MaxFactAdds) continue;
                        // The same fact in new words strengthens the one we have, archived ones included.
                        // A changed detail is a new fact: the old one stays until a later day archives it or it fades.
                        FactRow same = SameAs(all, text, 0, false);
                        if (same != null)
                        {
                            if (touched.Add(same.Id)) { Reinforce(same, day, now); counts[2]++; }
                            continue;
                        }
                        long id = db.Insert("INSERT INTO facts(text,updated,kind,confidence,hits,first,last,sources,status) VALUES(?,?,?,?,1,?,?,?,'active')",
                            text, now, CleanKind(op.Kind), NewConfidence, now, now, AddSource("", day));
                        db.Exec("INSERT INTO facts_fts(rowid,text) VALUES(?,?)", id, text);
                        var row = new FactRow { Id = id, Text = text, Kind = CleanKind(op.Kind), Confidence = NewConfidence, Hits = 1, First = IngatDb.Unix(now), Last = IngatDb.Unix(now), Sources = AddSource("", day), Status = "active" };
                        all.Add(row);
                        byId[id] = row;
                        touched.Add(id);
                        counts[0]++;
                    }
                    else if (kind == "update")
                    {
                        string text = CleanFact(op.Text);
                        if (!byId.TryGetValue(op.Id, out f) || f.Status != "active" || text.Length <= 3 || !touched.Add(f.Id)) continue;
                        // Updated into what another fact already says: merge into that one instead of keeping two
                        var twin = SameAs(all, text, f.Id, true);
                        if (twin != null)
                        {
                            if (touched.Add(twin.Id)) Reinforce(twin, day, now);
                            Archive(f.Id);
                            f.Status = "archived";
                            counts[1]++;
                            continue;
                        }
                        db.Exec("UPDATE facts SET text=?, kind=CASE WHEN ?<>'' THEN ? ELSE kind END, hits=hits+1, confidence=MAX(confidence, ?), last=?, updated=?, sources=? WHERE id=?",
                            text, op.Kind ?? "", op.Kind == null ? "" : CleanKind(op.Kind), NewConfidence, now, now, AddSource(f.Sources, day), f.Id);
                        db.Exec("DELETE FROM facts_fts WHERE rowid=?", f.Id);
                        db.Exec("INSERT INTO facts_fts(rowid,text) VALUES(?,?)", f.Id, text);
                        f.Text = text;
                        counts[1]++;
                    }
                    else if (kind == "reinforce")
                    {
                        if (!byId.TryGetValue(op.Id, out f) || f.Status != "active" || !touched.Add(f.Id)) continue;
                        Reinforce(f, day, now);
                        counts[2]++;
                    }
                    else if (kind == "delete" || kind == "archive")
                    {
                        // Archived, not erased: kept as history, and a later day can bring it back
                        if (!byId.TryGetValue(op.Id, out f) || f.Status != "active" || counts[3] >= MaxFactArchives || !touched.Add(f.Id)) continue;
                        Archive(f.Id);
                        f.Status = "archived";
                        counts[3]++;
                    }
                }
                EnforceFactCap();
            });
            return counts;
        }

        // Past the cap, the weakest active facts step back
        void EnforceFactCap()
        {
            var active = ActiveFacts();
            for (int i = FactCap; i < active.Count; i++) Archive(active[i].Id);
        }

        // Facts nothing confirmed for long enough fade out; archived ones older than the episode window go for good
        public int DecayFacts(DateTime purgeBeforeUtc)
        {
            int faded = 0;
            db.Tx(() =>
            {
                foreach (var f in FactRows("status='active'"))
                    if (f.Strength < ArchiveBelow) { Archive(f.Id); faded++; }
                EnforceFactCap();
                db.Exec("DELETE FROM facts WHERE status='archived' AND last<?", purgeBeforeUtc);
            });
            return faded;
        }

        // Forgetting days also forgets what was learned only from them; facts other days confirmed stay
        void ForgetFactsTx(DateTime fromUtc, DateTime toUtc)
        {
            var days = new HashSet<string>();
            for (var d = fromUtc.ToLocalTime().Date; d <= toUtc.ToLocalTime().Date; d = d.AddDays(1))
                days.Add(d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
            foreach (var f in FactRows("sources<>''"))
            {
                var keep = new List<string>();
                bool hit = false;
                foreach (var s in f.Sources.Split(new[] { '|' }, StringSplitOptions.RemoveEmptyEntries))
                    if (days.Contains(s)) hit = true; else keep.Add(s);
                if (!hit) continue;
                if (keep.Count == 0)
                {
                    db.Exec("DELETE FROM facts_fts WHERE rowid=?", f.Id);
                    db.Exec("DELETE FROM facts WHERE id=?", f.Id);
                    DeleteItemVector("fact", f.Id);
                }
                else
                {
                    db.Exec("UPDATE facts SET sources=?, hits=MAX(1, hits-?) WHERE id=?", "|" + string.Join("|", keep.ToArray()) + "|",
                        f.Sources.Split(new[] { '|' }, StringSplitOptions.RemoveEmptyEntries).Length - keep.Count, f.Id);
                }
            }
        }

        public long FactCount() { return db.Long("SELECT COUNT(*) FROM facts WHERE status='active'"); }
    }
}
