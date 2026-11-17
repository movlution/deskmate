using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace Deskmate
{
    // Settings window; every change applies live
    partial class Island
    {
        PrefsWindow prefs;

        void OpenPrefs()
        {
            if (prefs != null && !prefs.IsDisposed) { prefs.Activate(); return; }
            prefs = new PrefsWindow(this);
            prefs.Show();
            prefs.Activate();
        }

        void OpenPrefs(string section)
        {
            OpenPrefs();
            prefs.Go(section);
        }

        void SetClock24(bool h24)
        {
            settings.Set("clock.24", h24);
            settings.Save();
            Fmt.H24 = h24;
            clockRoller.Set(Fmt.Clock(DateTime.Now));
            // Cached prayer strings follow at once
            if (prayToday != null)
            {
                for (int i = 0; i < 6; i++) prayHm[i] = Fmt.Hm(prayToday[i]);
                prayTomorrowHm = Fmt.Hm(prayTomorrow);
            }
            Kick();
        }

        // theme: gelap, terang, otomatis
        void SetThemeMode(string mode)
        {
            settings.Set("theme", mode);
            settings.Save();
            ApplyThemeMode();
        }

        bool ThemeLight()
        {
            string mode = settings.Get("theme", "gelap");
            return mode == "terang" || (mode == "otomatis" && !winDark);
        }

        void ApplyThemeMode()
        {
            bool light = ThemeLight();
            if (light == Ink.Light) return;
            Ink.Apply(light);
            string curHex = settings.Get("accent", "");
            if (!string.IsNullOrEmpty(curHex))
            {
                for (int i = 0; i < Accents.Length; i++)
                {
                    if (Accents[i][1] == curHex || AccentsLight[i][1] == curHex)
                    {
                        string newHex = light ? AccentsLight[i][1] : Accents[i][1];
                        if (newHex != curHex)
                        {
                            settings.Set("accent", newHex);
                            settings.Save();
                            Ink.SetAccent(newHex);
                        }
                        break;
                    }
                }
            }
            WallApply();
            ResetFrost();
            RefreshDocks();
            KitWindow.BroadcastThemeChange();
            Kick();
        }

        void SetAccentHex(string hex)
        {
            settings.Set("accent", hex);
            settings.Save();
            Ink.SetAccent(hex);
            albumAccentFor = null;
            albumTinted = false;
            AlbumAccent();
            WallApply();
            RefreshDocks();
            KitWindow.BroadcastThemeChange();
            Kick();
        }

        void SetFlag(string key, bool on)
        {
            settings.Set(key, on);
            settings.Save();
            if (key == "dock") ResetFrost();
            else if (key == "notif") notifWatcher.Enabled = NotifOn;
            else if (key == "lyrics.online") { lyrics = null; SyncLyrics(); }
            else if (key == "shots") ApplyClips();
            else if (key == "clipboard")
            {
                ApplyClips();
                if (on && Array.IndexOf(tabs, PageShelf) < 0) SetLayout("panel.tabs", TabIds(tabs) + ",shelf");
            }
            else if (key == "downloads") ApplyDownloads();
            else if (key == "tidy") ApplyTidy();
            else if (key == "capture.show") ApplyGlass();
            else if (key == "shelf" || key == "convert") InitShelfDrop();
            else if (key == "wall.sync") WallToggle(on);
            else if (key.StartsWith("dockbar.", StringComparison.Ordinal)) ApplyDock();
            Kick();
        }

        void SetChoice(string key, string value)
        {
            settings.Set(key, value);
            settings.Save();
            if (key == "material") { ApplyGlass(); ResetFrost(); RefreshDocks(); }
            else if (key == "motion.reduce" || key == "motion.feel") Anim.Read(settings);
            else if (key == "share.mode") ApplyShare();
            Kick();
        }

        // A theme pack can switch the eyes off entirely
        internal bool EyesAllowed { get { return packEyes; } }

        void SetLanguage(bool english)
        {
            settings.Set("lang", english ? "en" : "id");
            settings.Save();
            Lang.English = english;
            BuildTray();
            if (chatWin != null && !chatWin.IsDisposed) chatWin.Relayout();
            Kick();
        }

        void SetLayout(string key, object value)
        {
            settings.Set(key, value);
            settings.Save();
            LoadLayout();
            ResetFrost();
            Kick();
        }

        // Open the panel while layout is being tuned
        void PreviewLayout(bool on)
        {
            if (on && !expanded) Expand(DefaultPage(false));
            else if (!on && expanded && !pointerInside) { expanded = false; Kick(); }
        }

        void PreviewPage(int pg)
        {
            if (!expanded) Expand(pg);
            else SetPage(pg, false);
        }

        // Corner inset or side height from settings
        void SetPlaceValue(string key, int v)
        {
            settings.Set(key, v);
            settings.Save();
            LoadPlace();
            if (GlassOn) NeedFrost(sh.Target);
            Kick();
        }

        // Random port; Claude's hook URL follows
        void MoveToRandomPort()
        {
            bool claude = HooksInstalled();
            int port = AgentHub.FreePort();
            if (hub != null) hub.Stop();
            settings.Set("agent.port", port);
            settings.Save();
            StartHub();
            if (claude)
            {
                string err = HookSetup.Install(port, settings.Get("agent.token", ""));
                if (err != null) ShowInfo(Ink.IcoTerminal, Lang.T("Belum terhubung"), err, true);
            }
            CheckLinks();
            ShowInfo(Ink.IcoTerminal, Lang.F("Port {0}", port), Lang.T("sesi agen baru memakainya"), true);
        }

        static readonly string[][] Accents =
        {
            new[] { "Biru", "#4C8DFF" },
            new[] { "Ungu", "#A78BFA" },
            new[] { "Merah muda", "#FF6B9A" },
            new[] { "Merah", "#FF5252" },
            new[] { "Oranye", "#FF9F43" },
            new[] { "Kuning", "#FFD24A" },
            new[] { "Hijau", "#3DD68C" },
            new[] { "Grafit", "" },
        };

        static readonly string[][] AccentsLight =
        {
            new[] { "Biru", "#1668E8" },
            new[] { "Ungu", "#6C3CE0" },
            new[] { "Merah muda", "#C2185B" },
            new[] { "Merah", "#C5221F" },
            new[] { "Oranye", "#A84B00" },
            new[] { "Kuning", "#855E00" },
            new[] { "Hijau", "#107C41" },
            new[] { "Grafit", "" },
        };

        partial class PrefsWindow : KitWindow
        {
            readonly Island island;
            readonly Settings settings;
            readonly Timer preview = new Timer { Interval = 2500 };
            // Opens on Beranda
            string section = "overview", beforeSearch = "overview";
            readonly TextBox search = new KitText();
            readonly Panel content = new Panel();
            readonly SideList side = new SideList();
            bool building;
            bool overClear;

            static readonly string[] GroupHeaders =
            {
                "",
                "Tampilan & Ruang Kerja",
                "Produktivitas",
                "Perangkat & Media",
                "Aplikasi",
            };

            // { id, title, glyph, group }
            static readonly string[][] Sections =
            {
                new[] { "overview", "Beranda", "\uE80F", "0" },
                new[] { "look", "Tema & Gaya", "\uE790", "1" },
                new[] { "island", "Notch & Perilaku", "\uE737", "1" },
                new[] { "tabs", "Tab & Kartu Notch", "\uE8A9", "1" },
                new[] { "dock", "Dock & Taskbar", "\uE90E", "1" },
                new[] { "integrasi", "Integrasi & Kalender", "\uE71B", "2" },
                new[] { "work", "Tugas & Fokus", "\uE73A", "2" },
                new[] { "ai", "Layanan AI", "\uE8F2", "2" },
                new[] { "agents", "Agen Coding", "\uE756", "2" },
                new[] { "dikte", "Dikte & Rapat", "\uE720", "2" },
                new[] { "messages", "Notifikasi & Pesan", "\uE8BD", "3" },
                new[] { "files", "Musik & Berkas", "\uE7B8", "3" },
                new[] { "combo", "Gamifikasi Ketikan", "\uE7FC", "3" },
                new[] { "system", "Sistem & Perangkat", "\uE9D9", "3" },
                new[] { "remind", "Cuaca & Pengingat", "\uEA8F", "3" },
                new[] { "phone", "Hubungkan HP", "\uE8EA", "3" },
                new[] { "general", "Umum & Data", "\uE713", "4" },
                new[] { "keys", "Pintasan & Aksesibilitas", "\uE765", "4" },
                new[] { "about", "Tentang", "\uE946", "4" },
            };

            static readonly Dictionary<string, string> SectionNotes = new Dictionary<string, string>
            {
                { "overview", "Ringkasan status, tombol cepat, dan pintasan pengaturan" },
                { "look", "Tema, material kaca, warna aksen, paket tema, dan warna kustom" },
                { "island", "Letak, ukuran, perilaku panel, saat diam, dan pengecualian aplikasi" },
                { "tabs", "Tab di panel notch, urutan, dan kartu Ringkasan" },
                { "dock", "Bar aplikasi dan widget, atau pengganti taskbar" },
                { "integrasi", "Tugas dan jadwal dari Kulia, Notion, Todoist, dan iCal" },
                { "work", "Daftar tugas, fokus, prioritas harian, dan catatan lokal" },
                { "ai", "Layanan AI untuk dikte, ringkasan, dan Suruh Deskmate" },
                { "agents", "Claude Code, Codex, dan agen coding lainnya" },
                { "dikte", "Bicara, lalu teksnya ditempel; teleprompter dan rapat" },
                { "messages", "Notifikasi pesan, aturan aplikasi, mode berbagi layar, VIP, dan Senyap" },
                { "files", "Lirik musik, visualizer, rak berkas, clipboard, screenshot, dan Downloads" },
                { "combo", "Combo, suara mekanikal/arcade, Fever Mode, penalti, XP, dan Sprint 30 detik" },
                { "system", "HUD keyboard, baterai, flashdisk, dan aktivitas sistem" },
                { "remind", "Lokasi cuaca, jadwal sholat, minum air, mata, dan peregangan" },
                { "phone", "Notifikasi, kode OTP, dan berkas dari HP" },
                { "general", "Bahasa, jam, mulai bersama Windows, pembaruan, dan data Deskmate" },
                { "keys", "Pintasan keyboard, pembaca layar, dan kenyamanan gerak" },
                { "about", "Versi, pembaruan, tur, dan pembuat Deskmate" },
            };

            static readonly Dictionary<string, string> SectionKeywords = new Dictionary<string, string>
            {
                { "overview", "beranda home status cepat" },
                { "look", "tema gelap terang otomatis aksen warna wallpaper paket" },
                { "island", "notch dynamic island letak posisi ukuran mata jam diam" },
                { "tabs", "tab kartu ringkasan home urutan" },
                { "dock", "dock taskbar widget aplikasi" },
                { "integrasi", "kulia notion todoist ical google kalender jadwal" },
                { "work", "tugas fokus prioritas catatan timer" },
                { "ai", "ai openai gemini groq claude dikte" },
                { "agents", "agen coding claude code codex kuota port" },
                { "dikte", "dikte suara mikrofon teleprompter" },
                { "messages", "pesan notifikasi chat banner vip senyap dnd" },
                { "files", "musik lirik visualizer rak berkas clipboard screenshot downloads" },
                { "combo", "combo ketikan gamifikasi suara mekanikal fever xp sprint" },
                { "system", "sistem keyboard caps num baterai flashdisk usb bluetooth wifi rapat" },
                { "remind", "cuaca hujan kota sholat adzan minum mata peregangan pengingat" },
                { "phone", "hp android qr otp notifikasi" },
                { "general", "bahasa jam startup windows update data ekspor impor reset" },
                { "keys", "pintasan hotkey shortcut aksesibilitas gerak pembaca layar" },
                { "about", "tentang versi github tur kopi pembuat" },
            };

            static string NormalizeSection(string id)
            {
                if (string.IsNullOrEmpty(id)) return "overview";
                switch (id)
                {
                    case "kulia": return "integrasi";
                    case "packs": return "look";
                    case "behave":
                    case "idle": return "island";
                    case "home": return "tabs";
                    case "music": return "files";
                    case "access": return "keys";
                    case "cat:0": return "overview";
                    case "cat:1": return "look";
                    case "cat:2": return "island";
                    case "cat:3": return "dock";
                    case "cat:4": return "work";
                    case "cat:5": return "about";
                }
                foreach (var s in Sections) if (s[0] == id) return id;
                return "overview";
            }
            [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern IntPtr SendMessage(IntPtr h, int msg, IntPtr w, string l);
            [DllImport("uxtheme.dll", CharSet = CharSet.Unicode)] static extern int SetWindowTheme(IntPtr hwnd, string app, string idList);

            public PrefsWindow(Island island)
            {
                this.island = island;
                settings = island.settings;
                Text = Lang.T("Pengaturan Deskmate");
                StartPosition = FormStartPosition.Manual;
                Icon = island.tray != null ? island.tray.Icon : null;
                preview.Tick += delegate { preview.Stop(); island.PreviewLayout(false); };
                FormClosed += delegate { preview.Stop(); island.PreviewLayout(false); };
                content.AutoScroll = true;
                search.BorderStyle = BorderStyle.None;
                search.TextChanged += delegate
                {
                    if (building) return;
                    bool typing = search.Text.Trim().Length > 0;
                    // Clearing the field returns to where it started
                    if (typing && section != "search") beforeSearch = section;
                    section = typing ? "search" : section == "search" ? beforeSearch : section;
                    Invalidate(Rectangle.Ceiling(SearchWell()));
                    Fill();
                };
                search.GotFocus += delegate { Invalidate(new Rectangle(0, 0, (int)(236 * Ui), (int)(60 * Ui))); };
                search.LostFocus += delegate { Invalidate(new Rectangle(0, 0, (int)(236 * Ui), (int)(60 * Ui))); };
                side.Picked += id =>
                {
                    section = NormalizeSection(id);
                    building = true;
                    search.Text = "";
                    building = false;
                    Invalidate(Rectangle.Ceiling(SearchWell()));
                    Fill();
                };
                search.KeyDown += delegate(object s, KeyEventArgs e)
                {
                    if (e.KeyCode == Keys.Escape && search.Text.Length > 0)
                    {
                        search.Text = "";
                        e.SuppressKeyPress = true;
                    }
                };
                Controls.Add(search);
                Controls.Add(side);
                Controls.Add(content);
            }

            protected override void OnShown(EventArgs e)
            {
                base.OnShown(e);
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
                if (section != "messages" && section != "search") return;
                var at = content.AutoScrollPosition;
                Fill();
                content.AutoScrollPosition = new Point(-at.X, -at.Y);
            }

            public void Go(string id)
            {
                section = NormalizeSection(id);
                building = true;
                search.Text = "";
                building = false;
                Invalidate(Rectangle.Ceiling(SearchWell()));
                if (IsHandleCreated) { Fill(); side.RevealSelected(); }
            }

            protected override void OnHandleCreated(EventArgs e)
            {
                base.OnHandleCreated(e);
                var area = Screen.FromHandle(Handle).WorkingArea;
                // Tall enough that the whole sidebar shows
                int chromeW = Width - ClientSize.Width, chromeH = Height - ClientSize.Height;
                float wantH = 720;
                Size = new Size((int)Math.Min(1040 * Ui + chromeW, area.Width - 40), (int)Math.Min(wantH * Ui + chromeH, area.Height - 40));
                MinimumSize = new Size((int)(680 * Ui), (int)(440 * Ui));
                Location = new Point(area.X + (area.Width - Width) / 2, area.Y + (area.Height - Height) / 2);
                Relayout();
            }

            protected override void OnResize(EventArgs e) { base.OnResize(e); if (IsHandleCreated) { Place(); Fill(); } }

            public override void Relayout()
            {
                BackColor = KitInk.Window;
                Native.DarkFrame(Handle, !Ink.Light);
                // Scrollbar follows the theme
                try { SetWindowTheme(content.Handle, Ink.Light ? "Explorer" : "DarkMode_Explorer", null); } catch (Exception) { }
                Text = Lang.T("Pengaturan Deskmate");
                side.Items = new List<string[]>();
                foreach (var s in Sections) side.Items.Add(new[] { s[0], Lang.T(s[1]), s[2], s[3] });
                var hdrs = new string[GroupHeaders.Length];
                for (int i = 0; i < GroupHeaders.Length; i++) hdrs[i] = GroupHeaders[i].Length > 0 ? Lang.T(GroupHeaders[i]) : "";
                side.Headers = hdrs;
                side.Selected = NormalizeSection(section);
                search.Font = UiFont(13f);
                search.BackColor = KitInk.Hover;
                search.ForeColor = KitInk.Text;
                // Hint stays while focused
                SendMessage(search.Handle, 0x1501, (IntPtr)1, Lang.T("Cari pengaturan"));
                Place();
                Fill();
                Invalidate(true);
            }

            void Place()
            {
                float s = Ui;
                int sideW = (int)(236 * s);
                // Text centred on the well, by its real height
                int h = search.PreferredHeight;
                search.SetBounds((int)(40 * s), (int)Math.Round(SearchWell().Y + SearchWell().Height / 2 - h / 2f + 1 * s), sideW - (int)(78 * s), h);
                side.SetBounds(0, (int)(58 * s), sideW, ClientSize.Height - (int)(58 * s));
                content.SetBounds(sideW, 0, Math.Max(10, ClientSize.Width - sideW), ClientSize.Height);
                content.BackColor = KitInk.Window;
                side.Invalidate();
            }

            RectangleF SearchWell() { return new RectangleF(12 * Ui, 16 * Ui, 236 * Ui - 24 * Ui, 32 * Ui); }

            RectangleF SearchClearRect()
            {
                var r = SearchWell();
                return new RectangleF(r.Right - 26 * Ui, r.Y + 2 * Ui, 24 * Ui, r.Height - 4 * Ui);
            }

            protected override void OnMouseMove(MouseEventArgs e)
            {
                base.OnMouseMove(e);
                bool over = search.Text.Length > 0 && SearchClearRect().Contains(e.Location);
                if (over == overClear) return;
                overClear = over;
                Cursor = over ? Cursors.Hand : Cursors.Default;
                Invalidate(Rectangle.Ceiling(SearchWell()));
            }

            protected override void OnMouseLeave(EventArgs e)
            {
                base.OnMouseLeave(e);
                if (overClear) { overClear = false; Cursor = Cursors.Default; Invalidate(Rectangle.Ceiling(SearchWell())); }
            }

            protected override void OnMouseUp(MouseEventArgs e)
            {
                base.OnMouseUp(e);
                if (e.Button == MouseButtons.Left && search.Text.Length > 0 && SearchClearRect().Contains(e.Location))
                {
                    search.Text = "";
                    search.Focus();
                }
            }

            protected override void OnPaintBackground(PaintEventArgs e)
            {
                e.Graphics.Clear(KitInk.Window);
                int sideW = (int)(236 * Ui);
                using (var b = new SolidBrush(KitInk.Side)) e.Graphics.FillRectangle(b, 0, 0, sideW, ClientSize.Height);
                // Search field: a rounded well
                var r = SearchWell();
                KitInk.Smooth(e.Graphics);
                using (var path = Ink.Round(r, 10 * Ui))
                {
                    using (var b = new SolidBrush(search.BackColor)) e.Graphics.FillPath(b, path);
                    if (search.Focused) using (var pen = new Pen(KitInk.Faint, 1.2f)) e.Graphics.DrawPath(pen, path);
                }
                // Magnifier leads the field
                using (var icons = new Font(Ink.IconFamily, 13f * Ui, GraphicsUnit.Pixel))
                    KitInk.Draw(e.Graphics, "\uE721", icons, KitInk.Sub, new RectangleF(r.X + 5 * Ui, r.Y, 24 * Ui, r.Height), ContentAlignment.MiddleCenter);
                if (search.Text.Length > 0)
                {
                    var cr = SearchClearRect();
                    if (overClear) using (var b = new SolidBrush(KitInk.Hover)) using (var p = Ink.Round(cr, 6 * Ui)) e.Graphics.FillPath(b, p);
                    using (var cf = new Font(Ink.MenuFamily, 14f * Ui, GraphicsUnit.Pixel))
                        KitInk.Draw(e.Graphics, "\u00D7", cf, overClear ? KitInk.Text : KitInk.Sub, cr, ContentAlignment.MiddleCenter);
                }
            }

            // Page builder: title, then groups of rows

            float y;
            int rowW;

            void Fill()
            {
                if (!IsHandleCreated) return;
                building = true;
                content.SuspendLayout();
                var old = new List<Control>();
                foreach (Control c in content.Controls) old.Add(c);
                content.Controls.Clear();
                foreach (var c in old) c.Dispose();
                content.AutoScrollPosition = Point.Empty;
                // Outer width: the old page's scrollbar must not narrow rows
                rowW = content.Width - (int)(56 * Ui) - SystemInformation.VerticalScrollBarWidth;
                y = 22 * Ui;
                if (section == "search")
                {
                    string q = search.Text.Trim();
                    Heading(Lang.F("Hasil untuk \"{0}\"", q));
                    int found = 0;
                    foreach (var s in Sections)
                    {
                        var rows = new List<KitRow>();
                        Build(s[0], rows, false);
                        string kw;
                        SectionKeywords.TryGetValue(s[0], out kw);
                        bool secMatch = Lang.T(s[1]).IndexOf(q, StringComparison.CurrentCultureIgnoreCase) >= 0 || (kw != null && kw.IndexOf(q, StringComparison.CurrentCultureIgnoreCase) >= 0);
                        var hits = secMatch ? rows : rows.FindAll(r => r.Title.IndexOf(q, StringComparison.CurrentCultureIgnoreCase) >= 0 || r.Detail.IndexOf(q, StringComparison.CurrentCultureIgnoreCase) >= 0);
                        if (!secMatch) foreach (var r in rows) if (!hits.Contains(r)) r.Dispose();
                        if (hits.Count == 0) continue;
                        found += hits.Count;
                        Caption(Lang.T(s[1]));
                        Group(hits);
                    }
                    if (found == 0) Caption(Lang.T("Tidak ada yang cocok"));
                }
                else
                {
                    section = NormalizeSection(section);
                    side.Selected = section;
                    side.Invalidate();
                    Head(section);
                    Build(section, new List<KitRow>(), true);
                }
                // Room under the last group
                var spacer = new Panel { Location = new Point(0, (int)y), Size = new Size(1, (int)(16 * Ui)), BackColor = KitInk.Window };
                content.Controls.Add(spacer);
                content.ResumeLayout();
                building = false;
            }

            // Display changes only rebuild the visible display page; other pages keep their controls.
            internal void RefreshDisplayPicker()
            {
                if (IsDisposed || !Visible || building || section != "island") return;
                Fill();
            }

            // Blank a Label draws before its first letter
            static int LabelPad(Label l)
            {
                var size = l.PreferredSize;
                if (size.Width <= 0 || size.Height <= 0) return 0;
                using (var bmp = new Bitmap(size.Width, size.Height))
                {
                    l.Size = size;
                    l.DrawToBitmap(bmp, new Rectangle(Point.Empty, size));
                    var back = l.BackColor;
                    for (int x = 0; x < size.Width; x++)
                        for (int y = 0; y < size.Height; y++)
                        {
                            var c = bmp.GetPixel(x, y);
                            if (Math.Abs(c.R - back.R) + Math.Abs(c.G - back.G) + Math.Abs(c.B - back.B) > 90) return x;
                        }
                }
                return 0;
            }

            void Heading(string text)
            {
                var l = new Label { Text = text, Font = UiFont(24f), ForeColor = KitInk.Text, BackColor = KitInk.Window, AutoSize = true };
                // Ink on the group's edge
                l.Location = new Point((int)(28 * Ui) - LabelPad(l), (int)y);
                content.Controls.Add(l);
                y += 52 * Ui;
            }

            // Head: the category, or "Category > Page" with a way back
            void Head(string page)
            {
                string note = null;
                string[] sec = null;
                foreach (var s in Sections) if (s[0] == page) sec = s;
                if (page != null) SectionNotes.TryGetValue(page, out note);
                var head = new PageHeader
                {
                    Glyph = sec != null ? sec[2][0] : '\uE713',
                    Title = sec != null ? Lang.T(sec[1]) : "",
                    Note = note != null ? Lang.T(note) : "",
                };
                head.AccessibleName = head.Title;
                head.AccessibleDescription = head.Note;
                head.SetBounds((int)(28 * Ui), (int)y, rowW, (int)(60 * Ui));
                content.Controls.Add(head);
                tabFocus["head"] = head;
                y += 78 * Ui;
            }

            protected override bool ProcessCmdKey(ref Message msg, Keys keys)
            {
                if (keys == Keys.Escape && search.Focused && search.Text.Length > 0)
                {
                    search.Text = "";
                    return true;
                }
                return base.ProcessCmdKey(ref msg, keys);
            }

            // Page head; a breadcrumb's first part leads back
            class PageHeader : KitControl
            {
                public char Glyph;
                public string Title = "", Note = "", Crumb;
                public event Action Back;
                RectangleF parentAt;
                bool overParent;

                public PageHeader() { TabStop = false; }

                // Focusable only when there is a way back
                protected override void OnCreateControl() { base.OnCreateControl(); TabStop = Crumb != null; }

                protected override void OnMouseMove(MouseEventArgs e)
                {
                    base.OnMouseMove(e);
                    bool over = Crumb != null && parentAt.Contains(e.Location);
                    if (over == overParent) return;
                    overParent = over;
                    Cursor = over ? Cursors.Hand : Cursors.Default;
                    Invalidate();
                }

                protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); overParent = false; Invalidate(); }
                protected override void OnMouseUp(MouseEventArgs e) { base.OnMouseUp(e); if (e.Button == MouseButtons.Left && overParent) Fire(); }
                protected override bool IsInputKey(Keys k) { return k == Keys.Enter || k == Keys.Space || base.IsInputKey(k); }

                protected override void OnKeyDown(KeyEventArgs e)
                {
                    base.OnKeyDown(e);
                    if (Crumb == null || (e.KeyCode != Keys.Enter && e.KeyCode != Keys.Space)) return;
                    e.Handled = true;
                    Fire();
                }

                void Fire() { var h = Back; if (h != null) h(); }

                protected override void OnPaint(PaintEventArgs e)
                {
                    var g = e.Graphics;
                    KitInk.Smooth(g);
                    g.Clear(KitInk.Under(this));
                    float s = Ui;
                    var tile = new RectangleF(0, 6 * s, 48 * s, 48 * s);
                    var badge = Ink.Mix(KitInk.Group, KitInk.Accent, 0.12f);
                    var glyphInk = Contrast(KitInk.Accent, badge) >= 3.0 ? KitInk.Accent : KitInk.Text;
                    Fill(g, tile, 13 * s, badge);
                    using (var icons = new Font(Ink.IconFamily, 22f * s, GraphicsUnit.Pixel)) KitInk.Draw(g, Ink.Str(Glyph), icons, glyphInk, tile, ContentAlignment.MiddleCenter);
                    using (var tf = new Font(Ink.MenuFamily, 24f * s, FontStyle.Bold, GraphicsUnit.Pixel))
                    {
                        float x = 68 * s;
                        var line = new RectangleF(x, 2 * s, Width - x, 32 * s);
                        if (Crumb != null)
                        {
                            // Category in grey, then the page in full ink
                            int pw = TextRenderer.MeasureText(g, Crumb, tf, Size.Empty, TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine).Width;
                            parentAt = new RectangleF(x - 6 * s, line.Y, pw + 10 * s, line.Height);
                            if (overParent) Fill(g, parentAt, 8 * s, KitInk.Hover);
                            KitInk.Draw(g, Crumb, tf, overParent ? KitInk.Text : KitInk.Sub, new RectangleF(x - 2 * s, line.Y, pw + 4 * s, line.Height), ContentAlignment.MiddleLeft);
                            x += pw + 6 * s;
                            using (var icons = new Font(Ink.IconFamily, 14f * s, GraphicsUnit.Pixel)) KitInk.Draw(g, "\uE76C", icons, KitInk.Sub, new RectangleF(x, line.Y, 20 * s, line.Height), ContentAlignment.MiddleCenter);
                            x += 26 * s;
                            Ring(g, parentAt, 8 * s);
                        }
                        KitInk.Draw(g, Title, tf, KitInk.Text, new RectangleF(x - 2 * s, line.Y, Width - x, line.Height), ContentAlignment.MiddleLeft);
                    }
                    using (var nf = new Font(Ink.MenuFamily, 13f * s, GraphicsUnit.Pixel)) KitInk.Draw(g, Note, nf, KitInk.Sub, new RectangleF(68 * s, 34 * s, Width - 68 * s, 22 * s), ContentAlignment.MiddleLeft);
                }
            }

            // Deskmate 2.0 big, build digits tiny
            KitRow VersionRow(Control field)
            {
                var r = Row("Deskmate " + AppVersion, Lang.T("Dynamic island untuk Windows · MIT"), field);
                string[] ver = AppVersion.Split('.');
                if (ver.Length > 2) { r.Title = "Deskmate " + ver[0] + "." + ver[1]; r.Suffix = "." + string.Join(".", ver, 2, ver.Length - 2); }
                return r;
            }

            void Caption(string text)
            {
                var l = new Label { Text = text, Font = UiFont(12f), ForeColor = KitInk.Sub, BackColor = KitInk.Window, AutoSize = true };
                // Ink lines up with the row icons
                l.Location = new Point((int)(42 * Ui) - LabelPad(l), (int)y);
                content.Controls.Add(l);
                y += 24 * Ui;
            }

            // Rows into one rounded group
            void Group(List<KitRow> rows)
            {
                if (rows.Count == 0) return;
                var g = new KitGroup { Left = (int)(28 * Ui), Top = (int)y, Width = rowW };
                int top = 0;
                foreach (var r in rows)
                {
                    r.Width = rowW;
                    r.Top = top;
                    r.Left = 0;
                    PlaceField(r);
                    g.Controls.Add(r);
                    top += r.Height;
                }
                g.Height = top;
                content.Controls.Add(g);
                y += top + 22 * Ui;
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
                new[] { "Buka jendela teleprompter", "\uE8A7" }, new[] { "Hentikan", "\uE71A" }, new[] { "Pintasan bawaan", "\uE765" }, new[] { "Tambah aplikasi", "\uE710" }, new[] { "Format jam", "\uE823" }, new[] { "Pakai warna sendiri", "\uE790" }, new[] { "Simpan jadi paket", "\uE74E" }, new[] { "Pembuat paket", "\uE774" }, new[] { "Pindai dari HP", "\uED14" }, new[] { "Aplikasi chat tambahan", "\uE8BD" }, new[] { "Pengirim prioritas", "\uE77B" }, new[] { "Senyap ikut Windows", "\uE708" }, new[] { "Suara", "\uE767" }, new[] { "Simpan isi rak", "\uE74E" }, new[] { "Kosongkan rak otomatis", "\uE916" }, new[] { "Ambil warna", "\uE790" }, new[] { "Ekspor pengaturan", "\uEDE1" }, new[] { "Dikte", "\uE720" }, new[] { "Tombol dikte", "\uE765" }, new[] { "Bahasa ucapan", "\uE774" }, new[] { "Mesin dikte", "\uE720" }, new[] { "Key Groq", "\uE8D7" }, new[] { "Buat key Groq", "\uE774" }, new[] { "Model offline", "\uE896" }, new[] { "Siap dipakai", "\uE73E" }, new[] { "Mengunduh", "\uE896" }, new[] { "Unduh model", "\uE896" }, new[] { "Kamus dan snippet", "\uE70B" }, new[] { "Buang kata pengisi", "\uE74D" }, new[] { "Perintah baris baru", "\uE751" }, new[] { "Gaya per aplikasi", "\uE71D" }, new[] { "Kecilkan musik", "\uE8D6" }, new[] { "Tempel dikte terakhir", "\uE77F" }, new[] { "Rekam rapat", "\uE7C8" }, new[] { "Ringkasan dan to-do", "\uE8A5" }, new[] { "Folder transkrip", "\uE8B7" }, new[] { "Apa yang baru", "\uE789" }, new[] { "Tur Deskmate", "\uE7C1" }, new[] { "GitHub", "\uE943" }, new[] { "Kirim saran", "\uED15" }, new[] { "Bicara", "\uE720" }, new[] { "Suruh Deskmate", "\uE765" }, new[] { "Instagram", "\uE722" }, new[] { "LinkedIn", "\uE716" }, new[] { "Impor pengaturan", "\uE8B5" }, new[] { "Setel ulang pengaturan", "\uE72C" },
                new[] { "Banner pesan", "\uE8BD" }, new[] { "Banner Windows masih nyala", "\uE7BA" }, new[] { "Deskmate penerima utama", "\uEA8F" }, new[] { "Mode berbagi layar", "\uE7F4" }, new[] { "Isi kiri", "\uE8A0" }, new[] { "Isi kanan", "\uE8A0" },
                new[] { "Atur Notch", "\uE737" }, new[] { "Atur Dock", "\uE90E" }, new[] { "Beranda", "\uE80F" }, new[] { "Lokasi & Cuaca", "\uE707" }, new[] { "Tema & Gaya", "\uE790" },
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
                var r = new KitRow { Title = title, Detail = detail ?? "", Field = field, Height = (int)((string.IsNullOrEmpty(detail) ? 52 : 62) * Ui), Glyph = GlyphFor(title) };
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
                        return;
                    }
                    row.Detail = Lang.T("Mencari lokasi…");
                    row.Invalidate();
                    System.Threading.ThreadPool.QueueUserWorkItem(delegate
                    {
                        string error;
                        var hits = Cities.Search(typed, out error);
                        BeginInvoke(new Action(delegate
                        {
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
                            Relayout();
                        }));
                    });
                };

                f.Submit = save;
                f.Box.Leave += delegate { if (!building) save(); };
                return row;
            }

            // Controls with settings plumbing

            KitSwitch Switch(string key, bool fallback, Action<bool> after)
            {
                var s = new KitSwitch { Checked = settings.GetBool(key, fallback), Size = new Size((int)(46 * Ui), (int)(26 * Ui)) };
                s.CheckedChanged += delegate { if (building) return; if (after != null) after(s.Checked); else island.SetFlag(key, s.Checked); };
                return s;
            }

            KitSegmented Seg(string[] items, int selected, Action<int> changed)
            {
                float w = 0;
                using (var f = UiFont(12.5f)) foreach (var it in items) w = Math.Max(w, TextRenderer.MeasureText(it, f).Width);
                var c = new KitSegmented { Items = items, Size = new Size((int)((w + 30 * Ui) * items.Length), (int)(30 * Ui)) };
                c.Selected = selected;
                c.Changed += i => { if (!building) changed(i); };
                return c;
            }

            KitSlider Slider(int min, int max, int step, int value, Func<int, string> fmt, Action<int> changed)
            {
                var c = new KitSlider { Minimum = min, Maximum = max, Step = step, Value = Math.Max(min, Math.Min(max, value)), Format = fmt, ShowValue = true, Size = new Size((int)(270 * Ui), (int)(30 * Ui)) };
                c.Changed += v => { if (!building) { changed(v); content.Invalidate(true); } };
                return c;
            }

            KitPopup Popup(string[] items, int selected, Action<int> changed)
            {
                var c = new KitPopup { Items = items, Font = UiFont(12.5f) };
                c.Selected = selected;
                float w = 0;
                using (var f = UiFont(12.5f)) foreach (var it in items) w = Math.Max(w, TextRenderer.MeasureText(it + "  ▾", f).Width);
                c.Size = new Size((int)Math.Min(260 * Ui, w + 30 * Ui), (int)(30 * Ui));
                c.Changed += i => { if (!building) changed(i); };
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
                back.Tick += delegate { back.Stop(); row.Detail = detail; row.Invalidate(); };
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
            // Starting points that read well together
            static string[][] SwatchPresets(string key)
            {
                if (key == "bg") return new[] { new[] { "Hitam", "#000000" }, new[] { "Arang", "#1C1C1E" }, new[] { "Navy", "#0E1734" }, new[] { "Hijau tua", "#0F2A22" }, new[] { "Putih", "#FFFFFF" }, new[] { "Krem", "#F5EFE6" } };
                if (key == "text") return new[] { new[] { "Putih", "#FFFFFF" }, new[] { "Putih lembut", "#EEF4FF" }, new[] { "Hitam", "#111111" }, new[] { "Cokelat tua", "#3B2F2F" } };
                var list = new System.Collections.Generic.List<string[]>();
                var table = Ink.Light ? AccentsLight : Accents;
                foreach (var a in table) if (a[1].StartsWith("#")) list.Add(a);
                return list.ToArray();
            }

            KitRow ColorRow(string key, string title, string detail)
            {
                var sw = new KitSwatch { Font = UiFont(12.5f), Value = island.CustomColor(key), Presets = SwatchPresets(key) };
                sw.Size = new Size(TextRenderer.MeasureText("      #DDDDDD", sw.Font).Width + (int)(32 * Ui), (int)(30 * Ui));
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
                var field = new KitHotkey { Font = UiFont(12.5f) };
                var row = Row(Lang.T(d.Name), HotkeyDetail(d), field);
                field.Size = new Size(TextRenderer.MeasureText("Ctrl+Alt+Shift+PageDown", field.Font).Width + (int)(32 * Ui), (int)(30 * Ui));
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

            KitButton Button(string text, bool primary, Action click)
            {
                var b = new KitButton { Text = text, Primary = primary, Font = UiFont(12.5f) };
                b.Size = new Size(TextRenderer.MeasureText(text, b.Font).Width + (int)(32 * Ui), (int)(30 * Ui));
                b.Click += delegate { click(); };
                return b;
            }

            void Preview()
            {
                island.PreviewLayout(true);
                preview.Stop();
                preview.Start();
            }

            // Open on one tab while tuning it
            void Preview(int page)
            {
                island.PreviewPage(page);
                preview.Stop();
                preview.Start();
            }

            // Sections; search only fills the list
            void Build(string id, List<KitRow> rows, bool show)
            {
                var group = new List<KitRow>();
                Action flush = delegate { if (show) Group(group); group = new List<KitRow>(); };
                Action<string> cap = c => { if (show) { flush(); Caption(c); } };
                Action<KitRow> add = r => { rows.Add(r); group.Add(r); };
                switch (id)
                {
                    case "overview":
                        cap(Lang.T("Status"));
                        var dockOn = settings.GetBool("dockbar.on", false);
                        var comboOn = settings.GetBool("keys.combo", false);
                        var dndOn = settings.GetBool("dnd", false);
                        add(Row(Lang.T("Dock"), dockOn ? Lang.T("Aktif") : Lang.T("Mati"), Switch("dockbar.on", false, on => { island.SetFlag("dockbar.on", on); island.ApplyDock(); Relayout(); })));
                        add(Row(Lang.T("Combo Ketikan"), comboOn ? Lang.T("Aktif") : Lang.T("Mati"), Switch("keys.combo", false, on => { island.SetFlag("keys.combo", on); island.SyncComboHook(); Relayout(); })));
                        add(Row(Lang.T("Senyap"), dndOn ? Lang.T("Aktif") : Lang.T("Mati"), Switch("dnd", false, on => { island.SetFlag("dnd", on); Relayout(); })));
                        string ovTheme = settings.Get("theme", "gelap");
                        add(Row(Lang.T("Tema"), ovTheme == "otomatis" ? Lang.T("Mengikuti mode aplikasi Windows") : null,
                            Seg(new[] { Lang.T("Gelap"), Lang.T("Terang"), Lang.T("Otomatis") }, ovTheme == "terang" ? 1 : ovTheme == "otomatis" ? 2 : 0, i =>
                            {
                                island.SetThemeMode(i == 1 ? "terang" : i == 2 ? "otomatis" : "gelap");
                                BeginInvoke(new Action(Relayout));
                            })));
                        add(Row(Lang.T("Mulai bersama Windows"), null, StartupSwitch()));
                        cap(Lang.T("Pintasan cepat"));
                        add(Row(Lang.T("Atur Notch"), Lang.T("Letak, ukuran, perilaku panel, dan saat diam"), Button(Lang.T("Buka"), false, delegate { Go("island"); })));
                        add(Row(Lang.T("Atur Dock"), Lang.T("Bar aplikasi, widget, dan pengganti taskbar"), Button(Lang.T("Buka"), false, delegate { Go("dock"); })));
                        add(Row(Lang.T("Integrasi"), Lang.T("Kulia, Notion, Todoist, dan kalender iCal"), Button(Lang.T("Buka"), false, delegate { Go("integrasi"); })));
                        add(Row(Lang.T("Gamifikasi Ketikan"), Lang.T("Suara, Fever Mode, penalti, XP, dan Sprint 30 detik"), Button(Lang.T("Buka"), false, delegate { Go("combo"); })));
                        break;
                    case "general":
                        add(VersionRow(null));
                        cap(Lang.T("Bahasa dan jam"));
                        add(Row(Lang.T("Bahasa"), null, Seg(new[] { "Indonesia", "English" }, Lang.English ? 1 : 0, i => { island.SetLanguage(i == 1); Relayout(); })));
                        add(Row(Lang.T("Format jam"), null, Seg(new[] { "14.30", "2.30 pm" }, Fmt.H24 ? 0 : 1, i => island.SetClock24(i == 0))));
                        cap(Lang.T("Sistem"));
                        add(Row(Lang.T("Mulai bersama Windows"), null, StartupSwitch()));
                        add(Row(Lang.T("Cek versi baru"), Lang.T("Sekali sehari ke GitHub; unduhan hanya saat diminta"), Switch("update.check", true, on => { settings.Set("update.check", on); settings.Save(); })));
                        cap(Lang.T("Data"));
                        add(Row(Lang.T("Folder data"), Settings.Dir, Button(Lang.T("Buka"), false, delegate { try { Process.Start("explorer.exe", "\"" + Settings.Dir + "\""); } catch (Exception) { } })));
                        KitRow io = null;
                        io = Row(Lang.T("Ekspor pengaturan"), Lang.T("Tanpa token, port, dan pasangan HP"), Button(Lang.T("Simpan…"), false, delegate
                        {
                            using (var d = new SaveFileDialog { FileName = "deskmate-pengaturan.ini", Filter = "INI|*.ini", RestoreDirectory = true })
                            {
                                if (d.ShowDialog(this) != DialogResult.OK) return;
                                try { io.Detail = Lang.F("{0} pengaturan disimpan", island.ExportSettings(d.FileName)); }
                                catch (Exception ex) { io.Detail = Lang.T("Gagal menyimpan"); Log.Write("export: " + ex.Message); }
                                io.Invalidate();
                            }
                        }));
                        add(io);
                        KitRow inRow = null;
                        inRow = Row(Lang.T("Impor pengaturan"), Lang.T("Deskmate dibuka ulang sesudahnya"), Button(Lang.T("Buka…"), false, delegate
                        {
                            using (var d = new OpenFileDialog { Filter = "INI|*.ini|*.*|*.*", RestoreDirectory = true })
                            {
                                if (d.ShowDialog(this) != DialogResult.OK) return;
                                int n = 0;
                                try { n = island.ImportSettings(d.FileName); }
                                catch (Exception ex) { Log.Write("import: " + ex.Message); }
                                if (n == 0) { inRow.Detail = Lang.T("Tidak ada pengaturan Deskmate di berkas itu"); inRow.Invalidate(); return; }
                                island.RestartDeskmate();
                            }
                        }));
                        add(inRow);
                        // Second click within 4 s confirms
                        KitButton resetBtn = null;
                        KitRow resetRow = null;
                        string resetNote = Lang.T("Integrasi, agen, dan HP tetap tersambung");
                        var armed = new System.Windows.Forms.Timer { Interval = 4000 };
                        armed.Tick += delegate { armed.Stop(); resetBtn.Text = Lang.T("Setel ulang"); resetRow.Detail = resetNote; resetBtn.Invalidate(); resetRow.Invalidate(); };
                        resetBtn = Button(Lang.T("Setel ulang"), false, delegate
                        {
                            if (!armed.Enabled)
                            {
                                armed.Start();
                                resetBtn.Text = Lang.T("Klik lagi");
                                resetRow.Detail = Lang.T("Klik sekali lagi dalam 4 detik untuk menyetel ulang");
                                resetBtn.Invalidate();
                                resetRow.Invalidate();
                                return;
                            }
                            armed.Stop();
                            island.ResetSettings();
                            island.RestartDeskmate();
                        });
                        resetBtn.Disposed += delegate { armed.Dispose(); };
                        resetRow = Row(Lang.T("Setel ulang pengaturan"), resetNote, resetBtn);
                        resetRow.Glyph = '\uE7A7';
                        add(resetRow);
                        break;
                    case "look":
                        cap(Lang.T("Gaya visual"));
                        string themeMode = settings.Get("theme", "gelap");
                        int themeIdx = themeMode == "terang" ? 1 : themeMode == "otomatis" ? 2 : 0;
                        var themeCards = new ThemeCardPicker
                        {
                            SelectedIndex = themeIdx,
                            Size = new Size((int)(246 * Ui), (int)(54 * Ui))
                        };
                        themeCards.Picked += i =>
                        {
                            island.SetThemeMode(i == 1 ? "terang" : i == 2 ? "otomatis" : "gelap");
                            BeginInvoke(new Action(Relayout));
                        };
                        var themeRow = Row(Lang.T("Tema"), themeMode == "otomatis" ? Lang.T("Mengikuti mode aplikasi Windows") : null, themeCards);
                        themeRow.Height = (int)(70 * Ui);
                        add(themeRow);

                        string accent = settings.Get("accent", "");
                        bool custom = island.CustomOn;
                        KitRow accentRow = null;
                        var palette = new KitPalette
                        {
                            Presets = Accents,
                            PresetsLight = AccentsLight,
                            SelectedHex = accent,
                            Enabled = !custom,
                            Size = new Size((int)(240 * Ui), (int)(30 * Ui))
                        };
                        palette.Picked += (i, hex) =>
                        {
                            island.SetAccentHex(hex);
                            if (accentRow != null)
                            {
                                accentRow.Detail = Lang.T(Accents[i][0]);
                                accentRow.Invalidate();
                            }
                        };
                        string curAccentName = Lang.T("Grafit");
                        for (int i = 0; i < Accents.Length; i++)
                        {
                            if (Accents[i][1] == accent || AccentsLight[i][1] == accent)
                            {
                                curAccentName = Lang.T(Accents[i][0]);
                                break;
                            }
                        }
                        string accentDetail = custom ? Lang.T("Warna sendiri sedang dipakai") :
                                              settings.Get("theme.pack", "").Length > 0 ? Lang.T("Paket tema bisa menimpanya") :
                                              curAccentName;
                        accentRow = Row(Lang.T("Warna aksen"), accentDetail, palette);
                        add(accentRow);

                        // Detail follows the chosen material; capture mode overrides glass entirely
                        string mat = island.MaterialName;
                        Func<int, string> matNote = i => settings.GetBool("capture.show", false) ? Lang.T("Tidak berefek saat tampil di rekaman layar aktif") :
                                                         i == 1 ? Lang.T("Buram lembut, tenang di mata") :
                                                         i == 2 ? Lang.T("Hitam paling hemat daya") :
                                                         Lang.T("Kaca cair, warna di belakangnya tetap hidup");
                        KitRow matRow = null;
                        var matSeg = Seg(new[] { "Liquid", "Frost", Lang.T("Hitam") }, mat == "frost" ? 1 : mat == "solid" ? 2 : 0, i =>
                        {
                            island.SetChoice("material", i == 1 ? "frost" : i == 2 ? "solid" : "liquid");
                            if (matRow == null) return;
                            matRow.Detail = matNote(i);
                            if (matRow.Field != null) matRow.Field.AccessibleDescription = matRow.Detail;
                            matRow.Invalidate();
                        });
                        matRow = Row(Lang.T("Material"), matNote(mat == "frost" ? 1 : mat == "solid" ? 2 : 0), matSeg);
                        add(matRow);

                        add(Row(Lang.T("Sinkronkan wallpaper"), custom ? Lang.T("Warna sendiri sedang dipakai") : Lang.T("Kaca dan aksen mengikuti wallpaper; blur tetap"), Switch("wall.sync", false, null)));

                        cap(Lang.T("Kustomisasi"));
                        add(Row(Lang.T("Warna kustom"), Lang.T("Pilih warna latar, teks, dan aksen manual"), Switch("look.custom", false, on =>
                        {
                            island.SetCustom(on);
                            KitWindow.BroadcastThemeChange();
                            BeginInvoke(new Action(Relayout));
                        })));
                        if (custom)
                        {
                            add(ColorRow("bg", Lang.T("Latar"), Lang.T("Klik untuk memilih atau ambil dari layar")));
                            add(ColorRow("text", Lang.T("Teks"), ContrastNote()));
                            add(ColorRow("accent", Lang.T("Aksen"), Lang.T("Klik untuk memilih atau ambil dari layar")));
                            KitRow saved = null;
                            saved = Row(Lang.T("Simpan jadi paket"), Lang.T("Ke folder themes, lalu bisa ditambah gambar"), Button(Lang.T("Simpan"), false, delegate
                            {
                                try { saved.Detail = Lang.F("Tersimpan sebagai {0}", island.SaveCustomPack()); }
                                catch (Exception ex) { saved.Detail = Lang.T("Gagal menyimpan"); Log.Write("save pack: " + ex.Message); }
                                saved.Invalidate();
                            }));
                            add(saved);
                        }
                        cap(Lang.T("Paket tema"));
                        var packs = new List<string> { Lang.T("Tanpa") };
                        packs.AddRange(ThemePacks());
                        int packAt = Math.Max(0, packs.IndexOf(settings.Get("theme.pack", "")));
                        add(Row(Lang.T("Paket"), Lang.T("Folder di themes, berisi theme.ini dan gambar"), Popup(packs.ToArray(), packAt, i =>
                        {
                            settings.Set("theme.pack", i == 0 ? "" : packs[i]);
                            settings.Save();
                            island.ApplyPack();
                            Relayout();
                        })));
                        add(Row(Lang.T("Suara sapaan paket tema"), null, Switch("theme.voice", true, null)));
                        add(Row(Lang.T("Folder paket"), null, Button(Lang.T("Buka"), false, delegate
                        {
                            string dir = System.IO.Path.Combine(Settings.Dir, "themes");
                            try { System.IO.Directory.CreateDirectory(dir); Process.Start("explorer.exe", "\"" + dir + "\""); } catch (Exception) { }
                        })));
                        add(Row(Lang.T("Pembuat paket"), Lang.T("Gambar, sapaan, dan suara, dari pembuat paket upstream"), Button(Lang.T("Buka"), false, delegate { OpenUrl("https://odlaver.github.io/tepi/pack/"); })));
                        break;
                    case "island":
                        cap(Lang.T("Letak & Ukuran"));
                        add(PlaceRow());
                        int place = island.placeMode;
                        if (place == PlaceTop)
                            add(Row(Lang.T("Geser"), null, Slider(-40, 40, 5, settings.GetInt("position", 0), v => v == 0 ? Lang.T("tengah") : (v < 0 ? Lang.T("kiri") : Lang.T("kanan")) + " " + Math.Abs(v) + "%",
                                v => { island.SetLayout("position", v); Preview(); })));
                        else if (place == PlaceTopLeft || place == PlaceTopRight)
                            add(Row(Lang.T("Jarak dari pojok"), null, Slider(0, 240, 4, (int)island.placeInset, v => v + " px", v => island.SetPlaceValue("place.inset", v))));
                        else
                            add(Row(Lang.T("Tinggi"), Lang.T("Dari tepi atas layar"), Slider(0, 100, 2, (int)Math.Round(island.placeY * 100), v => v + "%", v => island.SetPlaceValue("place.y", v))));
                        cap(Lang.T("Bentuk"));
                        add(Row(Lang.T("Menempel di tepi layar"), Lang.T("Bentuk notch yang menyatu dengan tepi"), Switch("dock", true, null)));
                        add(Row(Lang.T("Garis cahaya"), Lang.T("Progres timer, unduhan, dan cas di tepi island; kilau saat pesan masuk"), Switch("edge.light", true, null)));
                        cap(Lang.T("Ukuran dan layar"));
                        add(Row(Lang.T("Lebar panel"), null, Slider(440, 640, 8, (int)island.PanelW, v => v + " px", v => { island.SetLayout("panel.width", v); Preview(); })));
                        var screens = Screen.AllScreens;
                        var labels = new string[screens.Length];
                        var chosen = island.ChosenScreen();
                        int screenAt = 0;
                        for (int i = 0; i < screens.Length; i++)
                        {
                            labels[i] = Lang.F("Layar {0}", i + 1) + (screens[i].Primary ? " · " + Lang.T("utama") : "") +
                                " · " + screens[i].Bounds.Width + " × " + screens[i].Bounds.Height;
                            if (chosen != null && string.Equals(screens[i].DeviceName, chosen.DeviceName, StringComparison.OrdinalIgnoreCase)) screenAt = i;
                        }
                        var mon = Popup(labels, screenAt, i => island.UseMonitor(screens[i].DeviceName));
                        mon.Enabled = screens.Length > 1;
                        add(Row(Lang.T("Layar"), screens.Length > 1 ? Lang.T("Pilih layar tempat island tampil") : Lang.T("Sambungkan layar lain untuk memilihnya"), mon));
                        var reserve = Switch("reserve.top", false, on => { settings.Set("reserve.top", on); settings.Save(); island.ApplyReserve(); });
                        bool sideNow = place == PlaceLeft || place == PlaceRight;
                        reserve.Enabled = !sideNow;
                        add(Row(Lang.T("Sisakan ruang di tepi atas"), sideNow ? Lang.T("Hanya untuk letak di atas") : Lang.T("Jendela yang dimaksimalkan mulai di bawah island"), reserve));
                        cap(Lang.T("Perilaku & Fokus"));
                        string mode = settings.Get("mini", "auto");
                        add(Row(Lang.T("Mode"), Lang.T("Otomatis: mini saat tertutup atau diam"),
                            Seg(new[] { Lang.T("Normal"), Lang.T("Mini"), Lang.T("Otomatis") }, mode == "normal" ? 0 : mode == "mini" ? 1 : 2, i => island.SetMiniMode(i == 0 ? "normal" : i == 1 ? "mini" : "auto"))));
                        add(Row(Lang.T("Notifikasi saat mini"), null, Switch("mini.notif", true, null)));
                        cap(Lang.T("Panel"));
                        bool byClick = settings.Get("panel.open", "hover") == "click";
                        var delay = Slider(150, 800, 50, settings.GetInt("panel.delay", 330), v => v + " ms", v => { settings.Set("panel.delay", v); settings.Save(); });
                        delay.Enabled = !byClick;
                        add(Row(Lang.T("Buka panel"), null, Seg(new[] { Lang.T("Arahkan kursor"), Lang.T("Klik") }, byClick ? 1 : 0, i =>
                        {
                            island.SetChoice("panel.open", i == 1 ? "click" : "hover");
                            delay.Enabled = i == 0;
                        })));
                        add(Row(Lang.T("Jeda buka"), Lang.T("Sebelum panel terbuka"), delay));
                        cap(Lang.T("Pengecualian Aplikasi"));
                        var rules = island.ExceptRules();
                        foreach (var rule in rules)
                        {
                            var r = rule;
                            var modes = new[] { Lang.T("Sembunyikan island"), Lang.T("Tanpa notifikasi"), Lang.T("Jeda gamifikasi"), Lang.T("Hapus dari daftar") };
                            var appRow = Row(r.Name, r.Exe, Popup(modes, r.Mode == ExceptCombo ? 2 : r.Mode == ExceptQuiet ? 1 : 0, i =>
                            {
                                if (i == 3) rules.Remove(r);
                                else r.Mode = i == 2 ? ExceptCombo : i == 1 ? ExceptQuiet : ExceptHide;
                                island.SaveExceptRules(rules);
                                if (i == 3) BeginInvoke(new Action(Relayout));
                            }));
                            appRow.Glyph = '\uE71D';
                            add(appRow);
                        }
                        KitButton pick = null;
                        pick = Button(Lang.T("Pilih aplikasi"), false, delegate
                        {
                            var m = new ContextMenuStrip();
                            foreach (var app in island.OpenApps())
                            {
                                var a = app;
                                bool listed = rules.Exists(x => x.Exe == a.Exe);
                                m.Items.Add(new ToolStripMenuItem(a.Name, null, delegate
                                {
                                    rules.Add(a);
                                    island.SaveExceptRules(rules);
                                    BeginInvoke(new Action(Relayout));
                                }) { Enabled = !listed, ToolTipText = a.Exe });
                            }
                            if (m.Items.Count == 0) m.Items.Add(new ToolStripMenuItem(Lang.T("Tidak ada jendela aplikasi yang terbuka")) { Enabled = false });
                            MenuLook.Apply(m);
                            m.Closed += delegate { BeginInvoke(new Action(m.Dispose)); };
                            m.Show(pick, new Point(0, pick.Height));
                        });
                        add(Row(Lang.T("Tambah aplikasi"), Lang.T("Atur perilaku Island saat aplikasi di depan"), pick));
                        cap(Lang.T("Saat Diam"));
                        add(Row(Lang.T("Mata"), null, Switch("island.eyes", true, on => { island.SetFlag("island.eyes", on); BeginInvoke(new Action(Relayout)); })));
                        // Only means something while eyes show
                        var follow = Switch("island.follow", true, null);
                        follow.Enabled = settings.GetBool("island.eyes", true);
                        add(Row(Lang.T("Ikuti kursor"), follow.Enabled ? Lang.T("Mata melirik ke arah kursor") : Lang.T("Nyalakan Mata dulu"), follow));
                        // The face also decides what the mini notch shows, so it stays usable
                        var face = Seg(new[] { Lang.T("Mata"), Lang.T("Jam") }, settings.Get("idle.face", "eyes") == "clock" ? 1 : 0, i => { island.SetChoice("idle.face", i == 1 ? "clock" : "eyes"); BeginInvoke(new Action(Relayout)); });
                        face.Enabled = island.EyesAllowed;
                        add(Row(Lang.T("Saat diam"), face.Enabled ? Lang.T("Isi island saat tidak ada aktivitas, juga di mini") : Lang.T("Tema ini tidak memakai mata"), face));
                        cap(Lang.T("Isi samping"));
                        var slotNames = Array.ConvertAll(SlotNames, n => Lang.T(n));
                        foreach (var side in new[] { new[] { "idle.left", "Isi kiri" }, new[] { "idle.right", "Isi kanan" } })
                        {
                            string sk = side[0];
                            add(Row(Lang.T(side[1]), Lang.T("Di samping mata atau jam saat island diam"), Popup(slotNames, Math.Max(0, Array.IndexOf(SlotIds, settings.Get(sk, ""))), i => island.SetChoice(sk, SlotIds[i]))));
                        }
                        break;
                    case "tabs":
                    case "home":
                        string style = settings.Get("tabs.style", "auto");
                        add(Row(Lang.T("Gaya tab"), Lang.T("Otomatis: titik bila tab banyak"),
                            Seg(new[] { Lang.T("Otomatis"), Lang.T("Ikon"), Lang.T("Titik") }, style == "icons" ? 1 : style == "dots" ? 2 : 0, i => island.SetChoice("tabs.style", i == 1 ? "icons" : i == 2 ? "dots" : "auto"))));
                        bool tabHoverOn = settings.Get("tabs.trigger", "click") == "hover";
                        add(Row(Lang.T("Pindah tab"), Lang.T("Gulir roda mouse selalu bisa"),
                            Seg(new[] { Lang.T("Klik"), Lang.T("Arahkan kursor") }, tabHoverOn ? 1 : 0, i => island.SetChoice("tabs.trigger", i == 1 ? "hover" : "click"))));
                        var openNames = new List<string> { Lang.T("Tab pertama") };
                        foreach (var n in PageNames) openNames.Add(Lang.T(n));
                        int openAt = Array.IndexOf(PageIds, settings.Get("panel.first", "")) + 1;
                        add(Row(Lang.T("Dibuka di"), null, Popup(openNames.ToArray(), Math.Max(0, openAt), i => island.SetLayout("panel.first", i > 0 ? PageIds[i - 1] : ""))));
                        cap(Lang.T("Tab yang tampil"));
                        // Shown in panel order, hidden after
                        var order = new List<int>(island.tabs);
                        for (int pg = 0; pg < PageNames.Length; pg++) if (!order.Contains(pg)) order.Add(pg);
                        tabFocus.Clear();
                        foreach (int pg in order) add(TabRow(pg));
                        cap(Lang.T("Kartu Ringkasan"));
                        add(Row(Lang.T("Urutan kartu"), Lang.T("Otomatis: yang sedang berjalan naik ke atas"),
                            Seg(new[] { Lang.T("Otomatis"), Lang.T("Sesuai daftar") }, island.HomeAuto ? 0 : 1, i => { island.SetChoice("home.sort", i == 1 ? "list" : "auto"); Preview(PageHome); })));
                        cap(Lang.T("Kartu yang tampil"));
                        // Shown in order, hidden after
                        var cards = new List<HomeCard>(island.homeCards);
                        foreach (HomeCard c in Enum.GetValues(typeof(HomeCard))) if (!cards.Contains(c)) cards.Add(c);
                        tabFocus.Clear();
                        foreach (var c in cards) add(CardRow(c));
                        break;
                    case "dock":
                        DockRows(cap, add);
                        break;
                    case "work":
                        add(Row(Lang.T("Tugas dan catatan"), Lang.T("Buka daftar tugas dan editor catatan lokal"),
                            Button(Lang.T("Buka"), true, delegate { island.OpenWork(); })));
                        add(Row(Lang.T("Tombol di dock"), Lang.T("Satu tombol untuk mengakses Tugas dan catatan"), Switch("dockbar.work", true, null)));
                        add(Row(Lang.T("Pintasan keyboard"), island.HotkeyText(WorkHotkey),
                            Button(Lang.T("Atur pintasan"), false, delegate { Go("keys"); })));
                        cap(Lang.T("Fokus & Prioritas"));
                        add(Row(Lang.T("Tahan notifikasi saat fokus"), Lang.T("Notifikasi biasa ditunda sampai sesi fokus selesai"), Switch("focus.hold", true, null)));
                        add(Row(Lang.T("Istirahat fokus"), null, Slider(1, 30, 1, settings.GetInt("focus.break", 5), v => v + Lang.T(" menit"), v => { settings.Set("focus.break", v); settings.Save(); })));
                        add(Row(Lang.T("3 Prioritas Hari Ini"), Lang.F("{0}/{1} selesai hari ini", island.PriorityDoneCount, island.PriorityCount), Button(Lang.T("Tambah"), false, delegate { island.PromptAddPriority(); })));
                        KitButton resetFocusBtn = null;
                        KitRow resetFocusRow = null;
                        string resetFocusNote = Lang.F("Hari ini {0} mnt · Minggu ini {1} mnt · Streak {2} hari · {3} tugas", island.FocusMinToday, island.FocusWeekTotalMin, island.FocusStreakDays, island.FocusTasksDone);
                        var focusArmed = new System.Windows.Forms.Timer { Interval = 4000 };
                        int focusResetIdleW = 0;
                        resetFocusBtn = Button(Lang.T("Reset"), false, delegate
                        {
                            if (!focusArmed.Enabled)
                            {
                                focusArmed.Start();
                                int right = resetFocusBtn.Right;
                                resetFocusBtn.Text = Lang.T("Klik lagi");
                                int needW = TextRenderer.MeasureText(resetFocusBtn.Text, resetFocusBtn.Font).Width + (int)(32 * Ui);
                                if (needW != resetFocusBtn.Width)
                                {
                                    resetFocusBtn.Width = needW;
                                    resetFocusBtn.Left = right - needW;
                                }
                                resetFocusRow.Detail = Lang.T("Klik sekali lagi dalam 4 detik untuk menghapus statistik");
                                resetFocusBtn.Invalidate();
                                resetFocusRow.Invalidate();
                                return;
                            }
                            focusArmed.Stop();
                            island.ResetFocusStats();
                            Relayout();
                        });
                        focusResetIdleW = resetFocusBtn.Width;
                        focusArmed.Tick += delegate
                        {
                            focusArmed.Stop();
                            int right = resetFocusBtn.Right;
                            resetFocusBtn.Text = Lang.T("Reset");
                            resetFocusBtn.Width = focusResetIdleW;
                            resetFocusBtn.Left = right - focusResetIdleW;
                            resetFocusRow.Detail = resetFocusNote;
                            resetFocusBtn.Invalidate();
                            resetFocusRow.Invalidate();
                        };
                        resetFocusBtn.Disposed += delegate { focusArmed.Dispose(); };
                        resetFocusRow = Row(Lang.T("Statistik Fokus"), resetFocusNote, resetFocusBtn);
                        add(resetFocusRow);
                        break;
                    case "music":
                        add(Row(Lang.T("Lirik di island"), null, Switch("lyrics", true, null)));
                        add(Row(Lang.T("Cari lirik online"), Lang.T("Judul, artis, dan durasi ke LRCLIB, cadangannya NetEase"), Switch("lyrics.online", true, null)));
                        add(Row(Lang.T("Visualizer"), null, Switch("visualizer", true, null)));
                        break;
                    case "messages":
                        cap(Lang.T("Aturan notifikasi aplikasi"));
                        var notifApps = island.NotifApps(selectedNotifAppId, selectedNotifAppName);
                        if (notifApps.Count == 0)
                            add(Row(Lang.T("Belum ada aplikasi"), Lang.T("Klik kanan pesan di Island untuk mengatur aplikasi pengirim."), null));
                        foreach (var app in notifApps)
                        {
                            KitButton ruleButton = null;
                            ruleButton = Button(Lang.T("Atur"), false, delegate
                            {
                                var popup = island.BuildNotifAppMenu(app.Key, app.Value, delegate { BeginInvoke(new Action(RefreshNotifApps)); }, false);
                                MenuLook.Apply(popup);
                                popup.Closed += delegate { BeginInvoke(new Action(popup.Dispose)); };
                                popup.Show(ruleButton, new Point(0, ruleButton.Height));
                            });
                            add(Row(app.Value, island.NotifAppRuleStatus(app.Key), ruleButton));
                        }
                        cap(Lang.T("Pengaturan umum"));
                        add(Row(Lang.T("Notifikasi pesan"), null, Switch("notif", true, on => { island.SetFlag("notif", on); Relayout(); })));
                        var body = Switch("notif.content", true, null);
                        body.Enabled = settings.GetBool("notif", true);
                        add(Row(Lang.T("Tampilkan isi pesan"), body.Enabled ? null : Lang.T("Ikut notifikasi pesan"), body));
                        bool deskmateBanner = settings.Get("notif.banner", "auto") == "deskmate";
                        add(Row(Lang.T("Banner pesan"), deskmateBanner ? Lang.T("Deskmate selalu muncul; matikan banner Windows supaya tidak dobel") : Lang.T("Deskmate muncul hanya untuk aplikasi yang banner Windows-nya mati"),
                            Seg(new[] { Lang.T("Otomatis"), "Deskmate" }, deskmateBanner ? 1 : 0, i => { settings.Set("notif.banner", i == 1 ? "deskmate" : "auto"); settings.Save(); Relayout(); })));
                        // Deskmate first: one click turns Windows banners off, one click brings them back
                        int muted = settings.Get("notif.muted", "").Split(new[] { '|' }, StringSplitOptions.RemoveEmptyEntries).Length;
                        if (muted == 0)
                            add(Row(Lang.T("Deskmate penerima utama"), Lang.T("Matikan banner Windows untuk chat, panggilan, dan screenshot"),
                                Button(Lang.T("Jadikan utama"), true, delegate
                                {
                                    int n = island.MakeDeskmatePrimary();
                                    island.ShowInfo('', n > 0 ? Lang.F("Banner Windows dimatikan untuk {0} aplikasi", n) : Lang.T("Banner Windows sudah mati"), Lang.T("Semua notifikasi lewat Deskmate"), true);
                                    Relayout();
                                })));
                        else
                            add(Row(Lang.T("Deskmate penerima utama"), Lang.F("Banner Windows dimatikan untuk {0} aplikasi", muted),
                                Button(Lang.T("Kembalikan banner Windows"), false, delegate { island.RestoreWindowsBanners(); Relayout(); })));
                        var doubled = island.DoubledApps();
                        if (deskmateBanner && doubled.Count > 0)
                            add(Row(Lang.T("Banner Windows masih nyala"), string.Join(", ", doubled.ToArray()), Button(Lang.T("Buka pengaturan Windows"), false, delegate { try { Process.Start("ms-settings:notifications"); } catch (Exception ex) { Log.Write("ms-settings: " + ex.Message); } })));
                        add(ListRow(Lang.T("Aplikasi chat tambahan"), Lang.T("Nama aplikasi dipisah koma, Enter menyimpan"), "notif.apps", "Element, Zalo"));
                        string share = settings.Get("share.mode", "auto");
                        add(Row(Lang.T("Mode berbagi layar"), Lang.T("Zoom, Meet, Teams: isi pesan disamarkan saat berbagi layar"),
                            Seg(new[] { Lang.T("Otomatis"), Lang.T("Nyala"), Lang.T("Mati") }, share == "on" ? 1 : share == "off" ? 2 : 0, i => island.SetChoice("share.mode", i == 1 ? "on" : i == 2 ? "off" : "auto"))));
                        add(ListRow(Lang.T("Pengirim prioritas"), Lang.T("Tetap muncul saat Senyap, Enter menyimpan"), "notif.vip", Lang.T("Ibu, dosen")));
                        cap(Lang.T("Senyap"));
                        add(Row(Lang.T("Senyap"), Lang.T("Hanya izin, tenggat, sholat, dan timer yang muncul"), Switch("dnd", false, null)));
                        add(Row(Lang.T("Senyap ikut Windows"), Lang.T("Ikut menyala saat Jangan ganggu Windows aktif"), Switch("dnd.follow", false, null)));
                        break;
                    case "system":
                        cap(Lang.T("Keyboard"));
                        add(Row("Caps Lock", Lang.T("HUD saat ditekan, penanda selama nyala"), Switch("keys.caps", true, null)));
                        add(Row("Num Lock, Scroll Lock", Lang.T("HUD saat ditekan"), Switch("keys.num", false, null)));
                        add(Row(Lang.T("Bahasa input"), Lang.T("HUD saat layout keyboard berganti"), Switch("keys.lang", true, null)));
                        add(Row(Lang.T("Kustomisasi gamifikasi"), Lang.T("Suara, Fever Mode, penalti, XP, dan Sprint 30 detik"), Button(Lang.T("Buka"), false, delegate { Go("combo"); })));
                        cap(Lang.T("Perangkat"));
                        int[] unplugAt = { 0, 80, 85, 90 };
                        add(Row(Lang.T("Ingatkan cabut charger"), Lang.T("Menjaga umur baterai"), Popup(new[] { Lang.T("Mati"), "80%", "85%", "90%" }, Math.Max(0, Array.IndexOf(unplugAt, settings.GetInt("battery.unplug", 0))), i => { settings.Set("battery.unplug", unplugAt[i]); settings.Save(); })));
                        add(Row(Lang.T("Flashdisk"), Lang.T("Saat dicolok, dengan tombol Buka dan Keluarkan"), Switch("drives", true, null)));
                        cap(Lang.T("Lainnya"));
                        add(Row(Lang.T("Kelas yang berlangsung"), null, Switch("class.island", true, null)));
                        add(Row(Lang.T("Progres unduhan"), null, Switch("downloads", true, null)));
                        add(Row(Lang.T("Mode rapat"), Lang.T("Mikrofon dipakai lebih dari 20 detik: durasi tampil, notifikasi biasa ditahan"), Switch("meeting", true, null)));
                        break;
                    case "combo":
                        cap(Lang.T("Umum"));
                        add(Row(Lang.T("Combo Ketikan"), Lang.T("Hitungan combo, peringkat, dan efek seru saat mengetik cepat"), Switch("keys.combo", false, on => { island.SetFlag("keys.combo", on); island.SyncComboHook(); Relayout(); })));
                        add(Row(Lang.T("Overdrive / Fever Mode"), Lang.T("Efek api, multiplier 2x/4x, dan pendaran tepi saat 100x+"), Switch("keys.combo.fever", true, on => { island.SetFlag("keys.combo.fever", on); island.ReloadComboSettings(); })));
                        add(Row(Lang.T("Finisher tombol Enter"), Lang.T("Gelombang kejut & partikel saat tekan Enter di 25x+"), Switch("keys.combo.enter", true, on => { island.SetFlag("keys.combo.enter", on); island.ReloadComboSettings(); })));
                        cap(Lang.T("Suara ketikan"));
                        add(Row(Lang.T("Efek suara combo"), Lang.T("Suara ketikan dinamis & fanfare rekor baru"), Switch("keys.combo.sound", false, on => { island.SetFlag("keys.combo.sound", on); island.ReloadComboSettings(); })));
                        string[] profileIds = { "arcade", "thocky", "clicky", "piano", "classic" };
                        string[] profileNames = { Lang.T("Arcade Pitch-Up"), Lang.T("Thocky Mechanical"), Lang.T("Clicky Mechanical"), Lang.T("Piano"), Lang.T("Klasik (Naik tier saja)") };
                        int profAt = Math.Max(0, Array.IndexOf(profileIds, settings.Get("keys.combo.sound.profile", "arcade")));
                        add(Row(Lang.T("Profil suara"), Lang.T("Karakter bunyi tiap ketikan dan kenaikan peringkat"), Popup(profileNames, profAt, i => { settings.Set("keys.combo.sound.profile", profileIds[i]); settings.Save(); island.ReloadComboSettings(); })));
                        add(Row(Lang.T("Tes suara"), Lang.T("Putar contoh bunyi combo saat ini"), Button(Lang.T("Putar"), false, delegate { island.PlayComboTestSound(); })));
                        cap(Lang.T("Penalti & Rantai"));
                        string[] penIds = { "shield", "drain", "off" };
                        string[] penNames = { Lang.T("Shield + Drain"), Lang.T("Drain saja"), Lang.T("Mati") };
                        int penAt = Math.Max(0, Array.IndexOf(penIds, settings.Get("keys.combo.penalty", "shield")));
                        add(Row(Lang.T("Mode penalti"), Lang.T("Perilaku tombol Backspace saat combo berjalan; 25 ketikan bersih memberi Perfect Chain"), Popup(penNames, penAt, i => { settings.Set("keys.combo.penalty", penIds[i]); settings.Save(); island.ReloadComboSettings(); })));
                        cap(Lang.T("Level, XP & Target Harian"));
                        add(Row(Lang.T("Level, XP & Achievement"), Lang.T("Catat XP, target harian, dan lencana pencapaian"), Switch("keys.combo.rpg", true, on => { island.SetFlag("keys.combo.rpg", on); island.ReloadComboSettings(); Relayout(); })));
                        int[] charGoals = { 2000, 5000, 10000, 20000 };
                        string[] charGoalLabels = { "2.000", "5.000", "10.000", "20.000" };
                        int charAt = Math.Max(0, Array.IndexOf(charGoals, settings.GetInt("keys.combo.quest.chars", 5000)));
                        add(Row(Lang.T("Target ketikan harian"), null, Popup(charGoalLabels, charAt, i => { settings.Set("keys.combo.quest.chars", charGoals[i]); settings.Save(); island.ReloadComboSettings(); })));
                        int[] comboGoals = { 1, 3, 5, 10 };
                        string[] comboGoalLabels = { "1x", "3x", "5x", "10x" };
                        int comboAt = Math.Max(0, Array.IndexOf(comboGoals, settings.GetInt("keys.combo.quest.combos", 3)));
                        add(Row(Lang.T("Target 100x combo harian"), null, Popup(comboGoalLabels, comboAt, i => { settings.Set("keys.combo.quest.combos", comboGoals[i]); settings.Save(); island.ReloadComboSettings(); })));
                        cap(Lang.T("Statistik & Mini-Game"));
                        string progText = Lang.F("Lv.{0} · {1} ({2} XP)", island.ComboLevel, island.ComboLevelTitle, island.ComboXp);
                        Font progFont = UiFont(12f);
                        int progW = TextRenderer.MeasureText(progText, progFont).Width + (int)(8 * Ui);
                        add(Row(Lang.T("Progres & Rekor"),
                            Lang.F("Rekor {0}x · {1} WPM · Sprint {2} WPM · {3} hr ini", island.ComboAllTimeBest, island.ComboAllTimePeakWpm, island.ComboSprintBestWpm, island.ComboDailyChars),
                            new Label { Text = progText, Font = progFont, ForeColor = KitInk.Sub, BackColor = Color.Transparent, AutoSize = false, TextAlign = ContentAlignment.MiddleRight, Size = new Size(progW, (int)(24 * Ui)) }));
                        string badgeText = island.ComboBadgesCount + "/" + island.ComboBadgesTotal;
                        Font badgeFont = UiFont(12.5f);
                        int badgeW = TextRenderer.MeasureText(badgeText, badgeFont).Width + (int)(8 * Ui);
                        add(Row(Lang.T("Lencana"), island.ComboUnlockedBadgesSummary(),
                            new Label { Text = badgeText, Font = badgeFont, ForeColor = KitInk.Sub, BackColor = Color.Transparent, AutoSize = false, TextAlign = ContentAlignment.MiddleRight, Size = new Size(badgeW, (int)(24 * Ui)) }));
                        add(Row(Lang.T("Typing Sprint 30 Detik"), Lang.T("Uji kecepatan WPM selama 30 detik di island"), Button(Lang.T("Mulai Sprint"), true, delegate { island.StartTypingSprint(); })));
                        KitButton resetComboBtn = null;
                        KitRow resetComboRow = null;
                        string resetComboNote = Lang.T("Hapus semua XP, level, rekor combo, WPM, dan lencana");
                        var comboArmed = new System.Windows.Forms.Timer { Interval = 4000 };
                        int resetIdleW = 0;
                        resetComboBtn = Button(Lang.T("Reset"), false, delegate
                        {
                            if (!comboArmed.Enabled)
                            {
                                comboArmed.Start();
                                int right = resetComboBtn.Right;
                                resetComboBtn.Text = Lang.T("Klik lagi");
                                int needW = TextRenderer.MeasureText(resetComboBtn.Text, resetComboBtn.Font).Width + (int)(32 * Ui);
                                if (needW != resetComboBtn.Width)
                                {
                                    resetComboBtn.Width = needW;
                                    resetComboBtn.Left = right - needW;
                                }
                                resetComboRow.Detail = Lang.T("Klik sekali lagi dalam 4 detik untuk menghapus statistik");
                                resetComboBtn.Invalidate();
                                resetComboRow.Invalidate();
                                return;
                            }
                            comboArmed.Stop();
                            island.ResetComboStats();
                            Relayout();
                        });
                        resetIdleW = resetComboBtn.Width;
                        comboArmed.Tick += delegate
                        {
                            comboArmed.Stop();
                            int right = resetComboBtn.Right;
                            resetComboBtn.Text = Lang.T("Reset");
                            resetComboBtn.Width = resetIdleW;
                            resetComboBtn.Left = right - resetIdleW;
                            resetComboRow.Detail = resetComboNote;
                            resetComboBtn.Invalidate();
                            resetComboRow.Invalidate();
                        };
                        resetComboBtn.Disposed += delegate { comboArmed.Dispose(); };
                        resetComboRow = Row(Lang.T("Reset statistik ketikan"), resetComboNote, resetComboBtn);
                        add(resetComboRow);
                        break;
                    case "files":
                        cap(Lang.T("Musik"));
                        add(Row(Lang.T("Lirik di island"), null, Switch("lyrics", true, null)));
                        add(Row(Lang.T("Cari lirik online"), Lang.T("Judul, artis, dan durasi ke LRCLIB, cadangannya NetEase"), Switch("lyrics.online", true, null)));
                        add(Row(Lang.T("Visualizer"), null, Switch("visualizer", true, null)));
                        cap(Lang.T("Rak"));
                        add(Row(Lang.T("Rak berkas"), Lang.T("Jalur berkas saja, hanya di memori"), Switch("shelf", true, null)));
                        add(Row(Lang.T("Simpan isi rak"), Lang.T("Jalur berkas tetap ada setelah Deskmate dibuka lagi"), Switch("shelf.keep", false, on => { settings.Set("shelf.keep", on); settings.Save(); island.SaveShelf(); })));
                        int[] expireAt = { 0, 24, 72 };
                        add(Row(Lang.T("Kosongkan rak otomatis"), null, Popup(new[] { Lang.T("Tidak"), Lang.T("Setelah 1 hari"), Lang.T("Setelah 3 hari") },
                            Math.Max(0, Array.IndexOf(expireAt, settings.GetInt("shelf.expire", 0))), i => { settings.Set("shelf.expire", expireAt[i]); settings.Save(); })));
                        cap(Lang.T("Berkas dan gambar"));
                        add(Row(Lang.T("Ubah format"), Lang.T("Kotak di samping Rak saat berkas diseret ke island"), Switch("convert", true, null)));
                        add(Row(Lang.T("Riwayat clipboard"), Lang.T("Hanya di memori"), Switch("clipboard", false, null)));
                        add(Row(Lang.T("Preview screenshot"), Lang.T("Gambar yang disalin tampil di island"), Switch("shots", true, null)));
                        cap(Lang.T("Downloads"));
                        add(Row(Lang.T("Rapikan Downloads"), Lang.T("Berkas lepas dipindah ke folder per jenis; bisa diurungkan 1 jam"), Switch("tidy", false, on => { island.SetFlag("tidy", on); Relayout(); })));
                        add(Row(Lang.T("Susunan folder"), Lang.T("Folder lama tidak diubah"), Seg(new[] { Lang.T("Per jenis"), Lang.T("Tahun, lalu jenis") }, settings.Get("tidy.layout", "type") == "year" ? 1 : 0, i => { settings.Set("tidy.layout", i == 1 ? "year" : "type"); settings.Save(); })));
                        int[] nudgeAt = { 0, 5, 15, 30 };
                        add(Row(Lang.T("Tawarkan saat"), Lang.T("Jumlah berkas lepas sebelum Deskmate bertanya"), Popup(new[] { Lang.T("Tidak pernah"), Lang.F("{0} berkas", 5), Lang.F("{0} berkas", 15), Lang.F("{0} berkas", 30) },
                            Math.Max(0, Array.IndexOf(nudgeAt, settings.GetInt("tidy.nudge", 15))), i => { settings.Set("tidy.nudge", nudgeAt[i]); settings.Save(); })));
                        add(Row(Lang.T("Rapikan sekarang"), Lang.T("Berkas yang masih diunduh atau dipakai dilewati"), Button(Lang.T("Rapikan"), false, delegate { island.RunTidy(); })));
                        int undoLeft = island.TidyUndoLeft();
                        if (undoLeft > 0) add(Row(Lang.T("Urungkan rapikan terakhir"), Lang.F("Masih bisa {0} menit lagi", undoLeft), Button(Lang.T("Urungkan"), false, delegate { island.UndoTidy(); Relayout(); })));
                        break;
                    case "agents":
                        island.HooksInstalled();
                        add(Row("Claude Code", island.hookProfiles == 0 ? Lang.T("tidak ditemukan") : island.hooksOn ? Lang.F("terhubung · {0} profil", island.hookProfiles) : Lang.T("belum terhubung"),
                            AgentButton(island.hookProfiles > 0, island.hooksOn, delegate { island.ToggleHooks(); Relayout(); })));
                        foreach (var agentId in AgentSetup.Ids)
                        {
                            string aid = agentId;
                            bool present = AgentSetup.Present(aid), on = false;
                            try { on = present && AgentSetup.Installed(aid); } catch (Exception) { }
                            var agentRow = Row(AgentSetup.Label(aid), !present ? Lang.T("tidak ditemukan") : on ? Lang.T("terhubung") : Lang.T("belum terhubung"),
                                AgentButton(present, on, delegate { island.ToggleAgent(aid); Relayout(); }));
                            agentRow.Glyph = '\uE756';
                            add(agentRow);
                        }
                        // One switch: every agent's quota
                        var quota = new KitSwitch { Checked = island.QuotaOn, Size = new Size((int)(46 * Ui), (int)(26 * Ui)) };
                        quota.CheckedChanged += delegate { if (building) return; island.SetQuota(quota.Checked); Relayout(); };
                        add(Row(Lang.T("Kuota agent"), island.QuotaNote(), quota));
                        cap(Lang.T("Pemberitahuan"));
                        add(Row(Lang.T("Suara"), Lang.T("Saat agen minta izin atau selesai, dan saat --run selesai"), Switch("sound.agent", false, null)));
                        cap(Lang.T("Server hook"));
                        int port = island.hub != null ? island.hub.Port : settings.GetInt("agent.port", 47821);
                        add(Row(Lang.F("Port {0}", port), port == 47821 ? Lang.T("Port bawaan lama; port acak lebih sulit ditebak") : Lang.T("Hanya bisa dijangkau dari laptop ini"),
                            Button(Lang.T("Ganti port acak"), false, delegate { island.MoveToRandomPort(); Relayout(); })));
                        add(Row(Lang.T("Menahan izin"), Lang.T("Lewat batas ini, izin dijawab di terminal"), Slider(30, 300, 10, settings.GetInt("agent.wait", 110), v => v + Lang.T(" dtk"), v => { settings.Set("agent.wait", v); settings.Save(); })));
                        break;
                    case "dikte":
                        DictateRows(add, cap);
                        break;
                    case "ai":
                        AiRows(add, cap);
                        break;
                    case "phone":
                        var phoneSw = Switch("phone", false, on =>
                        {
                            // Saved so it starts with Deskmate
                            settings.Set("phone", on);
                            settings.Save();
                            if (on) island.StartPhone(); else island.StopPhone();
                            BeginInvoke(new Action(Relayout));
                        });
                        if (island.phone != null) phoneSw.Checked = true;
                        add(Row(Lang.T("Hubungkan HP"), Lang.T("Tepi di Android, lewat Wi-Fi yang sama"), phoneSw));
                        if (island.phone != null)
                        {
                            if (island.phone.Error != null) add(Row(island.phone.Error, null, null));
                            string pc = PhoneLink.Address() + ":" + island.phone.Port;
                            string pair = island.phone.Code;
                            add(Row(pair != null ? pair : island.phone.IsPaired ? Lang.F("Tersambung ke {0}", island.phone.Device) : Lang.T("Belum ada HP"),
                                pair != null ? Lang.F("Alamat {0} · berlaku 5 menit", pc) : Lang.F("Alamat {0}", pc),
                                Button(island.phone.IsPaired ? Lang.T("Ganti HP") : Lang.T("Buat kode"), false, delegate { island.phone.Forget(); island.phone.NewCode(); Relayout(); })));
                            if (pair != null)
                            {
                                var link = island.phone;
                                var qr = new KitQr(PhoneLink.PairLink(link.Port, pair)) { Size = new Size((int)(168 * Ui), (int)(168 * Ui)), AccessibleName = Lang.T("Kode QR pasangan") };
                                // Paired, expired, or replaced: rebuild
                                qr.StillValid = () => link.Code == pair;
                                qr.Expired += () => BeginInvoke(new Action(Relayout));
                                var qrRow = Row(Lang.T("Pindai dari HP"), Lang.T("Di HP: Integrasi › Laptop › Pindai QR"), qr);
                                qrRow.Height = (int)(188 * Ui);
                                add(qrRow);
                            }
                        }
                        cap(Lang.T("Dari HP"));
                        // Meaningless until a phone is linked
                        bool linked = island.phone != null;
                        var fromNotifs = Switch("phone.notifs", true, null);
                        var fromOtp = Switch("phone.otp", true, null);
                        fromNotifs.Enabled = fromOtp.Enabled = linked;
                        add(Row(Lang.T("Notifikasi HP"), linked ? Lang.T("Pesan dari HP ikut masuk ke Pesan") : Lang.T("Nyalakan Hubungkan HP dulu"), fromNotifs));
                        add(Row(Lang.T("Kode OTP"), linked ? Lang.T("Di island; hilang dari clipboard 1 menit kemudian") : Lang.T("Nyalakan Hubungkan HP dulu"), fromOtp));
                        break;
                    case "kulia":
                    case "integrasi":
                        Integrations(add, cap, show);
                        break;
                    case "remind":
                        var cities = Cities.Names();
                        string cityRaw = settings.Get("pray.city", "");
                        cap(Lang.T("Lokasi & Cuaca"));
                        add(CityRow());
                        var weatherSw = Switch("weather", false, null);
                        weatherSw.Enabled = island.PrayCity() != null;
                        add(Row(Lang.T("Cuaca dan hujan"), weatherSw.Enabled ? Lang.T("Suhu di Ringkasan dan peringatan hujan, dari Open-Meteo") : Lang.T("Pilih kota dulu"), weatherSw));
                        cap(Lang.T("Ibadah"));
                        add(Row(Lang.T("Sholat"), PrayerMethodDetail(cityRaw.Length > 0), Switch("pray", false, on =>
                        {
                            // Saved packed, so the default carries its province like any other pick
                            if (on && cityRaw.Length == 0) settings.Set("pray.city", Cities.PackOf(Cities.Find(cities[0])));
                            settings.Set("pray", on);
                            settings.Save();
                            island.prayToday = null;
                            island.Kick();
                            BeginInvoke(new Action(Relayout));
                        })));
                        add(Row(Lang.T("Ingatkan sebelum"), null, Slider(0, 30, 5, settings.GetInt("pray.before", 10), v => v == 0 ? Lang.T("tidak") : v + Lang.T(" menit"), v => { settings.Set("pray.before", v); settings.Save(); })));
                        // Adzan rows only once Sholat is on
                        if (settings.GetBool("pray", false))
                        {
                            add(Row(Lang.T("Suara adzan"), null, SoundSetting("sound.pray", Lang.T("Bawaan"))));
                            add(Row(Lang.T("Suara sebelum waktu"), null, SoundSetting("sound.pray.before", Lang.T("Bawaan"))));
                        }
                        IbadahRows(add, cap);
                        cap(Lang.T("Kesehatan"));
                        add(Row(Lang.T("Minum air"), null, Switch("drink", false, null)));
                        add(Row(Lang.T("Selang minum"), null, Slider(30, 120, 15, settings.GetInt("drink.every", 60), v => v + Lang.T(" menit"), v => { settings.Set("drink.every", v); settings.Save(); })));
                        add(Row(Lang.T("Target harian"), null, Slider(4, 12, 1, island.DrinkGoal, v => v + Lang.T(" gelas"), v => { settings.Set("drink.goal", v); settings.Save(); })));
                        add(Row(Lang.T("Suara minum"), null, SoundSetting("sound.drink", Lang.T("Bawaan"))));
                        add(Row(Lang.T("Istirahat mata 20-20-20"), Lang.T("Tiap 20 menit di layar, pandang jauh 20 detik"), Switch("eyes", false, null)));
                        add(Row(Lang.T("Suara istirahat mata"), null, SoundSetting("sound.eyes", Lang.T("Bawaan"))));
                        add(Row(Lang.T("Peregangan"), null, Switch("stretch", false, null)));
                        add(Row(Lang.T("Selang peregangan"), null, Slider(30, 120, 15, settings.GetInt("stretch.every", 60), v => v + Lang.T(" menit"), v => { settings.Set("stretch.every", v); settings.Save(); })));
                        add(Row(Lang.T("Suara peregangan"), null, SoundSetting("sound.stretch", Lang.T("Bawaan"))));
                        // Focus & Timer live in Tugas & Fokus
                        break;
                    case "access":
                    case "keys":
                        cap(Lang.T("Aksesibilitas"));
                        add(Row(Lang.T("Tampil di rekaman layar"), Lang.T("Ikut screenshot dan screen share, tanpa efek kaca"), Switch("capture.show", false, null)));
                        add(Row(Lang.T("Umumkan ke pembaca layar"), null, Switch("a11y.announce", true, null)));
                        string motion = settings.Get("motion.reduce", "auto");
                        add(Row(Lang.T("Kurangi gerak"), Lang.T("Tanpa pantulan dan efek terbang"),
                            Seg(new[] { Lang.T("Otomatis"), Lang.T("Nyala"), Lang.T("Mati") }, motion == "1" ? 1 : motion == "0" ? 2 : 0, i => island.SetChoice("motion.reduce", i == 1 ? "1" : i == 2 ? "0" : "auto"))));
                        string feel = settings.Get("motion.feel", "bawaan");
                        add(Row(Lang.T("Gerak"), Lang.T("Kecepatan dan pantulan island"),
                            Seg(new[] { Lang.T("Tenang"), Lang.T("Bawaan"), Lang.T("Lincah") }, feel == "tenang" ? 0 : feel == "lincah" ? 2 : 1, i => island.SetChoice("motion.feel", i == 0 ? "tenang" : i == 2 ? "lincah" : "bawaan"))));
                        cap(Lang.T("Pintasan keyboard"));
                        // How to change, said once up top
                        cap(Lang.T("Klik pintasan, lalu tekan kombinasi baru. Backspace mengosongkan, Esc batal."));
                        bool prompterCap = false;
                        foreach (var d in Hotkeys)
                        {
                            if (d.Prompter && !prompterCap) { cap(Lang.T("Teleprompter, hanya selama berjalan")); prompterCap = true; }
                            add(HotkeyRow(d));
                        }
                        flush();
                        add(Row(Lang.T("Pintasan bawaan"), null, Button(Lang.T("Kembalikan"), false, delegate { island.ResetHotkeys(); Relayout(); })));
                        break;
                    case "about":
                        add(VersionRow(Button("GitHub", false, delegate { OpenUrl("https://github.com/movlution/deskmate"); })));
                        add(UpdateRow());
                        var notes = WhatsNew.For(AppVersion);
                        if (notes != null) add(Row(Lang.T("Apa yang baru"), Lang.F("Ringkasan Deskmate {0}", AppVersion), Button(Lang.T("Lihat"), false, delegate { island.ShowWhatsNew(this, new List<WhatsNew.Note> { notes }); })));
                        add(Row(Lang.T("Tur Deskmate"), Lang.T("Kenalan lagi dengan fitur utama Deskmate"), Button(Lang.T("Mulai tur"), false, delegate { Close(); island.StartTour(); })));
                        add(Row(Lang.T("Layanan luar"), Lang.T("Lirik, Kulia, GitHub, cuaca, integrasi, dan layanan AI; semua bisa dimatikan"), null));
                        // QRIS dialog lives on the site
                        // Hidden until the form is wired
                        if (Feedback.Ready) add(Row(Lang.T("Kirim saran"), Lang.T("Ide fitur atau masalah, langsung ke pembuat Deskmate"), Button(Lang.T("Tulis…"), false, delegate { using (var f = new FeedbackWindow()) f.ShowDialog(this); })));
                        // Links as listed on the Deskmate site
                        cap(Lang.T("Pembuat"));
                        var maker = Row("R. Revaldo Vabiansyah", Lang.T("Membuat dan merawat Deskmate"), null);
                        maker.Glyph = '';
                        add(maker);
                        add(Row("GitHub", "@odlaver · " + Lang.T("kode Deskmate dan proyek lainnya"), Button(Lang.T("Buka profil"), false, delegate { OpenUrl("https://github.com/odlaver"); })));
                        add(Row("Instagram", "@odlavr", Button(Lang.T("Buka profil"), false, delegate { OpenUrl("https://www.instagram.com/odlavr"); })));
                        add(Row("LinkedIn", "revalaja", Button(Lang.T("Buka profil"), false, delegate { OpenUrl("https://www.linkedin.com/in/revalaja"); })));
                        break;
                }
                flush();
            }

            // Check now; found build installs here
            KitRow UpdateRow()
            {
                KitRow row = null;
                KitButton b = null;
                Action show = delegate
                {
                    if (row.IsDisposed) return;
                    bool found = island.newVersion != null;
                    b.Text = found ? (island.newAsset != null ? Lang.T("Perbarui") : "GitHub") : Lang.T("Cek pembaruan");
                    b.Primary = found;
                    b.Enabled = !island.checking && !island.updating;
                    b.Width = TextRenderer.MeasureText(b.Text, b.Font).Width + (int)(32 * Ui);
                    row.Detail = island.UpdateText();
                    b.AccessibleDescription = row.Detail;
                    if (row.Parent != null) PlaceField(row);
                    b.Invalidate();
                    row.Invalidate();
                };
                b = Button(Lang.T("Cek pembaruan"), false, delegate
                {
                    if (island.newVersion != null) island.InstallUpdate(this);
                    else island.CheckUpdate(true);
                });
                row = Row(Lang.T("Pembaruan"), island.UpdateText(), b);
                show();
                island.UpdateChanged += show;
                row.Disposed += delegate { island.UpdateChanged -= show; };
                return row;
            }

            // Tab row: grip | icon, name, switch
            readonly Dictionary<string, Control> tabFocus = new Dictionary<string, Control>();

            KitRow TabRow(int page)
            {
                int at = Array.IndexOf(island.tabs, page);
                string name = Lang.T(PageNames[page]);
                var sw = new KitSwitch { Checked = at >= 0, Size = new Size((int)(46 * Ui), (int)(26 * Ui)) };
                sw.CheckedChanged += delegate
                {
                    if (building) return;
                    var list = new List<int>(island.tabs);
                    if (sw.Checked && !list.Contains(page)) list.Add(page);
                    else if (!sw.Checked && list.Count > 1) list.Remove(page);
                    island.SetLayout("panel.tabs", island.TabIds(list));
                    Preview();
                    RefillTabs(page + "sw");
                };
                tabFocus[page + "sw"] = sw;
                var r = Row(name, null, sw);
                r.Lead = 38;
                if (at < 0) return r;
                r.Divider = true;
                var grip = new TabGrip { Size = new Size((int)(30 * Ui), (int)(30 * Ui)), AccessibleName = Lang.F("Urutan {0}", name), AccessibleDescription = Lang.T("Seret, atau tekan panah atas dan bawah") };
                grip.Location = new Point((int)(5 * Ui), (r.Height - grip.Height) / 2);
                grip.Dropped = to => MoveTabTo(page, to);
                grip.Nudge = step => MoveTabTo(page, at + step);
                r.Controls.Add(grip);
                tabFocus[page + "grip"] = grip;
                return r;
            }

            // Screen sketch; the slider below follows the pick
            KitRow PlaceRow()
            {
                var picker = new PlacePicker { Selected = island.placeMode, Size = new Size((int)(212 * Ui), (int)(104 * Ui)) };
                var row = Row(Lang.T("Letak"), Lang.T(PlaceNames[island.placeMode]), picker);
                row.Height = (int)(128 * Ui);
                row.Glyph = '';
                picker.AccessibleName = Lang.T("Letak");
                picker.Changed += i =>
                {
                    if (building) return;
                    island.SetPlace(i);
                    row.Detail = Lang.T(PlaceNames[i]);
                    row.Invalidate();
                    RefillTabs("place");
                };
                tabFocus["place"] = picker;
                return row;
            }

            // Card row: grip | icon, name, note, switch
            KitRow CardRow(HomeCard card)
            {
                int at = Array.IndexOf(island.homeCards, card);
                string name = Lang.T(HomeNames[(int)card]), key = "h" + (int)card;
                var sw = new KitSwitch { Checked = at >= 0, Size = new Size((int)(46 * Ui), (int)(26 * Ui)) };
                sw.CheckedChanged += delegate
                {
                    if (building) return;
                    var list = new List<HomeCard>(island.homeCards);
                    if (sw.Checked && !list.Contains(card)) list.Add(card);
                    else if (!sw.Checked) list.Remove(card);
                    island.SetHomeCards(list);
                    Preview(PageHome);
                    RefillTabs(key + "sw");
                };
                tabFocus[key + "sw"] = sw;
                var r = Row(name, Lang.T(HomeNotes[(int)card]), sw);
                r.Glyph = HomeGlyphs[(int)card];
                r.Lead = 38;
                if (at < 0) return r;
                r.Divider = true;
                var grip = new TabGrip { Size = new Size((int)(30 * Ui), (int)(30 * Ui)), AccessibleName = Lang.F("Urutan {0}", name), AccessibleDescription = Lang.T("Seret, atau tekan panah atas dan bawah") };
                grip.Location = new Point((int)(5 * Ui), (r.Height - grip.Height) / 2);
                grip.Dropped = to => MoveCardTo(card, to);
                grip.Nudge = step => MoveCardTo(card, at + step);
                r.Controls.Add(grip);
                tabFocus[key + "grip"] = grip;
                return r;
            }

            void MoveCardTo(HomeCard card, int to)
            {
                var list = new List<HomeCard>(island.homeCards);
                int i = list.IndexOf(card);
                if (i < 0 || to < 0 || to >= list.Count || to == i) return;
                list.RemoveAt(i);
                list.Insert(to, card);
                island.SetHomeCards(list);
                Preview(PageHome);
                RefillTabs("h" + (int)card + "grip");
            }

            void MoveTabTo(int page, int to)
            {
                var list = new List<int>(island.tabs);
                int i = list.IndexOf(page);
                if (i < 0 || to < 0 || to >= list.Count || to == i) return;
                list.RemoveAt(i);
                list.Insert(to, page);
                island.SetLayout("panel.tabs", island.TabIds(list));
                Preview();
                RefillTabs(page + "grip");
            }

            // Rebuild rows; scroll and focus stay
            void RefillTabs(string focusKey)
            {
                var at = content.AutoScrollPosition;
                bool refocus = ContainsFocus;
                BeginInvoke(new Action(delegate
                {
                    Fill();
                    content.AutoScrollPosition = new Point(-at.X, -at.Y);
                    Control c;
                    if (refocus && tabFocus.TryGetValue(focusKey, out c) && c.Visible) c.Focus();
                }));
            }

            // Six-dot handle: drag, or arrow keys
            class TabGrip : KitControl
            {
                public Action<int> Dropped, Nudge;
                KitRow row;
                List<KitRow> peers;
                int grabY, startTop, baseTop, slot, target;
                bool dragging;

                public TabGrip() { Cursor = Cursors.SizeNS; }

                protected override bool IsInputKey(Keys k) { return k == Keys.Up || k == Keys.Down || base.IsInputKey(k); }

                protected override void OnKeyDown(KeyEventArgs e)
                {
                    base.OnKeyDown(e);
                    if ((e.KeyCode != Keys.Up && e.KeyCode != Keys.Down) || Nudge == null) return;
                    e.Handled = true;
                    Nudge(e.KeyCode == Keys.Up ? -1 : 1);
                }

                static bool Shown(KitRow r)
                {
                    foreach (Control c in r.Controls) if (c is TabGrip) return true;
                    return false;
                }

                // Group space: scrolling keeps the row under the pointer
                int GroupY(MouseEventArgs e) { return row.Parent.PointToClient(PointToScreen(e.Location)).Y; }

                protected override void OnMouseDown(MouseEventArgs e)
                {
                    base.OnMouseDown(e);
                    if (e.Button != MouseButtons.Left || Parent == null) return;
                    row = Parent as KitRow;
                    if (row == null || row.Parent == null) return;
                    peers = new List<KitRow>();
                    foreach (Control c in row.Parent.Controls)
                    {
                        var r = c as KitRow;
                        if (r != null && Shown(r)) peers.Add(r);
                    }
                    peers.Sort((a, b) => a.Top.CompareTo(b.Top));
                    slot = target = peers.IndexOf(row);
                    baseTop = peers[0].Top;
                    startTop = row.Top;
                    grabY = GroupY(e);
                    dragging = true;
                    Capture = true;
                    row.BringToFront();
                    Lift(true);
                }

                protected override void OnMouseMove(MouseEventArgs e)
                {
                    base.OnMouseMove(e);
                    if (!dragging) return;
                    int h = row.Height, max = baseTop + (peers.Count - 1) * h;
                    int top = Math.Max(baseTop, Math.Min(max, startTop + GroupY(e) - grabY));
                    row.Top = top;
                    target = Math.Max(0, Math.Min(peers.Count - 1, (top - baseTop + h / 2) / h));
                    // Others close ranks around the gap
                    int k = 0;
                    foreach (var p in peers)
                    {
                        if (p == row) continue;
                        if (k == target) k++;
                        p.Top = baseTop + k * h;
                        k++;
                    }
                    row.Parent.Invalidate();
                }

                protected override void OnMouseUp(MouseEventArgs e)
                {
                    base.OnMouseUp(e);
                    Drop();
                }

                protected override void OnMouseCaptureChanged(EventArgs e)
                {
                    base.OnMouseCaptureChanged(e);
                    if (!Capture) Drop();
                }

                void Drop()
                {
                    if (!dragging) return;
                    dragging = false;
                    Capture = false;
                    Lift(false);
                    row.Top = baseTop + target * row.Height;
                    if (target != slot && Dropped != null) Dropped(target);
                }

                // Held row takes the hover tint
                void Lift(bool on)
                {
                    row.BackColor = on ? KitInk.Hover : Color.Transparent;
                    row.Invalidate(true);
                }

                protected override void OnPaint(PaintEventArgs e)
                {
                    var g = e.Graphics;
                    KitInk.Smooth(g);
                    g.Clear(KitInk.Under(this));
                    float s = Ui, cx = Width / 2f, cy = Height / 2f, d = 3f * s;
                    if (Hovered && !dragging) Fill(g, new RectangleF(0.5f, 0.5f, Width - 1, Height - 1), Height / 2f, KitInk.Line);
                    using (var b = new SolidBrush(dragging || Hovered || Focused ? KitInk.Text : KitInk.Sub))
                        for (int x = -1; x <= 1; x += 2)
                            for (int y = -1; y <= 1; y++)
                                g.FillEllipse(b, cx + x * 3f * s - d / 2, cy + y * 5f * s - d / 2, d, d);
                    Ring(g, new RectangleF(1, 1, Width - 2, Height - 2), Height / 2f);
                }
            }

            Control StartupSwitch()
            {
                var s = new KitSwitch { Size = new Size((int)(46 * Ui), (int)(26 * Ui)) };
                try { s.Checked = AutoStart.Enabled; } catch (Exception) { }
                s.CheckedChanged += delegate { if (building) return; try { AutoStart.Enabled = s.Checked; } catch (Exception ex) { Log.Write("autostart: " + ex.Message); } };
                return s;
            }

            Control AgentButton(bool present, bool on, Action act)
            {
                var b = Button(on ? Lang.T("Lepas") : Lang.T("Hubungkan"), !on && present, act);
                b.Enabled = present;
                return b;
            }

            protected override void Dispose(bool disposing) { if (disposing) preview.Dispose(); base.Dispose(disposing); }
        }

        // Settings sidebar: icon, name; arrows move
        class SideList : KitControl
        {
            public List<string[]> Items = new List<string[]>();
            // One heading per group, above its first row
            public string[] Headers = new string[0];
            public string Selected = "";
            public event Action<string> Picked;
            int hover = -1;

            float RowH { get { return 32 * Ui; } }

            static int GroupOf(string[] item) { return item.Length > 3 ? int.Parse(item[3]) : 0; }

            // Groups sit apart under their heading
            RectangleF Cell(int i)
            {
                float s = Ui;
                int group = GroupOf(Items[i]);
                return new RectangleF(10 * s, 6 * s + i * RowH + (group * GroupGap + HeadsThrough(group) * HeaderDip) * s - scroll, Width - 20 * s, RowH - 2 * s);
            }

            const float GroupGap = 10, HeaderDip = 26;

            string HeaderOf(int group) { return group < Headers.Length ? Headers[group] ?? "" : ""; }

            // Headings above this group, its own included
            int HeadsThrough(int group)
            {
                int n = 0;
                for (int g = 0; g <= group; g++) if (HeaderOf(g).Length > 0) n++;
                return n;
            }

            // Short screens: the list scrolls
            float scroll;

            float MaxScroll()
            {
                if (Items.Count == 0) return 0;
                return Math.Max(0, Cell(Items.Count - 1).Bottom + scroll + 14 * Ui - Height);
            }

            void ScrollTo(float v)
            {
                v = Math.Max(0, Math.Min(MaxScroll(), v));
                if (v == scroll) return;
                scroll = v;
                Invalidate();
            }

            // Arrow keys keep the pick in view
            void Reveal(int i)
            {
                if (i < 0 || i >= Items.Count) return;
                var c = Cell(i);
                if (c.Top < 0) ScrollTo(scroll + c.Top - 6 * Ui);
                else if (c.Bottom > Height) ScrollTo(scroll + c.Bottom - Height + 6 * Ui);
            }

            protected override void OnMouseWheel(MouseEventArgs e)
            {
                base.OnMouseWheel(e);
                ScrollTo(scroll - e.Delta / 120f * RowH * 1.5f);
            }

            protected override void OnResize(EventArgs e) { base.OnResize(e); ScrollTo(scroll); RevealSelected(); }

            public void RevealSelected() { Reveal(IndexOf(Selected)); }

            int IndexOf(string id) { for (int i = 0; i < Items.Count; i++) if (Items[i][0] == id) return i; return -1; }

            void Pick(int i)
            {
                if (i < 0 || i >= Items.Count) return;
                Selected = Items[i][0];
                Reveal(i);
                Invalidate();
                AccessibilityNotifyClients(AccessibleEvents.Selection, i);
                var h = Picked;
                if (h != null) h(Selected);
            }

            protected override void OnMouseMove(MouseEventArgs e)
            {
                base.OnMouseMove(e);
                int h = -1;
                for (int i = 0; i < Items.Count; i++) if (Cell(i).Contains(e.Location)) h = i;
                if (h != hover) { hover = h; Invalidate(); }
            }

            protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); hover = -1; Invalidate(); }

            protected override void OnMouseUp(MouseEventArgs e)
            {
                base.OnMouseUp(e);
                for (int i = 0; i < Items.Count; i++) if (Cell(i).Contains(e.Location)) { Pick(i); return; }
            }

            protected override bool IsInputKey(Keys key) { return key == Keys.Up || key == Keys.Down || base.IsInputKey(key); }

            protected override void OnKeyDown(KeyEventArgs e)
            {
                base.OnKeyDown(e);
                int at = IndexOf(Selected);
                if (e.KeyCode == Keys.Up) Pick(Math.Max(0, at - 1));
                else if (e.KeyCode == Keys.Down) Pick(Math.Min(Items.Count - 1, at + 1));
            }

            protected override void OnPaint(PaintEventArgs e)
            {
                var g = e.Graphics;
                KitInk.Smooth(g);
                g.Clear(KitInk.Side);
                float s = Ui;
                var active = Ink.Mix(KitInk.Hover, KitInk.Accent, 0.10f);
                var activeMark = Contrast(KitInk.Accent, active) >= 3.0 ? KitInk.Accent : KitInk.Text;
                using (var f = new Font(Ink.MenuFamily, 13.5f * s, GraphicsUnit.Pixel))
                using (var head = new Font(Ink.MenuFamily, 11.5f * s, FontStyle.Bold, GraphicsUnit.Pixel))
                using (var icons = new Font(Ink.IconFamily, 14f * s, GraphicsUnit.Pixel))
                    for (int i = 0; i < Items.Count; i++)
                    {
                        var c = Cell(i);
                        int group = GroupOf(Items[i]);
                        // First row of a group: its heading above
                        if ((i == 0 || GroupOf(Items[i - 1]) != group) && HeaderOf(group).Length > 0)
                            KitInk.Draw(g, HeaderOf(group), head, KitInk.Sub, new RectangleF(c.X + 10 * s, c.Y - HeaderDip * s + 4 * s, c.Width - 10 * s, HeaderDip * s - 6 * s), ContentAlignment.MiddleLeft);
                        bool sel = Items[i][0] == Selected;
                        if (sel || i == hover) Fill(g, c, 9 * s, sel ? active : Ink.Mix(KitInk.Side, KitInk.Hover, 0.5f));
                        if (sel)
                        {
                            var bar = new RectangleF(c.X + 3 * s, c.Y + 8 * s, 2 * s, c.Height - 16 * s);
                            Fill(g, bar, s, activeMark);
                        }
                        KitInk.Draw(g, Items[i][2], icons, sel ? KitInk.Text : KitInk.Sub, new RectangleF(c.X + 8 * s, c.Y, 22 * s, c.Height), ContentAlignment.MiddleCenter);
                        KitInk.Draw(g, Items[i][1], f, sel ? KitInk.Text : KitInk.Sub, new RectangleF(c.X + 38 * s, c.Y, c.Width - 44 * s, c.Height), ContentAlignment.MiddleLeft);
                    }
                int at = IndexOf(Selected);
                if (at >= 0) Ring(g, Cell(at), 9 * s);
            }

            protected override AccessibleObject CreateAccessibilityInstance() { return new Access(this); }

            class Access : ControlAccessibleObject
            {
                readonly SideList owner;
                public Access(SideList owner) : base(owner) { this.owner = owner; }
                public override AccessibleRole Role { get { return AccessibleRole.PageTabList; } }
                public override int GetChildCount() { return owner.Items.Count; }
                public override AccessibleObject GetChild(int i) { return new Item(owner, i); }
            }

            class Item : AccessibleObject
            {
                readonly SideList owner;
                readonly int index;
                public Item(SideList owner, int index) { this.owner = owner; this.index = index; }
                public override string Name { get { return owner.Items[index][1]; } }
                // Readers hear the group's heading too
                public override string Description { get { return owner.HeaderOf(GroupOf(owner.Items[index])); } }
                public override AccessibleRole Role { get { return AccessibleRole.PageTab; } }
                public override AccessibleObject Parent { get { return owner.AccessibilityObject; } }
                public override AccessibleStates State { get { return AccessibleStates.Selectable | (owner.Items[index][0] == owner.Selected ? AccessibleStates.Selected : 0); } }
                public override Rectangle Bounds { get { return owner.RectangleToScreen(Rectangle.Round(owner.Cell(index))); } }
                public override void DoDefaultAction() { owner.Pick(index); }
            }
        }

        class ThemeCardPicker : KitControl
        {
            public int SelectedIndex;
            public event Action<int> Picked;
            int hover = -1;

            static readonly string[] Labels = { "Gelap", "Terang", "Otomatis" };

            RectangleF CardRect(int i)
            {
                float s = Ui;
                float gap = 6 * s;
                float w = (Width - gap * 2) / 3f;
                return new RectangleF(i * (w + gap), 0, w, Height);
            }

            protected override void OnMouseMove(MouseEventArgs e)
            {
                base.OnMouseMove(e);
                int h = -1;
                for (int i = 0; i < 3; i++) if (CardRect(i).Contains(e.Location)) h = i;
                if (h != hover) { hover = h; Cursor = h >= 0 ? Cursors.Hand : Cursors.Default; Invalidate(); }
            }

            protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); hover = -1; Cursor = Cursors.Default; Invalidate(); }

            protected override void OnMouseUp(MouseEventArgs e)
            {
                base.OnMouseUp(e);
                if (e.Button != MouseButtons.Left) return;
                for (int i = 0; i < 3; i++)
                {
                    if (CardRect(i).Contains(e.Location))
                    {
                        SelectedIndex = i;
                        Invalidate();
                        var h = Picked;
                        if (h != null) h(i);
                        return;
                    }
                }
            }

            protected override void OnPaint(PaintEventArgs e)
            {
                var g = e.Graphics;
                KitInk.Smooth(g);
                g.Clear(KitInk.Under(this));
                float s = Ui;
                using (var f = new Font(Ink.MenuFamily, 11f * s, GraphicsUnit.Pixel))
                    for (int i = 0; i < 3; i++)
                    {
                        var r = CardRect(i);
                        bool sel = i == SelectedIndex;
                        Color bg = i == 0 ? Color.FromArgb(28, 28, 30) : i == 1 ? Color.FromArgb(242, 242, 245) : Color.FromArgb(28, 28, 30);
                        Fill(g, r, 9 * s, bg);
                        if (i == 2)
                        {
                            var half = new RectangleF(r.X + r.Width / 2f, r.Y, r.Width / 2f, r.Height);
                            using (var b = new SolidBrush(Color.FromArgb(242, 242, 245)))
                            using (var clip = Ink.Round(r, 9 * s))
                            {
                                var old = g.Clip;
                                g.SetClip(clip);
                                g.FillRectangle(b, half);
                                g.Clip = old;
                            }
                        }
                        var pill = new RectangleF(r.X + r.Width * 0.25f, r.Y + 8 * s, r.Width * 0.5f, 5 * s);
                        Fill(g, pill, 2.5f * s, i == 1 ? Color.FromArgb(60, 60, 65) : Color.FromArgb(220, 220, 225));
                        Color txt = i == 1 ? Color.FromArgb(30, 30, 34) : i == 0 ? Color.FromArgb(235, 235, 240) : Color.FromArgb(140, 140, 148);
                        KitInk.Draw(g, Lang.T(Labels[i]), f, txt, new RectangleF(r.X, r.Bottom - 20 * s, r.Width, 18 * s), ContentAlignment.MiddleCenter);
                        if (sel)
                        {
                            using (var pen = new Pen(KitInk.Accent, 2f * s))
                            using (var path = Ink.Round(new RectangleF(r.X + s, r.Y + s, r.Width - 2 * s, r.Height - 2 * s), 8 * s))
                                g.DrawPath(pen, path);
                        }
                        else if (i == hover)
                        {
                            using (var pen = new Pen(KitInk.Faint, 1f * s))
                            using (var path = Ink.Round(new RectangleF(r.X + 0.5f * s, r.Y + 0.5f * s, r.Width - s, r.Height - s), 8.5f * s))
                                g.DrawPath(pen, path);
                        }
                    }
            }
        }
    }

    // Sits inside the settings window as a child control: a child can never take
    // focus away from the text box the way a separate top-level window did
    class CitySuggest : Panel, IMessageFilter
    {
        readonly float ui;
        string[] items = new string[0];
        int hot = -1;
        Action<string> picked;
        Control anchorCtl;
        bool filtering;

        // A click inside the card belongs to a row; anywhere else dismisses the list
        public bool ContainsScreen(Point pt)
        {
            return Visible && RectangleToScreen(ClientRectangle).Contains(pt);
        }

        void Hook()
        {
            if (filtering) return;
            Application.AddMessageFilter(this);
            filtering = true;
        }

        void Unhook()
        {
            if (!filtering) return;
            Application.RemoveMessageFilter(this);
            filtering = false;
        }

        // The box loses focus the moment the pointer goes down on the list, and the old
        // Leave handler hid the card before the click could land on a row. Filtering the
        // press lets a row keep its click and still closes the list on an outside press.
        public bool PreFilterMessage(ref Message m)
        {
            if (!Visible) return false;
            if (m.Msg != 0x0201 && m.Msg != 0x0204 && m.Msg != 0x0207) return false;
            return OutsidePress(ScreenOf(m.LParam));
        }

        // Screen point carried by a mouse message: low word x, high word y, signed
        static Point ScreenOf(IntPtr l)
        {
            int v = l.ToInt32();
            return new Point((short)(v & 0xFFFF), (short)((v >> 16) & 0xFFFF));
        }

        // True when the press landed outside both the list and the box it belongs to
        internal bool OutsidePress(Point screen)
        {
            if (ContainsScreen(screen)) return false;
            var a = anchorCtl;
            if (a != null && !a.IsDisposed && a.RectangleToScreen(a.ClientRectangle).Contains(screen)) return false;
            Hide2();
            return false; // never swallow: the press still lands where it was aimed
        }

        protected override void OnVisibleChanged(EventArgs e)
        {
            base.OnVisibleChanged(e);
            if (Visible) Hook(); else Unhook();
        }

        public CitySuggest(float ui)
        {
            this.ui = ui;
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            BackColor = KitInk.Window;
            // Never a tab stop, never focusable, so typing never leaves the box
            TabStop = false;
            SetStyle(ControlStyles.Selectable, false);
            Visible = false;
        }

        protected override bool IsInputKey(Keys key) { return false; }

        public void Bind(string[] names)
        {
            items = names ?? new string[0];
            hot = -1;
            Height = (int)((Math.Min(items.Length, 8) * 30 + 8) * ui);
            Invalidate();
        }

        // The anchor sits several panels deep, so convert through screen space
        public void ShowUnder(Control anchor, Action<string> onPick)
        {
            picked = onPick;
            anchorCtl = anchor;
            var host = Parent;
            if (host == null) return;
            var at = host.PointToClient(anchor.PointToScreen(Point.Empty));
            // Wide enough for a full "City · Province" label, but never wider than the page
            int room = Math.Max((int)(160 * ui), host.ClientSize.Width - 16);
            Width = Math.Min(Math.Max(anchor.Width, (int)(240 * ui)), room);
            // Flush with one edge of the field it belongs to: left when it fits, else right,
            // so a clamped list still reads as attached to its box instead of floating off it
            int x = at.X;
            if (x + Width > host.ClientSize.Width - 8) x = Math.Max(8, at.X + anchor.Width - Width);
            // Last guard: whatever the anchor did, the list stays on the page
            if (x + Width > host.ClientSize.Width - 8) x = Math.Max(0, host.ClientSize.Width - 8 - Width);
            int y = at.Y + anchor.Height + (int)(4 * ui);
            if (y + Height > host.ClientSize.Height - 8) y = Math.Max(4, at.Y - Height - (int)(4 * ui));
            Location = new Point(x, y);
            Visible = true;
            BringToFront();
            Invalidate();
        }

        public void Hide2() { Visible = false; hot = -1; }

        public void Step(int delta)
        {
            if (items.Length == 0) return;
            if (hot < 0) hot = delta > 0 ? 0 : items.Length - 1;
            else hot = (hot + delta + items.Length) % items.Length;
            Invalidate();
        }

        public string Selected()
        {
            if (items.Length == 0) return null;
            return hot >= 0 && hot < items.Length ? items[hot] : items[0];
        }

        protected override void OnPaintBackground(PaintEventArgs e) { }

        // Hover band for a row: inset from the card so it never touches the rim
        RectangleF RowRect(int i)
        {
            float pad = 4 * ui;
            return new RectangleF(pad, i * 30 * ui + pad, Width - pad * 2, 30 * ui);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            KitInk.Smooth(g);
            // The hairline sits half a pixel inside the bounds: a pen is centred on its
            // path, so stroking the full rectangle would clip half the width and render
            // a smeared grey edge instead of a crisp line
            var card = new RectangleF(0.5f, 0.5f, Width - 1, Height - 1);
            using (var path = Ink.Round(card, 10 * ui))
            {
                using (var b = new SolidBrush(KitInk.Window)) g.FillPath(b, path);
                // Rows are clipped to the card, so a hover band cannot square off the corners
                var old = g.Clip;
                g.SetClip(path);
                using (var f = new Font(Ink.MenuFamily, 12.5f * ui, GraphicsUnit.Pixel))
                {
                    for (int i = 0; i < items.Length; i++)
                    {
                        var r = RowRect(i);
                        if (i == hot)
                            using (var hb = new SolidBrush(KitInk.Hover))
                            using (var hp = Ink.Round(r, 7 * ui))
                                g.FillPath(hb, hp);
                        // Centred by the shared text helper, which also ellipsizes a long label
                        KitInk.Draw(g, items[i], f, KitInk.Text,
                            new RectangleF(r.X + 10 * ui, r.Y, r.Width - 20 * ui, r.Height), ContentAlignment.MiddleLeft);
                    }
                }
                g.Clip = old;
                using (var pen = new Pen(KitInk.Line, 1f)) g.DrawPath(pen, path);
            }
        }

        int RowAt(Point p)
        {
            int i = (int)((p.Y / ui - 4) / 30);
            return i >= 0 && i < items.Length ? i : -1;
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            int at = RowAt(e.Location);
            if (at != hot) { hot = at; Invalidate(); }
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            if (hot != -1) { hot = -1; Invalidate(); }
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            int at = RowAt(e.Location);
            if (at < 0 || picked == null) return;
            string pick = items[at];
            Visible = false;
            picked(pick);
        }
    }
}

