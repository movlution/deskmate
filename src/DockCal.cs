using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Globalization;
using System.Windows.Forms;

namespace Deskmate
{
    partial class Island
    {
        // Notifications and calendar card (Win+N)
        sealed class CalendarSheet : GlassSheet
        {
            readonly System.Windows.Forms.Timer refresh = new System.Windows.Forms.Timer { Interval = 1000 };
            int month, rows, first;
            string pressedId;
            DateTime? _selectedDay;
            public DateTime? selectedDay { get { return _selectedDay; } set { _selectedDay = value; } }
            public DateTime? SelectedDay { get { return _selectedDay; } set { _selectedDay = value; } }
            float notifH, EventAreaH;

            const float Pad = 20, RowH = 58, Cell = 44, CellH = 38, Foot = 52, MaxRows = 4;

            public CalendarSheet(Island isle, DockWindow dock) : base(isle, dock, "cal", "Deskmate Calendar")
            {
                W = 360;
                refresh.Tick += delegate { Render(); };
            }

            public bool Pressed(string id) { return Down(id); }
            public new bool Down(string id) { return (pressedId == id || pressId == id) && hotId == id; }

            protected override void OnMouseDown(MouseEventArgs e)
            {
                if (e.Button == MouseButtons.Left) pressedId = HitAt(e.Location);
                base.OnMouseDown(e);
            }
            protected override void OnMouseUp(MouseEventArgs e)
            {
                pressedId = null;
                base.OnMouseUp(e);
            }
            protected override void OnMouseLeave(EventArgs e)
            {
                pressedId = null;
                base.OnMouseLeave(e);
            }

            protected override void Opening(Rectangle mon, int barTop)
            {
                rows = isle.NotifOn ? (int)Math.Min(MaxRows, Math.Max(1, isle.notifs.Count)) : 0;
                notifH = isle.NotifOn ? 46 + (isle.notifs.Count == 0 ? 44 : rows * RowH) + 8 : 0;
                EventAreaH = ((isle.classes != null && isle.classes.Count > 0) || isle.GoogleCalendarConnected) ? 96f : 0f;
                H = notifH + 76 + 24 + 6 * CellH + 12 + EventAreaH + Foot;
                refresh.Start();
            }

            protected override void Teardown() { refresh.Stop(); refresh.Dispose(); }

            protected override void Backdrop(RectangleF r) { Shelf(r, Foot); }

            protected override void Draw()
            {
                if (isle.NotifOn) DrawNotifs();
                DrawMonth(notifH);
                if (EventAreaH > 0) DrawDayEvents(notifH + 76 + 24 + 6 * CellH + 8);
                DrawFoot();
            }

            internal List<ClassSlot> EventsForDay(DateTime day)
            {
                var list = new List<ClassSlot>();
                if (isle.classes == null) return list;
                DateTime d = day.Date;
                DateTime next = d.AddDays(1);
                for (int i = 0; i < isle.classes.Count; i++)
                {
                    var c = isle.classes[i];
                    if (c == null) continue;
                    bool match = c.AllDay
                        ? (c.Start.Date <= d && c.End.Date > d)
                        : (c.Start < next && (c.End > c.Start ? c.End > d : c.Start.Date == d));
                    if (match) list.Add(c);
                }
                return list;
            }

            // Clearing motion: rows fly off, the rest close the gap, an all-clear check pops
            sealed class Ghost { public NotifItem Item; public float Y; public double T0; public bool Lit; }
            sealed class Shift { public float From; public double T0; }
            readonly List<Ghost> ghosts = new List<Ghost>();
            readonly Dictionary<uint, Shift> shifts = new Dictionary<uint, Shift>();
            readonly System.Diagnostics.Stopwatch fx = System.Diagnostics.Stopwatch.StartNew();
            double cheerT0 = -10, clearBtnT0 = -10;
            bool cheered;
            const float FlySecs = 0.46f, ShiftSecs = 0.36f, CheerSecs = 0.95f, BtnSecs = 0.2f, Stagger = 0.065f;

            double Now { get { return fx.Elapsed.TotalSeconds; } }

            // Anticipation: a small pull back, then away
            static float InBack(float t)
            {
                t = Ease.Clamp01(t);
                const float c1 = 1.70158f, c3 = c1 + 1;
                return c3 * t * t * t - c1 * t * t;
            }

            float ShiftOf(uint id, double now)
            {
                Shift s;
                if (!shifts.TryGetValue(id, out s)) return 0;
                float t = (float)((now - s.T0) / ShiftSecs);
                return s.From * (1 - Ease.OutBack(t));
            }

            protected override bool Animating(float dt)
            {
                double now = Now;
                ghosts.RemoveAll(gh => now - gh.T0 >= FlySecs);
                List<uint> done = null;
                foreach (var kv in shifts)
                    if (now - kv.Value.T0 >= ShiftSecs) { if (done == null) done = new List<uint>(); done.Add(kv.Key); }
                if (done != null) foreach (var k in done) shifts.Remove(k);
                return ghosts.Count > 0 || shifts.Count > 0 || now - cheerT0 < CheerSecs || now - clearBtnT0 < BtnSecs;
            }

            void ClearAllFx()
            {
                var list = isle.notifs;
                double now = Now;
                cheered = true;
                shifts.Clear();
                if (Anim.Reduced) { cheerT0 = now - CheerSecs; return; }
                int shown = Math.Min(rows, list.Count - first);
                for (int i = 0; i < shown; i++)
                {
                    int k = first + i;
                    ghosts.Add(new Ghost { Item = list[k], Y = 46 + i * RowH, T0 = now + i * Stagger, Lit = hotId == "n" + k || hotId == "x" + k });
                }
                clearBtnT0 = now;
                cheerT0 = now + Math.Max(0, shown - 1) * Stagger + FlySecs * 0.6f;
                first = 0;
                Animate();
            }

            void RemoveOneFx(int x)
            {
                var list = isle.notifs;
                var item = list[x];
                int oldFirst = first;
                isle.RemoveNotifs(new List<NotifItem> { item });
                if (Anim.Reduced) { if (list.Count == 0) { cheered = true; cheerT0 = Now - CheerSecs; } return; }
                double now = Now;
                ghosts.Add(new Ghost { Item = item, Y = 46 + (x - oldFirst) * RowH, T0 = now, Lit = true });
                int newFirst = Math.Max(0, Math.Min(oldFirst, list.Count - rows));
                first = newFirst;
                // Everyone below (or above, when the window slid back) closes the gap
                for (int j = 0; j < list.Count; j++)
                {
                    int was = j < x ? j : j + 1;
                    float delta = ((was - oldFirst) - (j - newFirst)) * RowH;
                    if (delta == 0) continue;
                    uint id = list[j].Id;
                    shifts[id] = new Shift { From = ShiftOf(id, now) + delta, T0 = now + 0.08 };
                }
                if (list.Count == 0) { cheered = true; cheerT0 = now + FlySecs * 0.55f; clearBtnT0 = now; }
                Animate();
            }

            void DrawLogo(Image img, RectangleF r)
            {
                if (p.Alpha >= 0.995f) { g.DrawImage(img, r); return; }
                using (var ia = new ImageAttributes())
                {
                    ia.SetColorMatrix(new ColorMatrix { Matrix33 = p.Alpha });
                    var pts = new[] { new PointF(r.X, r.Y), new PointF(r.Right, r.Y), new PointF(r.X, r.Bottom) };
                    g.DrawImage(img, pts, new RectangleF(0, 0, img.Width, img.Height), GraphicsUnit.Pixel, ia);
                }
            }

            void DrawRowFace(NotifItem item, RectangleF row, bool showTime)
            {
                float cy = row.Y + row.Height / 2;
                var logo = new RectangleF(row.X + 10, cy - 16, 32, 32);
                if (item.Logo != null) DrawLogo(item.Logo, logo);
                else
                {
                    p.FillRound(logo, 9, p.T.FillHover);
                    string name = item.AppName.Length > 0 ? item.AppName : "?";
                    p.TextCenter(name.Substring(0, 1).ToUpperInvariant(), Ink.Semi(13f), p.T.Text, logo.X + 16, cy);
                }
                float tx = row.X + 52;
                p.NotifText(isle.NotifTitle(item), Ink.Semi(12.5f), p.T.Text, tx, cy - 9, row.Right - tx - 74, StringAlignment.Near);
                p.NotifText(isle.NotifBody(item), Ink.Text(11.5f), p.T.Sub, tx, cy + 9, row.Right - tx - 40, StringAlignment.Near);
                if (showTime) p.TextRight(Fmt.Ago(item.Time), Ink.Small(11f), p.T.Sub, row.Right - 12, cy - 9);
            }

            void DrawClearButton(float a, bool live)
            {
                var f = Ink.Small(12f);
                string label = Lang.T("Hapus semua");
                float w = p.Measure(label, f) + 24;
                var b = new RectangleF(W - Pad - w, 13, w, 26);
                float keep = p.Alpha;
                var st = g.Save();
                if (live) Hit(b, "clear");
                else
                {
                    // Sinks into itself as the list goes
                    float s = 0.82f + 0.18f * a;
                    g.TranslateTransform(b.X + w / 2, b.Y + 13);
                    g.ScaleTransform(s, s);
                    g.TranslateTransform(-(b.X + w / 2), -(b.Y + 13));
                    p.Alpha = keep * a;
                }
                bool down = live && Down("clear");
                if (down) b.Offset(0, 0.75f);
                p.FillRound(b, 8, down ? p.T.FillPressed : live && hotId == "clear" ? p.T.FillHover : p.T.Fill);
                p.TextCenter(label, f, p.T.Text, b.X + w / 2, b.Y + b.Height / 2);
                g.Restore(st);
                p.Alpha = keep;
            }

            void DrawGhost(Ghost gh, double now)
            {
                float t = (float)((now - gh.T0) / FlySecs);
                if (t >= 1) return;
                t = Math.Max(0, t);
                var row = new RectangleF(Pad - 8, gh.Y, W - Pad * 2 + 16, RowH - 4);
                float dx = InBack(t) * (W * 0.95f);
                float s = 1 - 0.07f * Ease.OutCubic(t);
                float keep = p.Alpha;
                var st = g.Save();
                g.TranslateTransform(row.X + row.Width / 2 + dx, row.Y + row.Height / 2);
                g.RotateTransform(2.5f * InBack(t));
                g.ScaleTransform(s, s);
                g.TranslateTransform(-(row.X + row.Width / 2), -(row.Y + row.Height / 2));
                float fade = 1 - t * t;
                // The card lifts before it leaves
                float lift = gh.Lit ? 1 : Math.Min(1, t * 6);
                p.Alpha = keep * fade * lift;
                p.FillRound(row, 14, p.T.Fill);
                p.Alpha = keep * fade;
                DrawRowFace(gh.Item, row, !gh.Lit);
                g.Restore(st);
                p.Alpha = keep;
            }

            // All clear: badge pops, ripple, a burst of sparks, then the words
            void DrawCheer(double now)
            {
                float t = (float)((now - cheerT0) / CheerSecs);
                if (t <= 0) return;
                t = Math.Min(1, t);
                var f = Ink.Small(12f);
                string txt = Lang.T("Tidak ada notifikasi baru");
                float tw = p.Measure(txt, f);
                const float R = 11, Gap = 10;
                float areaBottom = notifH - 8;
                float cy = 46 + (areaBottom - 46) / 2;
                float x0 = (W - (R * 2 + Gap + tw)) / 2, cx = x0 + R;
                float keep = p.Alpha;

                float ru = Ease.Clamp01((t - 0.08f) / 0.6f);
                if (ru > 0 && ru < 1)
                {
                    p.Alpha = keep * (1 - ru) * 0.55f;
                    float rr = R + 18 * Ease.OutCubic(ru);
                    g.DrawEllipse(p.P(p.T.Accent, 1.6f * (1 - ru) + 0.4f), cx - rr, cy - rr, rr * 2, rr * 2);
                }
                float su = Ease.Clamp01((t - 0.12f) / 0.55f);
                if (su > 0 && su < 1)
                {
                    p.Alpha = keep * (1 - su * su);
                    for (int k = 0; k < 8; k++)
                    {
                        double ang = (k * 45 + 22.5) * Math.PI / 180;
                        float dist = R + 5 + (k % 2 == 0 ? 17 : 12) * Ease.OutCubic(su);
                        float sz = (k % 2 == 0 ? 2.2f : 1.6f) * (1 - su * 0.6f);
                        p.Circle(cx + (float)Math.Cos(ang) * dist, cy + (float)Math.Sin(ang) * dist, sz, p.T.Accent);
                    }
                }
                p.Alpha = keep;
                float pop = Ease.OutBack(t / 0.42f);
                if (pop > 0.01f) p.Circle(cx, cy, R * pop, p.T.Accent);
                float cu = Ease.OutCubic(Ease.Clamp01((t - 0.22f) / 0.32f));
                if (cu > 0)
                {
                    // Two strokes drawn in order: short leg, then the long one
                    var a = new PointF(cx - 4.6f, cy + 0.2f);
                    var b = new PointF(cx - 1.4f, cy + 3.4f);
                    var c = new PointF(cx + 4.8f, cy - 3.4f);
                    const float l1 = 0.32f;
                    var pen = p.P(p.T.OnAccent, 1.9f);
                    float u1 = Math.Min(1, cu / l1);
                    g.DrawLine(pen, a, new PointF(a.X + (b.X - a.X) * u1, a.Y + (b.Y - a.Y) * u1));
                    if (cu > l1)
                    {
                        float u2 = (cu - l1) / (1 - l1);
                        g.DrawLine(pen, b, new PointF(b.X + (c.X - b.X) * u2, b.Y + (c.Y - b.Y) * u2));
                    }
                }
                float tu = Ease.OutCubic(Ease.Clamp01((t - 0.3f) / 0.45f));
                if (tu > 0)
                {
                    p.Alpha = keep * tu;
                    p.Text(txt, f, p.T.Sub, x0 + R * 2 + Gap - 8 * (1 - tu), cy, tw + 4, StringAlignment.Near);
                }
                p.Alpha = keep;
            }

            void DrawNotifs()
            {
                var list = isle.notifs;
                double now = Now;
                Caption(Lang.T("Notifikasi"), Pad + 4, 26);
                if (list.Count > 0) { cheered = false; DrawClearButton(1, true); }
                else if (now - clearBtnT0 < BtnSecs) DrawClearButton(1 - Ease.OutCubic((float)((now - clearBtnT0) / BtnSecs)), false);
                var area = new RectangleF(0, 44, W, Math.Max(0, notifH - 4 - 44));
                if (ghosts.Count > 0 || list.Count == 0)
                {
                    var st = g.Save();
                    g.SetClip(area, CombineMode.Intersect);
                    foreach (var gh in ghosts) DrawGhost(gh, now);
                    g.Restore(st);
                }
                if (list.Count == 0)
                {
                    if (cheered) DrawCheer(now);
                    else p.Text(Lang.T("Tidak ada notifikasi baru"), Ink.Small(12f), p.T.Sub, Pad + 4, 68, W - Pad * 2, StringAlignment.Near);
                    return;
                }
                first = Math.Max(0, Math.Min(first, list.Count - rows));
                var clip = g.Save();
                if (shifts.Count > 0) g.SetClip(area, CombineMode.Intersect);
                for (int i = 0; i < rows && first + i < list.Count; i++)
                {
                    var item = list[first + i];
                    var row = new RectangleF(Pad - 8, 46 + i * RowH + ShiftOf(item.Id, now), W - Pad * 2 + 16, RowH - 4);
                    string id = "n" + (first + i), del = "x" + (first + i);
                    bool hover = hotId == id || hotId == del;
                    Hit(row, id);
                    if (hover) p.FillRound(row, 14, pressId == id ? p.T.FillPressed : p.T.Fill);
                    DrawRowFace(item, row, !hover);
                    float cy = row.Y + row.Height / 2;
                    var x = new RectangleF(row.Right - 36, cy - 14, 28, 28);
                    if (hover)
                    {
                        Hit(x, del);
                        bool xdown = Down(del);
                        float xcy = x.Y + 14 + (xdown ? 0.75f : 0);
                        p.Circle(x.X + 14, xcy, 12, xdown ? p.T.FillPressed : hotId == del ? p.T.FillHover : p.T.Fill);
                        p.Cross(x.X + 14, xcy, 10, hotId == del ? p.T.Text : p.T.Sub);
                    }
                }
                g.Restore(clip);
                // More than fit: a thin scroll mark
                if (list.Count > rows)
                {
                    float track = rows * RowH - 8, thumb = Math.Max(20, track * rows / list.Count);
                    float y = 46 + (track - thumb) * first / Math.Max(1, list.Count - rows);
                    p.FillRound(new RectangleF(W - 7, y, 3, thumb), 1.5f, p.T.Line);
                }
                g.DrawLine(p.P(p.T.Line, 1f), Pad, notifH - 4, W - Pad, notifH - 4);
            }

            void DrawMonth(float top)
            {
                var ci = Fmt.Id;
                var today = DateTime.Today;
                var shown = new DateTime(today.Year, today.Month, 1).AddMonths(month);
                p.Text(today.ToString("dddd, d MMMM", ci), Ink.Semi(15f), p.T.Text, Pad + 4, top + 24, W - Pad * 2, StringAlignment.Near);
                if (month != 0)
                {
                    string mText = shown.ToString("MMMM yyyy", ci);
                    float mw = p.Measure(mText, Ink.Small(12.5f)) + 12;
                    var tb = new RectangleF(Pad, top + 42, mw, 24);
                    Hit(tb, "today");
                    bool tdown = Down("today");
                    if (tdown) tb.Offset(0, 0.75f);
                    if (hotId == "today") p.FillRound(tb, 6, tdown ? p.T.FillPressed : p.T.FillHover);
                    p.Text(mText, Ink.Small(12.5f), p.T.Accent, tb.X + 6, tb.Y + tb.Height / 2, mw, StringAlignment.Near);
                }
                else
                {
                    p.Text(shown.ToString("MMMM yyyy", ci), Ink.Small(12.5f), p.T.Sub, Pad + 4, top + 54, 200, StringAlignment.Near);
                }
                Button(new RectangleF(W - Pad - 34, top + 38, 32, 32), "next", (char)0xE76C, Lang.T("Bulan berikutnya"));
                Button(new RectangleF(W - Pad - 70, top + 38, 32, 32), "prev", (char)0xE76B, Lang.T("Bulan sebelumnya"));
                var dayOne = CultureInfo.CurrentCulture.DateTimeFormat.FirstDayOfWeek;
                float x0 = (W - 7 * Cell) / 2, y = top + 88;
                for (int c = 0; c < 7; c++)
                {
                    var dow = (DayOfWeek)(((int)dayOne + c) % 7);
                    p.TextCenter(ci.DateTimeFormat.GetShortestDayName(dow), Ink.Small(11f), p.T.Sub, x0 + c * Cell + Cell / 2, y);
                }
                y += 16;
                int lead = ((int)shown.DayOfWeek - (int)dayOne + 7) % 7;
                var day = shown.AddDays(-lead);
                for (int r = 0; r < 6; r++)
                    for (int c = 0; c < 7; c++, day = day.AddDays(1))
                    {
                        float cx = x0 + c * Cell + Cell / 2, cy = y + r * CellH + CellH / 2;
                        bool inMonth = day.Month == shown.Month, isToday = day == today;
                        string did = "d" + (day.Year * 10000 + day.Month * 100 + day.Day);
                        Hit(new RectangleF(cx - 16, cy - 16, 32, 32), did);
                        bool ddown = Down(did), dhot = hotId == did;
                        float dcy = ddown ? cy + 0.75f : cy;
                        if (isToday)
                        {
                            Color fill = ddown ? KitInk.Deepen(p.T.Accent, p.T.OnAccent, 0.22f) : dhot ? KitInk.Deepen(p.T.Accent, p.T.OnAccent, 0.1f) : p.T.Accent;
                            p.Circle(cx, dcy, 16, fill);
                        }
                        else if (ddown || dhot)
                        {
                            p.Circle(cx, dcy, 16, ddown ? p.T.FillPressed : p.T.FillHover);
                        }
                        else if (selectedDay.HasValue && day == selectedDay.Value)
                        {
                            g.DrawEllipse(p.P(p.T.Accent, 1.2f), cx - 16, dcy - 16, 32, 32);
                        }
                        p.TextCenter(day.Day.ToString(CultureInfo.InvariantCulture), isToday ? Ink.Semi(12.5f) : Ink.Small(12.5f),
                                     isToday ? p.T.OnAccent : inMonth ? p.T.Text : p.T.Sub, cx, dcy);
                        var dayEvs = EventsForDay(day);
                        if (dayEvs.Count > 0)
                        {
                            bool hasHoliday = false;
                            for (int k = 0; k < dayEvs.Count; k++)
                                if (dayEvs[k].Source == "holiday" || dayEvs[k].Kind == "libur") { hasHoliday = true; break; }
                            p.Circle(cx, dcy + 11.5f, 1.75f, isToday ? p.T.OnAccent : (hasHoliday ? p.T.Negative : p.T.Accent));
                        }
                    }
            }

            void DrawDayEvents(float top)
            {
                g.DrawLine(p.P(p.T.Line, 1f), Pad, top, W - Pad, top);
                DateTime target = selectedDay ?? DateTime.Today;
                var dayEvents = EventsForDay(target);
                string countText = dayEvents.Count > 0 ? Lang.F("{0} acara", dayEvents.Count) : Lang.T("Tidak ada acara");
                string header = target.ToString("ddd, d MMM", Fmt.Id) + " · " + countText;
                float addReserve = 0;
                if (isle.GoogleCalendarConnected)
                {
                    string addLbl = Lang.T("+ Acara");
                    float addW = p.Measure(addLbl, Ink.Semi(11.5f)) + 20;
                    var addRect = new RectangleF(W - Pad - addW, top + 4, addW, 24);
                    Hit(addRect, "addEvent");
                    bool addDown = Down("addEvent");
                    if (addDown) addRect.Offset(0, 0.75f);
                    Color addFill = addDown ? KitInk.Deepen(p.T.Accent, p.T.OnAccent, 0.22f)
                                            : hotId == "addEvent" ? KitInk.Deepen(p.T.Accent, p.T.OnAccent, 0.1f)
                                            : p.T.Accent;
                    p.FillRound(addRect, 12, addFill);
                    p.TextCenter(addLbl, Ink.Semi(11.5f), p.T.OnAccent, addRect.X + addW / 2, addRect.Y + addRect.Height / 2);
                    addReserve = addW + 8;
                }
                p.Text(header, Ink.Semi(12f), p.T.Sub, Pad + 4, top + 16, W - Pad * 2 - addReserve, StringAlignment.Near);
                int maxShow = Math.Min(3, dayEvents.Count);
                for (int i = 0; i < maxShow; i++)
                {
                    var c = dayEvents[i];
                    var rowRect = new RectangleF(Pad - 4, top + 31 + i * 22, W - Pad * 2 + 8, 22);
                    float cy = rowRect.Y + rowRect.Height / 2;
                    string evId = "ev" + i;
                    bool isGoogle = c.Source == "google" && !string.IsNullOrEmpty(c.Id);
                    if (!string.IsNullOrEmpty(c.Url) || isGoogle)
                    {
                        Hit(rowRect, evId);
                        bool evDown = Down(evId);
                        if (!string.IsNullOrEmpty(c.Url) && (hotId == evId || evDown))
                            p.FillRound(rowRect, 6, evDown ? p.T.FillPressed : p.T.FillHover);
                    }
                    bool isHoliday = c.Source == "holiday" || c.Kind == "libur";
                    string timeStr = isHoliday ? Lang.T("Libur") : (c.AllDay ? Lang.T("Seharian") : Fmt.Hm(c.Start));
                    Color timeColor = c.Cancelled ? p.T.Faint : (isHoliday ? p.T.Negative : p.T.Accent);
                    p.Text(timeStr, Ink.Small(11f), timeColor, Pad + 4, cy, 52, StringAlignment.Near);
                    string roomPart = !string.IsNullOrEmpty(c.Room) ? " · " + c.Room : "";
                    string titleStr = (c.Title ?? "") + roomPart;
                    Color titleColor = c.Cancelled ? p.T.Faint : p.T.Text;
                    float titleX = Pad + 4 + 56;
                    p.Text(titleStr, Ink.Small(11.5f), titleColor, titleX, cy, W - Pad - titleX, StringAlignment.Near);
                }
            }

            void DrawFoot()
            {
                float cy = H - Foot / 2;
                bool quiet = isle.QuietSwitch;
                var f = Ink.Small(12f);
                string label = Lang.T("Senyap");
                var b = new RectangleF(Pad - 4, cy - 15, p.Measure(label, f) + 46, 30);
                Hit(b, "quiet");
                bool qdown = Down("quiet");
                if (qdown) b.Offset(0, 0.75f);
                Color qfill = quiet ? (qdown ? KitInk.Deepen(p.T.Accent, p.T.OnAccent, 0.22f) : hotId == "quiet" ? KitInk.Deepen(p.T.Accent, p.T.OnAccent, 0.1f) : p.T.Accent)
                                    : (qdown ? p.T.FillPressed : hotId == "quiet" ? p.T.FillHover : p.T.Fill);
                p.FillRound(b, 15, qfill);
                float bcy = b.Y + b.Height / 2;
                p.Icon(Ink.IcoQuiet, 13f, quiet ? p.T.OnAccent : p.T.Text, b.X + 17, bcy);
                p.Text(label, f, quiet ? p.T.OnAccent : p.T.Text, b.X + 32, bcy, b.Width - 36, StringAlignment.Near);
                Button(new RectangleF(W - Pad - 38, cy - 19, 38, 38), "gear", (char)0xE713, Lang.T("Tanggal dan waktu"));
            }

            static int IndexOf(string id, char tag)
            {
                int i;
                return id != null && id.Length > 1 && id[0] == tag && int.TryParse(id.Substring(1), out i) ? i : -1;
            }

            protected override void Wheel(string id, int delta)
            {
                if (IndexOf(id, 'n') >= 0 || IndexOf(id, 'x') >= 0) first -= Math.Sign(delta);
                else month -= Math.Sign(delta);
            }

            void ShowEventMenu(ClassSlot slot)
            {
                if (slot == null || string.IsNullOrEmpty(slot.Id)) return;
                var m = new ContextMenuStrip();
                m.Items.Add(new ToolStripMenuItem(slot.Title ?? "") { Enabled = false });
                m.Items.Add(new ToolStripSeparator());
                if (!string.IsNullOrEmpty(slot.Url))
                {
                    string evUrl = slot.Url;
                    m.Items.Add(Lang.T("Buka tautan"), null, delegate
                    {
                        Done();
                        Island.OpenUrl(evUrl);
                    });
                }
                var editSlot = slot;
                m.Items.Add(Lang.T("Edit acara…"), null, delegate
                {
                    Done();
                    isle.BeginInvoke(new Action(delegate { isle.OpenEditGoogleEvent(editSlot); }));
                });
                string eventId = slot.Id;
                string evTitle = slot.Title ?? "";
                m.Items.Add(Lang.T("Hapus acara"), null, delegate
                {
                    isle.DeleteGoogleEventAsync(eventId, evTitle);
                });
                ShowMenu(m);
            }

            protected override void Clicked(string id, MouseButtons button)
            {
                if (id != null && id.Length > 2 && id[0] == 'e' && id[1] == 'v')
                {
                    int ev;
                    if (int.TryParse(id.Substring(2), out ev))
                    {
                        var dayEvents = EventsForDay(selectedDay ?? DateTime.Today);
                        if (ev >= 0 && ev < dayEvents.Count)
                        {
                            var slot = dayEvents[ev];
                            bool isGoogle = slot.Source == "google" && !string.IsNullOrEmpty(slot.Id);
                            if (button == MouseButtons.Right)
                            {
                                if (isGoogle) ShowEventMenu(slot);
                                return;
                            }
                            if (button == MouseButtons.Left && !string.IsNullOrEmpty(slot.Url))
                            {
                                string evUrl = slot.Url;
                                Done();
                                Island.OpenUrl(evUrl);
                            }
                        }
                    }
                    return;
                }
                if (button != MouseButtons.Left) return;
                var list = isle.notifs;
                int n = IndexOf(id, 'n'), x = IndexOf(id, 'x');
                if (n >= 0 && n < list.Count) { var item = list[n]; Done(); isle.OpenNotifApp(item); return; }
                if (x >= 0 && x < list.Count) { RemoveOneFx(x); return; }
                int d = IndexOf(id, 'd');
                if (d > 0)
                {
                    int dy = d / 10000, dm = (d / 100) % 100, dd = d % 100;
                    var clicked = new DateTime(dy, dm, dd);
                    selectedDay = clicked;
                    var currShown = new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1).AddMonths(month);
                    int mDiff = (clicked.Year - currShown.Year) * 12 + (clicked.Month - currShown.Month);
                    if (mDiff != 0) month += mDiff;
                    return;
                }
                switch (id)
                {
                    case "clear": ClearAllFx(); isle.RemoveNotifs(new List<NotifItem>(list)); break;
                    case "prev": month--; break;
                    case "next": month++; break;
                    case "today": month = 0; break;
                    case "quiet": isle.actQuiet(); break;
                    case "gear": Open("ms-settings:dateandtime", null); Done(); break;
                    case "addEvent": { DateTime picked = selectedDay ?? DateTime.Today; Done(); isle.BeginInvoke(new Action(delegate { isle.OpenNewGoogleEvent(picked); })); break; }
                }
            }
        }
    }
}
