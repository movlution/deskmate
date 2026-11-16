using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.Threading;
using System.Windows.Forms;

namespace Deskmate
{
    // Class schedule from Kulia: tab and reminders
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
            // Hero: the class that matters next
            var next = NextClass();
            var hero = new RectangleF(16, PanelTop + 4, w - 32, HeroH - 8);
            p.FillRound(hero, Ink.RadiusModule, p.T.Fill);
            if (next != null)
            {
                bool live = nowLocal >= next.Start;
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
                    p.FillRound(join, 11, Hover("joinNext") ? p.T.Text : p.T.Accent);
                    p.TextCenter(next.Source == "kulia" ? Lang.T("Buka kelas") : Lang.T("Buka tautan"), Ink.Semi(10.5f), Hover("joinNext") ? p.T.Bg : p.T.OnAccent, join.X + join.Width / 2, join.Y + 11);
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
            var chosen = ChosenDay();
            var days = ScheduleDays();
            float addReserve = GoogleCalendarConnected ? p.Measure(Lang.T("+ Acara"), Ink.Semi(11.5f)) + 28 : 0;
            var labels = ChipLabels(days, nowLocal.Date, w - 32 - addReserve);
            float cx = 16, cy = PanelTop + HeroH + ChipsH / 2 - 2;
            for (int di = 0; di < days.Count; di++)
            {
                var day = days[di];
                string label = labels[di];
                float cw = p.Measure(label, Ink.Semi(11.5f)) + 24;
                if (cx + cw > w - 16 - addReserve) break;
                var chip = new RectangleF(cx, cy - 13, cw, 26);
                string id = "day" + day.ToString("yyyyMMdd");
                bool on = day == chosen;
                if (on) p.FillRound(chip, 13, p.T.Accent);
                else if (Hover(id)) p.FillRound(chip, 13, p.T.FillHover);
                else p.StrokeRound(chip, 13, p.T.Line, 1f);
                p.TextCenter(label, Ink.Semi(11.5f), on ? p.T.OnAccent : p.T.Sub, chip.X + cw / 2, cy);
                var pick = day;
                AddHit(chip, id, delegate { scheduleDay = pick; scheduleScroll.Snap(0); Kick(); });
                cx += cw + 6;
            }
            DrawAddGoogleEventPill(w - 16, cy);

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
                float rc = y + ClassRowH / 2;
                index++;
                if (y + ClassRowH > area.Top && y < area.Bottom)
                {
                    bool isGoogle = c.Source == "google" && !string.IsNullOrEmpty(c.Id);
                    if (c.Url.Length > 0 || isGoogle)
                    {
                        var row = new RectangleF(14, y + 2, w - 28, ClassRowH - 4);
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
                    bool live = !c.Cancelled && !c.AllDay && nowLocal >= c.Start && nowLocal < c.End;
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

        sealed class GoogleEventDialog : KitWindow
        {
            sealed class RoundedField : Panel
            {
                public readonly KitText Box = new KitText();
                readonly Label cue = new Label { AutoSize = false };
                bool focused, invalid;
                float uiScale = 1f;

                public bool Invalid
                {
                    get { return invalid; }
                    set { if (invalid != value) { invalid = value; Invalidate(); } }
                }

                public RoundedField()
                {
                    SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
                    Box.BorderStyle = BorderStyle.None;
                    Box.Multiline = false;
                    Box.MaxLength = 500;
                    cue.Cursor = Cursors.IBeam;
                    cue.Click += delegate { Box.Focus(); };
                    Click += delegate { Box.Focus(); };
                    Box.GotFocus += delegate { focused = true; UpdateCue(); Invalidate(); };
                    Box.LostFocus += delegate { focused = false; UpdateCue(); Invalidate(); };
                    Box.TextChanged += delegate { UpdateCue(); };
                    Controls.Add(Box);
                    Controls.Add(cue);
                    cue.BringToFront();
                }

                public void UpdateCue()
                {
                    cue.Visible = Box.Text.Length == 0 && !focused;
                }

                public void Place(float s, Font font, string placeholderText)
                {
                    uiScale = s;
                    BackColor = KitInk.Window;
                    Box.Font = font;
                    Box.BackColor = KitInk.Group;
                    Box.ForeColor = KitInk.Text;
                    cue.Font = font;
                    cue.BackColor = KitInk.Group;
                    cue.ForeColor = KitInk.Faint;
                    cue.Text = placeholderText;
                    int pad = (int)Math.Round(12 * s);
                    int th = Math.Max(16, Box.PreferredHeight);
                    int ty = Math.Max(4, (Height - th) / 2);
                    int tw = Math.Max(20, Width - 2 * pad);
                    Box.SetBounds(pad, ty, tw, th);
                    cue.SetBounds(pad, ty, tw, th);
                    UpdateCue();
                    Invalidate();
                }

                protected override void OnPaintBackground(PaintEventArgs e)
                {
                    var g = e.Graphics;
                    KitInk.Smooth(g);
                    g.Clear(KitInk.Window);
                    var r = new RectangleF(0.75f, 0.75f, Width - 1.5f, Height - 1.5f);
                    float rad = Math.Min(10f * uiScale, r.Height / 2f);
                    using (var path = Ink.Round(r, rad))
                    {
                        using (var b = new SolidBrush(KitInk.Group)) g.FillPath(b, path);
                        Color border = invalid ? Ink.Base.Negative : focused ? KitInk.Accent : KitInk.Line;
                        using (var pen = new Pen(border, focused ? 1.6f : 1f)) g.DrawPath(pen, path);
                    }
                }
            }

            sealed class EventPillButton : Button
            {
                public bool Active;
                public bool Negative;
                public bool Subtle;
                public float Radius = -1f;
                bool isHovered, isPressed;

                public EventPillButton()
                {
                    SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.SupportsTransparentBackColor, true);
                    FlatStyle = FlatStyle.Flat;
                    FlatAppearance.BorderSize = 0;
                    BackColor = Color.Transparent;
                    UseVisualStyleBackColor = false;
                    Cursor = Cursors.Hand;
                }

                protected override void OnMouseEnter(EventArgs e) { base.OnMouseEnter(e); isHovered = true; Invalidate(); }
                protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); isHovered = false; isPressed = false; Invalidate(); }
                protected override void OnMouseDown(MouseEventArgs e)
                {
                    KitInk.ByMouse = true;
                    if (e.Button == MouseButtons.Left) { isPressed = true; Invalidate(); }
                    base.OnMouseDown(e);
                }
                protected override void OnMouseUp(MouseEventArgs e)
                {
                    base.OnMouseUp(e);
                    if (isPressed) { isPressed = false; Invalidate(); }
                }
                protected override void OnKeyUp(KeyEventArgs e) { if (KitInk.ByMouse) { KitInk.ByMouse = false; Invalidate(); } base.OnKeyUp(e); }
                protected override void OnGotFocus(EventArgs e) { base.OnGotFocus(e); Invalidate(); }
                protected override void OnLostFocus(EventArgs e) { base.OnLostFocus(e); Invalidate(); }

                protected override void OnPaint(PaintEventArgs e)
                {
                    var g = e.Graphics;
                    KitInk.Smooth(g);
                    g.Clear(KitInk.Under(this));
                    var kw = FindForm() as KitWindow;
                    float s = kw != null ? kw.Ui : 1f;
                    if (s <= 0) s = 1f;
                    bool ringOn = Focused && ShowFocusCues && !KitInk.ByMouse;
                    var r = ringOn ? new RectangleF(3f * s, 3f * s, Width - 6f * s, Height - 6f * s)
                                   : new RectangleF(0.75f, 0.75f, Width - 1.5f, Height - 1.5f);
                    if (isPressed && Enabled) r.Offset(0, 0.75f * s);

                    Color bg;
                    Color border = Color.Empty;
                    Color fg;
                    if (!Enabled)
                    {
                        bg = KitInk.Group;
                        border = KitInk.Line;
                        fg = KitInk.Faint;
                    }
                    else if (Negative)
                    {
                        bg = isPressed ? Ink.Mix(Ink.Base.Negative, KitInk.Window, 0.75f)
                                       : isHovered ? Ink.Mix(Ink.Base.Negative, KitInk.Window, 0.84f)
                                       : Ink.Mix(Ink.Base.Negative, KitInk.Window, 0.92f);
                        border = Color.FromArgb(isHovered ? 180 : 100, Ink.Base.Negative);
                        fg = Ink.Base.Negative;
                    }
                    else if (Active)
                    {
                        bg = isPressed ? Ink.Mix(KitInk.Accent, KitInk.OnAccent, 0.22f)
                                       : isHovered ? Ink.Mix(KitInk.Accent, KitInk.OnAccent, 0.1f)
                                       : KitInk.Accent;
                        fg = KitInk.OnAccent;
                    }
                    else
                    {
                        bg = isPressed ? KitInk.Hover : isHovered ? KitInk.Hover : KitInk.Group;
                        border = KitInk.Line;
                        fg = isHovered ? KitInk.Text : (Subtle ? KitInk.Sub : KitInk.Text);
                    }

                    float rad = Radius > 0 ? Math.Min(Radius * s, r.Height / 2f) : r.Height / 2f;
                    using (var path = Ink.Round(r, rad))
                    {
                        using (var b = new SolidBrush(bg)) g.FillPath(b, path);
                        if (border != Color.Empty)
                            using (var pen = new Pen(border, 1f)) g.DrawPath(pen, path);
                    }
                    KitInk.Draw(g, Text, Font, fg, r, ContentAlignment.MiddleCenter);
                    if (ringOn)
                    {
                        var ring = new RectangleF(1f, 1f, Width - 2f, Height - 2f);
                        float rrad = Radius > 0 ? Math.Min(Radius * s + 2f, ring.Height / 2f) : ring.Height / 2f;
                        using (var path = Ink.Round(ring, rrad))
                        using (var pen = new Pen(KitInk.Accent, 1.6f)) g.DrawPath(pen, path);
                    }
                }
            }

            sealed class CalendarPopupControl : Control
            {
                DateTime shownMonth;
                readonly DateTime selected;
                readonly float s;
                readonly Action<DateTime> onPick;
                readonly Font fTitle, fDay, fNum;
                int hotCell = -1; // 0..41 days, -2 prev, -3 next, -4 today

                const float Pad = 12f, CellW = 32f, CellH = 28f;

                public CalendarPopupControl(DateTime initial, float uiScale, Action<DateTime> onPick)
                {
                    s = uiScale > 0 ? uiScale : 1f;
                    selected = initial.Date;
                    shownMonth = new DateTime(selected.Year, selected.Month, 1);
                    this.onPick = onPick;
                    SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
                    fTitle = new Font(Ink.MenuFamily, 13f * s, FontStyle.Bold, GraphicsUnit.Pixel);
                    fDay = new Font(Ink.MenuFamily, 11f * s, GraphicsUnit.Pixel);
                    fNum = new Font(Ink.MenuFamily, 12f * s, GraphicsUnit.Pixel);
                    Size = new Size((int)Math.Ceiling((Pad * 2 + 7 * CellW) * s), (int)Math.Ceiling(266f * s));
                }

                RectangleF PrevRect() { return new RectangleF(Pad * s, 8 * s, 28 * s, 28 * s); }
                RectangleF NextRect() { return new RectangleF(Width - Pad * s - 28 * s, 8 * s, 28 * s, 28 * s); }
                RectangleF TodayRect() { return new RectangleF((Width - 88 * s) / 2f, 234 * s, 88 * s, 24 * s); }

                DateTime GridStart()
                {
                    var dayOne = CultureInfo.CurrentCulture.DateTimeFormat.FirstDayOfWeek;
                    int lead = ((int)shownMonth.DayOfWeek - (int)dayOne + 7) % 7;
                    return shownMonth.AddDays(-lead);
                }

                RectangleF DayRect(int idx)
                {
                    int r = idx / 7, c = idx % 7;
                    return new RectangleF((Pad + c * CellW) * s, (62f + r * CellH) * s, CellW * s, CellH * s);
                }

                int HitCell(Point pt)
                {
                    if (PrevRect().Contains(pt)) return -2;
                    if (NextRect().Contains(pt)) return -3;
                    if (TodayRect().Contains(pt)) return -4;
                    for (int i = 0; i < 42; i++)
                        if (DayRect(i).Contains(pt)) return i;
                    return -1;
                }

                protected override void OnMouseMove(MouseEventArgs e)
                {
                    base.OnMouseMove(e);
                    int h = HitCell(e.Location);
                    if (h != hotCell) { hotCell = h; Invalidate(); }
                }

                protected override void OnMouseLeave(EventArgs e)
                {
                    base.OnMouseLeave(e);
                    if (hotCell != -1) { hotCell = -1; Invalidate(); }
                }

                protected override void OnMouseWheel(MouseEventArgs e)
                {
                    base.OnMouseWheel(e);
                    shownMonth = shownMonth.AddMonths(e.Delta > 0 ? -1 : 1);
                    Invalidate();
                }

                protected override void OnMouseClick(MouseEventArgs e)
                {
                    base.OnMouseClick(e);
                    if (e.Button != MouseButtons.Left) return;
                    int h = HitCell(e.Location);
                    if (h == -2) { shownMonth = shownMonth.AddMonths(-1); Invalidate(); return; }
                    if (h == -3) { shownMonth = shownMonth.AddMonths(1); Invalidate(); return; }
                    if (h == -4) { onPick(DateTime.Today); return; }
                    if (h >= 0 && h < 42) { onPick(GridStart().AddDays(h)); }
                }

                protected override void OnPaint(PaintEventArgs e)
                {
                    var g = e.Graphics;
                    KitInk.Smooth(g);
                    g.Clear(KitInk.Window);
                    var ci = Fmt.Id;
                    var prev = PrevRect();
                    var next = NextRect();
                    if (hotCell == -2)
                        using (var pth = Ink.Round(prev, 7 * s))
                        using (var b = new SolidBrush(KitInk.Hover)) g.FillPath(b, pth);
                    if (hotCell == -3)
                        using (var pth = Ink.Round(next, 7 * s))
                        using (var b = new SolidBrush(KitInk.Hover)) g.FillPath(b, pth);
                    KitInk.Draw(g, "‹", fTitle, KitInk.Text, prev, ContentAlignment.MiddleCenter);
                    KitInk.Draw(g, "›", fTitle, KitInk.Text, next, ContentAlignment.MiddleCenter);
                    var titleRect = new RectangleF(prev.Right + 4 * s, 8 * s, next.Left - prev.Right - 8 * s, 28 * s);
                    KitInk.Draw(g, shownMonth.ToString("MMMM yyyy", ci), fTitle, KitInk.Text, titleRect, ContentAlignment.MiddleCenter);

                    var dayOne = CultureInfo.CurrentCulture.DateTimeFormat.FirstDayOfWeek;
                    for (int c = 0; c < 7; c++)
                    {
                        var dow = (DayOfWeek)(((int)dayOne + c) % 7);
                        var hr = new RectangleF((Pad + c * CellW) * s, 40 * s, CellW * s, 20 * s);
                        KitInk.Draw(g, ci.DateTimeFormat.GetShortestDayName(dow), fDay, KitInk.Sub, hr, ContentAlignment.MiddleCenter);
                    }

                    var today = DateTime.Today;
                    var day = GridStart();
                    float circleD = 26 * s;
                    for (int i = 0; i < 42; i++, day = day.AddDays(1))
                    {
                        var cell = DayRect(i);
                        var pill = new RectangleF(cell.X + (cell.Width - circleD) / 2f, cell.Y + (cell.Height - circleD) / 2f, circleD, circleD);
                        bool isSel = day == selected;
                        bool isToday = day == today;
                        bool inMonth = day.Month == shownMonth.Month;
                        if (isSel)
                        {
                            using (var b = new SolidBrush(KitInk.Accent)) g.FillEllipse(b, pill);
                        }
                        else if (hotCell == i)
                        {
                            using (var b = new SolidBrush(KitInk.Hover)) g.FillEllipse(b, pill);
                        }
                        if (isToday && !isSel)
                        {
                            using (var pen = new Pen(KitInk.Accent, 1.4f)) g.DrawEllipse(pen, pill);
                        }
                        Color fg = isSel ? KitInk.OnAccent : isToday ? KitInk.Accent : inMonth ? KitInk.Text : KitInk.Faint;
                        KitInk.Draw(g, day.Day.ToString(CultureInfo.InvariantCulture), fNum, fg, pill, ContentAlignment.MiddleCenter);
                    }

                    var tr = TodayRect();
                    using (var pth = Ink.Round(tr, tr.Height / 2f))
                    {
                        using (var b = new SolidBrush(hotCell == -4 ? KitInk.Hover : KitInk.Group)) g.FillPath(b, pth);
                        using (var pen = new Pen(KitInk.Line, 1f)) g.DrawPath(pen, pth);
                    }
                    KitInk.Draw(g, Lang.T("Hari ini"), fDay, KitInk.Accent, tr, ContentAlignment.MiddleCenter);

                    using (var pen = new Pen(KitInk.Line, 1f))
                        g.DrawRectangle(pen, 0, 0, Width - 1, Height - 1);
                }

                protected override void Dispose(bool disposing)
                {
                    if (disposing)
                    {
                        fTitle.Dispose();
                        fDay.Dispose();
                        fNum.Dispose();
                    }
                    base.Dispose(disposing);
                }
            }

            sealed class TimePopupControl : Control
            {
                readonly int selectedMinutes;
                readonly int minHighlightAfter;
                readonly string headerTitle;
                readonly float s;
                readonly Action<int> onPick;
                readonly Font fHead, fCell;
                bool fineQuarter;
                int hotIndex = -1; // 0..47 slots, -2 quarter toggle, -3 23:59

                const float Pad = 10f, CellW = 58f, CellH = 23f, Gap = 3f;

                public TimePopupControl(int currentMinutes, int minHighlightAfter, string headerTitle, float uiScale, Action<int> onPick)
                {
                    s = uiScale > 0 ? uiScale : 1f;
                    selectedMinutes = currentMinutes;
                    fineQuarter = (currentMinutes % 30) == 15;
                    this.minHighlightAfter = minHighlightAfter;
                    this.headerTitle = headerTitle;
                    this.onPick = onPick;
                    SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
                    fHead = new Font(Ink.MenuFamily, 12f * s, FontStyle.Bold, GraphicsUnit.Pixel);
                    fCell = new Font(Ink.MenuFamily, 11.5f * s, GraphicsUnit.Pixel);
                    float w = (Pad * 2 + 4 * CellW + 3 * Gap) * s;
                    float gridH = (12 * CellH + 11 * Gap) * s;
                    float footH = minHighlightAfter >= 0 ? 32f * s : 8f * s;
                    Size = new Size((int)Math.Ceiling(w), (int)Math.Ceiling(38f * s + gridH + footH));
                }

                RectangleF QuarterRect() { return new RectangleF(Width - Pad * s - 64 * s, 8 * s, 64 * s, 22 * s); }
                RectangleF EndDayRect() { return new RectangleF(Pad * s, 38 * s + (12 * CellH + 11 * Gap) * s + 5 * s, Width - 2 * Pad * s, 22 * s); }

                RectangleF CellRect(int idx)
                {
                    int r = idx / 4, c = idx % 4;
                    return new RectangleF((Pad + c * (CellW + Gap)) * s, (38f + r * (CellH + Gap)) * s, CellW * s, CellH * s);
                }

                int SlotMinutes(int idx) { return idx * 30 + (fineQuarter ? 15 : 0); }

                int HitItem(Point pt)
                {
                    if (QuarterRect().Contains(pt)) return -2;
                    if (minHighlightAfter >= 0 && EndDayRect().Contains(pt)) return -3;
                    for (int i = 0; i < 48; i++)
                        if (CellRect(i).Contains(pt)) return i;
                    return -1;
                }

                protected override void OnMouseMove(MouseEventArgs e)
                {
                    base.OnMouseMove(e);
                    int h = HitItem(e.Location);
                    if (h != hotIndex) { hotIndex = h; Invalidate(); }
                }

                protected override void OnMouseLeave(EventArgs e)
                {
                    base.OnMouseLeave(e);
                    if (hotIndex != -1) { hotIndex = -1; Invalidate(); }
                }

                protected override void OnMouseClick(MouseEventArgs e)
                {
                    base.OnMouseClick(e);
                    if (e.Button != MouseButtons.Left) return;
                    int h = HitItem(e.Location);
                    if (h == -2) { fineQuarter = !fineQuarter; Invalidate(); return; }
                    if (h == -3) { onPick(23 * 60 + 59); return; }
                    if (h >= 0 && h < 48) { onPick(SlotMinutes(h)); }
                }

                protected override void OnPaint(PaintEventArgs e)
                {
                    var g = e.Graphics;
                    KitInk.Smooth(g);
                    g.Clear(KitInk.Window);
                    var qr = QuarterRect();
                    KitInk.Draw(g, headerTitle, fHead, KitInk.Text, new RectangleF(Pad * s, 8 * s, qr.Left - Pad * s - 4 * s, 22 * s), ContentAlignment.MiddleLeft);

                    using (var qp = Ink.Round(qr, qr.Height / 2f))
                    {
                        Color qbg = fineQuarter ? KitInk.Accent : (hotIndex == -2 ? KitInk.Hover : KitInk.Group);
                        using (var b = new SolidBrush(qbg)) g.FillPath(b, qp);
                        if (!fineQuarter) using (var pen = new Pen(KitInk.Line, 1f)) g.DrawPath(pen, qp);
                    }
                    KitInk.Draw(g, ":15 / :45", fCell, fineQuarter ? KitInk.OnAccent : KitInk.Sub, qr, ContentAlignment.MiddleCenter);

                    for (int i = 0; i < 48; i++)
                    {
                        int mins = SlotMinutes(i);
                        var r = CellRect(i);
                        bool isSel = mins == selectedMinutes;
                        bool isHot = hotIndex == i;
                        bool dimmed = minHighlightAfter >= 0 && mins <= minHighlightAfter;
                        Color bg = isSel ? KitInk.Accent : isHot ? KitInk.Hover : dimmed ? KitInk.Window : KitInk.Group;
                        Color fg = isSel ? KitInk.OnAccent : dimmed && !isHot ? KitInk.Faint : KitInk.Text;
                        using (var pth = Ink.Round(r, 6 * s))
                        {
                            using (var b = new SolidBrush(bg)) g.FillPath(b, pth);
                            if (!isSel && !dimmed)
                                using (var pen = new Pen(KitInk.Line, 1f)) g.DrawPath(pen, pth);
                        }
                        string lbl = (mins / 60).ToString("00") + ":" + (mins % 60).ToString("00");
                        KitInk.Draw(g, lbl, fCell, fg, r, ContentAlignment.MiddleCenter);
                    }

                    if (minHighlightAfter >= 0)
                    {
                        var er = EndDayRect();
                        bool selEnd = selectedMinutes == 23 * 60 + 59;
                        using (var pth = Ink.Round(er, 6 * s))
                        {
                            using (var b = new SolidBrush(selEnd ? KitInk.Accent : hotIndex == -3 ? KitInk.Hover : KitInk.Group)) g.FillPath(b, pth);
                            if (!selEnd) using (var pen = new Pen(KitInk.Line, 1f)) g.DrawPath(pen, pth);
                        }
                        KitInk.Draw(g, "23:59", fCell, selEnd ? KitInk.OnAccent : KitInk.Sub, er, ContentAlignment.MiddleCenter);
                    }

                    using (var pen = new Pen(KitInk.Line, 1f))
                        g.DrawRectangle(pen, 0, 0, Width - 1, Height - 1);
                }

                protected override void Dispose(bool disposing)
                {
                    if (disposing)
                    {
                        fHead.Dispose();
                        fCell.Dispose();
                    }
                    base.Dispose(disposing);
                }
            }

            readonly Island island;
            readonly bool isEdit;
            readonly string eventId;
            readonly DateTime initialDay;
            readonly string accountEmail;

            readonly Label headerLabel = new Label { AutoSize = false };
            readonly Label emailLabel = new Label { AutoSize = false };
            readonly RoundedField titleField = new RoundedField();
            readonly RoundedField locationField = new RoundedField();

            readonly EventPillButton chipToday = new EventPillButton { Subtle = true };
            readonly EventPillButton chipTomorrow = new EventPillButton { Subtle = true };
            readonly EventPillButton chipWeek = new EventPillButton { Subtle = true };

            readonly EventPillButton prevDayBtn = new EventPillButton { Text = "‹", Radius = 9f };
            readonly EventPillButton dateBtn = new EventPillButton { Radius = 9f };
            readonly EventPillButton nextDayBtn = new EventPillButton { Text = "›", Radius = 9f };

            readonly KitCheck allDay = new KitCheck();
            readonly EventPillButton dur30 = new EventPillButton { Subtle = true };
            readonly EventPillButton dur60 = new EventPillButton { Subtle = true };
            readonly EventPillButton dur120 = new EventPillButton { Subtle = true };

            readonly EventPillButton startBtn = new EventPillButton { Radius = 9f };
            readonly Label timeSep = new Label { AutoSize = false, Text = "→", TextAlign = ContentAlignment.MiddleCenter };
            readonly EventPillButton endBtn = new EventPillButton { Radius = 9f };

            readonly Label warning = new Label { AutoSize = false };
            readonly EventPillButton deleteBtn = new EventPillButton { Negative = true };
            readonly KitButton cancel = new KitButton();
            readonly KitButton save = new KitButton { Primary = true };

            DateTime chosen;
            int startMinutes;
            int endMinutes;
            Font fHead, fBody, fField, fSmall, fChip;
            bool busy;

            public bool IsEdit { get { return isEdit; } }
            public string EventId { get { return eventId; } }
            public DateTime InitialDay { get { return initialDay; } }

            public GoogleEventDialog(Island island, DateTime initialDay)
                : this(island, initialDay, null)
            {
            }

            public GoogleEventDialog(Island island, ClassSlot existing)
                : this(island, existing != null && existing.Start != DateTime.MinValue ? existing.Start.Date : DateTime.Today, existing)
            {
            }

            GoogleEventDialog(Island island, DateTime day, ClassSlot existing)
            {
                this.island = island;
                isEdit = existing != null && !string.IsNullOrEmpty(existing.Id);
                eventId = isEdit ? existing.Id : "";
                initialDay = day.Date;
                chosen = initialDay;
                accountEmail = island != null ? (island.GoogleCalendarEmail ?? "").Trim() : "";

                FormBorderStyle = FormBorderStyle.FixedDialog;
                MaximizeBox = MinimizeBox = false;
                StartPosition = FormStartPosition.CenterScreen;
                ShowInTaskbar = false;
                TopMost = true;

                if (isEdit)
                {
                    titleField.Box.Text = existing.Title ?? "";
                    locationField.Box.Text = existing.Room ?? "";
                    allDay.Checked = existing.AllDay;
                    if (existing.AllDay)
                    {
                        startMinutes = 9 * 60;
                        endMinutes = 10 * 60;
                    }
                    else
                    {
                        startMinutes = existing.Start.Hour * 60 + existing.Start.Minute;
                        int rawEnd = (int)Math.Round((existing.End - existing.Start.Date).TotalMinutes);
                        if (rawEnd <= startMinutes) rawEnd = Math.Min(1439, startMinutes + 60);
                        if (rawEnd > 1439) rawEnd = 1439;
                        endMinutes = rawEnd;
                    }
                }
                else
                {
                    if (chosen == DateTime.Today)
                    {
                        var now = DateTime.Now;
                        int nowMins = now.Hour * 60 + now.Minute;
                        int nextSlot = ((nowMins / 30) + 1) * 30;
                        if (nextSlot >= 23 * 60 + 30)
                        {
                            startMinutes = 23 * 60 + 30;
                            endMinutes = 23 * 60 + 59;
                        }
                        else
                        {
                            startMinutes = nextSlot;
                            endMinutes = Math.Min(23 * 60 + 59, startMinutes + 60);
                        }
                    }
                    else
                    {
                        startMinutes = 9 * 60;
                        endMinutes = 10 * 60;
                    }
                }

                chipToday.Click += delegate { SetDate(DateTime.Today); };
                chipTomorrow.Click += delegate { SetDate(DateTime.Today.AddDays(1)); };
                chipWeek.Click += delegate { SetDate(DateTime.Today.AddDays(7)); };
                prevDayBtn.Click += delegate { SetDate(chosen.AddDays(-1)); };
                nextDayBtn.Click += delegate { SetDate(chosen.AddDays(1)); };
                dateBtn.Click += delegate { ShowCalendarPopup(); };

                allDay.CheckedChanged += delegate { UpdateTimeState(); };
                dur30.Click += delegate { ApplyDuration(30); };
                dur60.Click += delegate { ApplyDuration(60); };
                dur120.Click += delegate { ApplyDuration(120); };
                startBtn.Click += delegate { ShowTimePopup(true); };
                endBtn.Click += delegate { ShowTimePopup(false); };

                titleField.Box.TextChanged += delegate
                {
                    if (titleField.Invalid) titleField.Invalid = false;
                    if (warning.Text.Length > 0) warning.Text = "";
                };
                titleField.Box.KeyDown += HandleSingleLineEnter;
                locationField.Box.KeyDown += HandleSingleLineEnter;

                deleteBtn.Click += delegate { DeleteCurrent(); };
                save.Click += delegate { Submit(); };
                cancel.Click += delegate { DialogResult = DialogResult.Cancel; Close(); };

                Controls.AddRange(new Control[]
                {
                    headerLabel, emailLabel,
                    titleField, locationField,
                    chipToday, chipTomorrow, chipWeek,
                    prevDayBtn, dateBtn, nextDayBtn,
                    allDay, dur30, dur60, dur120,
                    startBtn, timeSep, endBtn,
                    warning, deleteBtn, cancel, save
                });

                Shown += delegate
                {
                    titleField.Box.Focus();
                    if (isEdit) titleField.Box.SelectAll();
                };

                ApplyTexts();
                UpdateDateControls();
                UpdateTimeState();
            }

            void HandleSingleLineEnter(object sender, KeyEventArgs e)
            {
                if (e.KeyCode == Keys.Enter && !e.Shift)
                {
                    e.SuppressKeyPress = true;
                    e.Handled = true;
                    Submit();
                }
            }

            void ApplyTexts()
            {
                Text = isEdit ? Lang.T("Edit acara") : Lang.T("Acara baru");
                headerLabel.Text = Text;
                emailLabel.Text = accountEmail;
                emailLabel.Visible = accountEmail.Length > 0;
                titleField.Box.AccessibleName = Lang.T("Judul acara");
                locationField.Box.AccessibleName = Lang.T("Lokasi (opsional)");
                chipToday.Text = Lang.T("Hari ini");
                chipTomorrow.Text = Lang.T("Besok");
                chipWeek.Text = Lang.T("+1 Minggu");
                allDay.Text = Lang.T("Sepanjang hari");
                dur30.Text = Lang.T("30 mnt");
                dur60.Text = Lang.T("1 jam");
                dur120.Text = Lang.T("2 jam");
                deleteBtn.Text = Lang.T("Hapus");
                deleteBtn.Visible = isEdit;
                cancel.Text = Lang.T("Batal");
                if (!busy) save.Text = Lang.T("Simpan");
            }

            void SetDate(DateTime day)
            {
                chosen = day.Date;
                if (warning.Text.Length > 0) warning.Text = "";
                UpdateDateControls();
            }

            void UpdateDateControls()
            {
                var today = DateTime.Today;
                chipToday.Active = chosen == today;
                chipTomorrow.Active = chosen == today.AddDays(1);
                chipWeek.Active = chosen == today.AddDays(7);
                dateBtn.Text = chosen.ToString("ddd, d MMM yyyy", Fmt.Id) + "  ▾";
                chipToday.Invalidate();
                chipTomorrow.Invalidate();
                chipWeek.Invalidate();
            }

            void ApplyDuration(int minutes)
            {
                if (allDay.Checked) return;
                endMinutes = Math.Min(1439, startMinutes + minutes);
                if (endMinutes <= startMinutes) endMinutes = Math.Min(1439, startMinutes + 15);
                if (warning.Text.Length > 0) warning.Text = "";
                UpdateTimeState();
            }

            void SetStartMinutes(int newStart)
            {
                int dur = endMinutes - startMinutes;
                if (dur <= 0) dur = 60;
                startMinutes = Math.Max(0, Math.Min(1438, newStart));
                endMinutes = Math.Min(1439, startMinutes + dur);
                if (endMinutes <= startMinutes) endMinutes = Math.Min(1439, startMinutes + 15);
                if (warning.Text.Length > 0) warning.Text = "";
                UpdateTimeState();
            }

            void SetEndMinutes(int newEnd)
            {
                endMinutes = Math.Max(0, Math.Min(1439, newEnd));
                if (warning.Text.Length > 0) warning.Text = "";
                UpdateTimeState();
            }

            static string FormatHm(int mins)
            {
                return (mins / 60).ToString("00") + ":" + (mins % 60).ToString("00");
            }

            void UpdateTimeState()
            {
                bool timed = !allDay.Checked;
                startBtn.Enabled = timed;
                endBtn.Enabled = timed;
                dur30.Enabled = timed;
                dur60.Enabled = timed;
                dur120.Enabled = timed;
                timeSep.ForeColor = timed ? KitInk.Sub : KitInk.Faint;

                startBtn.Text = FormatHm(startMinutes) + "  ▾";
                endBtn.Text = FormatHm(endMinutes) + "  ▾";
                int dur = endMinutes - startMinutes;
                dur30.Active = timed && dur == 30;
                dur60.Active = timed && dur == 60;
                dur120.Active = timed && dur == 120;
                dur30.Invalidate();
                dur60.Invalidate();
                dur120.Invalidate();
            }

            void ShowCalendarPopup()
            {
                var drop = new ToolStripDropDown
                {
                    Padding = Padding.Empty,
                    Margin = Padding.Empty,
                    BackColor = KitInk.Window,
                    AutoClose = true,
                    DropShadowEnabled = true
                };
                var cal = new CalendarPopupControl(chosen, Ui, delegate(DateTime picked)
                {
                    SetDate(picked);
                    drop.Close();
                });
                var host = new ToolStripControlHost(cal)
                {
                    Padding = Padding.Empty,
                    Margin = Padding.Empty,
                    AutoSize = false,
                    Size = cal.Size
                };
                drop.Items.Add(host);
                drop.HandleCreated += delegate { Native.RoundCorners(drop.Handle); };
                drop.Closed += delegate
                {
                    if (!IsDisposed && IsHandleCreated)
                        BeginInvoke(new Action(delegate { cal.Dispose(); drop.Dispose(); }));
                    else
                    {
                        cal.Dispose();
                        drop.Dispose();
                    }
                };
                drop.Show(dateBtn, new Point(0, dateBtn.Height + (int)Math.Round(4 * Ui)));
            }

            void ShowTimePopup(bool isStart)
            {
                if (allDay.Checked) return;
                var drop = new ToolStripDropDown
                {
                    Padding = Padding.Empty,
                    Margin = Padding.Empty,
                    BackColor = KitInk.Window,
                    AutoClose = true,
                    DropShadowEnabled = true
                };
                int current = isStart ? startMinutes : endMinutes;
                int after = isStart ? -1 : startMinutes;
                string hdr = isStart ? Lang.T("Mulai") : Lang.T("Waktu selesai");
                var picker = new TimePopupControl(current, after, hdr, Ui, delegate(int mins)
                {
                    if (isStart) SetStartMinutes(mins);
                    else SetEndMinutes(mins);
                    drop.Close();
                });
                var host = new ToolStripControlHost(picker)
                {
                    Padding = Padding.Empty,
                    Margin = Padding.Empty,
                    AutoSize = false,
                    Size = picker.Size
                };
                drop.Items.Add(host);
                drop.HandleCreated += delegate { Native.RoundCorners(drop.Handle); };
                drop.Closed += delegate
                {
                    if (!IsDisposed && IsHandleCreated)
                        BeginInvoke(new Action(delegate { picker.Dispose(); drop.Dispose(); }));
                    else
                    {
                        picker.Dispose();
                        drop.Dispose();
                    }
                };
                var anchor = isStart ? startBtn : endBtn;
                drop.Show(anchor, new Point(0, anchor.Height + (int)Math.Round(4 * Ui)));
            }

            void SetBusy(bool on, string saveLabel)
            {
                busy = on;
                save.Enabled = !on;
                deleteBtn.Enabled = !on;
                cancel.Enabled = !on;
                save.Text = saveLabel;
            }

            void Submit()
            {
                if (busy) return;
                string evTitle = titleField.Box.Text.Replace('\r', ' ').Replace('\n', ' ').Trim();
                if (evTitle.Length == 0)
                {
                    titleField.Invalid = true;
                    warning.Text = Lang.T("Judul wajib diisi");
                    titleField.Box.Focus();
                    return;
                }
                string evLoc = locationField.Box.Text.Replace('\r', ' ').Replace('\n', ' ').Trim();
                bool isAllDay = allDay.Checked;
                DateTime startLocal;
                DateTime endLocal;
                if (isAllDay)
                {
                    startLocal = chosen.Date;
                    endLocal = chosen.Date.AddDays(1);
                }
                else
                {
                    startLocal = chosen.Date + TimeSpan.FromMinutes(startMinutes);
                    endLocal = chosen.Date + TimeSpan.FromMinutes(endMinutes);
                    if (endLocal <= startLocal)
                    {
                        warning.Text = Lang.T("Waktu selesai harus setelah mulai");
                        return;
                    }
                }

                warning.Text = "";
                SetBusy(true, Lang.T("Menyimpan…"));
                ThreadPool.QueueUserWorkItem(delegate
                {
                    string err;
                    bool ok = isEdit
                        ? island.UpdateGoogleCalendarEvent(eventId, evTitle, startLocal, endLocal, evLoc, isAllDay, out err)
                        : island.CreateGoogleCalendarEvent(evTitle, startLocal, endLocal, evLoc, isAllDay, out err);
                    if (IsDisposed) return;
                    try
                    {
                        BeginInvoke(new Action(delegate
                        {
                            if (IsDisposed) return;
                            if (ok)
                            {
                                DialogResult = DialogResult.OK;
                                Close();
                                return;
                            }
                            SetBusy(false, Lang.T("Simpan"));
                            warning.Text = err ?? Lang.T("Gagal");
                        }));
                    }
                    catch (InvalidOperationException) { }
                });
            }

            void DeleteCurrent()
            {
                if (!isEdit || busy) return;
                string evTitle = titleField.Box.Text.Replace('\r', ' ').Replace('\n', ' ').Trim();
                warning.Text = "";
                SetBusy(true, Lang.T("Menghapus…"));
                ThreadPool.QueueUserWorkItem(delegate
                {
                    string err;
                    bool ok = island.DeleteGoogleCalendarEvent(eventId, out err);
                    if (IsDisposed) return;
                    try
                    {
                        BeginInvoke(new Action(delegate
                        {
                            if (IsDisposed) return;
                            if (ok)
                            {
                                DialogResult = DialogResult.OK;
                                Close();
                                return;
                            }
                            SetBusy(false, Lang.T("Simpan"));
                            warning.Text = err ?? Lang.T("Gagal");
                        }));
                    }
                    catch (InvalidOperationException) { }
                });
            }

            protected override void OnHandleCreated(EventArgs e) { base.OnHandleCreated(e); Relayout(); }

            void DisposeFonts()
            {
                if (fHead != null) { fHead.Dispose(); fHead = null; }
                if (fBody != null) { fBody.Dispose(); fBody = null; }
                if (fField != null) { fField.Dispose(); fField = null; }
                if (fSmall != null) { fSmall.Dispose(); fSmall = null; }
                if (fChip != null) { fChip.Dispose(); fChip = null; }
            }

            static int MeasureChip(string text, Font f, int padX)
            {
                return TextRenderer.MeasureText(text, f, Size.Empty, TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine).Width + padX;
            }

            public override void Relayout()
            {
                if (!IsHandleCreated) return;
                DisposeFonts();
                float s = Ui > 0 ? Ui : 1f;
                fHead = new Font(Ink.MenuFamily, 18f * s, FontStyle.Bold, GraphicsUnit.Pixel);
                fBody = UiFont(13f);
                fField = UiFont(14f);
                fSmall = UiFont(12f);
                fChip = UiFont(11.5f);

                Font = fBody;
                BackColor = KitInk.Window;
                ForeColor = KitInk.Text;
                ApplyTexts();

                int w = (int)Math.Round(460 * s);
                int m = (int)Math.Round(22 * s);
                int cw = w - 2 * m;
                int gap = (int)Math.Round(6 * s);
                int y = (int)Math.Round(18 * s);

                headerLabel.Font = fHead;
                headerLabel.BackColor = KitInk.Window;
                headerLabel.ForeColor = KitInk.Text;
                headerLabel.SetBounds(m, y, cw, (int)Math.Round(26 * s));
                y += (int)Math.Round(26 * s);

                if (emailLabel.Visible)
                {
                    emailLabel.Font = fSmall;
                    emailLabel.BackColor = KitInk.Window;
                    emailLabel.ForeColor = KitInk.Sub;
                    emailLabel.SetBounds(m, y, cw, (int)Math.Round(18 * s));
                    y += (int)Math.Round(26 * s);
                }
                else
                {
                    y += (int)Math.Round(10 * s);
                }

                titleField.SetBounds(m, y, cw, (int)Math.Round(40 * s));
                titleField.Place(s, fField, Lang.T("Judul acara…"));
                y += (int)Math.Round(50 * s);

                locationField.SetBounds(m, y, cw, (int)Math.Round(38 * s));
                locationField.Place(s, fBody, Lang.T("Lokasi atau tautan rapat…"));
                y += (int)Math.Round(54 * s);

                // Quick date chips
                int chipH = (int)Math.Round(26 * s);
                int chipPad = (int)Math.Round(20 * s);
                chipToday.Font = chipTomorrow.Font = chipWeek.Font = fChip;
                int wToday = MeasureChip(chipToday.Text, fChip, chipPad);
                int wTom = MeasureChip(chipTomorrow.Text, fChip, chipPad);
                int wWeek = MeasureChip(chipWeek.Text, fChip, chipPad);
                chipToday.SetBounds(m, y, wToday, chipH);
                chipTomorrow.SetBounds(m + wToday + gap, y, wTom, chipH);
                chipWeek.SetBounds(m + wToday + wTom + 2 * gap, y, wWeek, chipH);
                y += chipH + (int)Math.Round(8 * s);

                // Date stepper row
                int rowH = (int)Math.Round(34 * s);
                int stepW = (int)Math.Round(34 * s);
                prevDayBtn.Font = nextDayBtn.Font = fField;
                dateBtn.Font = fBody;
                prevDayBtn.SetBounds(m, y, stepW, rowH);
                dateBtn.SetBounds(m + stepW + gap, y, cw - 2 * stepW - 2 * gap, rowH);
                nextDayBtn.SetBounds(w - m - stepW, y, stepW, rowH);
                y += rowH + (int)Math.Round(16 * s);

                // All-day toggle + Duration chips
                allDay.Font = fSmall;
                int allW = Math.Min((int)Math.Round(165 * s), MeasureChip(allDay.Text, fSmall, (int)Math.Round(42 * s)));
                allDay.SetBounds(m, y, allW, chipH);
                dur30.Font = dur60.Font = dur120.Font = fChip;
                int w30 = MeasureChip(dur30.Text, fChip, chipPad);
                int w60 = MeasureChip(dur60.Text, fChip, chipPad);
                int w120 = MeasureChip(dur120.Text, fChip, chipPad);
                int dx = w - m - w120;
                dur120.SetBounds(dx, y, w120, chipH);
                dx -= w60 + gap;
                dur60.SetBounds(dx, y, w60, chipH);
                dx -= w30 + gap;
                dur30.SetBounds(dx, y, w30, chipH);
                y += chipH + (int)Math.Round(8 * s);

                // Start -> End time row
                int sepW = (int)Math.Round(24 * s);
                int timeW = (cw - sepW) / 2;
                startBtn.Font = endBtn.Font = fBody;
                timeSep.Font = fBody;
                timeSep.BackColor = KitInk.Window;
                timeSep.ForeColor = allDay.Checked ? KitInk.Faint : KitInk.Sub;
                startBtn.SetBounds(m, y, timeW, rowH);
                timeSep.SetBounds(m + timeW, y, sepW, rowH);
                endBtn.SetBounds(w - m - timeW, y, timeW, rowH);
                y += rowH + (int)Math.Round(10 * s);

                // Inline warning
                warning.Font = fSmall;
                warning.BackColor = KitInk.Window;
                warning.ForeColor = Ink.Base.Negative;
                warning.SetBounds(m, y, cw, (int)Math.Round(34 * s));
                y += (int)Math.Round(40 * s);

                // Footer actions
                int footH = (int)Math.Round(36 * s);
                int saveW = (int)Math.Round(116 * s);
                int cancelW = (int)Math.Round(96 * s);
                int delW = (int)Math.Round(92 * s);
                deleteBtn.Font = cancel.Font = save.Font = fBody;
                deleteBtn.SetBounds(m, y, delW, footH);
                save.SetBounds(w - m - saveW, y, saveW, footH);
                cancel.SetBounds(save.Left - (int)Math.Round(10 * s) - cancelW, y, cancelW, footH);
                y += footH + (int)Math.Round(20 * s);

                ClientSize = new Size(w, y);
            }

            protected override bool ProcessCmdKey(ref Message msg, Keys keys)
            {
                if (keys == (Keys.Control | Keys.Enter)) { save.PerformClick(); return true; }
                if (keys == Keys.Escape) { DialogResult = DialogResult.Cancel; Close(); return true; }
                return base.ProcessCmdKey(ref msg, keys);
            }

            protected override void Dispose(bool disposing)
            {
                base.Dispose(disposing);
                if (disposing) DisposeFonts();
            }
        }
    }
}
