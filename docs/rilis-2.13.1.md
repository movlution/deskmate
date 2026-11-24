# Deskmate 2.13.1

Rilis 25 November 2026. Rilis perbaikan untuk Ingatan: tiga celah privasi, satu masalah data, dan kata waktu lewat tengah malam.

## Perbaikan

- **Incognito Chrome dilewati.** Pengaturan Ingatan menjanjikan jendela InPrivate dan Incognito selalu dilewati, tetapi deteksinya hanya membaca judul jendela. Edge menulis `[InPrivate]` di judul, sedangkan Chrome tidak: jendela Incognito tetap berjudul `Halaman - Google Chrome`, sehingga judul, URL, dan teks layarnya ikut tercatat. Kini Ingatan membaca nama panel `BrowserRootView` lewat UI Automation, yang berakhiran `(Incognito)` atau `(Guest)` dalam bahasa antarmuka Chrome, dan melewati jendela itu. Jendela profil lain dan aplikasi Electron tidak terpengaruh.
- **Rapat yang dilupakan tidak kembali.** **Lupakan**, **Hapus semua**, dan batas masa simpan dulu menghapus catatan rapat, padahal berkas transkripnya tetap ada di `rapat\`. Pemindaian berikutnya (tiap 45 detik) menganggapnya rapat baru, lalu mengimpor dan meringkasnya lagi, termasuk mengirimnya ke layanan AI cloud bila itu yang dipilih. Kini rapat yang dilupakan meninggalkan penanda kosong sehingga tidak diimpor ulang. Berkas di `rapat\` tidak disentuh.
- **Lupakan ikut melupakan layar yang terbuka.** Setelah **Lupakan** untuk rentang yang mencakup saat ini, isi layar dan episode yang sedang berjalan masih tersimpan di memori dan masih bisa dibaca lewat `/now`, `/context`, dan pertanyaan ke Ingatan, sementara aktivitas berikutnya tidak masuk episode mana pun. Kini episode, layar, dan draf yang masuk rentang itu ikut dilepas, dan pencatatan berlanjut ke episode baru.
- **Aktivitas tidak lagi masuk ke episode yang salah.** Pencatat layar dan pekerja latar memakai satu koneksi database. ID baris baru dibaca setelah kuncinya dilepas, sehingga sesekali ID milik baris thread lain yang terbaca, dan aktivitas baru tertempel ke episode lama. ID kini dibaca di bawah kunci yang sama dengan penyisipannya.
- **"Tadi" lewat tengah malam.** "Tadi" dan "barusan" dulu berarti sejak pukul 00.00, jadi pertanyaan pukul 00.15 tidak menemukan apa yang dikerjakan setengah jam sebelumnya. Kini kata itu menjangkau paling tidak 6 jam terakhir. Bagian hari yang belum dimulai berarti yang terakhir lewat: "tadi malam" pukul 00.30 adalah semalam, dan "tadi pagi" sebelum subuh adalah pagi kemarin.

## Verifikasi

- Build `csc /warn:4` berhasil tanpa warning.
- Feature checks lulus, dijalankan pukul 00.07 untuk memastikan kata waktu lewat tengah malam. Pemeriksaan Ingatan kini mencakup tombstone rapat, ID insert di bawah thread paralel, nama panel Incognito dan Tamu, Lupakan pada layar yang terbuka, dan "tadi" setelah tengah malam.
- Deteksi Incognito dan Tamu diuji pada jendela Chrome sungguhan: jendela biasa dicatat, Incognito dan Tamu dilewati, aplikasi Electron tidak terpengaruh.
- Smoke test: **19/19**.
- Tier 1: **299/300**. Satu-satunya yang gagal adalah uji pemindaian Bluetooth yang memakai radio sungguhan dan tidak selesai dalam 5 detik di mesin uji, sama seperti di 2.12.0 dan 2.13.0; `BluetoothScanner.cs` dan tesnya tidak berubah sejak 2.11.1.
- Tier 2: **114/114**.
- Tier 3: **30/30**.
- Tier 4: **19/19**.
- Total Tier 1–4: **462/463**.

## Instalasi

Unduh `Deskmate-Setup.exe` dari aset rilis dan jalankan, atau unduh `Deskmate.exe` lalu ganti binary lama. Pengaturan dan data tetap dipakai. Rilis ini belum ditandatangani; verifikasi SHA-256 terhadap `SHA256SUMS.txt` sebelum menjalankan file.

Mendukung Windows 10 versi 2004 ke atas dan Windows 11 64-bit dengan .NET Framework 4.8.
