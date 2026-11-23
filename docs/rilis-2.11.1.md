# Deskmate 2.11.1

Rilis 23 November 2026. Rilis perbaikan untuk 2.11.0: dock tetap di atas aplikasi, dan pengaturan pet tidak lagi menggulir halaman.

## Perbaikan

- **Dock selalu di atas.** Dock, pet, dan notch di layar lain bisa tenggelam di bawah jendela aplikasi biasa walaupun bit `WS_EX_TOPMOST`-nya masih terpasang. Sebelumnya dock hanya menaikkan diri saat ditahan (`hold`), jadi sekali tenggelam ia tetap di bawah. Kini `Native.KeepTopmost` memeriksa jendela di atasnya setiap kali jendela depan berganti (`Nudge`) dan sekali per detik (`Tick`); bila ada jendela biasa yang terlihat di atasnya, dock kembali ke pita topmost. Pet ikut naik karena dimiliki dock, dan kaca latar disusun ulang tepat di bawahnya. Saat urutannya sudah benar, tidak ada perubahan z-order, jadi menu dan kartu dock tidak tertutup. Replika notch di monitor lain memakai pemeriksaan yang sama.
- **Pengaturan pet tidak melompat.** Mengganti hewan (misalnya Kucing ke Anjing), warna bulu, atau sakelar **Pet di dock** dulu membangun ulang seluruh halaman lewat `Relayout()` sehingga gulirannya kembali ke atas. Kini hanya baris pet yang diperbarui di tempat, dan tinggi barisnya dicadangkan supaya teks status tidak menggeser baris lain.

## Verifikasi

- Build `csc /warn:4` berhasil tanpa warning.
- Feature checks lulus.
- Smoke test: **19/19**.
- Tier 1: **276/276**.
- Tier 2: **114/114**.
- Tier 3: **30/30**.
- Tier 4: **19/19**.
- Total Tier 1–4: **439/439**.
- Uji z-order di luar layar: jendela topmost yang ditenggelamkan di bawah jendela biasa naik kembali, dan urutan yang sudah benar tidak disentuh.

## Instalasi

Unduh `Deskmate.exe` dari aset rilis, lalu ganti binary lama. Pengaturan dan data tetap dipakai. Rilis ini belum ditandatangani; verifikasi SHA-256 terhadap `SHA256SUMS.txt` sebelum menjalankan binary.

Mendukung Windows 10 versi 2004 ke atas dan Windows 11 64-bit dengan .NET Framework 4.8.
