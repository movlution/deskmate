using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace Deskmate
{
    // One stretch of work in one app or project
    sealed class IngatEpisode
    {
        public long Id;
        public DateTime StartUtc, EndUtc;
        public string App = "", Exe = "", Project = "", Title = "", Url = "", Kind = "", Summary = "", Body = "", Context = "";
        public bool Closed;
        public double Score;
        public int BestChunk = -1;

        public Dictionary<string, object> ToJson(bool withBody)
        {
            var d = new Dictionary<string, object>
            {
                { "id", Id },
                { "start", StartUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture) },
                { "end", EndUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture) },
                { "minutes", Math.Max(0, (int)Math.Round((EndUtc - StartUtc).TotalMinutes)) },
                { "app", App },
                { "project", Project },
                { "title", Title },
                { "kind", Kind },
            };
            if (Url.Length > 0) d["url"] = Url;
            if (Summary.Length > 0) d["summary"] = Summary;
            if (Context.Length > 0) IngatContext.Parse(Context).WriteTo(d);
            if (withBody && Body.Length > 0) d["text"] = Body.Length > 2400 ? Body.Substring(0, 2400) + "…" : Body;
            return d;
        }
    }

    // Activity memory on disk: events → episodes → days → facts
    sealed partial class IngatStore : IDisposable
    {
        public static string Dir { get { return Path.Combine(Settings.Dir, "ingat"); } }
        public static string DbPath { get { return Path.Combine(Dir, "ingat.db"); } }

        // Body text an episode keeps for search
        public const int BodyCap = 12000;

        const int SchemaVersion = 4;
        readonly IngatDb db;

        public IngatStore(string path)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            db = new IngatDb(path);
            // Connection-level tuning: WAL + NORMAL sync, in-memory temp B-trees, 8 MB page cache, 64 MB mmap
            db.Script(
                "PRAGMA journal_mode=WAL;" +
                "PRAGMA synchronous=NORMAL;" +
                "PRAGMA temp_store=MEMORY;" +
                "PRAGMA cache_size=-8000;" +
                "PRAGMA wal_autocheckpoint=1000");
            try { db.Exec("PRAGMA mmap_size=67108864"); } catch (InvalidOperationException) { }

            if (db.Long("PRAGMA user_version") < SchemaVersion)
            {
                db.Script(
                    "CREATE TABLE IF NOT EXISTS episodes(id INTEGER PRIMARY KEY, start INTEGER, end INTEGER, app TEXT, exe TEXT, project TEXT, title TEXT, url TEXT, kind TEXT DEFAULT '', summary TEXT DEFAULT '', body TEXT DEFAULT '', closed INTEGER DEFAULT 0, embedded INTEGER DEFAULT 0, context TEXT DEFAULT '');" +
                    "CREATE TABLE IF NOT EXISTS events(id INTEGER PRIMARY KEY, t INTEGER, episode INTEGER, source TEXT, app TEXT, title TEXT, url TEXT, text TEXT, hash TEXT);" +
                    "CREATE VIRTUAL TABLE IF NOT EXISTS episodes_fts USING fts5(title, url, body, summary, app, tokenize='unicode61 remove_diacritics 2');" +
                    "CREATE TABLE IF NOT EXISTS embeddings(episode INTEGER PRIMARY KEY, model TEXT, vec BLOB);" +
                    "CREATE TABLE IF NOT EXISTS daily(date TEXT PRIMARY KEY, summary TEXT, made INTEGER);" +
                    "CREATE TABLE IF NOT EXISTS facts(id INTEGER PRIMARY KEY, text TEXT, updated INTEGER);" +
                    "CREATE TABLE IF NOT EXISTS meetings(id INTEGER PRIMARY KEY, path TEXT UNIQUE, start INTEGER, title TEXT, episode INTEGER);" +
                    "CREATE TABLE IF NOT EXISTS meta(k TEXT PRIMARY KEY, v TEXT);" +
                    // People and conversations, the same person across apps
                    "CREATE TABLE IF NOT EXISTS people(id INTEGER PRIMARY KEY, name TEXT, norm TEXT UNIQUE, aliases TEXT DEFAULT '', about TEXT DEFAULT '', last INTEGER, mentions INTEGER DEFAULT 0);" +
                    "CREATE TABLE IF NOT EXISTS conversations(id INTEGER PRIMARY KEY, app TEXT, name TEXT, norm TEXT, kind TEXT, person INTEGER, last INTEGER, digest TEXT DEFAULT '', digest_t INTEGER DEFAULT 0, UNIQUE(app, norm));" +
                    "CREATE TABLE IF NOT EXISTS messages(id INTEGER PRIMARY KEY, conversation INTEGER, t INTEGER, sender TEXT, mine INTEGER, text TEXT, hash TEXT UNIQUE, episode INTEGER);" +
                    "CREATE VIRTUAL TABLE IF NOT EXISTS messages_fts USING fts5(sender, text, conv, tokenize='unicode61 remove_diacritics 2');" +
                    "CREATE TABLE IF NOT EXISTS commitments(id INTEGER PRIMARY KEY, conversation INTEGER, person INTEGER, who TEXT, what TEXT, due TEXT, t INTEGER, done INTEGER DEFAULT 0, hash TEXT UNIQUE);" +
                    "CREATE TABLE IF NOT EXISTS episode_people(episode INTEGER, person INTEGER, PRIMARY KEY(episode, person));" +
                    "CREATE TABLE IF NOT EXISTS episode_entities(episode INTEGER, kind TEXT, value TEXT, PRIMARY KEY(episode, kind, value))");
                // Columns added after the first release: older databases get them here
                foreach (var col in new[]
                {
                    "episodes|channel TEXT DEFAULT ''", "episodes|digested INTEGER DEFAULT 0", "episodes|context TEXT DEFAULT ''",
                    // sent | received | draft (written, never sent); said: the time the chat showed
                    "messages|status TEXT DEFAULT ''", "messages|said TEXT DEFAULT ''",
                    // Where a conversation stands: topic, who waits for whom, questions still open, decisions
                    "conversations|topic TEXT DEFAULT ''", "conversations|status TEXT DEFAULT ''", "conversations|last_from TEXT DEFAULT ''",
                    "conversations|open TEXT DEFAULT ''", "conversations|decisions TEXT DEFAULT ''",
                })
                {
                    var p = col.Split('|');
                    try { db.Exec("ALTER TABLE " + p[0] + " ADD COLUMN " + p[1]); }
                    catch (InvalidOperationException) { }
                }
                MigrateFacts();
                MigrateVectors();
                // Targeted B-tree and partial indexes for range seeks, background queues, chats, and retention
                db.Script(
                    "CREATE INDEX IF NOT EXISTS episodes_start ON episodes(start);" +
                    "CREATE INDEX IF NOT EXISTS episodes_end_start ON episodes(end, start);" +
                    "CREATE INDEX IF NOT EXISTS episodes_open ON episodes(id) WHERE closed=0;" +
                    "CREATE INDEX IF NOT EXISTS episodes_need_summary ON episodes(start DESC) WHERE closed=1 AND summary='';" +
                    "CREATE INDEX IF NOT EXISTS episodes_need_embed ON episodes(start DESC) WHERE closed=1 AND embedded=0 AND summary<>'';" +
                    "CREATE INDEX IF NOT EXISTS episodes_need_digest ON episodes(start) WHERE closed=1 AND digested=0 AND channel<>'';" +
                    "CREATE INDEX IF NOT EXISTS episodes_meeting_end ON episodes(end, start) WHERE kind='meeting';" +
                    "CREATE INDEX IF NOT EXISTS events_t ON events(t);" +
                    "CREATE INDEX IF NOT EXISTS events_episode ON events(episode, hash);" +
                    "CREATE INDEX IF NOT EXISTS events_episode_t ON events(episode, t);" +
                    "CREATE INDEX IF NOT EXISTS messages_conv ON messages(conversation, t);" +
                    "CREATE INDEX IF NOT EXISTS messages_t ON messages(t);" +
                    "CREATE INDEX IF NOT EXISTS conversations_inbox ON conversations(last DESC) WHERE status='awaiting_me';" +
                    "CREATE INDEX IF NOT EXISTS conversations_person ON conversations(person, last DESC);" +
                    "CREATE INDEX IF NOT EXISTS conversations_norm ON conversations(norm, last DESC);" +
                    "CREATE INDEX IF NOT EXISTS commitments_open_person ON commitments(person, t DESC) WHERE done=0;" +
                    "CREATE INDEX IF NOT EXISTS commitments_open_conv ON commitments(conversation, t DESC) WHERE done=0;" +
                    "CREATE INDEX IF NOT EXISTS meetings_episode ON meetings(episode);" +
                    "CREATE INDEX IF NOT EXISTS facts_status ON facts(status, last);" +
                    "CREATE INDEX IF NOT EXISTS people_last ON people(last DESC);" +
                    "CREATE INDEX IF NOT EXISTS people_mentions ON people(mentions DESC);" +
                    "CREATE INDEX IF NOT EXISTS episode_entities_kv ON episode_entities(value, kind, episode)");
                if (Meta("ent.upto").Length == 0)
                    SetMeta("ent.upto", db.Long("SELECT COALESCE(MAX(id),0) FROM episodes").ToString(CultureInfo.InvariantCulture));
                db.Exec("PRAGMA user_version=" + SchemaVersion);
            }
            MigrateTrigram();
        }

        public void Dispose()
        {
            try { FlushIndex(); } catch (InvalidOperationException) { }
            try { db.Exec("PRAGMA optimize"); } catch (InvalidOperationException) { }
            db.Dispose();
        }

        // Episodes

        public long Open(DateTime t, string app, string exe, string project, string title, string url, string kind)
        {
            return Open(t, app, exe, project, title, url, kind, "");
        }

        public long Open(DateTime t, string app, string exe, string project, string title, string url, string kind, string channel)
        {
            string autoKind = string.IsNullOrEmpty(kind) ? IngatStructure.Classify(exe, url, title, "") : kind;
            var ctx = new IngatContext();
            ctx.Absorb(t, title, url, "", null);
            long id = 0;
            db.Tx(() =>
            {
                id = db.Insert("INSERT INTO episodes(start,end,app,exe,project,title,url,kind,body,context,channel) VALUES(?,?,?,?,?,?,?,?,?,?,?)",
                    t, t, app ?? "", exe ?? "", project ?? "", title ?? "", url ?? "", autoKind, title ?? "", ctx.Encode(), channel ?? "");
                SyncEntitiesTx(id, app, project, url, ctx);
            });
            // Deferred FTS/trigram indexing: avoids indexing brief pass-through episodes before DropTiny removes them,
            // while Search/SearchSubstring/Close always flush dirty episodes first.
            MarkDirty(id);
            return id;
        }

        // Later title and URL win; new lines merge without duplicating scrolled regions, and structured steps/anchors accumulate
        public void Touch(long id, DateTime t, string title, string url, string text)
        {
            Touch(id, t, title, url, text, null);
        }

        public void Touch(long id, DateTime t, string title, string url, string text, IngatSnapshot snap)
        {
            db.Tx(() => TouchTx(id, t, title, url, text, snap));
        }

        // Atomic capture update: merges episode body/context and records the event in one WAL transaction
        public IngatEpisode RecordCapture(long id, DateTime t, string title, string url, string text, IngatSnapshot snap, bool addEvent, string source, string app)
        {
            IngatEpisode updated = null;
            db.Tx(() =>
            {
                TouchTx(id, t, title, url, text, snap);
                if (addEvent) AddEvent(t, id, source, app, title, url, text);
                updated = GetLite(id);
            });
            return updated;
        }

        // Still on the same thing with nothing new to merge: only the end time moves. SQLite rewrites the
        // whole record on any UPDATE, 12 KB body overflow pages included, so the open episode's end is
        // saved at most every EndSeconds; Close writes the exact end, CloseStale covers a crash.
        const double EndSeconds = 30;
        long endId;
        DateTime endSavedUtc;

        public void TouchEnd(long id, DateTime t)
        {
            lock (db.Gate)
            {
                if (id == endId && (DateTime.UtcNow - endSavedUtc).TotalSeconds < EndSeconds) return;
                db.Exec("UPDATE episodes SET end=? WHERE id=?", t, id);
                EndSaved(id);
            }
        }

        void EndSaved(long id)
        {
            endId = id;
            endSavedUtc = DateTime.UtcNow;
        }

        void TouchTx(long id, DateTime t, string title, string url, string text, IngatSnapshot snap)
        {
            var rows = db.Query("SELECT body, kind, context, exe, title, url, app, project FROM episodes WHERE id=?", id);
            if (rows.Count == 0) return;
            string origBody = IngatDb.Str(rows[0][0]);
            string body = origBody;
            string curKind = IngatDb.Str(rows[0][1]);
            string rawCtx = IngatDb.Str(rows[0][2]);
            string exe = IngatDb.Str(rows[0][3]);
            string origTitle = IngatDb.Str(rows[0][4]);
            string origUrl = IngatDb.Str(rows[0][5]);
            string app = IngatDb.Str(rows[0][6]);
            string project = IngatDb.Str(rows[0][7]);
            string effTitle = !string.IsNullOrEmpty(title) ? title : origTitle;
            string effUrl = !string.IsNullOrEmpty(url) ? url : origUrl;

            // Combine all non-empty pieces into a single MergeBody pass instead of splitting body up to 3x
            var addSb = new StringBuilder();
            if (!string.IsNullOrEmpty(title) && origBody.IndexOf(title, StringComparison.Ordinal) < 0) addSb.Append(title);
            if (!string.IsNullOrEmpty(url) && origBody.IndexOf(url, StringComparison.Ordinal) < 0)
            {
                if (addSb.Length > 0) addSb.Append('\n');
                addSb.Append(url);
            }
            if (!string.IsNullOrEmpty(text))
            {
                if (addSb.Length > 0) addSb.Append('\n');
                addSb.Append(text);
            }
            if (addSb.Length > 0) body = IngatStructure.MergeBody(body, addSb.ToString(), BodyCap);

            var ctx = IngatContext.Parse(rawCtx);
            ctx.Absorb(t, effTitle, effUrl, text, snap);

            string nextKind = curKind;
            if ((nextKind.Length == 0 || nextKind == "other") && curKind != "meeting")
            {
                string guessed = snap != null && snap.Kind.Length > 0 ? snap.Kind : IngatStructure.Classify(exe, effUrl, effTitle, text);
                if (guessed.Length > 0 && guessed != "other") nextKind = guessed;
                else if (nextKind.Length == 0) nextKind = guessed;
            }

            string nextCtx = ctx.Encode();
            // A reread that found only what the episode already holds: nothing to rewrite but the end time
            if (body == origBody && nextCtx == rawCtx && nextKind == curKind &&
                effTitle == origTitle && effUrl == origUrl)
            {
                TouchEnd(id, t);
                return;
            }
            db.Exec("UPDATE episodes SET end=?, title=CASE WHEN ?<>'' THEN ? ELSE title END, url=CASE WHEN ?<>'' THEN ? ELSE url END, kind=?, body=?, context=? WHERE id=?",
                t, title ?? "", title ?? "", url ?? "", url ?? "", nextKind, body, nextCtx, id);
            if (nextCtx != rawCtx || effUrl != origUrl) SyncEntitiesTx(id, app, project, effUrl, ctx);
            lock (db.Gate) EndSaved(id);
            // The open episode must be findable too, not only once it closes: reindexed at most every
            // ReindexSeconds while it grows, and always before a search reads the index
            MarkDirty(id);
        }

        // Rewriting a 12 KB FTS row on every capture is the hot path's main cost; batch it instead
        const double ReindexSeconds = 30;
        readonly Dictionary<long, DateTime> dirty = new Dictionary<long, DateTime>();

        void MarkDirty(long id)
        {
            lock (db.Gate)
            {
                DateTime since;
                if (!dirty.TryGetValue(id, out since)) { dirty[id] = DateTime.UtcNow; return; }
                if ((DateTime.UtcNow - since).TotalSeconds < ReindexSeconds) return;
                dirty.Remove(id);
                Reindex(id);
            }
        }

        // Pending episodes reach the index before anything reads it
        void FlushIndex()
        {
            lock (db.Gate)
            {
                if (dirty.Count == 0) return;
                var ids = new List<long>(dirty.Keys);
                dirty.Clear();
                db.Tx(() => { foreach (var id in ids) Reindex(id); });
            }
        }

        public void Close(long id, DateTime t)
        {
            lock (db.Gate)
            {
                dirty.Remove(id);
                db.Tx(() =>
                {
                    db.Exec("UPDATE episodes SET end=MAX(end,?), closed=1 WHERE id=?", t, id);
                    Reindex(id);
                });
            }
            VecMoved(id);
        }

        // Whatever a crash left open is closed at its last sighting
        // and indexed again, since a crash can leave its last ReindexSeconds of text out of the index
        public void CloseStale()
        {
            db.Tx(() =>
            {
                foreach (var r in db.Query("SELECT id FROM episodes WHERE closed=0")) Reindex(IngatDb.Num(r[0]));
                db.Exec("UPDATE episodes SET closed=1 WHERE closed=0");
            });
        }

        // Brief pass-throughs under 8 s without typed/copied/sent actions are dropped; quick reference lookups stay
        public void DropTiny(long id)
        {
            int dropped = 0;
            db.Tx(() =>
            {
                dropped = db.Exec("DELETE FROM episodes WHERE id=? AND end-start<8 AND kind<>'meeting' AND NOT EXISTS(SELECT 1 FROM events WHERE episode=episodes.id AND source IN ('sent','typed','clipboard'))", id);
                if (dropped > 0)
                {
                    dirty.Remove(id);
                    if (eventHashEp == id) { eventHashEp = 0; eventHashes.Clear(); }
                    db.Exec("DELETE FROM episodes_fts WHERE rowid=?", id);
                    if (triOn) db.Exec("DELETE FROM episodes_tri WHERE rowid=?", id);
                    db.Exec("DELETE FROM episode_entities WHERE episode=?", id);
                    db.Exec("DELETE FROM events WHERE episode=?", id);
                }
            });
            if (dropped > 0) VecDrop();
        }

        public void Reindex(long id)
        {
            db.Tx(() =>
            {
                db.Exec("DELETE FROM episodes_fts WHERE rowid=?", id);
                var rows = db.Query("SELECT title, project, url, body, summary, app, context FROM episodes WHERE id=?", id);
                if (rows.Count == 0) return;
                var r = rows[0];
                var ctx = IngatContext.Parse(IngatDb.Str(r[6]));
                string titleIdx = (IngatDb.Str(r[0]) + " " + IngatDb.Str(r[1]) + " " + ctx.SearchTitles()).Trim();
                string sumIdx = (IngatDb.Str(r[4]) + " " + ctx.SearchSummary()).Trim();
                db.Exec("INSERT INTO episodes_fts(rowid,title,url,body,summary,app) VALUES(?,?,?,?,?,?)",
                    id, titleIdx, IngatDb.Str(r[2]), IngatDb.Str(r[3]), sumIdx, IngatDb.Str(r[5]));
                TriIndex(id, titleIdx, sumIdx);
            });
        }

        // Substring index over the short fields (titles, summaries, steps, anchors): "kirim" finds "dikirim",
        // "deskm" finds "Deskmate". Bodies stay out: a trigram index of 12 KB bodies would outgrow the data.
        // Windows builds whose SQLite has no trigram tokenizer simply go without it.
        bool triOn;

        void MigrateTrigram()
        {
            try
            {
                bool fresh = db.Long("SELECT COUNT(*) FROM sqlite_master WHERE name='episodes_tri'") == 0;
                db.Exec("CREATE VIRTUAL TABLE IF NOT EXISTS episodes_tri USING fts5(title, summary, tokenize='trigram')");
                triOn = true;
                // Episodes from before the index are filled in by the worker, oldest first, up to the id they had then
                if (fresh) SetMeta("tri.upto", db.Long("SELECT COALESCE(MAX(id),0) FROM episodes").ToString(CultureInfo.InvariantCulture));
            }
            catch (InvalidOperationException ex) { triOn = false; Log.Write("ingat: indeks trigram tidak tersedia (" + Fmt.OneLine(ex.Message, 80) + ")"); }
        }

        void TriIndex(long id, string title, string summary)
        {
            if (!triOn) return;
            db.Exec("DELETE FROM episodes_tri WHERE rowid=?", id);
            db.Exec("INSERT INTO episodes_tri(rowid,title,summary) VALUES(?,?,?)", id, title, summary);
        }

        void TriDelete(string where, params object[] args)
        {
            if (triOn) db.Exec("DELETE FROM episodes_tri WHERE rowid IN (SELECT id FROM episodes WHERE " + where + ")", args);
        }

        // One worker step: false when nothing is left to fill
        public bool BackfillTrigram(int max)
        {
            if (!triOn) return false;
            long upto, through;
            long.TryParse(Meta("tri.upto"), out upto);
            long.TryParse(Meta("tri.through"), out through);
            if (through >= upto) return false;
            db.Tx(() =>
            {
                long last = through;
                foreach (var r in db.Query("SELECT id, title, project, summary, context FROM episodes WHERE id>? AND id<=? ORDER BY id LIMIT ?", through, upto, max))
                {
                    last = IngatDb.Num(r[0]);
                    var ctx = IngatContext.Parse(IngatDb.Str(r[4]));
                    TriIndex(last, (IngatDb.Str(r[1]) + " " + IngatDb.Str(r[2]) + " " + ctx.SearchTitles()).Trim(), (IngatDb.Str(r[3]) + " " + ctx.SearchSummary()).Trim());
                }
                SetMeta("tri.through", (last == through ? upto : last).ToString(CultureInfo.InvariantCulture));
            });
            return true;
        }

        // Words of 3+ letters as substrings of titles and summaries, best first
        public List<IngatEpisode> SearchSubstring(string q, DateTime fromUtc, DateTime toUtc, int limit)
        {
            return SearchSubstringCore(q, fromUtc, toUtc, limit, false);
        }

        // Same substring search without loading 12 KB bodies during hybrid candidate scoring
        public List<IngatEpisode> SearchSubstringLite(string q, DateTime fromUtc, DateTime toUtc, int limit)
        {
            return SearchSubstringCore(q, fromUtc, toUtc, limit, true);
        }

        List<IngatEpisode> SearchSubstringCore(string q, DateTime fromUtc, DateTime toUtc, int limit, bool lite)
        {
            var list = new List<IngatEpisode>();
            if (!triOn) return list;
            var parts = new List<string>();
            foreach (var t in QueryTerms(q))
            {
                string w = t.Replace("\"", "");
                if (w.Length < 3) continue;
                parts.Add("\"" + w + "\"");
                foreach (var f in IdForms(w)) parts.Add("\"" + f + "\"");
            }
            if (parts.Count == 0) return list;
            FlushIndex();
            foreach (var r in db.Query("SELECT " + (lite ? LiteColsE : ColsE) + ", bm25(episodes_tri) AS s FROM episodes_tri JOIN episodes e ON e.id=episodes_tri.rowid " +
                "WHERE episodes_tri MATCH ? AND e.end>=? AND e.start<=? ORDER BY s LIMIT ?", string.Join(" OR ", parts), fromUtc, toUtc, limit))
            {
                var ep = Read(r);
                ep.Score = r[13] is double ? -(double)r[13] : 0;
                list.Add(ep);
            }
            return list;
        }

        const string ColsE = "e.id,e.start,e.end,e.app,e.exe,e.project,e.title,e.url,e.kind,e.summary,e.body,e.closed,e.context";
        const string LiteColsE = "e.id,e.start,e.end,e.app,e.exe,e.project,e.title,e.url,e.kind,e.summary,'',e.closed,e.context";

        public void SetSummary(long id, string summary, string kind)
        {
            SetSummary(id, summary, kind, null, null);
        }

        public void SetSummary(long id, string summary, string kind, string[] points, string[] anchors)
        {
            db.Tx(() =>
            {
                if (points != null || anchors != null)
                {
                    var rows = db.Query("SELECT context, app, project, url FROM episodes WHERE id=?", id);
                    string raw = rows.Count > 0 ? IngatDb.Str(rows[0][0]) : "";
                    var ctx = IngatContext.Parse(raw);
                    ctx.ApplyAi(points, anchors);
                    db.Exec("UPDATE episodes SET summary=?, kind=CASE WHEN ?<>'' THEN ? ELSE kind END, context=? WHERE id=?",
                        summary ?? "", kind ?? "", kind ?? "", ctx.Encode(), id);
                    if (rows.Count > 0) SyncEntitiesTx(id, IngatDb.Str(rows[0][1]), IngatDb.Str(rows[0][2]), IngatDb.Str(rows[0][3]), ctx);
                }
                else
                {
                    db.Exec("UPDATE episodes SET summary=?, kind=CASE WHEN ?<>'' THEN ? ELSE kind END WHERE id=?", summary ?? "", kind ?? "", kind ?? "", id);
                }
                Reindex(id);
            });
        }

        // Per-episode hash cache avoids repeated SQLite duplicate probes during rapid captures
        long eventHashEp;
        readonly HashSet<string> eventHashes = new HashSet<string>(StringComparer.Ordinal);

        public void AddEvent(DateTime t, long episode, string source, string app, string title, string url, string text)
        {
            string hash = Json.Sha256(source + "|" + app + "|" + title + "|" + url + "|" + text).Substring(0, 16);
            lock (db.Gate)
            {
                if (episode == eventHashEp && eventHashes.Contains(hash)) return;
                // Same thing seen again inside this episode: once is enough (LIMIT 1 stops on first hit)
                if (db.Scalar("SELECT 1 FROM events WHERE episode=? AND hash=? LIMIT 1", episode, hash) != null)
                {
                    RememberEventHash(episode, hash);
                    return;
                }
                db.Exec("INSERT INTO events(t,episode,source,app,title,url,text,hash) VALUES(?,?,?,?,?,?,?,?)", t, episode, source, app, title, url, text, hash);
                RememberEventHash(episode, hash);
            }
        }

        void RememberEventHash(long episode, string hash)
        {
            if (episode != eventHashEp) { eventHashEp = episode; eventHashes.Clear(); }
            if (eventHashes.Count >= 512) eventHashes.Clear();
            eventHashes.Add(hash);
        }

        const string Cols = "id,start,end,app,exe,project,title,url,kind,summary,body,closed,context";

        static IngatEpisode Read(object[] r)
        {
            return new IngatEpisode
            {
                Id = IngatDb.Num(r[0]),
                StartUtc = IngatDb.FromUnix(IngatDb.Num(r[1])),
                EndUtc = IngatDb.FromUnix(IngatDb.Num(r[2])),
                App = IngatDb.Str(r[3]), Exe = IngatDb.Str(r[4]), Project = IngatDb.Str(r[5]), Title = IngatDb.Str(r[6]),
                Url = IngatDb.Str(r[7]), Kind = IngatDb.Str(r[8]), Summary = IngatDb.Str(r[9]), Body = IngatDb.Str(r[10]),
                Closed = IngatDb.Num(r[11]) != 0,
                Context = r.Length > 12 ? IngatDb.Str(r[12]) : "",
            };
        }

        static List<IngatEpisode> ReadAll(List<object[]> rows)
        {
            var list = new List<IngatEpisode>(rows.Count);
            foreach (var r in rows) list.Add(Read(r));
            return list;
        }

        public IngatEpisode Get(long id)
        {
            var rows = db.Query("SELECT " + Cols + " FROM episodes WHERE id=?", id);
            return rows.Count > 0 ? Read(rows[0]) : null;
        }

        public IngatEpisode GetLite(long id)
        {
            var rows = db.Query("SELECT " + LiteCols + " FROM episodes WHERE id=?", id);
            return rows.Count > 0 ? Read(rows[0]) : null;
        }

        // Batch-loads full episodes for a small set of winner ids in one round-trip without polluting the statement cache
        public Dictionary<long, IngatEpisode> GetMany(IList<long> ids)
        {
            return GetManyCore(ids, Cols);
        }

        // Batch-loads lightweight episodes (no 12 KB bodies) for semantic candidate scoring
        public Dictionary<long, IngatEpisode> GetManyLite(IList<long> ids)
        {
            return GetManyCore(ids, LiteCols);
        }

        Dictionary<long, IngatEpisode> GetManyCore(IList<long> ids, string cols)
        {
            var map = new Dictionary<long, IngatEpisode>(ids == null ? 0 : ids.Count);
            if (ids == null || ids.Count == 0) return map;
            var sb = new StringBuilder("SELECT ");
            sb.Append(cols).Append(" FROM episodes WHERE id IN (");
            for (int i = 0; i < ids.Count; i++)
            {
                if (i > 0) sb.Append(',');
                sb.Append(ids[i].ToString(CultureInfo.InvariantCulture));
            }
            sb.Append(')');
            foreach (var r in db.QueryDirect(sb.ToString()))
            {
                var ep = Read(r);
                map[ep.Id] = ep;
            }
            return map;
        }

        public List<IngatEpisode> Timeline(DateTime fromUtc, DateTime toUtc, int limit)
        {
            return ReadAll(db.Query("SELECT " + Cols + " FROM episodes WHERE end>=? AND start<=? ORDER BY start LIMIT ?", fromUtc, toUtc, limit));
        }

        // Same rows without the body (up to 12 KB each): for callers that only group, count or list episodes
        const string LiteCols = "id,start,end,app,exe,project,title,url,kind,summary,'',closed,context";

        public List<IngatEpisode> TimelineLite(DateTime fromUtc, DateTime toUtc, int limit)
        {
            return ReadAll(db.Query("SELECT " + LiteCols + " FROM episodes WHERE end>=? AND start<=? ORDER BY start LIMIT ?", fromUtc, toUtc, limit));
        }

        public List<IngatEpisode> Recent(int limit)
        {
            return ReadAll(db.Query("SELECT " + Cols + " FROM episodes ORDER BY start DESC LIMIT ?", limit));
        }

        public List<IngatEpisode> RecentLite(int limit)
        {
            return ReadAll(db.Query("SELECT " + LiteCols + " FROM episodes ORDER BY start DESC LIMIT ?", limit));
        }

        public List<IngatEpisode> Meetings(DateTime fromUtc, DateTime toUtc, int limit)
        {
            return ReadAll(db.Query("SELECT " + Cols + " FROM episodes WHERE kind='meeting' AND end>=? AND start<=? ORDER BY start DESC LIMIT ?", fromUtc, toUtc, limit));
        }

        public List<IngatEpisode> MeetingsLite(DateTime fromUtc, DateTime toUtc, int limit)
        {
            return ReadAll(db.Query("SELECT " + LiteCols + " FROM episodes WHERE kind='meeting' AND end>=? AND start<=? ORDER BY start DESC LIMIT ?", fromUtc, toUtc, limit));
        }

        // Fast 4-column streak calculation over the last 6 hours using episodes_end_start
        public int StreakMinutes(string project, string exe, DateTime nowUtc)
        {
            var rows = db.Query("SELECT start, end, project, exe FROM episodes WHERE end>=? AND start<=? ORDER BY start DESC LIMIT 400", nowUtc.AddHours(-6), nowUtc);
            double minutes = 0;
            DateTime edge = nowUtc;
            bool byProj = !string.IsNullOrEmpty(project);
            for (int i = 0; i < rows.Count; i++)
            {
                var r = rows[i];
                var s = IngatDb.FromUnix(IngatDb.Num(r[0]));
                var e = IngatDb.FromUnix(IngatDb.Num(r[1]));
                bool same = byProj ? IngatDb.Str(r[2]) == project : IngatDb.Str(r[3]) == exe;
                if (!same) { if ((e - s).TotalMinutes > 10) break; continue; }
                if ((edge - e).TotalMinutes > 10) break;
                minutes += Math.Max(0, (e - s).TotalMinutes);
                edge = s;
            }
            return (int)Math.Round(minutes);
        }

        // Words become quoted OR terms: no FTS syntax from user text reaches SQLite
        // Lowercase topic words of a question: no stop words, no time phrases, at most 12
        public static List<string> QueryTerms(string q)
        {
            var terms = new List<string>();
            var sb = new StringBuilder();
            foreach (char c in IngatTime.StripSpans(q) + " ")
            {
                if (char.IsLetterOrDigit(c) || c == '_' || c == '.' || c == '-') { sb.Append(c); continue; }
                string w = sb.ToString().Trim('.', '-');
                sb.Length = 0;
                if (w.Length < 2 || IngatTime.Stop(w)) continue;
                if (!terms.Contains(w.ToLowerInvariant())) terms.Add(w.ToLowerInvariant());
                if (terms.Count == 12) break;
            }
            return terms;
        }

        public static string FtsQuery(string q)
        {
            var terms = QueryTerms(q);
            if (terms.Count == 0) return "";
            var parts = new List<string>();
            foreach (var t in terms)
            {
                string w = t.Replace("\"", "");
                // Prefix match on longer words: "embedding" finds "embeddings", "kirim" finds "kirimkan"
                parts.Add("\"" + w + "\"" + (Prefixable(w) ? "*" : ""));
                // unicode61 has no Indonesian stemmer: also look for the bare forms, so "dikirimkan" finds "dikirim" and "kirim"
                foreach (var f in IdForms(w))
                    if (!IngatTime.Stop(f)) parts.Add("\"" + f + "\"*");
            }
            return string.Join(" OR ", parts);
        }

        static bool Prefixable(string w)
        {
            if (w.Length < 4) return false;
            foreach (char c in w) if (char.IsLetter(c)) return true;
            return false;
        }

        static readonly string[] IdPrefixes = { "ter", "ber", "di", "ke", "se" };
        static readonly string[] IdSuffixes = { "nya", "kan", "lah", "kah", "an" };

        static bool IsVowel(char c) { return c == 'a' || c == 'e' || c == 'i' || c == 'o' || c == 'u'; }

        // Conservative: Indonesian affixes (including nasalized me-/pe- rules) and light English suffixes.
        public static string IdRoot(string w)
        {
            var forms = IdForms(w);
            return forms.Count > 0 ? forms[forms.Count - 1] : w;
        }

        // The word without its suffix, then without its prefix too; the word itself is not included
        public static List<string> IdForms(string w)
        {
            var forms = new List<string>();
            if (string.IsNullOrEmpty(w) || w.Length < 5) return forms;
            string lw = w.ToLowerInvariant();
            for (int i = 0; i < lw.Length; i++) if (!char.IsLetter(lw[i])) return forms;
            Action<string> add = f =>
            {
                if (f.Length >= 4 && f != lw && !forms.Contains(f)) forms.Add(f);
            };

            string r = lw;
            foreach (var s in IdSuffixes)
            {
                // me-...-an is not an Indonesian circumfix (prevents stripping -an from roots like "menyimpan", "memesan")
                if (s == "an" && r.StartsWith("me", StringComparison.Ordinal)) continue;
                if (r.Length - s.Length >= 5 && r.EndsWith(s, StringComparison.Ordinal)) { r = r.Substring(0, r.Length - s.Length); add(r); break; }
            }

            bool prefixed = false;
            foreach (var p in IdPrefixes)
            {
                if (r.Length - p.Length >= 5 && r.StartsWith(p, StringComparison.Ordinal))
                {
                    add(r.Substring(p.Length));
                    prefixed = true;
                    break;
                }
            }
            if (!prefixed)
            {
                // Indonesian nasalized me-/pe- prefixes: mengirim->kirim, menulis->tulis, membuat->buat, menyimpan->simpan, pembayaran->bayar
                if ((r.StartsWith("meng", StringComparison.Ordinal) || r.StartsWith("peng", StringComparison.Ordinal)) && r.Length >= 8)
                {
                    string rest = r.Substring(4);
                    if (IsVowel(rest[0])) add("k" + rest);
                    if (rest.Length >= 5) add(rest);
                    prefixed = true;
                }
                else if ((r.StartsWith("meny", StringComparison.Ordinal) || r.StartsWith("peny", StringComparison.Ordinal)) && r.Length >= 8)
                {
                    string rest = r.Substring(4);
                    if (IsVowel(rest[0])) { add("s" + rest); prefixed = true; }
                }
                else if ((r.StartsWith("mem", StringComparison.Ordinal) || r.StartsWith("pem", StringComparison.Ordinal)) && r.Length >= 7)
                {
                    string rest = r.Substring(3);
                    char c0 = rest[0];
                    if (IsVowel(c0)) { add("p" + rest); prefixed = true; }
                    else if (c0 == 'b' || c0 == 'f' || c0 == 'v' || c0 == 'p') { add(rest); prefixed = true; }
                }
                else if ((r.StartsWith("men", StringComparison.Ordinal) || r.StartsWith("pen", StringComparison.Ordinal)) && r.Length >= 7)
                {
                    string rest = r.Substring(3);
                    char c0 = rest[0];
                    if (IsVowel(c0)) { add("t" + rest); prefixed = true; }
                    else if (c0 == 'c' || c0 == 'd' || c0 == 'j' || c0 == 'z') { add(rest); prefixed = true; }
                }
                else if ((r.StartsWith("me", StringComparison.Ordinal) || r.StartsWith("pe", StringComparison.Ordinal)) && r.Length >= 7)
                {
                    string rest = r.Substring(2);
                    char c0 = rest[0];
                    if (c0 == 'l' || c0 == 'r' || c0 == 'w' || c0 == 'y' || c0 == 'm' || c0 == 'n') { add(rest); prefixed = true; }
                }
            }

            // Conservative English suffix stemming when no Indonesian affix matched (debugging->debug, commits->commit, queries->query)
            if (forms.Count == 0 && !prefixed)
            {
                if (lw.Length >= 7 && lw.EndsWith("ing", StringComparison.Ordinal))
                {
                    string stem = lw.Substring(0, lw.Length - 3);
                    if (stem.Length >= 5 && stem[stem.Length - 1] == stem[stem.Length - 2] && !IsVowel(stem[stem.Length - 1]))
                        stem = stem.Substring(0, stem.Length - 1);
                    add(stem);
                }
                else if (lw.Length >= 6 && lw.EndsWith("ies", StringComparison.Ordinal))
                {
                    add(lw.Substring(0, lw.Length - 3) + "y");
                }
                else if (lw.Length >= 6 && lw.EndsWith("ied", StringComparison.Ordinal))
                {
                    add(lw.Substring(0, lw.Length - 3) + "y");
                }
                else if (lw.Length >= 6 && lw.EndsWith("ed", StringComparison.Ordinal))
                {
                    string stem = lw.Substring(0, lw.Length - 2);
                    if (stem.Length >= 5 && stem[stem.Length - 1] == stem[stem.Length - 2] && !IsVowel(stem[stem.Length - 1]))
                        stem = stem.Substring(0, stem.Length - 1);
                    add(stem);
                }
                else if (lw.Length >= 5 && lw.EndsWith("s", StringComparison.Ordinal) &&
                         !lw.EndsWith("ss", StringComparison.Ordinal) && !lw.EndsWith("us", StringComparison.Ordinal) && !lw.EndsWith("is", StringComparison.Ordinal))
                {
                    add(lw.Substring(0, lw.Length - 1));
                }
            }
            return forms;
        }

        public List<IngatEpisode> Search(string q, DateTime fromUtc, DateTime toUtc, int limit)
        {
            return SearchCore(q, fromUtc, toUtc, limit, false);
        }

        public List<IngatEpisode> SearchLite(string q, DateTime fromUtc, DateTime toUtc, int limit)
        {
            return SearchCore(q, fromUtc, toUtc, limit, true);
        }

        List<IngatEpisode> SearchCore(string q, DateTime fromUtc, DateTime toUtc, int limit, bool lite)
        {
            string fts = FtsQuery(q);
            FlushIndex();
            // No topic words ("what was I doing?"): the latest moments, not the oldest
            if (fts.Length == 0)
                return ReadAll(db.Query("SELECT " + (lite ? LiteCols : Cols) + " FROM episodes WHERE end>=? AND start<=? ORDER BY start DESC LIMIT ?", fromUtc, toUtc, limit));
            var rows = db.Query("SELECT " + (lite ? LiteColsE : ColsE) + ", bm25(episodes_fts, 4.0, 2.0, 1.0, 3.0, 2.0) AS s " +
                "FROM episodes_fts JOIN episodes e ON e.id=episodes_fts.rowid WHERE episodes_fts MATCH ? AND e.end>=? AND e.start<=? ORDER BY s LIMIT ?",
                fts, fromUtc, toUtc, limit);
            var list = new List<IngatEpisode>(rows.Count);
            foreach (var r in rows)
            {
                var ep = Read(r);
                ep.Score = r[13] is double ? -(double)r[13] : 0;
                list.Add(ep);
            }
            return list;
        }

        // Work for the background worker

        public List<IngatEpisode> NeedSummary(int limit)
        {
            return ReadAll(db.Query("SELECT " + Cols + " FROM episodes WHERE closed=1 AND summary='' AND end-start>=25 ORDER BY start DESC LIMIT ?", limit));
        }

        // Pending episodes shorter than maxSeconds: summarized without a model, so no body is needed
        public List<IngatEpisode> NeedShortSummary(int maxSeconds, int limit)
        {
            return ReadAll(db.Query("SELECT " + LiteCols + " FROM episodes WHERE closed=1 AND summary='' AND end-start>=25 AND end-start<? ORDER BY start DESC LIMIT ?", maxSeconds, limit));
        }

        public List<IngatEpisode> NeedEmbedding(int limit)
        {
            return ReadAll(db.Query("SELECT " + Cols + " FROM episodes WHERE closed=1 AND embedded=0 AND summary<>'' ORDER BY start DESC LIMIT ?", limit));
        }

        // Vectors live in IngatVectors.cs

        // Days and facts

        public string Daily(string date) { return IngatDb.Str(db.Scalar("SELECT summary FROM daily WHERE date=?", date)); }
        public bool HasDaily(string date) { return db.Long("SELECT COUNT(*) FROM daily WHERE date=?", date) > 0; }
        public void SaveDaily(string date, string summary) { db.Exec("INSERT OR REPLACE INTO daily(date,summary,made) VALUES(?,?,?)", date, summary, DateTime.UtcNow); }

        // Facts live in IngatFacts.cs

        // Conversations and people

        public void SetChannel(long episode, string channel) { db.Exec("UPDATE episodes SET channel=? WHERE id=?", channel, episode); }

        // Chat and mail episodes that ended and were not read into messages yet
        public List<IngatEpisode> NeedDigest(int limit)
        {
            return ReadAll(db.Query("SELECT " + Cols + " FROM episodes WHERE closed=1 AND digested=0 AND channel<>'' AND end-start>=15 ORDER BY start LIMIT ?", limit));
        }

        public void SetDigested(long episode, int state) { db.Exec("UPDATE episodes SET digested=? WHERE id=?", state, episode); }

        public string ChannelOf(long episode) { return IngatDb.Str(db.Scalar("SELECT channel FROM episodes WHERE id=?", episode)); }

        // Snapshots of what the chat showed, drafts, and sent lines, oldest first
        public List<string[]> EventsOf(long episode, int max)
        {
            var list = new List<string[]>();
            foreach (var r in db.Query("SELECT t, source, title, text FROM events WHERE episode=? AND source IN ('chat','draft','sent','uia','ocr','clipboard') ORDER BY t LIMIT ?", episode, max))
                list.Add(new[] { IngatDb.FromUnix(IngatDb.Num(r[0])).ToLocalTime().ToString("HH:mm", CultureInfo.InvariantCulture), IngatDb.Str(r[1]), IngatDb.Str(r[2]), IngatDb.Str(r[3]) });
            return list;
        }

        // Latest screen snapshot text for an episode in O(1) via events_episode_t
        public string LastScreenEvent(long episode)
        {
            return IngatDb.Str(db.Scalar("SELECT text FROM events WHERE episode=? AND source IN ('chat','uia','ocr') ORDER BY t DESC, id DESC LIMIT 1", episode));
        }

        // One row per person; a new spelling of a known name becomes an alias
        public long Person(string name, DateTime seenUtc)
        {
            string norm = IngatChat.Norm(name);
            if (norm.Length < 2) return 0;
            var row = db.Query("SELECT id FROM people WHERE norm=?", norm);
            if (row.Count == 0) row = db.Query("SELECT id FROM people WHERE ('|' || aliases || '|') LIKE ? LIMIT 1", "%|" + norm + "|%");
            if (row.Count > 0)
            {
                long id = IngatDb.Num(row[0][0]);
                db.Exec("UPDATE people SET last=MAX(COALESCE(last,0),?), mentions=mentions+1 WHERE id=?", seenUtc, id);
                DropPeopleCache();
                return id;
            }
            long created = db.Insert("INSERT INTO people(name,norm,last,mentions) VALUES(?,?,?,1)", name.Trim(), norm, seenUtc);
            DropPeopleCache();
            return created;
        }

        // Lookup only: 0 when nobody by that name or alias is known
        public long FindPerson(string name)
        {
            string norm = IngatChat.Norm(name);
            if (norm.Length < 2) return 0;
            // norm is UNIQUE, so the exact match uses its index; only a miss scans the aliases
            long id = db.Long("SELECT id FROM people WHERE norm=?", norm);
            return id != 0 ? id : db.Long("SELECT id FROM people WHERE ('|' || aliases || '|') LIKE ? LIMIT 1", "%|" + norm + "|%");
        }

        public void AddAlias(long person, string alias)
        {
            string norm = IngatChat.Norm(alias);
            if (person == 0 || norm.Length < 2) return;
            var cur = IngatDb.Str(db.Scalar("SELECT aliases FROM people WHERE id=?", person));
            if (("|" + cur + "|").Contains("|" + norm + "|") || IngatDb.Str(db.Scalar("SELECT norm FROM people WHERE id=?", person)) == norm) return;
            db.Exec("UPDATE people SET aliases=? WHERE id=?", cur.Length == 0 ? norm : cur + "|" + norm, person);
            DropPeopleCache();
        }

        public void SetAbout(long person, string about) { if (person != 0 && !string.IsNullOrEmpty(about)) db.Exec("UPDATE people SET about=? WHERE id=?", about, person); }

        public long Conversation(string app, string name, string kind, long person, DateTime seenUtc)
        {
            string norm = IngatChat.Norm(name);
            if (norm.Length < 1) return 0;
            var row = db.Query("SELECT id FROM conversations WHERE app=? AND norm=?", app, norm);
            if (row.Count > 0)
            {
                long id = IngatDb.Num(row[0][0]);
                db.Exec("UPDATE conversations SET last=MAX(COALESCE(last,0),?), person=CASE WHEN ?<>0 THEN ? ELSE person END WHERE id=?", seenUtc, person, person, id);
                return id;
            }
            return db.Insert("INSERT INTO conversations(app,name,norm,kind,person,last) VALUES(?,?,?,?,?,?)", app, name.Trim(), norm, kind, person, seenUtc);
        }

        public void SetDigest(long conversation, string digest) { db.Exec("UPDATE conversations SET digest=?, digest_t=? WHERE id=?", digest, DateTime.UtcNow, conversation); }

        public bool AddMessage(long conversation, DateTime t, string sender, bool mine, string text, long episode)
        {
            return AddMessage(conversation, t, sender, mine ? "sent" : "received", text, episode, "");
        }

        // False when this line is already known in this conversation (same sender, same words; spacing and case aside).
        // A draft that later shows up as sent becomes sent.
        public bool AddMessage(long conversation, DateTime t, string sender, string status, string text, long episode, string said)
        {
            text = (text ?? "").Trim();
            if (conversation == 0 || text.Length == 0) return false;
            if (text.Length > 2000) text = text.Substring(0, 2000);
            bool mine = status == "sent" || status == "draft";
            string same = Regex.Replace(text.ToLowerInvariant(), @"\s+", " ").TrimEnd('.', '!', '?', ' ');
            string hash = Json.Sha256(conversation + "|" + (mine ? "me" : IngatChat.Norm(sender)) + "|" + same).Substring(0, 20);
            bool inserted = false;
            db.Tx(() =>
            {
                var known = db.Query("SELECT id, status FROM messages WHERE hash=?", hash);
                if (known.Count > 0)
                {
                    if (status == "sent" && IngatDb.Str(known[0][1]) == "draft") db.Exec("UPDATE messages SET status='sent', t=? WHERE id=?", t, IngatDb.Num(known[0][0]));
                    if (!string.IsNullOrEmpty(said)) db.Exec("UPDATE messages SET said=? WHERE id=? AND said=''", said, IngatDb.Num(known[0][0]));
                    return;
                }
                long id = db.Insert("INSERT INTO messages(conversation,t,sender,mine,text,hash,episode,status,said) VALUES(?,?,?,?,?,?,?,?,?)", conversation, t, mine ? "me" : sender, mine, text, hash, episode, status, said ?? "");
                db.Exec("INSERT INTO messages_fts(rowid,sender,text,conv) SELECT ?, ?, ?, name FROM conversations WHERE id=?", id, sender, text, conversation);
                inserted = true;
            });
            return inserted;
        }

        public void AddCommitment(long conversation, long person, string who, string what, string due, DateTime t)
        {
            if (string.IsNullOrEmpty(what)) return;
            string hash = Json.Sha256(conversation + "|" + IngatChat.Norm(who) + "|" + IngatChat.Norm(what)).Substring(0, 20);
            db.Exec("INSERT OR IGNORE INTO commitments(conversation,person,who,what,due,t,hash) VALUES(?,?,?,?,?,?,?)", conversation, person, who ?? "", what, due ?? "", t, hash);
        }

        // The model names a fulfilled promise in its own words: match by shared words
        public void FulfillPromise(long conversation, string what)
        {
            var want = new HashSet<string>();
            foreach (var w in Regex.Split((what ?? "").ToLowerInvariant(), @"[^\p{L}\p{N}]+")) if (w.Length >= 3) want.Add(w);
            if (want.Count == 0) return;
            foreach (var r in db.Query("SELECT id, what FROM commitments WHERE conversation=? AND done=0", conversation))
            {
                int hit = 0, total = 0;
                foreach (var w in Regex.Split(IngatDb.Str(r[1]).ToLowerInvariant(), @"[^\p{L}\p{N}]+"))
                {
                    if (w.Length < 3) continue;
                    total++;
                    // Affixes aside: "kirim" matches "dikirim", "kirimkan"
                    foreach (var x in want)
                        if (x == w || (w.Length >= 4 && x.Contains(w)) || (x.Length >= 4 && w.Contains(x))) { hit++; break; }
                }
                if (total > 0 && hit * 2 >= total) db.Exec("UPDATE commitments SET done=1 WHERE id=?", IngatDb.Num(r[0]));
            }
        }

        public void SetCommitmentDone(long id, bool done) { db.Exec("UPDATE commitments SET done=? WHERE id=?", done, id); }

        public void LinkPerson(long episode, long person) { if (person != 0) db.Exec("INSERT OR IGNORE INTO episode_people(episode,person) VALUES(?,?)", episode, person); }

        sealed class PersonIndexRow
        {
            public long Id;
            public string Norm, FirstNorm;
            public string[] Aliases;
        }

        readonly object peopleGate = new object();
        List<PersonIndexRow> peopleCache;
        static readonly Regex NameWordRx = new Regex(@"[^\p{L}\p{N}]+", RegexOptions.Compiled);

        void DropPeopleCache() { lock (peopleGate) peopleCache = null; }

        List<PersonIndexRow> PeopleIndex()
        {
            lock (peopleGate) if (peopleCache != null) return peopleCache;
            var rows = db.Query("SELECT id, norm, aliases, name FROM people ORDER BY mentions DESC LIMIT 2000");
            var list = new List<PersonIndexRow>(rows.Count);
            foreach (var r in rows)
            {
                string rawName = IngatDb.Str(r[3]).Trim();
                int sp = rawName.IndexOf(' ');
                string first = sp > 0 ? rawName.Substring(0, sp) : rawName;
                string fn = IngatTime.Stop(first) ? "" : IngatChat.Norm(first);
                if (fn.Length < 3) fn = "";
                string rawAliases = IngatDb.Str(r[2]);
                list.Add(new PersonIndexRow
                {
                    Id = IngatDb.Num(r[0]),
                    Norm = IngatDb.Str(r[1]),
                    FirstNorm = fn,
                    Aliases = rawAliases.Length == 0 ? new string[0] : rawAliases.Split(new[] { '|' }, StringSplitOptions.RemoveEmptyEntries),
                });
            }
            lock (peopleGate) peopleCache = list;
            return list;
        }

        // People named in a question: whole name, alias, or first name of a known person
        public List<long> PeopleIn(string text)
        {
            var found = new List<long>();
            if (string.IsNullOrEmpty(text)) return found;
            var index = PeopleIndex();
            if (index.Count == 0) return found;
            string all = IngatChat.Norm(text);
            var words = new HashSet<string>(StringComparer.Ordinal);
            foreach (var w in NameWordRx.Split(text)) if (w.Length >= 3) words.Add(IngatChat.Norm(w));
            for (int i = 0; i < index.Count; i++)
            {
                var p = index[i];
                bool hit = p.Norm.Length >= 4 && all.Contains(p.Norm);
                if (!hit)
                {
                    for (int a = 0; a < p.Aliases.Length; a++)
                        if (p.Aliases[a].Length >= 4 && all.Contains(p.Aliases[a])) { hit = true; break; }
                }
                if (!hit && p.FirstNorm.Length >= 3 && words.Contains(p.FirstNorm)) hit = true;
                if (hit) { found.Add(p.Id); if (found.Count == 3) break; }
            }
            return found;
        }

        public List<Dictionary<string, object>> People(int limit)
        {
            var list = new List<Dictionary<string, object>>();
            foreach (var r in db.Query("SELECT p.id, p.name, p.about, p.last, p.mentions, (SELECT GROUP_CONCAT(DISTINCT c.app) FROM conversations c WHERE c.person=p.id), " +
                "(SELECT COUNT(*) FROM commitments k WHERE k.person=p.id AND k.done=0) FROM people p ORDER BY p.last DESC LIMIT ?", limit))
                list.Add(new Dictionary<string, object>
                {
                    { "id", IngatDb.Num(r[0]) }, { "name", IngatDb.Str(r[1]) }, { "about", IngatDb.Str(r[2]) },
                    { "last", Local(r[3]) }, { "apps", IngatDb.Str(r[5]) }, { "openPromises", IngatDb.Num(r[6]) },
                });
            return list;
        }

        static string Local(object unix) { long s = IngatDb.Num(unix); return s == 0 ? "" : IngatDb.FromUnix(s).ToLocalTime().ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture); }

        // Everything about one person, across apps: who, where you talk, the latest lines, open promises
        public Dictionary<string, object> PersonView(long person, int messages)
        {
            var p = db.Query("SELECT name, about, aliases, last FROM people WHERE id=?", person);
            if (p.Count == 0) return null;
            var convs = new List<object>();
            foreach (var r in db.Query("SELECT id, app, name, kind, digest, last, topic, status, last_from, open, decisions FROM conversations WHERE person=? OR norm=? ORDER BY last DESC LIMIT 12", person, IngatChat.Norm(IngatDb.Str(p[0][0]))))
            {
                var c = new Dictionary<string, object> { { "id", IngatDb.Num(r[0]) }, { "app", IngatDb.Str(r[1]) }, { "name", IngatDb.Str(r[2]) }, { "kind", IngatDb.Str(r[3]) }, { "digest", IngatDb.Str(r[4]) }, { "last", Local(r[5]) } };
                foreach (var kv in State(r, 6)) c[kv.Key] = kv.Value;
                convs.Add(c);
            }
            var msgs = new List<object>();
            var rows = db.Query("SELECT " + MsgCols + " FROM messages m JOIN conversations c ON c.id=m.conversation WHERE c.person=? ORDER BY m.t DESC, m.id DESC LIMIT ?", person, messages);
            for (int i = rows.Count - 1; i >= 0; i--) msgs.Add(Message(rows[i]));
            return new Dictionary<string, object>
            {
                { "id", person }, { "name", IngatDb.Str(p[0][0]) }, { "about", IngatDb.Str(p[0][1]) }, { "last", Local(p[0][3]) },
                { "conversations", convs.ToArray() }, { "messages", msgs.ToArray() }, { "promises", Commitments(person, false, 15).ToArray() },
            };
        }

        public List<Dictionary<string, object>> Commitments(long person, bool all, int limit)
        {
            var list = new List<Dictionary<string, object>>();
            List<object[]> rows;
            if (person != 0)
            {
                rows = all
                    ? db.Query("SELECT k.id, k.who, k.what, k.due, k.t, k.done, c.app, c.name, p.name FROM commitments k LEFT JOIN conversations c ON c.id=k.conversation LEFT JOIN people p ON p.id=k.person WHERE k.person=? ORDER BY k.t DESC LIMIT ?", person, limit)
                    : db.Query("SELECT k.id, k.who, k.what, k.due, k.t, k.done, c.app, c.name, p.name FROM commitments k LEFT JOIN conversations c ON c.id=k.conversation LEFT JOIN people p ON p.id=k.person WHERE k.person=? AND k.done=0 ORDER BY k.t DESC LIMIT ?", person, limit);
            }
            else
            {
                rows = all
                    ? db.Query("SELECT k.id, k.who, k.what, k.due, k.t, k.done, c.app, c.name, p.name FROM commitments k LEFT JOIN conversations c ON c.id=k.conversation LEFT JOIN people p ON p.id=k.person ORDER BY k.t DESC LIMIT ?", limit)
                    : db.Query("SELECT k.id, k.who, k.what, k.due, k.t, k.done, c.app, c.name, p.name FROM commitments k LEFT JOIN conversations c ON c.id=k.conversation LEFT JOIN people p ON p.id=k.person WHERE k.done=0 ORDER BY k.t DESC LIMIT ?", limit);
            }
            foreach (var r in rows)
                list.Add(new Dictionary<string, object>
                {
                    { "id", IngatDb.Num(r[0]) }, { "who", IngatDb.Str(r[1]) }, { "what", IngatDb.Str(r[2]) }, { "due", IngatDb.Str(r[3]) }, { "when", Local(r[4]) },
                    { "done", IngatDb.Num(r[5]) != 0 }, { "app", IngatDb.Str(r[6]) }, { "conversation", IngatDb.Str(r[7]) }, { "person", IngatDb.Str(r[8]) },
                });
            return list;
        }

        // Words in messages, across every chat
        public List<Dictionary<string, object>> SearchMessages(string q, DateTime fromUtc, DateTime toUtc, int limit)
        {
            var list = new List<Dictionary<string, object>>();
            string fts = FtsQuery(q);
            if (fts.Length == 0) return list;
            foreach (var r in db.Query("SELECT " + MsgCols + " FROM messages_fts JOIN messages m ON m.id=messages_fts.rowid JOIN conversations c ON c.id=m.conversation " +
                "WHERE messages_fts MATCH ? AND m.t>=? AND m.t<=? ORDER BY bm25(messages_fts) LIMIT ?", fts, fromUtc, toUtc, limit))
                list.Add(Message(r));
            return list;
        }

        public long ConversationPerson(long conversation) { return db.Long("SELECT person FROM conversations WHERE id=?", conversation); }

        public long FindConversation(string app, string name)
        {
            string norm = IngatChat.Norm(name);
            if (norm.Length == 0) return 0;
            long id = db.Long("SELECT id FROM conversations WHERE app=? AND norm=?", app, norm);
            return id != 0 ? id : db.Long("SELECT id FROM conversations WHERE norm=? ORDER BY last DESC LIMIT 1", norm);
        }

        public string ConversationDigest(long conversation) { return IngatDb.Str(db.Scalar("SELECT digest FROM conversations WHERE id=?", conversation)); }

        public void SetState(long conversation, string digest, string topic, string status, string lastFrom, string[] open, string[] decisions)
        {
            db.Exec("UPDATE conversations SET digest=?, digest_t=?, topic=?, status=?, last_from=?, open=?, decisions=? WHERE id=?",
                digest ?? "", DateTime.UtcNow, topic ?? "", status ?? "", lastFrom ?? "", string.Join("\n", open ?? new string[0]), string.Join("\n", decisions ?? new string[0]), conversation);
        }

        static Dictionary<string, object> Message(object[] r)
        {
            // r: t, sender, mine, text, status, said, app, conversation name
            string status = IngatDb.Str(r[4]);
            if (status.Length == 0) status = IngatDb.Num(r[2]) != 0 ? "sent" : "received";
            var d = new Dictionary<string, object>
            {
                { "time", Local(r[0]) }, { "from", IngatDb.Num(r[2]) != 0 ? "me" : IngatDb.Str(r[1]) }, { "status", status }, { "text", IngatDb.Str(r[3]) },
            };
            if (IngatDb.Str(r[5]).Length > 0) d["shown"] = IngatDb.Str(r[5]);
            if (r.Length > 6) { d["app"] = IngatDb.Str(r[6]); d["conversation"] = IngatDb.Str(r[7]); }
            return d;
        }

        const string MsgCols = "m.t, m.sender, m.mine, m.text, m.status, m.said, c.app, c.name";

        // Latest lines of one conversation, oldest first
        public List<Dictionary<string, object>> Messages(long conversation, int limit)
        {
            var rows = db.Query("SELECT " + MsgCols + " FROM messages m JOIN conversations c ON c.id=m.conversation WHERE m.conversation=? ORDER BY m.t DESC, m.id DESC LIMIT ?", conversation, limit);
            var list = new List<Dictionary<string, object>>();
            for (int i = rows.Count - 1; i >= 0; i--) list.Add(Message(rows[i]));
            return list;
        }

        static Dictionary<string, object> State(object[] r, int at)
        {
            // r[at..]: topic, status, last_from, open, decisions
            var d = new Dictionary<string, object>();
            if (IngatDb.Str(r[at]).Length > 0) d["topic"] = IngatDb.Str(r[at]);
            if (IngatDb.Str(r[at + 1]).Length > 0) d["status"] = IngatDb.Str(r[at + 1]);
            if (IngatDb.Str(r[at + 2]).Length > 0) d["lastFrom"] = IngatDb.Str(r[at + 2]);
            var open = IngatDb.Str(r[at + 3]).Split(new[] { '\n' }, StringSplitOptions.RemoveEmptyEntries);
            if (open.Length > 0) d["openQuestions"] = open;
            var dec = IngatDb.Str(r[at + 4]).Split(new[] { '\n' }, StringSplitOptions.RemoveEmptyEntries);
            if (dec.Length > 0) d["decisions"] = dec;
            return d;
        }

        // One conversation in full: where it stands, its story so far, and the latest lines
        public Dictionary<string, object> ConversationView(long conversation, int messages)
        {
            var r = db.Query("SELECT c.id, c.app, c.name, c.kind, c.digest, c.last, p.name, c.topic, c.status, c.last_from, c.open, c.decisions FROM conversations c LEFT JOIN people p ON p.id=c.person WHERE c.id=?", conversation);
            if (r.Count == 0) return null;
            var d = new Dictionary<string, object>
            {
                { "id", IngatDb.Num(r[0][0]) }, { "app", IngatDb.Str(r[0][1]) }, { "name", IngatDb.Str(r[0][2]) }, { "kind", IngatDb.Str(r[0][3]) },
                { "summary", IngatDb.Str(r[0][4]) }, { "last", Local(r[0][5]) }, { "person", IngatDb.Str(r[0][6]) },
            };
            foreach (var kv in State(r[0], 7)) d[kv.Key] = kv.Value;
            d["messages"] = Messages(conversation, messages).ToArray();
            var proms = new List<object>();
            foreach (var k in db.Query("SELECT who, what, due FROM commitments WHERE conversation=? AND done=0 ORDER BY t DESC LIMIT 10", conversation))
                proms.Add(new Dictionary<string, object> { { "who", IngatDb.Str(k[0]) }, { "what", IngatDb.Str(k[1]) }, { "due", IngatDb.Str(k[2]) } });
            d["promises"] = proms.ToArray();
            return d;
        }

        // Conversations waiting for the user's reply, newest first
        public List<Dictionary<string, object>> Inbox(int limit)
        {
            var list = new List<Dictionary<string, object>>();
            foreach (var r in db.Query("SELECT c.id, c.app, c.name, c.last, c.topic, c.status, c.last_from, c.open, c.decisions FROM conversations c WHERE c.status='awaiting_me' ORDER BY c.last DESC LIMIT ?", limit))
            {
                var d = new Dictionary<string, object> { { "id", IngatDb.Num(r[0]) }, { "app", IngatDb.Str(r[1]) }, { "name", IngatDb.Str(r[2]) }, { "last", Local(r[3]) } };
                foreach (var kv in State(r, 4)) d[kv.Key] = kv.Value;
                list.Add(d);
            }
            return list;
        }

        public void SetConversationStatus(long conversation, string status) { db.Exec("UPDATE conversations SET status=? WHERE id=?", status, conversation); }

        // Meetings come from Rapat's transcript files

        public bool HasMeeting(string path) { return db.Long("SELECT COUNT(*) FROM meetings WHERE path=?", path) > 0; }

        public long AddMeeting(string path, DateTime startUtc, DateTime endUtc, string app, string title, string text)
        {
            if (text.Length > BodyCap * 4) text = text.Substring(0, BodyCap * 4);
            long id = 0;
            db.Tx(() =>
            {
                id = db.Insert("INSERT INTO episodes(start,end,app,exe,project,title,url,kind,body,closed) VALUES(?,?,?,?,?,?,?,?,?,1)", startUtc, endUtc, app, "", "", title, "", "meeting", text);
                db.Exec("INSERT OR IGNORE INTO meetings(path,start,title,episode) VALUES(?,?,?,?)", path, startUtc, title, id);
                SyncEntitiesTx(id, app, "", "", null);
                Reindex(id);
            });
            return id;
        }

        public string MeetingPath(long episode) { return IngatDb.Str(db.Scalar("SELECT path FROM meetings WHERE episode=?", episode)); }

        // Housekeeping

        public string Meta(string k) { return IngatDb.Str(db.Scalar("SELECT v FROM meta WHERE k=?", k)); }
        public void SetMeta(string k, string v) { db.Exec("INSERT OR REPLACE INTO meta(k,v) VALUES(?,?)", k, v); }

        // Lightweight Entity Graph: links episodes to projects, domains, files, error codes, commands, and apps

        void SyncEntitiesTx(long episode, string app, string project, string url, IngatContext ctx)
        {
            if (episode == 0) return;
            Action<string, string> put = (kind, raw) =>
            {
                string v = (raw ?? "").Trim().ToLowerInvariant();
                if (v.Length < 2 || v.Length > 100) return;
                db.Exec("INSERT OR IGNORE INTO episode_entities(episode,kind,value) VALUES(?,?,?)", episode, kind, v);
            };
            put("project", project);
            put("app", app);
            put("host", IngatPrivacy.Host(url));
            if (ctx != null)
                for (int i = 0; i < ctx.Anchors.Count; i++) put("anchor", ctx.Anchors[i]);
        }

        // Incremental worker backfill for episodes created before schema v4
        public bool BackfillEntities(int max)
        {
            long upto, through;
            long.TryParse(Meta("ent.upto"), out upto);
            long.TryParse(Meta("ent.through"), out through);
            if (through >= upto) return false;
            db.Tx(() =>
            {
                long last = through;
                foreach (var r in db.Query("SELECT id, app, project, url, context FROM episodes WHERE id>? AND id<=? ORDER BY id LIMIT ?", through, upto, max))
                {
                    last = IngatDb.Num(r[0]);
                    SyncEntitiesTx(last, IngatDb.Str(r[1]), IngatDb.Str(r[2]), IngatDb.Str(r[3]), IngatContext.Parse(IngatDb.Str(r[4])));
                }
                SetMeta("ent.through", (last == through ? upto : last).ToString(CultureInfo.InvariantCulture));
            });
            return true;
        }

        // O(log N) lookup of episodes linked to a concrete entity (project, file, error code, command, or domain)
        public List<IngatEpisode> EpisodesByEntityLite(string value, DateTime fromUtc, DateTime toUtc, int limit)
        {
            string norm = (value ?? "").Trim().ToLowerInvariant();
            if (norm.Length < 2) return new List<IngatEpisode>();
            return ReadAll(db.Query(
                "SELECT DISTINCT " + LiteColsE + " FROM episode_entities ee JOIN episodes e ON e.id=ee.episode " +
                "WHERE ee.value=? AND ee.kind IN ('project','anchor','host') AND e.end>=? AND e.start<=? ORDER BY e.end DESC LIMIT ?",
                norm, fromUtc, toUtc, limit));
        }

        // Co-occurring projects, files, errors, apps, and people linked with an entity across episodes
        public Dictionary<string, object> EntityGraph(string value, int limit)
        {
            string norm = (value ?? "").Trim().ToLowerInvariant();
            var d = new Dictionary<string, object> { { "entity", norm }, { "related", new object[0] }, { "people", new object[0] }, { "episodes", new object[0] } };
            if (norm.Length < 2) return d;
            var rel = new List<object>();
            foreach (var r in db.Query(
                "SELECT b.kind, b.value, COUNT(DISTINCT b.episode) AS c FROM episode_entities a " +
                "JOIN episode_entities b ON b.episode=a.episode AND (b.value<>a.value OR b.kind<>a.kind) " +
                "WHERE a.value=? GROUP BY b.kind, b.value ORDER BY c DESC LIMIT ?", norm, limit))
            {
                rel.Add(new Dictionary<string, object> { { "kind", IngatDb.Str(r[0]) }, { "value", IngatDb.Str(r[1]) }, { "episodes", IngatDb.Num(r[2]) } });
            }
            d["related"] = rel.ToArray();
            var ppl = new List<object>();
            foreach (var r in db.Query(
                "SELECT p.name, COUNT(DISTINCT ep.episode) AS c FROM episode_entities a " +
                "JOIN episode_people ep ON ep.episode=a.episode JOIN people p ON p.id=ep.person " +
                "WHERE a.value=? GROUP BY p.id ORDER BY c DESC LIMIT 10", norm))
            {
                ppl.Add(new Dictionary<string, object> { { "name", IngatDb.Str(r[0]) }, { "episodes", IngatDb.Num(r[1]) } });
            }
            d["people"] = ppl.ToArray();
            var eps = EpisodesByEntityLite(norm, DateTime.UtcNow.AddYears(-5), DateTime.UtcNow.AddMinutes(1), Math.Min(20, Math.Max(1, limit)));
            var ej = new object[eps.Count];
            for (int i = 0; i < eps.Count; i++) ej[i] = eps[i].ToJson(false);
            d["episodes"] = ej;
            return d;
        }

        // Raw events go first; episodes keep the gist much longer
        public void Retain(int eventDays, int episodeDays)
        {
            db.Tx(() => RetainTx(eventDays, episodeDays));
            lock (db.Gate) { eventHashEp = 0; eventHashes.Clear(); }
            DropPeopleCache();
            VecDrop();
            DecayFacts(DateTime.UtcNow.AddDays(-Math.Max(1, episodeDays)));
            // Keep FTS5 b-trees compact, query planner stats fresh, and WAL file bounded
            try { db.Exec("INSERT INTO episodes_fts(episodes_fts, rank) VALUES('merge', -8)"); } catch (InvalidOperationException) { }
            if (triOn) { try { db.Exec("INSERT INTO episodes_tri(episodes_tri, rank) VALUES('merge', -8)"); } catch (InvalidOperationException) { } }
            try { db.Exec("PRAGMA optimize"); } catch (InvalidOperationException) { }
            try { db.Exec("PRAGMA wal_checkpoint(PASSIVE)"); } catch (InvalidOperationException) { }
        }

        void RetainTx(int eventDays, int episodeDays)
        {
            db.Exec("DELETE FROM events WHERE t<?", DateTime.UtcNow.AddDays(-Math.Max(1, eventDays)));
            var cut = DateTime.UtcNow.AddDays(-Math.Max(1, episodeDays));
            db.Exec("DELETE FROM episodes_fts WHERE rowid IN (SELECT id FROM episodes WHERE end<?)", cut);
            TriDelete("end<?", cut);
            db.Exec("DELETE FROM chunks WHERE episode IN (SELECT id FROM episodes WHERE end<?)", cut);
            // A meeting keeps a blank tombstone row: the transcript file stays in rapat\, and the scan would bring it back otherwise
            db.Exec("UPDATE meetings SET episode=0, title='' WHERE episode IN (SELECT id FROM episodes WHERE end<?)", cut);
            db.Exec("DELETE FROM episode_people WHERE episode IN (SELECT id FROM episodes WHERE end<?)", cut);
            db.Exec("DELETE FROM episode_entities WHERE episode IN (SELECT id FROM episodes WHERE end<?)", cut);
            db.Exec("DELETE FROM episodes WHERE end<?", cut);
            // Messages follow episodes; people and promises stay while anything still points at them
            db.Exec("DELETE FROM messages_fts WHERE rowid IN (SELECT id FROM messages WHERE t<?)", cut);
            db.Exec("DELETE FROM messages WHERE t<?", cut);
            db.Exec("DELETE FROM commitments WHERE done=1 AND t<?", cut);
            db.Exec("DELETE FROM item_vectors WHERE kind='fact' AND item_id NOT IN (SELECT id FROM facts WHERE status='active')");
            db.Exec("DELETE FROM item_vectors WHERE kind='conv' AND item_id NOT IN (SELECT id FROM conversations)");
        }

        // Forget a stretch of time entirely
        public int Forget(DateTime fromUtc, DateTime toUtc)
        {
            int forgotten = 0;
            db.Tx(() => forgotten = ForgetTx(fromUtc, toUtc));
            lock (db.Gate) { eventHashEp = 0; eventHashes.Clear(); }
            DropPeopleCache();
            VecDrop();
            return forgotten;
        }

        int ForgetTx(DateTime fromUtc, DateTime toUtc)
        {
            const string ids = "SELECT id FROM episodes WHERE end>=? AND start<=?";
            db.Exec("DELETE FROM episodes_fts WHERE rowid IN (" + ids + ")", fromUtc, toUtc);
            TriDelete("end>=? AND start<=?", fromUtc, toUtc);
            db.Exec("DELETE FROM chunks WHERE episode IN (" + ids + ")", fromUtc, toUtc);
            db.Exec("UPDATE meetings SET episode=0, title='' WHERE episode IN (" + ids + ")", fromUtc, toUtc);
            db.Exec("DELETE FROM events WHERE t>=? AND t<=?", fromUtc, toUtc);
            db.Exec("DELETE FROM episode_people WHERE episode IN (" + ids + ")", fromUtc, toUtc);
            db.Exec("DELETE FROM episode_entities WHERE episode IN (" + ids + ")", fromUtc, toUtc);
            db.Exec("DELETE FROM messages_fts WHERE rowid IN (SELECT id FROM messages WHERE t>=? AND t<=?)", fromUtc, toUtc);
            db.Exec("DELETE FROM messages WHERE t>=? AND t<=?", fromUtc, toUtc);
            db.Exec("DELETE FROM commitments WHERE t>=? AND t<=?", fromUtc, toUtc);
            db.Exec("DELETE FROM item_vectors WHERE kind='conv' AND item_id IN (SELECT id FROM conversations WHERE last>=? AND last<=?)", fromUtc, toUtc);
            int n = db.Exec("DELETE FROM episodes WHERE end>=? AND start<=?", fromUtc, toUtc);
            var from = fromUtc.ToLocalTime().Date;
            for (var d = from; d <= toUtc.ToLocalTime().Date; d = d.AddDays(1))
            {
                // The recap is rebuilt from what is left and folded into the facts again, so what other hours taught comes back
                string key = d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
                db.Exec("DELETE FROM daily WHERE date=?", key);
                db.Exec("DELETE FROM meta WHERE k=?", "facts.folded." + key);
            }
            ForgetFactsTx(fromUtc, toUtc);
            return n;
        }

        public void ForgetAll()
        {
            lock (db.Gate) { dirty.Clear(); eventHashEp = 0; eventHashes.Clear(); }
            DropPeopleCache();
            VecDrop();
            db.Tx(() =>
            {
                if (triOn) db.Exec("DELETE FROM episodes_tri");
                db.Script("DELETE FROM episodes_fts;DELETE FROM embeddings;DELETE FROM chunks;DELETE FROM item_vectors;UPDATE meetings SET episode=0, title='';DELETE FROM events;DELETE FROM episodes;DELETE FROM daily;DELETE FROM meta WHERE k LIKE 'facts.folded.%';DELETE FROM facts;DELETE FROM facts_fts;" +
                    "DELETE FROM messages_fts;DELETE FROM messages;DELETE FROM commitments;DELETE FROM conversations;DELETE FROM episode_people;DELETE FROM episode_entities;DELETE FROM people");
            });
            db.ClearStatements();
            db.Exec("VACUUM");
        }

        public Dictionary<string, object> Stats()
        {
            long bytes = 0;
            try { bytes = new FileInfo(DbPath).Length; if (File.Exists(DbPath + "-wal")) bytes += new FileInfo(DbPath + "-wal").Length; } catch (Exception) { }
            var ep = db.Query("SELECT COUNT(*), COALESCE(SUM(CASE WHEN summary<>'' THEN 1 ELSE 0 END),0), COALESCE(SUM(CASE WHEN embedded=1 THEN 1 ELSE 0 END),0), COALESCE(SUM(CASE WHEN kind='meeting' THEN 1 ELSE 0 END),0) FROM episodes");
            long epTotal = ep.Count > 0 ? IngatDb.Num(ep[0][0]) : 0;
            long epSum = ep.Count > 0 ? IngatDb.Num(ep[0][1]) : 0;
            long epEmb = ep.Count > 0 ? IngatDb.Num(ep[0][2]) : 0;
            long epMeet = ep.Count > 0 ? IngatDb.Num(ep[0][3]) : 0;
            return new Dictionary<string, object>
            {
                { "episodes", epTotal },
                { "events", db.Long("SELECT COUNT(*) FROM events") },
                { "summarized", epSum },
                { "embedded", epEmb },
                { "meetings", epMeet },
                { "days", db.Long("SELECT COUNT(*) FROM daily") },
                { "facts", FactCount() },
                { "people", db.Long("SELECT COUNT(*) FROM people") },
                { "conversations", db.Long("SELECT COUNT(*) FROM conversations") },
                { "messages", db.Long("SELECT COUNT(*) FROM messages") },
                { "promises", db.Long("SELECT COUNT(*) FROM commitments WHERE done=0") },
                { "bytes", bytes },
            };
        }
    }
}
