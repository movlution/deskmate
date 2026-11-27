using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;

namespace Deskmate
{
    partial class Island
    {
        // Empty state backward compatibility wrapper delegating to Parts.EmptyState
        float Empty(float w, char glyph, string title, string detail)
        {
            var area = new RectangleF(0, PanelTop, w, PanelHeight() - PanelTop - 10);
            EmptyState(area, glyph, title, detail);
            return area.Y + Math.Max(8, (area.Height - 108) / 2) + 108;
        }

        // Backward compatibility wrapper delegating to Parts.Pill
        void Button(RectangleF r, string id, string label, bool filled, Action act)
        {
            Pill(r, id, label, filled ? PillKind.Primary : PillKind.Secondary, act);
        }

        // Music

        void DrawControl(RectangleF r, string id, Action act, Action<RectangleF, Color> glyph)
        {
            AddHit(r, id, act);
            if (Hover(id)) p.Circle(r.X + r.Width / 2, r.Y + r.Height / 2, r.Width / 2 - (Pressed(id) ? 1.5f : 0), Pressed(id) ? p.T.FillHover : p.T.Fill);
            if (Pressed(id)) r.Inflate(-1f, -1f);
            glyph(r, p.T.Text);
        }

        // Scrolling lyrics; current line sweeps
        void DrawTeleprompter(RectangleF box, DateTime now)
        {
            if (lyrics == null || lyrics.Key != mediaInfo.Key || lyrics.State == LyricState.Loading)
            {
                p.Orbit(box.X + 8, box.Y + 26, 6f, orbitPhase, p.T.Sub);
                p.Text(Lang.T("Mencari lirik…"), Ink.Text(12.5f), p.T.Sub, box.X + 22, box.Y + 26, box.Width - 22, StringAlignment.Near);
                return;
            }
            if (lyrics.State == LyricState.Plain) { DrawPlainLyrics(box); return; }
            if (lyrics.State != LyricState.Synced)
            {
                string msg = lyrics.State == LyricState.Failed ? Lang.T("Server lirik tidak terjangkau · dicoba lagi otomatis") : !settings.GetBool("lyrics.online", true) ? Lang.T("Pencarian lirik online dimatikan") : Lang.T("Lirik tidak ditemukan di LRCLIB maupun NetEase");
                p.TextBox(msg, Ink.Text(12.5f), p.T.Sub, new RectangleF(box.X, box.Y + 16, box.Width, 40));
                return;
            }
            // Seek targets where lines come to rest
            float anchor = box.Y + 34;
            // Right click shifts lyric timing
            if (!mediaInfo.CanSeek && lyrics.State == LyricState.Synced) AddHit(box, "lyBox", delegate { }, null, LyricMenu);
            if (mediaInfo.CanSeek)
                for (int i = Math.Max(0, (int)(lyricScroll.Target / LyricLineH) - 2); i < lyrics.Lines.Count; i++)
                {
                    float y = anchor + i * LyricLineH - lyricScroll.Target;
                    if (y > box.Bottom + LyricLineH) break;
                    if (lyrics.Lines[i].Text.Length == 0 || y <= box.Top || y >= box.Bottom) continue;
                    double at = lyrics.Lines[i].AtMs;
                    AddHit(new RectangleF(box.X, y - LyricLineH / 2, box.Width, LyricLineH), "ly" + i, delegate { media.Seek(at + 50); }, null, LyricMenu);
                }
            // List scrolls alone
            LiveRect(box, t => PaintTeleprompter(box, t));
        }

        double PaintTeleprompter(RectangleF box, DateTime now)
        {
            var st = g.Save();
            g.IntersectClip(box);
            double pos = mediaInfo.Position(now);
            float anchor = box.Y + 34;
            float alpha = p.Alpha, lit = Ease.OutCubic((float)(now - lyricSwitchUtc).TotalSeconds / 0.2f);
            int first = Math.Max(0, (int)(lyricScroll.Value / LyricLineH) - 2);
            for (int i = first; i < lyrics.Lines.Count; i++)
            {
                float d = i * LyricLineH - lyricScroll.Value;
                float y = anchor + d;
                if (y > box.Bottom + LyricLineH) break;
                if (y < box.Top - LyricLineH) continue;
                var line = lyrics.Lines[i];
                if (line.Text.Length == 0) continue;
                float edge = Ease.Clamp01((y - box.Top - 4) / 24f) * Ease.Clamp01((box.Bottom - y - 4) / 24f);
                p.Alpha = alpha * edge;
                if (i == lyricIndex)
                {
                    // Long lines shrink to fit
                    var big = Ink.Display(15.5f);
                    float mw = p.Measure(line.Text, big);
                    var font = mw <= box.Width ? big : Ink.Display(Math.Max(12f, (float)Math.Floor(15.5f * box.Width / mw * 2) / 2));
                    p.Text(line.Text, font, Ink.Mix(p.T.Sub, p.T.Text, Ease.Clamp01(lit)), box.X, y, box.Width, StringAlignment.Near);
                }
                else p.Text(line.Text, Ink.Text(13.5f), i < lyricIndex ? p.T.Faint : p.T.Sub, box.X, y, box.Width, StringAlignment.Near);
            }
            p.Alpha = alpha;
            g.Restore(st);
            // 60 fps while moving, else next line
            if (!lyricScroll.Settled || lit < 1) return 1 / 60.0;
            if (!mediaInfo.Playing) return 0;
            double next = lyricIndex >= 0 ? lyrics.NextAt(lyricIndex) : lyrics.Lines.Count > 0 ? lyrics.Lines[0].AtMs : double.MaxValue;
            return next == double.MaxValue ? 0 : Math.Max(0.01, (next - pos) / 1000 + 0.005);
        }

        readonly Spring plainScroll = new Spring(0, 300, 34, 0.2f);
        string plainKey;
        const float PlainLineH = 22f;

        // Untimed lyrics, wheel-scrolled
        void DrawPlainLyrics(RectangleF box)
        {
            if (plainKey != lyrics.Key) { plainKey = lyrics.Key; plainScroll.Snap(0); }
            p.Text(Lang.T("Lirik tanpa waktu"),Ink.Small(11f), p.T.Faint, box.X, box.Y + 8, box.Width, StringAlignment.Near);
            var area = new RectangleF(box.X, box.Y + 20, box.Width, box.Height - 20);
            var st = g.Save();
            g.IntersectClip(area);
            float alpha = p.Alpha;
            for (int i = 0; i < lyrics.Lines.Count; i++)
            {
                float y = area.Y + 12 + i * PlainLineH - plainScroll.Value;
                if (y < area.Top - PlainLineH) continue;
                if (y > area.Bottom + PlainLineH) break;
                float edge = Ease.Clamp01((y - area.Top) / 18f) * Ease.Clamp01((area.Bottom - y) / 18f);
                p.Alpha = alpha * edge;
                p.Text(lyrics.Lines[i].Text, Ink.Text(13f), p.T.Sub, area.X, y, area.Width, StringAlignment.Near);
            }
            p.Alpha = alpha;
            g.Restore(st);
        }

        // Agents

        // Session open to its checklist
        string planOpen;
        // Row lit after a jump from a toast
        string agentFocus;
        DateTime agentFocusUtc = DateTime.MinValue;

        // Agent tab, scrolled to this session, plan open
        void ShowAgent(AgentSession s)
        {
            Show(PageAgents);
            if (s == null) return;
            if (s.Plan != null && s.Plan.Count > 0) planOpen = s.Id;
            float y = 0;
            foreach (var x in SortedSessions()) { if (x == s) break; y += AgentRowH(x); }
            float view = AgentsHeight() - PanelTop - 14;
            y += AgentSessionOffset();
            agentScroll.Snap(Clamp(y, 0, Math.Max(0, AgentsPageContentH() - view)));
            agentFocus = s.Id;
            agentFocusUtc = DateTime.UtcNow;
            Kick();
        }

        float AgentRowH(AgentSession s)
        {
            float h = 56;
            if (s.Plan != null && s.Plan.Count > 0) h += 12;
            if (planOpen == s.Id && s.Plan != null) h += PlanListH(s);
            return h;
        }

        float AgentsContentH()
        {
            float h = 0;
            foreach (var s in sessions.Values) h += AgentRowH(s);
            return h;
        }

        const float AgentHookH = 44, AgentEmptyH = 110;

        bool AgentHookFailed { get { return hub != null && hub.Error != null; } }
        float AgentSessionOffset() { return QuotaBlockH() + (AgentHookFailed ? AgentHookH : 0); }
        float AgentsPageContentH() { return AgentSessionOffset() + (sessions.Count == 0 ? AgentEmptyH : AgentsContentH()); }

        void DrawAgentsPage(float w, float h, DateTime now)
        {
            var area = new RectangleF(10, PanelTop + 4, w - 20, Math.Max(0, h - PanelTop - 14));
            if (area.Height <= 0) return;
            var st = g.Save();
            g.IntersectClip(area);
            float maxScroll = Math.Max(0, AgentsPageContentH() - (AgentsHeight() - PanelTop - 14));
            float y = area.Y - Clamp(agentScroll.Value, 0, maxScroll);
            y += DrawQuotaBlock(w, y);
            if (AgentHookFailed)
            {
                p.Text(Lang.T("Server hook mati"), Ink.SmallSemi(12), p.T.Warning, 26, y + 12, w - 52, StringAlignment.Near);
                p.Text(hub.Error, Ink.Caption, p.T.Sub, 26, y + 29, w - 52, StringAlignment.Near);
                y += AgentHookH;
            }
            var list = SortedSessions();
            if (list.Count == 0)
            {
                EmptyState(new RectangleF(0, y, w, AgentEmptyH), Ink.IcoTerminal, Lang.T("Belum ada sesi agen"), linked.Length > 0 ? Lang.T("Terhubung: ") + linked : Lang.T("Belum terhubung · tray > Hubungkan agen"));
            }
            for (int i = 0; i < list.Count; i++)
            {
                var s = list[i];
                float rowH = AgentRowH(s), dy, a = RowIn(i, now, out dy);
                float top = y + dy;
                y += rowH;
                if (top + rowH < area.Top || top > area.Bottom) continue;
                float keep = p.Alpha;
                p.Alpha = keep * a;
                DrawAgentRow(s, w, top, rowH, i < list.Count - 1);
                p.Alpha = keep;
            }
            g.Restore(st);
        }

        void DrawAgentRow(AgentSession s, float w, float y, float rowH, bool divider)
        {
            float cy = y + 28;
            var row = new RectangleF(14, y + 3, w - 28, rowH - 6);
            string rowId = "agent" + s.Id;
            var session = s;
            AddHit(row, rowId, delegate { if (!FocusTerminal(session)) ShowInfo(Ink.IcoTerminal, Lang.T("Terminal tidak ditemukan"), session.Project, true); });
            if (Hover(rowId)) p.FillRound(row, Ink.RadiusControl, Pressed(rowId) ? p.T.FillPressed : p.T.FillHover);
            // Just jumped here: an accent wash that fades
            float lit = s.Id == agentFocus ? 1 - (float)((DateTime.UtcNow - agentFocusUtc).TotalSeconds / 1.6) : 0;
            if (lit > 0) p.FillRound(row, Ink.RadiusControl, Ink.A(p.T.Accent, 0.16f * lit));
            DrawStateMark(s, 36, cy);
            var semi = Ink.Headline;
            p.Text(s.Project, semi, p.T.Text, 60, cy - 10, 220, StringAlignment.Near);
            float px = 60 + Math.Min(220, p.Measure(s.Project, semi)) + 8;
            p.Text(AgentTag(s), Ink.Caption, p.T.Sub, px, cy - 9.5f, 160, StringAlignment.Near);
            // Hover offers prompt box for live sessions
            string sendId = "send" + s.Id;
            if ((Hover(rowId) || Hover(sendId)) && s.State != AgentState.Ended && s.Window != IntPtr.Zero)
            {
                var send = new RectangleF(w - 26 - 58, cy - 21, 58, 22);
                AddHit(send, sendId, delegate { AskAgent(session); });
                p.FillRound(send, 11, Hover(sendId) ? p.T.Text : p.T.Accent);
                p.TextCenter(Lang.T("Chat"), Ink.Caption, Hover(sendId) ? p.T.Bg : p.T.OnAccent, send.X + send.Width / 2, send.Y + 11);
            }
            else
            {
                // Working time ticks alone, once a second
                LiveRect(new RectangleF(w - 186, cy - 18, 160, 16), s.State == AgentState.Working ? TickFps : 0, () => StateWord(session).GetHashCode(),
                         t => p.TextRight(StateWord(session), Ink.Caption, p.T.Sub, w - 26, cy - 10));
            }
            string meta = Summary(s, DateTime.UtcNow);
            var mf = Ink.Caption;
            float lineW = w - 60 - 26 - p.Measure(meta, mf) - 14;
            p.TextRight(meta, mf, p.T.Faint, w - 26, cy + 10);
            string now = PlanNow(s);
            if ((s.State == AgentState.Done || s.State == AgentState.Ended) && s.LastMessage.Length > 0)
                p.Text(s.LastMessage, Ink.Text(12f), p.T.Sub, 60, cy + 10, lineW, StringAlignment.Near);
            else if (now.Length > 0)
                p.Text(now, Ink.Text(12f), p.T.Sub, 60, cy + 10, lineW, StringAlignment.Near);
            else
                p.Text(AgentLine(s), Ink.Mono(11.5f), p.T.Sub, 60, cy + 10, lineW, StringAlignment.Near);
            if (s.Plan != null && s.Plan.Count > 0)
            {
                // Plan: segments, count, list toggle
                var bar = new RectangleF(60, cy + 24, w - 60 - 26 - 64, 4);
                DrawPlanBar(s, bar);
                string toggle = "plan" + s.Id;
                var chip = new RectangleF(w - 26 - 56, cy + 17, 56, 18);
                AddHit(chip, toggle, delegate { planOpen = planOpen == session.Id ? null : session.Id; Kick(); });
                p.TextRight(PlanCount(s) + (planOpen == s.Id ? "  ▴" : "  ▾"), Ink.Semi(11f), Hover(toggle) ? p.T.Text : p.T.Sub, w - 26, cy + 26);
                if (planOpen == s.Id) DrawPlanList(s, 60, cy + 38, w - 60 - 26);
            }
            if (divider) g.DrawLine(p.P(p.T.Line, 1f), 60, y + rowH - 0.5f, w - 26, y + rowH - 0.5f);
        }

        void DrawStateMark(AgentSession s, float cx, float cy)
        {
            switch (s.State)
            {
                case AgentState.Working: LiveRect(new RectangleF(cx - 10, cy - 10, 20, 20), LiveFps, t => p.Orbit(cx, cy, 8f, orbitPhase, p.T.Text)); break;
                case AgentState.Waiting: p.Icon(Ink.IcoTerminal, 14f, p.T.Text, cx, cy); break;
                case AgentState.Done:
                    p.Circle(cx, cy, 10, p.T.Text);
                    if (s.Failed) p.Cross(cx, cy, 9, p.T.Bg);
                    else p.Check(cx, cy, 10, p.T.Bg, 1);
                    break;
                case AgentState.Ready: g.DrawEllipse(p.P(p.T.Sub, 1.6f), cx - 8, cy - 8, 16, 16); break;
                default: g.DrawLine(p.P(p.T.Faint, 2f), cx - 6, cy, cx + 6, cy); break;
            }
        }

        static string StateWord(AgentSession s)
        {
            switch (s.State)
            {
                case AgentState.Working: return Lang.T("bekerja · ") + Duration(DateTime.UtcNow - (s.TurnStartedUtc > DateTime.MinValue ? s.TurnStartedUtc : s.StartedUtc));
                case AgentState.Waiting: return Lang.T("menunggu izin");
                case AgentState.Done: return Lang.T("selesai · ") + Fmt.Ago(s.LastUtc);
                case AgentState.Ready: return Lang.T("siap");
            }
            return Lang.T("ditutup · ") + Fmt.Ago(s.LastUtc);
        }

        // Work › Tasks

        const float TaskBarH = 34;
        // 0: all, 1: today/overdue, 2: with due date
        int taskFilter;

        List<KuliaTask> FilteredTasks()
        {
            if (tasks == null || tasks.Count == 0) return new List<KuliaTask>();
            if (taskFilter == 0) return tasks;
            var today = DateTime.Now.Date;
            var list = new List<KuliaTask>();
            for (int i = 0; i < tasks.Count; i++)
            {
                var t = tasks[i];
                bool hasDue = !t.NoDue && t.DeadlineUtc != DateTime.MinValue;
                if (taskFilter == 2 && hasDue) list.Add(t);
                else if (taskFilter == 1 && hasDue && t.DeadlineUtc.ToLocalTime().Date <= today) list.Add(t);
            }
            return list;
        }

        void FocusTaskFromWork(KuliaTask task)
        {
            if (task == null) return;
            SetFocusTask(task.Title);
            if (!TimerOn) StartFocus(25);
            SetSegment(PageClass, 1, false);
        }

        void DrawTasksPage(float w, float h, DateTime now)
        {
            var state = TaskState();
            if (state == DataState.Off)
            {
                NothingConnected(w, "linkTasks", true);
                return;
            }
            if (state == DataState.Failed)
            {
                EmptyState(new RectangleF(0, PanelTop, w, h - PanelTop), Ink.IcoCalendar, Lang.T("Tugas gagal dimuat"), taskNote, Lang.T("Coba lagi"), "retry", delegate { RefreshAll(1); }, PillKind.Secondary);
                return;
            }
            if (state == DataState.Loading)
            {
                float cy = PanelTop + (h - PanelTop) / 2 - 6;
                // Spinner and line centred as one
                string loading = OnlyKuliaTasks ? Lang.T("Memuat tugas dari Kulia…") : Lang.T("Memuat tugas…");
                float lx = w / 2 - (p.Measure(loading, Ink.Body) + 24) / 2;
                p.Orbit(lx + 7, cy, 7f, orbitPhase, p.T.Sub);
                p.Text(loading, Ink.Body, p.T.Sub, lx + 24, cy, w - lx - 40, StringAlignment.Near);
                return;
            }
            if (state == DataState.Empty)
            {
                EmptyState(new RectangleF(0, PanelTop, w, h - PanelTop), Ink.IcoCalendar, Lang.T("Tidak ada tugas aktif"), taskNote ?? (OnlyKuliaTasks ? Lang.T("Diperbarui tiap 10 menit") : Lang.T("Diperbarui tiap 15 menit")), Lang.T("+ Tugas"), "addTaskEmpty", delegate { OpenWork(0); }, PillKind.Secondary);
                return;
            }

            // Compact productivity toolbar: count + today cue, filter chip, + Task
            float barCy = PanelTop + TaskBarH / 2;
            var todayDate = DateTime.Now.Date;
            int dueToday = 0;
            for (int i = 0; i < tasks.Count; i++)
            {
                var tt = tasks[i];
                if (!tt.NoDue && tt.DeadlineUtc != DateTime.MinValue && tt.DeadlineUtc.ToLocalTime().Date <= todayDate) dueToday++;
            }
            string countText = tasks.Count == 1 ? Lang.T("1 tugas") : Lang.F("{0} tugas", tasks.Count);
            float countW = p.Measure(countText, Ink.Semi(11.5f));
            p.Text(countText, Ink.Semi(11.5f), p.T.Sub, 22, barCy, countW + 8, StringAlignment.Near);
            if (dueToday > 0)
            {
                string tdText = "· " + dueToday + " " + Lang.T("hari ini");
                p.Text(tdText, Ink.Semi(11.5f), p.T.Accent, 22 + countW + 6, barCy, 130, StringAlignment.Near);
            }

            string addLbl = Lang.T("+ Tugas");
            float addW = Math.Max(68, p.Measure(addLbl, Ink.Semi(11.5f)) + 20);
            var addR = new RectangleF(w - 16 - addW, barCy - 12, addW, 24);
            AddHit(addR, "workAddTask", delegate { OpenWork(0); });
            float addSq = Squish("workAddTask", addR, 0.94f);
            p.FillRound(addR, 12, Pressed("workAddTask") ? p.T.FillPressed : Hover("workAddTask") ? p.T.FillHover : p.T.Fill);
            p.StrokeRound(addR, 12, p.T.Line, 1f);
            p.TextCenter(addLbl, Ink.Semi(11.5f), Hover("workAddTask") ? p.T.Text : p.T.Sub, addR.X + addW / 2, barCy);
            Unsquish(addR, addSq);

            string fltLbl = taskFilter == 0 ? Lang.T("Semua") : taskFilter == 1 ? Lang.T("Hari ini") : Lang.T("Tenggat");
            float fltW = Math.Max(64, p.Measure(fltLbl, Ink.Semi(11.5f)) + 22);
            var fltR = new RectangleF(addR.X - 6 - fltW, barCy - 12, fltW, 24);
            AddHit(fltR, "workTaskFilter", delegate { taskFilter = (taskFilter + 1) % 3; taskScroll.Snap(0); Kick(); });
            float fltSq = Squish("workTaskFilter", fltR, 0.94f);
            bool fltOn = taskFilter != 0;
            p.FillRound(fltR, 12, fltOn ? Ink.A(p.T.Accent, Hover("workTaskFilter") ? 0.24f : 0.16f) : Pressed("workTaskFilter") ? p.T.FillPressed : Hover("workTaskFilter") ? p.T.FillHover : p.T.Fill);
            p.StrokeRound(fltR, 12, fltOn ? Ink.A(p.T.Accent, 0.45f) : p.T.Line, 1f);
            p.TextCenter(fltLbl, Ink.Semi(11.5f), fltOn ? p.T.Accent : Hover("workTaskFilter") ? p.T.Text : p.T.Sub, fltR.X + fltW / 2, barCy);
            Unsquish(fltR, fltSq);

            var shown = FilteredTasks();
            if (shown.Count == 0)
            {
                EmptyState(new RectangleF(0, PanelTop + TaskBarH, w, h - PanelTop - TaskBarH), Ink.IcoCalendar, Lang.T("Tidak ada tugas untuk filter ini"), "", Lang.T("Tampilkan semua"), "resetTaskFilter", delegate { taskFilter = 0; taskScroll.Snap(0); Kick(); }, PillKind.Secondary);
                return;
            }

            // Partial failure: one quiet line below
            float foot = TaskFoot();
            if (foot > 0) p.Text(taskNote, Ink.Caption, p.T.Sub, 26, h - 18, w - 52, StringAlignment.Near);

            const float rowH = 54;
            var area = new RectangleF(10, PanelTop + TaskBarH + 2, w - 20, h - PanelTop - TaskBarH - 12 - foot);
            var st = g.Save();
            g.IntersectClip(area);
            var nowUtc = DateTime.UtcNow;
            string curFocus = FocusTaskTitle;
            float keep = p.Alpha;
            for (int i = 0; i < shown.Count; i++)
            {
                var t = shown[i];
                float dy, a = RowIn(i, nowUtc, out dy);
                float y = area.Y + i * rowH - taskScroll.Value + CollapseAt("task", i) + dy;
                if (y + rowH < area.Top || y > area.Bottom) continue;
                p.Alpha = keep * a;
                var row = new RectangleF(14, y + 2, w - 28, rowH - 4);
                string rowId = "task" + t.Id, doneId = "taskDone" + t.Id, focusId = "taskFocus" + t.Id, checkId = "taskChk" + t.Id;
                bool isFocused = curFocus.Length > 0 && string.Equals(curFocus, t.Title, StringComparison.OrdinalIgnoreCase);
                bool hover = Hover(rowId) || Hover(doneId) || Hover(focusId) || Hover(checkId);
                if (hover) p.FillRound(row, Ink.RadiusControl, p.T.Fill);
                else if (isFocused) p.FillRound(row, Ink.RadiusControl, Ink.A(p.T.Accent, 0.08f));
                if (isFocused) p.FillRound(new RectangleF(row.X + 2, row.Y + 11, 3, row.Height - 22), 1.5f, p.T.Accent);

                float cy = row.Y + row.Height / 2;
                bool hasDue = !t.NoDue && t.DeadlineUtc != DateTime.MinValue;
                bool overdue = hasDue && (t.DeadlineUtc - nowUtc).TotalMinutes < 0;
                bool urgent = hasDue && (t.DeadlineUtc - nowUtc).TotalHours < 24;
                if (hasDue)
                {
                    var local = t.DeadlineUtc.ToLocalTime();
                    Color dayColor = overdue ? p.T.Negative : local.Date == todayDate ? p.T.Accent : p.T.Text;
                    p.TextCenter(local.Day.ToString(), Ink.Title2, dayColor, 44, cy - 8);
                    p.TextCenter(local.ToString("MMM", Fmt.Id), Ink.Caption, overdue ? p.T.Negative : p.T.Sub, 44, cy + 10);
                }
                else
                {
                    var chkR = new RectangleF(33, cy - 11, 22, 22);
                    bool chkHot = Hover(checkId);
                    float chkSq = Squish(checkId, chkR, 0.9f);
                    if (chkHot && !t.ReadOnly)
                    {
                        p.FillRound(chkR, 11, Ink.A(p.T.Accent, 0.18f));
                        p.StrokeRound(chkR, 11, p.T.Accent, 1.5f);
                        p.Icon('\uE73E', 10.5f, p.T.Accent, 44, cy);
                    }
                    else
                    {
                        p.StrokeRound(chkR, 11, isFocused ? p.T.Accent : p.T.Line, 1.4f);
                        if (isFocused) p.Circle(44, cy, 3.5f, p.T.Accent);
                    }
                    Unsquish(chkR, chkSq);
                }

                float tx = 74, right = row.Right - 12;
                string when = hasDue ? Relative(t, nowUtc) : "";
                var wf = Ink.Caption;
                float ww = when.Length > 0 ? p.Measure(when, wf) + (urgent ? 18 : 0) : 0;
                var doneBtn = new RectangleF(right - 84, cy - 14, 84, 28);
                var focusBtn = new RectangleF((t.ReadOnly ? right : doneBtn.X - 6) - 30, cy - 14, 30, 28);

                if (hover)
                {
                    // Quick-focus button: starts Pomodoro on this task
                    float fsq = Squish(focusId, focusBtn, 0.92f);
                    bool fHot = Hover(focusId);
                    p.FillRound(focusBtn, 9, fHot ? p.T.Accent : p.T.FillHover);
                    p.Icon('\uE768', 10.5f, fHot ? p.T.OnAccent : isFocused ? p.T.Accent : p.T.Text, focusBtn.X + focusBtn.Width / 2 + 1, cy);
                    Unsquish(focusBtn, fsq);

                    if (!t.ReadOnly)
                    {
                        float dsq = Squish(doneId, doneBtn, 0.94f);
                        bool dHot = Hover(doneId);
                        p.FillRound(doneBtn, 9, dHot ? p.T.Positive : p.T.FillHover);
                        if (!dHot) p.StrokeRound(doneBtn, 9, p.T.Line, 1f);
                        p.TextCenter(Lang.T("Selesai"), Ink.Semi(11.5f), dHot ? p.T.Bg : p.T.Text, doneBtn.X + doneBtn.Width / 2, cy);
                        Unsquish(doneBtn, dsq);
                        ww = 84 + 6 + 30;
                    }
                    else ww = 30;
                }
                else if (overdue)
                {
                    var pill = new RectangleF(right - ww, cy - 11, ww, 22);
                    p.FillRound(pill, 11, Ink.A(p.T.Negative, 0.22f));
                    p.TextCenter(when, wf, p.T.Negative, pill.X + pill.Width / 2, cy);
                }
                else if (urgent)
                {
                    var pill = new RectangleF(right - ww, cy - 11, ww, 22);
                    p.FillRound(pill, 11, p.T.Accent);
                    p.TextCenter(when, wf, p.T.OnAccent, pill.X + pill.Width / 2, cy);
                }
                else if (when.Length > 0) p.TextRight(when, wf, p.T.Sub, right, cy);

                string course = t.Course + (t.Kind == "ujian" ? Lang.T(" · ujian") : "") + (t.Status == "proses" ? Lang.T(" · dikerjakan") : "");
                string from = SourceHub.TaskLabel(t.Source);
                if (from.Length > 0 && from != t.Course) course = course.Length > 0 ? from + " · " + course : from;
                p.Text(course, Ink.Caption, isFocused ? p.T.Accent : p.T.Sub, tx, cy - 10, right - ww - tx - 12, StringAlignment.Near);
                p.Text(t.Title, Ink.Headline, p.T.Text, tx, cy + 9, right - ww - tx - 12, StringAlignment.Near);

                string url = t.Url;
                bool isGtasks = t.Source == "gtasks";
                var task = t;
                int idx = i;
                var taskRow = row;
                AddHit(row, rowId, delegate { if (isGtasks) OpenWork(0); else OpenUrl(url); });
                AddHit(focusBtn, focusId, delegate { FocusTaskFromWork(task); });
                if (!t.ReadOnly)
                {
                    Action completeAct = delegate
                    {
                        Vanish(new[] { CanvasRect(taskRow) }, false);
                        Collapse("task", idx, rowH);
                        MarkDone(task);
                    };
                    AddHit(doneBtn, doneId, completeAct);
                    if (!hasDue) AddHit(new RectangleF(28, cy - 16, 32, 32), checkId, completeAct);
                }
            }
            p.Alpha = keep;
            g.Restore(st);
        }

        static string Relative(DateTime dueUtc, DateTime nowUtc)
        {
            var left = dueUtc - nowUtc;
            var local = dueUtc.ToLocalTime();
            if (left.TotalMinutes < 0) return Lang.F("lewat {0}", Fmt.AgoShort(dueUtc));
            if (left.TotalMinutes < 60) return (int)Math.Max(1, left.TotalMinutes) + Lang.T(" menit lagi");
            if (left.TotalHours < 12) return (int)left.TotalHours + Lang.T(" jam lagi");
            var today = DateTime.Now.Date;
            if (local.Date == today) return Lang.T("hari ini ") + Fmt.Hm(local);
            if (local.Date == today.AddDays(1)) return Lang.T("besok ") + Fmt.Hm(local);
            if ((local.Date - today).TotalDays < 7) return local.ToString("ddd", Fmt.Id) + " " + Fmt.Hm(local);
            return local.ToString("d MMM", Fmt.Id);
        }

        // Messages

        void DrawMessagesPage(float w, float h, DateTime now)
        {
            if (!NotifOn)
            {
                EmptyState(new RectangleF(0, PanelTop, w, h - PanelTop), Ink.IcoBell, Lang.T("Notifikasi pesan dimatikan"), Lang.T("Saklarnya ada di menu klik kanan"));
                return;
            }
            if (notifAccess != Windows.UI.Notifications.Management.UserNotificationListenerAccessStatus.Allowed)
            {
                EmptyState(new RectangleF(0, PanelTop, w, h - PanelTop), Ink.IcoLock, Lang.T("Akses notifikasi ditolak"), Lang.T("Pengaturan Windows > Privasi > Notifikasi"), Lang.T("Buka pengaturan"), "notifPerm", delegate
                {
                    try { Process.Start("ms-settings:privacy-notifications"); } catch (Exception ex) { Log.Write("settings: " + ex.Message); }
                }, PillKind.Secondary);
                return;
            }
            if (notifs.Count == 0)
            {
                // A read failure is not an empty inbox; never claim "no messages" for it
                if (notifWatcher.Failing)
                {
                    EmptyState(new RectangleF(0, PanelTop, w, h - PanelTop), Ink.IcoBell, Lang.T("Pesan tidak bisa dibaca"), Lang.T("Notifikasi Windows tidak menjawab. Deskmate mencoba lagi otomatis."), Lang.T("Coba lagi"), "notifRetry", delegate { notifWatcher.Refresh(); }, PillKind.Secondary);
                    return;
                }
                EmptyState(new RectangleF(0, PanelTop, w, h - PanelTop), Ink.IcoBell, Lang.T("Belum ada pesan"), Lang.T("WhatsApp, Telegram, Discord, Teams, email"));
                return;
            }
            const float rowH = 50;
            var area = new RectangleF(10, PanelTop + 2, w - 20, h - PanelTop - 12);
            var st = g.Save();
            g.IntersectClip(area);
            float keep = p.Alpha;
            for (int i = 0; i < notifs.Count; i++)
            {
                var item = notifs[i];
                float dy, a = RowIn(i, now, out dy);
                float y = area.Y + i * rowH - notifScroll.Value + CollapseAt("notif", i) + dy;
                if (y + rowH < area.Top || y > area.Bottom) continue;
                p.Alpha = keep * a;
                var row = new RectangleF(14, y + 2, w - 28, rowH - 4);
                string id = "notif" + item.Id, del = "notifDel" + item.Id;
                bool hover = Hover(id) || Hover(del);
                if (hover) p.FillRound(row, Ink.RadiusControl, p.T.Fill);
                float cy = row.Y + row.Height / 2;
                DrawAppBadge(item, new RectangleF(row.X + 8, cy - 16, 32, 32));
                float tx = row.X + 50;
                p.NotifText(NotifTitle(item), Ink.Callout, p.T.Text, tx, cy - 9, row.Right - tx - 80, StringAlignment.Near);
                p.NotifText(NotifBody(item), Ink.Caption, p.T.Sub, tx, cy + 9, row.Right - tx - 44, StringAlignment.Near);
                var x = new RectangleF(row.Right - 36, cy - 14, 28, 28);
                if (hover)
                {
                    p.Circle(x.X + 14, x.Y + 14, 12, Hover(del) ? p.T.FillHover : p.T.Fill);
                    p.Cross(x.X + 14, x.Y + 14, 10, p.T.Sub);
                }
                else p.TextRight(Fmt.Ago(item.Time), Ink.Caption, p.T.Sub, row.Right - 12, cy - 9);
                var open = item;
                var appMenu = NotifAppMenuAction(item);
                AddHit(RectangleF.Intersect(row, area), id, delegate { OpenNotifApp(open); }, null, appMenu);
                // Added last: wins over the row
                var victim = item;
                int idx = i;
                var victimRow = row;
                AddHit(RectangleF.Intersect(x, area), del, delegate
                {
                    Vanish(new[] { CanvasRect(victimRow) }, false);
                    Collapse("notif", idx, rowH);
                    RemoveNotifs(new List<NotifItem> { victim });
                }, null, appMenu);
            }
            p.Alpha = keep;
            g.Restore(st);
        }
    }
}
