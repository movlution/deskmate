using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace Deskmate
{
    // One stretch of work in one app or project
    sealed class IngatEpisode
    {
        public long Id;
        public DateTime StartUtc, EndUtc;
        public string App = "", Exe = "", Project = "", Title = "", Url = "", Kind = "", Summary = "", Body = "";
        public bool Closed;
        public double Score;

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
            if (withBody && Body.Length > 0) d["text"] = Body.Length > 1500 ? Body.Substring(0, 1500) + "…" : Body;
            return d;
        }
    }

    // Activity memory on disk: events → episodes → days → facts
    sealed class IngatStore : IDisposable
    {
        public static string Dir { get { return Path.Combine(Settings.Dir, "ingat"); } }
        public static string DbPath { get { return Path.Combine(Dir, "ingat.db"); } }

        // Body text an episode keeps for search
        public const int BodyCap = 12000;

        readonly IngatDb db;

        public IngatStore(string path)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            db = new IngatDb(path);
            db.Script(
                "PRAGMA journal_mode=WAL;" +
                "PRAGMA synchronous=NORMAL;" +
                "CREATE TABLE IF NOT EXISTS episodes(id INTEGER PRIMARY KEY, start INTEGER, end INTEGER, app TEXT, exe TEXT, project TEXT, title TEXT, url TEXT, kind TEXT DEFAULT '', summary TEXT DEFAULT '', body TEXT DEFAULT '', closed INTEGER DEFAULT 0, embedded INTEGER DEFAULT 0);" +
                "CREATE INDEX IF NOT EXISTS episodes_start ON episodes(start);" +
                "CREATE TABLE IF NOT EXISTS events(id INTEGER PRIMARY KEY, t INTEGER, episode INTEGER, source TEXT, app TEXT, title TEXT, url TEXT, text TEXT, hash TEXT);" +
                "CREATE INDEX IF NOT EXISTS events_t ON events(t);" +
                "CREATE VIRTUAL TABLE IF NOT EXISTS episodes_fts USING fts5(title, url, body, summary, app, tokenize='unicode61 remove_diacritics 2');" +
                "CREATE TABLE IF NOT EXISTS embeddings(episode INTEGER PRIMARY KEY, model TEXT, vec BLOB);" +
                "CREATE TABLE IF NOT EXISTS daily(date TEXT PRIMARY KEY, summary TEXT, made INTEGER);" +
                "CREATE TABLE IF NOT EXISTS facts(id INTEGER PRIMARY KEY, text TEXT, updated INTEGER);" +
                "CREATE TABLE IF NOT EXISTS meetings(id INTEGER PRIMARY KEY, path TEXT UNIQUE, start INTEGER, title TEXT, episode INTEGER);" +
                "CREATE TABLE IF NOT EXISTS meta(k TEXT PRIMARY KEY, v TEXT)");
        }

        public void Dispose() { db.Dispose(); }

        // Episodes

        public long Open(DateTime t, string app, string exe, string project, string title, string url, string kind)
        {
            db.Exec("INSERT INTO episodes(start,end,app,exe,project,title,url,kind,body) VALUES(?,?,?,?,?,?,?,?,?)", t, t, app, exe, project, title, url, kind, title);
            long id = db.LastId;
            Reindex(id);
            return id;
        }

        // Later title and URL win; new text joins the body once
        public void Touch(long id, DateTime t, string title, string url, string text)
        {
            var rows = db.Query("SELECT body FROM episodes WHERE id=?", id);
            if (rows.Count == 0) return;
            string body = IngatDb.Str(rows[0][0]);
            foreach (var piece in new[] { title, url, text })
                if (!string.IsNullOrEmpty(piece) && body.Length < BodyCap && body.IndexOf(piece, StringComparison.Ordinal) < 0)
                    body = body.Length == 0 ? piece : body + "\n" + piece;
            if (body.Length > BodyCap) body = body.Substring(0, BodyCap);
            db.Exec("UPDATE episodes SET end=?, title=CASE WHEN ?<>'' THEN ? ELSE title END, url=CASE WHEN ?<>'' THEN ? ELSE url END, body=? WHERE id=?",
                t, title ?? "", title ?? "", url ?? "", url ?? "", body, id);
            // The open episode must be findable too, not only once it closes
            Reindex(id);
        }

        public void Close(long id, DateTime t)
        {
            db.Exec("UPDATE episodes SET end=MAX(end,?), closed=1 WHERE id=?", t, id);
            Reindex(id);
        }

        // Whatever a crash left open is closed at its last sighting
        public void CloseStale() { db.Exec("UPDATE episodes SET closed=1 WHERE closed=0"); }

        // Episodes under a minute say nothing on their own
        public void DropTiny(long id)
        {
            db.Exec("DELETE FROM episodes WHERE id=? AND end-start<20 AND kind<>'meeting'", id);
            if (db.Changes > 0) { db.Exec("DELETE FROM episodes_fts WHERE rowid=?", id); db.Exec("DELETE FROM events WHERE episode=?", id); }
        }

        public void Reindex(long id)
        {
            db.Exec("DELETE FROM episodes_fts WHERE rowid=?", id);
            db.Exec("INSERT INTO episodes_fts(rowid,title,url,body,summary,app) SELECT id,title,url,body,summary,app FROM episodes WHERE id=?", id);
        }

        public void SetSummary(long id, string summary, string kind)
        {
            db.Exec("UPDATE episodes SET summary=?, kind=CASE WHEN ?<>'' THEN ? ELSE kind END WHERE id=?", summary ?? "", kind ?? "", kind ?? "", id);
            Reindex(id);
        }

        public void AddEvent(DateTime t, long episode, string source, string app, string title, string url, string text)
        {
            string hash = Json.Sha256(source + "|" + app + "|" + title + "|" + url + "|" + text).Substring(0, 16);
            // Same thing seen again inside this episode: once is enough
            if (db.Long("SELECT COUNT(*) FROM events WHERE episode=? AND hash=?", episode, hash) > 0) return;
            db.Exec("INSERT INTO events(t,episode,source,app,title,url,text,hash) VALUES(?,?,?,?,?,?,?,?)", t, episode, source, app, title, url, text, hash);
        }

        const string Cols = "id,start,end,app,exe,project,title,url,kind,summary,body,closed";

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

        public List<IngatEpisode> Timeline(DateTime fromUtc, DateTime toUtc, int limit)
        {
            return ReadAll(db.Query("SELECT " + Cols + " FROM episodes WHERE end>=? AND start<=? ORDER BY start LIMIT ?", fromUtc, toUtc, limit));
        }

        public List<IngatEpisode> Recent(int limit)
        {
            return ReadAll(db.Query("SELECT " + Cols + " FROM episodes ORDER BY start DESC LIMIT ?", limit));
        }

        public List<IngatEpisode> Meetings(DateTime fromUtc, DateTime toUtc, int limit)
        {
            return ReadAll(db.Query("SELECT " + Cols + " FROM episodes WHERE kind='meeting' AND end>=? AND start<=? ORDER BY start DESC LIMIT ?", fromUtc, toUtc, limit));
        }

        // Words become quoted OR terms: no FTS syntax from user text reaches SQLite
        public static string FtsQuery(string q)
        {
            var terms = new List<string>();
            var sb = new StringBuilder();
            foreach (char c in (q ?? "") + " ")
            {
                if (char.IsLetterOrDigit(c) || c == '_' || c == '.' || c == '-') { sb.Append(c); continue; }
                string w = sb.ToString().Trim('.', '-');
                sb.Length = 0;
                if (w.Length < 2 || IngatTime.Stop(w)) continue;
                if (!terms.Contains(w.ToLowerInvariant())) terms.Add(w.ToLowerInvariant());
                if (terms.Count == 12) break;
            }
            if (terms.Count == 0) return "";
            for (int i = 0; i < terms.Count; i++) terms[i] = "\"" + terms[i].Replace("\"", "") + "\"";
            return string.Join(" OR ", terms);
        }

        public List<IngatEpisode> Search(string q, DateTime fromUtc, DateTime toUtc, int limit)
        {
            string fts = FtsQuery(q);
            if (fts.Length == 0) return Timeline(fromUtc, toUtc, limit);
            var rows = db.Query("SELECT e.id,e.start,e.end,e.app,e.exe,e.project,e.title,e.url,e.kind,e.summary,e.body,e.closed, bm25(episodes_fts, 4.0, 2.0, 1.0, 3.0, 2.0) AS s " +
                "FROM episodes_fts JOIN episodes e ON e.id=episodes_fts.rowid WHERE episodes_fts MATCH ? AND e.end>=? AND e.start<=? ORDER BY s LIMIT ?",
                fts, fromUtc, toUtc, limit);
            var list = new List<IngatEpisode>();
            foreach (var r in rows)
            {
                var ep = Read(r);
                ep.Score = r[12] is double ? -(double)r[12] : 0;
                list.Add(ep);
            }
            return list;
        }

        // Work for the background worker

        public List<IngatEpisode> NeedSummary(int limit)
        {
            return ReadAll(db.Query("SELECT " + Cols + " FROM episodes WHERE closed=1 AND summary='' AND end-start>=60 ORDER BY start DESC LIMIT ?", limit));
        }

        public List<IngatEpisode> NeedEmbedding(int limit)
        {
            return ReadAll(db.Query("SELECT " + Cols + " FROM episodes WHERE closed=1 AND embedded=0 AND summary<>'' ORDER BY start DESC LIMIT ?", limit));
        }

        public void SaveVector(long id, string model, float[] vec)
        {
            var bytes = new byte[vec.Length * 4];
            Buffer.BlockCopy(vec, 0, bytes, 0, bytes.Length);
            db.Exec("INSERT OR REPLACE INTO embeddings(episode,model,vec) VALUES(?,?,?)", id, model, bytes);
            db.Exec("UPDATE episodes SET embedded=1 WHERE id=?", id);
        }

        // Embedding failed for good (no server, too long): stop asking
        public void SkipVector(long id) { db.Exec("UPDATE episodes SET embedded=2 WHERE id=?", id); }

        public List<KeyValuePair<long, float[]>> Vectors(string model, DateTime fromUtc, DateTime toUtc)
        {
            var list = new List<KeyValuePair<long, float[]>>();
            foreach (var r in db.Query("SELECT v.episode, v.vec FROM embeddings v JOIN episodes e ON e.id=v.episode WHERE v.model=? AND e.end>=? AND e.start<=?", model, fromUtc, toUtc))
            {
                var b = r[1] as byte[];
                if (b == null || b.Length < 4) continue;
                var v = new float[b.Length / 4];
                Buffer.BlockCopy(b, 0, v, 0, b.Length);
                list.Add(new KeyValuePair<long, float[]>(IngatDb.Num(r[0]), v));
            }
            return list;
        }

        // Days and facts

        public string Daily(string date) { return IngatDb.Str(db.Scalar("SELECT summary FROM daily WHERE date=?", date)); }
        public bool HasDaily(string date) { return db.Long("SELECT COUNT(*) FROM daily WHERE date=?", date) > 0; }
        public void SaveDaily(string date, string summary) { db.Exec("INSERT OR REPLACE INTO daily(date,summary,made) VALUES(?,?,?)", date, summary, DateTime.UtcNow); }

        public List<string> Facts()
        {
            var list = new List<string>();
            foreach (var r in db.Query("SELECT text FROM facts ORDER BY updated DESC LIMIT 40")) list.Add(IngatDb.Str(r[0]));
            return list;
        }

        public void ReplaceFacts(List<string> facts)
        {
            lock (db.Gate)
            {
                db.Exec("DELETE FROM facts");
                foreach (var f in facts) db.Exec("INSERT INTO facts(text,updated) VALUES(?,?)", f, DateTime.UtcNow);
            }
        }

        // Meetings come from Rapat's transcript files

        public bool HasMeeting(string path) { return db.Long("SELECT COUNT(*) FROM meetings WHERE path=?", path) > 0; }

        public long AddMeeting(string path, DateTime startUtc, DateTime endUtc, string app, string title, string text)
        {
            if (text.Length > BodyCap * 4) text = text.Substring(0, BodyCap * 4);
            db.Exec("INSERT INTO episodes(start,end,app,exe,project,title,url,kind,body,closed) VALUES(?,?,?,?,?,?,?,?,?,1)", startUtc, endUtc, app, "", "", title, "", "meeting", text);
            long id = db.LastId;
            db.Exec("INSERT OR IGNORE INTO meetings(path,start,title,episode) VALUES(?,?,?,?)", path, startUtc, title, id);
            Reindex(id);
            return id;
        }

        public string MeetingPath(long episode) { return IngatDb.Str(db.Scalar("SELECT path FROM meetings WHERE episode=?", episode)); }

        // Housekeeping

        public string Meta(string k) { return IngatDb.Str(db.Scalar("SELECT v FROM meta WHERE k=?", k)); }
        public void SetMeta(string k, string v) { db.Exec("INSERT OR REPLACE INTO meta(k,v) VALUES(?,?)", k, v); }

        // Raw events go first; episodes keep the gist much longer
        public void Retain(int eventDays, int episodeDays)
        {
            db.Exec("DELETE FROM events WHERE t<?", DateTime.UtcNow.AddDays(-Math.Max(1, eventDays)));
            var cut = DateTime.UtcNow.AddDays(-Math.Max(1, episodeDays));
            db.Exec("DELETE FROM episodes_fts WHERE rowid IN (SELECT id FROM episodes WHERE end<?)", cut);
            db.Exec("DELETE FROM embeddings WHERE episode IN (SELECT id FROM episodes WHERE end<?)", cut);
            db.Exec("DELETE FROM meetings WHERE episode IN (SELECT id FROM episodes WHERE end<?)", cut);
            db.Exec("DELETE FROM episodes WHERE end<?", cut);
        }

        // Forget a stretch of time entirely
        public int Forget(DateTime fromUtc, DateTime toUtc)
        {
            lock (db.Gate)
            {
                const string ids = "SELECT id FROM episodes WHERE end>=? AND start<=?";
                db.Exec("DELETE FROM episodes_fts WHERE rowid IN (" + ids + ")", fromUtc, toUtc);
                db.Exec("DELETE FROM embeddings WHERE episode IN (" + ids + ")", fromUtc, toUtc);
                db.Exec("DELETE FROM meetings WHERE episode IN (" + ids + ")", fromUtc, toUtc);
                db.Exec("DELETE FROM events WHERE t>=? AND t<=?", fromUtc, toUtc);
                int n = db.Exec("DELETE FROM episodes WHERE end>=? AND start<=?", fromUtc, toUtc);
                var from = fromUtc.ToLocalTime().Date;
                for (var d = from; d <= toUtc.ToLocalTime().Date; d = d.AddDays(1))
                    db.Exec("DELETE FROM daily WHERE date=?", d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
                return n;
            }
        }

        public void ForgetAll()
        {
            db.Script("DELETE FROM episodes_fts;DELETE FROM embeddings;DELETE FROM meetings;DELETE FROM events;DELETE FROM episodes;DELETE FROM daily;DELETE FROM facts");
            db.Exec("VACUUM");
        }

        public Dictionary<string, object> Stats()
        {
            long bytes = 0;
            try { bytes = new FileInfo(DbPath).Length; if (File.Exists(DbPath + "-wal")) bytes += new FileInfo(DbPath + "-wal").Length; } catch (Exception) { }
            return new Dictionary<string, object>
            {
                { "episodes", db.Long("SELECT COUNT(*) FROM episodes") },
                { "events", db.Long("SELECT COUNT(*) FROM events") },
                { "summarized", db.Long("SELECT COUNT(*) FROM episodes WHERE summary<>''") },
                { "embedded", db.Long("SELECT COUNT(*) FROM episodes WHERE embedded=1") },
                { "meetings", db.Long("SELECT COUNT(*) FROM episodes WHERE kind='meeting'") },
                { "days", db.Long("SELECT COUNT(*) FROM daily") },
                { "facts", db.Long("SELECT COUNT(*) FROM facts") },
                { "bytes", bytes },
            };
        }
    }
}
