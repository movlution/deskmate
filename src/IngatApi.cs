using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;

namespace Deskmate
{
    // Loopback HTTP for the island, --mcp, and scripts; token required, browsers refused
    sealed class IngatApi
    {
        readonly IngatService svc;
        readonly int port;
        readonly string token;
        TcpListener listener;
        readonly Semaphore slots = new Semaphore(8, 8);

        public IngatApi(IngatService svc, int port, string token)
        {
            this.svc = svc;
            this.port = port;
            this.token = token;
        }

        public bool Start()
        {
            try
            {
                listener = new TcpListener(IPAddress.Loopback, port) { ExclusiveAddressUse = true };
                listener.Start();
            }
            catch (Exception ex) { Log.Write("ingat api: " + ex.Message); return false; }
            new Thread(Prof.Wrap(Accept)) { IsBackground = true, Name = "ingat api" }.Start();
            return true;
        }

        public void Stop() { try { if (listener != null) listener.Stop(); } catch (Exception) { } }

        void Accept()
        {
            var own = listener;
            while (true)
            {
                TcpClient c;
                try { c = own.AcceptTcpClient(); }
                catch (Exception) { return; }
                if (!slots.WaitOne(0)) { c.Close(); continue; }
                new Thread(delegate () { try { Serve(c); } finally { slots.Release(); } }) { IsBackground = true, Name = "ingat req" }.Start();
            }
        }

        void Serve(TcpClient client)
        {
            using (client)
            {
                NetworkStream stream = null;
                try
                {
                    client.ReceiveTimeout = 5000;
                    stream = client.GetStream();
                    int started = Environment.TickCount;
                    string head;
                    byte[] early;
                    if (!AgentHub.ReadHead(stream, started, out head, out early)) { Reply(stream, 400, Err("bad request")); return; }
                    var lines = head.Split(new[] { "\r\n" }, StringSplitOptions.None);
                    var parts = lines[0].Split(' ');
                    var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                    for (int i = 1; i < lines.Length; i++)
                    {
                        int colon = lines[i].IndexOf(':');
                        if (colon > 0) headers[lines[i].Substring(0, colon).Trim()] = lines[i].Substring(colon + 1).Trim();
                    }
                    string got;
                    if (parts.Length < 2 || headers.ContainsKey("Origin") || !headers.TryGetValue("X-Deskmate-Token", out got) || !Same(got, token))
                    {
                        Reply(stream, 403, Err("forbidden"));
                        return;
                    }
                    string method = parts[0], target = parts[1], path = target, query = "";
                    int qm = target.IndexOf('?');
                    if (qm >= 0) { path = target.Substring(0, qm); query = target.Substring(qm + 1); }
                    var q = Query(query);
                    Dictionary<string, object> body = null;
                    if (method == "POST")
                    {
                        byte[] raw;
                        if (!AgentHub.ReadBody(stream, head, early, started, 256 * 1024, out raw)) { Reply(stream, 400, Err("bad body")); return; }
                        try { body = Json.Parse(Encoding.UTF8.GetString(raw)) as Dictionary<string, object>; }
                        catch (ArgumentException) { }
                        if (body == null) body = new Dictionary<string, object>();
                    }
                    object result = Route(method, path, q, body);
                    if (result == null) { Reply(stream, 404, Err("not found")); return; }
                    Reply(stream, 200, Json.Encode(result));
                }
                catch (Exception ex)
                {
                    Log.Write("ingat api serve: " + ex.Message);
                    try { if (stream != null) Reply(stream, 500, Err(ex.Message)); } catch (Exception) { }
                }
            }
        }

        static string Err(string m) { return Json.Encode(new Dictionary<string, object> { { "error", m } }); }

        object Route(string method, string path, Dictionary<string, string> q, Dictionary<string, object> body)
        {
            var store = svc.Store;
            switch (method + " " + path)
            {
                case "GET /status":
                    var st = store.Stats();
                    st["state"] = svc.State;
                    st["live"] = svc.LiveFocus();
                    st["paused"] = svc.Paused;
                    st["thinking"] = svc.Brain.Thinking;
                    st["embedModel"] = svc.Brain.EmbedModel;
                    var p = svc.Brain.Provider;
                    st["provider"] = p == null ? "" : p.Id;
                    st["local"] = p != null && p.NoKey;
                    try { using (var self = System.Diagnostics.Process.GetCurrentProcess()) st["privateMb"] = (int)(self.PrivateMemorySize64 / 1048576); } catch (Exception) { }
                    return st;
                case "GET /now":
                    if (Get(q, "fresh") != "0") svc.EnsureFresh(750);
                    return svc.Now();
                case "GET /context":
                    return svc.ContextNow(Clamp(Get(q, "recent"), 6, 1, 20));
                case "GET /search":
                {
                    svc.EnsureFresh(650);
                    DateTime from, to;
                    Span(q, out from, out to);
                    var hits = svc.Brain.Search(Get(q, "q"), from, to, Clamp(Get(q, "limit"), 10, 1, 40));
                    return new Dictionary<string, object>
                    {
                        { "results", Jsons(hits, Get(q, "text") == "1") },
                        { "structured", IngatStructure.GroupTimeline(hits) },
                    };
                }
                case "GET /timeline":
                {
                    DateTime day;
                    if (!DateTime.TryParseExact(Get(q, "date"), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out day)) day = DateTime.Today;
                    var eps = store.TimelineLite(day.ToUniversalTime(), day.AddDays(1).ToUniversalTime(), 600);
                    return new Dictionary<string, object>
                    {
                        { "date", day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) },
                        { "recap", store.Daily(day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)) },
                        { "structured", IngatStructure.GroupTimeline(eps) },
                        { "episodes", Jsons(eps, false) },
                    };
                }
                case "GET /episode":
                {
                    long id;
                    long.TryParse(Get(q, "id"), out id);
                    var ep = store.Get(id);
                    return ep == null ? null : (object)ep.ToJson(true);
                }
                case "GET /meetings":
                {
                    int days = Clamp(Get(q, "days"), 14, 1, 365);
                    return new Dictionary<string, object> { { "meetings", Jsons(store.Meetings(DateTime.UtcNow.AddDays(-days), DateTime.UtcNow, 100), false) } };
                }
                case "GET /meeting":
                {
                    long id;
                    long.TryParse(Get(q, "id"), out id);
                    var ep = store.Get(id);
                    if (ep == null || ep.Kind != "meeting") return null;
                    var d = ep.ToJson(false);
                    d["transcript"] = ep.Body;
                    return d;
                }
                case "GET /composing":
                    return svc.ComposingNow();
                case "GET /brief":
                    return svc.BriefNow();
                case "GET /people":
                    return new Dictionary<string, object> { { "people", store.People(Clamp(Get(q, "limit"), 50, 1, 500)).ToArray() } };
                case "GET /person":
                {
                    long id;
                    if (!long.TryParse(Get(q, "id"), out id) || id == 0)
                    {
                        id = store.FindPerson(Get(q, "name"));
                        if (id == 0) { var found = store.PeopleIn(Get(q, "name")); if (found.Count > 0) id = found[0]; }
                    }
                    var view = id == 0 ? null : store.PersonView(id, Clamp(Get(q, "messages"), 30, 1, 200));
                    return view ?? new Dictionary<string, object> { { "error", "unknown person" } };
                }
                case "GET /promises":
                {
                    long pid = Get(q, "person").Length > 0 ? store.FindPerson(Get(q, "person")) : 0;
                    if (Get(q, "person").Length > 0 && pid == 0) return new Dictionary<string, object> { { "promises", new object[0] } };
                    return new Dictionary<string, object> { { "promises", store.Commitments(pid, Get(q, "all") == "1", Clamp(Get(q, "limit"), 50, 1, 300)).ToArray() } };
                }
                case "GET /messages":
                {
                    DateTime from, to;
                    Span(q, out from, out to);
                    return new Dictionary<string, object> { { "messages", store.SearchMessages(Get(q, "q"), from, to, Clamp(Get(q, "limit"), 20, 1, 100)).ToArray() } };
                }
                case "GET /inbox":
                    return new Dictionary<string, object> { { "waiting", store.Inbox(Clamp(Get(q, "limit"), 20, 1, 100)).ToArray() } };
                case "GET /conversation":
                {
                    long id;
                    if (!long.TryParse(Get(q, "id"), out id) || id == 0) id = store.FindConversation(Get(q, "app"), Get(q, "name"));
                    if (id == 0)
                    {
                        // A person's name: their latest conversation
                        long pid = store.FindPerson(Get(q, "name"));
                        if (pid == 0) { var found = store.PeopleIn(Get(q, "name")); if (found.Count > 0) pid = found[0]; }
                        var pv = pid == 0 ? null : store.PersonView(pid, 1);
                        var convs = pv == null ? new object[0] : Json.Arr(pv, "conversations");
                        if (convs.Length > 0) id = (long)Json.Num(convs[0], "id", 0);
                    }
                    var view = id == 0 ? null : store.ConversationView(id, Clamp(Get(q, "messages"), 40, 1, 300));
                    return view ?? new Dictionary<string, object> { { "error", "unknown conversation" } };
                }
                case "POST /conversation/status":
                {
                    string want = Json.Str(body, "status");
                    if (want != "awaiting_me" && want != "awaiting_them" && want != "ongoing" && want != "done") return new Dictionary<string, object> { { "error", "status" } };
                    store.SetConversationStatus((long)Json.Num(body, "id", 0), want);
                    return new Dictionary<string, object> { { "ok", true } };
                }
                case "POST /promise":
                    store.SetCommitmentDone((long)Json.Num(body, "id", 0), Json.Str(body, "done").ToLowerInvariant() != "false");
                    return new Dictionary<string, object> { { "ok", true } };
                case "GET /facts":
                    // "facts" stays a list of strings for older clients; "detail" adds strength, sources, and kind
                    return new Dictionary<string, object> { { "facts", store.Facts().ToArray() }, { "detail", store.FactDetails().ToArray() } };
                case "POST /ask":
                    svc.EnsureFresh(900);
                    return svc.Brain.Ask(Fmt.OneLine(Json.Str(body, "q"), 500), svc.Now());
                case "POST /chat":
                {
                    svc.EnsureFresh(900);
                    var history = new List<KeyValuePair<string, string>>();
                    foreach (var o in Json.Arr(body, "history"))
                    {
                        string role = Json.Str(o, "role"), text = Fmt.OneLine(Json.Str(o, "text"), 800);
                        if ((role == "user" || role == "assistant") && text.Length > 0) history.Add(new KeyValuePair<string, string>(role, text));
                    }
                    if (history.Count > 12) history.RemoveRange(0, history.Count - 12);
                    return svc.Brain.Chat(Fmt.OneLine(Json.Str(body, "q"), 600), history, Fmt.OneLine(Json.Str(body, "name"), 40), Json.Str(body, "kind"), svc.Now());
                }
                case "POST /pause":
                    svc.Pause((int)Json.Num(body, "minutes", 30));
                    return new Dictionary<string, object> { { "paused", svc.Paused } };
                case "POST /event":
                    svc.External(Json.Str(body, "source"), Json.Str(body, "text"));
                    return new Dictionary<string, object> { { "ok", true } };
                case "POST /forget":
                {
                    if (Json.Str(body, "all") == "True" || Json.Str(body, "all") == "true" || Json.Str(body, "all") == "1") { svc.ForgetAll(); return new Dictionary<string, object> { { "forgot", "all" } }; }
                    var from = Json.Time(body, "from");
                    var to = Json.Time(body, "to");
                    if (from == DateTime.MinValue || to == DateTime.MinValue || to < from) return new Dictionary<string, object> { { "error", "from/to" } };
                    return new Dictionary<string, object> { { "forgot", svc.Forget(from, to) } };
                }
            }
            return null;
        }

        static object[] Jsons(List<IngatEpisode> eps, bool text)
        {
            var a = new object[eps.Count];
            for (int i = 0; i < eps.Count; i++) a[i] = eps[i].ToJson(text);
            return a;
        }

        // from/to as yyyy-MM-dd or yyyy-MM-ddTHH:mm local; else words in q ("kemarin"); else all
        static void Span(Dictionary<string, string> q, out DateTime fromUtc, out DateTime toUtc)
        {
            DateTime f, t, fl, tl;
            fromUtc = DateTime.UtcNow.AddYears(-5);
            toUtc = DateTime.UtcNow.AddMinutes(1);
            string[] fmts = { "yyyy-MM-dd", "yyyy-MM-ddTHH:mm", "yyyy-MM-dd HH:mm" };
            bool hf = DateTime.TryParseExact(Get(q, "from"), fmts, CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out f);
            bool ht = DateTime.TryParseExact(Get(q, "to"), fmts, CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out t);
            if (hf) fromUtc = f.ToUniversalTime();
            if (ht) toUtc = (Get(q, "to").Length == 10 ? t.AddDays(1) : t).ToUniversalTime();
            if (!hf && !ht && IngatTime.Range(Get(q, "q"), DateTime.Now, out fl, out tl)) { fromUtc = fl.ToUniversalTime(); toUtc = tl.ToUniversalTime(); }
        }

        static string Get(Dictionary<string, string> q, string k) { string v; return q.TryGetValue(k, out v) ? v : ""; }

        static int Clamp(string s, int fallback, int min, int max)
        {
            int v;
            if (!int.TryParse(s, out v)) v = fallback;
            return Math.Max(min, Math.Min(max, v));
        }

        static Dictionary<string, string> Query(string s)
        {
            var d = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var pair in s.Split('&'))
            {
                if (pair.Length == 0) continue;
                int eq = pair.IndexOf('=');
                string k = eq < 0 ? pair : pair.Substring(0, eq), v = eq < 0 ? "" : pair.Substring(eq + 1);
                try { d[Uri.UnescapeDataString(k.Replace('+', ' '))] = Uri.UnescapeDataString(v.Replace('+', ' ')); }
                catch (UriFormatException) { }
            }
            return d;
        }

        static bool Same(string a, string b)
        {
            if (a == null || b == null || a.Length != b.Length) return false;
            int diff = 0;
            for (int i = 0; i < a.Length; i++) diff |= a[i] ^ b[i];
            return diff == 0;
        }

        static void Reply(NetworkStream s, int status, string body)
        {
            var payload = Encoding.UTF8.GetBytes(body);
            string reason = status == 200 ? "OK" : status == 403 ? "Forbidden" : status == 404 ? "Not Found" : status == 500 ? "Internal Server Error" : "Bad Request";
            var h = Encoding.ASCII.GetBytes("HTTP/1.1 " + status + " " + reason + "\r\nContent-Type: application/json; charset=utf-8\r\nContent-Length: " + payload.Length + "\r\nConnection: close\r\n\r\n");
            s.Write(h, 0, h.Length);
            s.Write(payload, 0, payload.Length);
            s.Flush();
        }
    }

    // Callers of the service: the island, --mcp, tests
    static class IngatClient
    {
        static string cachedPort, cachedToken;
        static DateTime readUtc;

        static bool Endpoint(out string baseUrl, out string token)
        {
            baseUrl = token = null;
            if ((DateTime.UtcNow - readUtc).TotalSeconds > 5 || cachedPort == null)
            {
                readUtc = DateTime.UtcNow;
                cachedPort = cachedToken = null;
                try
                {
                    if (!File.Exists(IngatService.ApiFile)) return false;
                    var d = Json.Parse(File.ReadAllText(IngatService.ApiFile, Encoding.UTF8)) as Dictionary<string, object>;
                    cachedPort = Json.Str(d, "port");
                    cachedToken = Json.Str(d, "token");
                }
                catch (Exception) { return false; }
            }
            if (string.IsNullOrEmpty(cachedPort) || string.IsNullOrEmpty(cachedToken)) return false;
            baseUrl = "http://127.0.0.1:" + cachedPort;
            token = cachedToken;
            return true;
        }

        // True when the service process holds its mutex
        public static bool Running()
        {
            Mutex m;
            if (!Mutex.TryOpenExisting(IngatService.MutexName, out m)) return false;
            m.Dispose();
            return true;
        }

        // Null when the service is off or did not answer; briefly waits if the service process just acquired its mutex
        public static Dictionary<string, object> Call(string method, string path, object body, int timeoutMs)
        {
            if (!Running()) return null;
            string payload = body == null ? null : Json.Encode(body);
            var bootWait = System.Diagnostics.Stopwatch.StartNew();
            int maxBootMs = Math.Min(Math.Max(120, timeoutMs / 2), 900);
            while (true)
            {
                string b, token;
                if (Endpoint(out b, out token))
                {
                    int left = Math.Max(200, timeoutMs - (int)bootWait.ElapsedMilliseconds);
                    var r = Web.Send(method, b + path, new Dictionary<string, string> { { "X-Deskmate-Token", token } }, payload, left);
                    if (r.Status != 0)
                    {
                        try { return Json.Parse(r.Body) as Dictionary<string, object>; }
                        catch (ArgumentException) { return null; }
                    }
                    readUtc = DateTime.MinValue;
                }
                else readUtc = DateTime.MinValue;

                if (bootWait.ElapsedMilliseconds >= maxBootMs || !Running()) return null;
                Thread.Sleep(80);
            }
        }

        public static Dictionary<string, object> Get(string path, int timeoutMs) { return Call("GET", path, null, timeoutMs); }

        public static string Q(string s) { return Uri.EscapeDataString(s ?? ""); }
    }
}
