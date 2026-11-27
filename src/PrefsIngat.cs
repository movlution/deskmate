using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Threading;
using System.Windows.Forms;

namespace Deskmate
{
    partial class Island
    {
        partial class PrefsWindow
        {
            // Ingatan page: on and off, what is captured, which AI thinks about it, privacy, MCP
            void IngatRows(Action<string> cap, Action<string> note, Action<KitRow> add)
            {
                cap(Lang.T("Ingatan"));
                KitRow statusRow = null;
                var on = Switch("ingat", false, v =>
                {
                    island.SetFlag("ingat", v);
                    searchIndex.Remove("ingat");
                    if (statusRow != null && !statusRow.IsDisposed)
                    {
                        statusRow.Detail = v ? Lang.T("Memeriksa…") : Lang.T("Mati; tidak ada yang direkam");
                        statusRow.Invalidate();
                        if (v) IngatStatus(statusRow);
                    }
                });
                var onRow = Row(Lang.T("Ingatan aktif"), Lang.T("Mencatat aplikasi, jendela, dan teks di layar ke memori di laptop ini, lalu bisa ditanya"), on);
                onRow.Glyph = (char)0xE81C;
                add(onRow);

                bool active = settings.GetBool("ingat", false);
                statusRow = Row(Lang.T("Status"), active ? Lang.T("Memeriksa…") : Lang.T("Mati; tidak ada yang direkam"),
                    Button(Lang.T("Jeda 1 jam"), false, delegate { IngatPost("/pause", new Dictionary<string, object> { { "minutes", 60 } }); }));
                statusRow.Glyph = (char)0xE9D9;
                add(statusRow);
                if (active && !indexing) IngatStatus(statusRow);

                note(Lang.T("Tanya lewat chat Suruh, tombol Ingat di pet, atau MCP. Ketikan tidak pernah direkam."));

                cap(Lang.T("Yang dicatat"));
                add(Row(Lang.T("Teks di layar"), Lang.T("Dibaca lewat UI Automation saat jendela atau isinya berubah"), Switch("ingat.text", true, null)));
                add(Row(Lang.T("OCR cadangan"), Lang.T("Untuk aplikasi yang tidak memberi teks; dilewati saat baterai rendah dan layar penuh"), Switch("ingat.ocr", true, null)));
                add(Row(Lang.T("Teks yang disalin"), Lang.T("Kecuali yang ditandai rahasia oleh pengelola kata sandi"), Switch("ingat.clip", true, null)));
                add(Row(Lang.T("Percakapan dan orang"), Lang.T("Obrolan di WhatsApp, Telegram, Discord, Slack, Teams, dan email: siapa, apa yang dibahas, dan janji"), Switch("ingat.chat", true, null)));
                add(Row(Lang.T("Isi kolom yang kamu tulis"), Lang.T("Dibaca dari kolomnya, bukan dari keyboard; kolom kata sandi dilewati"), Switch("ingat.typed", true, null)));
                add(Row(Lang.T("Konteks saat menulis pesan"), Lang.T("Pet merangkum obrolan dan janji dengan orang itu saat kamu mulai mengetik"), Switch("ingat.assist", true, null)));
                add(Row(Lang.T("Rekam panggilan otomatis"), Lang.T("Mulai saat mikrofon dipakai panggilan, berhenti saat selesai; beri tahu peserta lain"), Switch("ingat.rapat", false, null)));
                add(Row(Lang.T("Pet memakai Ingatan"), Lang.T("Rekap pagi dan pengingat istirahat setelah 2 jam di satu proyek"), Switch("ingat.pet", true, null)));

                cap(Lang.T("AI untuk Ingatan"));
                var ids = new List<string> { "ollama", "lmstudio", "same", "off" };
                var names = new List<string> { Ai.Label(Ai.Find("ollama")), Ai.Label(Ai.Find("lmstudio")), Lang.T("Sama dengan AI utama"), Lang.T("Mati (pencarian saja)") };
                foreach (var p in Ai.Providers)
                    if (!p.NoKey) { ids.Add(p.Id); names.Add(Ai.Label(p)); }
                string cur = settings.Get("ingat.ai", "ollama");
                var aiPop = Popup(names.ToArray(), Math.Max(0, ids.IndexOf(cur)), i =>
                {
                    string id = ids[i];
                    var prov = id == "same" ? Ai.Text : Ai.Find(id);
                    // Cloud means screen text leaves the laptop: say it once, plainly
                    if (prov != null && !prov.NoKey && id != "off" &&
                        !KitAsk.Confirm(this, Lang.F("Ringkasan dan jawaban Ingatan akan dikirim ke {0}. Teks layar yang sudah disaring (kata sandi, token, email, nomor HP dibuang) ikut terkirim. Lanjutkan?", Ai.Label(prov)),
                            Lang.T("Lanjutkan"), Lang.T("Batal"), "Deskmate"))
                    {
                        RefreshContent();
                        return;
                    }
                    settings.Set("ingat.ai", id);
                    settings.Save();
                    searchIndex.Remove("ingat");
                    RefreshContent();
                });
                var curProv = cur == "same" ? Ai.Text : Ai.Find(cur);
                string aiDetail = cur == "off" ? Lang.T("Tanpa ringkasan; pencarian kata kunci tetap jalan")
                    : curProv == null ? "" : curProv.NoKey ? Lang.T("Lokal: aktivitas tidak keluar dari laptop ini")
                    : Lang.T("Cloud: teks yang sudah disaring dikirim ke layanan ini");
                if (curProv != null && cur != "off" && !Ai.Ready(curProv)) aiDetail = Lang.T("Belum siap; atur di Pengaturan › AI");
                add(Row(Lang.T("Layanan AI"), aiDetail, aiPop));

                string[] embeds = { "nomic-embed-text", "mxbai-embed-large", "bge-m3", "text-embedding-3-small", "off" };
                string[] embedNames = { "nomic-embed-text", "mxbai-embed-large", "bge-m3", "text-embedding-3-small", Lang.T("Mati") };
                string embed = settings.Get("ingat.embed", "nomic-embed-text");
                add(Row(Lang.T("Model makna (embedding)"), Lang.T("Mencari berdasarkan arti, bukan hanya kata yang sama"),
                    Popup(embedNames, Math.Max(0, Array.IndexOf(embeds, embed)), i => { settings.Set("ingat.embed", embeds[i]); settings.Save(); })));

                cap(Lang.T("Privasi"));
                add(ListRow(Lang.T("Aplikasi dikecualikan"), Lang.T("Nama proses, dipisah koma; pengelola kata sandi dan perbankan sudah dikecualikan"), "ingat.block.apps", "spotify, steam"));
                add(ListRow(Lang.T("Situs dikecualikan"), Lang.T("Domain, dipisah koma; jendela InPrivate dan Incognito selalu dilewati"), "ingat.block.sites", "bank.co.id, mail.google.com"));
                add(Row(Lang.T("Simpan detail mentah"), Lang.T("Teks per kejadian; ringkasan episode bertahan lebih lama"),
                    Slider(3, 60, 1, settings.GetInt("ingat.keep.events", 14), v => Lang.F("{0} hari", v), v => { settings.Set("ingat.keep.events", v); settings.SaveDeferred(); })));
                add(Row(Lang.T("Simpan episode"), Lang.T("Ringkasan harian dan fakta jangka panjang tidak ikut terhapus"),
                    Slider(30, 365, 15, settings.GetInt("ingat.keep.days", 180), v => Lang.F("{0} hari", v), v => { settings.Set("ingat.keep.days", v); settings.SaveDeferred(); })));
                add(Row(Lang.T("Lupakan 1 jam terakhir"), null, Button(Lang.T("Lupakan"), false, delegate
                {
                    IngatPost("/forget", new Dictionary<string, object> { { "from", DateTime.UtcNow.AddHours(-1).ToString("o", CultureInfo.InvariantCulture) }, { "to", DateTime.UtcNow.AddMinutes(1).ToString("o", CultureInfo.InvariantCulture) } });
                })));
                add(Row(Lang.T("Hapus semua Ingatan"), Lang.T("Episode, ringkasan, fakta, dan rapat yang sudah diindeks"), Button(Lang.T("Hapus semua"), false, delegate
                {
                    if (!KitAsk.Confirm(this, Lang.T("Hapus seluruh isi Ingatan? Ini tidak bisa dibatalkan. Berkas transkrip rapat tetap ada di folder rapat."),
                        Lang.T("Hapus semua"), Lang.T("Batal"), "Deskmate")) return;
                    IngatPost("/forget", new Dictionary<string, object> { { "all", true } });
                })));

                cap(Lang.T("Asisten AI lain (MCP)"));
                add(Row(Lang.T("Konfigurasi MCP"), Lang.T("Tempel ke Claude, Cursor, atau klien MCP lain: Deskmate.exe --mcp"), Button(Lang.T("Salin"), false, delegate
                {
                    try { Clipboard.SetText(IngatMcp.ConfigJson()); } catch (Exception) { }
                })));
                add(Row(Lang.T("Folder data"), IngatStore.Dir, Button(Lang.T("Buka"), false, delegate
                {
                    try { System.IO.Directory.CreateDirectory(IngatStore.Dir); Process.Start("explorer.exe", "\"" + IngatStore.Dir + "\""); } catch (Exception) { }
                })));
            }

            // Status off the UI thread; the row updates when the service answers
            void IngatStatus(KitRow row)
            {
                ThreadPool.QueueUserWorkItem(delegate
                {
                    var st = IngatClient.Get("/status", 3000);
                    string text;
                    if (st == null) text = Lang.T("Layanan sedang dinyalakan…");
                    else
                    {
                        string state = Json.Str(st, "state");
                        string mode = state == "paused" ? Lang.T("Dijeda") : state == "away" ? Lang.T("Menunggu kamu kembali") : state == "private" ? Lang.T("Melewati jendela pribadi") : Lang.T("Mencatat");
                        double mb = Json.Num(st, "bytes", 0) / 1048576.0;
                        text = mode + " · " + Lang.F("{0} episode", Json.Str(st, "episodes")) + " · " + Lang.F("{0} rapat", Json.Str(st, "meetings")) + " · " +
                               mb.ToString("0.0", CultureInfo.InvariantCulture) + " MB" + " · RAM " + Json.Str(st, "privateMb") + " MB";
                        var live = Json.Obj(st, "live");
                        if (live != null && state == "on")
                        {
                            string at = Json.Str(live, "app"), fo = Fmt.OneLine(Json.Str(live, "focus"), 50);
                            if (fo.Length == 0) fo = Fmt.OneLine(Json.Str(live, "title"), 50);
                            if (at.Length > 0) text = Lang.F("Sekarang: {0}", at + (fo.Length > 0 ? " › " + fo : "")) + " · " + text;
                        }
                    }
                    try { BeginInvoke(new Action(delegate { if (IsDisposed) return; row.Detail = text; row.Invalidate(); })); } catch (Exception) { }
                });
            }

            void IngatPost(string path, Dictionary<string, object> body)
            {
                ThreadPool.QueueUserWorkItem(delegate
                {
                    var r = IngatClient.Call("POST", path, body, 15000);
                    try { BeginInvoke(new Action(delegate { if (!IsDisposed) Relayout(); })); } catch (Exception) { }
                    if (r == null) Log.Write("ingat prefs: layanan tidak menjawab " + path);
                });
            }
        }
    }
}
