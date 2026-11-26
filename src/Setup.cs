using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Windows.Forms;
using Microsoft.Win32;

namespace Deskmate
{
    // First run: language, dock, and which features to switch on, before the tour
    partial class Island
    {
        SetupWindow setupWin;

        // Language the installer ran in, else Windows' own display language
        static string SetupLanguage()
        {
            try
            {
                using (var k = Registry.CurrentUser.OpenSubKey(@"Software\Deskmate"))
                {
                    var v = k != null ? k.GetValue("SetupLang") as string : null;
                    if (v == "en" || v == "id") return v;
                }
            }
            catch (Exception ex) { Log.Write("setup lang: " + ex.Message); }
            return CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "id" ? "id" : "en";
        }

        // Called once from OnLoad; the old tour-only first run now opens setup first
        void SetupOnFirstRun()
        {
            if (!settings.Fresh || settings.GetBool("setup.done", false)) return;
            var wait = new System.Windows.Forms.Timer { Interval = 1800 };
            wait.Tick += delegate { wait.Stop(); wait.Dispose(); StartSetup(); };
            wait.Start();
        }

        public void StartSetup()
        {
            if (setupWin != null && !setupWin.IsDisposed) { setupWin.Activate(); return; }
            setupWin = new SetupWindow(this);
            setupWin.FormClosed += delegate
            {
                bool tourNext = setupWin.Finished && setupWin.TourNext;
                settings.Set("setup.done", true);
                settings.Save();
                setupWin = null;
                if (tourNext) StartTour();
            };
            setupWin.Show();
            Native.SetForegroundWindow(setupWin.Handle);
        }

        // One feature the user may switch on or off here; the rest live in Settings
        class SetupFeature
        {
            public string Key, Title, Detail;
            public char Glyph;
            public bool On, Was;
        }

        class SetupWindow : KitWindow
        {
            readonly Island isle;
            readonly Settings settings;
            readonly Label count = new Label(), title = new Label(), body = new Label();
            readonly KitButton next = new KitButton(), back = new KitButton(), skip = new KitButton();
            readonly Panel area = new Panel();
            readonly List<SetupFeature> island = new List<SetupFeature>(), extras = new List<SetupFeature>();
            const int Pages = 4;
            int at;
            bool dockOn = true, replace = true, winKey = true, fullWidth;
            public bool TourNext = true, Finished;

            public SetupWindow(Island isle)
            {
                this.isle = isle;
                settings = isle.settings;
                FormBorderStyle = FormBorderStyle.FixedSingle;
                MaximizeBox = false;
                MinimizeBox = false;
                StartPosition = FormStartPosition.CenterScreen;
                TopMost = true;
                fullWidth = settings.Get("dockbar.style", "float") == "full";

                Feature(island, "notif", true, "Notifikasi pesan", "WhatsApp, Telegram, Discord, dan lainnya tampil di island", (char)0xE8BD);
                Feature(island, "downloads", true, "Progres unduhan", "Unduhan yang berjalan tampil di island", (char)0xE896);
                Feature(island, "shots", true, "Preview screenshot", "Gambar yang disalin tampil di island", (char)0xE91B);
                Feature(island, "shelf", true, "Rak berkas", "Seret berkas ke island untuk menyimpannya sementara", (char)0xE7B8);
                Feature(island, "clipboard", false, "Riwayat clipboard", "Hanya di memori", (char)0xE77F);
                Feature(extras, "drink", false, "Minum air", "Pengingat minum berkala", (char)0xEB42);
                Feature(extras, "eyes", false, "Istirahat mata 20-20-20", "Tiap 20 menit, lihat jauh selama 20 detik", (char)0xE890);
                Feature(extras, "pet.on", false, "Pet di dock", "Hewan kecil yang berjalan di atas dock, atau di tepi bawah layar tanpa dock", (char)0xEB51);
                Feature(extras, "keys.combo", false, "Combo Ketikan", "Hitungan combo, peringkat, dan efek seru saat mengetik cepat", (char)0xE7FC);
                Feature(extras, "tidy", false, "Rapikan Downloads", "Berkas lepas dipindah ke folder per jenis; bisa diurungkan 1 jam", (char)0xE8B7);
                Feature(extras, "ingat", false, "Ingatan", "Memori aktivitas lokal: cari dan tanya apa yang sudah kamu kerjakan", (char)0xE81C);

                foreach (var l in new[] { count, title, body })
                {
                    l.AutoSize = true;
                    l.UseMnemonic = false;
                    Controls.Add(l);
                }
                area.AutoScroll = true;
                Controls.Add(area);
                next.Primary = true;
                next.Click += delegate { Go(at + 1); };
                back.Click += delegate { Go(at - 1); };
                skip.Click += delegate { Close(); };
                Controls.Add(skip);
                Controls.Add(back);
                Controls.Add(next);
                AcceptButton = next;
            }

            void Feature(List<SetupFeature> into, string key, bool fallback, string titleText, string detail, char glyph)
            {
                into.Add(new SetupFeature { Key = key, Title = titleText, Detail = detail, Glyph = glyph, On = settings.GetBool(key, fallback) });
                into[into.Count - 1].Was = into[into.Count - 1].On;
            }

            protected override void OnHandleCreated(EventArgs e)
            {
                base.OnHandleCreated(e);
                Go(0);
            }

            void Go(int i)
            {
                if (i >= Pages) { Finish(); return; }
                at = Math.Max(0, i);
                Relayout();
                if (IsHandleCreated) next.Focus();
                body.AccessibleName = title.Text + ". " + body.Text;
                AccessibilityNotifyClients(AccessibleEvents.NameChange, -1);
            }

            // Everything chosen lands at once, through the same paths Settings uses
            void Finish()
            {
                Finished = true;
                settings.Set("dockbar.style", fullWidth ? "full" : "float");
                settings.Set("dockbar.winkey", winKey);
                settings.Set("dockbar.replace", dockOn && replace);
                isle.SetFlag("dockbar.on", dockOn);
                foreach (var f in island) Apply(f);
                foreach (var f in extras) Apply(f);
                Close();
            }

            void Apply(SetupFeature f)
            {
                if (f.On == f.Was) return;
                if (f.Key == "pet.on") { isle.SetPetChoice(f.Key, f.On); return; }
                isle.SetFlag(f.Key, f.On);
                if (f.Key == "keys.combo") isle.SyncComboHook();
            }

            int U(float v) { return (int)Math.Round(v * Ui); }

            public override void Relayout()
            {
                if (!IsHandleCreated) return;
                SuspendLayout();
                Text = Lang.T("Selamat datang di Deskmate");
                BackColor = KitInk.Window;
                Native.DarkFrame(Handle, !Ink.Light);
                int pad = U(28), w = U(560), inner = w - pad * 2;

                count.Text = Lang.F("Langkah {0} dari {1}", at + 1, Pages);
                count.Font = UiFont(12f);
                count.ForeColor = KitInk.Sub;
                title.Font = UiFont(22f);
                title.ForeColor = KitInk.Text;
                body.Font = UiFont(13f);
                body.ForeColor = KitInk.Sub;
                foreach (var l in new[] { count, title, body }) { l.BackColor = KitInk.Window; l.MaximumSize = new Size(inner, 0); }

                switch (at)
                {
                    case 0:
                        title.Text = Lang.T("Pilih bahasa");
                        body.Text = Lang.T("Bahasa untuk island, menu, dan Pengaturan. Bisa diganti kapan saja di Pengaturan › Umum.");
                        break;
                    case 1:
                        title.Text = Lang.T("Dock pengganti taskbar");
                        body.Text = Lang.T("Dock Deskmate menampung aplikasi, jendela terbuka, tray, dan jam di tepi bawah layar. Saat menggantikan taskbar, taskbar Windows disembunyikan dan kembali lagi kalau Deskmate ditutup.");
                        break;
                    case 2:
                        title.Text = Lang.T("Pilih fitur");
                        body.Text = Lang.T("Nyalakan yang kamu butuhkan. Semua bisa diubah lagi di Pengaturan.");
                        break;
                    default:
                        title.Text = Lang.T("Siap dipakai");
                        body.Text = Lang.T("Dikte, rapat, sholat, cuaca, agen coding, dan integrasi tugas butuh sedikit persiapan, jadi diatur di Pengaturan saat kamu perlu.");
                        break;
                }
                count.Location = new Point(pad, U(22));
                title.Location = new Point(pad, count.Bottom + U(10));
                body.Location = new Point(pad, title.Bottom + U(8));

                foreach (Control c in area.Controls) c.Dispose();
                area.Controls.Clear();
                area.BackColor = KitInk.Window;
                area.AutoScrollPosition = Point.Empty;
                int listW = inner, y = 0;
                switch (at)
                {
                    case 0: y = LanguagePage(listW); break;
                    case 1: y = DockPage(listW); break;
                    case 2: y = FeaturesPage(listW); break;
                    default: y = DonePage(listW); break;
                }
                var screen = Screen.FromHandle(Handle).WorkingArea;
                int chrome = body.Bottom + U(20) + U(36) + U(48) + (Height - ClientSize.Height);
                int areaH = Math.Min(y, Math.Max(U(160), Math.Min(U(430), screen.Height - chrome - U(40))));
                bool scroll = areaH < y;
                // Room for the scrollbar so rows never sit under it
                if (scroll) { foreach (Control c in area.Controls) c.Width = listW - SystemInformation.VerticalScrollBarWidth - U(4); }
                area.SetBounds(pad, body.Bottom + U(20), listW, areaH);

                next.Text = at == Pages - 1 ? Lang.T("Mulai pakai") : Lang.T("Lanjut");
                back.Text = Lang.T("Kembali");
                back.Visible = at > 0;
                skip.Text = Lang.T("Lewati");
                skip.Visible = at < Pages - 1;
                int btnH = U(36), by = area.Bottom + U(24);
                foreach (var b in new[] { next, back, skip })
                {
                    b.Font = UiFont(12.5f);
                    b.Size = new Size(TextRenderer.MeasureText(b.Text, b.Font).Width + U(32), btnH);
                    b.Top = by;
                }
                next.Left = w - pad - next.Width;
                back.Left = next.Left - U(8) - back.Width;
                skip.Left = pad;
                ClientSize = new Size(w, next.Bottom + U(22));
                // Pages differ in height: stay centred and on screen
                Location = new Point(screen.Left + (screen.Width - Width) / 2, Math.Max(screen.Top, screen.Top + (screen.Height - Height) / 2));
                ResumeLayout();
                Invalidate(true);
            }

            KitRow NewRow(string titleText, string detail, char glyph, Control field, int width)
            {
                var r = new KitRow { Title = titleText, Detail = detail ?? "", Glyph = glyph, Field = field, Width = width, Height = U(string.IsNullOrEmpty(detail) ? 44 : 58) };
                if (field != null)
                {
                    field.AccessibleName = titleText;
                    if (!string.IsNullOrEmpty(detail)) field.AccessibleDescription = detail;
                    field.Left = r.Width - field.Width - U(16);
                    field.Top = (r.Height - field.Height) / 2;
                    field.Anchor = AnchorStyles.Top | AnchorStyles.Right;
                    r.Controls.Add(field);
                }
                return r;
            }

            KitSwitch NewSwitch(bool on, Action<bool> changed)
            {
                var s = new KitSwitch { Checked = on, Size = new Size(U(38), U(22)) };
                s.CheckedChanged += delegate { changed(s.Checked); };
                return s;
            }

            // Rows stacked in one soft card; returns its bottom
            int Group(int y, int width, params KitRow[] rows)
            {
                var g = new KitGroup { Left = 0, Top = y, Width = width, Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right };
                int ry = 0;
                foreach (var r in rows)
                {
                    r.Top = ry;
                    r.Left = 0;
                    r.Width = width;
                    r.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
                    ry += r.Height;
                    g.Controls.Add(r);
                }
                g.Height = ry;
                area.Controls.Add(g);
                return g.Bottom;
            }

            int Caption(int y, string text)
            {
                var l = new Label { AutoSize = true, Text = text, Font = UiFont(12f), ForeColor = KitInk.Sub, BackColor = KitInk.Window, UseMnemonic = false, Location = new Point(U(4), y) };
                area.Controls.Add(l);
                return l.Bottom + U(8);
            }

            int LanguagePage(int width)
            {
                var id = new KitCheck { Text = "Bahasa Indonesia", Checked = !Lang.English, Size = new Size(width, U(48)), Font = UiFont(14f) };
                var en = new KitCheck { Text = "English", Checked = Lang.English, Size = new Size(width, U(48)), Font = UiFont(14f) };
                // Radio pair: picking one applies at once, so the next pages read in it
                Action<bool> pick = english =>
                {
                    // KitCheck flips itself after Click: settle the pair once it has
                    if (Lang.English == english) { BeginInvoke(new Action(() => { id.Checked = !english; en.Checked = english; })); return; }
                    isle.SetLanguage(english);
                    BeginInvoke(new Action(Relayout));
                };
                id.Click += delegate { pick(false); };
                en.Click += delegate { pick(true); };
                id.AccessibleRole = en.AccessibleRole = AccessibleRole.RadioButton;
                en.Top = id.Bottom + U(10);
                area.Controls.Add(id);
                area.Controls.Add(en);
                return en.Bottom + U(4);
            }

            int DockPage(int width)
            {
                KitSwitch replaceSw = null, winSw = null;
                KitSegmented style = null;
                var dockSw = NewSwitch(dockOn, v =>
                {
                    dockOn = v;
                    replaceSw.Enabled = v;
                    style.Enabled = v;
                    winSw.Enabled = v && replace;
                });
                replaceSw = NewSwitch(replace, v => { replace = v; winSw.Enabled = dockOn && v; });
                replaceSw.Enabled = dockOn;
                winSw = NewSwitch(winKey, v => winKey = v);
                winSw.Enabled = dockOn && replace;
                string[] items = { Lang.T("Mengambang"), Lang.T("Selebar layar") };
                float segW = 0;
                foreach (var it in items) segW = Math.Max(segW, TextRenderer.MeasureText(it, Ink.Semi(12.5f * Ui)).Width);
                style = new KitSegmented { Items = items, Size = new Size((int)((segW + 30 * Ui) * items.Length), U(32)) };
                style.Selected = fullWidth ? 1 : 0;
                style.Changed += i => fullWidth = i == 1;
                style.Enabled = dockOn;
                int y = Group(0, width,
                    NewRow(Lang.T("Pakai Dock Deskmate"), Lang.T("Aplikasi, widget, dan jam di tepi bawah setiap layar"), (char)0xE8A9, dockSw, width),
                    NewRow(Lang.T("Ganti taskbar Windows"), Lang.T("Taskbar asli disembunyikan; ikon tray tetap bisa dibuka dari Dock"), (char)0xE75B, replaceSw, width),
                    NewRow(Lang.T("Tombol Windows membuka Deskmate Start"), Lang.T("Pintasan Win lain tetap jalan"), (char)0xE765, winSw, width));
                y += U(12);
                y = Group(y, width, NewRow(Lang.T("Bentuk"), null, (char)0xE90E, style, width));
                return y + U(4);
            }

            int FeaturesPage(int width)
            {
                int y = Caption(0, Lang.T("Island"));
                y = Group(y, width, FeatureRows(island, width)) + U(16);
                y = Caption(y, Lang.T("Pengingat dan keseruan"));
                return Group(y, width, FeatureRows(extras, width)) + U(4);
            }

            KitRow[] FeatureRows(List<SetupFeature> list, int width)
            {
                var rows = new KitRow[list.Count];
                for (int i = 0; i < list.Count; i++)
                {
                    var f = list[i];
                    rows[i] = NewRow(Lang.T(f.Title), Lang.T(f.Detail), f.Glyph, NewSwitch(f.On, v => f.On = v), width);
                }
                return rows;
            }

            int DonePage(int width)
            {
                var on = new List<string>();
                if (dockOn) on.Add(replace ? Lang.T("Dock menggantikan taskbar") : Lang.T("Dock"));
                foreach (var f in island) if (f.On) on.Add(Lang.T(f.Title));
                foreach (var f in extras) if (f.On) on.Add(Lang.T(f.Title));
                int y = Caption(0, Lang.T("Yang akan menyala"));
                // A wrapped list: KitRow details stay on one line
                var list = new Label
                {
                    AutoSize = true,
                    MaximumSize = new Size(width - U(8), 0),
                    Text = on.Count == 0 ? Lang.T("Hanya island") : string.Join(" · ", on.ToArray()),
                    Font = UiFont(13.5f),
                    ForeColor = KitInk.Text,
                    BackColor = KitInk.Window,
                    UseMnemonic = false,
                    Location = new Point(U(4), y),
                };
                area.Controls.Add(list);
                y = list.Bottom + U(18);
                var tour = new KitCheck { Text = Lang.T("Lanjut ke tur singkat setelah ini"), Checked = TourNext, Size = new Size(width, U(40)), Font = UiFont(13f), Top = y };
                tour.CheckedChanged += delegate { TourNext = tour.Checked; };
                area.Controls.Add(tour);
                return tour.Bottom + U(4);
            }
        }
    }
}
