# Deskmate 2.15.0

Rilis 25 November 2026. Dock kini bisa menahan berkas, gambar, dan teks; pet bisa dilempar; dan AI Deskmate bisa membaca serta mengubah hampir semua fitur. Ada juga perbaikan untuk mode sembunyi Dock, halaman Pengaturan, dan Ingatan.

## Baru

- **Rak di Dock.** Saat berkas, gambar, atau teks diseret ke atas dock, slot rak muncul di ujung barisan aplikasi. Yang dijatuhkan di sana ditahan (dan ikut disalin ke Rak di island), lalu bisa diseret dari slot itu ke kolom unggah, chat, atau email. Menyorot ikon aplikasi sambil menyeret keluar tetap memunculkan jendelanya. Nyalakan atau matikan di **Pengaturan › Dock › Rak di Dock**.
- **Pet bisa dilempar.** Lepas pet sambil menyeret dengan cepat, dan kecepatan seretan itu menjadi lemparan: pet melayang dalam lengkung gravitasi, memantul paling banyak dua kali, meluncur, lalu melompat kembali ke jalurnya. Tinggi pet tetap mulus saat melintas ke atas atau keluar dari dock yang mengambang. Pendaratan keras membuat pet kaget, pendaratan pelan membuatnya senang. Lepasan pelan tetap jatuh lurus seperti sebelumnya.
- **AI mengatur semua fitur.** Suruh kini punya alat untuk membaca dan mengubah tugas, catatan, kalender, pengingat, timer, media, sistem, rak dan clipboard, integrasi, utilitas, dan pengaturan (`read_deskmate` dan `manage_*`). Untuk mengubah atau menghapus, AI membaca item lebih dulu supaya memakai ID yang benar. Bertanya ke pet kini memakai alur alat yang sama, termasuk Ingatan lewat `read_deskmate(activity)` saat Ingatan menyala.

## Perbaikan

- **Dock kembali saat sembunyi dimatikan.** Mengganti **Sembunyikan dock** dari Otomatis atau Saat tertutup ke Selalu tampil dulu membuat dock yang sedang tersembunyi tetap hilang, karena di mode itu tepi bawah tidak lagi memunculkannya. Kini dock langsung naik.
- **Pengaturan tidak lagi melompat.** Mengubah pilihan yang membangun ulang halaman dulu mengembalikan halaman ke atas dan menggambar ulang baris demi baris. Kini halaman tetap di posisi gulir yang sama dan digambar sekali setelah selesai.
- **Ingatan tenang di jendela yang sibuk.** Jendela yang terus berubah saat kamu tidak menyentuh apa pun (Task Manager, video, log, timer) dulu dipindai ulang dengan UI Automation atau OCR tiap 1,8 detik. Kini setiap pembacaan yang berubah tanpa interaksi menggandakan jedanya, sampai 15 detik; input, pindah jendela, fokus, gulir, permintaan konteks, atau layar yang diam mengembalikan ke tempo biasa.
- **Ringkasan AI di luar jalur cepat.** Episode di bawah 2 menit kini memakai ringkasan terstruktur tanpa model, dan panggilan model (ringkasan, rangkuman chat, rekap harian) menunggu sampai kamu diam 45 detik. Sebelumnya Codex CLI dijalankan sekali per episode, sekitar 280 kali sehari.

## Verifikasi

- Build `csc /warn:4` berhasil tanpa warning.
- Smoke test: **19/19**.
- Tier 1: **308/308**, termasuk uji baru untuk rak Dock, lemparan pet, dan alat AI.
- Tier 2: **114/114**.
- Tier 3: **30/30**.
- Tier 4: **19/19**.
- Total Tier 1–4: **471/471**.
- Dicek langsung di aplikasi: dock muncul lagi saat diganti ke Selalu tampil, dan halaman Pengaturan tetap di tempat serta tidak berkedip.

## Instalasi

Unduh `Deskmate-Setup.exe` dari aset rilis dan jalankan, atau unduh `Deskmate.exe` lalu ganti binary lama. Pengaturan dan data tetap dipakai. Rilis ini belum ditandatangani; verifikasi SHA-256 terhadap `SHA256SUMS.txt` sebelum menjalankan file.

Mendukung Windows 10 versi 2004 ke atas dan Windows 11 64-bit dengan .NET Framework 4.8.
