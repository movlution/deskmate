# Deskmate 2.20.1

## Performa & Akurasi Ingatan

- **Pembacaan konteks layar lebih cepat dan akurat.** UI Automation kini mem-batch anak langsung root window dan panel dangkal dalam satu panggilan COM, melewati kontrol dekoratif/ikon kecil, menggabungkan teks inline rapat di baris yang sama, membaca URL langsung dari dokumen browser (termasuk `localhost`), serta membaca jendela 60 baris di sekitar kursor saat teks terlihat tidak tersedia. Anchor error (`CS*`, `TS*`, `ERR_*`, `SQLITE_*`, `MSB*`) dan perintah terminal diprioritaskan dari isi utama.
- **Database SQLite lebih optimal.** Menambahkan LRU prepared statement cache, tuning PRAGMA (`temp_store=MEMORY`, `cache_size`, `mmap_size`, `PRAGMA optimize`, `wal_checkpoint`), schema versioning (`user_version`), serta indeks B-tree dan partial index (`episodes_end_start`, antrean ringkasan/embedding/digest, inbox, dan event per episode). Transaksi capture, ringkasan, pesan, dan pencarian hibrida 2-tahap (`Lite` → batch hydrate top-K) memangkas baca-tulis disk secara signifikan.
- **OCR & fingerprinting lebih ringan.** Instance `OcrEngine` di-cache dengan proteksi thread-safe dan pengurutan baris atas-ke-bawah; buffer bitmap `Stamp`/`ScreenStamp` dipakai ulang untuk menekan alokasi memori.

## Pengaturan & Antarmuka

- **Pengaturan dan kontrol lebih rapi.** Pembaruan navigasi Pengaturan dengan beranda (`PrefsHub`) dan submenu yang lebih terstruktur, penyempurnaan tombol/picker/panel, tampilan notifikasi, dan sinkronisasi sumber.
