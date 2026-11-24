# Deskmate 2.14.0

Rilis 25 November 2026. Codex CLI dan Claude Code bisa dipakai sebagai layanan AI tanpa kunci API, model bisa dipilih dari daftar, dan Ingatan jadi lebih cepat, lebih jeli mencari, dan lebih hati-hati menyimpan fakta jangka panjang.

## Baru

- **Codex CLI dan Claude Code sebagai layanan AI.** Dua pilihan baru di **Pengaturan › AI** memakai CLI `codex` atau `claude` yang sudah masuk di laptop ini. Deskmate tidak butuh kunci API dan tidak pernah memegang token. CLI dicari di PATH dan folder pemasangan yang umum, lalu dicek ulang tiap 30 detik. Pengaturan menampilkan apakah CLI ditemukan, lengkap dengan tombol **Tes** dan **Cari lagi**. Kedua CLI tidak menyediakan embedding, jadi Ingatan mencari dengan kata kunci saja saat memakainya.
- **Alat Deskmate lewat CLI.** Lewat kedua CLI itu, Suruh Deskmate kini bisa memasang pengingat, menyalakan timer, membaca tugas dan Ingatan, serta membuka fitur, sama seperti lewat layanan API. Daftar alat dikirim di prompt, dan CLI menjawab dalam bentuk JSON yang ketat (`--json-schema` di Claude Code, `--output-schema` di Codex). Nama alat yang tidak dikenal dibuang, dan jawaban yang tidak mengikuti bentuk itu tetap ditampilkan sebagai teks.
- **Pilih model dari daftar.** Baris Model di **Pengaturan › AI** punya tombol **Pilih**. Daftarnya diambil dari layanan (`/models` untuk layanan HTTP, `codex debug models` untuk Codex CLI, daftar alias dan ID tetap untuk Claude Code), dengan pencarian banyak kata, navigasi keyboard, tanda centang di model yang sedang dipakai, dan pilihan "pakai nama ini" untuk nama yang tidak ada di daftar.

## Ingatan

- **Lebih cepat.** Pengecekan duplikat di setiap pembacaan layar memakai indeks, bukan memindai seluruh tabel. Penulisan episode, retensi, dan Lupakan masing-masing berjalan dalam satu transaksi, dan indeks teks episode yang masih terbuka ditulis ulang paling sering tiap 30 detik (dan selalu sebelum pencarian). Satu siklus pembacaan turun dari 4,9 ms menjadi 1,7 ms pada data uji beberapa minggu.
- **Pencarian menemukan lebih banyak.** Sampai 8 potongan per episode di-embed, bukan hanya 2.400 karakter pertama, dan potongan terbaik yang menentukan skor. Vektor disimpan di RAM dalam bentuk int8 (paling banyak 64 MB): 12.000 potongan memakan 9,9 MB dan dinilai dalam sekitar 15 ms. Batas kemiripan menyesuaikan sebaran skor tiap pertanyaan. Kata 4 huruf ke atas dicocokkan sebagai awalan, imbuhan umum bahasa Indonesia dibuang dari kueri, dan indeks trigram membuat "kirim" menemukan "dikirim". Episode yang baru dan berbobot (panjang, rapat, sudah diringkas) mendapat sedikit dorongan, tetapi relevansi tetap yang utama.
- **Fakta jangka panjang diubah sedikit demi sedikit.** Setiap fakta menyimpan jenis, tingkat keyakinan, berapa kali terkonfirmasi, kapan pertama dan terakhir terlihat, serta hari asalnya. Rekap harian mengubahnya lewat operasi tambah, perbarui, kuatkan, dan arsipkan, bukan menulis ulang semuanya. Satu rekap paling banyak menambah 10 dan mengarsipkan 5 fakta. Fakta yang tidak pernah terkonfirmasi memudar perlahan, dan jumlah fakta aktif dibatasi 80. Setiap pertanyaan membawa 12 fakta: 3 inti, lalu yang berkaitan dengan pertanyaan. `GET /facts` kini juga mengembalikan `detail`.
- **Detail yang berubah disimpan sebagai fakta baru.** "Rapat tim setiap Kamis pagi" tidak lagi menguatkan fakta "Rapat tim setiap Senin pagi", dan "game Godot" tidak lagi menguatkan "game Unity". Kalimat yang hanya ditambah kata tetap dianggap fakta yang sama. Fakta yang diperbarui menjadi sama dengan fakta lain digabung menjadi satu, bukan dibiarkan kembar.
- **Hari yang dilupakan disusun ulang.** Lupakan menghapus rekap hari yang tersentuh dan fakta yang hanya berasal dari hari itu. Kemudian rekap disusun lagi dari aktivitas yang tersisa dan diolah lagi menjadi fakta, jadi apa yang diajarkan jam-jam lain kembali. Hari yang tertunda diolah dari yang terlama, sehingga koreksi dari hari yang lebih baru tidak tertimpa rekap yang lebih lama.
- **Model yang tidak terjangkau.** Kalau layanan AI tidak bisa dihubungi, pekerjaan berpikir dijeda 10 menit, bukan menunggu batas waktu di setiap putaran. Episode tetap menunggu diringkas, bukan langsung diberi ringkasan cadangan setelah satu kali gagal.

## Verifikasi

- Build `csc /warn:4` berhasil tanpa warning.
- Feature checks lulus. Pemeriksaan Ingatan kini mencakup jalur cepat, kualitas pencarian, operasi fakta, detail fakta yang berubah, penggabungan fakta kembar, dan penanda rekap yang dihapus saat Lupakan.
- Smoke test: **19/19**.
- Tier 1: **299/300**. Satu-satunya yang gagal adalah uji pemindaian Bluetooth yang memakai radio sungguhan dan tidak selesai dalam 5 detik di mesin uji, sama seperti di 2.12.0 sampai 2.13.1; `BluetoothScanner.cs` dan tesnya tidak berubah di rilis ini.
- Tier 2: **114/114**.
- Tier 3: **30/30**.
- Tier 4: **19/19**.
- Total Tier 1–4: **462/463**.

## Instalasi

Unduh `Deskmate-Setup.exe` dari aset rilis dan jalankan, atau unduh `Deskmate.exe` lalu ganti binary lama. Pengaturan dan data tetap dipakai. Rilis ini belum ditandatangani; verifikasi SHA-256 terhadap `SHA256SUMS.txt` sebelum menjalankan file.

Mendukung Windows 10 versi 2004 ke atas dan Windows 11 64-bit dengan .NET Framework 4.8.
