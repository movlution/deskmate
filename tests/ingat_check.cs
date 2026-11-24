using System;
using System.Collections.Generic;
using System.IO;

namespace Deskmate
{
    // Ingatan without the screen: storage, search, privacy, time words, MCP framing
    public static class IngatCheck
    {
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
            TestMcpWithoutService();
            TestChatDetection();
            TestPeopleAndMessages();
            TestDirectionAndState();
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

        static void TestMcpWithoutService()
        {
            Assert(!IngatClient.Running() || true, "client probe does not throw");
            Assert(IngatMcp.ConfigJson().Contains("--mcp"), "config names the flag");
        }
    }
}
