using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows.Forms;

namespace Deskmate
{
    // Ingatan: storage, search, privacy, time words, UIA snapshots, OCR, and live service/MCP
    public static class IngatCheck
    {
        [DllImport("gdi32.dll", CharSet = CharSet.Unicode)] static extern bool TextOut(IntPtr hdc, int x, int y, string lpString, int c);
        [DllImport("gdi32.dll")] static extern uint SetTextColor(IntPtr hdc, int crColor);
        [DllImport("gdi32.dll")] static extern uint SetBkColor(IntPtr hdc, int crColor);
        [DllImport("gdi32.dll")] static extern bool PatBlt(IntPtr hdc, int x, int y, int w, int h, uint rop);

        static void Assert(bool ok, string msg)
        {
            if (!ok) throw new Exception("Assertion failed: " + msg);
        }

        public static string Run()
        {
            TestRedaction();
            TestPrivateWindows();
            TestProject();
            TestTimeWords();
            TestFtsQuery();
            TestStoreAndSearch();
            TestRetentionAndForget();
            TestForgottenMeetingsStayForgotten();
            TestInsertIdsUnderConcurrency();
            TestChromiumPrivateRootName();
            TestServiceForgetDropsLiveState();
            TestMcpWithoutService();
            TestChatDetection();
            TestPeopleAndMessages();
            TestDirectionAndState();
            TestStructuredClassificationAndAnchors();
            TestRollingBodyAndStructuredEpisode();
            TestLiveQueriesAndStructuredFallback();
            TestDualZoneStampAndInMemoryOcr();
            TestLiveUiaSnapshotAndChat();
            TestLiveServiceAndMcp();
            return "Ingatan checks passed";
        }

        static void TestDirectionAndState()
        {
            // Times read against the day the chat was seen
            var seen = new IngatEpisode { StartUtc = new DateTime(2026, 11, 24, 9, 0, 0, DateTimeKind.Local).ToUniversalTime(), EndUtc = new DateTime(2026, 11, 24, 10, 0, 0, DateTimeKind.Local).ToUniversalTime() };
            Assert(IngatBrain.When("09.13", "", seen).ToLocalTime() == new DateTime(2026, 11, 24, 9, 13, 0), "dot time today");
            Assert(IngatBrain.When("9:13 PM", "", seen).ToLocalTime() == new DateTime(2026, 11, 23, 21, 13, 0), "later than seen: yesterday");
            Assert(IngatBrain.When("08:00", "kemarin", seen).ToLocalTime() == new DateTime(2026, 11, 23, 8, 0, 0), "yesterday word");
            Assert(IngatBrain.When("", "", seen) == DateTime.MinValue && IngatBrain.When("Read", "", seen) == DateTime.MinValue, "no time");
            Assert(IngatBrain.Me("Aku") && IngatBrain.Me("me") && !IngatBrain.Me("Budi"), "who is me");

            using (var store = new IngatStore(TempDb()))
            {
                var t = DateTime.UtcNow.AddMinutes(-30);
                long rina = store.Person("Rina", t);
                long conv = store.Conversation("WhatsApp", "Rina", "person", rina, t);
                Assert(store.AddMessage(conv, t, "Rina", "received", "Invoice bulan ini sudah ada?", 0, "09.10"), "received");
                Assert(store.AddMessage(conv, t.AddMinutes(1), "me", "draft", "Sudah, aku kirim sore ini", 0, ""), "draft");
                Assert(!store.AddMessage(conv, t.AddMinutes(2), "me", "sent", "sudah,  aku kirim sore ini.", 0, "09.12"), "the draft, sent: same line");
                var msgs = store.Messages(conv, 10);
                Assert(msgs.Count == 2 && Json.Str(msgs[0], "status") == "received" && Json.Str(msgs[1], "status") == "sent", "draft became sent");
                Assert(Json.Str(msgs[0], "from") == "Rina" && Json.Str(msgs[1], "from") == "me" && Json.Str(msgs[0], "shown") == "09.10", "senders and shown time");

                store.SetState(conv, "Rina menanyakan invoice; aku janji kirim sore ini.", "invoice", "awaiting_me", "Rina", new[] { "Kapan invoice dikirim?" }, new[] { "Invoice dikirim sore" });
                var view = store.ConversationView(conv, 10);
                Assert(Json.Str(view, "status") == "awaiting_me" && Json.Arr(view, "openQuestions").Length == 1 && Json.Arr(view, "decisions").Length == 1, "state kept");
                Assert(store.Inbox(5).Count == 1 && Json.Str(store.Inbox(5)[0], "name") == "Rina", "inbox lists chats waiting for me");
                store.SetConversationStatus(conv, "done");
                Assert(store.Inbox(5).Count == 0, "settled chats leave the inbox");

                store.AddCommitment(conv, rina, "me", "kirim invoice bulan ini ke Rina", "sore ini", t);
                store.FulfillPromise(conv, "invoice sudah dikirim ke Rina");
                Assert(store.Commitments(rina, false, 5).Count == 0, "promise fulfilled by meaning-alike words");
                var person = store.PersonView(rina, 10);
                Assert(Json.Str(Json.Arr(person, "conversations")[0], "status") == "done", "person view carries conversation state");
            }
        }

        static void TestChatDetection()
        {
            Assert(IngatChat.Kind("whatsapp.root", "", "WhatsApp") == "chat", "whatsapp desktop");
            Assert(IngatChat.Kind("chrome", "https://web.whatsapp.com/", "WhatsApp") == "chat", "whatsapp web");
            Assert(IngatChat.Kind("chrome", "https://mail.google.com/mail/u/0", "Inbox") == "mail", "gmail");
            Assert(IngatChat.Kind("chrome", "https://x.com/home", "Home / X") == "", "x feed is not chat");
            Assert(IngatChat.Kind("chrome", "https://x.com/messages/1", "Messages / X") == "chat", "x dms are chat");
            Assert(IngatChat.Kind("code", "", "Ai.cs - Deskmate") == "", "editor is not chat");
            Assert(IngatChat.Conversation("Budi Santoso - Discord") == "Budi Santoso", "discord dm: " + IngatChat.Conversation("Budi Santoso - Discord"));
            Assert(IngatChat.Conversation("#general | Tim Deskmate - Discord") == "general", "discord channel");
            Assert(IngatChat.Conversation("(3) Rina - Telegram") == "Rina", "unread count stripped");
            Assert(IngatChat.Conversation("WhatsApp") == "", "title without a name");
            Assert(IngatChat.Norm("Pak Budi 😀") == IngatChat.Norm("budi"), "title and emoji ignored: " + IngatChat.Norm("Pak Budi 😀"));
        }

        static void TestPeopleAndMessages()
        {
            string path = TempDb();
            using (var store = new IngatStore(path))
            {
                var t = DateTime.UtcNow.AddMinutes(-20);
                long budi = store.Person("Budi Santoso", t);
                Assert(budi != 0 && store.Person("budi santoso", t) == budi, "same person, any case");
                store.AddAlias(budi, "Bud");
                Assert(store.FindPerson("Bud") == budi && store.FindPerson("Nobody") == 0, "alias lookup");
                long wa = store.Conversation("WhatsApp", "Budi Santoso", "person", budi, t);
                long dc = store.Conversation("Discord", "budi.s", "person", budi, t.AddMinutes(5));
                Assert(wa != dc && store.Conversation("WhatsApp", "Budi Santoso", "person", budi, t) == wa, "one conversation per app and name");
                long ep = store.Open(t, "WhatsApp", "whatsapp.root", "", "WhatsApp", "", "");
                store.SetChannel(ep, "chat");
                Assert(store.AddMessage(wa, t, "Budi Santoso", false, "Laporan keuangan sudah dikirim belum?", ep), "message saved");
                Assert(!store.AddMessage(wa, t.AddSeconds(5), "Budi Santoso", false, "Laporan keuangan sudah dikirim belum?", ep), "same line once");
                store.AddMessage(dc, t.AddMinutes(5), "me", true, "Nanti malam aku kirim draft desainnya ya", ep);
                store.AddCommitment(dc, budi, "me", "kirim draft desain", "nanti malam", t.AddMinutes(5));
                store.AddCommitment(dc, budi, "me", "kirim draft desain", "nanti malam", t.AddMinutes(6));
                store.LinkPerson(ep, budi);
                store.Close(ep, t.AddMinutes(3));

                Assert(store.PeopleIn("apa yang Budi minta kemarin?").Contains(budi), "first name in a question");
                Assert(store.PeopleIn("apa yang aku kerjakan").Count == 0, "no person named");
                var view = store.PersonView(budi, 10);
                Assert(Json.Arr(view, "conversations").Length == 2, "conversations across apps");
                Assert(Json.Arr(view, "messages").Length == 2 && Json.Str(Json.Arr(view, "messages")[1], "from") == "me", "messages in order, mine marked");
                Assert(Json.Arr(view, "promises").Length == 1, "promise kept once");
                var found = store.SearchMessages("laporan keuangan", DateTime.UtcNow.AddDays(-1), DateTime.UtcNow, 5);
                Assert(found.Count == 1 && Json.Str(found[0], "app") == "WhatsApp", "message search across apps");
                Assert(store.NeedDigest(5).Count == 1, "chat episode waits for its digest");
                store.SetDigested(ep, 1);
                Assert(store.NeedDigest(5).Count == 0, "digested once");
                long pid = (long)store.Commitments(budi, false, 5)[0]["id"];
                store.SetCommitmentDone(pid, true);
                Assert(store.Commitments(budi, false, 5).Count == 0 && store.Commitments(budi, true, 5).Count == 1, "promise done");
                Assert((long)store.Stats()["people"] == 1 && (long)store.Stats()["messages"] == 2, "stats count people and messages");

                store.Forget(DateTime.UtcNow.AddHours(-1), DateTime.UtcNow);
                Assert((long)store.Stats()["messages"] == 0 && store.SearchMessages("laporan", DateTime.UtcNow.AddDays(-1), DateTime.UtcNow, 5).Count == 0, "forget removes messages and their index");
            }
            // Reopening an existing database adds columns only once
            using (var again = new IngatStore(path)) Assert((long)again.Stats()["people"] == 1, "reopen keeps data");
        }

        static void TestRedaction()
        {
            string s = IngatPrivacy.Redact("key sk-ant-api03-abcdefghijklmnopqrstuvwx mail budi@contoh.com hp 0812-3456-7890 card 4111 1111 1111 1111 password: hunter2 ghp_abcdefghijklmnopqrstuvwxyz0123");
            Assert(!s.Contains("sk-ant"), "api key hidden: " + s);
            Assert(!s.Contains("budi@contoh.com"), "email hidden");
            Assert(!s.Contains("3456"), "phone hidden: " + s);
            Assert(!s.Contains("4111"), "card hidden");
            Assert(!s.Contains("hunter2"), "password value hidden");
            Assert(!s.Contains("ghp_"), "github token hidden");
            Assert(IngatPrivacy.Redact("biasa saja") == "biasa saja", "plain text untouched");
            Assert(IngatPrivacy.CleanUrl("https://a.com/p?token=x#y") == "https://a.com/p", "query dropped");
        }

        static void TestPrivateWindows()
        {
            Assert(IngatPrivacy.BlockedApp("1Password", null), "password manager blocked");
            Assert(IngatPrivacy.BlockedApp("KeePassXC.exe", null), "exe suffix");
            Assert(!IngatPrivacy.BlockedApp("code", null), "editor allowed");
            Assert(IngatPrivacy.BlockedApp("spotify", new[] { "spotify" }), "user list");
            Assert(IngatPrivacy.PrivateWindow("New tab - Microsoft Edge [InPrivate]", "", null), "inprivate");
            Assert(IngatPrivacy.PrivateWindow("Inbox", "https://mail.google.com/x", new[] { "mail.google.com" }), "blocked site");
            Assert(!IngatPrivacy.PrivateWindow("Docs - WASAPI", "https://learn.microsoft.com/x", new[] { "mail.google.com" }), "other site fine");
        }

        static void TestProject()
        {
            Assert(IngatPrivacy.Project("code", "PetActions.cs - Deskmate - Visual Studio Code") == "Deskmate", "vscode project");
            Assert(IngatPrivacy.Project("code", "● Ai.cs - Deskmate - Visual Studio Code") == "Deskmate", "vscode dirty marker");
            Assert(IngatPrivacy.Project("unity", "ProjectAdam - Gameplay - Windows - Unity 6.6") == "ProjectAdam", "unity project");
            Assert(IngatPrivacy.Project("chrome", "Docs - Google Chrome") == "", "browser has none");
        }

        static void TestTimeWords()
        {
            var now = new DateTime(2026, 11, 24, 15, 0, 0, DateTimeKind.Local);
            DateTime f, t;
            Assert(IngatTime.Range("kemarin saya buka apa", now, out f, out t) && f == now.Date.AddDays(-1) && t == now.Date, "kemarin");
            Assert(IngatTime.Range("tadi pagi", now, out f, out t) && f == now.Date.AddHours(4) && t == now.Date.AddHours(11), "tadi pagi");
            Assert(IngatTime.Range("what did I do yesterday afternoon", now, out f, out t) && f == now.Date.AddDays(-1).AddHours(11), "yesterday afternoon");
            Assert(IngatTime.Range("3 jam terakhir", now, out f, out t) && f == now.AddHours(-3), "last hours");
            Assert(IngatTime.Range("minggu lalu", now, out f, out t) && (t - f).TotalDays == 7 && f.DayOfWeek == DayOfWeek.Monday, "last week");
            Assert(!IngatTime.Range("docs wasapi loopback", now, out f, out t), "no time words");

            // Right after midnight "tadi" still reaches the evening before, and "tadi malam" is last night
            var late = new DateTime(2026, 11, 25, 0, 30, 0, DateTimeKind.Local);
            Assert(IngatTime.Range("apa yang saya kerjakan tadi?", late, out f, out t) && f == late.AddHours(-6) && t == late, "tadi just after midnight");
            Assert(IngatTime.Range("barusan buka apa", late, out f, out t) && f == late.AddHours(-6), "barusan just after midnight");
            Assert(IngatTime.Range("tadi malam", late, out f, out t) && f == new DateTime(2026, 11, 24, 18, 0, 0) && t == late, "tadi malam after midnight is last night");
            Assert(IngatTime.Range("tadi", now, out f, out t) && f == now.Date && t == now, "tadi in the afternoon is today");
            Assert(IngatTime.Range("hari ini", late, out f, out t) && f == late.Date, "hari ini stays today");
            Assert(IngatTime.Range("tadi pagi", late, out f, out t) && f == new DateTime(2026, 11, 24, 4, 0, 0), "tadi pagi before dawn is yesterday morning");
        }

        static void TestFtsQuery()
        {
            Assert(IngatStore.FtsQuery("apa yang saya baca soal WASAPI kemarin?") == "\"wasapi\"", "stop words gone: " + IngatStore.FtsQuery("apa yang saya baca soal WASAPI kemarin?"));
            Assert(!IngatStore.FtsQuery("a\" OR 1=1 -- NEAR(x)").Contains("NEAR("), "no fts syntax passes");
            Assert(IngatStore.FtsQuery("apa yang") == "", "nothing to search");
        }

        static string TempDb()
        {
            string dir = Path.Combine(Path.GetTempPath(), "ingat-check-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            return Path.Combine(dir, "t.db");
        }

        static void TestStoreAndSearch()
        {
            string path = TempDb();
            using (var store = new IngatStore(path))
            {
                var t0 = DateTime.UtcNow.AddMinutes(-30);
                long a = store.Open(t0, "Visual Studio Code", "code", "Deskmate", "Recorder.cs - Deskmate", "", "");
                store.Touch(a, t0.AddMinutes(10), "Recorder.cs - Deskmate", "", "IAudioClient loopback capture WASAPI");
                store.AddEvent(t0.AddMinutes(10), a, "uia", "Visual Studio Code", "Recorder.cs", "", "IAudioClient loopback capture WASAPI");
                store.AddEvent(t0.AddMinutes(11), a, "uia", "Visual Studio Code", "Recorder.cs", "", "IAudioClient loopback capture WASAPI");
                store.Close(a, t0.AddMinutes(12));
                long b = store.Open(t0.AddMinutes(13), "Google Chrome", "chrome", "", "Ollama embeddings", "https://ollama.com/blog/embedding-models", "");
                store.Close(b, t0.AddMinutes(20));
                long tiny = store.Open(t0.AddMinutes(21), "Explorer", "explorer", "", "Downloads", "", "");
                store.Close(tiny, t0.AddMinutes(21).AddSeconds(5));
                store.DropTiny(tiny);

                Assert(store.Get(tiny) == null, "tiny episode dropped");
                var latest = store.Search("apa yang", DateTime.UtcNow.AddDays(-1), DateTime.UtcNow, 10);
                Assert(latest.Count == 2 && latest[0].Id == b, "no topic words: newest first");
                var hits = store.Search("wasapi", DateTime.UtcNow.AddDays(-1), DateTime.UtcNow, 10);
                Assert(hits.Count == 1 && hits[0].Id == a, "keyword finds episode");
                hits = store.Search("embedding", DateTime.UtcNow.AddDays(-1), DateTime.UtcNow, 10);
                Assert(hits.Count == 1 && hits[0].Id == b, "title search");
                Assert(store.Search("wasapi", DateTime.UtcNow.AddDays(-9), DateTime.UtcNow.AddDays(-8), 10).Count == 0, "range filters");
                Assert((long)store.Stats()["events"] == 1, "duplicate event kept once");
                Assert(store.Timeline(DateTime.UtcNow.AddHours(-1), DateTime.UtcNow, 50).Count == 2, "timeline");

                store.SetSummary(a, "Menulis perekam WASAPI loopback di Deskmate", "coding");
                Assert(store.Get(a).Kind == "coding" && store.NeedSummary(10).Count == 1, "summary saved, one left");
                store.SaveVector(a, "m", new float[] { 0.6f, 0.8f });
                var vecs = store.Vectors("m", DateTime.UtcNow.AddDays(-1), DateTime.UtcNow);
                Assert(vecs.Count == 1 && Math.Abs(vecs[0].Value[1] - 0.8f) < 1e-6, "vector round trip");

                long m = store.AddMeeting("C:\\x\\2026-11-24 10.00 Zoom.md", t0, t0.AddMinutes(5), "Zoom", "Rapat · Zoom", "Kita sepakat rilis Jumat");
                Assert(store.HasMeeting("C:\\x\\2026-11-24 10.00 Zoom.md") && store.Search("rilis jumat", DateTime.UtcNow.AddDays(-1), DateTime.UtcNow, 5)[0].Id == m, "meeting indexed");

                // Search over the brain with AI off: keyword only, no network
                var settings = new Settings();
                settings.Set("ingat.ai", "off");
                var brain = new IngatBrain(store, settings);
                Assert(!brain.Thinking && brain.EmbedModel == "" && !brain.EmbedUsable, "ai off");
                Assert(!store.HasVectors("none") && store.HasVectors("m"), "vectors known per model");
                Assert(brain.Search("wasapi", DateTime.UtcNow.AddDays(-1), DateTime.UtcNow, 5)[0].Id == a, "brain search");
                var ans = brain.Ask("apa yang saya kerjakan soal wasapi tadi?");
                Assert(Json.Arr(ans, "sources").Length >= 1, "ask has sources without a model");
                // Pet chat without a model: says so and flags it, so the island falls back to the main AI
                var chat = brain.Chat("kemarin aku kerja wasapi?", new List<KeyValuePair<string, string>>(), "Rubah", "custom:Rubah", new Dictionary<string, object>());
                Assert(chat.ContainsKey("error") && Json.Str(chat, "answer").Length > 0, "pet chat flags missing model");
            }
        }

        static void TestRetentionAndForget()
        {
            using (var store = new IngatStore(TempDb()))
            {
                var old = DateTime.UtcNow.AddDays(-40);
                long a = store.Open(old, "App", "app", "", "old", "", "");
                store.AddEvent(old, a, "uia", "App", "old", "", "lama sekali");
                store.Close(a, old.AddMinutes(5));
                long b = store.Open(DateTime.UtcNow.AddMinutes(-10), "App", "app", "", "new", "", "");
                store.Close(b, DateTime.UtcNow);
                store.Retain(14, 30);
                Assert(store.Get(a) == null && store.Get(b) != null, "old episode retained away");
                Assert((long)store.Stats()["events"] == 0, "old events gone");
                Assert(store.Forget(DateTime.UtcNow.AddHours(-1), DateTime.UtcNow.AddMinutes(1)) == 1 && store.Get(b) == null, "forget range");
                Assert(store.Search("new", DateTime.UtcNow.AddDays(-1), DateTime.UtcNow, 5).Count == 0, "fts row gone too");
            }
        }

        // The scan re-imports any rapat\*.md without a meetings row: forgetting must leave a blank tombstone
        static void TestForgottenMeetingsStayForgotten()
        {
            using (var store = new IngatStore(TempDb()))
            {
                var t = DateTime.UtcNow.AddMinutes(-30);
                string recent = "C:\\x\\2026-11-24 10.00 Zoom.md", old = "C:\\x\\2026-01-01 09.00 Meet.md", wiped = "C:\\x\\2026-11-20 08.00 Teams.md";
                long m = store.AddMeeting(recent, t, t.AddMinutes(5), "Zoom", "Rapat · Zoom", "Rahasia rapat Jumat");
                Assert(store.Forget(DateTime.UtcNow.AddHours(-1), DateTime.UtcNow) == 1 && store.Get(m) == null, "forgotten meeting episode is gone");
                Assert(store.HasMeeting(recent), "forgotten meeting keeps a tombstone so the scan does not bring it back");
                Assert(store.MeetingPath(m) == "" && (long)store.Stats()["meetings"] == 0, "tombstone points at no episode");

                var o = DateTime.UtcNow.AddDays(-400);
                store.AddMeeting(old, o, o.AddMinutes(30), "Meet", "Rapat · Meet", "Rapat lama");
                store.Retain(14, 180);
                Assert(store.HasMeeting(old) && (long)store.Stats()["meetings"] == 0, "meetings past retention stay retained away");

                store.AddMeeting(wiped, t, t.AddMinutes(5), "Teams", "Rapat · Teams", "Rapat dihapus");
                store.ForgetAll();
                Assert(store.HasMeeting(wiped) && store.HasMeeting(recent) && (long)store.Stats()["meetings"] == 0, "forget all keeps tombstones too");
            }
        }

        // last_insert_rowid is per connection: an id read after another thread's insert would point at its row
        static void TestInsertIdsUnderConcurrency()
        {
            using (var store = new IngatStore(TempDb()))
            {
                var t = DateTime.UtcNow.AddMinutes(-5);
                string failure = null;
                var other = new Thread(delegate ()
                {
                    try { for (int i = 0; i < 400; i++) store.Person("Orang " + i, t); }
                    catch (Exception ex) { failure = ex.Message; }
                });
                other.Start();
                for (int i = 0; i < 400 && failure == null; i++)
                {
                    string app = "App" + i;
                    long id = store.Open(t, app, "app" + i, "", "judul " + i, "", "");
                    var ep = store.Get(id);
                    if (ep == null || ep.App != app) { failure = "episode id " + id + " is not the row just opened (" + (ep == null ? "missing" : ep.App) + ")"; break; }
                }
                other.Join();
                Assert(failure == null, "insert ids stay with their own row: " + failure);
            }
        }

        static void TestChromiumPrivateRootName()
        {
            Assert(IngatText.PrivateRootName("Example Domain - Google Chrome (Incognito)", "Example Domain - Google Chrome"), "chrome incognito");
            Assert(IngatText.PrivateRootName("Example Domain - Google Chrome (Guest)", "Example Domain - Google Chrome"), "chrome guest");
            Assert(!IngatText.PrivateRootName("Example Domain - Google Chrome", "Example Domain - Google Chrome"), "normal window");
            Assert(!IngatText.PrivateRootName("Example - Network error - Google Chrome - Person 1", "Example - Google Chrome"), "another profile is not private");
            Assert(!IngatText.PrivateRootName("Inbox (3)", "Inbox (3)"), "a title ending in parentheses is not a marker");
            Assert(!IngatText.PrivateRootName("", "Visual Studio Code"), "electron app without a browser root view");
        }

        // Forgetting the stretch that holds the live episode must drop it and the screen kept in memory
        static void TestServiceForgetDropsLiveState()
        {
            var type = typeof(IngatService);
            var flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
            var svc = (IngatService)type.GetConstructor(flags, null, new[] { typeof(int) }, null).Invoke(new object[] { 0 });
            try
            {
                var now = DateTime.UtcNow;
                long id = svc.Store.Open(now.AddMinutes(-3), "Notepad", "notepad", "", "rahasia.txt", "", "");
                svc.Store.Touch(id, now, "rahasia.txt", "", "isi yang mau dilupakan");
                var snap = new IngatSnapshot { App = "Notepad", Title = "rahasia.txt" };
                snap.MainLines.Add("isi yang mau dilupakan");
                type.GetField("current", flags).SetValue(svc, svc.Store.Get(id));
                type.GetField("latestSnap", flags).SetValue(svc, snap);
                type.GetField("lastDraft", flags).SetValue(svc, "draf yang mau dilupakan");
                Assert(svc.Now().ContainsKey("episode") && svc.Now().ContainsKey("screen"), "live episode and screen before forgetting");

                svc.Forget(now.AddMinutes(-15), now.AddMinutes(1));
                var after = svc.Now();
                Assert(!after.ContainsKey("episode") && !after.ContainsKey("screen"), "forgotten live episode and screen are not served any more");
                Assert((string)type.GetField("lastDraft", flags).GetValue(svc) == "", "forgotten draft is dropped");

                long keep = svc.Store.Open(now.AddMinutes(-1), "Notepad", "notepad", "", "baru.txt", "", "");
                type.GetField("current", flags).SetValue(svc, svc.Store.Get(keep));
                svc.Forget(now.AddDays(-3), now.AddDays(-2));
                Assert(svc.Now().ContainsKey("episode"), "forgetting an older stretch leaves the live episode alone");
                svc.ForgetAll();
                Assert(!svc.Now().ContainsKey("episode"), "forget all drops the live episode");
            }
            finally { svc.Store.Dispose(); }
        }

        static void TestMcpWithoutService()
        {
            Assert(!IngatClient.Running() || true, "client probe does not throw");
            Assert(IngatMcp.ConfigJson().Contains("--mcp"), "config names the flag");
        }

        static void TestStructuredClassificationAndAnchors()
        {
            Assert(IngatStructure.Classify("code", "", "IngatService.cs - Deskmate - Visual Studio Code", "") == "coding", "vscode is coding");
            Assert(IngatStructure.Classify("pwsh", "", "Administrator: PowerShell", "") == "coding", "terminal is coding");
            Assert(IngatStructure.Classify("chrome", "https://github.com/reval/deskmate/pull/12", "PR #12", "") == "coding", "github is coding");
            Assert(IngatStructure.Classify("chrome", "https://learn.microsoft.com/en-us/windows/win32", "Win32 Docs", "") == "reading", "docs site is reading");
            Assert(IngatStructure.Classify("chrome", "https://docs.google.com/document/d/abc", "Proposal", "") == "writing", "gdocs is writing");
            Assert(IngatStructure.Classify("figma", "", "Deskmate UI", "") == "design", "figma is design");
            Assert(IngatStructure.Classify("spotify", "", "Spotify", "") == "media", "spotify is media");
            Assert(IngatStructure.Classify("whatsapp.root", "", "WhatsApp", "") == "chat", "whatsapp is chat");
            // 'meeting' is reserved for Rapat transcripts so Stats()['meetings'] stays accurate
            Assert(IngatStructure.Classify("chrome", "https://meet.google.com/abc-defg-hij", "Meet", "") != "meeting", "browser meet does not spoof rapat transcript");
            Assert(IngatStructure.Classify("", "", "", "") == "other", "empty input safe");

            Assert(IngatStructure.CleanLine("Minimize") == "" && IngatStructure.CleanLine("Tutup") == "", "window chrome stripped");
            Assert(IngatStructure.CleanLine("\uE81C  Ingatan   aktif \r\t ") == "Ingatan aktif", "icon glyph and spacing cleaned");
            Assert(IngatStructure.CleanLine("   ---   ") == "" && IngatStructure.CleanLine(null) == "", "non-alnum and null dropped");

            var anchors = IngatStructure.ExtractAnchors(
                "IngatStore.cs - Deskmate",
                "https://learn.microsoft.com/windows/win32/uiauto?secret=123",
                "Error CS0103 pada src/IngatText.cs saat jalankan git status dan NullReferenceException dengan key sk-ant-api03-abcdefghijklmnopqrstuvwx",
                12);
            Assert(anchors.Contains("learn.microsoft.com/windows/win32"), "url slug anchor");
            Assert(anchors.Contains("IngatStore.cs") && anchors.Contains("src/IngatText.cs"), "file anchors");
            Assert(anchors.Contains("CS0103") && anchors.Contains("NullReferenceException"), "diagnostic anchors");
            foreach (var a in anchors) Assert(!a.Contains("sk-ant") && !a.Contains("secret=123"), "anchors redacted: " + a);

            // Snapshot formatting, redaction, and secret clearing
            var snap = new IngatSnapshot
            {
                Source = "uia", App = "Visual Studio Code", Exe = "code", Project = "Deskmate",
                Title = "IngatService.cs - Deskmate", Url = "https://example.com/docs?token=abc#top",
                Kind = "coding", Focus = "IngatService.cs › Capture()",
            };
            snap.Headings.Add("Capture Loop");
            snap.Anchors.Add("IngatService.cs");
            snap.MainLines.Add("void Capture(bool force) mail budi@contoh.com");
            snap.NavLines.Add("Explorer");
            snap.RedactAll();
            Assert(snap.Url == "https://example.com/docs", "snapshot cleans url");
            string formatted = snap.FormatText(2000);
            Assert(formatted.Contains("[Fokus]") && formatted.Contains("[Bagian]") && formatted.Contains("[Konteks]") && formatted.Contains("[Isi]") && formatted.Contains("[Navigasi]"), "structured sections formatted");
            Assert(!formatted.Contains("budi@contoh.com"), "snapshot redacts body");
            var sj = snap.ToJson();
            Assert(Json.Str(sj, "focus").Contains("Capture") && Json.Arr(sj, "anchors").Length == 1 && Json.Str(sj, "excerpt").Length > 0, "snapshot json structured");
            snap.ClearSecret();
            Assert(snap.Secret && snap.FormatText(2000) == "" && snap.Focus == "" && snap.MainLines.Count == 0, "secret wipes snapshot");
        }

        static void TestRollingBodyAndStructuredEpisode()
        {
            // Scrolling: 95% identical lines must not duplicate
            var sb = new StringBuilder();
            for (int i = 1; i <= 40; i++) sb.Append("Baris dokumen tetap nomor ").Append(i).Append('\n');
            string screen1 = sb.ToString().TrimEnd();
            string screen2 = screen1 + "\nBaris baru setelah scroll 41\nBaris baru setelah scroll 42";
            string merged = IngatStructure.MergeBody(screen1, screen2, IngatStore.BodyCap);
            Assert(merged.Length < screen1.Length + 80 && merged.Contains("scroll 42"), "scroll deduplicates existing lines");

            // Long multi-hour episodes must roll instead of freezing at BodyCap
            string rolling = "Pembuka proyek awal yang penting";
            for (int step = 0; step < 60; step++)
            {
                var chunk = new StringBuilder();
                for (int line = 0; line < 10; line++)
                    chunk.Append("Langkah ").Append(step).Append(" baris unik nomor ").Append(line).Append(" dengan isi cukup panjang untuk uji kapasitas memori").Append('\n');
                rolling = IngatStructure.MergeBody(rolling, chunk.ToString(), IngatStore.BodyCap);
            }
            Assert(rolling.Length <= IngatStore.BodyCap, "rolling body stays within cap: " + rolling.Length);
            Assert(rolling.Contains("Pembuka proyek awal") && rolling.Contains("Langkah 59 baris unik nomor 9"), "rolling body keeps opening context and latest lines");

            using (var store = new IngatStore(TempDb()))
            {
                var t0 = DateTime.UtcNow.AddMinutes(-25);
                long ep = store.Open(t0, "Visual Studio Code", "code", "Deskmate", "IngatService.cs - Deskmate", "", "");
                Assert(store.Get(ep).Kind == "coding", "auto kind set on open");

                var snap1 = new IngatSnapshot { Source = "uia", Kind = "coding", Focus = "HookLoop" };
                snap1.Headings.Add("Window Events");
                snap1.Anchors.Add("IngatService.cs");
                snap1.Anchors.Add("CS0168");
                snap1.MainLines.Add("SetWinEventHook untuk fokus dan scroll");
                store.Touch(ep, t0.AddMinutes(3), "IngatService.cs - Deskmate", "", snap1.FormatText(2000), snap1);

                var snap2 = new IngatSnapshot { Source = "uia", Kind = "coding", Focus = "ReadSnapshot" };
                snap2.Headings.Add("Cached UIA");
                snap2.Anchors.Add("IngatText.cs");
                snap2.MainLines.Add("CacheRequest mempercepat pembacaan pohon UIA");
                store.Touch(ep, t0.AddMinutes(8), "IngatText.cs - Deskmate", "", snap2.FormatText(2000), snap2);
                store.Close(ep, t0.AddMinutes(10));

                var loaded = store.Get(ep);
                var json = loaded.ToJson(true);
                Assert(Json.Str(json, "focus") == "ReadSnapshot", "latest focus tracked");
                Assert(Json.Arr(json, "steps").Length == 2, "distinct file steps recorded: " + Json.Arr(json, "steps").Length);
                Assert(Json.Arr(json, "anchors").Length >= 3, "anchors accumulated across touches");

                // Search finds the episode by project name, by an earlier file step, and by diagnostic code
                Assert(store.Search("Deskmate", DateTime.UtcNow.AddDays(-1), DateTime.UtcNow, 5).Count == 1, "project name indexed in FTS");
                Assert(store.Search("IngatService", DateTime.UtcNow.AddDays(-1), DateTime.UtcNow, 5).Count == 1, "earlier step title still searchable after title changed");
                Assert(store.Search("CS0168", DateTime.UtcNow.AddDays(-1), DateTime.UtcNow, 5).Count == 1, "extracted anchor searchable");

                // AI summary with structured points and anchors upgrades context
                store.SetSummary(ep, "Mempercepat pembacaan layar dan menyusun episode terstruktur.", "coding",
                    new[] { "Menambah CacheRequest di IngatText.cs", "Menyimpan jejak langkah di IngatStore.cs" },
                    new[] { "IngatStore.cs" });
                var afterAi = store.Get(ep).ToJson(false);
                Assert(Json.Arr(afterAi, "points").Length == 2 && Json.Arr(afterAi, "anchors").Length >= 4, "curated AI points and anchors saved");
                Assert(store.Search("IngatStore", DateTime.UtcNow.AddDays(-1), DateTime.UtcNow, 5).Count == 1, "new AI anchor reindexed");

                // DropTiny boundaries: 10s lookup stays; 3s with clipboard event stays; 4s empty pass-through drops
                long quickLookup = store.Open(t0.AddMinutes(12), "Google Chrome", "chrome", "", "WinEvent Constants", "https://learn.microsoft.com/win32", "");
                store.Close(quickLookup, t0.AddMinutes(12).AddSeconds(10));
                store.DropTiny(quickLookup);
                Assert(store.Get(quickLookup) != null, "10s reference lookup kept");

                long quickCopy = store.Open(t0.AddMinutes(13), "Google Chrome", "chrome", "", "Stack Overflow", "", "");
                store.AddEvent(t0.AddMinutes(13).AddSeconds(2), quickCopy, "clipboard", "Google Chrome", "Stack Overflow", "", "EVENT_SYSTEM_SCROLLINGEND = 0x0013");
                store.Close(quickCopy, t0.AddMinutes(13).AddSeconds(3));
                store.DropTiny(quickCopy);
                Assert(store.Get(quickCopy) != null, "3s episode with clipboard action kept");

                long passThrough = store.Open(t0.AddMinutes(14), "Explorer", "explorer", "", "Desktop", "", "");
                store.Close(passThrough, t0.AddMinutes(14).AddSeconds(4));
                store.DropTiny(passThrough);
                Assert(store.Get(passThrough) == null, "4s empty pass-through dropped");

                // Timeline structured grouping
                var grouped = IngatStructure.GroupTimeline(store.Timeline(DateTime.UtcNow.AddHours(-1), DateTime.UtcNow, 20));
                Assert(Json.Arr(grouped, "byProject").Length >= 2 && Json.Arr(grouped, "byKind").Length >= 2, "timeline grouped by project and kind");
            }
        }

        static void TestDualZoneStampAndInMemoryOcr()
        {
            Assert(IngatText.ReadSnapshot(IntPtr.Zero, "code", "", false).Source == "window", "zero hwnd safe");

            using (var a = new Bitmap(400, 300))
            using (var b = new Bitmap(400, 300))
            {
                using (var ga = Graphics.FromImage(a))
                using (var gb = Graphics.FromImage(b))
                {
                    ga.Clear(Color.FromArgb(24, 24, 28));
                    gb.Clear(Color.FromArgb(24, 24, 28));
                    // Identical outer frame (sidebar + top bar)
                    ga.FillRectangle(Brushes.DimGray, 0, 0, 70, 300);
                    gb.FillRectangle(Brushes.DimGray, 0, 0, 70, 300);
                    ga.FillRectangle(Brushes.DarkSlateBlue, 0, 0, 400, 28);
                    gb.FillRectangle(Brushes.DarkSlateBlue, 0, 0, 400, 28);
                    // Different inner viewport content (simulating scrolled text/chat)
                    ga.FillRectangle(Brushes.WhiteSmoke, 110, 60, 220, 40);
                    gb.FillRectangle(Brushes.WhiteSmoke, 120, 190, 180, 55);
                }
                var sa = IngatText.Stamp(a);
                var saCopy = IngatText.Stamp(a);
                var sb = IngatText.Stamp(b);
                Assert(sa.Valid && !IngatText.Changed(sa, saCopy), "identical frame unchanged");
                Assert(IngatText.Changed(sa, sb), "inner content shift detected despite static chrome");
            }

            if (Ocr.Available)
            {
                using (var bmp = new Bitmap(520, 140))
                {
                    using (var g = Graphics.FromImage(bmp))
                    {
                        g.Clear(Color.White);
                        using (var f = new Font("Segoe UI", 22f, FontStyle.Bold))
                            g.DrawString("DESKMATE INGATAN 2026", f, Brushes.Black, 40, 42);
                    }
                    string placed = IngatText.OcrPlaced(bmp);
                    Assert(placed.IndexOf("DESKMATE", StringComparison.OrdinalIgnoreCase) >= 0 && placed.StartsWith("[", StringComparison.Ordinal), "in-memory placed OCR reads text: " + placed);
                    if (Directory.Exists(IngatStore.Dir))
                        Assert(Directory.GetFiles(IngatStore.Dir, "ocr-*.png").Length == 0, "no temp OCR png files written to disk");
                }

                // Classic GDI rendering leaves alpha = 0 in 32bpp bitmaps; SoftwareBitmap(Alpha=Ignore) must read it cleanly
                using (var gdiBmp = new Bitmap(600, 140, PixelFormat.Format32bppArgb))
                {
                    using (var g = Graphics.FromImage(gdiBmp))
                    {
                        IntPtr hdc = g.GetHdc();
                        PatBlt(hdc, 0, 0, 600, 140, 0x00FF0062);
                        SetBkColor(hdc, 0xFFFFFF);
                        SetTextColor(hdc, 0x000000);
                        TextOut(hdc, 40, 50, "DESKMATE GDI WINDOW 2026", 24);
                        g.ReleaseHdc(hdc);
                    }
                    string gdiText = IngatText.OcrPlaced(gdiBmp);
                    Assert(gdiText.IndexOf("DESKMATE", StringComparison.OrdinalIgnoreCase) >= 0, "GDI alpha=0 bitmap OCR works: " + gdiText);
                }

                // 4K screenshot OCR must downscale cleanly without >30 MB LOH bloat
                GC.Collect();
                GC.WaitForPendingFinalizers();
                GC.Collect();
                long before = GC.GetTotalMemory(false);
                using (var bmp4k = new Bitmap(3840, 2160, PixelFormat.Format32bppArgb))
                {
                    using (var g4 = Graphics.FromImage(bmp4k))
                    {
                        g4.Clear(Color.White);
                        using (var f = new Font("Segoe UI", 36f))
                            g4.DrawString("DESKMATE 4K SCREEN", f, Brushes.Black, 120, 120);
                    }
                    string t4k = IngatText.OcrPlaced(bmp4k);
                    long deltaMb = (GC.GetTotalMemory(false) - before) / 1048576;
                    Assert(t4k.IndexOf("DESKMATE", StringComparison.OrdinalIgnoreCase) >= 0, "4K OCR reads text: " + t4k);
                    Assert(deltaMb < 28, "4K OCR keeps managed memory delta bounded: " + deltaMb + " MB");
                }
            }
        }

        static void TestLiveQueriesAndStructuredFallback()
        {
            var now = new DateTime(2026, 11, 24, 15, 0, 0, DateTimeKind.Local);
            DateTime f, t;
            Assert(IngatTime.Range("15 menit terakhir saya ngapain?", now, out f, out t) && f == now.AddMinutes(-15) && t == now, "minute range parsed");
            Assert(IngatStore.FtsQuery("15 menit terakhir saya ngapain?") == "", "minute range query leaves no spurious numeric FTS token");
            Assert(IngatStore.FtsQuery("apa yang sedang kubuka di layar sekarang?") == "", "live screen question in ID has no spurious FTS keywords");
            Assert(IngatStore.FtsQuery("what am I looking at on screen right now?") == "", "live screen question in EN has no spurious FTS keywords");
            Assert(IngatStore.FtsQuery("rangkum aktivitas saya hari ini") == "", "broad recap question has no spurious FTS keywords");

            // Emoji surrogate safety and metadata deduplication in MergeBody
            string baseBody = "[Fokus] Awal\n[Bagian] Satu\nBaris utama emoji 😀🚀🔥 nomor satu";
            string nextBody = "[Fokus] Kedua\n[Bagian] Satu · Dua\n[Navigasi] Berkas.cs\nBaris utama emoji 😀🚀🔥 nomor satu\nBaris baru setelah scroll 😀🚀";
            string merged = IngatStructure.MergeBody(baseBody, nextBody, 95);
            Assert(!merged.Contains("[Navigasi]") && !merged.Contains("[Fokus] Kedua"), "MergeBody skips repeated metadata headers");
            for (int i = 0; i < merged.Length; i++)
            {
                if (char.IsHighSurrogate(merged[i])) Assert(i + 1 < merged.Length && char.IsLowSurrogate(merged[i + 1]), "high surrogate paired");
                if (char.IsLowSurrogate(merged[i])) Assert(i > 0 && char.IsHighSurrogate(merged[i - 1]), "low surrogate paired");
            }

            // Structured fallback when AI is off: live screen context + project summary must still answer cleanly
            using (var store = new IngatStore(TempDb()))
            {
                var settings = new Settings();
                settings.Set("ingat.ai", "off");
                var brain = new IngatBrain(store, settings);

                var snap = new IngatSnapshot
                {
                    Source = "uia", App = "Visual Studio Code", Exe = "code", Project = "Deskmate",
                    Title = "IngatText.cs - Deskmate", Kind = "coding", Focus = "WalkStructured",
                };
                snap.Anchors.Add("IngatText.cs");
                snap.Anchors.Add("CS0103");
                snap.MainLines.Add("CacheRequest element-only scope");
                var liveNow = new Dictionary<string, object> { { "state", "on" }, { "screen", snap.ToJson() } };

                var liveOnlyAns = brain.Ask("apa yang sedang kubuka di layar sekarang?", liveNow);
                string ansText = Json.Str(liveOnlyAns, "answer");
                Assert(ansText.Contains("[Layar aktif]") && ansText.Contains("WalkStructured") && ansText.Contains("CS0103"),
                    "fallback Ask includes live screen focus and anchors even with zero stored episodes: " + ansText);
                Assert(Json.Obj(liveOnlyAns, "screen") != null, "Ask response exposes structured screen object");

                // Broad day recap without AI must return main episodes even if one unrelated episode had literal word 'aktivitas'.
                // "Hari ini" starts at local midnight: keep both episodes inside today, however soon after 00:00 this runs
                var dayStartUtc = DateTime.Today.ToUniversalTime();
                double span = Math.Min(50 * 60, (DateTime.UtcNow - dayStartUtc).TotalSeconds - 2);
                var t0 = DateTime.UtcNow.AddSeconds(-span);
                Func<double, DateTime> at = share => t0.AddSeconds(span * share);
                long noise = store.Open(t0, "Chrome", "chrome", "", "Artikel aktivitas harian", "", "reading");
                store.Touch(noise, at(0.02), "Artikel aktivitas harian", "", "Tips mencatat aktivitas");
                store.Close(noise, at(0.02));
                long mainWork = store.Open(at(0.06), "Visual Studio Code", "code", "Deskmate", "IngatService.cs - Deskmate", "", "coding");
                store.Touch(mainWork, at(0.6), "IngatService.cs - Deskmate", "", "Perbaikan EnsureFresh dan UIA");
                store.Close(mainWork, at(0.6));

                var recapAns = brain.Ask("rangkum aktivitas saya hari ini", liveNow);
                Assert(Json.Arr(recapAns, "sources").Length >= 2 && Json.Str(recapAns, "answer").Contains("Deskmate"),
                    "broad recap includes main coding episode instead of matching literal word 'aktivitas'");

                // Chat without AI falls back to the live screen instead of a bare "cannot think"
                var chatLive = brain.Chat("sekarang aku lagi buka apa?", new List<KeyValuePair<string, string>>(), "Rubah", "cat", liveNow);
                string chatText = Json.Str(chatLive, "answer");
                Assert(chatText.Contains("Visual Studio Code") && chatText.Contains("WalkStructured"), "Chat fallback without AI answers from live screen: " + chatText);
                var chatNone = brain.Chat("sekarang aku lagi buka apa?", new List<KeyValuePair<string, string>>(), "Rubah", "cat", new Dictionary<string, object>());
                Assert(!Json.Str(chatNone, "answer").Contains("[Layar aktif]") && chatText.Contains("[Layar aktif]"), "Chat fallback without live context has no screen line");

                // Fallback labels are localized
                bool wasEnglish = Lang.English;
                try
                {
                    Lang.English = true;
                    string en = Json.Str(brain.Ask("apa yang sedang kubuka di layar sekarang?", liveNow), "answer");
                    Assert(en.Contains("[Live screen]") && !en.Contains("[Layar aktif]"), "English fallback label: " + en);
                    var enRecap = Json.Str(brain.Ask("rangkum aktivitas saya hari ini", liveNow), "answer");
                    Assert(!enRecap.Contains(" mnt)") && !enRecap.Contains("Ringkasan:"), "English recap has no Indonesian labels: " + enRecap);
                }
                finally { Lang.English = wasEnglish; }

                // Settings.Load must be safe against concurrent readers: no torn or empty reads
                var shared = new Settings();
                shared.Set("ingat.probe", "1");
                shared.Save();
                shared.Load();
                int torn = 0;
                var stop = false;
                var readers = new List<Thread>();
                for (int i = 0; i < 3; i++)
                {
                    var rt = new Thread(delegate () { while (!stop) if (shared.Get("ingat.probe", "") != "1") Interlocked.Increment(ref torn); });
                    rt.Start();
                    readers.Add(rt);
                }
                for (int i = 0; i < 300; i++) shared.Load();
                stop = true;
                foreach (var rt in readers) rt.Join();
                Assert(torn == 0, "Settings.Load concurrent with reads never exposes a cleared state: " + torn);            }
        }

        static IngatSnapshot RunFormSnapshot(Func<Form> makeForm, string exe, bool chatMode, out long elapsedMs)
        {
            IngatSnapshot snap = null;
            long ms = 0;
            Exception err = null;
            var thread = new Thread(delegate ()
            {
                try
                {
                    using (var form = makeForm())
                    {
                        form.StartPosition = FormStartPosition.Manual;
                        form.Location = new Point(40, 40);
                        form.ShowInTaskbar = false;
                        form.Opacity = 0.02;
                        form.Shown += delegate
                        {
                            IntPtr hwnd = form.Handle;
                            var worker = new Thread(delegate ()
                            {
                                var sw = Stopwatch.StartNew();
                                snap = IngatText.ReadSnapshot(hwnd, exe, form.Text, chatMode);
                                sw.Stop();
                                ms = sw.ElapsedMilliseconds;
                                try { form.BeginInvoke(new Action(form.Close)); } catch (Exception) { }
                            });
                            worker.SetApartmentState(ApartmentState.MTA);
                            worker.Start();
                        };
                        Application.Run(form);
                    }
                }
                catch (Exception ex) { err = ex; }
            });
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
            thread.Join(15000);
            if (err != null) throw err;
            elapsedMs = ms;
            return snap;
        }

        static void TestLiveUiaSnapshotAndChat()
        {
            long ms;
            // 1. Heavy 700-item sidebar TreeView must not starve deeply nested main editor content
            var heavy = RunFormSnapshot(delegate
            {
                var form = new Form { Text = "IngatService.cs - Deskmate - Visual Studio Code", Size = new Size(1200, 800) };
                var tree = new TreeView { Bounds = new Rectangle(0, 40, 200, 720) };
                for (int i = 1; i <= 700; i++) tree.Nodes.Add("sidebar_item_" + i + ".txt");
                form.Controls.Add(tree);
                var mainPanel = new Panel { Bounds = new Rectangle(240, 40, 920, 720) };
                var p1 = new Panel { Bounds = new Rectangle(0, 0, 900, 700) }; mainPanel.Controls.Add(p1);
                var p2 = new Panel { Bounds = new Rectangle(0, 0, 900, 700) }; p1.Controls.Add(p2);
                var p3 = new Panel { Bounds = new Rectangle(0, 0, 900, 700) }; p2.Controls.Add(p3);
                p3.Controls.Add(new Label { Text = "Error CS0103 di src/IngatText.cs baris utama", Bounds = new Rectangle(20, 30, 620, 40) });
                form.Controls.Add(mainPanel);
                return form;
            }, "code", false, out ms);

            string heavyFmt = heavy.FormatText(4000);
            Assert(heavy.Source == "uia" && heavyFmt.Contains("Error CS0103 di src/IngatText.cs baris utama"),
                "heavy 700-node sidebar did not starve main editor (" + ms + "ms): " + heavyFmt);
            Assert(heavy.Anchors.Contains("CS0103") && heavy.Anchors.Contains("src/IngatText.cs"), "anchors extracted from live UIA window");
            Assert(heavy.NavLines.Count > 0 && heavy.NavLines.Count <= 12, "sidebar tree items capped into NavLines: " + heavy.NavLines.Count);

            // 2. Split editor panes on both left (~22% width) and right must both stay in MainLines
            var split = RunFormSnapshot(delegate
            {
                var form = new Form { Text = "Split - Deskmate - Visual Studio Code", Size = new Size(1400, 800) };
                form.Controls.Add(new Label { Text = "Panel kiri kode utama pertama", Bounds = new Rectangle(30, 140, 280, 40) });
                form.Controls.Add(new Label { Text = "Panel kanan kode utama kedua", Bounds = new Rectangle(360, 140, 600, 40) });
                return form;
            }, "code", false, out ms);
            Assert(split.MainLines.Contains("Panel kiri kode utama pertama") && split.MainLines.Contains("Panel kanan kode utama kedua"),
                "both split editor panes captured in MainLines (" + ms + "ms)");

            // 3. Password field inside window immediately marks snapshot secret and wipes content
            var pwSnap = RunFormSnapshot(delegate
            {
                var form = new Form { Text = "Pengaturan Akun", Size = new Size(800, 500) };
                form.Controls.Add(new Label { Text = "Teks biasa yang tidak boleh bocor", Bounds = new Rectangle(40, 40, 400, 30) });
                form.Controls.Add(new TextBox { Text = "rahasia123", UseSystemPasswordChar = true, Bounds = new Rectangle(40, 100, 300, 30) });
                return form;
            }, "app", false, out ms);
            Assert(pwSnap.Secret && pwSnap.FormatText(2000) == "" && pwSnap.MainLines.Count == 0, "password control triggers secret wipe");

            // 4. Narrow single-pane WhatsApp window detects header conversation name and [x0-x1] bubble positions
            var chatSnap = RunFormSnapshot(delegate
            {
                var form = new Form { Text = "WhatsApp", Size = new Size(480, 750) };
                form.Controls.Add(new Label { Text = "Budi Santoso", Bounds = new Rectangle(68, 24, 180, 28) });
                form.Controls.Add(new Label { Text = "Proposal sudah dikirim?", Bounds = new Rectangle(24, 160, 220, 30) });
                form.Controls.Add(new Label { Text = "Sudah aku kirim barusan", Bounds = new Rectangle(220, 220, 230, 30) });
                return form;
            }, "whatsapp.root", true, out ms);
            string chatFmt = chatSnap.FormatText(4000);
            Assert(chatSnap.Focus == "Budi Santoso", "narrow chat header detected: [" + chatSnap.Focus + "]");
            Assert(chatFmt.Contains("Proposal sudah dikirim?") && chatFmt.Contains("Sudah aku kirim barusan"), "chat bubbles captured in order: " + chatFmt);
        }

        static void TestLiveServiceAndMcp()
        {
            string exe = typeof(IngatCheck).Assembly.Location;
            if (!File.Exists(exe)) return;

            var settings = new Settings();
            settings.Set("ingat", "1");
            settings.Set("ingat.ai", "off");
            settings.Save();

            int selfPid = Process.GetCurrentProcess().Id;
            var psi = new ProcessStartInfo(exe, "--ingat --parent " + selfPid)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            psi.EnvironmentVariables["DESKMATE_DIR"] = Settings.Dir;

            using (var proc = Process.Start(psi))
            {
                try
                {
                    Dictionary<string, object> st = null;
                    var sw = Stopwatch.StartNew();
                    while (sw.ElapsedMilliseconds < 8000)
                    {
                        st = IngatClient.Get("/status", 1500);
                        if (st != null) break;
                        Thread.Sleep(100);
                    }
                    Assert(st != null && st.ContainsKey("state"), "ingat service came up fast");

                    var ctx = IngatClient.Get("/context?recent=4", 3000);
                    Assert(ctx != null && ctx.ContainsKey("today") && ctx.ContainsKey("recent"), "live /context endpoint returns structured workspace");

                    var tl = IngatClient.Get("/timeline", 3000);
                    Assert(tl != null && Json.Obj(tl, "structured") != null, "live /timeline includes structured grouping");

                    var sr = IngatClient.Get("/search?q=wasapi", 3000);
                    Assert(sr != null && Json.Obj(sr, "structured") != null, "live /search includes structured grouping");

                    var ask = IngatClient.Call("POST", "/ask", new Dictionary<string, object> { { "q", "apa yang saya kerjakan hari ini?" } }, 5000);
                    Assert(ask != null && ask.ContainsKey("answer") && Json.Obj(ask, "structured") != null, "live /ask returns structured metadata");

                    var liveAsk = IngatClient.Call("POST", "/ask", new Dictionary<string, object> { { "q", "apa yang sedang kubuka di layar sekarang?" } }, 5000);
                    Assert(liveAsk != null && Json.Str(liveAsk, "answer").Length > 0, "live /ask answers screen-now query cleanly");

                    // MCP subprocess over stdio against the running service
                    var mcpPsi = new ProcessStartInfo(exe, "--mcp")
                    {
                        UseShellExecute = false,
                        CreateNoWindow = true,
                        RedirectStandardInput = true,
                        RedirectStandardOutput = true,
                    };
                    mcpPsi.EnvironmentVariables["DESKMATE_DIR"] = Settings.Dir;
                    using (var mcp = Process.Start(mcpPsi))
                    {
                        mcp.StandardInput.WriteLine("{\"jsonrpc\":\"2.0\",\"id\":1,\"method\":\"tools/list\",\"params\":{}}");
                        mcp.StandardInput.WriteLine("{\"jsonrpc\":\"2.0\",\"id\":2,\"method\":\"tools/call\",\"params\":{\"name\":\"get_screen_context\",\"arguments\":{\"recent\":3}}}");
                        mcp.StandardInput.Close();
                        string mcpOut = mcp.StandardOutput.ReadToEnd();
                        mcp.WaitForExit(5000);
                        Assert(mcpOut.Contains("get_screen_context") && mcpOut.Contains("current_context"), "MCP exposes get_screen_context tool");
                        Assert(mcpOut.Contains("inboxWaiting") && !mcpOut.Contains("\"isError\":true"), "MCP get_screen_context call succeeded: " + mcpOut);
                    }
                }
                finally
                {
                    settings.Set("ingat", "0");
                    settings.Save();
                    try { if (!proc.WaitForExit(2000)) proc.Kill(); } catch (Exception) { }
                }
            }
        }
    }
}
