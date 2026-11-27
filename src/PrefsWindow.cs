using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace Deskmate
{
    partial class Island
    {
        partial class PrefsWindow : KitWindow
        {
            readonly Island island;
            readonly Settings settings;
            readonly Timer preview = new Timer { Interval = 2500 };
            // Opens on Main Menu Hub; section stays "general" as default target
            string section = "general", beforeSearch = "general";
            bool inHub = true, beforeSearchInHub = true;
            // Part a caller asked for inside a merged page; Fill scrolls to it once
            string focusPart;
            readonly Dictionary<string, int> partTop = new Dictionary<string, int>();
            readonly TextBox search = new KitText();
            readonly Timer searchTimer = new Timer { Interval = 140 };
            string appliedSearch = "";
            // Row text per part, built once: typing filters this, and only matching parts build controls
            readonly Dictionary<string, List<string>> searchIndex = new Dictionary<string, List<string>>();

            void EnsureSearchIndex()
            {
                bool wasIndexing = indexing;
                indexing = true;
                try
                {
                    foreach (var s in Sections)
                        foreach (var part in PartsOf(s[0]))
                        {
                            if (searchIndex.ContainsKey(part)) continue;
                            var rows = new List<KitRow>();
                            Build(part, rows, false);
                            var text = new List<string>();
                            foreach (var r in rows) { text.Add(r.Title + "\n" + r.Detail); if (r.Field != null) r.Field.Dispose(); r.Dispose(); }
                            searchIndex[part] = text;
                        }
                }
                finally { indexing = wasIndexing; }
            }

            bool PartMatches(string part, string q)
            {
                List<string> text;
                if (!searchIndex.TryGetValue(part, out text)) return true;
                foreach (var t in text) if (t.IndexOf(q, StringComparison.CurrentCultureIgnoreCase) >= 0) return true;
                return false;
            }
            readonly KitScrollPanel content = new KitScrollPanel();
            readonly SettingsHub hub = new SettingsHub();
            bool building, indexing, redrawFrozen, animateNextFill, syncingPartNav, partJumpActive;
            bool overClear, overBack, downBack;
            PartTabStrip partNav;
            PageHeader activeHeader;
            Color activeGroupTile;
            string[] activeParts = new string[0];
            readonly Stack<string> navBack = new Stack<string>();
            readonly Stack<string> navForward = new Stack<string>();
            readonly PageTransitionOverlay pageTransition = new PageTransitionOverlay();
            int nextEntranceDir = 1;

            // Lightweight single-bitmap viewport transition overlay: zero HWND moves, VSync-paced fade + subtle glide
            sealed class PageTransitionOverlay : Control
            {
                Bitmap snap;
                int slideDir = 1;
                float offsetPx;
                double startT;
                readonly KitPulse pulse;

                public PageTransitionOverlay()
                {
                    SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
                    TabStop = false;
                    Visible = false;
                    pulse = new KitPulse(Step);
                }

                // Pass clicks/scroll through to real controls underneath if user interacts mid-transition
                protected override void WndProc(ref Message m)
                {
                    if (m.Msg == 0x0084) { m.Result = (IntPtr)(-1); return; } // WM_NCHITTEST -> HTTRANSPARENT
                    base.WndProc(ref m);
                }

                public void Play(Bitmap snapshot, Rectangle bounds, int dir, float offset)
                {
                    Stop();
                    if (snapshot == null || bounds.Width < 10 || bounds.Height < 10)
                    {
                        if (snapshot != null) snapshot.Dispose();
                        return;
                    }
                    snap = snapshot;
                    slideDir = dir >= 0 ? 1 : -1;
                    offsetPx = offset;
                    startT = KitAnim.Now;
                    Bounds = bounds;
                    Visible = true;
                    BringToFront();
                    Invalidate();
                    pulse.Start();
                }

                public void Stop()
                {
                    pulse.Stop();
                    if (Visible) Visible = false;
                    if (snap != null) { snap.Dispose(); snap = null; }
                }

                bool Step(float dt)
                {
                    if (IsDisposed || snap == null || Anim.Reduced)
                    {
                        Stop();
                        return false;
                    }
                    float dur = Math.Max(0.04f, Anim.Response(0.145f));
                    float t = (float)((KitAnim.Now - startT) / dur);
                    if (t >= 1f)
                    {
                        Stop();
                        return false;
                    }
                    Invalidate();
                    return true;
                }

                protected override void OnPaint(PaintEventArgs e)
                {
                    var g = e.Graphics;
                    g.Clear(KitInk.Window);
                    if (snap == null) return;
                    float dur = Math.Max(0.04f, Anim.Response(0.145f));
                    float t = Ease.Clamp01((float)((KitAnim.Now - startT) / dur));
                    float eased = Ease.OutQuart(t);
                    int dx = (int)Math.Round(slideDir * offsetPx * (1f - eased));
                    g.DrawImageUnscaled(snap, dx, 0);
                    int veilA = (int)Math.Round((1f - eased) * 145f);
                    if (veilA > 2)
                        g.FillRectangle(KitInk.BrushOf(Color.FromArgb(veilA, KitInk.Window)), ClientRectangle);
                }

                protected override void Dispose(bool disposing)
                {
                    if (disposing)
                    {
                        Stop();
                        pulse.Dispose();
                    }
                    base.Dispose(disposing);
                }
            }

            // Double-buffered scroll host with VSync-paced smooth wheel inertia (snaps under Reduced Motion)
            sealed class KitScrollPanel : Panel
            {
                readonly KitPulse wheelPulse;
                float targetY, currentY;
                bool programmaticScroll;
                public bool IsProgrammaticScrolling { get { return programmaticScroll; } }
                public event Action UserScrolled;
                public event Action ScrollPositionChanged;

                public KitScrollPanel()
                {
                    AutoScroll = true;
                    SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint, true);
                    wheelPulse = new KitPulse(StepScroll);
                }

                bool StepScroll(float dt)
                {
                    if (IsDisposed || !Visible || Anim.Reduced)
                    {
                        programmaticScroll = false;
                        return false;
                    }
                    currentY += (targetY - currentY) * KitAnim.Decay(dt, 0.056f);
                    bool more = true;
                    if (Math.Abs(targetY - currentY) < 0.45f)
                    {
                        currentY = targetY;
                        programmaticScroll = false;
                        more = false;
                    }
                    int nextY = (int)Math.Round(currentY);
                    if (nextY != -AutoScrollPosition.Y)
                    {
                        AutoScrollPosition = new Point(-AutoScrollPosition.X, nextY);
                        var ch = ScrollPositionChanged;
                        if (ch != null) ch();
                    }
                    return more;
                }

                public void StopScroll() { wheelPulse.Stop(); programmaticScroll = false; }

                public void SmoothScrollTo(int y)
                {
                    int maxY = Math.Max(0, VerticalScroll.Maximum - VerticalScroll.LargeChange + 1);
                    y = Math.Max(0, Math.Min(maxY, y));
                    if (Anim.Reduced || !Visible)
                    {
                        StopScroll();
                        AutoScrollPosition = new Point(-AutoScrollPosition.X, y);
                        return;
                    }
                    programmaticScroll = true;
                    currentY = -AutoScrollPosition.Y;
                    targetY = y;
                    if (Math.Abs(targetY - currentY) < 1f) { programmaticScroll = false; return; }
                    wheelPulse.Start();
                }

                protected override void OnMouseWheel(MouseEventArgs e)
                {
                    programmaticScroll = false;
                    var u = UserScrolled;
                    if (u != null) u();
                    if (Anim.Reduced || !VerticalScroll.Visible)
                    {
                        base.OnMouseWheel(e);
                        var ch = ScrollPositionChanged;
                        if (ch != null) ch();
                        return;
                    }
                    int maxY = Math.Max(0, VerticalScroll.Maximum - VerticalScroll.LargeChange + 1);
                    if (maxY <= 0) { base.OnMouseWheel(e); return; }
                    if (!wheelPulse.Running)
                    {
                        currentY = -AutoScrollPosition.Y;
                        targetY = currentY;
                    }
                    float step = Math.Max(42f, SystemInformation.MouseWheelScrollLines * 24f);
                    targetY = Math.Max(0f, Math.Min(maxY, targetY - (e.Delta / 120f) * step));
                    wheelPulse.Start();
                    var he = e as HandledMouseEventArgs;
                    if (he != null) he.Handled = true;
                }

                protected override void OnScroll(ScrollEventArgs se)
                {
                    StopScroll();
                    var u = UserScrolled;
                    if (u != null) u();
                    base.OnScroll(se);
                    var ch = ScrollPositionChanged;
                    if (ch != null) ch();
                }

                protected override void Dispose(bool disposing)
                {
                    if (disposing) wheelPulse.Dispose();
                    base.Dispose(disposing);
                }
            }

            // { id, title, glyph, sidebar group, tile color (Ink.Labels key) }; the Deskmate card leads
            static readonly string[][] Sections =
            {
                new[] { "about", "Deskmate", "\u0001", "0", "" },
                new[] { "general", "Umum", "\uE713", "1", "slate" },
                new[] { "look", "Tampilan", "\uE790", "1", "violet" },
                new[] { "island", "Island", "\uE737", "1", "sky" },
                new[] { "dock", "Dock & Taskbar", "\uE90E", "1", "emerald" },
                new[] { "messages", "Notifikasi", "\uE8BD", "2", "coral" },
                new[] { "work", "Tugas & Kalender", "\uE73A", "2", "amber" },
                new[] { "remind", "Cuaca & Pengingat", "\uEA8F", "2", "emerald" },
                new[] { "ai", "AI & Dikte", "\uE8F2", "3", "violet" },
                new[] { "agents", "Agen Coding", "\uE756", "3", "slate" },
                new[] { "ingat", "Ingatan", "\uE81C", "3", "sky" },
                new[] { "files", "Musik & Berkas", "\uE7B8", "3", "rose" },
                new[] { "system", "Perangkat & HP", "\uE9D9", "3", "sky" },
                new[] { "game", "Gamifikasi", "\uE7FC", "4", "amber" },
                new[] { "keys", "Pintasan & Aksesibilitas", "\uE765", "4", "slate" },
            };

            // Pages that merge builders; later parts get a part title
            static readonly Dictionary<string, string[]> Parts = new Dictionary<string, string[]>
            {
                { "island", new[] { "island", "tabs" } },
                { "game", new[] { "combo", "pet" } },
                { "work", new[] { "work", "integrasi" } },
                { "ai", new[] { "ai", "dikte" } },
                { "system", new[] { "system", "phone" } },
            };

            static bool IsTabbedPage(string page) { return Parts.ContainsKey(page); }
            readonly Dictionary<string, string> activeSubTab = new Dictionary<string, string>();

            static readonly Dictionary<string, string> PartTitles = new Dictionary<string, string>
            {
                { "island", "Island" },
                { "tabs", "Tab dan kartu" },
                { "combo", "Ketikan" },
                { "pet", "Pet" },
                { "work", "Tugas & Fokus" },
                { "integrasi", "Integrasi dan kalender" },
                { "ai", "Layanan AI" },
                { "dikte", "Dikte dan rapat" },
                { "system", "Perangkat" },
                { "phone", "HP Android" },
            };

            // { glyph, tile color key, short subtitle } for each part of a merged page
            static readonly Dictionary<string, string[]> PartMeta = new Dictionary<string, string[]>
            {
                { "island", new[] { "\uE737", "sky", "Posisi, bentuk, ukuran, layar, perilaku, dan saat diam" } },
                { "tabs", new[] { "\uECA5", "violet", "Gaya tab, urutan tab panel, dan kartu Ringkasan" } },
                { "combo", new[] { "\uE765", "amber", "Combo ketikan, efek suara, Fever Mode, XP, dan Sprint 30 detik" } },
                { "pet", new[] { "\uEB51", "emerald", "Hewan pendamping di dock, pet buatan sendiri, dan teman online" } },
                { "work", new[] { "\uE73A", "amber", "Daftar tugas lokal, catatan, fokus, dan prioritas harian" } },
                { "integrasi", new[] { "\uE71B", "sky", "Kulia, Notion, Todoist, iCal, dan Google Calendar" } },
                { "ai", new[] { "\uE8F2", "violet", "Penyedia teks, mesin suara, kunci API, dan model offline" } },
                { "dikte", new[] { "\uE720", "rose", "Tombol dikte, kamus, snippet, dan rekaman rapat" } },
                { "system", new[] { "\uE9D9", "sky", "HUD keyboard, baterai, flashdisk, unduhan, dan penjaga beban" } },
                { "phone", new[] { "\uE8EA", "emerald", "Hubungkan HP lewat Wi-Fi, kode QR, notifikasi, dan OTP" } },
            };

            static string[] PartsOf(string page) { string[] p; return Parts.TryGetValue(page, out p) ? p : new[] { page }; }

            static readonly Dictionary<string, string> SectionNotes = new Dictionary<string, string>
            {
                { "about", "Versi, pembaruan, tur, dan pembuat Deskmate" },
                { "general", "Bahasa, jam, mulai bersama Windows, pembaruan, dan data Deskmate" },
                { "look", "Tema, material kaca, warna aksen, paket tema, dan warna kustom" },
                { "island", "Letak, ukuran, perilaku, saat diam, serta tab dan kartu panel" },
                { "dock", "Bar aplikasi dan widget, atau pengganti taskbar" },
                { "messages", "Notifikasi pesan, aturan aplikasi, mode berbagi layar, VIP, dan Senyap" },
                { "work", "Tugas, fokus, catatan, serta Kulia, Notion, Todoist, dan iCal" },
                { "remind", "Lokasi cuaca, jadwal sholat, minum air, mata, dan peregangan" },
                { "ai", "Layanan AI, dikte, teleprompter, dan rapat" },
                { "agents", "Claude Code, Codex, dan agen coding lainnya" },
                { "ingat", "Memori aktivitas lokal: cari dan tanya apa yang sudah kamu kerjakan, plus MCP" },
                { "files", "Lirik musik, visualizer, rak berkas, clipboard, screenshot, dan Downloads" },
                { "system", "HUD keyboard, baterai, flashdisk, dan HP Android" },
                { "game", "Combo ketikan, XP, Sprint 30 detik, dan pet yang menemani di dock" },
                { "keys", "Pintasan keyboard, pembaca layar, dan kenyamanan gerak" },
            };

            static readonly Dictionary<string, string> SectionKeywords = new Dictionary<string, string>
            {
                { "about", "tentang versi github tur pembuat" },
                { "general", "bahasa jam startup windows update data ekspor impor reset" },
                { "look", "tema sistem gelap terang otomatis aksen warna wallpaper paket material kaca" },
                { "island", "notch dynamic island letak posisi ukuran mata jam diam tab kartu ringkasan home urutan" },
                { "dock", "dock taskbar widget aplikasi" },
                { "messages", "pesan notifikasi chat banner vip senyap dnd" },
                { "work", "tugas fokus prioritas catatan timer kulia notion todoist ical google kalender jadwal integrasi" },
                { "remind", "cuaca hujan kota sholat adzan minum mata peregangan pengingat" },
                { "ai", "ai openai gemini groq claude dikte suara mikrofon teleprompter rapat" },
                { "agents", "agen coding claude code codex kuota port" },
                { "ingat", "ingatan memori aktivitas riwayat pieces recall timeline cari tanya mcp ocr rapat transkrip privasi" },
                { "files", "musik lirik visualizer rak berkas clipboard screenshot downloads" },
                { "system", "sistem keyboard caps num baterai flashdisk usb bluetooth wifi hp android qr otp" },
                { "game", "gamifikasi game combo ketikan typing suara mekanikal fever xp sprint pet kucing anjing hewan aseprite teman" },
                { "keys", "pintasan hotkey shortcut aksesibilitas gerak pembaca layar" },
            };

            // Older ids and builder parts, resolved to the part they name
            static string AliasOf(string id)
            {
                switch (id)
                {
                    case "kulia": return "integrasi";
                    case "packs": return "look";
                    case "behave":
                    case "idle": return "island";
                    case "home": return "tabs";
                    case "music": return "files";
                    case "access": return "keys";
                    case "overview":
                    case "cat:0": return "general";
                    case "cat:1": return "look";
                    case "cat:2": return "island";
                    case "cat:3": return "dock";
                    case "cat:4": return "work";
                    case "gamification": return "game";
                    case "cat:5": return "about";
                }
                return id;
            }

            // Any id, old or new, to the sidebar page that shows it
            static string NormalizeSection(string id)
            {
                if (string.IsNullOrEmpty(id)) return "general";
                id = AliasOf(id);
                foreach (var kv in Parts) if (Array.IndexOf(kv.Value, id) >= 0) return kv.Key;
                foreach (var s in Sections) if (s[0] == id) return id;
                return "general";
            }
            [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern IntPtr SendMessage(IntPtr h, int msg, IntPtr w, string l);
            [DllImport("uxtheme.dll", CharSet = CharSet.Unicode)] static extern int SetWindowTheme(IntPtr hwnd, string app, string idList);
            [DllImport("user32.dll")] static extern bool RedrawWindow(IntPtr hwnd, IntPtr rect, IntPtr rgn, uint flags);
            const int WmSetRedraw = 0x000B;
            const uint RdwInvalidate = 0x1, RdwErase = 0x4, RdwAllChildren = 0x80, RdwUpdateNow = 0x100, RdwFrame = 0x400;

            public PrefsWindow(Island island)
            {
                this.island = island;
                settings = island.settings;
                Text = Lang.T("Pengaturan Deskmate");
                StartPosition = FormStartPosition.Manual;
                Icon = island.tray != null ? island.tray.Icon : null;
                preview.Tick += delegate { preview.Stop(); island.PreviewLayout(false); };
                FormClosed += delegate { preview.Stop(); FinishPageEntrance(); content.StopScroll(); settings.FlushSave(); island.PreviewLayout(false); };
                content.UserScrolled += delegate { partJumpActive = false; FinishPageEntrance(); };
                content.ScrollPositionChanged += delegate { SyncPartNavFromScroll(); };
                search.BorderStyle = BorderStyle.None;
                searchTimer.Tick += delegate { ApplySearch(); };
                search.TextChanged += delegate
                {
                    searchTimer.Stop();
                    if (building) return;
                    PlaceSearchBox();
                    Invalidate(Rectangle.Ceiling(SearchWell()));
                    string query = search.Text.Trim();
                    if (query.Length == 0) ApplySearch();
                    else if (query != appliedSearch) searchTimer.Start();
                };
                search.GotFocus += delegate { BeginInvoke(new Action(EnsureSearchIndex)); PlaceSearchBox(); Invalidate(TopBarRect()); };
                search.LostFocus += delegate { PlaceSearchBox(); Invalidate(TopBarRect()); };
                hub.Picked += id => EnterSection(id, null, true);
                search.KeyDown += delegate(object s, KeyEventArgs e)
                {
                    if (e.KeyCode == Keys.Enter && searchTimer.Enabled)
                    {
                        ApplySearch();
                        e.SuppressKeyPress = true;
                    }
                    else if (e.KeyCode == Keys.Escape && search.Text.Length > 0)
                    {
                        search.Text = "";
                        e.SuppressKeyPress = true;
                    }
                };
                hub.Visible = true;
                content.Visible = false;
                Controls.Add(search);
                Controls.Add(hub);
                Controls.Add(content);
                Controls.Add(pageTransition);
            }

            void EnterSection(string id, string part, bool animate, int dir = 1)
            {
                string next = NormalizeSection(id);
                if (!inHub && section != "search" && section != next)
                {
                    navBack.Push(section);
                    navForward.Clear();
                }
                inHub = false;
                section = next;
                focusPart = part;
                building = true;
                appliedSearch = "";
                search.Text = "";
                building = false;
                hub.Visible = false;
                content.Visible = true;
                animateNextFill = animate;
                nextEntranceDir = dir;
                Invalidate(TopBarRect());
                if (IsHandleCreated) Fill();
            }

            void ShowHub(bool animate)
            {
                FinishPageEntrance();
                content.StopScroll();
                navBack.Clear();
                navForward.Clear();
                inHub = true;
                building = true;
                appliedSearch = "";
                search.Text = "";
                building = false;
                content.Visible = false;
                hub.Visible = true;
                if (!hub.HasCards) BindHub(section);
                else hub.UpdateStatuses(HubStatus);
                if (animate && IsHandleCreated && Visible) hub.PlayEntrance(section);
                PlaceSearchBox();
                Invalidate(TopBarRect());
                if (IsHandleCreated && Visible) hub.Focus();
            }

            void ApplySearch()
            {
                searchTimer.Stop();
                if (building || IsDisposed) return;
                string query = search.Text.Trim();
                if (query == appliedSearch) return;
                appliedSearch = query;
                if (query.Length > 0)
                {
                    if (section != "search") { beforeSearch = section; beforeSearchInHub = inHub; }
                    inHub = false;
                    section = "search";
                    hub.Visible = false;
                    content.Visible = true;
                    Invalidate(TopBarRect());
                    Fill();
                }
                else if (section == "search")
                {
                    section = beforeSearch;
                    if (beforeSearchInHub) ShowHub(true);
                    else
                    {
                        inHub = false;
                        hub.Visible = false;
                        content.Visible = true;
                        Invalidate(TopBarRect());
                        Fill();
                    }
                }
            }

            protected override void OnShown(EventArgs e)
            {
                base.OnShown(e);
                if (inHub)
                {
                    hub.PlayEntrance(section);
                    hub.Focus();
                }
                BeginInvoke(new Action(delegate { island.OfferWhatsNew(this); }));
            }

            string selectedNotifAppId, selectedNotifAppName;

            public void ShowNotifApp(string id, string name)
            {
                selectedNotifAppId = id;
                selectedNotifAppName = name;
                Go("messages");
                content.AutoScrollPosition = Point.Empty;
            }

            public void RefreshNotifApps()
            {
                if (searchTimer.Enabled || inHub || (section != "messages" && section != "search")) return;
                var at = content.AutoScrollPosition;
                Fill();
                content.AutoScrollPosition = new Point(-at.X, -at.Y);
            }

            public void Go(string id)
            {
                if (id == "hub")
                {
                    ShowHub(false);
                    return;
                }
                inHub = false;
                section = NormalizeSection(id);
                focusPart = string.IsNullOrEmpty(id) ? null : AliasOf(id);
                building = true;
                appliedSearch = "";
                search.Text = "";
                building = false;
                hub.Visible = false;
                content.Visible = true;
                Invalidate(TopBarRect());
                if (IsHandleCreated) Fill();
            }

            protected override void OnHandleCreated(EventArgs e)
            {
                base.OnHandleCreated(e);
                var area = Screen.FromHandle(Handle).WorkingArea;
                int chromeW = Width - ClientSize.Width, chromeH = Height - ClientSize.Height;
                float wantW = 960, wantH = 700;
                Size = new Size((int)Math.Min(wantW * Ui + chromeW, area.Width - 40), (int)Math.Min(wantH * Ui + chromeH, area.Height - 40));
                MinimumSize = new Size((int)(680 * Ui), (int)(460 * Ui));
                Location = new Point(area.X + (area.Width - Width) / 2, area.Y + (area.Height - Height) / 2);
                Relayout();
            }

            protected override void OnResize(EventArgs e)
            {
                base.OnResize(e);
                if (!IsHandleCreated) return;
                Place();
                if (inHub)
                {
                    hub.Invalidate();
                }
                else
                {
                    if (content.Controls.Count == 0) Fill();
                    else ReflowContent();
                }
            }

            // Reposition existing page controls on window resize without destroying or losing scroll/state
            void ReflowContent()
            {
                FinishPageEntrance();
                int room = content.Width - SystemInformation.VerticalScrollBarWidth;
                int newRowW = Math.Max((int)(280 * Ui), Math.Min((int)(840 * Ui), room - (int)(52 * Ui)));
                int newLeft = Math.Max((int)(26 * Ui), (room - newRowW) / 2);
                if (newRowW == rowW && newLeft == left) return;
                rowW = newRowW;
                left = newLeft;
                content.SuspendLayout();
                foreach (Control c in content.Controls)
                {
                    var head = c as PageHeader;
                    if (head != null) { head.Left = left; head.Width = rowW; head.Invalidate(); continue; }
                    var cap = c as DeckCaption;
                    if (cap != null) { cap.Left = left; cap.Width = rowW; cap.Invalidate(); continue; }
                    var banner = c as PartBanner;
                    if (banner != null) { banner.Left = left; banner.Width = rowW; banner.Reflow(); continue; }
                    var foot = c as SubMenuFooter;
                    if (foot != null) { foot.Left = left; foot.Width = rowW; foot.Invalidate(); continue; }
                    var empty = c as SearchEmptyCard;
                    if (empty != null) { empty.Left = left; empty.Width = rowW; empty.Reflow(); continue; }
                    if (c == partNav && partNav != null) { partNav.Left = left; partNav.Width = rowW; partNav.Invalidate(); continue; }
                    var g = c as KitGroup;
                    if (g != null)
                    {
                        g.Left = left;
                        g.Width = rowW;
                        foreach (Control child in g.Controls)
                        {
                            var r = child as KitRow;
                            if (r == null) continue;
                            r.Width = rowW;
                            PlaceField(r);
                        }
                        g.Invalidate(true);
                        continue;
                    }
                    var lbl = c as Label;
                    if (lbl != null && lbl.Tag is int)
                        lbl.Left = left + (int)lbl.Tag;
                }
                content.ResumeLayout();
            }

            // Rebuild current page content in place (preserving scroll) without clearing searchIndex
            void RefreshContent()
            {
                if (!IsHandleCreated || IsDisposed) return;
                if (inHub)
                {
                    hub.UpdateStatuses(HubStatus);
                    return;
                }
                var at = content.AutoScrollPosition;
                bool frozen = content.IsHandleCreated && content.Visible && !redrawFrozen;
                if (frozen) { redrawFrozen = true; SendMessage(content.Handle, WmSetRedraw, IntPtr.Zero, null); }
                try
                {
                    animateNextFill = false;
                    Fill();
                    if (at.Y != 0)
                    {
                        foreach (Control c in content.Controls) if (!c.IsHandleCreated) { var h = c.Handle; }
                        content.AutoScrollPosition = new Point(-at.X, -at.Y);
                    }
                }
                finally
                {
                    if (frozen)
                    {
                        redrawFrozen = false;
                        SendMessage(content.Handle, WmSetRedraw, (IntPtr)1, null);
                        RedrawWindow(content.Handle, IntPtr.Zero, IntPtr.Zero, RdwErase | RdwFrame | RdwInvalidate | RdwAllChildren | RdwUpdateNow);
                    }
                }
            }

            void BindHub(string focusId)
            {
                var decks = new[]
                {
                    Lang.T("Utama & Tampilan"),
                    Lang.T("Produktivitas & Harian"),
                    Lang.T("AI, Agen & Sistem"),
                    Lang.T("Bermain, Kontrol & Info"),
                };
                // Curated Bento deck order: featured Hero cards lead
                var spec = new[]
                {
                    // Deck 0: Hero pair + Wide pair
                    new object[] { "look", 0, true, false, true },
                    new object[] { "island", 0, true, false, false },
                    new object[] { "dock", 0, false, true, false },
                    new object[] { "general", 0, false, true, false },
                    // Deck 1: Productivity & Daily
                    new object[] { "messages", 1, false, false, false },
                    new object[] { "work", 1, false, false, false },
                    new object[] { "remind", 1, false, false, false },
                    // Deck 2: AI featured (2-col) + Agents + Memory, Files, Devices
                    new object[] { "ai", 2, true, false, false },
                    new object[] { "agents", 2, false, false, false },
                    new object[] { "ingat", 2, false, false, false },
                    new object[] { "files", 2, false, false, false },
                    new object[] { "system", 2, false, false, false },
                    // Deck 3: Play, Controls & About
                    new object[] { "game", 3, false, false, false },
                    new object[] { "keys", 3, false, false, false },
                    new object[] { "about", 3, false, false, false },
                };
                var cards = new List<SettingsHub.Card>();
                for (int i = 0; i < spec.Length; i++)
                {
                    string id = (string)spec[i][0];
                    string[] sec = FindSection(id);
                    if (sec == null) continue;
                    string note;
                    SectionNotes.TryGetValue(id, out note);
                    cards.Add(new SettingsHub.Card
                    {
                        Id = id,
                        Title = Lang.T(sec[1]),
                        Subtitle = note != null ? Lang.T(note) : "",
                        Status = HubStatus(id),
                        Glyph = sec[2].Length > 0 ? sec[2][0] : '\uE713',
                        TileKey = sec[4],
                        Deck = (int)spec[i][1],
                        Featured = (bool)spec[i][2],
                        Wide = (bool)spec[i][3],
                        ShowPaletteDots = (bool)spec[i][4],
                    });
                }
                hub.Bind(cards, decks, focusId);
            }

            static string[] FindSection(string id)
            {
                for (int i = 0; i < Sections.Length; i++)
                    if (Sections[i][0] == id) return Sections[i];
                return null;
            }

            // Real-time live status badge for each Hub card (zero filler stats)
            string HubStatus(string id)
            {
                switch (id)
                {
                    case "look":
                    {
                        string theme = island.ThemeMode();
                        string tName = theme == "terang" ? Lang.T("Terang") : theme == "gelap" ? Lang.T("Gelap") : Lang.T("Sistem");
                        string mat = settings.Get("material", "frost");
                        string mName = mat == "solid" ? "Solid" : mat == "liquid" ? "Liquid" : mat == "clear" ? Lang.T("Bening") : mat == "calm" ? Lang.T("Kalem") : "Frost";
                        return tName + " · " + mName;
                    }
                    case "island":
                    {
                        string mode = settings.Get("mini", "auto");
                        return mode == "always" ? Lang.T("Mini") : mode == "off" ? Lang.T("Penuh") : Lang.T("Otomatis");
                    }
                    case "dock":
                    {
                        bool on = settings.GetBool("dockbar.on", false);
                        if (!on) return Lang.T("Mati");
                        return settings.GetBool("dockbar.replace", false) ? Lang.T("Ganti Taskbar") : Lang.T("Aktif");
                    }
                    case "general":
                        return (Lang.English ? "EN" : "ID") + " · " + (settings.GetBool("clock.24", true) ? Lang.T("24 jam") : Lang.T("12 jam"));
                    case "messages":
                        return settings.GetBool("notif", true) ? Lang.T("Aktif") : Lang.T("Mati");
                    case "work":
                        return Lang.T("Tugas & Fokus");
                    case "remind":
                    {
                        string city = Cities.NameOf(settings.Get("pray.city", ""));
                        if (!string.IsNullOrEmpty(city)) return city;
                        return settings.GetBool("pray", false) ? Lang.T("Sholat") : Lang.T("Cuaca");
                    }
                    case "ai":
                    {
                        bool dict = settings.GetBool("dictate", false);
                        return dict ? Lang.T("Dikte Aktif") : Lang.T("Siap");
                    }
                    case "agents":
                        return "Claude · Codex";
                    case "ingat":
                        return settings.GetBool("ingat", false) ? Lang.T("Aktif") : Lang.T("Mati");
                    case "files":
                        return settings.GetBool("lyrics", true) ? Lang.T("Lirik & Rak") : Lang.T("Rak Berkas");
                    case "system":
                        return Lang.T("HUD & HP");
                    case "game":
                    {
                        bool combo = settings.GetBool("keys.combo", false);
                        bool pet = settings.GetBool("pet.on", false);
                        if (combo && pet) return "Combo · Pet";
                        if (combo) return "Combo";
                        if (pet) return "Pet";
                        return Lang.T("Mati");
                    }
                    case "keys":
                    {
                        string feel = settings.Get("motion.feel", "bawaan");
                        return feel == "lincah" ? Lang.T("Lincah") : feel == "tenang" ? Lang.T("Tenang") : Lang.T("Bawaan");
                    }
                    case "about":
                        return "v" + AppVersion;
                }
                return "";
            }

            public override void Relayout()
            {
                BackColor = KitInk.Window;
                Native.DarkFrame(Handle, !Ink.Light);
                // Scrollbar follows the theme
                try { SetWindowTheme(content.Handle, Ink.Light ? "Explorer" : "DarkMode_Explorer", null); } catch (Exception) { }
                Text = Lang.T("Pengaturan Deskmate");
                search.Font = Ink.Text(12.5f * Ui);
                search.BackColor = KitInk.Hover;
                search.ForeColor = KitInk.Text;
                // Hint stays while focused
                SendMessage(search.Handle, 0x1501, (IntPtr)1, Lang.T("Cari pengaturan"));
                searchIndex.Clear();
                BindHub(section);
                Place();
                if (!inHub)
                {
                    // A setting changed in place: the page rebuilds where the reader was,
                    // drawn once when done instead of row by row from the top
                    var at = content.AutoScrollPosition;
                    bool frozen = content.IsHandleCreated && content.Visible && !redrawFrozen;
                    if (frozen) { redrawFrozen = true; SendMessage(content.Handle, WmSetRedraw, IntPtr.Zero, null); }
                    try
                    {
                        animateNextFill = false;
                        Fill();
                        if (at.Y != 0)
                        {
                            foreach (Control c in content.Controls) if (!c.IsHandleCreated) { var h = c.Handle; }
                            content.AutoScrollPosition = new Point(-at.X, -at.Y);
                        }
                    }
                    finally
                    {
                        if (frozen)
                        {
                            redrawFrozen = false;
                            SendMessage(content.Handle, WmSetRedraw, (IntPtr)1, null);
                            RedrawWindow(content.Handle, IntPtr.Zero, IntPtr.Zero, RdwErase | RdwFrame | RdwInvalidate | RdwAllChildren | RdwUpdateNow);
                        }
                    }
                }
                Invalidate(false);
                hub.Invalidate();
            }

            // Fixed top command bar height, 1x px
            const float TopBarH = 56;

            Rectangle TopBarRect() { return new Rectangle(0, 0, ClientSize.Width, (int)Math.Ceiling(TopBarH * Ui) + 2); }

            void PlaceSearchBox()
            {
                float s = Ui;
                var sw = SearchWell();
                int h = search.PreferredHeight;
                bool showHint = !search.Focused && search.Text.Length == 0;
                float rightPad = showHint ? 56f * s : 28f * s;
                search.SetBounds(
                    (int)(sw.X + 28 * s),
                    (int)Math.Round(sw.Y + sw.Height / 2f - h / 2f + 1 * s),
                    Math.Max(40, (int)(sw.Width - 28 * s - rightPad)),
                    h);
            }

            void Place()
            {
                float s = Ui;
                int topH = (int)(TopBarH * s);
                PlaceSearchBox();
                hub.SetBounds(0, topH, Math.Max(10, ClientSize.Width), Math.Max(10, ClientSize.Height - topH));
                content.SetBounds(0, topH, Math.Max(10, ClientSize.Width), Math.Max(10, ClientSize.Height - topH));
                content.BackColor = KitInk.Window;
                hub.Invalidate();
                Invalidate(TopBarRect());
            }

            RectangleF SearchWell()
            {
                float s = Ui;
                float w = Math.Max(180 * s, Math.Min(270 * s, ClientSize.Width * 0.30f));
                float h = 30 * s;
                return new RectangleF(ClientSize.Width - w - 20 * s, (TopBarH * s - h) / 2f, w, h);
            }

            RectangleF SearchClearRect()
            {
                var r = SearchWell();
                return new RectangleF(r.Right - 26 * Ui, r.Y + 2 * Ui, 24 * Ui, r.Height - 4 * Ui);
            }

            RectangleF BackButtonRect()
            {
                float s = Ui;
                string label = Lang.T("Menu Utama");
                int tw = TextRenderer.MeasureText(label, Ink.Semi(12.5f * s)).Width;
                float w = tw + 68 * s;
                float h = 32 * s;
                return new RectangleF(18 * s, (TopBarH * s - h) / 2f, w, h);
            }

            protected override void OnMouseMove(MouseEventArgs e)
            {
                base.OnMouseMove(e);
                bool oClear = search.Text.Length > 0 && SearchClearRect().Contains(e.Location);
                bool oBack = !inHub && BackButtonRect().Contains(e.Location);
                if (oClear == overClear && oBack == overBack) return;
                overClear = oClear;
                overBack = oBack;
                Cursor = (overClear || overBack) ? Cursors.Hand : Cursors.Default;
                Invalidate(TopBarRect());
            }

            protected override void OnMouseLeave(EventArgs e)
            {
                base.OnMouseLeave(e);
                if (overClear || overBack || downBack)
                {
                    overClear = overBack = downBack = false;
                    Cursor = Cursors.Default;
                    Invalidate(TopBarRect());
                }
            }

            protected override void OnMouseDown(MouseEventArgs e)
            {
                base.OnMouseDown(e);
                if (e.Button != MouseButtons.Left) return;
                if (!inHub && BackButtonRect().Contains(e.Location))
                {
                    downBack = true;
                    Invalidate(TopBarRect());
                    return;
                }
                if (SearchWell().Contains(e.Location) && !(search.Text.Length > 0 && SearchClearRect().Contains(e.Location)))
                {
                    search.Focus();
                }
            }

            protected override void OnMouseUp(MouseEventArgs e)
            {
                base.OnMouseUp(e);
                bool wasDownBack = downBack;
                if (downBack) { downBack = false; Invalidate(TopBarRect()); }
                if (e.Button != MouseButtons.Left) return;
                if (wasDownBack && !inHub && BackButtonRect().Contains(e.Location))
                {
                    ShowHub(true);
                    return;
                }
                if (search.Text.Length > 0 && SearchClearRect().Contains(e.Location))
                {
                    search.Text = "";
                    search.Focus();
                }
            }

            protected override void OnPaintBackground(PaintEventArgs e)
            {
                var g = e.Graphics;
                float s = Ui;
                int topH = (int)(TopBarH * s);
                g.Clear(KitInk.Window);
                g.FillRectangle(KitInk.BrushOf(KitInk.Side), 0, 0, ClientSize.Width, topH);
                g.DrawLine(KitInk.PenOf(KitInk.Line, 1f), 0, topH - 1, ClientSize.Width, topH - 1);
                KitInk.Smooth(g);

                var sw = SearchWell();

                if (inHub)
                {
                    // Brand mark + Pengaturan title + version pill
                    var ico = new RectangleF(20 * s, (topH - 26 * s) / 2f, 26 * s, 26 * s);
                    g.DrawIcon(KitWindow.AppIcon, Rectangle.Round(ico));
                    string title = Lang.T("Pengaturan");
                    Font tf = Ink.Semi(15.5f * s);
                    int tw = TextRenderer.MeasureText(title, tf).Width;
                    KitInk.Draw(g, title, tf, KitInk.Text, new RectangleF(54 * s, 0, tw + 8 * s, topH), ContentAlignment.MiddleLeft);
                    string ver = "v" + AppVersion;
                    Font vf = Ink.Semi(10.5f * s);
                    int vw = TextRenderer.MeasureText(ver, vf).Width;
                    var vr = new RectangleF(54 * s + tw + 10 * s, (topH - 20 * s) / 2f, vw + 14 * s, 20 * s);
                    if (vr.Right < sw.Left - 14 * s)
                    {
                        KitInk.FillRound(g, vr, 10 * s, KitInk.Hover);
                        KitInk.Draw(g, ver, vf, KitInk.Sub, vr, ContentAlignment.MiddleCenter);
                    }
                }
                else
                {
                    // Tactile "← Menu Utama [Esc]" back button + breadcrumb
                    var br = BackButtonRect();
                    Color secTile = SectionTileColor(section);
                    Color bBg = downBack ? Ink.Mix(KitInk.Hover, secTile, 0.22f)
                        : overBack ? Ink.Mix(KitInk.Group, secTile, Ink.Light ? 0.10f : 0.16f)
                        : KitInk.Group;
                    Color bEdge = overBack || downBack ? Ink.Mix(KitInk.Line, secTile, 0.65f) : KitInk.Line;
                    KitInk.FillRound(g, br, 9 * s, bBg);
                    KitInk.StrokeRound(g, br, 9 * s, bEdge, 1f * s);
                    KitInk.Draw(g, "\uE72B", Ink.Icons(12f * s), overBack ? secTile : KitInk.Text, new RectangleF(br.X + 8 * s, br.Y, 18 * s, br.Height), ContentAlignment.MiddleCenter);
                    KitInk.Draw(g, Lang.T("Menu Utama"), Ink.Semi(12.5f * s), KitInk.Text, new RectangleF(br.X + 27 * s, br.Y, br.Width - 62 * s, br.Height), ContentAlignment.MiddleLeft);
                    var escR = new RectangleF(br.Right - 32 * s, br.Y + 6 * s, 26 * s, br.Height - 12 * s);
                    KitInk.FillRound(g, escR, 5 * s, Ink.Mix(bBg, KitInk.Text, Ink.Light ? 0.06f : 0.09f));
                    KitInk.StrokeRound(g, escR, 5 * s, KitInk.Line, 1f * s);
                    KitInk.Draw(g, "Esc", Ink.Semi(9f * s), KitInk.Faint, escR, ContentAlignment.MiddleCenter);

                    // Breadcrumb separator + current section badge & title
                    float bx = br.Right + 10 * s;
                    if (bx + 80 * s < sw.Left)
                    {
                        KitInk.Draw(g, "\uE76C", Ink.Icons(9.5f * s), KitInk.Faint, new RectangleF(bx, 0, 14 * s, topH), ContentAlignment.MiddleCenter);
                        bx += 18 * s;
                        if (section == "search")
                        {
                            KitInk.Draw(g, Lang.T("Cari pengaturan"), Ink.Semi(13f * s), KitInk.Sub, new RectangleF(bx, 0, Math.Max(40 * s, sw.Left - bx - 12 * s), topH), ContentAlignment.MiddleLeft);
                        }
                        else
                        {
                            var sec = FindSection(section);
                            if (sec != null)
                            {
                                var tileR = new RectangleF(bx, (topH - 20 * s) / 2f, 20 * s, 20 * s);
                                if (sec[2].Length > 0 && sec[2][0] == KitRow.MarkGlyph)
                                    g.DrawIcon(KitWindow.AppIcon, Rectangle.Round(tileR));
                                else
                                {
                                    Color tc = sec[4].Length > 0 ? Ink.Label(sec[4], true) : Ink.Label("slate", true);
                                    KitInk.FillRound(g, tileR, 5 * s, tc);
                                    KitInk.Draw(g, sec[2], Ink.Icons(11f * s), Ink.On(tc), tileR, ContentAlignment.MiddleCenter);
                                }
                                bx += 28 * s;
                                KitInk.Draw(g, Lang.T(sec[1]), Ink.Semi(13.5f * s), KitInk.Text, new RectangleF(bx, 0, Math.Max(40 * s, sw.Left - bx - 12 * s), topH), ContentAlignment.MiddleLeft);
                            }
                        }
                    }
                }

                // Search pill well on the right of Top Command Bar
                KitInk.FillRound(g, sw, 9 * s, search.BackColor);
                if (search.Focused) KitInk.StrokeRound(g, sw, 9 * s, KitInk.Focus, 1.5f * s);
                else KitInk.StrokeRound(g, sw, 9 * s, KitInk.Line, 1f * s);
                KitInk.Draw(g, "\uE721", Ink.Icons(12f * s), KitInk.Sub, new RectangleF(sw.X + 4 * s, sw.Y, 22 * s, sw.Height), ContentAlignment.MiddleCenter);
                if (search.Text.Length > 0)
                {
                    var cr = SearchClearRect();
                    if (overClear) KitInk.FillRound(g, cr, 6 * s, KitInk.Hover);
                    KitInk.Draw(g, "\uE711", Ink.Icons(10f * s), overClear ? KitInk.Text : KitInk.Sub, cr, ContentAlignment.MiddleCenter);
                }
                else if (!search.Focused)
                {
                    var kr = new RectangleF(sw.Right - 50 * s, sw.Y + 5 * s, 44 * s, sw.Height - 10 * s);
                    KitInk.FillRound(g, kr, 5 * s, KitInk.Group);
                    KitInk.StrokeRound(g, kr, 5 * s, KitInk.Line, 1f * s);
                    KitInk.Draw(g, "Ctrl+F", Ink.Semi(9.5f * s), KitInk.Faint, kr, ContentAlignment.MiddleCenter);
                }
            }

            // Page builder: title, then groups of rows

            float y;
            int rowW, left;

            void Fill()
            {
                if (!IsHandleCreated) return;
                bool doEntrance = animateNextFill;
                animateNextFill = false;
                FinishPageEntrance();
                content.StopScroll();
                building = true;
                bool freeze = content.IsHandleCreated && content.Visible && !redrawFrozen;
                if (freeze) { redrawFrozen = true; SendMessage(content.Handle, WmSetRedraw, IntPtr.Zero, null); }
                try
                {
                    content.SuspendLayout();
                    var old = new List<Control>();
                    foreach (Control c in content.Controls) old.Add(c);
                    content.Controls.Clear();
                    foreach (var c in old) c.Dispose();
                    content.AutoScrollPosition = Point.Empty;
                    // Outer width: the old page's scrollbar must not narrow rows. One centred column, at most 840
                    int room = content.Width - SystemInformation.VerticalScrollBarWidth;
                    rowW = Math.Max((int)(280 * Ui), Math.Min((int)(840 * Ui), room - (int)(52 * Ui)));
                    left = Math.Max((int)(26 * Ui), (room - rowW) / 2);
                    y = Ink.Sp20 * Ui;
                    partTop.Clear();
                    partNav = null;
                    partJumpActive = false;
                    activeHeader = null;
                    activeGroupTile = SectionTileColor(section);
                    activeParts = new string[0];
                    if (section == "search")
                    {
                        string q = appliedSearch;
                        var searchHead = new PageHeader
                        {
                            Glyph = '\uE721',
                            Title = Lang.F("Hasil untuk \"{0}\"", q),
                            Note = Lang.T("Menampilkan pengaturan yang cocok di semua kategori"),
                            Tile = KitInk.Accent,
                            Status = "",
                        };
                        searchHead.SetBounds(left, (int)y, rowW, (int)(86 * Ui));
                        content.Controls.Add(searchHead);
                        activeHeader = searchHead;
                        y += (86 + Ink.Sp20) * Ui;

                        int found = 0;
                        EnsureSearchIndex();
                        foreach (var s in Sections)
                        {
                            string kw;
                            SectionKeywords.TryGetValue(s[0], out kw);
                            bool secMatch = Lang.T(s[1]).IndexOf(q, StringComparison.CurrentCultureIgnoreCase) >= 0 || (kw != null && kw.IndexOf(q, StringComparison.CurrentCultureIgnoreCase) >= 0);
                            var rows = new List<KitRow>();
                            foreach (var part in PartsOf(s[0])) if (secMatch || PartMatches(part, q)) Build(part, rows, false);
                            var hits = secMatch ? rows : rows.FindAll(r => r.Title.IndexOf(q, StringComparison.CurrentCultureIgnoreCase) >= 0 || r.Detail.IndexOf(q, StringComparison.CurrentCultureIgnoreCase) >= 0);
                            if (!secMatch) foreach (var r in rows) if (!hits.Contains(r)) { if (r.Field != null && !r.Field.IsDisposed) r.Field.Dispose(); r.Dispose(); }
                            if (hits.Count == 0) continue;
                            found += hits.Count;
                            activeGroupTile = SectionTileColor(s[0]);
                            Caption(Lang.T(s[1]));
                            Group(hits);
                        }
                        if (found > 0)
                        {
                            searchHead.Status = Lang.F("{0} hasil", found);
                            searchHead.Invalidate();
                        }
                        else
                        {
                            searchHead.Status = Lang.T("0 hasil");
                            searchHead.Invalidate();
                            var empty = new SearchEmptyCard(
                                Lang.T("Tidak ada yang cocok"),
                                Lang.T("Coba kata kunci lain seperti tema, dock, sholat, atau dikte"),
                                Ui);
                            empty.SetBounds(left, (int)y, rowW, empty.Height);
                            empty.Reflow();
                            content.Controls.Add(empty);
                            y += empty.Height + Ink.Sp20 * Ui;
                        }
                    }
                    else
                    {
                        section = NormalizeSection(section);
                        activeGroupTile = SectionTileColor(section);
                        Head(section);
                        var parts = PartsOf(section);
                        activeParts = parts;
                        if (IsTabbedPage(section) && parts.Length > 1)
                        {
                            string curTab = null;
                            if (!string.IsNullOrEmpty(focusPart) && Array.IndexOf(parts, focusPart) >= 0) curTab = focusPart;
                            else if (!activeSubTab.TryGetValue(section, out curTab) || Array.IndexOf(parts, curTab) < 0) curTab = parts[0];
                            activeSubTab[section] = curTab;
                            int tabIdx = Math.Max(0, Array.IndexOf(parts, curTab));
                            AddPartSwitcher(parts, tabIdx);
                            BuildTabbedPart(curTab);
                        }
                        else
                        {
                            if (parts.Length > 1) AddPartSwitcher(parts, 0);
                            for (int i = 0; i < parts.Length; i++)
                            {
                                partTop[parts[i]] = i == 0 ? 0 : Math.Max(0, (int)(y + Ink.Sp8 * Ui));
                                string[] meta;
                                if (PartMeta.TryGetValue(parts[i], out meta) && meta != null && meta[1].Length > 0)
                                {
                                    var pt = Ink.Label(meta[1], true);
                                    if (!pt.IsEmpty) activeGroupTile = pt;
                                }
                                string title;
                                if (parts.Length > 1 && PartTitles.TryGetValue(parts[i], out title))
                                    PartTitle(parts[i], Lang.T(title), i > 0);
                                Build(parts[i], new List<KitRow>(), true);
                            }
                            AddSubMenuFooter(section);
                        }
                    }
                    // Room under the last group
                    var spacer = new Panel { Location = new Point(0, (int)y), Size = new Size(1, (int)(Ink.Sp16 * Ui)), BackColor = KitInk.Window };
                    content.Controls.Add(spacer);
                    content.ResumeLayout();
                    // A caller named a later part of this page: start there, once
                    int at;
                    bool jumped = false;
                    if (!IsTabbedPage(section) && focusPart != null && focusPart != PartsOf(section)[0] && partTop.TryGetValue(focusPart, out at))
                    {
                        // WinForms scrolls only children that have a window; make them all first
                        foreach (Control c in content.Controls) if (!c.IsHandleCreated) { var h = c.Handle; }
                        content.AutoScrollPosition = new Point(0, at);
                        int idx = Array.IndexOf(activeParts, focusPart);
                        if (partNav != null && idx >= 0) { syncingPartNav = true; partNav.Selected = idx; syncingPartNav = false; }
                        jumped = true;
                    }
                    focusPart = null;
                    bool wantEntrance = doEntrance && !jumped && Visible && !Anim.Reduced;
                    if (freeze)
                    {
                        redrawFrozen = false;
                        SendMessage(content.Handle, WmSetRedraw, (IntPtr)1, null);
                        if (wantEntrance) StartPageEntrance(null, nextEntranceDir);
                        RedrawWindow(content.Handle, IntPtr.Zero, IntPtr.Zero,
                            wantEntrance ? (RdwErase | RdwFrame | RdwInvalidate | RdwAllChildren) : (RdwErase | RdwFrame | RdwInvalidate | RdwAllChildren | RdwUpdateNow));
                        freeze = false;
                    }
                    else if (wantEntrance) StartPageEntrance(null, nextEntranceDir);
                }
                finally
                {
                    building = false;
                    if (freeze)
                    {
                        redrawFrozen = false;
                        SendMessage(content.Handle, WmSetRedraw, (IntPtr)1, null);
                        RedrawWindow(content.Handle, IntPtr.Zero, IntPtr.Zero, RdwErase | RdwFrame | RdwInvalidate | RdwAllChildren | RdwUpdateNow);
                    }
                }
            }

            // Lightweight single-bitmap viewport slide+fade: controls stay at final positions with zero HWND thrash
            void StartPageEntrance(Control afterControl = null, int slideDir = 1)
            {
                FinishPageEntrance();
                if (IsDisposed || !Visible || !content.Visible || Anim.Reduced) return;
                int clipTop = afterControl != null ? Math.Max(0, afterControl.Bottom) : 0;
                int vw = content.ClientSize.Width;
                int vh = content.ClientSize.Height - clipTop;
                if (vw < 24 || vh < 24) return;
                Bitmap snap = null;
                try
                {
                    snap = new Bitmap(vw, vh, System.Drawing.Imaging.PixelFormat.Format32bppPArgb);
                    using (var sg = Graphics.FromImage(snap))
                    {
                        sg.Clear(KitInk.Window);
                        foreach (Control c in content.Controls)
                        {
                            if (!c.Visible || c.Width <= 0 || c.Height <= 0) continue;
                            if (c.Bottom <= clipTop || c.Top >= content.ClientSize.Height) continue;
                            if (!c.IsHandleCreated) { var h = c.Handle; }
                            using (var cb = new Bitmap(c.Width, c.Height, System.Drawing.Imaging.PixelFormat.Format32bppPArgb))
                            {
                                c.DrawToBitmap(cb, new Rectangle(0, 0, c.Width, c.Height));
                                sg.DrawImageUnscaled(cb, c.Left, c.Top - clipTop);
                            }
                        }
                    }
                    float offset = Math.Max(10f, 14f * Ui);
                    var bounds = new Rectangle(content.Left, content.Top + clipTop, vw, vh);
                    pageTransition.Play(snap, bounds, slideDir, offset);
                    snap = null;
                }
                catch (Exception)
                {
                    if (snap != null) snap.Dispose();
                }
            }

            void FinishPageEntrance()
            {
                pageTransition.Stop();
            }

            // Display changes only rebuild the visible display page; other pages keep their controls.
            internal void RefreshDisplayPicker()
            {
                if (IsDisposed || !Visible || building || inHub || section != "island") return;
                Fill();
            }

            static readonly Dictionary<string, int> labelPadCache = new Dictionary<string, int>();

            // Blank a Label draws before its first letter (cached per font + leading glyph)
            static int LabelPad(Label l)
            {
                string lead = string.IsNullOrEmpty(l.Text) ? "" : l.Text[0].ToString();
                string key = l.Font.Name + "|" + l.Font.Size.ToString("0.##", CultureInfo.InvariantCulture) + "|" + (int)l.Font.Style + "|" + lead;
                int cached;
                if (labelPadCache.TryGetValue(key, out cached)) return cached;
                var size = l.PreferredSize;
                if (size.Width <= 0 || size.Height <= 0) return 0;
                int pad = 0;
                using (var bmp = new Bitmap(size.Width, size.Height))
                {
                    l.Size = size;
                    l.DrawToBitmap(bmp, new Rectangle(Point.Empty, size));
                    var back = l.BackColor;
                    for (int x = 0; x < size.Width && pad == 0; x++)
                        for (int y = 0; y < size.Height; y++)
                        {
                            var c = bmp.GetPixel(x, y);
                            if (Math.Abs(c.R - back.R) + Math.Abs(c.G - back.G) + Math.Abs(c.B - back.B) > 90) { pad = x; break; }
                        }
                }
                if (labelPadCache.Count > 256) labelPadCache.Clear();
                labelPadCache[key] = pad;
                return pad;
            }

            Label Heading(string text)
            {
                // UseMnemonic off: an "&" in a title is text, not an access key
                var l = new Label { Text = text, Font = Ink.Display(20f * Ui), ForeColor = KitInk.Text, BackColor = KitInk.Window, AutoSize = true, UseMnemonic = false };
                // Ink on the group's edge
                int off = -LabelPad(l);
                l.Tag = off;
                l.Location = new Point(left + off, (int)y);
                content.Controls.Add(l);
                y += 46 * Ui;
                return l;
            }

            // Hero Banner matching the Hub Featured card language
            void Head(string page)
            {
                string note = null;
                string[] sec = FindSection(page);
                if (page != null) SectionNotes.TryGetValue(page, out note);
                var head = new PageHeader
                {
                    Glyph = sec != null ? sec[2][0] : '\uE713',
                    Title = sec != null ? Lang.T(sec[1]) : "",
                    Note = note != null ? Lang.T(note) : "",
                    Status = HubStatus(page),
                    Tile = sec != null && sec[4].Length > 0 ? Ink.Label(sec[4], true) : Color.Empty,
                    ShowPaletteDots = page == "look",
                };
                head.AccessibleName = head.Title;
                head.AccessibleDescription = head.Note;
                head.SetBounds(left, (int)y, rowW, (int)(86 * Ui));
                content.Controls.Add(head);
                activeHeader = head;
                tabFocus["head"] = head;
                y += (86 + Ink.Sp20) * Ui;
            }

            void RefreshHeroStatus()
            {
                if (activeHeader != null && !activeHeader.IsDisposed && !inHub && section != "search")
                {
                    string next = HubStatus(section);
                    if (activeHeader.Status != next)
                    {
                        activeHeader.Status = next;
                        activeHeader.Invalidate();
                    }
                }
            }

            protected override bool ProcessCmdKey(ref Message msg, Keys keys)
            {
                if (keys == (Keys.Control | Keys.F))
                {
                    search.Focus();
                    search.SelectAll();
                    return true;
                }
                if (keys == (Keys.Alt | Keys.Left))
                {
                    if (navBack.Count > 0)
                    {
                        string prev = navBack.Pop();
                        if (!inHub && section != "search") navForward.Push(section);
                        NavigateHistory(prev, -1);
                        return true;
                    }
                    if (!inHub)
                    {
                        ShowHub(true);
                        return true;
                    }
                }
                if (keys == (Keys.Alt | Keys.Right) && navForward.Count > 0)
                {
                    string next = navForward.Pop();
                    if (!inHub && section != "search") navBack.Push(section);
                    NavigateHistory(next, 1);
                    return true;
                }
                if (keys == Keys.Escape)
                {
                    if (search.Focused)
                    {
                        if (search.Text.Length > 0) search.Text = "";
                        else if (inHub) hub.Focus();
                        else content.Focus();
                        return true;
                    }
                    if (!inHub && !(ActiveControl is TextBoxBase) && !(ActiveControl is KitHotkey))
                    {
                        ShowHub(true);
                        return true;
                    }
                }
                return base.ProcessCmdKey(ref msg, keys);
            }

            void NavigateHistory(string target, int dir = 1)
            {
                inHub = false;
                section = NormalizeSection(target);
                focusPart = null;
                building = true;
                appliedSearch = "";
                search.Text = "";
                building = false;
                hub.Visible = false;
                content.Visible = true;
                Invalidate(TopBarRect());
                animateNextFill = true;
                nextEntranceDir = dir;
                Fill();
            }

            // Deskmate 2.0 big, build digits tiny
            KitRow VersionRow(Control field)
            {
                var r = Row("Deskmate " + AppVersion, Lang.T("Dynamic island untuk Windows · MIT"), field);
                string[] ver = AppVersion.Split('.');
                if (ver.Length > 2) { r.Title = "Deskmate " + ver[0] + "." + ver[1]; r.Suffix = "." + string.Join(".", ver, 2, ver.Length - 2); }
                return r;
            }

            // Section deck heading: category accent pill + real child Label + trailing hairline
            void Caption(string text)
            {
                var cap = new DeckCaption(text, activeGroupTile, Ui);
                cap.SetBounds(left, (int)y, rowW, cap.Height);
                content.Controls.Add(cap);
                y += cap.Height + 6 * Ui;
            }

            void BuildTabbedPart(string partId)
            {
                string[] meta;
                if (PartMeta.TryGetValue(partId, out meta) && meta != null && meta[1].Length > 0)
                {
                    var pt = Ink.Label(meta[1], true);
                    if (!pt.IsEmpty) activeGroupTile = pt;
                }
                Build(partId, new List<KitRow>(), true);
                AddSubMenuFooter(section);
            }

            void SwitchTabbedContent(string partId, int slideDir = 1)
            {
                if (!IsHandleCreated || IsDisposed || partNav == null || partNav.IsDisposed) return;
                FinishPageEntrance();
                content.StopScroll();
                building = true;
                bool freeze = content.IsHandleCreated && content.Visible && !redrawFrozen;
                if (freeze) { redrawFrozen = true; SendMessage(content.Handle, WmSetRedraw, IntPtr.Zero, null); }
                try
                {
                    content.SuspendLayout();
                    content.AutoScrollPosition = Point.Empty;
                    int keepIdx = content.Controls.IndexOf(partNav);
                    var drop = new List<Control>();
                    for (int i = keepIdx + 1; i < content.Controls.Count; i++) drop.Add(content.Controls[i]);
                    foreach (var c in drop) { content.Controls.Remove(c); c.Dispose(); }
                    y = partNav.Bottom + Ink.Sp16 * Ui;
                    BuildTabbedPart(partId);
                    var spacer = new Panel { Location = new Point(0, (int)y), Size = new Size(1, (int)(Ink.Sp16 * Ui)), BackColor = KitInk.Window };
                    content.Controls.Add(spacer);
                    content.ResumeLayout();
                    bool wantEntrance = Visible && !Anim.Reduced;
                    if (freeze)
                    {
                        redrawFrozen = false;
                        SendMessage(content.Handle, WmSetRedraw, (IntPtr)1, null);
                        if (wantEntrance) StartPageEntrance(partNav, slideDir);
                        RedrawWindow(content.Handle, IntPtr.Zero, IntPtr.Zero,
                            wantEntrance ? (RdwErase | RdwFrame | RdwInvalidate | RdwAllChildren) : (RdwErase | RdwFrame | RdwInvalidate | RdwAllChildren | RdwUpdateNow));
                        freeze = false;
                    }
                    else if (wantEntrance) StartPageEntrance(partNav, slideDir);
                }
                finally
                {
                    building = false;
                    if (freeze)
                    {
                        redrawFrozen = false;
                        SendMessage(content.Handle, WmSetRedraw, (IntPtr)1, null);
                        RedrawWindow(content.Handle, IntPtr.Zero, IntPtr.Zero, RdwErase | RdwFrame | RdwInvalidate | RdwAllChildren | RdwUpdateNow);
                    }
                }
            }

            // Icon-enhanced spring-gliding chapter bar right below the Hero banner on merged pages
            void AddPartSwitcher(string[] parts, int initialSelected)
            {
                var labels = new string[parts.Length];
                var glyphs = new char[parts.Length];
                var tiles = new Color[parts.Length];
                for (int i = 0; i < parts.Length; i++)
                {
                    string t;
                    labels[i] = PartTitles.TryGetValue(parts[i], out t) ? Lang.T(t) : parts[i];
                    string[] meta;
                    if (PartMeta.TryGetValue(parts[i], out meta) && meta != null)
                    {
                        glyphs[i] = meta[0].Length > 0 ? meta[0][0] : '\0';
                        tiles[i] = meta[1].Length > 0 ? Ink.Label(meta[1], true) : KitInk.Accent;
                    }
                    else
                    {
                        glyphs[i] = '\0';
                        tiles[i] = KitInk.Accent;
                    }
                }
                int h = (int)(38 * Ui);
                int lastIdx = initialSelected;
                var nav = new PartTabStrip { Labels = labels, Glyphs = glyphs, Tiles = tiles, Selected = initialSelected };
                nav.SetBounds(left, (int)(y - Ink.Sp8 * Ui), rowW, h);
                nav.Changed += idx =>
                {
                    if (building || syncingPartNav || idx < 0 || idx >= parts.Length) return;
                    int dir = idx >= lastIdx ? 1 : -1;
                    lastIdx = idx;
                    if (IsTabbedPage(section))
                    {
                        activeSubTab[section] = parts[idx];
                        SwitchTabbedContent(parts[idx], dir);
                        return;
                    }
                    int targetY;
                    if (!partTop.TryGetValue(parts[idx], out targetY)) return;
                    partJumpActive = true;
                    foreach (Control c in content.Controls) if (!c.IsHandleCreated) { var hnd = c.Handle; }
                    content.SmoothScrollTo(targetY);
                };
                partNav = nav;
                content.Controls.Add(nav);
                y = nav.Bottom + Ink.Sp16 * Ui;
            }

            void SyncPartNavFromScroll()
            {
                if (partNav == null || partNav.IsDisposed || activeParts.Length < 2 || building || partJumpActive || content.IsProgrammaticScrolling || IsTabbedPage(section)) return;
                int scrollY = -content.AutoScrollPosition.Y;
                int maxScroll = Math.Max(0, content.VerticalScroll.Maximum - content.VerticalScroll.LargeChange + 1);
                int best = 0;
                int threshold = (int)(90 * Ui);
                for (int i = 1; i < activeParts.Length; i++)
                {
                    int py;
                    if (partTop.TryGetValue(activeParts[i], out py))
                    {
                        int trigger = maxScroll > 0 ? Math.Min(py, maxScroll) : py;
                        if (scrollY + threshold >= trigger) best = i;
                    }
                }
                if (partNav.Selected != best)
                {
                    syncingPartNav = true;
                    partNav.Selected = best;
                    syncingPartNav = false;
                }
            }

            // Distinct visual part chapter banner (colored icon badge + title + subtitle)
            void PartTitle(string partId, string text, bool withDivider)
            {
                if (withDivider) y += Ink.Sp12 * Ui;
                string[] meta;
                PartMeta.TryGetValue(partId, out meta);
                char glyph = meta != null && meta[0].Length > 0 ? meta[0][0] : '\uE713';
                Color tile = meta != null && meta[1].Length > 0 ? Ink.Label(meta[1], true) : Ink.Label("slate", true);
                string sub = meta != null && meta.Length > 2 ? Lang.T(meta[2]) : "";
                var banner = new PartBanner(glyph, tile, text, sub, withDivider, Ui);
                banner.SetBounds(left, (int)y, rowW, banner.PreferredH);
                content.Controls.Add(banner);
                y += banner.Height + Ink.Sp12 * Ui;
            }

            // Sleek chapter banner for each part of a merged page; keeps real child Labels for a11y/tests
            sealed class PartBanner : Panel
            {
                readonly char glyph;
                readonly Color tile;
                readonly bool divider;
                readonly float ui;
                readonly Label titleLabel;
                readonly Label subLabel;
                public readonly int PreferredH;

                public PartBanner(char glyph, Color tile, string title, string sub, bool divider, float ui)
                {
                    this.glyph = glyph;
                    this.tile = tile;
                    this.divider = divider;
                    this.ui = ui;
                    SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
                    BackColor = KitInk.Window;
                    TabStop = false;

                    int topPad = divider ? (int)(16 * ui) : 0;
                    int padY = (int)(8 * ui);
                    Color stripBg = Ink.Mix(KitInk.Window, tile, Ink.Light ? 0.065f : 0.11f);
                    int badgeSize = (int)(32 * ui);
                    int tx = (int)(12 * ui) + badgeSize + (int)(11 * ui);

                    titleLabel = new Label
                    {
                        Text = title,
                        Font = Ink.Semi(14.5f * ui),
                        ForeColor = KitInk.Text,
                        BackColor = stripBg,
                        AutoSize = true,
                        UseMnemonic = false,
                        Location = new Point(tx, topPad + padY),
                    };
                    int titleH = titleLabel.PreferredHeight;
                    Controls.Add(titleLabel);

                    if (!string.IsNullOrEmpty(sub))
                    {
                        int subGap = (int)(2 * ui);
                        subLabel = new Label
                        {
                            Text = sub,
                            Font = Ink.Text(11.5f * ui),
                            ForeColor = KitInk.Sub,
                            BackColor = stripBg,
                            AutoSize = false,
                            AutoEllipsis = true,
                            UseMnemonic = false,
                            Location = new Point(tx, topPad + padY + titleH + subGap),
                        };
                        int subH = Math.Max((int)(18 * ui), subLabel.PreferredHeight);
                        subLabel.Height = subH;
                        Controls.Add(subLabel);
                        PreferredH = topPad + Math.Max((int)(58 * ui), padY + titleH + subGap + subH + padY);
                    }
                    else
                    {
                        PreferredH = topPad + Math.Max((int)(48 * ui), padY * 2 + titleH);
                        titleLabel.Location = new Point(tx, topPad + Math.Max(0, (PreferredH - topPad - titleH) / 2));
                    }
                }

                public void Reflow()
                {
                    if (subLabel != null) subLabel.Width = Math.Max(40, Width - subLabel.Left - (int)(12 * ui));
                    Invalidate();
                }

                protected override void OnResize(EventArgs e)
                {
                    base.OnResize(e);
                    if (subLabel != null) subLabel.Width = Math.Max(40, Width - subLabel.Left - (int)(12 * ui));
                }

                protected override void OnPaint(PaintEventArgs e)
                {
                    var g = e.Graphics;
                    KitInk.Smooth(g);
                    g.Clear(KitInk.Window);
                    float s = ui;
                    if (divider)
                    {
                        var pen = KitInk.PenOf(Ink.Mix(KitInk.Line, KitInk.Sub, 0.25f), Math.Max(1f, s));
                        g.DrawLine(pen, 0, 1f * s, Width - 1, 1f * s);
                    }
                    float topPad = divider ? 16f * s : 0f;
                    var strip = new RectangleF(0.5f, topPad + 0.5f, Width - 1.5f, Height - topPad - 1.5f);
                    Color stripBg = Ink.Mix(KitInk.Window, tile, Ink.Light ? 0.065f : 0.11f);
                    Color stripEdge = Ink.Mix(KitInk.Line, tile, Ink.Light ? 0.24f : 0.34f);
                    KitInk.FillRound(g, strip, 11f * s, stripBg);
                    KitInk.StrokeRound(g, strip, 11f * s, stripEdge, 1f * s);
                    float badgeSize = 32f * s;
                    var badge = new RectangleF(12f * s, strip.Y + (strip.Height - badgeSize) / 2f, badgeSize, badgeSize);
                    KitInk.FillRound(g, badge, 9f * s, tile);
                    KitInk.Draw(g, Ink.Str(glyph), Ink.Icons(15f * s), Ink.On(tile), badge, ContentAlignment.MiddleCenter);
                }
            }

            void AddSubMenuFooter(string currentPage)
            {
                int idx = Array.IndexOf(HubOrder, currentPage);
                if (idx < 0) return;
                string prevId = HubOrder[(idx - 1 + HubOrder.Length) % HubOrder.Length];
                string nextId = HubOrder[(idx + 1) % HubOrder.Length];
                var prevSec = FindSection(prevId);
                var nextSec = FindSection(nextId);
                if (prevSec == null || nextSec == null) return;
                var footer = new SubMenuFooter(
                    prevId, Lang.T(prevSec[1]), prevSec[2].Length > 0 ? prevSec[2][0] : '\uE713',
                    prevSec[4].Length > 0 ? Ink.Label(prevSec[4], true) : Color.Empty,
                    nextId, Lang.T(nextSec[1]), nextSec[2].Length > 0 ? nextSec[2][0] : '\uE713',
                    nextSec[4].Length > 0 ? Ink.Label(nextSec[4], true) : Color.Empty);
                footer.Picked += id => EnterSection(id, null, true, id == prevId ? -1 : 1);
                footer.GoHub += delegate { ShowHub(true); };
                footer.SetBounds(left, (int)(y + 4 * Ui), rowW, (int)(66 * Ui));
                content.Controls.Add(footer);
                y = footer.Bottom + Ink.Sp12 * Ui;
            }

            // Guidance under a heading: body weight, so it never reads as another heading
            void Note(string text)
            {
                var l = new Label { Text = text, Font = Ink.Text(12.5f * Ui), ForeColor = KitInk.Sub, BackColor = KitInk.Window, AutoSize = true, UseMnemonic = false };
                int off = (int)(4 * Ui) - LabelPad(l);
                l.Tag = off;
                l.Location = new Point(left + off, (int)y);
                content.Controls.Add(l);
                y += 22 * Ui;
            }

            // Rows into one rounded category-cohesive card
            void Group(List<KitRow> rows)
            {
                if (rows.Count == 0) return;
                var g = new KitGroup { Left = left, Top = (int)y, Width = rowW, AccentTile = activeGroupTile };
                int top = 0;
                foreach (var r in rows)
                {
                    r.AccentTile = activeGroupTile;
                    r.Width = rowW;
                    r.Top = top;
                    r.Left = 0;
                    PlaceField(r);
                    g.Controls.Add(r);
                    top += r.Height;
                }
                g.Height = top;
                content.Controls.Add(g);
                y += top + Ink.Sp20 * Ui;
            }

            void PlaceField(KitRow r)
            {
                if (r.Field == null) return;
                r.Field.Left = r.Width - r.Field.Width - (int)(16 * Ui);
                r.Field.Top = (r.Height - r.Field.Height) / 2;
                if (!r.Controls.Contains(r.Field)) r.Controls.Add(r.Field);
            }

            // Row icons by source title
            static readonly string[][] RowGlyphs =
            {
                new[] { "Bahasa", "\uE774" }, new[] { "Mulai bersama Windows", "\uE7E8" }, new[] { "Cek versi baru", "\uE72C" },
                new[] { "Folder data", "\uE8B7" }, new[] { "Folder paket", "\uE8B7" }, new[] { "Tema", "\uE706" }, new[] { "Material", "\uE81E" },
                new[] { "Aksen", "\uE790" }, new[] { "Paket", "\uE7B8" }, new[] { "Suara sapaan paket tema", "\uE767" },
                new[] { "Mode", "\uE8A0" }, new[] { "Notifikasi saat mini", "\uEA8F" }, new[] { "Mata", "\uE890" }, new[] { "Ikuti kursor", "\uE7C9" }, new[] { "Saat diam", "\uE890" }, new[] { "Buka panel", "\uE8A7" }, new[] { "Jeda buka", "\uE916" },
                new[] { "Menempel di tepi atas", "\uE840" }, new[] { "Menempel di tepi layar", "\uE840" }, new[] { "Sisakan ruang di tepi atas", "\uE73F" },
                new[] { "Dock", "\uE90E" }, new[] { "Sisakan ruang untuk dock", "\uE73F" }, new[] { "Perbesar ikon", "\uE71E" }, new[] { "Ganti taskbar Windows", "\uE90E" },
                new[] { "Letak", "\uE759" }, new[] { "Geser", "\uE759" }, new[] { "Jarak dari pojok", "\uE759" }, new[] { "Tinggi", "\uE759" }, new[] { "Urutan kartu", "\uE8CB" }, new[] { "Garis cahaya", "\uE781" }, new[] { "Posisi", "\uE759" }, new[] { "Lebar panel", "\uE740" }, new[] { "Layar", "\uE7F4" },
                new[] { "Gaya tab", "\uECA5" }, new[] { "Dibuka di", "\uE8A7" }, new[] { "Pindah tab", "\uE8A9" },
                new[] { "Lirik di island", "\uE8D6" }, new[] { "Cari lirik online", "\uE721" }, new[] { "Visualizer", "\uE9E9" },
                new[] { "Notifikasi pesan", "\uE8BD" }, new[] { "Tampilkan isi pesan", "\uE8BD" }, new[] { "Caps Lock", "\uE765" }, new[] { "Ingatkan cabut charger", "\uE83E" }, new[] { "Cuaca dan hujan", "\uE753" }, new[] { "Mode rapat", "\uE720" },
                new[] { "Num Lock, Scroll Lock", "\uE765" }, new[] { "Bahasa input", "\uE765" }, new[] { "Combo Ketikan", "\uE7FC" }, new[] { "Efek suara combo", "\uE767" }, new[] { "Kelas yang berlangsung", "\uE7BE" },
                new[] { "Progres unduhan", "\uE896" }, new[] { "Riwayat clipboard", "\uE77F" }, new[] { "Flashdisk", "\uE88E" },
                new[] { "Preview screenshot", "\uE91B" }, new[] { "Rak berkas", "\uE7B8" }, new[] { "Istirahat fokus", "\uE916" }, new[] { "Tahan notifikasi saat fokus", "\uE7E7" },
                new[] { "Claude Code", "\uE756" }, new[] { "Kuota agent", "\uEC4A" }, new[] { "Menahan izin", "\uE72E" },
                new[] { "Kulia", "\uE7BE" }, new[] { "Tugas dan jadwal", "\uE787" }, new[] { "Kolom tanggal", "\uE787" }, new[] { "Kolom selesai", "\uE73E" },
                new[] { "Belum ada database", "\uE8F1" }, new[] { "Pengingat sholat", "\uE708" }, new[] { "Sholat", "\uE708" }, new[] { "Suara adzan", "\uE767" }, new[] { "Suara sebelum waktu", "\uE767" }, new[] { "Suara minum", "\uE767" }, new[] { "Suara istirahat mata", "\uE767" }, new[] { "Suara peregangan", "\uE767" }, new[] { "Tambah ibadah", "\uE710" }, new[] { "Nama", "\uE8AC" }, new[] { "Jam", "\uE823" }, new[] { "Hari", "\uE787" }, new[] { "Kota", "\uE707" }, new[] { "Ingatkan sebelum", "\uEA8F" },
                new[] { "Minum air", "\uEB42" }, new[] { "Selang minum", "\uE916" }, new[] { "Target harian", "\uE73E" },
                new[] { "Istirahat mata 20-20-20", "\uE890" }, new[] { "Peregangan", "\uE805" }, new[] { "Selang peregangan", "\uE916" },
                new[] { "Tampil di rekaman layar", "\uE7F4" }, new[] { "Umumkan ke pembaca layar", "\uE7F5" }, new[] { "Kurangi gerak", "\uE7AD" }, new[] { "Gerak", "\uE7AD" },
                new[] { "Ringkasan", "\uE80F" }, new[] { "Musik", "\uE8D6" }, new[] { "Agen", "\uE756" }, new[] { "Kuliah", "\uE7BE" },
                new[] { "Pesan", "\uE8BD" }, new[] { "Rak", "\uE7B8" }, new[] { "Kontrol", "\uE9E9" }, new[] { "Pengingat", "\uEA8F" },
                new[] { "HP", "\uE8EA" }, new[] { "Ubah", "\uE8AB" }, new[] { "Ubah format", "\uE8AB" },
                new[] { "Hubungkan HP", "\uE8EA" }, new[] { "Notifikasi HP", "\uE8BD" }, new[] { "Kode OTP", "\uE8D7" },
                new[] { "Notion", "\uE8F1" }, new[] { "Todoist", "\uE73E" }, new[] { "Kalender iCal", "\uE787" },
                new[] { "Sembunyikan island", "\uED1A" }, new[] { "Navigasi keyboard", "\uE765" }, new[] { "Mini", "\uE898" }, new[] { "Mikrofon", "\uE720" },
                new[] { "Catatan kilat", "\uE70B" }, new[] { "Cermin kamera", "\uE714" }, new[] { "Jeda dan lanjut", "\uE769" }, new[] { "Ulang dari awal", "\uE72C" },
                new[] { "Mundur 5 detik", "\uE76B" }, new[] { "Maju 5 detik", "\uE76C" }, new[] { "Lebih cepat", "\uE710" }, new[] { "Lebih lambat", "\uE738" },
                new[] { "Buka jendela teleprompter", "\uE8A7" }, new[] { "Hentikan", "\uE71A" }, new[] { "Pintasan bawaan", "\uE765" }, new[] { "Tambah aplikasi", "\uE710" }, new[] { "Format jam", "\uE823" }, new[] { "Pakai warna sendiri", "\uE790" }, new[] { "Simpan jadi paket", "\uE74E" }, new[] { "Pindai dari HP", "\uED14" }, new[] { "Aplikasi chat tambahan", "\uE8BD" }, new[] { "Pengirim prioritas", "\uE77B" }, new[] { "Senyap ikut Windows", "\uE708" }, new[] { "Suara", "\uE767" }, new[] { "Simpan isi rak", "\uE74E" }, new[] { "Kosongkan rak otomatis", "\uE916" }, new[] { "Ambil warna", "\uE790" }, new[] { "Ekspor pengaturan", "\uEDE1" }, new[] { "Dikte", "\uE720" }, new[] { "Tombol dikte", "\uE765" }, new[] { "Bahasa ucapan", "\uE774" }, new[] { "Mesin dikte", "\uE720" }, new[] { "Key Groq", "\uE8D7" }, new[] { "Buat key Groq", "\uE774" }, new[] { "Model offline", "\uE896" }, new[] { "Siap dipakai", "\uE73E" }, new[] { "Mengunduh", "\uE896" }, new[] { "Unduh model", "\uE896" }, new[] { "Kamus dan snippet", "\uE70B" }, new[] { "Buang kata pengisi", "\uE74D" }, new[] { "Perintah baris baru", "\uE751" }, new[] { "Gaya per aplikasi", "\uE71D" }, new[] { "Kecilkan musik", "\uE8D6" }, new[] { "Tempel dikte terakhir", "\uE77F" }, new[] { "Rekam rapat", "\uE7C8" }, new[] { "Ringkasan dan to-do", "\uE8A5" }, new[] { "Folder transkrip", "\uE8B7" }, new[] { "Apa yang baru", "\uE789" }, new[] { "Tur Deskmate", "\uE7C1" }, new[] { "GitHub", "\uE943" }, new[] { "Kirim saran", "\uED15" }, new[] { "Bicara", "\uE720" }, new[] { "Suruh Deskmate", "\uE765" }, new[] { "Impor pengaturan", "\uE8B5" }, new[] { "Setel ulang pengaturan", "\uE72C" },
                new[] { "Banner pesan", "\uE8BD" }, new[] { "Banner Windows masih nyala", "\uE7BA" }, new[] { "Deskmate penerima utama", "\uEA8F" }, new[] { "Mode berbagi layar", "\uE7F4" }, new[] { "Isi kiri", "\uE8A0" }, new[] { "Isi kanan", "\uE8A0" },
                new[] { "Atur Notch", "\uE737" }, new[] { "Atur Dock", "\uE90E" }, new[] { "Beranda", "\uE80F" }, new[] { "Lokasi & Cuaca", "\uE707" }, new[] { "Tema & Gaya", "\uE790" },
                new[] { "Warna aksen", "\uE790" }, new[] { "Sinkronkan wallpaper", "\uE771" }, new[] { "Warna kustom", "\uE790" }, new[] { "Senyap", "\uE708" }, new[] { "Integrasi", "\uE71B" }, new[] { "Gamifikasi Ketikan", "\uE7FC" }, new[] { "Pembaruan", "\uE72C" }, new[] { "Layanan luar", "\uE774" },
                new[] { "Rapikan Downloads", "\uE8B7" }, new[] { "Susunan folder", "\uE8B7" }, new[] { "Tawarkan saat", "\uEA8F" }, new[] { "Rapikan sekarang", "\uE8B7" }, new[] { "Urungkan rapikan terakhir", "\uE7A7" },
                new[] { "Layanan teks", "\uE8F2" }, new[] { "Mesin suara", "\uE720" }, new[] { "Yang dikirim", "\uE72E" }, new[] { "Perbaiki teks terpilih", "\uE8AC" }, new[] { "Terjemahkan teks terpilih", "\uE8C1" },
                new[] { "Kustomisasi gamifikasi", "\uE7FC" }, new[] { "Overdrive / Fever Mode", "\uE945" }, new[] { "Finisher tombol Enter", "\uE751" },
                new[] { "Profil suara", "\uE8D6" }, new[] { "Tes suara", "\uE767" }, new[] { "Mode penalti", "\uE72E" },
                new[] { "Level, XP & Achievement", "\uE734" }, new[] { "Target ketikan harian", "\uE73E" }, new[] { "Target 100x combo harian", "\uE945" },
                new[] { "Progres & Rekor", "\uE9D2" }, new[] { "Lencana", "\uE734" }, new[] { "Typing Sprint 30 Detik", "\uE916" }, new[] { "Reset statistik ketikan", "\uE7A7" },
                new[] { "3 Prioritas Hari Ini", "\uE73A" }, new[] { "Statistik Fokus", "\uE916" },
            };

            static char GlyphFor(string title)
            {
                foreach (var g in RowGlyphs)
                    if (Lang.T(g[0]) == title || g[0] == title) return g[1][0];
                if (title.StartsWith("Deskmate")) return KitRow.MarkGlyph;
                return '\uE713';
            }

            KitRow Row(string title, string detail, Control field)
            {
                var r = new KitRow { Title = title, Detail = detail ?? "", Field = field, Height = (int)((string.IsNullOrEmpty(detail) ? 44 : 54) * Ui), Glyph = GlyphFor(title) };
                if (field != null)
                {
                    field.AccessibleName = title;
                    if (!string.IsNullOrEmpty(detail)) field.AccessibleDescription = detail;
                }
                return r;
            }

            // The method follows the region, so the row reports it instead of offering a choice
            string PrayerMethodDetail(bool havePlace)
            {
                if (!havePlace) return Lang.T("Pilih daerah dulu");
                var c = island.PrayCity();
                if (c == null) return Lang.T("Pilih daerah dulu");
                var prm = PrayMethods.Pick(Cities.CountryCode(c), Convert.ToDouble(c[1]));
                string convention = prm.IshaMinutes > 0
                    ? Lang.F("Isya {0} menit", prm.IshaMinutes)
                    : Lang.F("Subuh {0}°, Isya {1}°", prm.Fajr.ToString("0.#", CultureInfo.InvariantCulture), prm.Isha.ToString("0.#", CultureInfo.InvariantCulture));
                string zone = PrayZone.Label(Cities.OffsetOn(c, DateTime.Today));
                string detail = prm.Name + " · " + convention + " · " + zone;
                if (prm.AsrFactor == 2) detail += " · " + Lang.T("Ashar Hanafi");
                if (prm.HighLat > 0) detail += " · " + Lang.T("lintang tinggi");
                return detail;
            }

            // "Jawa Barat · -7.213, 107.902", or "Saudi Arabia · 21.42, 39.83 · Asia/Riyadh"
            string PlaceDetail(object[] hit)
            {
                var inv = CultureInfo.InvariantCulture;
                var parts = new List<string>();
                string region = Cities.RegionOf(hit), tzid = hit.Length > 6 ? (string)hit[6] : "";
                if (!string.IsNullOrEmpty(region)) parts.Add(region);
                parts.Add(Convert.ToDouble(hit[1]).ToString("0.###", inv) + ", " + Convert.ToDouble(hit[2]).ToString("0.###", inv));
                if (!string.IsNullOrEmpty(tzid)) parts.Add(tzid);
                return string.Join(" · ", parts);
            }

            // Plain input: no dropdown of any kind, the box is the whole control
            KitRow CityRow()
            {
                var f = new KitField(Ui, false, Lang.T("Ketik kota atau wilayah"), 60);
                f.Box.Text = Cities.NameOf(settings.Get("pray.city", ""));
                var row = Row(Lang.T("Daerah"), Lang.T("Ketik nama kota di mana saja, lalu Enter"), f);
                // Autocomplete would paint a Windows dropdown over the ring
                f.Box.AutoCompleteMode = AutoCompleteMode.None;
                f.Box.AutoCompleteSource = AutoCompleteSource.None;

                // Typing opens a pickable list right under the box
                Action save = null;
                CitySuggest suggest = null;
                f.Box.TextChanged += delegate
                {
                    if (building) return;
                    var hits = Cities.Suggest(f.Box.Text, 8);
                    if (hits.Count == 0) { if (suggest != null) suggest.Hide2(); return; }
                    // Host it in the scrolling page so it moves with the box, not on the window
                    if (suggest == null || suggest.IsDisposed)
                    {
                        suggest = new CitySuggest(Ui);
                        Control host = f.Box.Parent;
                        while (host != null && !(host is Panel && ((Panel)host).AutoScroll)) host = host.Parent;
                        if (host == null) host = TopLevelControl;
                        if (host == null) return;
                        host.Controls.Add(suggest);
                        var scrollHost = host as ScrollableControl;
                        if (scrollHost != null) scrollHost.Scroll += delegate { if (suggest != null && suggest.Visible) suggest.Hide2(); };
                        suggest.BringToFront();
                    }
                    suggest.Bind(hits.ToArray());
                    // Anchor to the field itself, not the box inside it: the field is the
                    // well the eye lines up with, and the box is inset from it on both sides
                    suggest.ShowUnder(f, delegate(string picked)
                    {
                        // Picking fills the box and commits straight away
                        f.Box.Text = picked;
                        f.Box.SelectionStart = f.Box.Text.Length;
                        save();
                        if (suggest != null) suggest.Hide2();
                    });
                };

                // Up/Down moves selection, Enter commits highlighted (or first) suggestion, Esc dismisses
                f.Box.KeyDown += delegate(object s, KeyEventArgs e)
                {
                    if (suggest == null || !suggest.Visible) return;
                    if (e.KeyCode == Keys.Down) { e.SuppressKeyPress = true; suggest.Step(1); return; }
                    if (e.KeyCode == Keys.Up) { e.SuppressKeyPress = true; suggest.Step(-1); return; }
                    if (e.KeyCode == Keys.Escape) { e.SuppressKeyPress = true; suggest.Hide2(); return; }
                    if (e.KeyCode != Keys.Enter) return;
                    string pick = suggest.Selected();
                    if (string.IsNullOrEmpty(pick)) return;
                    e.SuppressKeyPress = true;
                    suggest.Hide2();
                    f.Box.Text = pick;
                    f.Box.SelectionStart = f.Box.Text.Length;
                    save();
                };
                // Dismissal is handled by the list itself (outside press or Escape), never by
                // the box losing focus: the box blurs on the very press that should hit a row

                save = delegate
                {
                    string typed = (f.Box.Text ?? "").Trim();
                    if (typed.Length == 0) return;
                    // A listed name is stored as before; anything else is looked up
                    var known = Cities.Find(typed);
                    if (known != null)
                    {
                        // Saved packed, so the province survives: it is what the notch shows
                        settings.Set("pray.city", Cities.Pack((string)known[0], Convert.ToDouble(known[1]), Convert.ToDouble(known[2]),
                            Convert.ToDouble(known[3]), Cities.CountryCode(known), Cities.CountryOf(known), Cities.TzidOf(known), Cities.ProvinceOf(known)));
                        settings.Save();
                        f.Box.Text = (string)known[0];
                        row.Detail = PlaceDetail(known);
                        row.Invalidate();
                        island.prayToday = null;
                        island.Kick();
                        RefreshHeroStatus();
                        return;
                    }
                    row.Detail = Lang.T("Mencari lokasi…");
                    row.Invalidate();
                    System.Threading.ThreadPool.QueueUserWorkItem(delegate
                    {
                        string error;
                        var hits = Cities.Search(typed, out error);
                        if (IsDisposed || !IsHandleCreated) return;
                        try
                        {
                            BeginInvoke(new Action(delegate
                            {
                                if (IsDisposed || row.IsDisposed || f.IsDisposed) return;
                                if (hits.Count == 0)
                                {
                                    row.Detail = error != null ? Lang.T("Pencarian gagal, coba lagi") : Lang.T("Lokasi tidak ditemukan");
                                    row.Invalidate();
                                    return;
                                }
                                // First match wins: it is the closest name match from the service
                                var hit = hits[0];
                                string packed = Cities.Pack((string)hit[0], Convert.ToDouble(hit[1]), Convert.ToDouble(hit[2]), Convert.ToDouble(hit[3]),
                                                            (string)hit[4], (string)hit[5], (string)hit[6]);
                                Cities.Remember(packed);
                                settings.Set("pray.city", packed);
                                settings.Save();
                                f.Box.Text = (string)hit[0];
                                row.Detail = PlaceDetail(hit);
                                row.Invalidate();
                                island.prayToday = null;
                                island.Kick();
                                // Weather and the ring follow the new coordinates
                                RefreshContent();
                            }));
                        }
                        catch (Exception) { }
                    });
                };

                f.Submit = save;
                f.Box.Leave += delegate { if (!building) save(); };
                return row;
            }

            // Controls with settings plumbing

            KitSwitch Switch(string key, bool fallback, Action<bool> after)
            {
                var s = new KitSwitch { Checked = settings.GetBool(key, fallback), Size = new Size((int)(38 * Ui), (int)(22 * Ui)) };
                s.CheckedChanged += delegate { if (building) return; if (after != null) after(s.Checked); else island.SetFlag(key, s.Checked); RefreshHeroStatus(); };
                return s;
            }

            KitSegmented Seg(string[] items, int selected, Action<int> changed)
            {
                float w = 0;
                foreach (var it in items) w = Math.Max(w, TextRenderer.MeasureText(it, Ink.Semi(12.5f * Ui)).Width);
                var c = new KitSegmented { Items = items, Size = new Size((int)((w + 30 * Ui) * items.Length), (int)(ControlH * Ui)) };
                c.Selected = selected;
                c.Changed += i => { if (!building) { changed(i); RefreshHeroStatus(); } };
                return c;
            }

            KitSlider Slider(int min, int max, int step, int value, Func<int, string> fmt, Action<int> changed)
            {
                var c = new KitSlider { Minimum = min, Maximum = max, Step = step, Value = Math.Max(min, Math.Min(max, value)), Format = fmt, ShowValue = true, Size = new Size((int)(270 * Ui), (int)(ControlH * Ui)) };
                c.Changed += v => { if (!building) changed(v); };
                c.Committed += delegate { if (!building) settings.FlushSave(); };
                return c;
            }

            KitPopup Popup(string[] items, int selected, Action<int> changed)
            {
                var c = new KitPopup { Items = items, Font = Ink.Text(12.5f * Ui) };
                c.Selected = selected;
                float w = 0;
                foreach (var it in items) w = Math.Max(w, TextRenderer.MeasureText(it + "  ▾", c.Font).Width);
                c.Size = new Size((int)Math.Min(260 * Ui, w + 30 * Ui), (int)(ControlH * Ui));
                c.Changed += i => { if (!building) { changed(i); RefreshHeroStatus(); } };
                return c;
            }

            // Comma list saved on Enter or leaving the box
            // Row says Tersimpan for a moment after a save
            KitRow ListRow(string title, string detail, string key, string cue)
            {
                var f = new KitField(Ui, false, cue, 300);
                f.Box.Text = settings.Get(key, "");
                var row = Row(title, detail, f);
                var back = new System.Windows.Forms.Timer { Interval = 2500 };
                back.Tick += delegate { back.Stop(); if (row.IsDisposed) return; row.Detail = detail; row.Invalidate(); };
                f.Disposed += delegate { back.Dispose(); };
                Action save = delegate
                {
                    string clean = string.Join(", ", CommaList(f.Box.Text));
                    if (clean == settings.Get(key, "")) return;
                    settings.Set(key, clean);
                    settings.Save();
                    island.ApplyNotifLists();
                    row.Detail = Lang.T("Tersimpan");
                    row.Invalidate();
                    back.Stop();
                    back.Start();
                };
                f.Submit = save;
                f.Box.LostFocus += delegate { save(); };
                return row;
            }

            // Swatch row; a pick repaints island and window
            // Starting points that read well together; the brand night, ivory, and plum ink lead
            static string[][] SwatchPresets(string key)
            {
                if (key == "bg") return new[] { new[] { "Malam", "#1C1A22" }, new[] { "Gading", "#F5F2EC" }, new[] { "Hitam", "#000000" }, new[] { "Arang", "#1C1C1E" }, new[] { "Navy", "#0E1734" }, new[] { "Hijau tua", "#0F2A22" }, new[] { "Putih", "#FFFFFF" }, new[] { "Krem", "#F5EFE6" } };
                if (key == "text") return new[] { new[] { "Gading", "#F5F2EC" }, new[] { "Plum", "#211C2A" }, new[] { "Putih", "#FFFFFF" }, new[] { "Putih lembut", "#EEF4FF" }, new[] { "Hitam", "#111111" }, new[] { "Cokelat tua", "#3B2F2F" } };
                var list = new System.Collections.Generic.List<string[]>();
                var table = Ink.Light ? Ink.LightAccents : Ink.DarkAccents;
                foreach (var a in table) if (a[1].StartsWith("#")) list.Add(a);
                return list.ToArray();
            }

            KitRow ColorRow(string key, string title, string detail)
            {
                var sw = new KitSwatch { Font = Ink.Text(12.5f * Ui), Value = island.CustomColor(key), Presets = SwatchPresets(key) };
                sw.Size = new Size(TextRenderer.MeasureText("      #DDDDDD", sw.Font).Width + (int)(32 * Ui), (int)(ControlH * Ui));
                sw.Picked += c => { island.SetCustomColor(key, c); BeginInvoke(new Action(Relayout)); };
                var row = Row(title, detail, sw);
                row.Glyph = '\uE790';
                return row;
            }

            string ContrastNote()
            {
                double c = Contrast(island.CustomColor("text"), island.CustomColor("bg"));
                string v = c.ToString("0.0", Lang.English ? System.Globalization.CultureInfo.InvariantCulture : new System.Globalization.CultureInfo("id-ID"));
                return c >= 4.5 ? Lang.F("Kontras dengan latar {0}:1", v) : Lang.F("Kontras {0}:1, teks sulit dibaca", v);
            }

            // Shortcut row: detail shows default or the clash
            KitRow HotkeyRow(HotkeyDef d)
            {
                var field = new KitHotkey { Font = Ink.Text(12.5f * Ui) };
                var row = Row(Lang.T(d.Name), HotkeyDetail(d), field);
                field.Size = new Size(TextRenderer.MeasureText("Ctrl+Alt+Shift+PageDown", field.Font).Width + (int)(32 * Ui), (int)(ControlH * Ui));
                field.Combo = island.HotkeyText(d.Id);
                field.Begin = delegate { island.HoldHotkeys(true); };
                field.End = delegate { island.HoldHotkeys(false); };
                field.Commit = combo =>
                {
                    string error = island.SetHotkey(d.Id, combo);
                    field.Combo = island.HotkeyText(d.Id);
                    row.Detail = error ?? HotkeyDetail(d);
                    field.AccessibleDescription = row.Detail;
                    row.Invalidate();
                    return error == null;
                };
                return row;
            }

            string HotkeyDetail(HotkeyDef d)
            {
                if (island.HotkeyTaken(d.Id)) return Lang.T("Dipakai aplikasi lain");
                return d.Fallback.Length == 0 ? Lang.T("Tanpa pintasan bawaan") : Lang.F("Bawaan: {0}", d.Fallback);
            }

            // Row controls: the compact 30 px Settings target from DESIGN.md
            const float ControlH = 30;

            KitButton Button(string text, bool primary, Action click)
            {
                var b = new KitButton { Text = text, Primary = primary, Font = Ink.Text(12.5f * Ui) };
                b.Size = new Size(TextRenderer.MeasureText(text, b.Font).Width + (int)(32 * Ui), (int)(ControlH * Ui));
                b.Click += delegate { click(); };
                return b;
            }

            void Preview()
            {
                island.PreviewLayout(true);
                preview.Stop();
                preview.Start();
            }

        }
    }
}
