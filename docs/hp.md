# Sambungan HP Android

[← Kembali ke README](../README.md)

Deskmate mendukung sambungan lokal dengan aplikasi pendamping Android lewat Wi-Fi untuk meneruskan notifikasi, OTP, berkas, dan permintaan izin agen.

Setelah tersambung ke Deskmate di laptop:

- Kode OTP dari SMS, email, aplikasi bank, dan aplikasi lain muncul di island dalam bentuk tersamar. **Salin** menyalin kode utuh, lalu kode itu dihapus dari clipboard semenit kemudian.
- Notifikasi pesan dari HP masuk ke Pesan di laptop.
- Berkas dari HP (menu Bagikan › **Kirim ke laptop**, tombol **Pilih berkas** di Integrasi › Laptop, atau aksi **Kirim ke laptop** di Rak) masuk ke `Downloads\Deskmate` dan Rak. Berkas di laptop dikirim ke HP lewat **Kirim berkas** di tab HP atau klik kanan berkas di Rak.
- Permintaan izin agen bisa dijawab dari HP.
- Tab HP menampilkan nama HP, status, dan baterainya.

## Menyambungkan

1. Di laptop: **Pengaturan > HP**, nyalakan **Hubungkan HP**, lalu klik **Buat kode**.
2. Di HP: buka aplikasi pendamping › **Integrasi** › **Laptop** › **Pindai QR**, arahkan kamera ke kode QR di laptop, lalu ketuk **Sambungkan**. Bisa juga dipindai dari aplikasi kamera HP. Tanpa kamera, ketik alamat dan kode 12 karakter yang muncul di laptop.
3. Windows Firewall akan bertanya sekali; izinkan untuk jaringan privat.

Kode berlaku 5 menit dan hangus setelah lima kali salah. Tab HP muncul sendiri setelah sambungan dinyalakan. Selama kode QR tampil, jendela Pengaturan tidak ikut terekam saat screen share.

## Keamanan

Sambungan HP mati sampai dinyalakan, hanya berjalan di jaringan lokal tanpa server perantara, dan Deskmate tidak pernah menjalankan apa pun yang datang dari HP. Rinciannya ada di [Privasi dan keamanan](privasi-keamanan.md).
