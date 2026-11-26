# Deskmate 2.16.0

Rilis 26 November 2026. Ruang teman pet kini terasa seperti satu ruang: chat bersama dengan riwayat, nama pemilik di atas setiap pet, dan teman bisa rusuh di layar satu sama lain. Ada pet bawaan ketiga, dan klik di Dock kini meminimalkan aplikasi seperti taskbar Windows. Tray di Dock juga tidak lagi kehilangan ikon.

## Baru

- **Chat room bersama.** Jendela chat pet kini menampilkan semua pesan di ruang di atas kotak ketik: pengirim, jam, dan isi, pesanmu sendiri berwarna amber, ditambah catatan saat teman masuk atau keluar. Daftar mengikuti pesan baru kecuali kamu sedang menggulir ke atas. Setelah mengirim, jendela tetap terbuka dengan kotak kosong, dan setiap pesan tetap muncul sebagai gelembung di atas pet pengirimnya. Riwayat hanya disimpan di memori (100 pesan terakhir) dan dikosongkan saat ruang dimulai atau berakhir; teman yang masuk belakangan melihat pesan sejak ia masuk.
- **Nama di atas setiap pet.** Selama ruang tersambung, setiap pet memakai label nama kecil: pet teman menampilkan nama teman itu, pet milikmu menampilkan nama ruangmu. Label tembus klik, tidak pernah mengambil fokus, mengikuti pet, dan menyingkir saat gelembung chat, bilah aksi, atau pengingat muncul.
- **Rusuh lewat pet.** Klik kanan pet teman untuk **Bakar**, **Retakkan**, **Lempar cat**, atau **Bekukan**. Teman melihat efek tembus klik selama 6 detik yang tidak mengambil fokus. Efek dilewati saat layar dibagikan, Deskmate tersembunyi, atau **Kurangi gerak** menyala, hanya satu yang tampil sekaligus, dan bisa dimatikan lewat **Izinkan teman rusuh di layar** di **Pengaturan › Dock & Taskbar**. Setiap teman hanya bisa rusuh sekali tiap 30 detik ("Rusuh lagi dalam 12 dtk"), dan aturan itu juga dijaga di sisi penerima.
- **Chao, pet ketiga.** Di samping kucing dan anjing, kini ada roh fantasi mungil berwarna pastel dengan kepala tetes air, mata besar berkilau, sayap kecil, dan bola emosi yang melayang. Kelima pilihan warna punya palet pastelnya sendiri, dan teman di ruang ikut melihatnya.
- **Klik Dock meminimalkan.** Klik ikon aplikasi yang jendelanya sedang di depan kini meminimalkan jendela itu, seperti taskbar Windows. Aplikasi yang tertutup jendela lain atau sedang diminimalkan tetap dibawa ke depan. Pindah antarjendela lewat pratinjau saat kursor diarahkan; menu klik kanan tetap hanya memfokuskan.
- **Geser pet teman.** Pet teman yang menutupi sesuatu di layar bisa diseret menyamping. Saat dilepas, ia jatuh lagi dan terus berjalan dari tempat ia dijatuhkan. Hanya berlaku di layarmu.

## Perbaikan

- **Ikon tray tidak lagi hilang.** Mengubah tampilan layar (monitor tidur dan bangun, resolusi, DPI) atau pengaturan Dock dulu mematikan dan menyalakan ulang tray, sehingga daftar ikon kosong dan panah ikon tersembunyi hilang sampai aplikasi mendaftar lagi; aplikasi yang tidak mendaftar ulang tidak pernah kembali. Kini tray tetap hidup selama Dock pemiliknya sama, dan Dock tidak lagi mengira siaran Deskmate sendiri sebagai Explorer yang restart.
- **Pet teman berjalan mulus.** Posisi pet teman kini dikirim lebih sering beserta kecepatannya, dan penerima meneruskan gerak di antara kiriman, jadi pet teman tidak lagi bergerak tersendat.
- **Pet tergambar di ukuran yang tepat.** Pet kini digambar persis di tinggi yang diminta, bukan dibulatkan ke kelipatan piksel, dan gelembung serta bilah mengikuti ukuran itu.
- **Folder pet baru langsung terbaca.** Pilihan **Hewan** memindai ulang folder pet setiap kali dibuka, jadi pet yang baru disalin langsung muncul tanpa menutup Pengaturan, dan memilih pet (termasuk yang sama) memuat ulang `sheet.png` dan `pet.json`.
