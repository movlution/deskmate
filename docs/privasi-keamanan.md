# Privasi dan keamanan

[← Kembali ke README](../README.md)

## Privasi

Deskmate hanya menghubungi layanan luar berikut, dan masing-masing bisa dimatikan. Notion, Todoist, dan kalender hanya dihubungi kalau kamu menghubungkannya sendiri:

| Layanan | Data yang dikirim | Kapan | Cara mematikan |
|---|---|---|---|
| [LRCLIB](https://lrclib.net) | Judul, artis, dan durasi lagu | Saat lagu baru diputar | **Pengaturan > Cari lirik online** |
| [NetEase Cloud Music](https://music.163.com) | Judul dan artis lagu | Saat LRCLIB sibuk atau tidak punya lirik bersinkron | **Pengaturan > Cari lirik online** |
| [Kulia](https://kulia.live) | Email, kode masuk, dan permintaan daftar tugas serta jadwalmu | Hanya kalau kamu masuk ke Kulia | Menu tray > **Kulia > Keluar dari Kulia** |
| [Notion](https://www.notion.so) | Token integrasi dan permintaan isi database yang kamu bagikan | Hanya kalau kamu menghubungkan Notion | **Pengaturan > Integrasi > Lepas** |
| [Todoist](https://todoist.com) | Token API dan permintaan daftar tugas aktif | Hanya kalau kamu menghubungkan Todoist | **Pengaturan > Integrasi > Lepas** |
| Kalender iCal | Permintaan isi kalender ke alamat yang kamu tempel | Hanya kalau kamu menambah kalender | **Pengaturan > Integrasi**, hapus kalendernya |
| [Open-Meteo](https://open-meteo.com) | Koordinat kota yang kamu pilih di Pengingat | Tiap 30 menit, hanya kalau **Cuaca dan hujan** dinyalakan | **Pengaturan > Pengingat > Cuaca dan hujan** |
| Mesin suara pilihanmu: [Groq](https://groq.com), [OpenAI](https://openai.com), atau server kustom | Rekaman suara dikte atau rapat, bahasa ucapan, dan istilah di Kamus (sebagai petunjuk ejaan) | Hanya kalau mesin suara itu dipilih dan key-nya diisi; saat kamu selesai dikte atau menghentikan rekaman rapat | **Pengaturan > AI > Mesin suara > Offline**, atau **Lepas** key-nya |
| Layanan teks pilihanmu: Groq, OpenAI, [Anthropic](https://www.anthropic.com), Google Gemini, OpenRouter, DeepSeek, Mistral, server kustom, atau Lokal (Ollama dan LM Studio di laptop sendiri) | Ringkasan rapat: teks transkripnya. Suruh Deskmate: kalimat perintahmu beserta hingga 15 pesan sebelumnya di obrolan yang sama, dan waktu sekarang. Untuk pertanyaan tentang isi Deskmate, model dapat meminta data terbatas dari tugas, ringkasan catatan, jadwal, pengingat, status timer dan media, nama item di Rak, jumlah item clipboard, atau aplikasi dan waktu notifikasi. Perbaiki dan terjemahkan: teks yang kamu blok. | Hanya saat fitur itu kamu jalankan, dan hanya kalau layanannya sudah diatur. Ollama dan LM Studio tidak keluar dari laptop. | **Lepas** key di **Pengaturan > AI**; timer, pengingat, catatan, dan hitungan tetap jalan offline |
| Layanan AI Ingatan (bawaan: Ollama lokal) | Hanya kalau kamu memilih layanan cloud: judul jendela, URL tanpa query, dan teks layar yang sudah disaring dari episode yang diringkas atau dipakai menjawab, serta pertanyaanmu. Ollama dan LM Studio tidak keluar dari laptop. | Saat episode diringkas, saat rekap harian dibuat, dan saat kamu bertanya ke Ingatan | **Pengaturan › Ingatan › Layanan AI › Ollama** atau **Mati**, atau matikan Ingatan |
| Google Forms, untuk kotak saran | Jenis saran, isi pesanmu, kontak kalau kamu mengisinya, dan versi Deskmate serta Windows kalau opsinya menyala | Hanya saat kamu menekan **Kirim** di **Pengaturan › Tentang › Kirim saran** | Tidak mengirim saran |
| [Hugging Face](https://huggingface.co/ggerganov/whisper.cpp) dan GitHub ([whisper.cpp](https://github.com/ggml-org/whisper.cpp)) | Permintaan unduh model dan program whisper.cpp | Hanya saat kamu klik **Unduh** di **Pengaturan > Dikte** | Tidak mengunduh |
| GitHub | Permintaan rilis terbaru beserta SHA-256 `Deskmate.exe`. Exe baru hanya diunduh saat kamu klik **Perbarui**. | Sekali sehari, dan saat **Cek pembaruan** diklik | `update.check=0` di `settings.ini` |

- Isi pesan, data agent, dan isi layar tidak pernah dikirim ke layanan luar, kecuali teks layar yang sudah disaring saat Ingatan memakai layanan AI cloud yang kamu pilih sendiri.
- Ingatan (mati sampai dinyalakan) mencatat aplikasi, judul jendela, URL tanpa query, teks di layar, dan teks yang disalin ke `%APPDATA%\Deskmate\ingat\ingat.db`, hanya di laptop ini. Ketikan keyboard, jendela pribadi, kolom kata sandi, dan pengelola kata sandi tidak pernah dicatat. Rincian lengkapnya ada di [`ingatan.md`](ingatan.md). Mode berbagi layar hanya membaca judul dan kelas jendela yang terlihat, selama mikrofon dipakai. Server hook hanya bisa dijangkau dari komputer ini.
- Sambungan HP hanya berjalan di jaringan lokal, antara laptop dan HP yang kamu pasangkan, tanpa server perantara. Yang lewat: baterai HP, notifikasi pesan (isinya mengikuti setelan isi pesan di HP), kode OTP beserta nama aplikasinya, berkas yang kamu kirim, dan permintaan izin agen. Teks notifikasi lain tidak pernah dikirim.
- Lirik yang ditemukan disimpan di `%APPDATA%\Deskmate\lyrics` supaya tidak diambil ulang.
- Efek kaca membaca layar di belakang island untuk membuat latar buramnya. Karena itu island tidak ikut terekam saat screenshot atau screen share. Nyalakan **Tampil di rekaman layar** di Pengaturan kalau island perlu ikut terekam. Selama teleprompter berjalan, island selalu disembunyikan dari rekaman.
- Naskah teleprompter hanya disimpan di komputer ini.
- Riwayat teks dan gambar clipboard hanya ada di memori dan hilang saat Deskmate ditutup atau fiturnya dimatikan. Isi yang ditandai rahasia oleh aplikasi asal tidak dicatat.
- Dock membaca judul jendela, jalur exe, ikon, dan ID aplikasi yang sedang terbuka, serta beban CPU dan RAM, hanya di laptop ini untuk menggambar dirinya. Kaca dock dibuat dari salinan layar di belakangnya yang diperkecil dan diburamkan, tidak disimpan. Pratinjau jendela memakai thumbnail langsung dari Windows, tanpa menangkap atau menyimpan gambar.
- Saat dock menggantikan taskbar, ikon tray dari aplikasi lewat Deskmate lalu diteruskan ke Explorer seperti biasa. Deskmate hanya menyimpan salinan ikon dan tooltip-nya di memori untuk digambar.
- **Deskmate penerima utama** dan **Ganti taskbar Windows** mengubah setelan Windows milikmu sendiri (banner per aplikasi, dan taskbar disembunyikan otomatis). Deskmate mencatat keadaan semula dan mengembalikannya saat opsinya dimatikan, saat Deskmate keluar, atau saat Deskmate dibuka lagi setelah berhenti mendadak.
- Progres unduhan hanya membaca nama dan ukuran berkas sementara di folder Downloads.
- Rak hanya menyimpan jalur berkas, di memori, dan hilang saat Deskmate ditutup. Kalau **Simpan isi rak** dinyalakan, jalurnya disimpan di `shelf.txt` di folder data.
- **Teks** di preview screenshot dibaca oleh OCR bawaan Windows di laptop ini; tombolnya hanya muncul kalau Windows punya bahasa OCR yang terpasang.
- Ubah format berjalan di laptop ini. Berkasmu tidak dikirim ke mana pun; ffmpeg, LibreOffice, Pandoc, dan Microsoft Word (untuk PDF ke DOCX dan TXT) dijalankan dari yang sudah terpasang, Word tanpa jendela dan dengan makro dimatikan. Kompres dan gabung PDF dikerjakan Deskmate sendiri, dan OCR memakai mesin OCR bawaan Windows di laptop ini. Riwayat di tab Ubah hanya ada di memori.
- Jadwal sholat dihitung di laptop ini. Pengingat minum, mata, dan peregangan hanya membaca kapan terakhir ada input keyboard atau mouse, untuk tahu kamu sedang di depan laptop.
- Kecerahan, perangkat audio, Wi-Fi, dan Bluetooth diatur lewat API Windows di laptop ini.
- Dikte merekam mikrofon hanya selama tombol dikte ditahan, atau sampai kamu menyelesaikan dikte tanpa tangan. Rekamannya ada di memori dan dibuang setelah teksnya jadi. Dengan mesin offline, rekaman tidak keluar dari laptop ini; whisper.cpp membacanya dari berkas sementara yang langsung dihapus. Teks yang ditempel otomatis ditandai supaya tidak masuk riwayat clipboard Windows maupun riwayat clipboard Deskmate. Kamus, pengganti, dan snippet ada di `dikte.txt` di folder data, dan hitungan kata hanya di `settings.ini`.
- Selama Dikte menyala, Deskmate memasang hook keyboard untuk tahu kapan tombol dikte ditahan. Hook itu hanya memperhatikan tombol dikte, Esc, dan apakah tombol lain ikut ditekan (supaya Ctrl+C tidak dianggap dikte); tidak ada ketikan yang dicatat atau disimpan. Mematikan Dikte melepas hook-nya.
- Rekaman rapat ditulis ke `%APPDATA%\Deskmate\rapat\sementara` selama berjalan dan dihapus setelah transkrip jadi, juga saat rekaman dibatalkan. Transkripnya disimpan sebagai Markdown di `%APPDATA%\Deskmate\rapat`.
- Pengingat dari Suruh Deskmate disimpan di `pengingat-sekali.txt` di folder data dan dihapus begitu muncul. Riwayat dikte dan perintah di tab Bicara hanya ada di memori.
- Obrolan Suruh Deskmate (200 pesan terakhir) disimpan di `obrolan.json` di folder data, hanya di laptop ini. **Obrolan baru** mengosongkannya.
- Suruh Deskmate menjalankan aksi melalui fitur Deskmate, seperti membuka halaman, membuat tugas atau catatan, mengatur pengingat dan timer, serta mengirim kontrol pemutaran. Akses data menggunakan daftar bagian yang didukung. Konten clipboard, isi berkas Rak, isi notifikasi, dan kredensial dikecualikan.
- Semua pengaturan dan cache ada di `%APPDATA%\Deskmate`.

## Keamanan

- Server hook hanya mendengarkan di `127.0.0.1`, jadi tidak bisa dijangkau dari jaringan, dan memegang portnya secara eksklusif. Setiap request wajib membawa token acak 48 karakter, dan request yang datang dari browser ditolak. Token diperiksa sebelum isi request dibaca, koneksi serentak dibatasi, dan request yang tidak selesai dalam 10 detik diputus.
- Port acak per instalasi membuat pengguna Windows lain di komputer yang sama sulit menebak dan menyerobot port saat Deskmate tidak berjalan. Model ancamannya: program lain yang berjalan sebagai akunmu sendiri tetap bisa mengubah config agent secara langsung, jadi Deskmate tidak berusaha melindungi dari itu.
- Kartu izin menampilkan perintah atau isi perubahan berkas secara utuh. **Izinkan** terkunci sampai isi yang lebih dari 10 baris digulir habis, terkunci permanen bila teksnya terlalu panjang untuk island, dan juga selama 0,8 detik pertama setelah kartu muncul supaya klik yang kebetulan mendarat di sana tidak ikut mengizinkan.
- Kalau Deskmate tidak jalan atau tidak menjawab, agent kembali ke prompt izinnya sendiri. Deskmate tidak pernah mengizinkan apa pun tanpa klikmu.
- Tautan tugas Kulia hanya dibuka kalau alamatnya `http` atau `https`.
- Key semua layanan AI disimpan terenkripsi dengan DPAPI untuk akun Windows-mu (`groq.key` dan `ai-<layanan>.key`), sama seperti token integrasi, dan tidak ikut ekspor pengaturan. Alamat server kustom hanya diterima kalau `https://`, atau `http://` untuk localhost.
- Rapikan Downloads hanya memindah berkas di tingkat teratas Downloads, tidak pernah menimpa atau menghapus berkas, dan mencatat pindahannya di `rapikan.txt` supaya bisa diurungkan selama satu jam.
- Banner pesan membaca pengaturan notifikasi Windows per aplikasi (`ShowBanner`, `Enabled`) tanpa mengubahnya.
- Permintaan agent yang berisiko (hapus massal, unduh lalu jalankan, menyentuh `.env` atau kunci, push paksa) tidak bisa diizinkan "selalu" dan tidak ikut izin "selalu" yang sudah ada.
- whisper.cpp (rilis `b5454`) dan model offline dicek SHA-256-nya terhadap nilai yang tertanam di Deskmate sebelum dipakai. Berkas yang tidak cocok dihapus dan tidak pernah dijalankan.
- Tidak ada pembaruan diam-diam. Exe hanya berubah saat kamu klik **Perbarui**. Deskmate hanya menerima `Deskmate.exe` dari alamat rilis repo ini, mengunduhnya lewat HTTPS, lalu mencocokkan SHA-256-nya dengan digest yang dicatat GitHub untuk berkas itu. Kalau tidak cocok, exe lama tidak disentuh dan halaman rilis dibuka. Exe lama disimpan sebagai `Deskmate.old.exe` sampai Deskmate baru jalan, lalu dihapus. Kalau folder exe tidak bisa ditulis (misalnya di Program Files), Deskmate membuka halaman rilis. Begitu rilis Deskmate ditandatangani (Authenticode), exe baru juga wajib bertanda tangan sah dari penerbit yang sama dengan exe yang sedang jalan.
- `Deskmate.exe --notify` dan `/notify` dijaga token yang sama dengan hook agent. Teks dari skrip dibersihkan dari karakter kontrol, dipotong panjangnya, dan dibatasi satu toast per setengah detik.
- **Chat** di tab Agen hanya mengetik setelah Windows memastikan terminal sesi itu sudah di depan, dan memeriksanya lagi tepat sebelum mengetik.
- `settings.ini` dan sesi Kulia ditulis lewat berkas sementara lalu ditukar, jadi tidak pernah setengah tertulis.
- Rak menolak jalur jaringan (UNC) dan berkas pintasan (`.lnk`, `.url`, `.scf`, `.library-ms`, `.searchConnector-ms`), karena ikonnya bisa memicu koneksi keluar.
- Sambungan HP mati sampai kamu menyalakannya. Kunci dibuat dari kode pasangan (PBKDF2-SHA256, 60.000 putaran) dan disimpan dengan DPAPI. Setiap pesan dienkripsi AES-256 dan ditandatangani HMAC-SHA256 beserta waktu dan nonce, jadi pesan yang diubah atau diputar ulang ditolak. Berkas dari HP disimpan dengan nama yang dibersihkan dan ditandai sebagai unduhan, sehingga SmartScreen tetap memeriksanya sebelum dijalankan. Deskmate tidak pernah menjalankan apa pun yang datang dari HP.
- Menemukan celah? Laporkan secara privat lewat tab **Security > Report a vulnerability** di repo ini, jangan lewat issue publik.

## Tanda tangan kode

Rilis Deskmate belum ditandatangani, karena itu SmartScreen bisa memperingatkan saat pertama dijalankan. Cara memastikan exe yang kamu pegang asli:

- Setiap exe rilis dibangun dari repo ini oleh [GitHub Actions](../.github/workflows/build.yml).
- SHA-256 tertulis di catatan setiap rilis, dan GitHub mencatat digest yang sama untuk berkasnya. Cek dengan `Get-FileHash Deskmate.exe` di PowerShell.
- Atau build sendiri dari sumbernya (lihat [Build sendiri](../README.md#build-sendiri)).

Deskmate tidak mengirim data apa pun ke luar kecuali ke layanan di bagian [Privasi](privasi-keamanan.md#privasi), dan semuanya bisa dimatikan.
