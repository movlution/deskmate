using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;

namespace Deskmate
{
    // Panel structure: tabs, segments, heights, scrolling
    partial class Island
    {
        const int PageHome = 0, PageMusic = 1, PageAgents = 2, PageClass = 3, PageMessages = 4, PageShelf = 5, PageControl = 6, PageRemind = 7, PagePhone = 8;
        // Ids for settings; names for labels
        static readonly string[] PageIds = { "home", "music", "agents", "class", "messages", "shelf", "control", "remind", "phone", "convert", "suara" };
        static readonly string[] PageNames = { "Ringkasan", "Musik", "Agen", "Kerja", "Pesan", "Rak", "Kontrol", "Pengingat", "HP", "Ubah", "Bicara" };
        // HP joins once a phone is linked
        const string DefaultTabs = "home,music,agents,class,messages,shelf,control,remind,convert,suara";
        const float PanelTop = 54, SegTop = 36;
        float panelW = 560;
        float PanelW { get { return panelW; } }
        int[] tabs = { 0, 1, 2, 3, 4, 5, 6, 7 };
        // Segments: Kerja tasks/schedule/pomodoro, Rak files/clipboard
        int classSeg, shelfSeg;
        // Hit offset under a segment bar
        float hitDy;

        // Width, shift and tab order from settings
        void LoadLayout()
        {
            panelW = Clamp(settings.GetInt("panel.width", 560), 440, 640);
            shiftPct = Math.Max(-ShiftMax, Math.Min(ShiftMax, settings.GetInt("position", 0)));
            MigrateTabs();
            AddConvertTab();
            AddVoiceTab();
            var list = new List<int>();
            foreach (var part in settings.Get("panel.tabs", DefaultTabs).Split(','))
            {
                int v = Array.IndexOf(PageIds, part.Trim());
                if (v >= 0 && !list.Contains(v)) list.Add(v);
            }
            if (list.Count == 0) list.Add(PageHome);
            tabs = list.ToArray();
            LoadHome();
            LoadPlace();
        }

        // 1.x: 3 tasks, 5 schedule, 6 clipboard
        void MigrateTabs()
        {
            if (settings.Has("panel.tabs") || settings.GetInt("settings.v", 1) >= 2) return;
            string[] old = { "home", "music", "agents", "class", "messages", "class", "shelf" };
            if (settings.Has("tabs"))
            {
                var ids = new List<string>();
                foreach (var part in settings.Get("tabs", "").Split(','))
                {
                    int v;
                    if (int.TryParse(part.Trim(), out v) && v >= 0 && v < old.Length && !ids.Contains(old[v])) ids.Add(old[v]);
                }
                foreach (var add in new[] { "shelf", "control", "remind" }) if (!ids.Contains(add)) ids.Add(add);
                settings.Set("panel.tabs", string.Join(",", ids.ToArray()));
            }
            int first = settings.GetInt("panel.page", -1);
            if (first >= 0 && first < old.Length) settings.Set("panel.first", old[first]);
            settings.Set("settings.v", 2);
            settings.Save();
        }

        string TabIds(IEnumerable<int> pages)
        {
            var ids = new List<string>();
            foreach (int pg in pages) if (pg >= 0 && pg < PageIds.Length) ids.Add(PageIds[pg]);
            return string.Join(",", ids.ToArray());
        }

        DateTime panelOpenedUtc, pageSwitchUtc, segSwitchUtc;
        const float PageTransitionSeconds = 0.23f, SegmentTransitionSeconds = 0.20f, RowTransitionSeconds = 0.16f;
        const int RowStaggerLimit = 4;
        const float RowStaggerSeconds = 0.014f;
        int prevPage, prevSeg;
        readonly Spring agentScroll = new Spring(0, 300, 34, 0.2f), taskScroll = new Spring(0, 300, 34, 0.2f), notifScroll = new Spring(0, 300, 34, 0.2f), controlScroll = new Spring(0, 300, 34, 0.2f);
        readonly Spring classSegX = new Spring(0, 460, 34, 0.004f), shelfSegX = new Spring(0, 460, 34, 0.004f);

        // Deep links into segments
        void ShowTasks() { SetSegment(PageClass, 0, true); Show(PageClass); }
        void ShowPomodoro() { SetSegment(PageClass, 1, true); Show(PageClass); }
        void ExpandPomodoro() { SetSegment(PageClass, 1, true); Expand(PageClass); }
        void ShowSchedule() { SetSegment(PageClass, 2, true); Show(PageClass); }
        void ShowClips() { SetSegment(PageShelf, 1, true); Show(PageShelf); }
        void Show(int pg) { if (expanded) SetPage(pg, false); else Expand(pg); }

        bool SegmentPage(int pg) { return pg == PageClass || pg == PageShelf; }

        // Height follows each tab's content
        float PanelHeight() { return PageHeight(page); }

        float PageHeight(int pg)
        {
            switch (pg)
            {
                case PageHome: return HomeHeight();
                case PageMusic: return MusicHeight();
                case PageAgents: return AgentsHeight();
                case PageClass: return Math.Min(MaxH, SegTop + (classSeg == 0 ? TasksHeight() : classSeg == 1 ? PomodoroHeight() : ScheduleHeight()));
                case PageShelf: return Math.Min(MaxH, SegTop + (shelfSeg == 0 ? ShelfHeight() : ClipGalleryH));
                case PageControl: return ControlHeight();
                case PageRemind: return RemindHeight();
                case PagePhone: return PhoneHeight();
                case PageConvert: return ConvertPageHeight();
                case PageVoice: return VoicePageHeight();
                default: return MessagesHeight();
            }
        }

        float MusicHeight() { return mediaInfo.Present && mediaInfo.Title.Length > 0 ? 340 : 214; }

        float AgentsHeight()
        {
            return Clamp(PanelTop + 14 + AgentsPageContentH(), 150, 360);
        }

        float TasksHeight()
        {
            var state = TaskState();
            // Room for the button
            if (state == DataState.Off || state == DataState.Failed) return 252;
            if (state != DataState.Ready) return 214;
            int n = FilteredTasks().Count;
            if (n == 0) return 214;
            return Clamp(PanelTop + TaskBarH + 14 + n * 54 + TaskFoot(), 170, 372);
        }

        float MessagesHeight()
        {
            if (!NotifOn || notifs.Count == 0) return 214;
            if (notifAccess != Windows.UI.Notifications.Management.UserNotificationListenerAccessStatus.Allowed) return 252;
            return Clamp(PanelTop + 14 + notifs.Count * 50, 150, 372);
        }

        int DefaultPage(bool hover)
        {
            int want = Array.IndexOf(PageIds, settings.Get("panel.first", ""));
            return Array.IndexOf(tabs, want) >= 0 ? want : tabs[0];
        }

        void SetPage(int pg, bool snap)
        {
            if (pg != page)
            {
                if (page == PageControl) StopControlScans();
                prevPage = page;
                page = pg;
                pageSwitchUtc = DateTime.UtcNow;
            }
            else if (pg != PageControl)
            {
                StopControlScans();
            }
            if (snap) prevPage = pg;
            if (pg == PageMessages) unread = 0;
            if (pg == PageClass && classSeg != 1) RefreshTasksIfStale();
            if (pg == PageShelf) ShelfClipCheck();
            if (pg == PageControl)
            {
                if (controlMode == 2 && !ctlWifiScanning) StartControlWifiScan();
                else if (controlMode == 3 && !ctlBtScanning) StartControlBtScan();
            }
            Kick();
        }

        void SetSegment(int pg, int seg, bool snap)
        {
            var sx = pg == PageClass ? classSegX : shelfSegX;
            int was = pg == PageClass ? classSeg : shelfSeg;
            if (pg == PageClass) classSeg = seg; else shelfSeg = seg;
            if (snap) { sx.Snap(seg); return; }
            if (was == seg) return;
            prevSeg = was;
            if (Anim.Reduced) sx.Snap(seg);
            else sx.Target = seg;
            segSwitchUtc = DateTime.UtcNow;
            if (pg == PageClass && seg != 1) RefreshTasksIfStale();
            Kick();
        }

        void RefreshTasksIfStale()
        {
            if (kulia != null && kulia.SignedIn && (DateTime.UtcNow - tasksAtUtc).TotalMinutes > 2) RefreshTasks(false);
            if (sources != null) sources.Stale(TimeSpan.FromMinutes(2));
        }

        void StepPanel(float dt)
        {
            dotOpen.Step(dt); dotHotX.Step(dt); dotHotA.Step(dt);
            classSegX.Step(dt); shelfSegX.Step(dt);
            agentScroll.Step(dt); taskScroll.Step(dt); notifScroll.Step(dt); controlScroll.Step(dt); plainScroll.Step(dt); scheduleScroll.Step(dt); clipScroll.Step(dt); clipLift.Step(dt); shelfScroll.Step(dt); convScroll.Step(dt); homeScroll.Step(dt);
            // Rubber band: let go after a beat
            if (rubber != null && (DateTime.UtcNow - rubberUtc).TotalSeconds > 0.14)
            {
                rubber.Target = Math.Max(0, Math.Min(rubberMax, rubber.Target));
                rubber = null;
            }
            StepMusic(dt);
        }

        bool PanelMoving()
        {
            var now = DateTime.UtcNow;
            const float rowEnd = RowTransitionSeconds + RowStaggerLimit * RowStaggerSeconds;
            bool contentMoving = current == View.Panel && !Anim.Reduced &&
                ((now - pageSwitchUtc).TotalSeconds < PageTransitionSeconds ||
                 (now - panelOpenedUtc).TotalSeconds < rowEnd ||
                 (now - segSwitchUtc).TotalSeconds < Math.Max(rowEnd, SegmentTransitionSeconds));
            return DotsMoving() || !classSegX.Settled || !shelfSegX.Settled ||
                   !agentScroll.Settled || !taskScroll.Settled || !notifScroll.Settled || !controlScroll.Settled || !plainScroll.Settled || !scheduleScroll.Settled || !clipScroll.Settled || !shelfScroll.Settled || !homeScroll.Settled ||
                   (current == View.Panel && page == PageShelf && shelfSeg == 1 && !clipLift.Settled) ||
                   contentMoving || MusicMoving(now) ||
                   (page == PageAgents && (now - agentFocusUtc).TotalSeconds < 1.6);
        }

        Spring rubber;
        float rubberMax;
        DateTime rubberUtc;

        // Past an edge: stretch, spring back
        void ScrollTo(Spring s, float want, float max)
        {
            if (Anim.Reduced || (want >= 0 && want <= max)) { s.Target = Math.Max(0, Math.Min(max, want)); return; }
            float over = want < 0 ? want : want - max;
            s.Target = (want < 0 ? 0 : max) + Math.Sign(over) * Math.Min(28, Math.Abs(over) * 0.35f);
            rubber = s;
            rubberMax = max;
            rubberUtc = DateTime.UtcNow;
        }

        // Staggered row entry; dy is remaining drop
        float RowIn(int i, DateTime now, out float dy)
        {
            var since = pageSwitchUtc > panelOpenedUtc ? pageSwitchUtc : panelOpenedUtc;
            if (segSwitchUtc > since) since = segSwitchUtc;
            float t = ((float)(now - since).TotalSeconds - Math.Min(i, RowStaggerLimit) * RowStaggerSeconds) / RowTransitionSeconds;
            if (Anim.Reduced || t >= 1) { dy = 0; return 1; }
            float k = Ease.OutCubic(t);
            dy = (1 - k) * 4;
            return Ease.Lerp(0.72f, 1, k);
        }

        bool ScrollPanel(int delta)
        {
            // Visible list height under tabs (and segments)
            float view = PanelHeight() - PanelTop - 20 - (SegmentPage(page) ? SegTop : 0);
            Spring s;
            float content, step;
            if (page == PageHome && !HomeQuiet) { s = homeScroll; content = homeContentH; view = HomeView(); step = 72; }
            else if (page == PageAgents) { s = agentScroll; content = AgentsPageContentH(); view = AgentsHeight() - PanelTop - 14; step = 84; }
            else if (page == PageClass && classSeg == 0) { s = taskScroll; content = FilteredTasks().Count * 54 + TaskFoot(); view -= TaskBarH; step = 81; }
            else if (page == PageClass && classSeg == 2) { s = scheduleScroll; content = ScheduleContentH() + EventFoot(); step = ClassRowH * 1.5f; }
            else if (page == PageMessages) { s = notifScroll; content = notifs.Count * 50; step = 75; }
            else if (page == PageShelf && shelfSeg == 1) return ScrollClipCards(-Math.Sign(delta));
            else if (page == PageShelf) { s = shelfScroll; content = ShelfContentH(); step = 60; }
            else if (page == PageVoice) { s = voiceScroll; content = VoiceContentH(VoiceItems(), PanelW); view = VoiceListView(PanelHeight()); step = VoiceRowH * 1.5f; }
            else if (page == PageConvert && ConvertSetting) { s = convScroll; content = convHistory.Count * ConvRowH; view = ConvListView(PanelHeight()); step = ConvRowH * 1.5f; }
            else if (page == PageControl && (controlMode == 2 || controlMode == 3))
            {
                s = controlScroll;
                content = ControlSubContentH();
                view = ControlSubViewH();
                step = CtlSubRowPitch * 1.5f;
            }
            else if (page == PageMusic && lyrics != null && lyrics.State == LyricState.Plain && lyrics.Key == mediaInfo.Key)
            {
                s = plainScroll;
                content = lyrics.Lines.Count * PlainLineH;
                view = 150;
                step = PlainLineH * 3;
            }
            else return false;
            // Content fits: wheel changes tabs instead (inline Wi-Fi/BT sub-views absorb wheel)
            if (content - view <= 1 && s.Target <= 0.5f)
                return page == PageControl && (controlMode == 2 || controlMode == 3);
            ScrollTo(s, s.Target - delta / 120f * step, Math.Max(0, content - view));
            Kick();
            return true;
        }

        int wheelAcc;
        DateTime wheelTabUtc;

        // One tab per notch, no skipping
        void WheelTab(int delta)
        {
            if ((DateTime.UtcNow - wheelTabUtc).TotalSeconds > 0.5) wheelAcc = 0;
            wheelAcc += delta;
            if (Math.Abs(wheelAcc) < 100 || (DateTime.UtcNow - wheelTabUtc).TotalSeconds < 0.18) return;
            int step = Math.Sign(wheelAcc);
            wheelAcc = 0;
            wheelTabUtc = DateTime.UtcNow;
            int at = Array.IndexOf(tabs, page);
            int next = at + step;
            // Stop at the ends, like paging
            if (at < 0 || next < 0 || next >= tabs.Length) { if (!Anim.Reduced) tabNudge.Velocity += step * 220; Kick(); return; }
            SetPage(tabs[next], false);
            Announce(Lang.T(PageNames[tabs[next]]));
        }

        void DrawPanel(float w, float h, DateTime now)
        {
            float k = Anim.Reduced || prevPage == page ? 1 : Ease.OutCubic((float)((now - pageSwitchUtc).TotalSeconds / PageTransitionSeconds));
            float musicVis = BackdropShown(now) ? 1 : 0;
            double t0 = Prof.T();
            if (mediaInfo.Present) DrawBackdrop(musicVis);
            Prof.Add("backdrop", t0);
            t0 = Prof.T();
            DrawTabs(w, now);
            Prof.Add("tabs", t0);
            t0 = Prof.T();
            int dir = Math.Sign(Array.IndexOf(tabs, page) - Array.IndexOf(tabs, prevPage));
            var area = new RectangleF(0, PanelTop - 6, w, h - PanelTop + 6);
            var st = g.Save();
            g.IntersectClip(area);
            float alpha = p.Alpha;
            // Outgoing page, skipped once faded
            float outAlpha = alpha * (1 - Ease.Clamp01(k * 2.2f));
            if (k < 1 && prevPage != page && outAlpha >= 0.04f)
            {
                p.Alpha = outAlpha;
                var s1 = g.Save();
                g.TranslateTransform(-dir * k * 14, 0);
                bool hold = hitsOn;
                hitsOn = false;
                DrawPage(prevPage, w, h, now);
                hitsOn = hold;
                g.Restore(s1);
            }
            p.Alpha = alpha * Ease.Clamp01(k * 1.3f);
            var s2 = g.Save();
            g.TranslateTransform(dir * (1 - k) * 14, 0);
            if (k < 0.85f) hitsOn = false;
            DrawPage(page, w, h, now);
            g.Restore(s2);
            p.Alpha = alpha;
            g.Restore(st);
            Prof.Add("page", t0);
        }

        // Cover backdrop swaps mid-fade
        bool BackdropShown(DateTime now)
        {
            bool mid = Anim.Reduced || (now - pageSwitchUtc).TotalSeconds >= PageTransitionSeconds * 0.45;
            if (page == PageMusic) return prevPage == PageMusic || mid;
            return prevPage == PageMusic && !mid;
        }

        void DrawPage(int pg, float w, float h, DateTime now)
        {
            switch (pg)
            {
                case PageHome: DrawHome(w, h, now); break;
                case PageMusic: DrawMusicPage(w, h, now); break;
                case PageAgents: DrawAgentsPage(w, h, now); break;
                case PageClass:
                case PageShelf: DrawSegmentPage(pg, w, h, now); break;
                case PageMessages: DrawMessagesPage(w, h, now); break;
                case PageControl: DrawControlPage(w, h, now); break;
                case PageRemind: DrawRemindPage(w, h, now); break;
                case PagePhone: DrawPhonePage(w, h, now); break;
                case PageConvert: DrawConvertPage(w, h, now); break;
                case PageVoice: DrawVoicePage(w, h, now); break;
            }
        }

        // Segments

        void DrawSegmentPage(int pg, float w, float h, DateTime now)
        {
            bool isClass = pg == PageClass;
            int seg = isClass ? classSeg : shelfSeg;
            if (isClass)
            {
                string taskTab = tasks != null && tasks.Count > 0 ? Lang.T("Tugas") + " · " + tasks.Count : Lang.T("Tugas");
                DrawSegments(w, pg, taskTab, Lang.T("Pomodoro"), Lang.T("Jadwal"));
            }
            else DrawSegments(w, pg, Lang.T("Berkas"), Lang.T("Clipboard"));
            float k = Anim.Reduced || pg != page || prevSeg == seg ? 1 : Ease.OutCubic((float)((now - segSwitchUtc).TotalSeconds / SegmentTransitionSeconds));
            int dir = seg > prevSeg ? 1 : -1;
            var st = g.Save();
            g.IntersectClip(new RectangleF(0, PanelTop + SegTop - 6, w, h - PanelTop - SegTop + 6));
            float alpha = p.Alpha, keepDy = hitDy;
            bool hold = hitsOn;
            if (k < 1 && prevSeg != seg)
            {
                p.Alpha = alpha * (1 - Ease.Clamp01(k * 2.2f));
                var s1 = g.Save();
                g.TranslateTransform(-dir * k * 12, SegTop);
                hitsOn = false;
                DrawSub(pg, prevSeg, w, h - SegTop, now);
                g.Restore(s1);
            }
            p.Alpha = alpha * Ease.Clamp01(k * 1.3f);
            var s2 = g.Save();
            g.TranslateTransform(dir * (1 - k) * 12, SegTop);
            hitsOn = hold && k >= 0.85f;
            hitDy = keepDy + SegTop;
            DrawSub(pg, seg, w, h - SegTop, now);
            hitDy = keepDy;
            hitsOn = hold;
            g.Restore(s2);
            p.Alpha = alpha;
            g.Restore(st);
        }

        void DrawSub(int pg, int seg, float w, float h, DateTime now)
        {
            if (pg == PageClass)
            {
                if (seg == 0) DrawTasksPage(w, h, now);
                else if (seg == 1) DrawPomodoroPage(w, h, now);
                else DrawSchedulePage(w, h, now);
            }
            else { if (seg == 0) DrawShelfPage(w, h, now); else DrawClipsPage(w, h, now); }
        }

        // Segmented switch with a sliding thumb
        void DrawSegments(float w, int pg, params string[] labels)
        {
            int n = labels.Length;
            int seg = pg == PageClass ? classSeg : shelfSeg;
            var sx = pg == PageClass ? classSegX : shelfSegX;
            float boxW = n <= 2 ? 224 : Math.Min(w - 32, n * 106f);
            var box = new RectangleF(w / 2 - boxW / 2, PanelTop, boxW, 28);
            p.FillRound(box, 14, p.T.Fill);
            p.StrokeRound(box, 14, p.T.Line, 1f);
            float segW = (box.Width - 4) / n;
            var thumb = new RectangleF(box.X + 2 + segW * Math.Max(-0.05f, Math.Min(n - 1 + 0.05f, sx.Value)), box.Y + 2, segW, box.Height - 4);
            p.FillRound(thumb, 12, Ink.Mix(p.T.FillHover, p.T.Text, 0.12f));
            for (int i = 0; i < n; i++)
            {
                var r = new RectangleF(box.X + 2 + segW * i, box.Y + 2, segW, box.Height - 4);
                string id = "seg" + pg + "_" + i;
                int to = i;
                AddHit(r, id, delegate { SetSegment(pg, to, false); });
                float sq = Squish(id, r, 0.95f);
                bool dot = pg == PageClass && ((i == 1 && TimerOn) || (i == 2 && ClassNow() != null));
                float cy = r.Y + r.Height / 2;
                if (dot)
                {
                    float tw = p.Measure(labels[i], Ink.Semi(12f));
                    float cx = r.X + r.Width / 2;
                    p.Circle(cx - tw / 2 - 5, cy, 2.8f, p.T.Accent);
                    p.TextCenter(labels[i], Ink.Semi(12f), i == seg || Hover(id) ? p.T.Text : p.T.Sub, cx + 4, cy);
                }
                else
                {
                    p.TextCenter(labels[i], Ink.Semi(12f), i == seg || Hover(id) ? p.T.Text : p.T.Sub, r.X + r.Width / 2, cy);
                }
                Unsquish(r, sq);
            }
        }

        // Tabs keep equal cells. The title sits below the strip.

        const float TabIcon = 32, TabH = 28, TabGap = 4, TabY = 10, TabGlyph = 0.86f;
        // Wheel past last tab: bar leans back
        readonly Spring tabNudge = new Spring(0, 420, 18, 0.05f);
        bool tabDotsDrawn;
        // One progress for the whole strip: hover or focus anywhere opens every dot
        readonly Spring dotOpen = new Spring(0, 460, 30, 0.002f);
        // Highlight pill slides to the item the pointer would pick
        readonly Spring dotHotX = new Spring(0, 620, 38, 0.05f), dotHotA = new Spring(0, 620, 38, 0.004f);
        DateTime stripUtc = DateTime.MinValue;
        bool stripOpen, stripEngaged;
        // Dots band: full tab row tall; open cells keep this much room for the title on the left
        const float DotBandH = 48, DotPadX = 6, DotPadY = 4;

        bool DotsMoving()
        {
            return !dotOpen.Settled || !dotHotX.Settled || !dotHotA.Settled || (stripOpen && !stripEngaged && (DateTime.UtcNow - stripUtc).TotalSeconds < 0.32);
        }

        // Many tabs: dots plus page title
        bool DotTabs
        {
            get
            {
                string style = settings.Get("tabs.style", "auto");
                return style == "dots" || (style == "auto" && tabs.Length > 7);
            }
        }
        float tabLayoutW;

        string TabLabel(int pg) { return Lang.T(PageNames[pg]); }

        RectangleF TabStrip(float w, float preferredPitch)
        {
            float room = w - 110;
            float pitch = Math.Min(preferredPitch, room / Math.Max(1, tabs.Length));
            float total = pitch * tabs.Length;
            return new RectangleF(Math.Min((w - total) / 2, w - 92 - total), 0, total, DotBandH);
        }

        void DrawTabs(float w, DateTime now)
        {
            tabLayoutW = w;
            bool dots = DotTabs;
            if (tabDotsDrawn != dots)
            {
                tabDotsDrawn = dots;
                stripOpen = stripEngaged = false;
                stripUtc = DateTime.MinValue;
                dotOpen.Snap(0);
                dotHotA.Snap(0);
            }
            int n = tabs.Length;
            var strip = TabStrip(w, dots ? 32 : TabIcon + TabGap);
            DrawHeaderClock(strip);
            if (dots) { DrawDots(w, strip, now); return; }
            float pitch = strip.Width / Math.Max(1, n);
            int preview = page;
            DrawTabTrack(strip, TabY + TabH / 2, TabH + 2, 1f);
            for (int i = 0; i < n; i++)
            {
                int to = tabs[i];
                string id = "tab" + to;
                var hit = new RectangleF(strip.X + i * pitch, TabY, pitch, TabH);
                AddHit(hit, id, delegate { SetPage(to, false); });
                bool hot = Hover(id), active = to == page;
                if (hot || (keyboardMode && focusId == id)) preview = to;
                var pill = RectangleF.Inflate(hit, -2, 0);
                pill.X += tabNudge.Value;
                float cx = pill.X + pill.Width / 2;
                if (active || hot)
                    p.FillRound(pill, TabH / 2, active ? p.T.Accent : Pressed(id) ? p.T.FillPressed : p.T.FillHover);
                DrawTabIconFast(to, cx, TabY + TabH / 2, active ? p.T.OnAccent : hot ? p.T.Text : TabInk(), true);

                bool mark = (to == PageMessages && unread > 0) ||
                    (to == PageAgents && approvals.Count + BusyCount() > 0 && !active);
                if (mark)
                {
                    p.Circle(cx + 8, TabY + 6, 4.2f, p.T.Bg);
                    p.Circle(cx + 8, TabY + 6, 3f, p.T.Text);
                }
            }
            DrawTabName(strip, preview);
            DrawTabAction(w);
        }

        void DrawTabName(RectangleF strip, int preview)
        {
            float center = strip.X + strip.Width / 2;
            // Current page reads as a title; a hovered preview stays quieter
            p.Text(TabLabel(preview), Ink.Semi(12f), preview == page ? p.T.Text : Ink.Mix(p.T.Sub, p.T.Text, 0.5f), center, 42, 2 * (center - 12), StringAlignment.Center);
        }

        // Time and date fill the empty corner left of the strip; the quiet Overview hero already shows them
        void DrawHeaderClock(RectangleF strip)
        {
            const float x = 24;
            float room = strip.X - 4 - 12 - x;
            if (room < 56 || (page == PageHome && HomeQuiet)) return;
            var local = DateTime.Now;
            p.Text(Fmt.Clock(local), Ink.Display(17f), p.T.Text, x, TabY + TabH / 2 - 2, room, StringAlignment.Near);
            p.Text(local.ToString("ddd, d MMM", Fmt.Id), Ink.Small(11f), p.T.Sub, x, 40, room, StringAlignment.Near);
        }

        // Idle tab glyphs: a step above Sub so the strip reads on any glass
        Color TabInk() { return Ink.Mix(p.T.Sub, p.T.Text, 0.35f); }

        // One quiet capsule groups the tabs into a single control
        void DrawTabTrack(RectangleF strip, float cy, float h, float k)
        {
            if (k <= 0.01f) return;
            var r = new RectangleF(strip.X - 4 + tabNudge.Value, cy - h / 2, strip.Width + 8, h);
            float keep = p.Alpha;
            p.Alpha = keep * k;
            p.FillRound(r, h / 2, p.T.Fill);
            p.Alpha = keep;
        }

        // Dots: hover or focus anywhere opens the whole strip into icons; hover names page
        void DrawDots(float w, RectangleF strip, DateTime now)
        {
            int n = tabs.Length, hover = -1, at = Array.IndexOf(tabs, page);
            const float cy = 18f;
            float pitch = strip.Width / Math.Max(1, n);
            bool focusIn = keyboardMode && focusId != null && focusId.Length > 3 && focusId.StartsWith("tab", StringComparison.Ordinal) && char.IsDigit(focusId[3]);
            var local = new PointF(mouse.X - originX, mouse.Y - originY - hitDy);
            var zone = stripOpen ? RectangleF.Inflate(strip, DotPadX, DotPadY) : strip;
            bool engaged = (pointerInside && zone.Contains(local)) || focusIn;
            if (stripEngaged && !engaged) stripUtc = now;
            stripEngaged = engaged;
            bool open = engaged || (now - stripUtc).TotalSeconds < 0.28;
            stripOpen = open;
            dotOpen.Target = open ? 1 : 0;
            if (Anim.Reduced) dotOpen.Snap(dotOpen.Target);
            float e = Ease.Clamp01(dotOpen.Value);
            DrawTabTrack(strip, cy, Ease.Lerp(16, 30, e), Ease.Lerp(0.6f, 1f, e));
            for (int i = 0; i < n; i++)
            {
                string id = "tab" + tabs[i];
                int to = tabs[i];
                AddHit(new RectangleF(strip.X + i * pitch, 0, pitch, DotBandH), id, delegate { SetPage(to, false); });
                if (Hover(id) || (keyboardMode && focusId == id)) hover = i;
            }
            float hotTo = strip.X + (Math.Max(0, hover) + 0.5f) * pitch;
            dotHotA.Target = open && hover >= 0 && hover != at ? 1 : 0;
            if (dotHotA.Target > 0 && dotHotA.Value < 0.02f) dotHotX.Snap(hotTo); else dotHotX.Target = hotTo;
            if (Anim.Reduced) { dotHotA.Snap(dotHotA.Target); dotHotX.Snap(hotTo); }
            float ha = Ease.Clamp01(dotHotA.Value) * e;
            if (ha > 0.01f)
            {
                float keepA = p.Alpha;
                p.Alpha = keepA * ha;
                string hid = "tab" + tabs[Math.Max(0, hover)];
                p.FillRound(new RectangleF(dotHotX.Value - (pitch - 4) / 2, cy - 12, pitch - 4, 24), 12, Pressed(hid) ? p.T.FillPressed : p.T.FillHover);
                p.Alpha = keepA;
            }
            for (int i = 0; i < n; i++)
            {
                int to = tabs[i];
                bool lit = i == hover;
                bool news = (to == PageMessages && unread > 0) || (to == PageAgents && approvals.Count + BusyCount() > 0);
                bool mark = (to == PageMessages && unread > 0) || (to == PageAgents && approvals.Count + BusyCount() > 0 && page != PageAgents);
                float cx = strip.X + (i + 0.5f) * pitch + tabNudge.Value;
                // Small stagger across the strip; all settled once e reaches 1
                float t = Ease.Clamp01(e * 1.35f - 0.35f * i / Math.Max(1, n - 1));
                float ia = Ease.Clamp01((t - 0.35f) / 0.65f), keep = p.Alpha;
                Color iconColor;
                if (i == at)
                {
                    float pw = Ease.Lerp(Math.Min(20, pitch - 4), pitch - 4, t), ph = Ease.Lerp(8, 24, t);
                    p.FillRound(new RectangleF(cx - pw / 2, cy - ph / 2, pw, ph), ph / 2, p.T.Accent);
                    iconColor = p.T.OnAccent;
                }
                else
                {
                    // The dot melts away as its icon arrives
                    float rad = Ease.Lerp(lit ? 4.5f : news ? 4.2f : 3.8f, 7f, t);
                    p.Alpha = keep * (1 - t);
                    if (t < 0.99f) p.Circle(cx, cy, rad, lit || news ? p.T.Text : p.T.Sub);
                    p.Alpha = keep;
                    iconColor = lit ? p.T.Text : TabInk();
                }
                if (ia > 0.01f)
                {
                    float grow = (0.55f + 0.45f * t) * (lit && i != at ? 1.1f : 1f);
                    p.Alpha = keep * ia;
                    var st = g.Save();
                    g.TranslateTransform(cx, cy);
                    g.ScaleTransform(grow, grow);
                    DrawTabIcon(to, 0, 0, iconColor);
                    g.Restore(st);
                    // Unread or busy marker stays on the icon
                    if (mark)
                    {
                        p.Circle(cx + 8, cy - 8, 4.2f, p.T.Bg);
                        p.Circle(cx + 8, cy - 8, 3f, p.T.Text);
                    }
                    p.Alpha = keep;
                }
            }
            DrawTabName(strip, hover >= 0 ? tabs[hover] : page);
            DrawTabAction(w);
        }

        // Cached tab icons; vector while animating
        readonly Dictionary<long, Bitmap> tabIcons = new Dictionary<long, Bitmap>();
        float tabIconScale;
        int tabIconEpoch = -1;
        const float TabIconBox = 24f;

        void DrawTabIconFast(int i, float cx, float cy, Color c, bool spin)
        {
            // Busy agents: spark spins, still under pill
            if (i == PageAgents && BusyCount() > 0)
            {
                if (spin) LiveRect(new RectangleF(cx - 12, cy - 12, 24, 24), LiveFps, t => DrawTabIcon(i, cx, cy, c));
                else DrawTabIcon(i, cx, cy, c);
                return;
            }
            if (p.Alpha < 0.99f) { DrawTabIcon(i, cx, cy, c); return; }
            if (tabIconScale != scale || tabIconEpoch != Ink.Epoch || tabIcons.Count > 48)
            {
                foreach (var b in tabIcons.Values) b.Dispose();
                tabIcons.Clear();
                tabIconScale = scale;
                tabIconEpoch = Ink.Epoch;
            }
            long key = ((long)i << 32) | (uint)c.ToArgb();
            Bitmap bmp;
            if (!tabIcons.TryGetValue(key, out bmp))
            {
                int px = (int)Math.Ceiling(TabIconBox * scale) + 2;
                bmp = new Bitmap(px, px, PixelFormat.Format32bppPArgb);
                Graphics keepG = g, bg = Graphics.FromImage(bmp);
                float keepAlpha = p.Alpha;
                try
                {
                    bg.SmoothingMode = SmoothingMode.AntiAlias;
                    bg.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAlias;
                    bg.PixelOffsetMode = PixelOffsetMode.HighQuality;
                    bg.ScaleTransform(scale, scale);
                    g = bg;
                    p.G = bg;
                    p.Alpha = 1f;
                    DrawTabIcon(i, TabIconBox / 2, TabIconBox / 2, c);
                }
                finally { g = keepG; p.G = keepG; p.Alpha = keepAlpha; bg.Dispose(); }
                tabIcons[key] = bmp;
            }
            // Whole device pixels, 1:1
            var pt = new[] { new PointF(cx, cy) };
            g.TransformPoints(CoordinateSpace.Device, CoordinateSpace.World, pt);
            int x = (int)Math.Round(pt[0].X - TabIconBox / 2 * scale), y = (int)Math.Round(pt[0].Y - TabIconBox / 2 * scale);
            var st = g.Save();
            g.ResetTransform();
            g.InterpolationMode = InterpolationMode.NearestNeighbor;
            g.PixelOffsetMode = PixelOffsetMode.Half;
            g.DrawImage(bmp, new Rectangle(x, y, bmp.Width, bmp.Height), 0, 0, bmp.Width, bmp.Height, GraphicsUnit.Pixel);
            g.Restore(st);
        }

        void DrawTabIcon(int i, float cx, float cy, Color c)
        {
            // Glyphs a touch smaller than drawn
            var iconState = g.Save();
            g.TranslateTransform(cx, cy);
            g.ScaleTransform(TabGlyph, TabGlyph);
            cx = 0;
            cy = 0;
            var pen = p.P(c, 1.7f);
            switch (i)
            {
                case PageHome:
                    // Overview: a 2x2 grid of tiles
                    for (int gy = 0; gy < 2; gy++)
                        for (int gx = 0; gx < 2; gx++)
                            using (var tile = Ink.Round(new RectangleF(cx - 7.5f + gx * 8.5f, cy - 7.5f + gy * 8.5f, 6.5f, 6.5f), 2f)) g.DrawPath(p.P(c, 1.5f), tile);
                    break;
                case PageClass:
                    p.Icon('\uE821', 15f, c, cx, cy);
                    break;
                case PageShelf:
                    // Tray with a lip
                    using (var tray = Ink.Round(new RectangleF(cx - 8, cy - 5.5f, 16, 12), 3.5f)) g.DrawPath(pen, tray);
                    g.DrawLines(pen, new[] { new PointF(cx - 8, cy + 0.5f), new PointF(cx - 3.2f, cy + 0.5f), new PointF(cx - 2, cy + 2.6f), new PointF(cx + 2, cy + 2.6f), new PointF(cx + 3.2f, cy + 0.5f), new PointF(cx + 8, cy + 0.5f) });
                    break;
                case PageVoice:
                    p.Icon(Ink.IcoMic, 15f, c, cx, cy);
                    break;
                case PageRemind:
                    p.Icon('\uE823', 15f, c, cx, cy);
                    break;
                case PagePhone:
                    p.Icon(IcoPhone, 15f, c, cx, cy);
                    break;
                case PageConvert:
                    // Two opposing arrows
                    g.DrawLine(pen, cx - 7, cy - 3.5f, cx + 7, cy - 3.5f);
                    g.DrawLines(pen, new[] { new PointF(cx + 3.5f, cy - 7), new PointF(cx + 7, cy - 3.5f), new PointF(cx + 3.5f, cy) });
                    g.DrawLine(pen, cx + 7, cy + 3.5f, cx - 7, cy + 3.5f);
                    g.DrawLines(pen, new[] { new PointF(cx - 3.5f, cy), new PointF(cx - 7, cy + 3.5f), new PointF(cx - 3.5f, cy + 7) });
                    break;
                case PageControl:
                    // Two sliders
                    g.DrawLine(pen, cx - 7.5f, cy - 3.5f, cx + 7.5f, cy - 3.5f);
                    g.DrawLine(pen, cx - 7.5f, cy + 3.5f, cx + 7.5f, cy + 3.5f);
                    p.Circle(cx - 2.5f, cy - 3.5f, 2.6f, c);
                    p.Circle(cx + 3.5f, cy + 3.5f, 2.6f, c);
                    break;
                case PageMusic:
                    g.DrawLine(pen, cx - 3.5f, cy + 4.5f, cx - 3.5f, cy - 5.5f);
                    g.DrawLine(pen, cx + 5.5f, cy + 3f, cx + 5.5f, cy - 7f);
                    using (var beam = new GraphicsPath())
                    {
                        beam.AddPolygon(new[] { new PointF(cx - 4.3f, cy - 5.6f), new PointF(cx + 6.3f, cy - 7.4f), new PointF(cx + 6.3f, cy - 4.4f), new PointF(cx - 4.3f, cy - 2.6f) });
                        g.FillPath(p.B(c), beam);
                    }
                    g.FillEllipse(p.B(c), cx - 8.6f, cy + 2.4f, 5.6f, 4.4f);
                    g.FillEllipse(p.B(c), cx + 0.4f, cy + 0.9f, 5.6f, 4.4f);
                    break;
                case PageAgents:
                    // Spark turns while an agent works
                    float spin = BusyCount() > 0 ? orbitPhase * 360f : 0f;
                    for (int k = 0; k < 8; k++)
                    {
                        double a = (spin + k * 45) * Math.PI / 180;
                        float len = k % 2 == 0 ? 8f : 5.2f;
                        g.DrawLine(p.P(c, 2f), cx + (float)Math.Cos(a) * 1.6f, cy + (float)Math.Sin(a) * 1.6f, cx + (float)Math.Cos(a) * len, cy + (float)Math.Sin(a) * len);
                    }
                    break;
                default:
                    using (var bubble = Ink.Round(new RectangleF(cx - 8.5f, cy - 7f, 17, 12.5f), 4.6f)) g.DrawPath(pen, bubble);
                    g.DrawLines(pen, new[] { new PointF(cx - 5.5f, cy + 5.4f), new PointF(cx - 6.8f, cy + 9f), new PointF(cx - 1.8f, cy + 5.4f) });
                    for (int d = -1; d <= 1; d++) p.Circle(cx + d * 3.6f, cy - 0.8f, 1.15f, c);
                    break;
            }
            g.Restore(iconState);
        }

        // Click always switches; hover may too; the wheel always does
        bool TabsByHover { get { return settings.Get("tabs.trigger", "click") == "hover"; } }
        readonly System.Windows.Forms.Timer tabHover = new System.Windows.Forms.Timer { Interval = 140 };
        bool tabHoverWired;

        // Pointer rests on a tab a moment: open it
        void TabHoverCheck(string id)
        {
            bool onTab = id != null && id.Length > 3 && id.StartsWith("tab", StringComparison.Ordinal) && char.IsDigit(id[3]);
            tabHover.Stop();
            if (!onTab || !TabsByHover) return;
            if (!tabHoverWired)
            {
                tabHoverWired = true;
                tabHover.Tick += delegate
                {
                    tabHover.Stop();
                    int pg;
                    if (hoverId != null && hoverId.StartsWith("tab", StringComparison.Ordinal) && int.TryParse(hoverId.Substring(3), out pg) && pg != page) SetPage(pg, false);
                };
            }
            tabHover.Start();
        }

        // Gear, far right: settings without right-click
        void DrawPrefsButton(float w)
        {
            var r = new RectangleF(w - 52, TabY, 34, TabH);
            AddHit(r, "prefsOpen", openPrefsAct ?? (openPrefsAct = delegate { int pg = page; BeginInvoke(new Action(delegate { OpenPrefsForPage(pg); })); }));
            if (Hover("prefsOpen")) p.FillRound(r, TabH / 2, Pressed("prefsOpen") ? p.T.FillPressed : p.T.FillHover);
            p.Icon('', 13f, Hover("prefsOpen") ? p.T.Text : p.T.Sub, r.X + 17, r.Y + TabH / 2);
        }

        Action openPrefsAct;

        void OpenPrefsForPage(int pg)
        {
            string target = "overview";
            if (pg == PageHome) target = "tabs";
            else if (pg == PageMusic || pg == PageShelf || pg == PageConvert) target = "files";
            else if (pg == PageAgents) target = "agents";
            else if (pg == PageClass) target = "integrasi";
            else if (pg == PageMessages) target = "messages";
            else if (pg == PageRemind) target = "remind";
            else if (pg == PagePhone) target = "phone";
            else if (pg == PageVoice) target = "dikte";
            OpenPrefs(target);
        }

        // Round action button; trash swallows flyers
        void TabButton(string id, char glyph, Action act, bool trash)
        {
            var r = new RectangleF(tabLayoutW - 88, TabY, 34, TabH);
            AddHit(r, id, act);
            if (Hover(id)) p.FillRound(r, TabH / 2, Pressed(id) ? p.T.FillPressed : p.T.FillHover);
            if (trash) { var c = CanvasRect(r); trashPt = new PointF(c.X + c.Width / 2, c.Y + c.Height / 2); }
            float s = trash ? TrashScale(DateTime.UtcNow) : 1;
            p.Icon(glyph, 13f * s, Hover(id) || s > 1.01f ? p.T.Text : p.T.Sub, r.X + 17, r.Y + TabH / 2);
        }

        // Contextual action right of tabs
        void DrawTabAction(float w)
        {
            DrawPrefsButton(w);
            var r = new RectangleF(w - 88, TabY, 34, TabH);
            if (page == PageMusic && mediaInfo.Present && mediaInfo.Title.Length > 0)
            {
                bool album = AlbumMode();
                AddHit(r, "musicView", delegate { ToggleMusicView(); });
                if (Hover("musicView")) p.FillRound(r, TabH / 2, Pressed("musicView") ? p.T.FillPressed : p.T.FillHover);
                Color c = Hover("musicView") ? p.T.Text : p.T.Sub;
                float cx = r.X + r.Width / 2, cy = r.Y + r.Height / 2;
                if (album)
                {
                    // Lyrics glyph: ragged text lines
                    var pen = p.P(c, 1.7f);
                    g.DrawLine(pen, cx - 7, cy - 5, cx + 7, cy - 5);
                    g.DrawLine(pen, cx - 7, cy, cx + 3, cy);
                    g.DrawLine(pen, cx - 7, cy + 5, cx + 5.5f, cy + 5);
                }
                else
                {
                    // Record glyph
                    g.DrawEllipse(p.P(c, 1.7f), cx - 8, cy - 8, 16, 16);
                    g.DrawArc(p.P(c, 1.2f), cx - 5, cy - 5, 10, 10, 200, 70);
                    p.Circle(cx, cy, 2f, c);
                }
            }
            else if (page == PageClass && classSeg != 1 && (classSeg == 0 ? TaskState() : ScheduleState()) != DataState.Off)
            {
                int what = classSeg == 0 ? 1 : 2;
                if (tasksLoading && classSeg == 0) p.Orbit(r.X + 17, r.Y + TabH / 2, 6.5f, orbitPhase, p.T.Sub);
                else TabButton("refresh", Ink.IcoRefresh, delegate { if (!tasksLoading) RefreshAll(what); }, false);
            }
            else if (page == PageMessages && notifs.Count > 0)
                TabButton("clearAll", '', delegate
                {
                    Vanish(RowRects("notif", "notifDel"), true);
                    notifScroll.Snap(0);
                    var shown = new List<NotifItem>(notifs);
                    RemoveNotifs(shown);
                    // The page only holds the newest rows; clear everything else too
                    NotifSource.ClearAll();
                }, true);
            else if (page == PageShelf && shelfSeg == 1 && VisibleClips.Count > 0)
                TabButton("clearClips", '', delegate { Vanish(RowRects("clip", null), true); clipScroll.Snap(0); ClearClips(VisibleClips); Kick(); }, true);
            else if (page == PageShelf && shelfSeg == 0 && shelf.Count > 0)
                TabButton("clearShelf", '', delegate { Vanish(new List<RectangleF>(shelfCells.Values), true); shelfScroll.Snap(0); ClearShelf(); }, true);
            else if (page == PageVoice)
                TabButton("voiceFolder", '\uE8B7', OpenRapatFolder, false);
            else if (page == PageConvert && ConvertSetting && convHistory.Count > 0)
                TabButton("clearConv", '', ClearConvertHistory, true);
        }
    }
}
