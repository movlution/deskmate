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
        partial class PrefsWindow
        {
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
                        int matIdx = mat == "clear" ? 0 : mat == "frost" ? 2 : mat == "solid" ? 3 : 1;
                        Func<int, string> matNote = i => settings.GetBool("capture.show", false) ? Lang.T("Tidak berefek saat tampil di rekaman layar aktif") :
                                                         i == 0 ? Lang.T("Bening, mengikuti warna di belakangnya") :
                                                         i == 2 ? Lang.T("Buram lembut, tenang di mata") :
                                                         i == 3 ? Lang.T("Hitam paling hemat daya") :
                                                         Lang.T("Kaca cair, warna di belakangnya tetap hidup");
                        KitRow matRow = null;
                        var matSeg = Seg(new[] { Lang.T("Bening"), "Liquid", "Frost", Lang.T("Hitam") }, matIdx, i =>
                        {
                            island.SetChoice("material", i == 0 ? "clear" : i == 2 ? "frost" : i == 3 ? "solid" : "liquid");
                            if (matRow == null) return;
                            matRow.Detail = matNote(i);
                            if (matRow.Field != null) matRow.Field.AccessibleDescription = matRow.Detail;
                            matRow.Invalidate();
                        });
                        matRow = Row(Lang.T("Material"), matNote(matIdx), matSeg);
                        add(matRow);

                        add(Row(Lang.T("Sinkronkan wallpaper"), custom ? Lang.T("Warna sendiri sedang dipakai") : Lang.T("Kaca diwarnai dari wallpaper; dock solid di dalam dengan tepi transparan; gambar wallpaper tidak ditampilkan"), Switch("wall.sync", false, null)));

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

        }
    }
}
