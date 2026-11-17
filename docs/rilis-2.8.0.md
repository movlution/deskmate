# Deskmate 2.8.0

Rilis 18 November 2026. Fokus rilis ini adalah gambar di catatan tugas, notifikasi yang lebih ramah emoji, autostart yang lebih andal, dan keterbacaan antarmuka.

## Yang baru

- **Gambar di catatan tugas.** Pilih **Tambah gambar** atau tempel screenshot dengan **Ctrl+V**. Gambar disimpan sebagai PNG content-addressed, mendukung Undo/Redo dan tabel, lalu disinkronkan sebagai berkas privat ke folder **Deskmate Task Images** di Google Drive. Cache offline tetap bisa dibuka ketika berkas asli belum tersedia. Google Tasks resmi hanya menampilkan referensi gambar, bukan gambar itu sendiri.
- **Emoji di notifikasi.** Judul dan isi notifikasi merender emoji dengan font fallback yang tepat. Pemotongan teks sekarang mengikuti batas grapheme, jadi emoji, kombining mark, dan karakter multi-codepoint tidak terbelah.
- **Mulai bersama Windows.** Sakelar autostart memakai scheduled task per pengguna `Deskmate-AtLogon`, membaca status yang sebenarnya, dan tetap menyediakan fallback Run-key untuk sistem yang membatasi scheduled task.
- **Keterbacaan dan kaca.** Teks sekunder dan kontrol nonaktif dibuat lebih mudah dibaca, kontras chip tenggat diperkuat, dan target luminance Liquid Glass mode terang dikalibrasi ulang tanpa melonggarkan kontrak kontras.

## Perbaikan

- Sanitasi notifikasi memakai pembersihan satu baris yang aman untuk emoji dan memotong teks pada batas grapheme.
- Liquid Glass mode terang tidak lagi jatuh di bawah target luminance yang sudah ditetapkan.
- Token kontras dialog dan surface diperkuat di sumber token, bukan dengan melonggarkan assertion test.
- Struktur source dipecah menjadi modul yang lebih kecil agar perubahan berikutnya lebih mudah ditinjau dan diuji.

## Verifikasi

- Build `csc /warn:4` berhasil tanpa warning.
- Feature checks gambar catatan dan emoji notifikasi lulus.
- Smoke test: **19/19**.
- Tier 1: **228/228**.
- Tier 2: **112/112**.
- Tier 3: **26/26** interaksi pairwise.
- Tier 4: **19/19** test dalam 13 skenario (S01–S13).
- Total Tier 1–4: **385/385**.

## Instalasi

Unduh `Deskmate.exe` dari aset rilis, lalu ganti binary lama. Pengaturan, tugas, catatan, dan integrasi yang sudah tersambung tetap dipakai. Rilis ini belum ditandatangani, jadi SmartScreen dapat menampilkan peringatan saat pertama dijalankan; verifikasi SHA-256 terhadap checksum rilis sebelum menjalankan binary.

Mendukung Windows 10 versi 2004 ke atas dan Windows 11 64-bit dengan .NET Framework 4.8.
