# Pengaturan

[← Kembali ke README](../README.md)

Klik ikon gear di pojok kanan atas panel, atau klik kanan ikon Deskmate lalu **Pengaturan**. Jendelanya punya kolom cari dan bagian di samping; setiap perubahan langsung berlaku tanpa perlu menyimpan.

Susunannya mengikuti System Settings di macOS. Di atas daftar samping ada kartu **Deskmate** (versi, pembaruan, tur, dan pembuat). Di bawahnya ada 13 halaman dengan ikon berwarna, dikelompokkan dengan jarak tanpa judul. Pengaturan terbuka di **Umum**. Setiap halaman diawali kartu judul berisi ikon dan ringkasannya; tombol panah atas dan bawah memindah pilihan di daftar samping.

| Kelompok | Halaman | Isi |
|---|---|---|
| 1 | Umum · Tampilan · Island · Dock & Taskbar | Bahasa, jam, data; tema dan material; letak, perilaku, saat diam, serta **Tab dan kartu** panel; dock dan pengganti taskbar |
| 2 | Notifikasi · Tugas & Kalender · Cuaca & Pengingat | Aturan notifikasi dan Senyap; tugas, fokus, catatan, serta **Integrasi dan kalender**; cuaca, sholat, minum, mata, peregangan |
| 3 | AI & Dikte · Agen Coding · Musik & Berkas · Perangkat & HP | Layanan AI serta **Dikte dan rapat**; agen coding; musik, rak, clipboard, screenshot, Downloads; HUD sistem serta **HP Android** |
| 4 | Gamifikasi · Pintasan & Aksesibilitas | **Ketikan** (combo) serta **Pet** di dock dan teman pet; pintasan keyboard dan aksesibilitas |

Halaman yang digabung menampilkan bagian keduanya di bawah judul bagian (huruf tebal di tabel). Tautan lama ke bagian itu, misalnya dari tab panel, langsung menggulir ke judul bagiannya. Halaman **Beranda** sudah dihapus karena semua sakelarnya juga ada di halaman masing-masing.

### Umum

- **Dasar**: bahasa, format jam (24 jam, atau 12 jam dengan am/pm; jam besar di island tanpa am/pm), mulai bersama Windows, cek versi baru, folder data, ekspor dan impor pengaturan (token agent, port, pasangan HP, dan sesi Kulia tidak pernah ikut; impor hanya menerima kunci yang dikenal Deskmate), serta setel ulang pengaturan dengan integrasi, agen, dan HP tetap tersambung.
- **AI**: layanan teks untuk ringkasan rapat, Suruh Deskmate, dan perbaiki atau terjemahkan teks; key, tombol Tes, dan model tiap layanan; alamat server kustom; mesin suara (Groq, OpenAI, kustom, atau offline) beserta unduhan model offline.
- **HP**: hubungkan HP lewat kode pasangan, ganti HP, serta pilih apakah notifikasi pesan dan kode OTP dari HP ditampilkan.
- **Integrasi**: Kulia, Notion, Todoist, dan kalender iCal untuk tab Tugas dan Jadwal.

### Tampilan

- **Tema dan warna**: tema (Sistem, Terang, atau Gelap; Sistem mengikuti mode aplikasi Windows secara langsung), material (Bening, Liquid, Frost, Solid), aksen (termasuk dari sampul album: warna paling mencolok di sampul lagu yang diputar, diterangkan atau digelapkan sampai terbaca di island), sinkronkan warna wallpaper (kaca notch, dock, dan kartunya memakai warna turunan wallpaper; dock punya inti solid dan tepi tembus pandang agar latar belakang samar terlihat; gambar wallpaper tidak ditampilkan; warna ikut berubah saat wallpaper berganti; aksen ikut wallpaper selama belum memilih aksen), dan warna sendiri (pilih latar, teks, dan aksen dari preset, ambil dari layar, atau warna bebas; warna lain diturunkan dari ketiganya seperti di pembuat paket, kontras teks ditampilkan, dan hasilnya bisa disimpan jadi paket tema). Keempat material punya karakter tetap di island, panel, dock, kartu, dan sheet: **Bening** paling tembus pandang, blur paling kuat, mengikuti warna di belakangnya, dengan rim terang dan bayangan teks agar tetap terbaca di atas jendela terang; **Liquid** paling pekat dan hidup, blur paling lembut, kilau paling kuat, dan tanpa bayangan teks; **Frost** memakai warna netral yang diburamkan paling merata, lebih padat, dengan rim lembut; **Solid** sepenuhnya pekat, mengikuti warna latar tema, dengan rim tipis yang tegas. Jalur GPU dan jalur cadangan (termasuk saat wallpaper disinkronkan) memakai resep yang sama, jadi bentuk material tidak berubah ketika akselerasi grafis tidak tersedia. Saat **Sinkronkan wallpaper** aktif, Bening paling banyak memperlihatkan desktop, Frost paling pekat, dock tetap solid di dalam dengan tepi tembus pandang, dan gambar wallpaper tidak ditampilkan secara utuh. Pratinjau tema menampilkan desktop mini untuk Sistem, Terang, dan Gelap, sedangkan pemilih material memakai contoh kaca dari resep yang sama, jadi perbedaan Bening, Liquid, Frost, dan Solid tetap terlihat meski aksen sedang monokrom.
- **Paket tema**: paket, suara sapaan paket, folder paket, dan pembuat paket.

### Dynamic Island

- **Letak dan bentuk**: letak lewat sketsa layar (atas tengah, pojok kiri atas, pojok kanan atas, sisi kiri, sisi kanan; di sisi kiri dan kanan island berdiri tegak dan panelnya mekar ke samping), lalu geser, jarak dari pojok, atau tinggi sesuai letaknya; menempel di tepi layar, garis cahaya, lebar panel, layar tujuan (beserta resolusinya), dan sisakan ruang di tepi atas. Panel terbuka sebentar selama diatur supaya hasilnya terlihat. Saat layar tujuan dicabut, Deskmate pindah ke layar utama dan kembali saat layar itu tersambung lagi.
- **Layar**: di **Ukuran dan layar › Layar**, pilih monitor tertentu atau **Semua layar**. Dalam mode semua layar, monitor lain menampilkan notch ringkas; klik atau arahkan kursor sesuai cara buka panel untuk memindahkan panel ke sana. Hanya satu panel aktif pada satu waktu.
- **Perilaku**: mode (Normal, Mini, Otomatis), notifikasi saat Mini, buka panel dengan arahkan kursor atau klik beserta jedanya, dan aplikasi yang membuat island menyingkir atau menahan notifikasi saat jendelanya di depan.
- **Saat diam**: mata, mata mengikuti kursor, wajah saat diam (mata atau jam), serta isi kiri dan kanan (kosong, jam, tanggal, baterai, cuaca).
- **Ringkasan**: urutan kartu (Otomatis: yang sedang berjalan naik; atau Sesuai daftar) dan kartu yang tampil beserta urutannya: kuota AI, musik, agen, timer, kuliah, pesan, rak, cuaca, baterai, clipboard, dan sholat. Klik kanan kartu di Ringkasan untuk menyembunyikannya.
- **Tab panel**: gaya tab (otomatis, ikon, titik), pindah tab dengan klik atau cukup diarahkan kursor (gulir roda mouse selalu bisa), tab yang terbuka pertama, serta tab yang tampil beserta urutannya (seret pegangan di kiri ikon, atau fokuskan lalu tekan panah atas dan bawah).

### Dock

- **Dock**: bar aplikasi dan widget di tepi bawah setiap layar, mati sampai dinyalakan. Setiap dock mengikuti layar, ukuran, kecerahan, area kerja, layar penuh, dan sembunyi otomatisnya sendiri. Ganti taskbar Windows (Start, tray, Quick Settings, dan jam pindah ke dock di layar island; taskbar kembali saat dimatikan), bentuk (mengambang atau selebar layar), ukuran ikon, perbesar saat disorot, sembunyikan dock (selalu tampil, otomatis, atau saat tertutup jendela), tetap tampil di desktop kosong, tampil di rekaman layar, sisakan ruang, pratinjau jendela, angka notifikasi, tombol cari dan semua jendela, tombol Windows membuka Deskmate Start, widget (jam, musik, kuota AI dan agen, timer, cuaca, baterai, CPU dan RAM), dan aplikasi tersemat. Saat menggantikan taskbar, **Tampilkan taskbar Windows** di menu island atau klik kanan dock menawarkan durasi 5, 10, 30, atau 60 detik; taskbar asli kembali selama durasi itu lalu dock menggantikannya lagi. Pilihan **Ganti taskbar Windows** tidak berubah, dan **Sembunyikan taskbar Windows** tersedia untuk mengakhiri lebih cepat.

### Gamifikasi

Satu halaman untuk fitur yang dibuat seru: bagian **Ketikan** (combo, suara, Fever Mode, penalti, XP, dan Sprint 30 detik) lalu bagian **Pet**. Tombol **Atur** di bilah aksi pet langsung membuka bagian **Pet**.

- **Pet di dock**: hewan piksel kecil yang berjalan di atas dock, atau di tepi bawah layar tanpa dock; mati sampai dinyalakan. Pilih hewan (kucing, anjing, atau pet buatan sendiri), warna bulu, ukuran (kecil, sedang, besar), dan kecepatan jalan. **Folder pet** membuka `%APPDATA%\Deskmate\pets\`; satu folder per pet berisi `sheet.png` dan `pet.json` dari **Export Sprite Sheet** di Aseprite (JSON Array atau Hash). Tag `walk` wajib, `idle`, `sit`, dan `sleep` boleh tidak ada; tanpa tag sama sekali, semua frame dipakai untuk berjalan. Pet yang gagal dimuat diganti kucing dan alasannya tampil di bawah **Hewan**. **Reaksi** (nyala secara bawaan) membuat pet kaget saat notifikasi masuk (kecuali saat Senyap), senang saat tugas, agen, atau fokus selesai, bergoyang selama musik diputar, dan tidur setelah 3 menit tanpa input. Saat kembali atau mengeklik pet yang tidur karena idle, pet bangun dan menggeliat tanpa melompat. Selama sesi fokus aktif, pet berhenti berjalan dan menahan reaksi otomatis, terlepas dari opsi tahan notifikasi dan sakelar Reaksi. Mengelus pet (gerakkan kursor bolak-balik di atasnya) memunculkan hati, dengan atau tanpa **Reaksi**. Kucing dan anjing bawaan punya pose sendiri untuk senang, kaget, menggeliat, diangkat, dan mencakar; bulu hitam memakai mata kuning agar matanya tetap terlihat. Pet buatan sendiri boleh punya tag `happy`, `surprised`, dan `stretch`; tanpa tag itu, pose berdiri dan duduk yang dipakai. **Kejar kursor otomatis** (mati secara bawaan) memicu kejaran hingga 1,5 detik saat kursor disapukan cepat menyamping di sepanjang dock (di atas ikon atau tepat di atas tepinya, bukan di jendela aplikasi), dengan jeda minimal 20 detik sebelum kejaran otomatis berikutnya; kejaran batal jika kursor menjauh, dan tidak tersedia saat fokus, layar penuh, atau **Kurangi gerak**. Untuk mengangkat pet, tahan klik kiri pada badannya lalu geser. Pet tetap di sekitar jalurnya dan jatuh dengan satu pantulan kecil ketika dilepas. Klik singkat tetap untuk melompat, sekaligus membuka bilah aksi (Elus, Lompat, Kejar, Tidur, Chat saat terhubung, Atur); klik kanan juga membukanya, dan klik pet sekali lagi menutupnya. Kejar dan lompatan tidak tersedia saat fokus atau **Kurangi gerak**. Angkat tidak tersedia saat fokus, layar penuh, atau **Kurangi gerak**. Pet kustom bisa menambahkan tag `held`; jika tidak ada, pose duduk dipakai. Jika **Reaksi** menyala, pet melompat kecil ke arah ikon aplikasi yang baru mulai berjalan atau mencakar udara sekali saat angka badge bertambah. Hanya ikon di dekat pet pada dock yang terlihat yang bisa memicu reaksi. Mode Senyap mematikan cakaran notifikasi; Fokus, layar penuh, dan **Kurangi gerak** mematikan kedua reaksi ikon. Tag `scratch` opsional untuk pose cakaran kustom.
- **Teman pet (Online)**: **Nama tampilan** (yang dilihat teman; bawaannya "Teman"), **Terima custom pet dari teman**, **Jadi host (Internet)** untuk membuka ruang lewat Cloudflare Quick Tunnel dan **Salin kode** undangannya, serta **Gabung ke teman** untuk menempel kode `deskmate://…#PIN`. Satu ruang memuat sampai 4 pet. Jika `cloudflared.exe` belum ada, **Pasang & Host** mengunduhnya ke `%APPDATA%\Deskmate\tools\` dan menolaknya bila tidak bertanda tangan sah dari Cloudflare, Inc.

### Tugas dan catatan

* Buka dengan **Ctrl+Alt+J**, menu tray **Tugas dan catatan**, **Pengaturan › Tugas & Kalender › Tugas dan catatan › Buka**, atau tombol **Tugas dan catatan** di dock. **Ctrl+Alt+T** tetap untuk Rekam rapat.
* **Dock › Tombol tugas dan catatan** (`dockbar.work`, aktif secara bawaan) menyatukan pintasan dan jendelanya pada satu slot dock yang sama (seperti aplikasi tersemat): baik dibuka dari ikon dock maupun pintasan **Ctrl+Alt+J**, indikator aktif menyala tepat di bawah ikon tersebut tanpa memunculkan ikon jendela kedua di sisi kanan dock. Tombol **×** menyembunyikan jendela dan memadamkan indikatornya.
* Tugas lokal: ketik lalu Enter untuk tambah cepat, atau **Tugas baru…** untuk formulir. Gunakan pensil di baris tugas, klik ganda, menu klik kanan, atau **F2/Enter** untuk mengubah judul, tenggat, prioritas, dan status. Filter: Semua, Aktif, Hari ini, Terlambat, Selesai. Sintaks cepat `@besok 09:00` tetap tersedia.
* Catatan lokal: pilih catatan di kiri atau klik **Baru**. Baris pertama menjadi judul; isi tersimpan otomatis. Jika file berubah dari luar aplikasi, konfirmasi muncul sebelum draf menimpanya. Penghapusan memakai konfirmasi dan bisa dipulihkan dari Recycle Bin.

### Fitur

- **Musik**: lirik di island, lirik online, visualizer.
- **Pesan**: notifikasi pesan dan isinya, aplikasi chat tambahan, pengirim prioritas (nama atau kata yang tetap muncul saat Senyap dan rapat). Banner pesan (Otomatis atau Deskmate) dan mode berbagi layar (Otomatis, Nyala, Mati). **Deskmate penerima utama** mematikan banner Windows untuk aplikasi chat, panggilan, dan Snipping Tool sekali klik (notifikasinya tetap di pusat notifikasi untuk dibaca Deskmate), dan **Kembalikan banner Windows** membatalkannya.
- **Sistem**: HUD Caps Lock, Num Lock dan Scroll Lock, bahasa input; pengingat cabut charger di 80, 85, atau 90%, flashdisk; kelas yang berlangsung, progres unduhan, mode rapat.
- **Rak dan berkas**: rak berkas, simpan isi rak, kosongkan rak otomatis; ubah format, riwayat clipboard, preview screenshot.
- **Agen**: hubungkan atau lepas tiap agent, satu sakelar **Kuota agent** untuk kuota semua agent, suara saat agen minta izin atau selesai dan saat `--run` selesai (suara bawaan Windows, diam saat Senyap atau layar penuh), port server hook (termasuk **Ganti port acak**), dan berapa lama izin ditahan.
- **Dikte**: nyalakan dikte, tombol yang ditahan (Ctrl kanan atau Alt kanan), bahasa ucapan, mesin dikte (diatur di halaman AI), kamus dan snippet, buang kata pengisi, perintah baris baru, gaya per aplikasi, kecilkan musik, rekam rapat, ringkasan dan to-do, folder transkrip, serta hitungan kata.
- **Ingatan**: memori aktivitas lokal (`ingat`, bawaan mati) beserta status dan **Jeda 1 jam**. Bagian yang dicatat: teks di layar (`ingat.text`), OCR cadangan (`ingat.ocr`), teks yang disalin (`ingat.clip`), rekam panggilan otomatis (`ingat.rapat`), dan pet yang memakai Ingatan (`ingat.pet`). Layanan AI (`ingat.ai`: `ollama` bawaan, `lmstudio`, `same`, `off`, atau id layanan cloud) dan model embedding (`ingat.embed`). Aplikasi dan situs dikecualikan (`ingat.block.apps`, `ingat.block.sites`), lama simpan (`ingat.keep.events`, `ingat.keep.days`), **Lupakan 1 jam terakhir**, **Hapus semua Ingatan**, salin konfigurasi MCP, dan folder data. Penjelasan lengkap ada di [`ingatan.md`](ingatan.md).
- **Pengingat**: sholat (kota dan menit sebelum), cuaca dan hujan, minum (selang dan target), istirahat mata, peregangan, lama istirahat fokus, tahan notifikasi saat fokus.

### Bantuan

- **Aksesibilitas**: tampil di rekaman layar, umumkan ke pembaca layar, kurangi gerak, gerak (Tenang tanpa pantulan, Bawaan, atau Lincah), Senyap, dan Senyap ikut Jangan ganggu Windows.
- **Pintasan**: ganti atau kosongkan setiap pintasan, termasuk pintasan teleprompter. Klik pintasannya lalu tekan kombinasi baru (Backspace mengosongkan, Esc batal). Kombinasi yang sudah dipakai aplikasi lain ditolak saat itu juga, dan saat Deskmate dibuka island memberi tahu kalau ada pintasan yang direbut aplikasi lain. **Kembalikan** memasang lagi semua pintasan bawaan. Di keyboard yang punya AltGr (misalnya Jerman atau Polandia), AltGr tetap mengetik karakternya walau kombinasinya sama dengan pintasan Ctrl+Alt Deskmate.
- **Tentang**: versi, cek pembaruan, **Apa yang baru**, ulangi **Tur Deskmate**, **Kirim saran** (ide fitur atau laporan masalah, langsung dari Deskmate tanpa login), dan info pembuat.

Island yang ikut terekam tidak bisa memakai efek kaca, karena kaca membaca layar di belakangnya dan akan ikut membaca dirinya sendiri. Selama opsi itu menyala, latarnya polos.

## Pengaturan lanjutan

Semua kunci di `%APPDATA%\Deskmate\settings.ini` (ubah saat Deskmate tidak berjalan):

| Kunci | Bawaan | Fungsi |
|---|---|---|
| `material` | `liquid` | `clear` (paling tembus pandang, blur paling kuat, warna di belakang kaca terlihat, bayangan teks aktif; dengan `wall.sync`, warnanya dari wallpaper), `liquid` (paling pekat dan hidup, blur paling lembut, kilau paling kuat), `frost` (netral, padat, buram merata), atau `solid` (pekat, mengikuti warna latar tema). Kunci lama `glass=0` dibaca sebagai `solid` |
| `theme` | `sistem` | `sistem` (mengikuti mode aplikasi Windows), `terang`, atau `gelap`; nilai lama `otomatis` tetap dibaca sebagai `sistem` |
| `lyrics` | `1` | Lirik tersinkron di island |
| `lyrics.online` | `1` | Cari lirik di LRCLIB dan NetEase |
| `visualizer` | `1` | Visualizer musik |
| `music.view` | `auto` | `auto`, `lyrics`, atau `album` |
| `notif` | `1` | Notifikasi pesan |
| `notif.content` | `1` | Tampilkan isi pesan |
| `notif.apps` | kosong | Aplikasi chat tambahan, dipisah koma, dicocokkan dengan nama aplikasi |
| `notif.vip` | kosong | Nama pengirim atau kata, dipisah koma; pesan yang memuatnya tetap muncul saat Senyap |
| `agent.port` | acak | Port server hook; instalasi lama memakai `47821` |
| `agent.wait` | `110` | Detik menahan permintaan izin |
| `sound.agent` | `0` | Suara saat agen minta izin atau selesai, dan saat `--run` selesai |
| `update.check` | `1` | Cek versi baru sekali sehari |
| `accent` | tidak ada (Ungu tenang `#AE9DEB`) | Warna aksen, misalnya `#8FB1E6`; kosong untuk Grafit (monokrom); `album` mengambil warna dari sampul lagu yang diputar. Preset jenuh dari versi lama dipindah otomatis ke versi tenangnya saat Deskmate dibuka |
| `wall.sync` | `0` | Kaca notch, dock, dan kartu diwarnai dari wallpaper; Bening paling tembus pandang, Frost paling pekat; dock punya inti solid dan tepi tembus pandang agar latar belakang samar terlihat; perubahan wallpaper ikut terdeteksi; aksen ikut wallpaper selama `accent` belum diatur atau kosong; mati saat warna sendiri dipakai |
| `monitor` | kosong | Monitor pilihan untuk island; kosong berarti monitor utama. Jika monitor pilihan dicabut, island kembali sementara ke monitor utama tanpa melupakan pilihan. |
| `monitor.all` | `0` | Di **Island › Ukuran dan layar › Layar**, pilih **Semua layar** untuk menampilkan notch ringkas di monitor lain. Klik atau arahkan kursor sesuai mode **Buka panel** untuk membuka panel di sana. Hanya satu panel aktif; ruang tepi atas, bila diaktifkan, tetap di monitor pilihan. Pengaturan Dock tidak berubah. |
| `focus.break` | `5` | Menit istirahat setelah fokus |
| `dock` | `1` | Menempel di tepi layar; `0` untuk mengambang |
| `position` | `0` | Geser island, `-40` (kiri) sampai `40` (kanan), persen dari ruang kosong |
| `place` | `top` | Letak island: `top`, `top-left`, `top-right`, `left`, `right` |
| `place.inset` | `16` | Jarak dari pojok untuk `top-left` dan `top-right`, px |
| `place.y` | `22` | Tinggi island di sisi kiri atau kanan, persen dari atas layar |
| `home.cards` | `quota,media,agents,timer,class,messages,shelf` | Kartu Ringkasan dan urutannya; tambahan: `weather`, `battery`, `clips`, `pray` |
| `home.sort` | `auto` | `auto`: yang sedang berjalan naik; `list`: sesuai `home.cards` |
| `dockbar.on` | `0` | Dock di tepi bawah |
| `dockbar.replace` | `0` | Dock menggantikan taskbar Windows; taskbar kembali saat dimatikan atau Deskmate keluar |
| `dockbar.reserve` | `1` | Jendela yang dimaksimalkan berhenti di atas dock |
| `dockbar.magnify` | `1` | Ikon dock membesar saat disentuh kursor |
| `dockbar.mag` | `150` | Besar ikon saat disorot, persen; `100` sama dengan mati |
| `dockbar.style` | `float` | `float`: tiga kapsul mengambang; `full`: satu bar selebar layar |
| `dockbar.size` | `m` | Ukuran ikon dock: `s`, `m`, `l` |
| `dockbar.hide` | `never` | `never`; `auto`: muncul saat kursor menyentuh tepi bawah; `smart`: menyingkir saat jendela menutupinya |
| `dockbar.previews` | `1` | Pratinjau jendela saat kursor di atas aplikasi yang terbuka |
| `dockbar.badges` | `1` | Angka notifikasi Windows di ikon aplikasi |
| `dockbar.search`, `dockbar.taskview` | `1`, `0` | Tombol cari dan semua jendela saat menggantikan taskbar |
| `dockbar.tray` | `1` | Saat Dock menggantikan taskbar, tombol panah di setiap monitor membuka daftar ikon tersembunyi yang sama di atas Dock yang dipilih |
| `dockbar.capture` | `0` | Dock ikut screenshot dan screen share; kacanya diganti latar polos |
| `dockbar.w.clock`, `dockbar.w.media`, `dockbar.w.ai`, `dockbar.w.timer`, `dockbar.w.weather`, `dockbar.w.battery`, `dockbar.w.system` | `1` (`system`: `0`) | Widget dock |
| `dockbar.pins` | dari taskbar | Aplikasi tersemat dipisah `\|`: shortcut, exe, atau `shell:AppsFolder\<id>`; diisi dari taskbar Windows saat dock pertama kali nyala |
| `pet.on` | `0` | Pet berjalan di atas dock, atau di tepi bawah layar tanpa dock |
| `pet.kind` | `cat` | `cat`, `dog`, atau `custom:<nama folder>` di `%APPDATA%\Deskmate\pets\` |
| `pet.color` | `orange` | Warna bulu hewan bawaan: `orange`, `brown`, `gray`, `black`, `cream` |
| `pet.size` | `m` | Tinggi pet: `s` 32 px, `m` 48 px, `l` 64 px, dikali skala layar |
| `pet.speed` | `100` | Kecepatan jalan, persen, `50` sampai `200` |
| `pet.react` | `1` | Pet bereaksi pada notifikasi, pekerjaan selesai, musik, sesi fokus, dan saat Anda tidak di depan komputer |
| `pet.autoChase` | `0` | Pet mengejar sapuan kursor cepat di sepanjang dock selama maksimal 1,5 detik |
| `pet.link.name` | kosong | Nama tampilan untuk teman pet, maksimal 24 karakter; kosong berarti "Teman" |
| `pet.link.custom` | `1` | Unduh pet buatan sendiri milik teman dan simpan di `%APPDATA%\Deskmate\pet-cache\` |
| `notif.muted` | kosong | Aplikasi yang banner Windows-nya dimatikan oleh Deskmate penerima utama |
| `panel.width` | `560` | Lebar panel, `440` sampai `640`; dari sekitar `570` Ringkasan memakai tiga kolom |
| `panel.tabs` | semua | Tab yang tampil dan urutannya: `home`, `music`, `agents`, `class`, `messages`, `shelf`, `control`, `remind`, `convert`, `phone`. Kunci lama `tabs` dipindahkan sekali |
| `panel.first` | kosong | Tab yang terbuka pertama, misalnya `music`; kosong untuk tab pertama di urutan |
| `panel.open`, `panel.delay` | `hover`, `330` | Panel terbuka saat kursor diam di island (`hover`) atau saat diklik (`click`); jeda hover dalam ms, `150` sampai `800` |
| `island.except` | kosong | Aturan per aplikasi, dipisah koma: `exe:hide:Nama` menyembunyikan island, `exe:quiet:Nama` menahan notifikasi biasa selama jendela aplikasi itu di depan |
| `tabs.style` | `auto` | `auto` (titik bila lebih dari tujuh tab), `icons`, atau `dots` |
| `tabs.trigger` | `click` | `hover`: tab terbuka saat kursor diam sebentar di atasnya |
| `mini` | `auto` | `normal`, `mini`, atau `auto` (Mini saat tepi atas tertutup atau setelah diam semenit) |
| `mini.notif` | `1` | Notifikasi pesan tetap muncul saat Mini |
| `island.eyes`, `island.follow` | `1`, `1` | Mata di sliver Mini, dan mata mengikuti kursor |
| `agent.quota` | `1` | Pasang status line Deskmate untuk kuota Claude saat Claude Code dihubungkan; diatur bersama `agent.quota.agents` oleh sakelar **Kuota agent** |
| `agent.quota.agents` | `1` | Kuota Codex, Antigravity, dan Copilot di tab Agen, termasuk status line Deskmate di Antigravity CLI; diatur bersama `agent.quota` |
| `prompter.wpm` | `140` | Kecepatan teleprompter, kata per menit |
| `prompter.size` | `19` | Ukuran huruf teleprompter, px |
| `prompter.mirror` | `0` | Teleprompter dicerminkan |
| `prompter.width`, `prompter.height`, `prompter.bg` | `560`, `150`, `55` | Ukuran teleprompter dalam px (diubah dengan menyeret tepinya) dan kepekatan latar dalam persen |
| `lang` | `id` | `id` atau `en` |
| `clock.24` | `1` | `1` jam 24 (14.30), `0` jam 12 (2.30 pm) |
| `capture.show` | `0` | Island ikut terekam saat screenshot dan rekam layar (tanpa efek kaca) |
| `a11y.announce` | `1` | Bacakan toast ke pembaca layar yang sedang aktif |
| `notif.banner` | `auto` | `auto`: Deskmate muncul hanya untuk aplikasi yang banner Windows-nya mati; `tepi`: selalu |
| `share.mode` | `auto` | Mode berbagi layar: `auto`, `on`, atau `off` |
| `idle.left`, `idle.right` | kosong | Isi samping island diam: `clock`, `date`, `battery`, `weather` |
| `tidy` | `0` | Rapikan Downloads |
| `tidy.layout` | `type` | `type` (per jenis) atau `year` (tahun, lalu jenis) |
| `tidy.nudge` | `15` | Jumlah berkas lepas sebelum Deskmate bertanya; `0` tidak pernah |
| `clipboard` | `0` | Riwayat clipboard |
| `downloads` | `1` | Progres unduhan |
| `class.island` | `1` | Kelas yang sedang berjalan tampil di island |
| `shelf` | `1` | Rak berkas |
| `shelf.keep`, `shelf.expire` | `0`, `0` | Simpan jalur rak di `shelf.txt`; kosongkan otomatis setelah `24` atau `72` jam |
| `convert` | `1` | Kotak **Ubah format** saat berkas diseret ke island |
| `shots` | `1` | Preview screenshot |
| `drives` | `1` | Flashdisk dicolok dan dikeluarkan |
| `meeting` | `1` | Mode rapat |
| `keys.caps`, `keys.num`, `keys.lang` | `1`, `0`, `1` | HUD Caps Lock, HUD Num Lock dan Scroll Lock, HUD bahasa input |
| `battery.unplug` | `0` | Ingatkan cabut charger di `80`, `85`, atau `90` persen; `0` mati |
| `note.to` | `rak` | Tujuan catatan kilat: `rak`, `todoist`, atau `notion` |
| `dnd` | `0` | Senyap: hanya izin, tenggat, sholat, dan timer yang muncul |
| `dnd.follow` | `0` | Senyap ikut menyala selama Jangan ganggu Windows aktif |
| `motion.reduce` | `auto` | `auto` mengikuti efek animasi Windows, `1` selalu tenang, `0` selalu penuh |
| `motion.feel` | `bawaan` | `tenang` (lebih lambat, tanpa pantulan), `bawaan`, atau `lincah` (lebih cepat, pantulan lebih besar). `motion.reduce` tetap menang |
| `pray`, `pray.city`, `pray.before` | `0`, kosong, `10` | Pengingat sholat, kota, menit sebelum |
| `drink`, `drink.every`, `drink.goal` | `0`, `60`, `8` | Pengingat minum, selang menit, target gelas |
| `eyes` | `0` | Istirahat mata 20-20-20 |
| `stretch`, `stretch.every` | `0`, `60` | Peregangan dan selangnya |
| `theme.pack` | kosong | Nama folder paket tema di `%APPDATA%\Deskmate\themes` |
| `theme.voice` | `1` | Suara sapaan paket tema |
| `look.custom`, `look.bg`, `look.text`, `look.accent` | `0`, kosong | Warna sendiri dalam `#RRGGBB`, ditimpakan di atas paket tema |
| `weather` | `0` | Cuaca dan peringatan hujan, memakai kota `pray.city` |
| `phone`, `phone.notifs`, `phone.otp` | `0`, `1`, `1` | Sambungan HP, notifikasi pesan dari HP, kode OTP dari HP |
| `phone.port` | acak | Port sambungan HP, dipilih saat pertama dinyalakan |
| `key.hide`, `key.nav`, `key.mini`, `key.mic`, `key.note`, `key.mirror`, `key.picker` | `Ctrl+Alt+H`, `I`, `K`, `M`, `N`, `C`, kosong | Pintasan global, misalnya `Ctrl+Shift+F10` atau `Win+Alt+H`; `none` mematikannya |
| `key.prompter.pause`, `.restart`, `.back`, `.ahead`, `.faster`, `.slower`, `.setup`, `.stop` | `Ctrl+Alt+Space`, `R`, `Left`, `Right`, `=`, `-`, `P`, `X` | Pintasan teleprompter, hanya terpakai selama naskah berjalan |
| `kulia.url`, `kulia.key` | server kulia.live | Arahkan ke instance Kulia lain |
| `dictate` | `0` | Dikte. Hook keyboard hanya terpasang selama ini menyala |
| `dictate.key` | `rctrl` | Tombol yang ditahan: `rctrl` (Ctrl kanan) atau `ralt` (Alt kanan) |
| `dictate.engine` | `groq` | Dibaca hanya kalau `ai.stt` belum ada; `lokal` berarti whisper.cpp |
| `ai.stt` | `groq` | Mesin suara: `groq`, `openai`, `custom`, atau `lokal` |
| `ai.text` | `groq` | Layanan teks: `groq`, `openai`, `anthropic`, `gemini`, `openrouter`, `deepseek`, `mistral`, `ollama`, `lmstudio`, atau `custom` |
| `ai.<layanan>.model` | kosong | Model layanan teks; kosong berarti model pertama yang tersedia dari daftar Deskmate |
| `ai.custom.url` | kosong | Alamat server kustom, `https://` atau `http://localhost` |
| `ai.custom.stt` | `whisper-1` | Model suara server kustom |
| `dictate.lang` | `id` | `id`, `en`, atau `auto`. Bawaan mengikuti bahasa Deskmate |
| `dictate.model` | `seimbang` | Model offline: `cepat` (base), `seimbang` (small), `akurat` (large-v3-turbo) |
| `dictate.fillers`, `dictate.commands`, `dictate.style`, `dictate.duck` | `1` | Buang kata pengisi, perintah baris baru, gaya per aplikasi, kecilkan musik |
| `rapat.summary` | `1` | Ringkasan dan to-do di transkrip rapat, lewat layanan teks |
| `edge.light` | `1` | Garis cahaya progres dan kilau pesan di tepi island |
| `reserve.top` | `0` | Sisakan ruang di tepi atas; jendela yang dimaksimalkan mulai di bawah island |
| `focus.hold` | `1` | Tahan notifikasi biasa selama sesi fokus, lalu rangkum |
| `key.suruh` | kosong | Pintasan untuk membuka jendela obrolan Suruh Deskmate |
| `whatsnew.seen` | versi terpasang | Versi terakhir yang ringkasannya sudah tampil; kosongkan untuk melihatnya lagi saat Pengaturan dibuka |
| `tour.done` | `0` | Tur pengguna baru sudah selesai atau ditutup |
| `key.rapat`, `key.dictate.paste` | `Ctrl+Alt+T`, kosong | Mulai atau hentikan rekaman rapat; tempel dikte terakhir |

Variabel lingkungan untuk pengembangan:

- `DESKMATE_FPS=1` mencatat fps, biaya tiap bagian frame, dan frame yang lambat ke `deskmate.log`.
- `DESKMATE_DUMP=<folder>` menyimpan frame yang dirender, karena efek kaca tidak terlihat di screenshot.
- `DESKMATE_SCALE=<angka>` memaksa skala tampilan.
- `DESKMATE_HOME=<folder>` memasang dan melepas hook di folder home lain, untuk menguji tanpa menyentuh konfigurasi agent yang asli.
- `DESKMATE_DIR=<folder>` menjalankan salinan uji dengan data sendiri, berdampingan dengan Deskmate yang sedang dipakai. Salinan uji tidak menulis registry autostart, tidak mengecek pembaruan, dan memasang hook di `<folder>\home`.
- `build.cmd <keluaran.exe>` membangun ke lokasi lain, supaya exe yang sedang berjalan tidak tersentuh.
