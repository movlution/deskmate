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
            return "Ingatan checks passed";
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
                Assert(!brain.Thinking && brain.EmbedModel == "", "ai off");
                Assert(brain.Search("wasapi", DateTime.UtcNow.AddDays(-1), DateTime.UtcNow, 5)[0].Id == a, "brain search");
                var ans = brain.Ask("apa yang saya kerjakan soal wasapi tadi?");
                Assert(Json.Arr(ans, "sources").Length >= 1, "ask has sources without a model");
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
