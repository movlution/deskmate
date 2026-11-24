using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Threading;

namespace Deskmate
{
    // Ingatan seen from the island: keeps the service alive, feeds it the clipboard,
    // records calls when asked, and lets the pet and Suruh use what it remembers
    partial class Island
    {
        internal bool IngatOn { get { return settings.GetBool("ingat", false); } }

        DateTime ingatTickUtc, ingatStartedUtc = DateTime.MinValue, ingatNowUtc;
        int ingatRestarts;
        // A recording Ingatan started for a call, and will stop when it ends
        bool ingatRapat;
        DateTime micQuietUtc = DateTime.MinValue;
        // Pet lines: once per streak, once per morning
        DateTime petStreakSaidUtc = DateTime.MinValue;
        string petRecapDay = "";
        volatile bool ingatAsking;

        // Called once a second from ClockTick
        void IngatTick()
        {
            var now = DateTime.UtcNow;
            IngatRapatTick(now);
            if ((now - ingatTickUtc).TotalSeconds < 15) return;
            ingatTickUtc = now;
            if (!IngatOn) return;
            EnsureIngat();
            if ((now - ingatNowUtc).TotalSeconds >= 60 && !ingatAsking) { ingatNowUtc = now; IngatPetLook(); }
        }

        // Start the service when it is not running; a crashing one gets three tries an hour
        void EnsureIngat()
        {
            if (IngatClient.Running()) return;
            if ((DateTime.UtcNow - ingatStartedUtc).TotalHours >= 1) ingatRestarts = 0;
            if (ingatRestarts >= 3 || (DateTime.UtcNow - ingatStartedUtc).TotalSeconds < 20) return;
            ingatRestarts++;
            ingatStartedUtc = DateTime.UtcNow;
            try
            {
                var psi = new ProcessStartInfo(Process.GetCurrentProcess().MainModule.FileName, "--ingat --parent " + Process.GetCurrentProcess().Id)
                {
                    UseShellExecute = false,
                    CreateNoWindow = true,
                };
                using (Process.Start(psi)) { }
            }
            catch (Exception ex) { Log.Write("ingat start: " + ex.Message); }
        }

        // Switch flipped in Settings; the service sees "ingat=0" and quits on its own within seconds
        void ApplyIngat()
        {
            ingatRestarts = 0;
            ingatStartedUtc = DateTime.MinValue;
            ingatTickUtc = DateTime.MinValue;
            ApplyClips();
            if (IngatOn) EnsureIngat();
        }

        // Clipboard text, after Clips.cs dropped what password managers flag
        void IngatClipboard(System.Windows.Forms.IDataObject data)
        {
            if (!IngatOn || !settings.GetBool("ingat.clip", true)) return;
            string text;
            try { text = data.GetData(System.Windows.Forms.DataFormats.UnicodeText) as string; }
            catch (Exception) { return; }
            if (string.IsNullOrEmpty(text) || text.Trim().Length < 3) return;
            if (text.Length > 4000) text = text.Substring(0, 4000);
            ThreadPool.QueueUserWorkItem(delegate { IngatClient.Call("POST", "/event", new Dictionary<string, object> { { "source", "clipboard" }, { "text", text } }, 3000); });
        }

        // Calls: record when the mic has been busy 20 s, stop 15 s after it goes quiet
        void IngatRapatTick(DateTime now)
        {
            bool want = IngatOn && settings.GetBool("ingat.rapat", false);
            bool call = micSinceUtc != DateTime.MinValue && (now - micSinceUtc).TotalSeconds >= MeetAfterSec;
            if (call) micQuietUtc = DateTime.MinValue;
            else if (micQuietUtc == DateTime.MinValue) micQuietUtc = now;
            if (want && call && !RapatOn && !rapatWriting && !ingatRapat && DictMissing() == null)
            {
                StartRapat();
                ingatRapat = RapatOn;
            }
            else if (ingatRapat && (!RapatOn || (!call && (now - micQuietUtc).TotalSeconds >= 15)))
            {
                if (RapatOn) StopRapat();
                ingatRapat = false;
            }
        }

        // Pet: a nudge after long focus, a recap of yesterday on the first look of the day
        void IngatPetLook()
        {
            if (pet == null || pet.IsDisposed || !settings.GetBool("ingat.pet", true)) return;
            ingatAsking = true;
            string today = DateTime.Today.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            bool wantRecap = petRecapDay != today && DateTime.Now.Hour >= 5 && DateTime.Now.Hour < 12;
            ThreadPool.QueueUserWorkItem(delegate
            {
                string line = null;
                try
                {
                    var now = IngatClient.Get("/now", 5000);
                    if (now == null) return;
                    if (wantRecap && Json.Str(now, "state") == "on")
                    {
                        line = YesterdayLine();
                        Post(delegate { petRecapDay = today; });
                    }
                    var ep = Json.Obj(now, "episode");
                    int streak = (int)Json.Num(now, "streakMinutes", 0);
                    if (line == null && ep != null && streak >= 120 && (DateTime.UtcNow - petStreakSaidUtc).TotalHours >= 2)
                    {
                        string what = Json.Str(ep, "project");
                        if (what.Length == 0) what = Json.Str(ep, "app");
                        line = Lang.F("Sudah {0} jam di {1}. Istirahat sebentar?", (streak / 60).ToString(CultureInfo.InvariantCulture), what);
                        Post(delegate { petStreakSaidUtc = DateTime.UtcNow; });
                    }
                }
                catch (Exception ex) { Log.Write("ingat pet: " + ex.Message); }
                finally
                {
                    Post(delegate
                    {
                        ingatAsking = false;
                        if (line != null && pet != null && !pet.IsDisposed) pet.ShowChatBubble(PetDisplayName, line, false, delegate { AskIngat(""); });
                    });
                }
            });
        }

        // "Kemarin kamu berhenti di Deskmate · PetActions.cs (VS Code)" from the last real episode
        static string YesterdayLine()
        {
            var y = IngatClient.Get("/timeline?date=" + DateTime.Today.AddDays(-1).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), 5000);
            if (y == null) return null;
            var eps = Json.Arr(y, "episodes");
            for (int i = eps.Length - 1; i >= 0; i--)
            {
                if (Json.Num(eps[i], "minutes", 0) < 5) continue;
                string project = Json.Str(eps[i], "project"), app = Json.Str(eps[i], "app"), title = Fmt.OneLine(Json.Str(eps[i], "title"), 60);
                return Lang.F("Kemarin kamu terakhir di {0}. Lanjut?", (project.Length > 0 ? project + " · " : "") + title + " (" + app + ")");
            }
            return null;
        }

        // Pet bar and bubble: Suruh chat opens with a memory question ready
        internal void AskIngat(string question)
        {
            chatDraft = question.Length > 0 ? question : Lang.T("Tadi saya mengerjakan apa saja?");
            OpenSuruh();
            if (chatWin != null && !chatWin.IsDisposed) chatWin.SetDraft(chatDraft);
        }

        // Suruh's read_deskmate "activity" section; runs on the worker, never the UI thread
        static object ReadIngatActivity(string query)
        {
            if (!IngatClient.Running()) return new { status = "Ingatan is off. The user can turn it on in Settings › Ingatan." };
            var r = IngatClient.Call("POST", "/ask", new Dictionary<string, object> { { "q", query.Length > 0 ? query : "apa yang saya kerjakan hari ini?" } }, 120000);
            if (r == null) return new { status = "Ingatan did not answer" };
            return new { answer = Json.Str(r, "answer"), moments = Json.Arr(r, "sources") };
        }
    }
}
