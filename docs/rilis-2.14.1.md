# Deskmate 2.14.1

Rilis 25 November 2026. Penjaga beban: Deskmate memberi tahu saat aplikasi yang tidak sedang kamu pakai menghabiskan CPU atau RAM, dan menawarkan cara meredakannya.

## Baru

- **Penjaga beban.** Aplikasi di latar yang memakai 25% CPU atau lebih selama 2 menit muncul di island beserta nama dan pemakaiannya. Saat memori sudah terpakai di atas 88%, aplikasi di latar yang memakai 30% RAM selama 1 menit juga muncul. Semua proses satu aplikasi dihitung bersama, jadi Chrome dengan puluhan prosesnya tampil sebagai satu aplikasi.
  - **Mode hemat** sama dengan Efficiency mode di Task Manager: aplikasinya diperlambat selama di latar, dan kembali normal begitu kamu membukanya lagi.
  - **Tutup** meminta aplikasinya menutup jendelanya, jadi dialog simpan tetap muncul. Tidak ada aplikasi yang dipaksa mati.
  - **Abaikan** mendiamkan aplikasi itu sampai Deskmate dibuka ulang. Aplikasi yang sudah pernah muncul baru boleh muncul lagi setelah 30 menit.
- Aplikasi yang sedang di depan, proses Windows (Explorer, dwm, svchost, dan sejenisnya), serta alat yang dijalankan Deskmate sendiri tidak pernah ditandai. Penjaga beban diam selama Senyap, rapat, dan fokus.
- Pemakaian dibaca tiap 5 detik lewat satu panggilan Windows untuk semua proses. Fitur ini menyala secara bawaan; matikan di **Pengaturan › Perangkat & HP › Penjaga beban**.

## Verifikasi

- Build `csc /warn:4` berhasil tanpa warning.
- Feature checks lulus, termasuk pemeriksaan baru Penjaga beban (`guard_check`).
- Smoke test: **19/19**.
- Tier 1: **299/300**. Satu-satunya yang gagal adalah uji pemindaian Bluetooth yang memakai radio sungguhan dan tidak selesai dalam 5 detik di mesin uji, sama seperti di 2.12.0 sampai 2.14.0; `BluetoothScanner.cs` dan tesnya tidak berubah di rilis ini.
- Tier 2: **114/114**.
- Tier 3: **30/30**.
- Tier 4: **19/19**. Pada putaran pertama, uji halo media (S10) sekali gagal di pemeriksaan napas animasi antar-frame; uji itu lulus 3 kali berturut-turut saat dijalankan sendiri dan Tier 4 lulus penuh saat diulang.
- Total Tier 1–4: **462/463**.

## Instalasi

Unduh `Deskmate-Setup.exe` dari aset rilis dan jalankan, atau unduh `Deskmate.exe` lalu ganti binary lama. Pengaturan dan data tetap dipakai. Rilis ini belum ditandatangani; verifikasi SHA-256 terhadap `SHA256SUMS.txt` sebelum menjalankan file.

Mendukung Windows 10 versi 2004 ke atas dan Windows 11 64-bit dengan .NET Framework 4.8.
