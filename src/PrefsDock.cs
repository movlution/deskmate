using System;
using System.Collections.Generic;

namespace Deskmate
{
    partial class Island
    {
        void SetDockChoice(string key, string value, bool deferSave = false)
        {
            settings.Set(key, value);
            if (deferSave) settings.SaveDeferred(); else settings.Save();
            ApplyDock();
        }

        partial class PrefsWindow
        {
            // Dock page: on and off, look, behaviour, apps, widgets
            void DockRows(Action<string> cap, Action<KitRow> add)
            {
                bool on = settings.GetBool("dockbar.on", false), replacing = settings.GetBool("dockbar.replace", false);
                var depControls = new List<System.Windows.Forms.Control>();
                KitSlider revealDelay = null;
                KitSwitch onDesk = null, reserve = null;
                KitSegmented hideSeg = null;
                Action syncEnabled = () =>
                {
                    bool dockOn = settings.GetBool("dockbar.on", false);
                    string hideNow = settings.Get("dockbar.hide", "never");
                    foreach (var c in depControls) if (!c.IsDisposed) c.Enabled = dockOn;
                    if (revealDelay != null && !revealDelay.IsDisposed) revealDelay.Enabled = dockOn && hideNow != "never";
                    if (onDesk != null && !onDesk.IsDisposed) onDesk.Enabled = dockOn && hideNow == "auto";
                    if (reserve != null && !reserve.IsDisposed) reserve.Enabled = dockOn && !replacing && hideNow == "never";
                };

                var dock = Row(Lang.T("Dock"), Lang.T("Aplikasi, widget, dan jam di tepi bawah setiap layar"), Switch("dockbar.on", false, v => { island.SetFlag("dockbar.on", v); syncEnabled(); }));
                dock.Glyph = (char)0xE8A9;
                add(dock);
                var replace = Switch("dockbar.replace", false, v => { island.SetFlag("dockbar.replace", v); RefreshContent(); });
                replace.Enabled = on;
                depControls.Add(replace);
                var replaceRow = Row(Lang.T("Ganti taskbar Windows"), Lang.T("Ikon tray tersembunyi bisa dibuka dari setiap Dock"), replace);
                replaceRow.Glyph = (char)0xE75B;
                add(replaceRow);

                cap(Lang.T("Tampilan"));
                string style = settings.Get("dockbar.style", "float");
                var styleSeg = Seg(new[] { Lang.T("Mengambang"), Lang.T("Selebar layar") }, style == "full" ? 1 : 0, i => island.SetDockChoice("dockbar.style", i == 1 ? "full" : "float"));
                styleSeg.Enabled = on;
                depControls.Add(styleSeg);
                add(Row(Lang.T("Bentuk"), null, styleSeg));
                string size = settings.Get("dockbar.size", "m");
                var sizeSeg = Seg(new[] { Lang.T("Kecil"), Lang.T("Sedang"), Lang.T("Besar") }, size == "s" ? 0 : size == "l" ? 2 : 1, i => island.SetDockChoice("dockbar.size", i == 0 ? "s" : i == 2 ? "l" : "m"));
                sizeSeg.Enabled = on;
                depControls.Add(sizeSeg);
                add(Row(Lang.T("Ukuran ikon"), null, sizeSeg));
                int mag = settings.GetBool("dockbar.magnify", true) ? settings.GetInt("dockbar.mag", 150) : 100;
                var magSlider = Slider(100, 200, 10, mag, v => v <= 100 ? Lang.T("Mati") : v + "%", v =>
                {
                    settings.Set("dockbar.magnify", v > 100);
                    island.SetDockChoice("dockbar.mag", v.ToString(), true);
                });
                magSlider.Enabled = on;
                depControls.Add(magSlider);
                var magRow = Row(Lang.T("Perbesar saat disorot"), Lang.T("Ikon membesar seperti gelombang mengikuti kursor"), magSlider);
                magRow.Glyph = (char)0xE8A3;
                add(magRow);

                cap(Lang.T("Perilaku"));
                string hide = settings.Get("dockbar.hide", "never");
                hideSeg = Seg(new[] { Lang.T("Selalu tampil"), Lang.T("Otomatis"), Lang.T("Saat tertutup") }, hide == "auto" ? 1 : hide == "smart" ? 2 : 0, i =>
                {
                    island.SetDockChoice("dockbar.hide", i == 1 ? "auto" : i == 2 ? "smart" : "never");
                    syncEnabled();
                });
                hideSeg.Enabled = on;
                depControls.Add(hideSeg);
                add(Row(Lang.T("Sembunyikan dock"), Lang.T("Muncul lagi saat kursor menyentuh tepi bawah"), hideSeg));
                revealDelay = Slider(0, 2000, 100, Math.Max(0, Math.Min(2000, settings.GetInt("dockbar.revealdelay", 0))),
                    v => v == 0 ? Lang.T("Langsung") : v + " ms",
                    v => { settings.Set("dockbar.revealdelay", v); settings.SaveDeferred(); });
                revealDelay.Enabled = on && hide != "never";
                add(Row(Lang.T("Jeda sebelum muncul"), Lang.T("Berapa lama kursor harus berhenti di tepi bawah; klik tepi tetap langsung membuka"), revealDelay));
                onDesk = Switch("dockbar.desktop", true, null);
                onDesk.Enabled = on && hide == "auto";
                add(Row(Lang.T("Tetap tampil di desktop kosong"), Lang.T("Saat sembunyi otomatis dan tidak ada jendela di depan"), onDesk));
                var capture = Switch("dockbar.capture", false, null);
                capture.Enabled = on;
                depControls.Add(capture);
                var captureRow = Row(Lang.T("Tampil di rekaman layar"), Lang.T("Ikut screenshot dan screen share, tanpa efek kaca"), capture);
                captureRow.Glyph = (char)0xE722;
                add(captureRow);
                reserve = Switch("dockbar.reserve", true, null);
                reserve.Enabled = on && !replacing && hide == "never";
                add(Row(Lang.T("Sisakan ruang untuk dock"), replacing ? Lang.T("Selalu, selama menggantikan taskbar") : Lang.T("Jendela yang dimaksimalkan berhenti di atas dock"), reserve));
                var previews = Switch("dockbar.previews", true, null);
                previews.Enabled = on;
                depControls.Add(previews);
                var previewRow = Row(Lang.T("Pratinjau jendela"), Lang.T("Isi jendela langsung, saat kursor di atas aplikasi yang terbuka"), previews);
                previewRow.Glyph = (char)0xE7C4;
                add(previewRow);
                var cardMotion = Switch(DockWindowMotion.Setting, true, null);
                cardMotion.Enabled = on;
                depControls.Add(cardMotion);
                add(Row(Lang.T("Animasi kartu jendela"), Lang.T("Efek kartu pada bingkai jendela dan ikon Dock"), cardMotion));
                var badges = Switch("dockbar.badges", true, null);
                badges.Enabled = on;
                depControls.Add(badges);
                var badgeRow = Row(Lang.T("Angka notifikasi"), Lang.T("Jumlah notifikasi Windows di ikon aplikasinya"), badges);
                badgeRow.Glyph = (char)0xEA8F;
                add(badgeRow);
                if (replacing)
                {
                    var view = Switch("dockbar.taskview", false, null);
                    view.Enabled = on;
                    depControls.Add(view);
                    add(Row(Lang.T("Tombol semua jendela"), Lang.T("Task View, sama dengan Win+Tab"), view));
                    var winkey = Switch("dockbar.winkey", true, null);
                    winkey.Enabled = on;
                    depControls.Add(winkey);
                    var winkeyRow = Row(Lang.T("Tombol Windows membuka Deskmate Start"), Lang.T("Juga Win+S, Win+A, Win+N, Win+W, Win+X, dan Ctrl+Esc; pintasan Win lain tetap jalan"), winkey);
                    winkeyRow.Glyph = (char)0xE765;
                    add(winkeyRow);
                }

                var work = Switch("dockbar.work", true, null);
                work.Enabled = on;
                depControls.Add(work);
                add(Row(Lang.T("Tombol tugas dan catatan"), Lang.T("Satu tombol untuk mengakses Tugas dan catatan"), work));
                var suruh = Switch("dockbar.suruh", true, null);
                suruh.Enabled = on;
                depControls.Add(suruh);
                var suruhRow = Row(Lang.T("Tombol Suruh Deskmate"), Lang.T("Satu tombol untuk membuka obrolan Suruh Deskmate"), suruh);
                suruhRow.Glyph = (char)0xE8F2;
                add(suruhRow);
                var dockShelf = Switch("dockbar.shelf", true, null);
                dockShelf.Enabled = on;
                depControls.Add(dockShelf);
                var dockShelfRow = Row(Lang.T("Rak di Dock"), Lang.T("Seret berkas, gambar, atau teks ke dock untuk menahannya, lalu seret ke aplikasi lain"), dockShelf);
                dockShelfRow.Glyph = IcoShelf;
                add(dockShelfRow);

                cap(Lang.T("Widget"));
                string[] notes = { "Jam dan tanggal; klik membuka Ringkasan", "Sampul, judul, dan tombol putar", "Pemakaian tertinggi dan agen yang bekerja", "Saat timer berjalan", "Butuh kota di Pengaturan › Cuaca & Pengingat", "Hanya di laptop", "CPU dan RAM, dibaca tiap 3 detik" };
                char[] glyphs = { (char)0xE823, (char)0xE8D6, (char)0xEC4A, (char)0xE916, (char)0xE753, (char)0xE83F, (char)0xE9D9 };
                for (int i = 0; i < DockWidgetIds.Length; i++)
                {
                    // Clock and battery sit in the tray corner while replacing
                    if (replacing && (DockWidgetIds[i] == "clock" || DockWidgetIds[i] == "battery")) continue;
                    var ws = Switch("dockbar.w." + DockWidgetIds[i], DockWidgetIds[i] != "system", null);
                    ws.Enabled = on;
                    depControls.Add(ws);
                    var wr = Row(Lang.T(DockWidgetNames[i]), Lang.T(notes[i]), ws);
                    wr.Glyph = glyphs[i];
                    add(wr);
                }

                cap(Lang.T("Aplikasi tersemat"));
                var pins = island.DockPins();
                var clear = Button(Lang.T("Lepas semua"), false, delegate { island.SetDockPins(new List<string>()); RefreshContent(); });
                clear.Enabled = pins.Count > 0;
                var pinRow = Row(Lang.T("Aplikasi tersemat"), pins.Count == 0 ? Lang.T("Klik kanan aplikasi yang terbuka di dock, lalu Sematkan") : Lang.F("{0} aplikasi; seret ikon untuk mengurutkan, seret ke atas untuk melepas", pins.Count), clear);
                pinRow.Glyph = (char)0xE718;
                add(pinRow);
                var again = Button(Lang.T("Ambil"), false, delegate { island.ImportTaskbarPins(true); RefreshContent(); });
                var againRow = Row(Lang.T("Dari taskbar Windows"), Lang.T("Salin aplikasi yang tersemat di taskbar, dengan urutannya"), again);
                againRow.Glyph = (char)0xE896;
                add(againRow);
            }
        }
    }
}
