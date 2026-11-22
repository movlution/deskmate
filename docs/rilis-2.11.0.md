# Deskmate 2.11.0

Rilis 23 November 2026. Fokus rilis ini adalah pet di dock: hewan piksel kecil yang menemani di tepi bawah layar, ringan, dan bisa diganti dengan gambar buatan sendiri.

## Yang baru

- **Pet di dock** (`pet.on`, mati secara bawaan). Kucing atau anjing piksel berjalan, duduk, dan tidur di atas dock; tanpa dock, di tepi bawah area kerja. Pet ikut naik turun bersama dock yang bersembunyi, berhenti dan menoleh saat disentuh kursor, serta melompat dan bangun saat diklik. Bagian transparannya tembus klik.
- **Pengaturan pet** di Pengaturan › Dock & Taskbar: hewan, warna bulu (oranye, cokelat, abu-abu, hitam, krem), ukuran (32, 48, atau 64 px dikali skala layar), dan kecepatan jalan 50–200%. Irama langkah kaki mengikuti kecepatannya.
- **Pet dari Aseprite.** Satu folder per pet di `%APPDATA%\Deskmate\pets\<nama>\` berisi `sheet.png` dan `pet.json` dari **Export Sprite Sheet** (JSON Array atau Hash). Tag `walk` wajib; `idle`, `sit`, dan `sleep` boleh tidak ada. Durasi frame, arah `pingpong`/`reverse`, frame yang di-trim, dan transparansi sebagian ikut dibaca. Pet yang gagal dimuat diganti kucing, dan alasannya tampil di Pengaturan.

## Performa

- Pet punya jendela berlapis sendiri, jadi dock tidak digambar ulang saat pet bergerak. Semua frame diunggah sekali; saat berjalan, frame mengikuti vsync lewat `DockPacer`, dan pergeseran tanpa ganti gambar hanya memindahkan jendela.
- Saat diam atau tidur, pet hanya bangun ketika gambarnya perlu berganti. Pengukuran di luar layar: CPU sekitar 0,19% saat berjalan dan 0,007% saat tidur (12 core), dengan jeda frame terburuk 18 ms.
- Pet menyingkir saat aplikasi layar penuh atau Deskmate disembunyikan, dan diam di tempat saat **Kurangi gerak** menyala.

## Verifikasi

- Build `csc /warn:4` berhasil tanpa warning.
- Feature checks lulus.
- Smoke test: **19/19**.
- Tier 1: **275/275** (termasuk 15 tes pet).
- Tier 2: **114/114**.
- Tier 3: **30/30**.
- Tier 4: **19/19**.
- Total Tier 1–4: **438/438**.

## Instalasi

Unduh `Deskmate.exe` dari aset rilis, lalu ganti binary lama. Pengaturan dan data tetap dipakai. Rilis ini belum ditandatangani; verifikasi SHA-256 terhadap `SHA256SUMS.txt` sebelum menjalankan binary.

Mendukung Windows 10 versi 2004 ke atas dan Windows 11 64-bit dengan .NET Framework 4.8.
