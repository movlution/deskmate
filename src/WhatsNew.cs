using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace Deskmate
{
    // Release highlights, shown once per version
    static class WhatsNew
    {
        public class Note
        {
            public string Version;
            // Title, detail, icon from Settings
            public string[][] Items;
        }

        // Newest first; Indonesian source, English in Lang
        public static readonly Note[] Notes =
        {
            new Note
            {
                Version = "2.20.1",
                Items = new[]
                {
                    new[] { "Ingatan lebih cepat & akurat", "Pembacaan konteks layar (UI Automation & OCR), pencarian memori hibrida, dan database SQLite lokal kini lebih cepat, lebih akurat membaca fokus/URL/error, dan jauh lebih hemat baca-tulis disk.", "" },
                    new[] { "Tampilan Pengaturan & kontrol lebih rapi", "Navigasi Pengaturan diperbarui dengan beranda & submenu yang lebih ringkas, plus penyempurnaan visual kontrol, notifikasi, dan sinkronisasi sumber.", "" },
                },
            },
            new Note
            {
                Version = "2.20.0",
                Items = new[]
                {
                    new[] { "Pet mengingatkan tugas & acara", "Sesekali pet menyebut tugas yang belum selesai, tenggat yang dekat, atau acara yang sebentar lagi mulai. Klik gelembungnya untuk membuka Tugas atau Jadwal.", "" },
                    new[] { "Aplikasi admin di Dock bisa dibuka", "Jendela aplikasi yang berjalan sebagai administrator kini muncul saat ikonnya di Dock diklik, juga saat sedang di-minimize.", "" },
                },
            },
            new Note
            {
                Version = "2.19.1",
                Items = new[]
                {
                    new[] { "Pilihan bahasa di installer", "Installer kini menampilkan Bahasa Indonesia dan English, dan memilih otomatis sesuai bahasa Windows.", "" },
                },
            },
            new Note
            {
                Version = "2.19.0",
                Items = new[]
                {
                    new[] { "Kuota AI langsung dari akun", "Kuota Claude, Codex, dan Antigravity dibaca dari akun yang sudah masuk setiap 5 menit, tanpa menunggu sesi agent berjalan.", "" },
                },
            },
            new Note
            {
                Version = "2.18.0",
                Items = new[]
                {
                    new[] { "Tugas lebih cepat diatur", "Tampilan Hari ini dan Terencana, pencarian Ctrl+F, tenggat langsung dari kolom tulis, dan Urungkan setelah menyelesaikan atau menghapus tugas.", "" },
                    new[] { "Panduan awal", "Pilih bahasa, dock, dan fitur seperti saat pertama dipasang. Buka lagi dari Pengaturan › Tentang.", "" },
                },
            },
            new Note
            {
                Version = "2.17.1",
                Items = new[]
                {
                    new[] { "Bingkai kartu di tiap jendela", "Efek kartu mengikuti bingkai saat jendela dibuka, dipulihkan, diminimalkan, atau ditutup. Ikon Dock ikut bergerak.", "" },
                },
            },
            new Note
            {
                Version = "2.17.0",
                Items = new[]
                {
                    new[] { "Jendela seperti tumpukan kartu", "Buka, pindah, pulihkan, atau tutup jendela lewat Dock dengan animasi kartu. Tombol minimize di jendela juga mengirim kartu ke Dock.", "" },
                    new[] { "Chat room pet lebih mudah dibaca", "Pesan tampil dalam gelembung. Tombol Pesan baru membawa kamu ke bagian terbaru saat sedang membaca riwayat.", "" },
                },
            },
            new Note
            {
                Version = "2.16.0",
                Items = new[]
                {
                    new[] { "Chat bersama teman", "Chat pet kini menjadi chat room dengan riwayat bersama, dan nama pemilik tampil di atas setiap pet.", "" },
                    new[] { "Rusuh lewat pet", "Klik kanan pet teman untuk membakar, meretakkan, melempar cat, atau membekukan layarnya sebentar. Bisa dimatikan di Pengaturan › Dock & Taskbar.", "" },
                    new[] { "Chao, pet ketiga", "Roh fantasi mungil berwarna pastel kini bisa dipilih di samping kucing dan anjing.", "" },
                    new[] { "Klik dock untuk meminimalkan", "Klik aplikasi yang sedang di depan untuk meminimalkannya, seperti taskbar Windows.", "" },
                },
            },
            new Note
            {
                Version = "2.15.0",
                Items = new[]
                {
                    new[] { "Rak di Dock", "Seret berkas, gambar, atau teks ke dock untuk menahannya, lalu seret ke aplikasi lain. Atur di Pengaturan › Dock.", "" },
                    new[] { "Lempar pet", "Lepas pet sambil menyeret untuk melemparnya: ia melayang, memantul, lalu kembali ke tempatnya.", "" },
                    new[] { "AI mengatur semua fitur", "Suruh dan pet kini bisa membaca dan mengubah tugas, catatan, kalender, pengingat, timer, media, rak, integrasi, dan pengaturan.", "" },
                },
            },
            new Note
            {
                Version = "2.14.2",
                Items = new[]
                {
                    new[] { "Ingatan lebih ringan", "Ingatan kini memakai jauh lebih sedikit CPU, memori, dan tulisan disk selagi layar tidak berubah. Apa yang dicatat tetap sama.", "" },
                },
            },
            new Note
            {
                Version = "2.14.1",
                Items = new[]
                {
                    new[] { "Penjaga beban", "Aplikasi di latar yang memakai banyak CPU atau RAM selama beberapa menit muncul di island, dengan pilihan Mode hemat, Tutup, atau Abaikan. Matikan di Pengaturan › Perangkat & HP.", "" },
                },
            },
            new Note
            {
                Version = "2.14.0",
                Items = new[]
                {
                    new[] { "AI tanpa kunci API", "Pakai Codex CLI atau Claude Code yang sudah masuk di laptop ini sebagai layanan AI, lengkap dengan alat Deskmate: pengingat, timer, tugas, dan Ingatan.", "" },
                    new[] { "Pilih model dari daftar", "Tombol Pilih di Pengaturan › AI mengambil daftar model dari layanannya, lengkap dengan pencarian.", "" },
                    new[] { "Ingatan lebih cepat dan tahan lama", "Pencarian menemukan lebih banyak, dan fakta jangka panjang berubah sedikit demi sedikit tanpa tertimpa jawaban AI yang keliru.", "" },
                },
            },
            new Note
            {
                Version = "2.13.1",
                Items = new[]
                {
                    new[] { "Incognito Chrome dilewati", "Ingatan kini mengenali jendela Incognito dan Tamu di Chrome, yang judulnya tidak menyebut mode itu, dan tidak mencatatnya.", "" },
                    new[] { "Lupakan benar-benar lupa", "Rapat yang dilupakan tidak muncul lagi, dan layar yang sedang terbuka ikut dilupakan bila masuk rentangnya.", "" },
                },
            },
            new Note
            {
                Version = "2.13.0",
                Items = new[]
                {
                    new[] { "Geser island dengan mouse", "Seret island ke mana pun di sepanjang tepi layar; posisinya tersimpan saat dilepas. Bisa dimatikan di Pengaturan › Island › Posisi.", "" },
                    new[] { "Ingatan lebih peka layar", "Ingatan membaca layar aktif dalam hitungan ratusan milidetik dan menyusunnya per bagian, jadi jawaban tentang yang sedang Anda lihat lebih tepat, bahkan tanpa AI.", "" },
                },
            },
            new Note
            {
                Version = "2.12.0",
                Items = new[]
                {
                    new[] { "Pet bisa dibawa ke mana saja", "Seret pet ke mana pun di layar; saat dilepas ia jatuh kembali. Pilih Area jalan › Bawah layar agar pet berjalan selebar layar dan melompat naik ke dock.", "" },
                    new[] { "Bar aksi pet", "Klik pet untuk membuka tombol besar: elus, lompat, kejar, tidur, tanya, chat, dan pengaturan.", "" },
                    new[] { "Pet yang bereaksi", "Pet kaget oleh notifikasi, senang saat pekerjaan selesai, bergoyang saat musik, tenang saat fokus, dan tidur saat Anda pergi.", "" },
                    new[] { "Teman pet online", "Sampai empat pet berbagi satu dock lewat internet, lengkap dengan chat, emote, dan pet buatan sendiri.", "" },
                    new[] { "Ingatan", "Deskmate bisa mengingat apa yang Anda kerjakan di laptop ini dan menjawab pertanyaan tentangnya. Mati secara bawaan; nyalakan di Pengaturan › Ingatan.", "" },
                    new[] { "Pemasang per pengguna", "Deskmate kini punya installer tanpa admin yang tercantum di Pengaturan › Aplikasi dan membersihkan dirinya saat dicopot.", "" },
                },
            },
            new Note
            {
                Version = "2.11.1",
                Items = new[]
                {
                    new[] { "Dock selalu di atas", "Dock, pet, dan notch di layar lain kembali ke atas dalam sedetik bila ada aplikasi yang menutupinya.", "" },
                    new[] { "Pengaturan pet tidak melompat", "Mengganti hewan, warna bulu, atau menyalakan pet tidak lagi menggulir halaman Pengaturan ke atas.", "" },
                },
            },
            new Note
            {
                Version = "2.11.0",
                Items = new[]
                {
                    new[] { "Pet di dock", "Kucing atau anjing piksel berjalan di atas dock, ikut naik turun saat dock bersembunyi, dan menoleh atau melompat saat disentuh kursor. Nyalakan di Pengaturan › Dock & Taskbar.", "" },
                    new[] { "Pet dari Aseprite", "Taruh sheet.png dan pet.json hasil Export Sprite Sheet di folder pet, lalu pilih di Hewan. Warna bulu, ukuran, dan kecepatan jalan juga bisa diatur.", "" },
                },
            },
            new Note
            {
                Version = "2.10.0",
                Items = new[]
                {
                    new[] { "Kaca Kalem", "Material kaca baru: warna mengikuti wallpaper, diredam, tanpa kilau, dengan garis tepi tipis yang rata.", "" },
                    new[] { "Jam di kepala panel", "Panel terbuka menampilkan jam dan tanggal di pojok kiri atas, dan tab kini berada dalam satu kapsul yang lebih mudah dibaca.", "" },
                    new[] { "Ikon notifikasi asli", "Notifikasi Phone Link menampilkan logo aplikasi Android-nya, dan notifikasi situs web menampilkan ikon browser.", "" },
                    new[] { "Editor tugas dua kolom", "Editor tugas dan catatan kini dua kolom, sakelar bergerak lebih halus, dan pencarian Pengaturan lebih cepat.", "" },
                    new[] { "Dock dan panel lebih andal", "Dock kini muncul di atas aplikasi maksimal ber-title bar kustom seperti ChatGPT, dan panel Clear tetap terbaca di atas jendela terang.", "" },
                },
            },
            new Note
            {
                Version = "2.9.0",
                Items = new[]
                {
                    new[] { "Notch di semua layar", "Pilih Semua layar di Ukuran dan layar › Layar. Monitor lain menampilkan notch ringkas, dan panel terbuka di layar yang Anda arahkan atau klik.", "" },
                    new[] { "Ikon tersembunyi dari setiap Dock", "Saat Dock menggantikan taskbar, tombol panah di setiap monitor membuka daftar ikon tersembunyi tepat di atas Dock itu.", "" },
                    new[] { "Pengaturan yang ditata ulang", "Pengaturan kini disusun seperti System Settings di macOS, dengan kunci settings.ini yang tetap sama.", "" },
                    new[] { "Lebih responsif", "Deskmate berjalan pada prioritas normal, animasi notch dan panel lebih ringan, dan Start langsung menampilkan aplikasi, ikon, serta Terbaru.", "" },
                    new[] { "Kaca dan tema", "Kaca wall.sync kini mewarnai mengikuti wallpaper, Dock naik lebih cepat dan tenang, dan tema mengikuti Windows.", "" },
                },
            },
            new Note
            {
                Version = "2.8.0",
                Items = new[]
                {
                    new[] { "Gambar di catatan tugas", "Pilih Tambah gambar atau tempel screenshot dengan Ctrl+V. Gambar disimpan sebagai PNG, ikut tersinkron ke Google Drive privat, dan tetap bisa dibuka dari cache saat offline.", "\uE8B9" },
                    new[] { "Emoji di notifikasi", "Judul dan isi notifikasi kini menampilkan emoji dengan benar, tetap rapi saat dipotong, dan tidak lagi merusak teks di sekitarnya.", "\uE8BD" },
                    new[] { "Mulai bersama Windows", "Sakelar autostart kini memakai scheduled task, mengikuti status sebenarnya, dan tetap punya jalur cadangan untuk Windows yang membatasi scheduled task.", "\uE7E8" },
                    new[] { "Keterbacaan dan kaca", "Teks sekunder dan kontrol nonaktif lebih mudah dibaca, kontras chip tenggat diperkuat, dan target cahaya Liquid Glass di mode terang dikalibrasi ulang.", "\uE790" },
                    new[] { "Stabilitas", "Suite E2E Tier 1–4 kini mencakup 385 pemeriksaan, termasuk skenario nyata untuk onboarding, tema, kaca, tugas, notifikasi, dock, dan indikator mini.", "\uE74C" },
                },
            },
            new Note
            {
                Version = "2.7.2",
                Items = new[]
                {
                    new[] { "Tugas dan catatan", "Editor catatan baru dengan Markdown, tabel, checklist, blok kode, dan menu blok.", "\uE73A" },
                    new[] { "Google Workspace", "Tugas tersinkron dua arah dengan Google Tasks, dan catatan Markdown tersimpan di folder Deskmate Catatan pada Google Drive.", "\uE8F1" },
                    new[] { "Dock", "Pindai dan sambungkan Wi-Fi serta Bluetooth langsung dari dock, dengan interaksi dan pratinjau yang lebih rapi.", "\uE90E" },
                    new[] { "Aturan notifikasi aplikasi", "Atur tiap aplikasi: ikuti pengaturan umum, tampilkan di Pesan saja, sembunyikan, atau jeda banner 1 atau 8 jam.", "\uE8BD" },
                    new[] { "Tampilan kaca", "Resep Liquid Glass kini seragam di island, dock, dan panel, dan sisa klip dari tampilan sebelumnya tidak lagi tertinggal.", "\uE790" },
                    new[] { "Deskmate", "Ikon, nama, dan aset visual baru; aplikasi Windows ini kini sepenuhnya memakai identitas Deskmate.", "\uE737" },
                    new[] { "Tur", "Dua langkah baru untuk dock dan ruang kerja, tombol lebih besar, dan kontrol yang lebih ramah pembaca layar.", "\uE7C1" },
                },
            },
            new Note
            {
                Version = "2.7.0",
                Items = new[]
                {
                    new[] { "Combo Ketikan", "Combo, suara mekanikal/arcade, Fever Mode, penalti, XP, dan Sprint 30 detik", "\uE7FC" },
                    new[] { "3 Prioritas Hari Ini", "Checklist 3 target utama hari ini di Ringkasan", "\uE73A" },
                    new[] { "Fokus & Progres", "Menit fokus, streak harian, dan grafik 7 hari", "\uE916" },
                    new[] { "Google Calendar", "Masuk lewat browser untuk sinkron & kelola acara", "\uE787" },
                    new[] { "Hari libur nasional (Indonesia)", "Menampilkan tanggal merah & cuti bersama Indonesia di kalender", "\uE787" },
                    new[] { "Dock di semua layar", "Setiap monitor punya dock sendiri, lengkap dengan pratinjau jendela dan widget.", "\uE90E" },
                    new[] { "Edit gambar", "Coret, potong, putar, balik, dan samarkan gambar sebelum dibagikan.", "\uE8B9" },
                },
            },
            new Note
            {
                Version = "2.6.1",
                Items = new[]
                {
                    new[] { "Island di mana saja", "Atas tengah, pojok atas, atau sisi kiri dan kanan layar dengan island berdiri tegak. Saat diam, island bisa menampilkan jam, tanggal, baterai, atau suhu di sampingnya.", "\uE759" },
                    new[] { "Dock", "Bar kaca di tepi bawah berisi aplikasi, pratinjau jendela, dan widget, yang bisa menggantikan taskbar Windows sepenuhnya. Nyalakan di Pengaturan › Dock.", "" },
                    new[] { "Ringkasan pilihanmu", "Pilih dan urutkan kartunya, termasuk cuaca, baterai, clipboard, dan sholat. Panel yang lebar memakai tiga kolom.", "\uE80F" },
                    new[] { "Ngobrol dengan Deskmate", "Suruh Deskmate punya jendela obrolan yang ingat percakapannya, dan bisa memakai Groq, OpenAI, Claude, Gemini, OpenRouter, DeepSeek, Mistral, AI lokal di laptop sendiri, atau server sendiri.", "\uE8F2" },
                    new[] { "Perbaiki dan terjemahkan teks", "Blok teks di aplikasi apa pun, tekan pintasannya, dan teks itu diganti versi yang sudah diperbaiki atau diterjemahkan.", "\uE8AC" },
                    new[] { "Ubah format lebih lengkap", "PDF ke Word dan teks, OCR untuk foto dan hasil scan, kompres PDF, gambar, video, dan audio, gabung PDF dan gambar jadi satu, serta slide ke gambar.", "\uE8AB" },
                    new[] { "Notifikasi lebih rapi", "Deskmate bisa jadi penerima utama banner chat, panggilan, dan screenshot. Pesan dari HP dan laptop tampil sekali, dan isinya disamarkan saat berbagi layar.", "\uE8BD" },
                    new[] { "Screenshot dan Downloads", "Screenshot bisa disamarkan, dipotong, diputar, dan dibalik. Berkas lepas di Downloads bisa dirapikan per jenis dan diurungkan selama satu jam.", "\uE7B3" },
                    new[] { "Agent lebih hati-hati", "Perintah berisiko selalu ditanya ulang, island merangkum hasil kerja agent, klik notifikasinya membuka sesinya di tab Agen, dan kuota semua agent kini satu sakelar.", "\uE756" },
                    new[] { "Pengaturan baru", "Dikelompokkan per kategori: pilih satu untuk melihat daftar halamannya. Aksesibilitas, Pintasan, dan Tentang langsung ada di samping.", "\uE713" },
                },
            },
            new Note
            {
                Version = "2.6.0.2",
                Items = new[]
                {
                    new[] { "Dikte", "Tahan Ctrl kanan di aplikasi apa pun, bicara, lalu lepas: teksnya ditempel di tempat kursor. Ada kamus istilah dan snippet, lewat layanan AI pilihanmu atau offline.", "\uE720" },
                    new[] { "Suruh Deskmate", "Ketik atau ucapkan \"ingetin minum 30 menit lagi\", \"timer 5:30\", \"15% dari 240.000\", atau \"5 km ke mil\". Pengingat, timer, catatan, hitungan, dan konversi satuan jalan tanpa internet.", "\uE765" },
                    new[] { "Transkrip rapat", "Rekam panggilan tanpa bot dengan Ctrl+Alt+T. Mic jadi Kamu, suara laptop jadi Lawan bicara, lengkap dengan ringkasan dan to-do.", "\uE7C8" },
                    new[] { "Tab Bicara", "Dikte, rekam rapat, Suruh Deskmate, pengingat yang menunggu, dan riwayatnya di satu tab.", "\uE8A9" },
                    new[] { "Ringkasan setelah fokus", "Selama sesi fokus, pesan biasa ditahan. Begitu selesai, island merangkum siapa saja yang mengirim pesan.", "\uE7E7" },
                    new[] { "Garis cahaya", "Progres timer, unduhan, dan cas baterai menyala di tepi island. Saat tugas hari ini habis, island ikut merayakan.", "\uE781" },
                    new[] { "Salin teks dari layar", "Pilih area mana pun, teksnya langsung tersalin. Ada di menu klik kanan, dan pintasannya bisa dipasang di Pengaturan.", "\uE8C8" },
                    new[] { "Samarkan", "Kartu screenshot punya tombol Samarkan: seret di atas data pribadi sebelum gambarnya dibagikan.", "\uE7B3" },
                    new[] { "Kirim saran", "Ide fitur atau laporan masalah bisa dikirim langsung dari Pengaturan, bagian Tentang.", "\uE939" },
                    new[] { "Tur dan Apa yang baru", "Pengguna baru dipandu sembilan langkah di bawah island, dan setiap pembaruan merangkum isinya di sini.", "\uE7C1" },
                    new[] { "Pengaturan lebih lega", "Jendelanya terbuka lebih besar, daftar di samping bisa digulir, dan ada opsi menyisakan ruang di tepi atas untuk jendela.", "\uE713" },
                    new[] { "Perbaikan kecil untuk agent", "Antigravity tidak lagi terblokir saat terhubung ke Deskmate, setiap sesi agent kini bertanda Claude, Codex, atau Antigravity, dan sesi Codex atau Antigravity yang sudah diam 30 menit hilang sendiri dari daftar.", "\uE756" },
                    new[] { "Kuota semua agent", "Tab Agen kini menampilkan kuota Claude, Codex, Antigravity, dan Copilot dalam satu daftar, dengan nama AI di depan tiap baris.", "\uEC4A" },
                    new[] { "Copilot CLI", "Copilot CLI bisa dihubungkan dari Pengaturan › Agen: sesinya tampil di island, bertanda Copilot, dan ditutup rapi saat selesai.", "\uE756" },
                },
            },
            new Note
            {
                Version = "2.5.2",
                Items = new[]
                {
                    new[] { "Ibadah sendiri", "Tambahkan doa, kebaktian, meditasi, atau waktu ibadah lain dengan jam, hari, dan suaranya sendiri, di samping jadwal sholat.", "" },
                    new[] { "Suara pengingat", "Minum, Istirahat mata, dan Peregangan kini berbunyi, dan tiap pengingat bisa diberi suara sendiri, misalnya berkas adzan untuk waktu sholat.", "" },
                    new[] { "Pengingat yang benar-benar terbaca", "Selama pengingat tampil, notifikasi lain menunggu dan musik dikecilkan sebentar.", "" },
                    new[] { "Mata saat diam", "Island yang diam menampilkan mata, jam tetap bisa dipilih.", "" },
                    new[] { "Mini lebih mudah dibuka", "Cukup diamkan kursor di dekat tepi atas, tidak harus tepat di piksel teratas.", "" },
                },
            },
        };

        static Version Parse(string v)
        {
            Version r;
            return Version.TryParse(v ?? "", out r) ? r : new Version(0, 0);
        }

        // Unseen notes up to now, newest first
        public static List<Note> Since(string seen, string current)
        {
            var list = new List<Note>();
            Version cur = Parse(current), last = Parse(seen);
            foreach (var n in Notes)
            {
                var v = Parse(n.Version);
                if (v <= cur && (string.IsNullOrEmpty(seen) ? v == cur : v > last)) list.Add(n);
            }
            return list;
        }

        public static Note For(string version)
        {
            foreach (var n in Notes) if (n.Version == version) return n;
            return null;
        }
    }

    partial class Island
    {
        // First Settings open after an update
        void OfferWhatsNew(Form owner)
        {
            string seen = settings.Get("whatsnew.seen", "");
            if (seen == AppVersion) return;
            settings.Set("whatsnew.seen", AppVersion);
            settings.Save();
            var notes = WhatsNew.Since(seen, AppVersion);
            if (notes.Count == 0) return;
            ShowWhatsNew(owner, notes);
        }

        void ShowWhatsNew(Form owner, List<WhatsNew.Note> notes)
        {
            using (var w = new WhatsNewWindow(notes)) w.ShowDialog(owner);
        }
    }

    class WhatsNewWindow : KitWindow
    {
        readonly List<WhatsNew.Note> notes;
        readonly Panel scroller = new Panel();
        readonly NoteList list = new NoteList();
        readonly KitButton done = new KitButton(), full = new KitButton();

        public WhatsNewWindow(List<WhatsNew.Note> notes)
        {
            this.notes = notes;
            list.Notes = notes;
            Text = Lang.T("Apa yang baru");
            StartPosition = FormStartPosition.CenterParent;
            ShowInTaskbar = false;
            MinimizeBox = MaximizeBox = false;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            scroller.AutoScroll = true;
            scroller.Controls.Add(list);
            done.Text = Lang.T("Oke");
            done.Primary = true;
            done.Click += delegate { Close(); };
            full.Text = Lang.T("Catatan lengkap");
            full.AccessibleDescription = "GitHub";
            full.Click += delegate { Island.OpenUrl("https://github.com/movlution/deskmate/releases/tag/v" + notes[0].Version); };
            Controls.Add(scroller);
            Controls.Add(done);
            Controls.Add(full);
            AcceptButton = done;
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            Relayout();
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            done.Focus();
        }

        // Heading in the Settings voice
        protected override void OnPaintBackground(PaintEventArgs e)
        {
            e.Graphics.Clear(KitInk.Window);
            float s = Ui;
            using (var f = UiFont(24f))
                TextRenderer.DrawText(e.Graphics, Lang.F("Baru di Deskmate {0}", notes[0].Version), f, new Point((int)(26 * s), (int)(22 * s)), KitInk.Text, TextFormatFlags.NoPrefix);
        }

        public override void Relayout()
        {
            float s = Ui;
            Func<float, int> u = v => (int)Math.Round(v * s);
            BackColor = KitInk.Window;
            Native.DarkFrame(Handle, !Ink.Light);
            // Settings page metrics: 28 margin, 520 group
            int m = u(28), w = u(520), top = u(78), btnH = u(36);
            list.Width = w;
            list.Measure(s);
            var area = Screen.FromHandle(Handle).WorkingArea;
            int room = Math.Max(u(200), area.Height - top - btnH - u(200));
            bool scrolls = list.Height > room;
            scroller.BackColor = KitInk.Window;
            Native.ThemeScroll(scroller);
            scroller.SetBounds(m, top, w + (scrolls ? SystemInformation.VerticalScrollBarWidth : 0), Math.Min(list.Height, room));
            list.Location = Point.Empty;
            foreach (var b in new[] { done, full })
            {
                b.Font = UiFont(12.5f);
                b.Size = new Size(TextRenderer.MeasureText(b.Text, b.Font).Width + u(32), btnH);
            }
            int by = scroller.Bottom + u(18);
            done.Location = new Point(scroller.Left + w - done.Width, by);
            full.Location = new Point(done.Left - u(8) - full.Width, by);
            ClientSize = new Size(scroller.Right + m, by + btnH + u(22));
            Invalidate(true);
        }

        // Rows drawn like Settings groups
        class NoteList : Control
        {
            public List<WhatsNew.Note> Notes;
            float s = 1;
            readonly List<RectangleF> groups = new List<RectangleF>();
            readonly List<KeyValuePair<float, string[]>> rows = new List<KeyValuePair<float, string[]>>();
            readonly List<KeyValuePair<float, string>> captions = new List<KeyValuePair<float, string>>();

            public NoteList()
            {
                SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
                TabStop = false;
                AccessibleRole = AccessibleRole.List;
            }

            const TextFormatFlags Wrap = TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding;

            float TextX { get { return 56 * s; } }
            int TextW { get { return (int)(Width - TextX - 18 * s); } }

            public void Measure(float scale)
            {
                s = scale;
                groups.Clear(); rows.Clear(); captions.Clear();
                var spoken = new System.Text.StringBuilder();
                float y = 0;
                using (var title = new Font(Ink.MenuFamily, 13.5f * s, GraphicsUnit.Pixel))
                using (var detail = new Font(Ink.MenuFamily, 12f * s, GraphicsUnit.Pixel))
                    for (int k = 0; k < Notes.Count; k++)
                    {
                        // Older versions: a caption, like Settings
                        if (k > 0)
                        {
                            y += 22 * s;
                            captions.Add(new KeyValuePair<float, string>(y, Lang.F("Deskmate {0}", Notes[k].Version)));
                            y += 24 * s;
                        }
                        float top = y;
                        foreach (var it in Notes[k].Items)
                        {
                            string t = Lang.T(it[0]), d = Lang.T(it[1]);
                            float th = TextRenderer.MeasureText(t, title, new Size(TextW, int.MaxValue), Wrap).Height;
                            float dh = TextRenderer.MeasureText(d, detail, new Size(TextW, int.MaxValue), Wrap).Height;
                            rows.Add(new KeyValuePair<float, string[]>(y, new[] { t, d, it.Length > 2 ? it[2] : "", (th).ToString(System.Globalization.CultureInfo.InvariantCulture) }));
                            y += 13 * s + th + 3 * s + dh + 14 * s;
                            spoken.Append(t).Append(". ").Append(d).Append(' ');
                        }
                        groups.Add(new RectangleF(0, top, Width - 1, y - top));
                    }
                Height = (int)Math.Ceiling(y) + 2;
                AccessibleName = spoken.ToString();
            }

            protected override void OnPaint(PaintEventArgs e)
            {
                var g = e.Graphics;
                g.Clear(KitInk.Window);
                KitInk.Smooth(g);
                foreach (var r in groups)
                    using (var path = Ink.Round(r, 12 * s))
                    using (var b = new SolidBrush(KitInk.Group)) g.FillPath(b, path);
                using (var cap = new Font(Ink.MenuFamily, 12f * s, GraphicsUnit.Pixel))
                    foreach (var c in captions) TextRenderer.DrawText(g, c.Value, cap, new Point((int)(14 * s), (int)c.Key), KitInk.Sub, TextFormatFlags.NoPrefix);
                using (var title = new Font(Ink.MenuFamily, 13.5f * s, GraphicsUnit.Pixel))
                using (var detail = new Font(Ink.MenuFamily, 12f * s, GraphicsUnit.Pixel))
                using (var icons = new Font(Ink.IconFamily, 15f * s, GraphicsUnit.Pixel))
                using (var line = new Pen(KitInk.Line, 1f))
                    for (int i = 0; i < rows.Count; i++)
                    {
                        float y = rows[i].Key;
                        var row = rows[i].Value;
                        float th = float.Parse(row[3], System.Globalization.CultureInfo.InvariantCulture);
                        // Divider unless a group starts here
                        bool first = groups.Exists(gr => Math.Abs(gr.Y - y) < 0.5f);
                        if (!first) g.DrawLine(line, TextX, y, Width - 16 * s, y);
                        if (row[2].Length > 0)
                        {
                            var tile = new RectangleF(14 * s, y + 13 * s, 30 * s, 30 * s);
                            using (var path = Ink.Round(tile, 8 * s))
                            using (var b = new SolidBrush(KitInk.Hover)) g.FillPath(b, path);
                            KitInk.Draw(g, row[2], icons, KitInk.Text, tile, ContentAlignment.MiddleCenter);
                        }
                        TextRenderer.DrawText(g, row[0], title, new Rectangle((int)TextX, (int)(y + 13 * s), TextW, (int)th + 2), KitInk.Text, Wrap);
                        TextRenderer.DrawText(g, row[1], detail, new Rectangle((int)TextX, (int)(y + 16 * s + th), TextW, Height), KitInk.Sub, Wrap);
                    }
            }
        }
    }
}
