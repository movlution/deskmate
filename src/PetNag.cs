using System;
using System.Collections.Generic;

namespace Deskmate
{
    // The pet now and then reminds the user of open tasks and upcoming events, in its own chat bubble.
    // Random gaps so it feels alive rather than scheduled; only while the user is at the desk.
    partial class Island
    {
        const int PetNagMinMinutes = 25, PetNagMaxMinutes = 55;
        // Events this close count as "coming up"; deadlines of linked sources within this many days
        const double PetNagEventHours = 3, PetNagDeadlineDays = 3;

        readonly Random petNagRng = new Random();
        DateTime petNagNextUtc = DateTime.MinValue;
        string petNagLastKey = "";

        bool PetNagOn { get { return settings.GetBool("pet.nag", true); } }

        sealed class PetNagPick
        {
            public string Key = "", Text = "";
            public bool Event;
            public int Weight = 1;
        }

        void PetNagSchedule(DateTime nowUtc, int minMinutes, int maxMinutes)
        {
            petNagNextUtc = nowUtc.AddMinutes(minMinutes + petNagRng.NextDouble() * (maxMinutes - minMinutes));
        }

        // Called each clock tick, after the wellness nudges had their turn
        bool PetNagTick(DateTime nowUtc, double idle)
        {
            if (!PetOn || !PetNagOn || pet == null || pet.IsDisposed) return false;
            // First tick after launch: give the user a few minutes to settle in
            if (petNagNextUtc == DateTime.MinValue) { PetNagSchedule(nowUtc, 5, 15); return false; }
            if (nowUtc < petNagNextUtc) return false;
            int hour = DateTime.Now.Hour;
            if (idle > 120 || toast != null || fullscreen || Quiet || PrompterOn || expanded || hour < 7 || hour >= 23 ||
                petAskBusy || (petAskInput != null && !petAskInput.IsDisposed) ||
                (petChatInput != null && !petChatInput.IsDisposed) ||
                !pet.CanShowBubble || pet.ChatOpen || pet.WellnessOpen || (MiniOn() && !MiniLets(View.Remind, 2)))
            {
                // Busy or away: look again in a little while instead of waiting the whole gap
                petNagNextUtc = nowUtc.AddMinutes(3);
                return false;
            }

            var pick = PetNagChoose(DateTime.Now);
            PetNagSchedule(nowUtc, PetNagMinMinutes, PetNagMaxMinutes);
            if (pick == null) return false;
            petNagLastKey = pick.Key;
            bool isEvent = pick.Event;
            pet.PopEmote(PetEmote.Bang);
            pet.ShowChatBubble(PetAskName, pick.Text, false, delegate
            {
                if (isEvent) OpenAskSection("schedule");
                else OpenWork(0);
            }, 14);
            Announce(PetAskName + ": " + pick.Text);
            return false;
        }

        PetNagPick PetNagChoose(DateTime now)
        {
            var picks = new List<PetNagPick>();
            var today = now.Date;

            EnsureWorkData();
            if (todos != null)
                foreach (var t in todos)
                {
                    if (t.Done || string.IsNullOrEmpty(t.Title)) continue;
                    string title = Fmt.OneLine(t.Title, 60);
                    var p = new PetNagPick { Key = "t:" + t.Id };
                    if (t.Due.HasValue && t.Due.Value.Date < today)
                    {
                        p.Weight = 6;
                        p.Text = PetNagLine(title, Lang.T("\"{0}\" sudah lewat tenggat. Yuk, selesaikan sekarang!"),
                            Lang.T("Psst… \"{0}\" masih menunggu dari kemarin. Kita kerjakan, yuk?"));
                    }
                    else if (t.Due.HasValue && t.Due.Value.Date == today)
                    {
                        p.Weight = 5;
                        bool timed = t.Due.Value.TimeOfDay != TimeSpan.Zero;
                        p.Text = timed && t.Due.Value > now
                            ? Lang.F("\"{0}\" tenggatnya {1} lagi. Semangat!", title, Fmt.Left(t.Due.Value - now))
                            : PetNagLine(title, Lang.T("\"{0}\" harus selesai hari ini, lho!"),
                                Lang.T("Jangan lupa, \"{0}\" tenggatnya hari ini."));
                    }
                    else if (t.Due.HasValue && t.Due.Value.Date == today.AddDays(1))
                    {
                        p.Weight = 3;
                        p.Text = Lang.F("\"{0}\" tenggatnya besok. Cicil dari sekarang, yuk!", title);
                    }
                    else
                    {
                        p.Weight = t.Star ? 3 : 1;
                        p.Text = PetNagLine(title, Lang.T("Masih ada \"{0}\" yang belum selesai. Yuk, kita bereskan!"),
                            Lang.T("Hei, \"{0}\" belum dicentang. Sedikit lagi, pasti bisa!"),
                            Lang.T("Gimana kabar \"{0}\"? Ayo selesaikan biar lega."));
                    }
                    picks.Add(p);
                }

            // Linked sources (Kulia, Notion, Todoist…) with a deadline soon
            if (tasks != null)
                foreach (var t in tasks)
                {
                    if (t.Closed || string.IsNullOrEmpty(t.Title) || t.DeadlineUtc == DateTime.MinValue) continue;
                    var due = t.DeadlineUtc.ToLocalTime();
                    var left = due - now;
                    if (left.TotalDays > PetNagDeadlineDays) continue;
                    string title = Fmt.OneLine(t.Title, 60);
                    picks.Add(new PetNagPick
                    {
                        Key = "x:" + t.Source + ":" + t.Id,
                        Weight = left.TotalHours < 0 ? 6 : left.TotalHours < 24 ? 5 : 3,
                        Text = left.TotalMinutes <= 0
                            ? Lang.F("\"{0}\" sudah lewat tenggat. Yuk, selesaikan sekarang!", title)
                            : Lang.F("\"{0}\" tenggatnya {1} lagi. Semangat!", title, Fmt.Left(left)),
                    });
                }

            // Events starting soon
            if (classes != null)
                foreach (var c in classes)
                {
                    if (c.Cancelled || c.AllDay || string.IsNullOrEmpty(c.Title)) continue;
                    var left = c.Start - now;
                    if (left.TotalMinutes < 5 || left.TotalHours > PetNagEventHours) continue;
                    string title = Fmt.OneLine(c.Title, 60);
                    picks.Add(new PetNagPick
                    {
                        Key = "e:" + c.Id + ":" + c.Start.Ticks,
                        Event = true,
                        Weight = left.TotalMinutes <= 60 ? 6 : 4,
                        Text = PetNagLine("", Lang.F("\"{0}\" mulai {1} lagi. Siap-siap, ya!", title, Fmt.Left(left)),
                            Lang.F("Sebentar lagi ada \"{0}\" ({1}). Jangan sampai telat!", title, Fmt.Hm(c.Start))),
                    });
                }

            if (picks.Count == 0) return null;
            // Don't repeat the same thing twice in a row when there is something else
            if (picks.Count > 1) picks.RemoveAll(x => x.Key == petNagLastKey);
            int total = 0;
            foreach (var x in picks) total += x.Weight;
            int roll = petNagRng.Next(total);
            foreach (var x in picks)
            {
                roll -= x.Weight;
                if (roll < 0) return x;
            }
            return picks[picks.Count - 1];
        }

        // One of a few phrasings, so the nag doesn't sound like a recording
        string PetNagLine(string title, params string[] forms)
        {
            string f = forms[petNagRng.Next(forms.Length)];
            return title.Length == 0 ? f : string.Format(System.Globalization.CultureInfo.InvariantCulture, f, title);
        }
    }
}
