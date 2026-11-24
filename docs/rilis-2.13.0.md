# Deskmate 2.13.0

Rilis 24 November 2026. Fokus rilis ini adalah island yang bisa digeser dengan mouse dan Ingatan yang membaca layar lebih cepat dan lebih rapi.

## Yang baru

- **Geser island dengan mouse** (`place.drag`, menyala secara bawaan). Tekan island compact di bagian kosongnya lalu seret untuk memindahkannya di sepanjang tepi layar; posisinya tersimpan saat dilepas. Rentang **Geser** kini ±100% dan **Jarak dari pojok** sampai 320 px. Sakelarnya ada di Pengaturan › Island › Posisi.
- **Ingatan lebih peka layar.** Pergantian jendela, judul atau tab, fokus kontrol, dan akhir scroll memicu pembacaan dalam beberapa ratus milidetik. Selama Anda di jendela yang sama, sidik gambar dua zona diperiksa tiap sekitar 1,8 detik (5 detik saat diam), dan layar disegarkan seketika saat Anda bertanya.
- **Konteks layar yang tersusun.** Teks dari UI Automation kini dipisah menjadi fokus aktif, heading, isi utama sesuai urutan baca, navigasi samping, dan jangkar (berkas, kode, perintah). Episode menyimpan jejak langkah, poin kunci, dan kategori otomatis, dan teks yang baru muncul saat scroll digabung tanpa menggandakan baris lama.
- **OCR di memori.** OCR bawaan Windows kini berjalan tanpa berkas sementara, dan tetap hanya dipakai bila aplikasi tidak memberi teks.
- **Jawaban dari layar aktif tanpa AI.** Ingat dan Chat tetap bisa menjawab tentang layar yang sedang terbuka walaupun layanan AI mati, dan baris Status di Pengaturan › Ingatan menampilkan aplikasi serta fokus saat ini.
- **API dan MCP.** Endpoint baru `GET /context`, pengelompokan terstruktur (`structured`) di `/search` dan `/timeline`, konteks layar di `/now` dan `/status`, serta tool MCP `get_screen_context`.

## Perbaikan

- Pengaturan dimuat ke peta sementara lalu ditukar sekaligus, sehingga bagian lain tidak pernah melihat pengaturan kosong atau setengah termuat. Bila pembacaan `settings.ini` gagal, nilai sebelumnya tetap dipakai.

## Verifikasi

- Build `csc /warn:4` berhasil tanpa warning.
- Feature checks lulus.
- Smoke test: **19/19**.
- Tier 1: **299/300**. Satu-satunya yang gagal adalah uji pemindaian Bluetooth yang memakai radio sungguhan dan tidak selesai dalam 5 detik di mesin uji (juga gagal saat diulang, sama seperti di 2.12.0); `BluetoothScanner.cs` dan tesnya tidak berubah sejak 2.11.1.
- Tier 2: **114/114**.
- Tier 3: **30/30**.
- Tier 4: **19/19**.
- Total Tier 1–4: **462/463**.

## Instalasi

Unduh `Deskmate-Setup.exe` dari aset rilis dan jalankan, atau unduh `Deskmate.exe` lalu ganti binary lama. Pengaturan dan data tetap dipakai. Rilis ini belum ditandatangani; verifikasi SHA-256 terhadap `SHA256SUMS.txt` sebelum menjalankan file.

Mendukung Windows 10 versi 2004 ke atas dan Windows 11 64-bit dengan .NET Framework 4.8.
