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
                "CREATE TABLE IF NOT EXISTS meta(k TEXT PRIMARY KEY, v TEXT);" +
                // People and conversations, the same person across apps
                "CREATE TABLE IF NOT EXISTS people(id INTEGER PRIMARY KEY, name TEXT, norm TEXT UNIQUE, aliases TEXT DEFAULT '', about TEXT DEFAULT '', last INTEGER, mentions INTEGER DEFAULT 0);" +
                "CREATE TABLE IF NOT EXISTS conversations(id INTEGER PRIMARY KEY, app TEXT, name TEXT, norm TEXT, kind TEXT, person INTEGER, last INTEGER, digest TEXT DEFAULT '', digest_t INTEGER DEFAULT 0, UNIQUE(app, norm));" +
                "CREATE TABLE IF NOT EXISTS messages(id INTEGER PRIMARY KEY, conversation INTEGER, t INTEGER, sender TEXT, mine INTEGER, text TEXT, hash TEXT UNIQUE, episode INTEGER);" +
                "CREATE INDEX IF NOT EXISTS messages_conv ON messages(conversation, t);" +
                "CREATE VIRTUAL TABLE IF NOT EXISTS messages_fts USING fts5(sender, text, conv, tokenize='unicode61 remove_diacritics 2');" +
                "CREATE TABLE IF NOT EXISTS commitments(id INTEGER PRIMARY KEY, conversation INTEGER, person INTEGER, who TEXT, what TEXT, due TEXT, t INTEGER, done INTEGER DEFAULT 0, hash TEXT UNIQUE);" +
                "CREATE TABLE IF NOT EXISTS episode_people(episode INTEGER, person INTEGER, PRIMARY KEY(episode, person))");
            // Columns added after the first release: older databases get them here
            foreach (var col in new[]
            {
                "episodes|channel TEXT DEFAULT ''", "episodes|digested INTEGER DEFAULT 0",
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
            // No topic words ("what was I doing?"): the latest moments, not the oldest
            if (fts.Length == 0) return ReadAll(db.Query("SELECT " + Cols + " FROM episodes WHERE end>=? AND start<=? ORDER BY start DESC LIMIT ?", fromUtc, toUtc, limit));
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

        public bool HasVectors(string model) { return db.Long("SELECT COUNT(*) FROM embeddings WHERE model=?", model) > 0; }

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

        // One row per person; a new spelling of a known name becomes an alias
        public long Person(string name, DateTime seenUtc)
        {
            string norm = IngatChat.Norm(name);
            if (norm.Length < 2) return 0;
            var row = db.Query("SELECT id FROM people WHERE norm=? OR ('|' || aliases || '|') LIKE ?", norm, "%|" + norm + "|%");
            if (row.Count > 0)
            {
                long id = IngatDb.Num(row[0][0]);
                db.Exec("UPDATE people SET last=MAX(COALESCE(last,0),?), mentions=mentions+1 WHERE id=?", seenUtc, id);
                return id;
            }
            db.Exec("INSERT INTO people(name,norm,last,mentions) VALUES(?,?,?,1)", name.Trim(), norm, seenUtc);
            return db.LastId;
        }

        // Lookup only: 0 when nobody by that name or alias is known
        public long FindPerson(string name)
        {
            string norm = IngatChat.Norm(name);
            if (norm.Length < 2) return 0;
            return db.Long("SELECT id FROM people WHERE norm=? OR ('|' || aliases || '|') LIKE ? LIMIT 1", norm, "%|" + norm + "|%");
        }

        public void AddAlias(long person, string alias)
        {
            string norm = IngatChat.Norm(alias);
            if (person == 0 || norm.Length < 2) return;
            var cur = IngatDb.Str(db.Scalar("SELECT aliases FROM people WHERE id=?", person));
            if (("|" + cur + "|").Contains("|" + norm + "|") || IngatDb.Str(db.Scalar("SELECT norm FROM people WHERE id=?", person)) == norm) return;
            db.Exec("UPDATE people SET aliases=? WHERE id=?", cur.Length == 0 ? norm : cur + "|" + norm, person);
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
            db.Exec("INSERT INTO conversations(app,name,norm,kind,person,last) VALUES(?,?,?,?,?,?)", app, name.Trim(), norm, kind, person, seenUtc);
            return db.LastId;
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
            var known = db.Query("SELECT id, status FROM messages WHERE hash=?", hash);
            if (known.Count > 0)
            {
                if (status == "sent" && IngatDb.Str(known[0][1]) == "draft") db.Exec("UPDATE messages SET status='sent', t=? WHERE id=?", t, IngatDb.Num(known[0][0]));
                if (!string.IsNullOrEmpty(said)) db.Exec("UPDATE messages SET said=? WHERE id=? AND said=''", said, IngatDb.Num(known[0][0]));
                return false;
            }
            db.Exec("INSERT INTO messages(conversation,t,sender,mine,text,hash,episode,status,said) VALUES(?,?,?,?,?,?,?,?,?)", conversation, t, mine ? "me" : sender, mine, text, hash, episode, status, said ?? "");
            long id = db.LastId;
            db.Exec("INSERT INTO messages_fts(rowid,sender,text,conv) SELECT ?, ?, ?, name FROM conversations WHERE id=?", id, sender, text, conversation);
            return true;
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

        // People named in a question: whole name, alias, or first name of a known person
        public List<long> PeopleIn(string text)
        {
            var found = new List<long>();
            string all = IngatChat.Norm(text);
            var words = new HashSet<string>();
            foreach (var w in Regex.Split(text ?? "", @"[^\p{L}\p{N}]+")) if (w.Length >= 3) words.Add(IngatChat.Norm(w));
            foreach (var r in db.Query("SELECT id, norm, aliases, name FROM people ORDER BY mentions DESC LIMIT 2000"))
            {
                string norm = IngatDb.Str(r[1]);
                bool hit = norm.Length >= 4 && all.Contains(norm);
                if (!hit) foreach (var a in IngatDb.Str(r[2]).Split('|')) if (a.Length >= 4 && all.Contains(a)) { hit = true; break; }
                if (!hit)
                {
                    var first = Regex.Split(IngatDb.Str(r[3]), @"\s+")[0];
                    string fn = IngatChat.Norm(first);
                    hit = fn.Length >= 3 && words.Contains(fn) && !IngatTime.Stop(first);
                }
                if (hit) { found.Add(IngatDb.Num(r[0])); if (found.Count == 3) break; }
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
            string where = (person != 0 ? "k.person=" + person.ToString(CultureInfo.InvariantCulture) : "1=1") + (all ? "" : " AND k.done=0");
            foreach (var r in db.Query("SELECT k.id, k.who, k.what, k.due, k.t, k.done, c.app, c.name, p.name FROM commitments k LEFT JOIN conversations c ON c.id=k.conversation LEFT JOIN people p ON p.id=k.person WHERE " + where + " ORDER BY k.t DESC LIMIT ?", limit))
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
            db.Exec("DELETE FROM episode_people WHERE episode IN (SELECT id FROM episodes WHERE end<?)", cut);
            db.Exec("DELETE FROM episodes WHERE end<?", cut);
            // Messages follow episodes; people and promises stay while anything still points at them
            db.Exec("DELETE FROM messages_fts WHERE rowid IN (SELECT id FROM messages WHERE t<?)", cut);
            db.Exec("DELETE FROM messages WHERE t<?", cut);
            db.Exec("DELETE FROM commitments WHERE done=1 AND t<?", cut);
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
                db.Exec("DELETE FROM episode_people WHERE episode IN (" + ids + ")", fromUtc, toUtc);
                db.Exec("DELETE FROM messages_fts WHERE rowid IN (SELECT id FROM messages WHERE t>=? AND t<=?)", fromUtc, toUtc);
                db.Exec("DELETE FROM messages WHERE t>=? AND t<=?", fromUtc, toUtc);
                db.Exec("DELETE FROM commitments WHERE t>=? AND t<=?", fromUtc, toUtc);
                int n = db.Exec("DELETE FROM episodes WHERE end>=? AND start<=?", fromUtc, toUtc);
                var from = fromUtc.ToLocalTime().Date;
                for (var d = from; d <= toUtc.ToLocalTime().Date; d = d.AddDays(1))
                    db.Exec("DELETE FROM daily WHERE date=?", d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
                return n;
            }
        }

        public void ForgetAll()
        {
            db.Script("DELETE FROM episodes_fts;DELETE FROM embeddings;DELETE FROM meetings;DELETE FROM events;DELETE FROM episodes;DELETE FROM daily;DELETE FROM facts;" +
                "DELETE FROM messages_fts;DELETE FROM messages;DELETE FROM commitments;DELETE FROM conversations;DELETE FROM episode_people;DELETE FROM people");
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
                { "people", db.Long("SELECT COUNT(*) FROM people") },
                { "conversations", db.Long("SELECT COUNT(*) FROM conversations") },
                { "messages", db.Long("SELECT COUNT(*) FROM messages") },
                { "promises", db.Long("SELECT COUNT(*) FROM commitments WHERE done=0") },
                { "bytes", bytes },
            };
        }
    }
}
