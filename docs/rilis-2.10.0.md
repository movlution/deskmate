# Deskmate 2.10.0

Rilis 22 November 2026. Fokus rilis ini adalah material kaca baru, keterbacaan panel, dan Dock yang lebih andal di atas aplikasi modern.

## Yang baru

- **Kaca Kalem** (`calm`). Material kelima: warna diambil dari wallpaper, saturasi diredam, tanpa kilau, dengan garis tepi tipis yang rata. Jendela Kit ikut diwarnai dengan hue wallpaper pada luminans tema.
- **Jam di kepala panel.** Panel terbuka menampilkan jam (mengikuti `clock.24`) dan tanggal di pojok kiri atas. Jam disembunyikan bila ruang ke tab kurang dari 56 px atau Ringkasan sudah menampilkan jam besar.
- **Ikon notifikasi asli.** Notifikasi Phone Link memakai `appLogoOverride` sehingga tiap aplikasi Android tampil dengan logonya; notifikasi situs (`<browser>!https://...`) menampilkan ikon browser.
- **Editor tugas dua kolom.** Editor tugas dan catatan disusun ulang menjadi dua kolom, sakelar dan kontrol segmen bergerak berbasis waktu, dan pencarian Pengaturan memakai cache judul/detail.

## Tampilan

- Panel Clear kini punya lantai baca (`GlassRecipe.PanelScrim`) agar teks tetap sekitar 4,5:1 di atas jendela terang maupun gelap. Pill notch yang tertutup tetap transparan.
- Tab berada dalam satu kapsul; ikon tab yang tidak aktif sedikit lebih kontras, dan judul pratinjau saat hover lebih tenang.
- Dock memakai satu kurva (0,36 × tinggi bar) dengan plate bersarang pada inset sisi dan atas yang sama. Grup Pengaturan memakai `RadiusList` 14.

## Perbaikan

- Dock kini muncul saat diarahkan di atas aplikasi maksimal ber-title bar kustom (ChatGPT, Electron, WinUI). Jendela yang maksimal dan masih punya `WS_THICKFRAME` tidak lagi dianggap layar penuh; layar penuh sungguhan (game, F11) tetap menyembunyikan Dock.

## Verifikasi

- Build `csc /warn:4` berhasil tanpa warning.
- Feature checks lulus.
- Smoke test: **19/19**.
- Tier 1: **260/260**.
- Tier 2: **114/114**.
- Tier 3: **30/30**.
- Tier 4: **19/19**.
- Total Tier 1–4: **423/423**.

## Instalasi

Unduh `Deskmate.exe` dari aset rilis, lalu ganti binary lama. Pengaturan dan data tetap dipakai. Rilis ini belum ditandatangani; verifikasi SHA-256 terhadap `SHA256SUMS.txt` sebelum menjalankan binary.

Mendukung Windows 10 versi 2004 ke atas dan Windows 11 64-bit dengan .NET Framework 4.8.
