# Deskmate 2.9.0

Rilis 22 November 2026. Fokus rilis ini adalah dukungan banyak layar, Pengaturan yang ditata ulang, dan respons antarmuka yang lebih lancar saat sistem sibuk.

## Yang baru

- **Notch di semua layar.** Di **Island › Ukuran dan layar › Layar**, pilih **Semua layar** (`monitor.all`). Monitor lain menampilkan notch ringkas; klik atau arahkan kursor untuk memindahkan panel ke sana. Hanya satu panel aktif pada satu waktu.
- **Ikon tersembunyi dari setiap Dock.** Saat Dock menggantikan taskbar, tombol panah di setiap monitor membuka daftar ikon tersembunyi tepat di atas Dock itu (`dockbar.tray`).
- **Pengaturan yang ditata ulang** mengikuti pola System Settings di macOS. Kunci `settings.ini` tidak berubah.
- **Kaca dan tema.** Kaca wall.sync mewarnai mengikuti wallpaper, Dock naik lebih cepat dan tenang, dan tema mengikuti Windows.

## Performa

- Deskmate kini menaikkan dirinya ke prioritas proses **Normal** bila diluncurkan Task Scheduler pada BelowNormal, dan task `Deskmate-AtLogon` baru memakai `<Priority>4</Priority>`. Animasi tidak lagi kalah oleh aplikasi lain saat CPU sibuk.
- Animasi notch memakai skala bilinear selama bergerak (kualitas penuh kembali saat diam): sekitar 36 fps menjadi 66–100 fps saat panel bergerak.
- Latar cover di halaman Musik dipudarkan sekali per cover, menghapus lonjakan frame hingga ~80 ms.
- Start tidak lagi menunggu thread bantu saat dibuka (1–450 ms menjadi 0–5 ms). Katalog aplikasi, ikon, dan Terbaru dimuat di latar setelah startup, sehingga pembukaan pertama langsung lengkap. Ikon memudar bersama panel.
- `DESKMATE_FPS=1` kini juga mencatat waktu frame Dock dan waktu buka Start, tray, dan sheet.

## Perbaikan

- Glyph baris Pengaturan kembali memakai `KitInk.Text` agar kontras mode terang terjaga.
- Strip hover tepi Dock minimal 6 DIP agar auto-hide lebih mudah dipicu; test diperbarui sesuai perilaku ini.

## Verifikasi

- Build `csc /warn:4` berhasil tanpa warning.
- Feature checks lulus.
- Smoke test: **19/19**.
- Tier 1: **258/258**.
- Tier 2: **112/112**.
- Tier 3: **30/30**.
- Tier 4: **19/19**.
- Total Tier 1–4: **419/419**.

## Instalasi

Unduh `Deskmate.exe` dari aset rilis, lalu ganti binary lama. Pengaturan dan data tetap dipakai. Rilis ini belum ditandatangani; verifikasi SHA-256 terhadap `SHA256SUMS.txt` sebelum menjalankan binary.

Mendukung Windows 10 versi 2004 ke atas dan Windows 11 64-bit dengan .NET Framework 4.8.
