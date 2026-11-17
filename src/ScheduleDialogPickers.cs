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
        sealed partial class GoogleEventDialog
        {
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
        }
    }
}
