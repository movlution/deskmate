# Deskmate 2.14.2

Rilis 25 November 2026. Rilis efisiensi untuk Ingatan: layanan pencatatnya kini jauh lebih hemat CPU, memori, dan tulisan disk, tanpa mengubah apa yang dicatat.

## Perbaikan

- **Sidik layar tanpa tangkapan penuh.** Tiap 1,8 detik saat kamu aktif, Ingatan memeriksa apakah isi jendela berubah. Dulu pemeriksaan itu memakai `PrintWindow` seukuran jendela: aplikasi yang dicatat dipaksa menggambar ulang lewat GPU, dan tiap kali dibuat bitmap sekitar 8 MB (1080p) hanya untuk sidik 17×9 piksel. Kini jendela di depan dibaca langsung dari layar ke gambar mini 160×90. Satu pemeriksaan turun dari sekitar 45 ms ke 19 ms, dan aplikasi yang dicatat tidak lagi ikut menggambar ulang. `PrintWindow` hanya dipakai saat jendelanya tertutup jendela lain (misalnya overlay Pet atau Suruh) atau saat OCR memang dibutuhkan.
- **Tidak menulis ulang yang sudah tersimpan.** SQLite menulis ulang seluruh baris pada setiap `UPDATE`, termasuk isi episode yang bisa mencapai 12 KB. Dulu setiap pemeriksaan menulis ulang baris itu walau tidak ada yang berubah. Kini, bila isi, konteks, jenis, judul, dan URL episode sama, hanya waktu akhirnya yang bergerak, dan itu pun disimpan paling sering sekali per 30 detik. Waktu akhir yang tepat tetap ditulis saat episode ditutup; setelah Deskmate berhenti mendadak, waktu akhir episode terakhir bisa mundur paling banyak 30 detik. Pada uji 100 pembacaan layar yang sama, tulisan ke log WAL turun dari 402 KB menjadi 0.
- **Linimasa tanpa isi episode.** Hitungan streak di `/now`, ringkasan hari ini di `/context`, `GET /timeline`, dan rekap harian dulu memuat isi lengkap setiap episode, padahal tidak memakainya. Pada database sungguhan berisi 329 episode, itu sekitar 4 MB teks per panggilan. Kini keempatnya membaca baris tanpa isi: dari sekitar 8 ms menjadi 1,4 ms per panggilan, dengan hasil yang sama persis.

Pada uji berdampingan selama 150 detik dengan layar yang sama, layanan Ingatan versi baru memakai 1,01% satu inti CPU dibanding 1,65%, dan 38 MB memori privat dibanding 67 MB.

## Verifikasi

- Build `csc /warn:4` berhasil tanpa warning.
- Feature checks lulus, termasuk pemeriksaan Ingatan.
- Biaya pemeriksaan layar diukur pada jendela 1080p sungguhan; kepekaan terhadap perubahan setara dengan cara lama (1 berbanding 0 deteksi selama 20 detik layar diam).
- Penulisan ulang yang dilewati diuji dengan 100 pembacaan layar yang sama pada database baru; isi dan konteks episode tetap utuh.
- Linimasa tanpa isi diuji pada salinan database sungguhan: hasil pengelompokan sama persis dengan linimasa lengkap.
- Smoke test: **19/19**.
- Tier 1: **300/300**. Uji pemindaian Bluetooth yang gagal di 2.12.0 sampai 2.14.1 kali ini selesai tepat waktu.
- Tier 2: **114/114**.
- Tier 3: **30/30**.
- Tier 4: **19/19**.
- Total Tier 1–4: **463/463**.

## Instalasi

Unduh `Deskmate-Setup.exe` dari aset rilis dan jalankan, atau unduh `Deskmate.exe` lalu ganti binary lama. Pengaturan dan data tetap dipakai. Rilis ini belum ditandatangani; verifikasi SHA-256 terhadap `SHA256SUMS.txt` sebelum menjalankan file.

Mendukung Windows 10 versi 2004 ke atas dan Windows 11 64-bit dengan .NET Framework 4.8.
