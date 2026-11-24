using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace Deskmate
{
    enum RemindKind { None, Drink, Eyes, Stretch }

    // Pengingat: prayer, water, eyes, moving
    partial class Island
    {
        [StructLayout(LayoutKind.Sequential)]
        struct LASTINPUTINFO { public uint cbSize, dwTime; }
        [DllImport("user32.dll")] static extern bool GetLastInputInfo(ref LASTINPUTINFO info);

        static double IdleSeconds()
        {
            var li = new LASTINPUTINFO { cbSize = (uint)Marshal.SizeOf(typeof(LASTINPUTINFO)) };
            if (!GetLastInputInfo(ref li)) return 0;
            return (uint)(Environment.TickCount - (int)li.dwTime) / 1000.0;
        }

        DateTime[] prayToday;
        DateTime prayDay = DateTime.MinValue;
        int prayAnnounced = -1, prayWarned = -1;
        DateTime lastDrinkUtc = DateTime.UtcNow, lastEyesUtc = DateTime.UtcNow, lastStretchUtc = DateTime.UtcNow, snoozeUntilUtc = DateTime.MinValue;
        DateTime restUntilUtc = DateTime.MinValue;
        RemindKind remindShown;
        bool wellLoaded;

        // Well clocks live in settings: a restart must not zero a countdown
        static readonly string[] WellLastKeys = { "drink.last", "eyes.last", "stretch.last" };
        static readonly DateTime UnixEpoch = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        // Intervals in minutes
        const int EyesEvery = 20;
        int DrinkEvery { get { return Math.Max(15, settings.GetInt("drink.every", 60)); } }
        int StretchEvery { get { return Math.Max(30, settings.GetInt("stretch.every", 60)); } }

        DateTime WellLast(int i)
        {
            long sec;
            if (!long.TryParse(settings.Get(WellLastKeys[i], ""), NumberStyles.Integer, CultureInfo.InvariantCulture, out sec) || sec <= 0) return DateTime.MinValue;
            try { return UnixEpoch.AddSeconds(sec); }
            catch (ArgumentOutOfRangeException) { return DateTime.MinValue; }
        }

        // Save is opt-in: a quiet per-tick reset writes no disk
        void SetWell(int i, DateTime atUtc, bool save)
        {
            settings.Set(WellLastKeys[i], (long)(atUtc - UnixEpoch).TotalSeconds);
            if (save) settings.Save();
        }

        // Once per launch: continue from what was stored, so an overdue nudge is still due
        void LoadWellClocks()
        {
            if (wellLoaded) return;
            wellLoaded = true;
            var now = DateTime.UtcNow;
            var stored = new DateTime[3];
            for (int i = 0; i < 3; i++)
            {
                var at = WellLast(i);
                // Epoch, clock skew, or older than a day: no usable history, start fresh
                stored[i] = at == DateTime.MinValue || at > now || (now - at).TotalHours >= 24 ? now : at;
            }
            lastDrinkUtc = stored[0];
            lastEyesUtc = stored[1];
            lastStretchUtc = stored[2];
        }

        bool PrayOn { get { return settings.GetBool("pray", false) && PrayCity() != null; } }

        // A picked city, or a searched place saved as "name|lat|lng|tz"
        object[] PrayCity()
        {
            string want = settings.Get("pray.city", "");
            if (want.Length == 0) return null;
            int bar = want.LastIndexOf('|');
            if (bar > 0) return Cities.Parse(want) ?? Cities.Find(want.Substring(0, bar));
            return Cities.Find(want);
        }

        readonly string[] prayHm = new string[6];
        string prayTomorrowHm = "", prayFor = "", todayKey = "";
        DateTime prayTomorrow, todayKeyDay;

        // The place's own wall clock. Times are computed for the city, so every comparison
        // must use this rather than the laptop's clock: Jakarta and Mecca differ by four hours.
        DateTime PrayNow() { return DateTime.SpecifyKind(DateTime.UtcNow.AddHours(prayOffset), DateTimeKind.Unspecified); }

        // Today's times plus sunrise, cached daily.
        // The key is the stored place text compared by value: a searched place parses into a
        // fresh string on every call, so an identity test would miss and reset the announce
        // counters each tick, which re-fires the adhan while the window is open.
        DateTime[] Prayers()
        {
            var city = PrayCity();
            if (city == null) return null;
            string want = settings.Get("pray.city", "");
            if (prayToday == null || !string.Equals(want, prayFor, StringComparison.Ordinal)) prayOffset = Cities.OffsetOn(city, DateTime.UtcNow);
            // The city's date, not the laptop's: across the dateline they differ
            var today = PrayNow().Date;
            if (prayToday == null || prayDay != today || !string.Equals(want, prayFor, StringComparison.Ordinal))
            {
                double lat = Convert.ToDouble(city[1]), lng = Convert.ToDouble(city[2]);
                // The zone follows the calendar, so summer time needs no separate setting
                double tz = Cities.OffsetOn(city, today);
                var prm = PrayMethods.Pick(Cities.CountryCode(city), lat);
                prayToday = PrayTimes.For(today, lat, lng, tz, prm);
                // Tomorrow can sit on the other side of a clock change
                prayTomorrow = PrayTimes.For(today.AddDays(1), lat, lng, Cities.OffsetOn(city, today.AddDays(1)), prm)[0];
                for (int i = 0; i < 6; i++) prayHm[i] = Fmt.Hm(prayToday[i]);
                prayTomorrowHm = Fmt.Hm(prayTomorrow);
                prayDay = today;
                prayFor = want;
                prayAnnounced = prayWarned = -1;
                prayParams = prm;
                prayOffset = tz;
            }
            return prayToday;
        }

        // What the notch and settings show about the place in use
        PrayParams prayParams;
        double prayOffset;

        // Next prayer; after Isya, tomorrow's Subuh
        int NextPrayer(out DateTime at)
        {
            at = DateTime.MaxValue;
            var times = Prayers();
            if (times == null) return -1;
            // The city's clock, since the times belong to the city
            var now = PrayNow();
            for (int i = 0; i < 6; i++)
                if (i != 1 && times[i] > now) { at = times[i]; return i; }
            at = prayTomorrow;
            return 0;
        }

        // Day stamp built once per day
        string TodayKey()
        {
            if (todayKeyDay != DateTime.Today)
            {
                todayKeyDay = DateTime.Today;
                todayKey = todayKeyDay.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            }
            return todayKey;
        }

        int DrinkCount()
        {
            if (settings.Get("drink.day", "") != TodayKey()) return 0;
            return settings.GetInt("drink.count", 0);
        }

        int DrinkGoal { get { return Math.Max(1, Math.Min(20, settings.GetInt("drink.goal", 8))); } }

        void Drank()
        {
            lastDrinkUtc = DateTime.UtcNow;
            settings.Set("drink.day", TodayKey());
            settings.Set("drink.count", DrinkCount() + 1);
            SetWell(0, lastDrinkUtc, false);
            settings.Save();
            if (toast != null && toast.View == View.Remind) DismissToast();
            if (pet != null) pet.CloseWellness(RemindKind.Drink);
            Kick();
        }

        void Snooze(int minutes)
        {
            snoozeUntilUtc = DateTime.UtcNow.AddMinutes(minutes);
            if (toast != null && toast.View == View.Remind) DismissToast();
        }

        // Each clock tick
        bool RemindTick(DateTime nowUtc)
        {
            bool redraw = false;
            var now = DateTime.Now;
            LoadWellClocks();
            // In use: input within 5 minutes
            double idle = IdleSeconds();
            if (PrayOn)
            {
                var times = Prayers();
                // The adhan fires on the city's clock; the wellness hours below stay on the laptop's
                var pnow = PrayNow();
                int before = Math.Max(0, settings.GetInt("pray.before", 10));
                for (int i = 0; i < 6; i++)
                {
                    if (i == 1) continue;
                    if (before > 0 && prayWarned < i && pnow >= times[i].AddMinutes(-before) && pnow < times[i])
                    {
                        prayWarned = i;
                        redraw |= ShowWorship(i, false, settings.Get("sound.pray.before", ""));
                    }
                    if (prayAnnounced < i && pnow >= times[i] && pnow < times[i].AddMinutes(10))
                    {
                        prayAnnounced = prayWarned = i;
                        redraw |= ShowWorship(i, true, settings.Get("sound.pray", ""));
                    }
                }
            }
            redraw |= IbadahTick(now);
            // Nudges wait: away, fullscreen, or quiet
            if (idle > 120 || fullscreen || Quiet || PrompterOn || nowUtc < snoozeUntilUtc || toast != null ||
                (pet != null && pet.WellnessOpen)) return redraw;
            int hour = now.Hour;
            bool nudged = false;
            if (settings.GetBool("drink", false) && hour >= 7 && hour < 22 && DrinkCount() < DrinkGoal &&
                (nowUtc - lastDrinkUtc).TotalMinutes >= DrinkEvery)
                nudged = Nudge(RemindKind.Drink, ref lastDrinkUtc, 0);
            if (!nudged && settings.GetBool("eyes", false) && (nowUtc - lastEyesUtc).TotalMinutes >= EyesEvery)
                nudged = Nudge(RemindKind.Eyes, ref lastEyesUtc, 1);
            if (!nudged && settings.GetBool("stretch", false) && (nowUtc - lastStretchUtc).TotalMinutes >= StretchEvery)
                nudged = Nudge(RemindKind.Stretch, ref lastStretchUtc, 2);
            return redraw || nudged;
        }

        // Commit the clock and play sound once, whichever surface actually displays the reminder.
        bool Nudge(RemindKind kind, ref DateTime last, int well)
        {
            if (QuietToast(View.Remind, 2)) return false;
            bool spoken = PetOn && pet != null && !expanded && (!MiniOn() || MiniLets(View.Remind, 2)) &&
                pet.TryWellness(kind, RemindDetail(kind));
            if (!spoken)
            {
                var t = ShowToast(View.Remind, WellnessSeconds, 2, true);
                if (t == null) return false;
                remindShown = kind;
                t.Data = kind;
                if (!Anim.Reduced) topGap.Velocity += 80;
            }
            var now = DateTime.UtcNow;
            last = now;
            SetWell(well, now, true);
            PlayRemind(settings.Get("sound." + WellKeys[well], ""));
            if (kind == RemindKind.Eyes) restUntilUtc = now.AddSeconds(WellnessSeconds);
            return true;
        }

        const int WellnessSeconds = 20;

        // Compact views

        const char IcoPray = '';
        static readonly string[] Secs = MakeSecs();
        readonly Roller prayRoller = new Roller(), eyesRoller = new Roller();
        Action actCity, actPraySwitch, actDrinkPlus, actLater, actRemindOk, actRemindDone;
        readonly Action[] wellActs = new Action[3];
        Func<DateTime, double> prayLeftPaint, eyesLeftPaint;
        Action<DateTime> eyesRingPaint;
        static readonly string[] WellKeys = { "drink", "eyes", "stretch" }, WellIds = { "swdrink", "sweyes", "swstretch" };

        static string[] MakeSecs()
        {
            var s = new string[21];
            for (int i = 0; i < s.Length; i++) s[i] = i.ToString(CultureInfo.InvariantCulture);
            return s;
        }

        // Delegates built once, never per frame
        void RemindActs()
        {
            if (actCity != null) return;
            actCity = CityMenu;
            actPraySwitch = delegate
            {
                if (PrayCity() == null) { CityMenu(); return; }
                settings.Toggle("pray", false);
                Kick();
            };
            actDrinkPlus = Drank;
            for (int i = 0; i < 3; i++)
            {
                int k = i;
                wellActs[i] = delegate { ToggleWell(k); };
            }
            actLater = delegate { Snooze(15); };
            actRemindOk = delegate
            {
                if (toast != null && toast.Data is RemindKind && (RemindKind)toast.Data == RemindKind.Drink) Drank();
                else DismissToast();
            };
            actRemindDone = DismissToast;
            prayLeftPaint = PaintPrayLeft;
            eyesLeftPaint = PaintEyesLeft;
            eyesRingPaint = PaintEyesRing;
        }

        void ToggleWell(int i)
        {
            string key = WellKeys[i];
            settings.Toggle(key, false);
            // Start counting from now, not from launch, and remember that across restarts
            if (settings.GetBool(key, false))
            {
                var now = DateTime.UtcNow;
                if (i == 0) lastDrinkUtc = now;
                if (i == 1) lastEyesUtc = now;
                if (i == 2) lastStretchUtc = now;
                SetWell(i, now, true);
            }
            Kick();
        }

        // Leading glyph in a round tile
        void LeadTile(float cy, char glyph)
        {
            p.Module(new RectangleF(11, cy - 15, 30, 30), 15, p.T.Fill);
            p.Icon(glyph, 15f, p.T.Text, 26, cy);
        }

        int prayTitleKey = -1, prayTailKey = -1;
        string prayTitleText = "", prayTailText = "";

        float PrayWidth()
        {
            if (toast == null || p.G == null) return 280;
            return Clamp(p.Measure(PrayTitle(), Ink.Headline) + p.Measure(PrayTail(), Ink.Semi(13f)) + 52 + 44 + DoneW + 12, 300, 460);
        }

        string PrayTitle()
        {
            bool due = toast != null && toast.Flag;
            var custom = toast != null ? toast.Data as IbadahItem : null;
            if (custom != null) return due ? custom.Name : Lang.F("{0} sebentar lagi", custom.Name);
            int i = toast != null && toast.Data is int ? (int)toast.Data : 0;
            int key = i * 4 + (due ? 2 : 0) + (Lang.English ? 1 : 0);
            if (key != prayTitleKey)
            {
                prayTitleKey = key;
                string name = Lang.T(PrayTimes.Names[i]);
                prayTitleText = due ? Lang.F("Waktu {0}", name) : Lang.F("{0} sebentar lagi", name);
            }
            return prayTitleText;
        }

        // Time when due, else minutes left
        string PrayTail()
        {
            var custom = toast != null ? toast.Data as IbadahItem : null;
            if (custom != null)
            {
                if (toast.Flag) return custom.Hm;
                return Fmt.Left(custom.At(DateTime.Now) - DateTime.Now);
            }
            var times = Prayers();
            if (times == null || toast == null || !(toast.Data is int)) return "";
            int i = (int)toast.Data;
            if (toast.Flag) return prayHm[i];
            var left = times[i] - PrayNow();
            int key = i * 100000 + Math.Max(0, (int)left.TotalMinutes) * 2 + (Lang.English ? 1 : 0);
            if (key != prayTailKey) { prayTailKey = key; prayTailText = Fmt.Left(left); }
            return prayTailText;
        }

        void DrawPray(float w, float h, DateTime now)
        {
            if (toast == null || !(toast.Data is int || toast.Data is IbadahItem)) return;
            RemindActs();
            float cy = h / 2;
            LeadTile(cy, IcoPray);
            var done = new RectangleF(w - 14 - DoneW, cy - RemindPillH / 2, DoneW, RemindPillH);
            string tail = PrayTail();
            float tw = tail.Length > 0 ? p.Measure(tail, Ink.Semi(13f)) + 12 : 0;
            p.Text(PrayTitle(), Ink.Headline, p.T.Text, 52, cy, done.X - 12 - 52 - tw, StringAlignment.Near);
            if (tw > 0) p.TextRight(tail, Ink.Semi(13f), p.T.Sub, done.X - 12, cy);
            Pill(done, "prayDone", Lang.T("Selesai"), PillKind.Primary, actRemindDone);
        }

        const float RemindPillH = 30, LaterW = 66, DoneW = 72;

        float RemindWidth()
        {
            if (p.G == null) return 360;
            var k = remindShown;
            float text = Math.Max(p.Measure(RemindTitle(k), Ink.Headline), p.Measure(RemindDetail(k), Ink.Callout));
            float tail = k == RemindKind.Eyes ? 44 : LaterW + DoneW + 8 + 14;
            return Clamp(52 + text + 16 + tail, 300, 440);
        }

        string RemindTitle(RemindKind k)
        {
            if (k == RemindKind.Drink) return Lang.T("Waktunya minum");
            if (k == RemindKind.Eyes) return Lang.T("Istirahatkan mata");
            return Lang.T("Waktunya bergerak");
        }

        int drinkDetailKey = -1;
        string drinkDetail = "";

        string RemindDetail(RemindKind k)
        {
            if (k == RemindKind.Drink)
            {
                int key = DrinkCount() * 1000 + DrinkGoal * 2 + (Lang.English ? 1 : 0);
                if (key != drinkDetailKey) { drinkDetailKey = key; drinkDetail = Lang.F("{0} dari {1} gelas", DrinkCount(), DrinkGoal); }
                return drinkDetail;
            }
            if (k == RemindKind.Eyes) return Lang.T("pandang jauh 20 detik");
            return Lang.T("berdiri dan regangkan badan");
        }

        static char RemindGlyph(RemindKind k) { return k == RemindKind.Drink ? '' : k == RemindKind.Eyes ? '' : ''; }

        float remindCy, remindRight;

        void DrawRemind(float w, float h, DateTime now)
        {
            if (toast == null || !(toast.Data is RemindKind)) return;
            RemindActs();
            var k = (RemindKind)toast.Data;
            float cy = h / 2, tx = 52;
            remindCy = cy;
            if (k == RemindKind.Eyes)
            {
                AddHit(new RectangleF(0, 0, w, h), "remindDone", actRemindDone);
                // Ring and seconds count down alone
                p.Icon(RemindGlyph(k), 13f, p.T.Text, 26, cy);
                LiveRect(new RectangleF(11, cy - 15, 30, 30), 15f, eyesRingPaint);
                remindRight = w - 20;
                LiveRect(new RectangleF(w - 64, cy - 14, 46, 28), eyesLeftPaint);
                p.Text(RemindTitle(k), Ink.Headline, p.T.Text, tx, cy - 9, w - tx - 70, StringAlignment.Near);
                p.Text(RemindDetail(k), Ink.Callout, p.T.Sub, tx, cy + 10, w - tx - 70, StringAlignment.Near);
                return;
            }
            LeadTile(cy, RemindGlyph(k));
            var done = new RectangleF(w - 14 - DoneW, cy - RemindPillH / 2, DoneW, RemindPillH);
            var later = new RectangleF(done.X - 8 - LaterW, done.Y, LaterW, RemindPillH);
            float room = later.X - 12 - tx;
            p.Text(RemindTitle(k), Ink.Headline, p.T.Text, tx, cy - 9, room, StringAlignment.Near);
            p.Text(RemindDetail(k), Ink.Callout, p.T.Sub, tx, cy + 10, room, StringAlignment.Near);
            Pill(later, "remindLater", Lang.T("Nanti"), PillKind.Secondary, actLater);
            Pill(done, "remindOk", Lang.T("Sudah"), PillKind.Primary, actRemindOk);
        }

        void PaintEyesRing(DateTime t)
        {
            float left = (float)Math.Max(0, (restUntilUtc - t).TotalSeconds);
            Ring(26, remindCy, 13.5f, left / 20, p.T.Accent, 2.4f);
        }

        // Whole seconds roll, then wait
        double PaintEyesLeft(DateTime t)
        {
            double left = Math.Max(0, (restUntilUtc - t).TotalSeconds);
            int n = Math.Min(20, (int)Math.Ceiling(left));
            eyesRoller.Set(Secs[n]);
            eyesRoller.Draw(p, Ink.Display(17f), p.T.Text, remindRight, remindCy, StringAlignment.Far, t);
            if (eyesRoller.Moving(t)) return 1 / 60.0;
            if (n == 0) return 0;
            double wait = left - Math.Floor(left);
            return (wait < 0.001 ? 1 : wait) + 0.004;
        }

        // Page

        // The extra 14px carries the region line: the method is chosen for the user, so the card names it
        const float PrayCardH = 116, PrayEmptyH = 56, WellRowH = 40, WellPad = 4;

        float RemindHeight()
        {
            float pray = PrayCity() != null ? PrayCardH : PrayEmptyH;
            return Math.Min(MaxH, PanelTop + 2 + pray + Ink.ModuleGap + 3 * WellRowH + 2 * WellPad + Ink.ModuleInset);
        }

        void DrawRemindPage(float w, float h, DateTime now)
        {
            RemindActs();
            float x0 = Ink.ModuleInset, full = w - 2 * x0, y = PanelTop + 2, keep = p.Alpha, dy;
            bool city = PrayCity() != null;
            float ph = city ? PrayCardH : PrayEmptyH;
            p.Alpha = keep * RowIn(0, now, out dy);
            if (city) DrawPrayCard(new RectangleF(x0, y + dy, full, ph));
            else DrawPrayEmpty(new RectangleF(x0, y + dy, full, ph));
            y += ph + Ink.ModuleGap;
            p.Alpha = keep * RowIn(1, now, out dy);
            var well = new RectangleF(x0, y + dy, full, 3 * WellRowH + 2 * WellPad);
            Module(well, Ink.RadiusModule);
            for (int i = 0; i < 3; i++)
                DrawWellRow(new RectangleF(well.X + 6, well.Y + WellPad + i * WellRowH, well.Width - 12, WellRowH), i, i < 2);
            p.Alpha = keep;
        }

        // No city yet: one row, one action
        void DrawPrayEmpty(RectangleF r)
        {
            Module(r, Ink.RadiusModule);
            float cy = r.Y + r.Height / 2, pad = Ink.ModulePad;
            p.Icon(IcoPray, 16f, p.T.Text, r.X + pad + 8, cy);
            string label = Lang.T("Pilih daerah");
            float bw = p.Measure(label, Ink.Semi(13f)) + 32;
            var pill = new RectangleF(r.Right - pad - bw, cy - 16, bw, 32);
            p.Text(Lang.T("Waktu sholat"), Ink.Headline, p.T.Text, r.X + pad + 28, cy, pill.X - 12 - r.X - pad - 28, StringAlignment.Near);
            Pill(pill, "prayCity", label, PillKind.Primary, actCity);
        }

        static readonly int[] PrayRow = { 0, 2, 3, 4, 5 };
        float prayLeftX, prayLeftY;
        int prayLeftKey = -1;
        string prayLeftText = "";
        DateTime prayNextUtc;

        void DrawPrayCard(RectangleF r)
        {
            Module(r, Ink.RadiusModule);
            var times = Prayers();
            if (times == null) return;
            DateTime nextAt;
            int next = NextPrayer(out nextAt);
            bool today = nextAt.Date == PrayNow().Date;
            float pad = Ink.ModulePad, hy = r.Y + 27, x = r.X + pad;
            // Header right: current city and reminder switch
            var sw = new RectangleF(r.Right - pad - 38, hy - 11, 38, 22);
            string name = (string)PrayCity()[0];
            var cityFont = Ink.Semi(12.5f);
            float cityRight = sw.X - 12, cityWidth = Math.Min(120, p.Measure(name, cityFont) + 2);
            p.Text(name, cityFont, p.T.Sub, cityRight, hy, cityWidth, StringAlignment.Far);
            Switch(sw, "praySwitch", settings.GetBool("pray", false), actPraySwitch);
            // Hero: name, time, minutes left
            string hero = Lang.T(PrayTimes.Names[next]), at = today ? prayHm[next] : prayTomorrowHm;
            float heroRight = cityRight - cityWidth - 12;
            float room = heroRight - x, nw = Math.Min(room, p.Measure(hero, Ink.Title1)), tw = p.Measure(at, Ink.Title1);
            p.Text(hero, Ink.Title1, p.T.Text, x, hy, room, StringAlignment.Near);
            float lx = x + nw + 8;
            // Narrow panel: the row shows the time
            if (lx + tw + 10 + 76 <= heroRight)
            {
                p.Text(at, Ink.Title1, p.T.Sub, lx, hy, tw + 2, StringAlignment.Near);
                lx += tw + 10;
            }
            // The city's wall clock turned into an instant, so the live countdown can subtract UTC
            prayNextUtc = DateTime.SpecifyKind(nextAt, DateTimeKind.Utc).AddHours(-prayOffset);
            prayLeftX = lx;
            prayLeftY = hy + 1.5f;
            LiveRect(new RectangleF(lx - 2, prayLeftY - 10, Math.Max(10, heroRight + 4 - lx), 20), prayLeftPaint);
            // Five times: past dims, next takes accent
            var nowLocal = PrayNow();
            float top = r.Y + 52, cellW = (r.Width - 16) / PrayRow.Length;
            for (int k = 0; k < PrayRow.Length; k++)
            {
                int i = PrayRow[k];
                float cx = r.X + 8 + cellW * (k + 0.5f);
                bool isNext = today && i == next, past = times[i] <= nowLocal;
                if (isNext) p.FillRound(new RectangleF(cx - cellW / 2 + 2, top, cellW - 4, 40), Ink.RadiusControl, Ink.A(p.T.Accent, 0.16f));
                Color ink = isNext ? p.T.Text : past ? p.T.Faint : p.T.Sub;
                p.TextCenter(Lang.T(PrayTimes.Names[i]), Ink.Caption, isNext ? p.T.Accent : ink, cx, top + 12);
                p.TextCenter(prayHm[i], Ink.Semi(13.5f), isNext ? p.T.Text : past ? p.T.Faint : p.T.Text, cx, top + 28);
            }
            // Why these times: the region, the convention it implies, and the zone in force today
            p.Text(PrayRegionLine(), Ink.Small(11f), p.T.Faint, r.X + 8, top + 50, r.Width - 16, StringAlignment.Near);
        }

        int prayRegionKey = -1;
        string prayRegionText = "";

        // Cached on what the line is built from, so a zone or place change rebuilds it
        string PrayRegionLine()
        {
            var c = PrayCity();
            if (c == null || prayParams == null) return "";
            string want = settings.Get("pray.city", "");
            int quarter = (int)Math.Round(prayOffset * 4);
            int key = (want.GetHashCode() * 397) ^ quarter ^ (prayParams.Key != null ? prayParams.Key.GetHashCode() : 0);
            if (key == prayRegionKey && prayRegionText.Length > 0) return prayRegionText;
            prayRegionKey = key;
            string region = Cities.RegionOf(c);
            string line = (region.Length > 0 ? region + " · " : "") + prayParams.Name + " · " + PrayZone.Label(prayOffset);
            if (prayParams.AsrFactor == 2) line += " · " + Lang.T("Ashar Hanafi");
            if (prayParams.HighLat > 0) line += " · " + Lang.T("lintang tinggi");
            prayRegionText = line;
            return line;
        }

        // Minutes left; rolls, then sleeps
        double PaintPrayLeft(DateTime t)
        {
            var left = prayNextUtc - t;
            if (left.Ticks <= 0) return 0;
            int key = (int)left.TotalMinutes * 2 + (Lang.English ? 1 : 0);
            if (key != prayLeftKey) { prayLeftKey = key; prayLeftText = Lang.F("dalam {0}", Fmt.Left(left)); }
            prayRoller.Set(prayLeftText);
            prayRoller.Draw(p, Ink.Callout, p.T.Sub, prayLeftX, prayLeftY, StringAlignment.Near, t);
            if (prayRoller.Moving(t)) return 1 / 60.0;
            return left.TotalSeconds - Math.Floor(left.TotalMinutes) * 60 + 0.02;
        }

        // The city is chosen in Settings; the island just opens that page
        void CityMenu() { OpenPrefs("remind"); }


        void UseCity(string packed)
        {
            if (string.IsNullOrEmpty(packed)) return;
            settings.Set("pray.city", packed);
            settings.Set("pray", true);
            settings.Save();
            prayToday = null;
            Kick();
            // New coordinates: refresh within the minute instead of waiting 30
            weatherAskedUtc = DateTime.MinValue;
            TickWeather();
        }


        // Keyboard opens under the control
        Point MenuAt(string id)
        {
            if (keyboardMode)
                foreach (var h in hits)
                    if (h.Id == id) return PointToScreen(new Point((int)(h.Rect.X * scale), (int)(h.Rect.Bottom * scale)));
            return Control.MousePosition;
        }


        // Slim row: glyph, title, switch
        void DrawWellRow(RectangleF r, int i, bool separator)
        {
            float cy = r.Y + r.Height / 2, tx = r.X + 38;
            var kind = i == 0 ? RemindKind.Drink : i == 1 ? RemindKind.Eyes : RemindKind.Stretch;
            p.Icon(RemindGlyph(kind), 15f, p.T.Text, r.X + 18, cy);
            var sw = new RectangleF(r.Right - 10 - 38, cy - 11, 38, 22);
            float right = sw.X - 14;
            if (i == 0)
            {
                // +1, then glasses as dots
                var plus = new RectangleF(right - 40, cy - 13, 40, 26);
                Pill(plus, "drinkPlus", "+1", PillKind.Secondary, actDrinkPlus);
                right = plus.X - 12;
                right -= DrinkDots(right, cy) + 12;
            }
            Switch(sw, WellIds[i], settings.GetBool(WellKeys[i], false), wellActs[i]);
            string title = i == 0 ? Lang.T("Minum air") : i == 1 ? Lang.T("Istirahat mata 20-20-20") : Lang.T("Peregangan");
            p.Text(title, Ink.Body, p.T.Text, tx, cy, right - tx, StringAlignment.Near);
            if (separator) p.G.DrawLine(p.P(p.T.Line, 1f), tx, r.Bottom - 0.5f, r.Right - 12, r.Bottom - 0.5f);
        }

        // Dots up to 12, else a bar
        float DrinkDots(float right, float cy)
        {
            int goal = DrinkGoal, n = Math.Min(goal, DrinkCount());
            if (goal <= 12)
            {
                const float d = 6, gap = 4;
                float width = goal * d + (goal - 1) * gap, x = right - width;
                for (int k = 0; k < goal; k++)
                    p.Circle(x + k * (d + gap) + d / 2, cy, d / 2, k < n ? p.T.Accent : p.T.Quaternary);
                return width;
            }
            var bar = new RectangleF(right - 96, cy - 2.5f, 96, 5);
            p.FillRound(bar, 2.5f, p.T.Quaternary);
            if (n > 0) p.FillRound(bar, 2.5f, p.Split(bar.X, bar.Width, bar.X + bar.Width * n / goal, p.T.Accent, Color.Transparent));
            return 96;
        }
    }
}
