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
                { "look", "tema sistem gelap terang otomatis aksen warna wallpaper paket" },
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
                var g = e.Graphics;
                g.Clear(KitInk.Window);
                g.FillRectangle(KitInk.BrushOf(KitInk.Side), 0, 0, (int)(236 * Ui), ClientSize.Height);
                // Search field: a soft well; keyboard focus draws the accent edge
                var r = SearchWell();
                KitInk.Smooth(g);
                KitInk.FillRound(g, r, Ink.RadiusControl * Ui, search.BackColor);
                if (search.Focused) KitInk.StrokeRound(g, r, Ink.RadiusControl * Ui, KitInk.Focus, 1.5f * Ui);
                // Magnifier leads the field
                KitInk.Draw(g, "\uE721", Ink.Icons(13f * Ui), KitInk.Sub, new RectangleF(r.X + 5 * Ui, r.Y, 24 * Ui, r.Height), ContentAlignment.MiddleCenter);
                if (search.Text.Length > 0)
                {
                    var cr = SearchClearRect();
                    if (overClear) KitInk.FillRound(g, cr, 6 * Ui, KitInk.Hover);
                    KitInk.Draw(g, "\uE711", Ink.Icons(10f * Ui), overClear ? KitInk.Text : KitInk.Sub, cr, ContentAlignment.MiddleCenter);
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
                y = Ink.Sp24 * Ui;
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
                var spacer = new Panel { Location = new Point(0, (int)y), Size = new Size(1, (int)(Ink.Sp16 * Ui)), BackColor = KitInk.Window };
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
                var l = new Label { Text = text, Font = Ink.Display(22f * Ui), ForeColor = KitInk.Text, BackColor = KitInk.Window, AutoSize = true };
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
                y += (60 + Ink.Sp20) * Ui;
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

            // Page head
            class PageHeader : KitControl
            {
                public char Glyph;
                public string Title = "", Note = "";

                public PageHeader() { TabStop = false; }

                protected override void OnPaint(PaintEventArgs e)
                {
                    var g = e.Graphics;
                    KitInk.Smooth(g);
                    g.Clear(KitInk.Under(this));
                    float s = Ui;
                    var tile = new RectangleF(0, 6 * s, 48 * s, 48 * s);
                    // Same accent whisper as the row tiles, one size up
                    var badge = Ink.Mix(KitInk.Under(this), KitInk.Accent, Ink.Light ? 0.12f : 0.16f);
                    var glyphInk = Contrast(KitInk.Accent, badge) >= 3.0 ? KitInk.Accent : KitInk.Text;
                    Fill(g, tile, Ink.RadiusCard * s, badge);
                    KitInk.Draw(g, Ink.Str(Glyph), Ink.Icons(22f * s), glyphInk, tile, ContentAlignment.MiddleCenter);
                    float x = 68 * s;
                    KitInk.Draw(g, Title, Ink.Display(24f * s), KitInk.Text, new RectangleF(x - 2 * s, 2 * s, Width - x, 32 * s), ContentAlignment.MiddleLeft);
                    KitInk.Draw(g, Note, Ink.Text(13f * s), KitInk.Sub, new RectangleF(x, 34 * s, Width - x, 22 * s), ContentAlignment.MiddleLeft);
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

            // Section heading: small semibold, closer to its group than to the one above
            void Caption(string text)
            {
                y += Ink.Sp8 * Ui;
                var l = new Label { Text = text, Font = Ink.SmallSemi(12f * Ui), ForeColor = KitInk.Sub, BackColor = KitInk.Window, AutoSize = true };
                // Ink lines up with the row icons
                l.Location = new Point((int)(42 * Ui) - LabelPad(l), (int)y);
                content.Controls.Add(l);
                y += Ink.Sp24 * Ui;
            }

            // Guidance under a heading: body weight, so it never reads as another heading
            void Note(string text)
            {
                var l = new Label { Text = text, Font = Ink.Text(12.5f * Ui), ForeColor = KitInk.Sub, BackColor = KitInk.Window, AutoSize = true };
                l.Location = new Point((int)(42 * Ui) - LabelPad(l), (int)y);
                content.Controls.Add(l);
                y += Ink.Sp24 * Ui;
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
                y += top + Ink.Sp24 * Ui;
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
                var r = new KitRow { Title = title, Detail = detail ?? "", Field = field, Height = (int)((string.IsNullOrEmpty(detail) ? 56 : 64) * Ui), Glyph = GlyphFor(title) };
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
                foreach (var it in items) w = Math.Max(w, TextRenderer.MeasureText(it, Ink.Semi(12.5f * Ui)).Width);
                var c = new KitSegmented { Items = items, Size = new Size((int)((w + 30 * Ui) * items.Length), (int)(ControlH * Ui)) };
                c.Selected = selected;
                c.Changed += i => { if (!building) changed(i); };
                return c;
            }

            KitSlider Slider(int min, int max, int step, int value, Func<int, string> fmt, Action<int> changed)
            {
                var c = new KitSlider { Minimum = min, Maximum = max, Step = step, Value = Math.Max(min, Math.Min(max, value)), Format = fmt, ShowValue = true, Size = new Size((int)(270 * Ui), (int)(ControlH * Ui)) };
                c.Changed += v => { if (!building) { changed(v); content.Invalidate(true); } };
                return c;
            }

            KitPopup Popup(string[] items, int selected, Action<int> changed)
            {
                var c = new KitPopup { Items = items, Font = Ink.Text(12.5f * Ui) };
                c.Selected = selected;
                float w = 0;
                foreach (var it in items) w = Math.Max(w, TextRenderer.MeasureText(it + "  ▾", c.Font).Width);
                c.Size = new Size((int)Math.Min(260 * Ui, w + 30 * Ui), (int)(ControlH * Ui));
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

            // Row controls: the 36px target from DESIGN.md
            const float ControlH = 36;

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
