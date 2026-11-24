# Deskmate 2.12.0

Rilis 24 November 2026. Fokus rilis ini adalah pet yang lebih hidup dan bisa diajak bermain, teman pet lewat internet, Ingatan yang bisa ditanya, dan installer per pengguna.

## Yang baru

- **Pet bisa dibawa ke mana saja.** Pet kini bisa diseret ke mana pun di layarnya, bukan hanya diangkat sedikit. Saat dilepas, pet jatuh kembali dan memantul sedikit lebih tinggi setelah jatuh jauh. Di mode Dock, pet yang dilepas di luar dock melayang kembali ke atasnya.
- **Area jalan** (`pet.area`) di Pengaturan › Gamifikasi › Pet. **Dock** (bawaan) seperti sebelumnya. **Bawah layar** membuat pet berjalan selebar tepi bawah area kerja; dock yang mengambang menjadi pijakan yang dinaiki dan dituruni dengan melompat, sedangkan dock yang memesan area membuat pet berjalan tepat di atasnya.
- **Bar aksi pet.** Klik pet (kiri atau kanan) untuk membuka tombol besar di atasnya: elus, lompat, kejar, tidur, tanya, chat, dan pengaturan. Selama bar terbuka pet diam dan menoleh ke atas; bar menutup sendiri setelah 2,5 detik tanpa kursor.
- **Pet yang bereaksi** (`pet.react`, menyala secara bawaan). Pet kaget oleh notifikasi, senang saat pekerjaan agent selesai, bergoyang saat musik diputar, tidak berjalan selama fokus, tidur setelah 3 menit tanpa input lalu menggeliat saat Anda kembali, dan menampilkan hati saat dielus. Pet buatan sendiri boleh menambah tag `happy`, `surprised`, `stretch`, `held`, dan `scratch`.
- **Pet menjawab pertanyaan.** Tombol **Tanya** di bar membuka kolom pertanyaan; jawabannya muncul di gelembung pet dan percakapan diingat 10 menit. Dengan Ingatan menyala, jawaban memakai aktivitas terbaru Anda; tanpanya, pet memakai AI utama tanpa data aktivitas.
- **Teman pet online.** Satu PC menjadi host lewat Cloudflare Quick Tunnel dan membagikan undangan `deskmate://…#PIN`; sampai empat pet berbagi satu dock, lengkap dengan chat singkat, emote, dan pet buatan sendiri yang diunduh sesuai kebutuhan lalu diperiksa hash SHA-256-nya. `cloudflared.exe` hanya dijalankan bila bertanda tangan Authenticode dari Cloudflare, Inc. Pengingat minum, istirahat mata, dan peregangan kini diucapkan pet.
- **Ingatan** (mati secara bawaan, Pengaturan › Ingatan). Layanan terpisah mencatat aplikasi aktif, jendela, proyek, URL, teks di layar (UI Automation, OCR hanya sebagai cadangan), dan teks yang disalin ke SQLite di laptop ini, lalu bisa ditanya lewat chat Suruh, tombol pet, atau MCP. Kata sandi, token, jendela privat, dan pengelola kata sandi disaring; ketikan tidak pernah direkam. Aplikasi chat dan email dikenali: pesan dipilah menjadi terkirim, diterima, atau draf, setiap percakapan punya ringkasan, dan janji yang belum ditepati dilacak.
- **Halaman Gamifikasi** di Pengaturan menyatukan Ketikan dan Pet; tombol pengaturan di bar pet langsung membuka bagian Pet.
- **Ikon baterai hijau saat mengisi**, sehingga mudah dibedakan dari ikon putih biasa.
- **Installer per pengguna** (`Deskmate-Setup.exe`). Memasang ke `%LOCALAPPDATA%\Programs\Deskmate` tanpa admin, tercantum di Pengaturan › Aplikasi, dan saat dicopot mengembalikan taskbar serta membersihkan tugas logon, pintasan Start, dan hook agent. Data di `%APPDATA%\Deskmate` hanya dihapus bila Anda setuju. Pembaruan kini menimpa `Deskmate.exe` tanpa menyimpan salinan `Deskmate.previous-*.exe`.

## Performa

- Host teman pet mengirim ke setiap tamu lewat antrean sendiri di luar thread UI, jadi tamu yang macet tidak lagi membekukan Deskmate (2000 siaran dengan satu tamu macet: dari lebih dari 15 detik menjadi 14 ms). Posisi pet kini tiba berurutan.
- Gambar pet bawaan dilukis sekali per hewan dan warna, dan hash pet buatan sendiri disimpan selama berkasnya tidak berubah (20 hash sheet 4 MB: dari 431 ms menjadi 8 ms).
- Pet tetap memakai satu atlas yang diunggah sekali; drag, jatuh, dan lompatan ke dock berjalan pada pacer vsync yang sama.

## Verifikasi

- Build `csc /warn:4` berhasil tanpa warning.
- Feature checks lulus.
- Smoke test: **19/19**.
- Tier 1: **299/300** (termasuk 39 tes pet). Satu-satunya yang gagal adalah uji pemindaian Bluetooth yang memakai radio sungguhan dan tidak selesai dalam 5 detik di mesin uji; `BluetoothScanner.cs` dan tesnya tidak berubah sejak 2.11.1. Tes jumlah kategori Pengaturan diperbarui untuk halaman Ingatan.
- Tier 2: **114/114**.
- Tier 3: **30/30**.
- Tier 4: **19/19**.
- Total Tier 1–4: **462/463**.

## Instalasi

Unduh `Deskmate-Setup.exe` dari aset rilis dan jalankan, atau unduh `Deskmate.exe` lalu ganti binary lama. Pengaturan dan data tetap dipakai. Rilis ini belum ditandatangani; verifikasi SHA-256 terhadap `SHA256SUMS.txt` sebelum menjalankan file.

Mendukung Windows 10 versi 2004 ke atas dan Windows 11 64-bit dengan .NET Framework 4.8.
