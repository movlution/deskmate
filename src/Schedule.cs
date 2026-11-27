using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.Threading;
using System.Windows.Forms;

namespace Deskmate
{
    partial class Island
    {
        List<ClassSlot> classes;
        string classesError;
        readonly HashSet<string> warnedClasses = new HashSet<string>();
        readonly Spring scheduleScroll = new Spring(0, 300, 34, 0.2f);
        const float ClassRowH = 34;

        DateTime scheduleDay;
        const float HeroH = 82, ChipsH = 40;

        // Days with classes, soonest first, at most six
        List<DateTime> ScheduleDays()
        {
            var days = new List<DateTime>();
            if (classes != null)
                foreach (var c in classes)
                    if (!days.Contains(c.Start.Date) && days.Count < 6) days.Add(c.Start.Date);
            return days;
        }

        DateTime ChosenDay()
        {
            var days = ScheduleDays();
            if (days.Count == 0) return DateTime.Now.Date;
            if (days.Contains(scheduleDay)) return scheduleDay;
            var next = NextClass();
            return next != null && days.Contains(next.Start.Date) ? next.Start.Date : days[0];
        }

        List<ClassSlot> DayClasses()
        {
            var day = ChosenDay();
            return classes == null ? new List<ClassSlot>() : classes.FindAll(c => c.Start.Date == day);
        }

        ClassSlot NextClass()
        {
            if (classes == null) return null;
            var now = DateTime.Now;
            foreach (var c in classes) if (!c.Cancelled && !c.AllDay && c.End > now) return c;
            return null;
        }

        float ScheduleContentH() { return DayClasses().Count * ClassRowH; }

        float ScheduleHeight()
        {
            var state = ScheduleState();
            // Room for the button
            if (state == DataState.Off || state == DataState.Failed) return 252;
            if (state != DataState.Ready) return 214;
            return Clamp(PanelTop + 8 + HeroH + ChipsH + ScheduleContentH() + 12 + EventFoot(), 150, 372);
        }

        static string DayName(DateTime day)
        {
            var today = DateTime.Now.Date;
            if (day == today) return Lang.T("Hari ini");
            if (day == today.AddDays(1)) return Lang.T("Besok");
            return day.ToString("dddd, d MMM", Fmt.Id);
        }

        static string SlotNote(ClassSlot c)
        {
            var parts = new List<string>();
            if (c.Room.Length > 0) parts.Add(c.Room);
            if (c.Online) parts.Add(c.Url.Length > 0 ? Lang.T("daring · tautan") : Lang.T("daring"));
            if (c.Kind == "praktikum" || c.Kind == "responsi") parts.Add(Lang.T(c.Kind));
            if (c.Personal) parts.Add(Lang.T("pribadi"));
            string from = SourceHub.EventLabel(c.Source);
            if (from.Length > 0) parts.Add(from);
            return string.Join(" · ", parts);
        }

        void DrawSchedulePage(float w, float h, DateTime now)
        {
            var state = ScheduleState();
            if (state == DataState.Off)
            {
                NothingConnected(w, "linkSchedule", false);
                return;
            }
            if (state == DataState.Failed)
            {
                EmptyState(new RectangleF(0, PanelTop, w, h - PanelTop), Ink.IcoCalendar, Lang.T("Jadwal gagal dimuat"), eventNote, Lang.T("Coba lagi"), "retrySchedule", delegate { RefreshAll(2); }, PillKind.Secondary);
                return;
            }
            if (state == DataState.Loading)
            {
                EmptyState(new RectangleF(0, PanelTop, w, h - PanelTop), Ink.IcoCalendar, Lang.T("Memuat jadwal…"), "");
                return;
            }
            if (state == DataState.Empty)
            {
                bool kuliaOnly = OnlyKuliaEvents;
                EmptyState(new RectangleF(0, PanelTop, w, h - PanelTop), Ink.IcoCalendar, kuliaOnly ? Lang.T("Tidak ada kelas") : Lang.T("Jadwal kosong"), eventNote ?? (kuliaOnly ? Lang.T("7 hari ke depan kosong") : Lang.T("3 minggu ke depan kosong")));
                DrawAddGoogleEventPill(w - 16, PanelTop + 17);
                return;
            }
            var nowLocal = DateTime.Now;
            float keep = p.Alpha, dy;
            // Hero: the class that matters next
            var next = NextClass();
            p.Alpha = keep * RowIn(0, now, out dy);
            var hero = new RectangleF(16, PanelTop + 4 + dy, w - 32, HeroH - 8);
            p.FillRound(hero, Ink.RadiusModule, p.T.Fill);
            if (next != null)
            {
                bool live = nowLocal >= next.Start;
                if (live) p.FillRound(new RectangleF(hero.X + 2, hero.Y + 14, 3, hero.Height - 28), 1.5f, p.T.Accent);
                string when = live ? Lang.T("Sedang berlangsung · sampai ") + Fmt.Hm(next.End)
                                   : Lang.T("Berikutnya · ") + (next.Start.Date == nowLocal.Date ? Fmt.Left(next.Start - nowLocal) + Lang.T(" lagi") : DayName(next.Start.Date).ToLower());
                // Two or three lines, centred block
                string note = SlotNote(next);
                float hc = hero.Y + hero.Height / 2, ty = note.Length > 0 ? hc : hc + 10;
                p.Text(when, Ink.Small(11f), live ? p.T.Accent : p.T.Sub, hero.X + 16, ty - 20, hero.Width - 32, StringAlignment.Near);
                p.Text(next.Title, Ink.Semi(15f), p.T.Text, hero.X + 16, ty, hero.Width - 150, StringAlignment.Near);
                p.TextRight(Fmt.Hm(next.Start) + "–" + Fmt.Hm(next.End), Ink.Semi(12.5f), p.T.Text, hero.Right - 16, ty);
                if (note.Length > 0) p.Text(note, Ink.Small(11f), p.T.Sub, hero.X + 16, hc + 20, hero.Width - 32, StringAlignment.Near);
                if (next.Url.Length > 0)
                {
                    var join = new RectangleF(hero.Right - 92, hc + 9, 76, 22);
                    float jsq = Squish("joinNext", join, 0.93f);
                    p.FillRound(join, 11, Hover("joinNext") ? p.T.Text : p.T.Accent);
                    p.TextCenter(next.Source == "kulia" ? Lang.T("Buka kelas") : Lang.T("Buka tautan"), Ink.Semi(10.5f), Hover("joinNext") ? p.T.Bg : p.T.OnAccent, join.X + join.Width / 2, join.Y + 11);
                    Unsquish(join, jsq);
                    string url = next.Url;
                    AddHit(join, "joinNext", delegate { OpenUrl(url); });
                }
            }
            else
            {
                // Only all-day items ahead: show the first
                var day = classes.Find(c => c.AllDay && !c.Cancelled && c.End > nowLocal);
                if (day != null)
                {
                    p.Text(day.Start.Date == nowLocal.Date ? Lang.T("Hari ini") : Lang.T("Berikutnya · ") + DayName(day.Start.Date).ToLower(), Ink.Small(11f), p.T.Sub, hero.X + 16, hero.Y + 18, hero.Width - 32, StringAlignment.Near);
                    p.Text(day.Title, Ink.Semi(15f), p.T.Text, hero.X + 16, hero.Y + 40, hero.Width - 150, StringAlignment.Near);
                    p.TextRight(Lang.T("seharian"), Ink.Semi(12.5f), p.T.Text, hero.Right - 16, hero.Y + 40);
                    string dayNote = SlotNote(day);
                    if (dayNote.Length > 0) p.Text(dayNote, Ink.Small(11f), p.T.Sub, hero.X + 16, hero.Y + 60, hero.Width - 32, StringAlignment.Near);
                }
                else p.TextCenter(OnlyKuliaEvents ? Lang.T("Tidak ada kelas lagi minggu ini") : Lang.T("Tidak ada acara berikutnya"), Ink.Semi(13f), p.T.Sub, hero.X + hero.Width / 2, hero.Y + hero.Height / 2);
            }

            // Day chips
            p.Alpha = keep * RowIn(1, now, out dy);
            var chosen = ChosenDay();
            var days = ScheduleDays();
            float addReserve = GoogleCalendarConnected ? p.Measure(Lang.T("+ Acara"), Ink.Semi(11.5f)) + 28 : 0;
            var labels = ChipLabels(days, nowLocal.Date, w - 32 - addReserve);
            float cx = 16, cy = PanelTop + HeroH + ChipsH / 2 - 2 + dy;
            for (int di = 0; di < days.Count; di++)
            {
                var day = days[di];
                string label = labels[di];
                float cw = p.Measure(label, Ink.Semi(11.5f)) + 24;
                if (cx + cw > w - 16 - addReserve) break;
                var chip = new RectangleF(cx, cy - 13, cw, 26);
                string id = "day" + day.ToString("yyyyMMdd");
                bool on = day == chosen;
                float csq = Squish(id, chip, 0.94f);
                if (on) p.FillRound(chip, 13, p.T.Accent);
                else if (Hover(id)) p.FillRound(chip, 13, p.T.FillHover);
                else p.StrokeRound(chip, 13, p.T.Line, 1f);
                p.TextCenter(label, Ink.Semi(11.5f), on ? p.T.OnAccent : p.T.Sub, chip.X + cw / 2, cy);
                Unsquish(chip, csq);
                var pick = day;
                AddHit(chip, id, delegate { scheduleDay = pick; scheduleScroll.Snap(0); Kick(); });
                cx += cw + 6;
            }
            DrawAddGoogleEventPill(w - 16, cy);
            p.Alpha = keep;

            float foot = EventFoot();
            if (foot > 0) p.Text(eventNote, Ink.Small(11f), p.T.Sub, 26, h - 16, w - 52, StringAlignment.Near);

            // Classes of the chosen day, one line each
            var area = new RectangleF(10, PanelTop + HeroH + ChipsH, w - 20, h - PanelTop - HeroH - ChipsH - 8 - foot);
            var st = g.Save();
            g.IntersectClip(area);
            float y = area.Y - scheduleScroll.Value;
            int index = 0;
            foreach (var c in DayClasses())
            {
                float rdy, ra = RowIn(index + 2, now, out rdy);
                float rc = y + ClassRowH / 2 + rdy;
                index++;
                if (y + ClassRowH > area.Top && y < area.Bottom)
                {
                    p.Alpha = keep * ra;
                    bool live = !c.Cancelled && !c.AllDay && nowLocal >= c.Start && nowLocal < c.End;
                    bool isGoogle = c.Source == "google" && !string.IsNullOrEmpty(c.Id);
                    var row = new RectangleF(14, y + 2 + rdy, w - 28, ClassRowH - 4);
                    if (live)
                    {
                        p.FillRound(row, 12, Ink.A(p.T.Accent, 0.08f));
                        p.FillRound(new RectangleF(row.X + 2, row.Y + 6, 3, row.Height - 12), 1.5f, p.T.Accent);
                    }
                    if (c.Url.Length > 0 || isGoogle)
                    {
                        string rowId = "class" + c.Start.Ticks + "_" + index, url = c.Url;
                        var slot = c;
                        if (url.Length > 0 && Hover(rowId)) p.FillRound(row, 12, p.T.Fill);
                        if (row.Bottom <= area.Bottom && row.Top >= area.Top)
                        {
                            Action act = url.Length > 0 ? (Action)delegate { OpenUrl(url); } : noAct;
                            Action menuAct = isGoogle ? (Action)delegate { ShowGoogleEventMenu(slot); } : null;
                            AddHit(row, rowId, act, null, menuAct);
                        }
                    }
                    var ink = c.Cancelled || nowLocal >= c.End ? p.T.Faint : p.T.Text;
                    if (c.AllDay) p.Text(Lang.T("seharian"), Ink.Small(11f), p.T.Sub, 26, rc, 52, StringAlignment.Near);
                    else p.Text(Fmt.Hm(c.Start), Ink.Semi(12.5f), live ? p.T.Accent : ink, 26, rc, 52, StringAlignment.Near);
                    string tag = c.Cancelled ? Lang.T("batal") : c.Exam ? Lang.T("ujian") : live ? Lang.T("sekarang") : "";
                    string room = SlotNote(c);
                    float roomW = room.Length > 0 ? Math.Min(150, p.Measure(room, Ink.Small(11f))) : 0;
                    float tagW = tag.Length > 0 ? p.Measure(tag, Ink.Semi(10.5f)) + 16 : 0;
                    float right = w - 26;
                    if (tag.Length > 0)
                    {
                        var pill = new RectangleF(right - tagW, rc - 10, tagW, 20);
                        if (c.Cancelled) p.StrokeRound(pill, 10, p.T.Faint, 1f); else p.FillRound(pill, 10, p.T.Accent);
                        p.TextCenter(tag, Ink.Semi(10.5f), c.Cancelled ? p.T.Faint : p.T.OnAccent, pill.X + tagW / 2, rc);
                        right -= tagW + 8;
                    }
                    if (roomW > 0) p.Text(room, Ink.Small(11f), p.T.Faint, right, rc, roomW, StringAlignment.Far);
                    float tx = 84, tw = right - roomW - 12 - tx;
                    p.Text(c.Title, Ink.Semi(12.5f), ink, tx, rc, tw, StringAlignment.Near);
                    if (c.Cancelled) g.DrawLine(p.P(p.T.Faint, 1f), tx, rc, tx + Math.Min(tw, p.Measure(c.Title, Ink.Semi(12.5f))), rc);
                }
                y += ClassRowH;
            }
            p.Alpha = keep;
            g.Restore(st);
        }

        // Class running now, shown on the island
        ClassSlot ClassNow()
        {
            if (classes == null || !settings.GetBool("class.island", true)) return null;
            var now = DateTime.Now;
            foreach (var c in classes) if (!c.Cancelled && !c.AllDay && now >= c.Start && now < c.End) return c;
            return null;
        }

        float ClassWidth()
        {
            var c = ClassNow();
            if (c == null || p.G == null) return 240;
            return Clamp(p.Measure(c.Title, Ink.Semi(12.5f)) + p.Measure(Lang.F("sampai {0}", Fmt.Hm(c.End)), Ink.Semi(12f)) + 84, 220, 440);
        }

        readonly Roller classRoller = new Roller();

        void DrawClass(float w, float h, DateTime now)
        {
            var c = ClassNow();
            if (c == null) return;
            float cy = h / 2;
            // Ring empties as the class runs
            var rect = new RectangleF(12.5f, cy - 7.5f, 15, 15);
            g.DrawEllipse(p.P(p.T.Line, 2f), rect);
            float left = (float)Ease.Clamp01((float)((c.End - DateTime.Now).TotalSeconds / Math.Max(1, (c.End - c.Start).TotalSeconds)));
            if (left > 0.003f) g.DrawArc(p.P(p.T.Accent, 2.2f), rect, -90, -360 * left);
            classRoller.Set(Lang.F("sampai {0}", Fmt.Hm(c.End)));
            var f = Ink.Semi(12f);
            float rw = classRoller.Width(p, f);
            p.Text(c.Title, Ink.Semi(12.5f), p.T.Text, 38, cy, w - 38 - rw - 30, StringAlignment.Near);
            classRoller.Draw(p, f, p.T.Sub, w - 18, cy, StringAlignment.Far, now);
            // The pill opens Jadwal, like every other compact view opens its own panel. An event can run for
            // hours, so letting the whole pill open the event link meant the notch swallowed every click for
            // that entire span and could never be used to open the panel. The link stays reachable from the
            // Jadwal row and from this right-click menu.
            string url = c.Url;
            var slot = c;
            bool google = c.Source == "google" && !string.IsNullOrEmpty(c.Id);
            Action menu = google ? (Action)delegate { ShowGoogleEventMenu(slot); }
                        : url.Length > 0 ? (Action)delegate { OpenUrl(url); }
                        : null;
            AddHit(new RectangleF(0, 0, w, h), "classNow", delegate { ShowSchedule(); }, null, menu);
        }

        // Shorter labels until every day fits
        string[] ChipLabels(List<DateTime> days, DateTime today, float room)
        {
            var formats = new Func<DateTime, string>[]
            {
                d => d == today ? Lang.T("Hari ini") : d == today.AddDays(1) ? Lang.T("Besok") : d.ToString("ddd d", Fmt.Id),
                d => d.ToString("ddd d", Fmt.Id),
                d => d.ToString("ddd", Fmt.Id),
            };
            string[] labels = null;
            foreach (var format in formats)
            {
                labels = days.ConvertAll(d => format(d)).ToArray();
                float total = -6;
                foreach (var l in labels) total += p.Measure(l, Ink.Semi(11.5f)) + 30;
                if (total <= room) break;
            }
            return labels;
        }

        // Once per class, 15 minutes before it starts
        void CheckClasses(DateTime nowUtc)
        {
            // Held while a script runs; fires after if still due
            if (classes == null || PrompterOn) return;
            var now = nowUtc.ToLocalTime();
            foreach (var c in classes)
            {
                double mins = (c.Start - now).TotalMinutes;
                string key = c.Title + "|" + c.Start.ToString("o");
                if (c.Cancelled || c.AllDay || mins <= 0 || mins > 15 || warnedClasses.Contains(key)) continue;
                warnedClasses.Add(key);
                var t = ShowToast(View.Info, 8, 3, false, true);
                if (t == null) break;
                t.Glyph = Ink.IcoCalendar;
                t.Title = c.Title + " · " + Fmt.Hm(c.Start);
                string note = SlotNote(c);
                t.Detail = (c.Exam ? Lang.T("ujian · ") : "") + (note.Length > 0 ? note : Math.Ceiling(mins) + Lang.T(" menit lagi"));
                // Click joins, like the Jadwal button
                string url = c.Url;
                if (url.Length > 0) t.Data = (Action)delegate { OpenUrl(url); };
                break;
            }
        }

        GoogleEventDialog activeEventDialog;

        void DrawAddGoogleEventPill(float xRight, float cy)
        {
            if (!GoogleCalendarConnected) return;
            string addLbl = Lang.T("+ Acara");
            float addW = p.Measure(addLbl, Ink.Semi(11.5f)) + 20;
            var addRect = new RectangleF(xRight - addW, cy - 13, addW, 26);
            p.FillRound(addRect, 13, Hover("schedAddEvent") ? p.T.Text : p.T.Accent);
            p.TextCenter(addLbl, Ink.Semi(11.5f), Hover("schedAddEvent") ? p.T.Bg : p.T.OnAccent, addRect.X + addW / 2, cy);
            AddHit(addRect, "schedAddEvent", delegate { BeginInvoke(new Action(OpenNewGoogleEvent)); });
        }

        internal void DeleteGoogleEventAsync(string eventId, string title)
        {
            if (string.IsNullOrEmpty(eventId)) return;
            ThreadPool.QueueUserWorkItem(delegate
            {
                string err;
                bool ok = DeleteGoogleCalendarEvent(eventId, out err);
                Post(delegate
                {
                    if (!ok && err != null)
                    {
                        var t = ShowToast(View.Info, 5, 3, false, true);
                        if (t != null)
                        {
                            t.Glyph = Ink.IcoCalendar;
                            t.Title = Lang.T("Hapus acara");
                            t.Detail = err;
                        }
                    }
                });
            });
        }

        void ShowGoogleEventMenu(ClassSlot c)
        {
            if (c == null || string.IsNullOrEmpty(c.Id)) return;
            var m = new ContextMenuStrip();
            m.Items.Add(new ToolStripMenuItem(c.Title) { Enabled = false });
            m.Items.Add(new ToolStripSeparator());
            if (c.Url.Length > 0) m.Items.Add(Lang.T("Buka tautan"), null, delegate { OpenUrl(c.Url); });
            var editSlot = c;
            m.Items.Add(Lang.T("Edit acara…"), null, delegate
            {
                BeginInvoke(new Action(delegate { OpenEditGoogleEvent(editSlot); }));
            });
            string eventId = c.Id;
            string evTitle = c.Title ?? "";
            m.Items.Add(Lang.T("Hapus acara"), null, delegate
            {
                DeleteGoogleEventAsync(eventId, evTitle);
            });
            ShowIslandMenu(m);
        }

        void OpenNewGoogleEvent()
        {
            DateTime initial = ChosenDay();
            if (initial < DateTime.Today) initial = DateTime.Today;
            OpenNewGoogleEvent(initial);
        }

        public void OpenNewGoogleEvent(DateTime initialDay)
        {
            if (!GoogleCalendarConnected) return;
            DateTime day = initialDay.Date;
            if (activeEventDialog != null && !activeEventDialog.IsDisposed)
            {
                if (!activeEventDialog.IsEdit && activeEventDialog.InitialDay == day)
                {
                    FocusEventDialog(activeEventDialog);
                    return;
                }
                CloseActiveEventDialog();
            }
            ShowActiveEventDialog(new GoogleEventDialog(this, day));
        }

        public void OpenEditGoogleEvent(ClassSlot existing)
        {
            if (!GoogleCalendarConnected || existing == null || string.IsNullOrEmpty(existing.Id)) return;
            if (activeEventDialog != null && !activeEventDialog.IsDisposed)
            {
                if (activeEventDialog.IsEdit && activeEventDialog.EventId == existing.Id)
                {
                    FocusEventDialog(activeEventDialog);
                    return;
                }
                CloseActiveEventDialog();
            }
            ShowActiveEventDialog(new GoogleEventDialog(this, existing));
        }

        void ShowActiveEventDialog(GoogleEventDialog dlg)
        {
            activeEventDialog = dlg;
            dlg.FormClosed += delegate
            {
                if (activeEventDialog == dlg) activeEventDialog = null;
            };
            dlg.Show();
            FocusEventDialog(dlg);
        }

        static void FocusEventDialog(GoogleEventDialog dlg)
        {
            if (dlg == null || dlg.IsDisposed) return;
            if (dlg.WindowState == FormWindowState.Minimized) dlg.WindowState = FormWindowState.Normal;
            dlg.Activate();
            dlg.BringToFront();
            Native.SetForegroundWindow(dlg.Handle);
        }

        void CloseActiveEventDialog()
        {
            var prev = activeEventDialog;
            activeEventDialog = null;
            if (prev != null && !prev.IsDisposed)
            {
                prev.Close();
                if (!prev.IsDisposed) prev.Dispose();
            }
        }

    }
}
