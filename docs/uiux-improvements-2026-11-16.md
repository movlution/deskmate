# Perbaikan UI/UX Deskmate — 16 November 2026

## Ruang lingkup

Audit terarah pada kontrol bersama, panduan awal, dan aset identitas aplikasi. Mempertahankan perubahan lokal yang sudah ada pada dock, tugas, catatan, dan pengaturan. Ini bukan sertifikasi seluruh fitur aplikasi atau semua integrasi eksternal.

## Temuan dan implementasi

| Temuan dari kode | Dampak | Perbaikan |
| --- | --- | --- |
| KitSegmented menerima semua tombol mouse pada MouseUp | Klik kanan dapat mengubah pengaturan | Hanya klik kiri memilih; kontrol menerima fokus |
| Aksi aksesibilitas tetap memanggil Pick ketika disabled | Pengaturan nonaktif dapat berubah | Guard Enabled pada jalur perubahan, state Unavailable untuk pembaca layar |
| Item aksesibilitas dibuat tanpa memeriksa indeks | Enumerasi indeks invalid/daftar kosong dapat gagal | Mengembalikan null untuk indeks di luar batas |
| Navigasi hanya Left/Right dan tidak mengonsumsi keypress | Navigasi keyboard kurang lengkap | Home/End, konsumsi tombol navigasi, pemulihan indikator fokus |
| Segmen disabled tampak sama dengan aktif | Status interaksi tidak jelas | Warna pilihan dan teks mengikuti token disabled/sekunder |
| Panduan tidak mengenalkan dock atau ruang kerja lokal | Fitur utama sulit ditemukan | Dua langkah baru dengan jalur menu nyata; terjemahan Inggris |
| Tombol panduan setinggi 30px dan selalu satu baris | Target kecil dan potensi tabrakan terjemahan | Tinggi 36px; baris tambahan ketika ruang tidak cukup |
| Pembulatan animasi menghasilkan langkah nol pada jarak 2px | Timer glide dapat terus berjalan | Minimal perpindahan satu piksel sampai tujuan |
| Identitas visual belum hadir pada pembuka tur | Panduan terasa terpisah dari aplikasi | Ikon aplikasi di pembuka, ruang judul dan isi disesuaikan |

## Aset

- `assets/brand/deskmate-master.png`: hasil image generation asli, alpha transparan.
- `assets/brand/deskmate-{size}.png`: 16, 20, 24, 32, 40, 48, 64, 128, 256px.
- `src/Deskmate.ico`: ICO multi-resolution untuk executable dan resource aplikasi.
- `src/Deskmate.png`: preview README 256px.
- Aset lama disimpan sebagai `assets/brand/deskmate-previous.*`.
- `assets/brand/build-icon.ps1`: konversi deterministik dengan System.Drawing.

Identitas ungu, ivory, dan mint dipertahankan. Ikon fungsi tetap memakai keluarga ikon native yang sudah ada agar terbaca pada ukuran kecil.

## Validasi

- `build.cmd Deskmate.UIUX.exe`: berhasil, empat compiler warning pada kode di luar perubahan ini.
- `test_uiux_controls.ps1`: 7/7 lulus.
- Feature 7, Tier 1 + Tier 2: 10/10 lulus.
- Render aktual KitSegmented diperiksa pada tema terang/gelap dan enabled/disabled: `assets/brand/controls-preview.png`.
- Pemuatan ICO .NET diuji pada 16–128px. ICO juga berisi frame 256px; pemilih Icon milik .NET Framework memilih 128px untuk permintaan 256px, sehingga ukuran tersebut bukan klaim pengujian pemilihan frame .NET.
- Jendela aplikasi/tur tidak muncul dalam inventaris alat desktop, termasuk pada profil uji terisolasi. Interaksi tur end-to-end, skala DPI tinggi, dan multi-monitor belum diverifikasi visual.
- Build hasil disediakan sebagai `Deskmate.UIUX.exe`; executable utama yang sedang berjalan tidak diganti atau direstart. Tidak ada publikasi rilis.

## Tindak lanjut

Uji tur lengkap pada monitor normal dan HiDPI, termasuk teks Inggris dan perpindahan Back/Next. Setelah itu, perluas evaluasi ke alur tugas/catatan dan pengaturan pada perubahan lokal terbaru; jangan memakai hasil 17 tes terarah ini sebagai bukti seluruh aplikasi bebas masalah.
