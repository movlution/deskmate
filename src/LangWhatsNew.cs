using System;
using System.Collections.Generic;
using System.Globalization;

namespace Deskmate
{
    static partial class Lang
    {
        static void AddWhatsNew(Dictionary<string, string> en)
        {
            // What's new 2.22.0
            AddOnce(en, "Ikon Dock khas Deskmate", "Deskmate Dock icons");
            AddOnce(en, "Tombol Start, Semua jendela, Tugas, Suruh, dan Rak di Dock kini memakai ikon bergambar sendiri yang serasi dengan gaya Deskmate.", "The Start, All windows, Tasks, Ask Deskmate and Shelf buttons in the Dock now use their own illustrated icons that match the Deskmate style.");
            AddOnce(en, "Tombol Suruh di Dock", "Ask Deskmate in the Dock");
            AddOnce(en, "Satu tombol di Dock untuk membuka obrolan Suruh Deskmate. Atur di Pengaturan › Dock.", "One Dock button opens the Ask Deskmate chat. Set it in Settings › Dock.");
            AddOnce(en, "Konversi lebih lengkap", "More complete Convert");
            AddOnce(en, "Gabungkan banyak gambar jadi satu PDF atau satu PDF per berkas, susun gambar dan GIF, serta gabungkan audio jadi satu MP3.", "Merge many images into one PDF or one PDF per file, stitch images and GIFs, and join audio into one MP3.");
            AddOnce(en, "Pengaturan bergambar", "Illustrated Settings");
            AddOnce(en, "Setiap kategori Pengaturan kini punya ilustrasi sendiri, dan kuota agen, cuaca, serta hook agen lebih andal.", "Every Settings category now has its own illustration, and agent quota, weather and agent hooks are more reliable.");
            // What's new 2.21.3
            AddOnce(en, "Kirim saran lewat GitHub", "Send feedback on GitHub");
            AddOnce(en, "Kirim saran di Pengaturan › Tentang kini membuka GitHub Issues untuk laporan masalah dan ide fitur, dan panduan sambungan HP diperbarui.", "Send feedback in Settings › About now opens GitHub Issues for bug reports and feature ideas, and the phone connection guide has been updated.");
            // What's new 2.21.2
            AddOnce(en, "Pengaturan lebih mulus", "Smoother Settings");
            AddOnce(en, "Jendela Pengaturan dan kontrolnya kini beranimasi lebih halus, mengikuti pengaturan gerak, dan menggambar ulang lebih cepat berkat cache tampilan.", "The Settings window and its controls now animate more smoothly, respect your motion setting, and redraw faster thanks to view caching.");
            // What's new 2.21.1
            AddOnce(en, "Pemeriksaan pembaruan lebih andal", "More reliable update checks");
            AddOnce(en, "Jika API GitHub tidak merespons, Deskmate memeriksa rilis terbaru lewat halaman resmi GitHub. Pemasangan tetap memverifikasi SHA-256.", "If the GitHub API is unavailable, Deskmate checks the official GitHub releases page. Installation still verifies SHA-256.");
            // What's new 2.21.0
            AddOnce(en, "Ingatan lebih cerdas mencari & menghubungkan", "Smarter Memory search & connections");
            AddOnce(en, "Pencarian kini memahami fakta & percakapan secara semantik, mendukung imbuhan Indonesia/Inggris lebih luas, mengambil potongan teks paling relevan, serta mengelompokkan aktivitas lintas aplikasi menjadi alur kerja (workstream) dan graf entitas.", "Search now understands facts and conversations semantically, supports richer Indonesian/English stemming, extracts the most relevant passages, and groups cross-app activity into workstreams and an entity graph.");
            AddOnce(en, "Konteks layar & embedding jauh lebih cepat", "Faster screen context & embeddings");
            AddOnce(en, "Embedding episode, fakta, dan percakapan dikirim sekaligus dalam satu batch, pencarian vektor int8 lebih kencang, editor/terminal dibaca dengan jalur cepat, OCR fokus pada area isi utama, dan jendela split-screen pendamping ikut dikenali.", "Episode, fact, and conversation embeddings are now batched in a single request, int8 vector search is faster, editors/terminals use a fast UIA path, OCR targets the main viewport, and side-by-side split-screen windows are recognized.");
            // What's new 2.20.1
            AddOnce(en, "Ingatan lebih cepat & akurat", "Faster & more accurate Memory");
            AddOnce(en, "Pembacaan konteks layar (UI Automation & OCR), pencarian memori hibrida, dan database SQLite lokal kini lebih cepat, lebih akurat membaca fokus/URL/error, dan jauh lebih hemat baca-tulis disk.", "Screen context capture (UI Automation & OCR), hybrid memory search, and the local SQLite database are now faster, more accurate at reading focus/URLs/errors, and much lighter on disk I/O.");
            AddOnce(en, "Tampilan Pengaturan & kontrol lebih rapi", "Cleaner Settings & controls");
            AddOnce(en, "Navigasi Pengaturan diperbarui dengan beranda & submenu yang lebih ringkas, plus penyempurnaan visual kontrol, notifikasi, dan sinkronisasi sumber.", "Settings navigation is refreshed with a cleaner hub and submenus, alongside visual refinements to controls, notifications, and source syncing.");
            // What's new 2.20.0
            AddOnce(en, "Pet mengingatkan tugas & acara", "Your pet reminds you of tasks & events");
            AddOnce(en, "Sesekali pet menyebut tugas yang belum selesai, tenggat yang dekat, atau acara yang sebentar lagi mulai. Klik gelembungnya untuk membuka Tugas atau Jadwal.", "Now and then your pet mentions an unfinished task, a deadline that's close, or an event starting soon. Click its bubble to open Tasks or the schedule.");
            AddOnce(en, "Aplikasi admin di Dock bisa dibuka", "Admin apps open from the Dock");
            AddOnce(en, "Jendela aplikasi yang berjalan sebagai administrator kini muncul saat ikonnya di Dock diklik, juga saat sedang di-minimize.", "Windows of apps running as administrator now come up when you click them in the Dock, even when minimized.");
            // What's new 2.19.1
            AddOnce(en, "Pilihan bahasa di installer", "Installer language choice");
            AddOnce(en, "Installer kini menampilkan Bahasa Indonesia dan English, dan memilih otomatis sesuai bahasa Windows.", "The installer now lists Bahasa Indonesia and English, and picks one to match your Windows language.");
            // What's new 2.19.0
            AddOnce(en, "Kuota AI langsung dari akun", "AI quota straight from your accounts");
            AddOnce(en, "Kuota Claude, Codex, dan Antigravity dibaca dari akun yang sudah masuk setiap 5 menit, tanpa menunggu sesi agent berjalan.", "Claude, Codex, and Antigravity quota is read from your signed-in accounts every 5 minutes, without waiting for an agent session.");
            // What's new 2.18.0
            AddOnce(en, "Tugas lebih cepat diatur", "Faster task handling");
            AddOnce(en, "Tampilan Hari ini dan Terencana, pencarian Ctrl+F, tenggat langsung dari kolom tulis, dan Urungkan setelah menyelesaikan atau menghapus tugas.", "Today and Planned views, Ctrl+F search, due dates right from the composer, and Undo after completing or deleting tasks.");
            AddOnce(en, "Pilih bahasa, dock, dan fitur seperti saat pertama dipasang. Buka lagi dari Pengaturan › Tentang.", "Pick language, dock, and features like on first install. Reopen it from Settings › About.");
            // What's new 2.17.1
            en.Add("Bingkai kartu di tiap jendela", "Card animation on each window frame");
            en.Add("Efek kartu mengikuti bingkai saat jendela dibuka, dipulihkan, diminimalkan, atau ditutup. Ikon Dock ikut bergerak.", "The card effect follows the window frame when you open, restore, minimize, or close a window. The Dock icon also animates.");
            // What's new 2.17.0
            en.Add("Jendela seperti tumpukan kartu", "Windows move like a deck of cards");
            en.Add("Buka, pindah, pulihkan, atau tutup jendela lewat Dock dengan animasi kartu. Tombol minimize di jendela juga mengirim kartu ke Dock.", "Open, switch, restore, or close windows from the Dock with card animations. The window's minimize button also sends its card to the Dock.");
            en.Add("Chat room pet lebih mudah dibaca", "Pet room chat is easier to read");
            en.Add("Pesan tampil dalam gelembung. Tombol Pesan baru membawa kamu ke bagian terbaru saat sedang membaca riwayat.", "Messages appear in bubbles. The New message button jumps to the latest messages when you're reading history.");
            // What's new 2.16.0
            en.Add("Chat bersama teman", "Chat with friends");
            en.Add("Chat pet kini menjadi chat room dengan riwayat bersama, dan nama pemilik tampil di atas setiap pet.", "Pet chat is now a room chat with a shared history, and each owner's name shows above every pet.");
            en.Add("Rusuh lewat pet", "Pranks through pets");
            en.Add("Klik kanan pet teman untuk membakar, meretakkan, melempar cat, atau membekukan layarnya sebentar. Bisa dimatikan di Pengaturan › Dock & Taskbar.", "Right-click a friend's pet to briefly burn, crack, paint, or freeze their screen. Turn it off in Settings › Dock & Taskbar.");
            en.Add("Chao, pet ketiga", "Chao, a third pet");
            en.Add("Roh fantasi mungil berwarna pastel kini bisa dipilih di samping kucing dan anjing.", "A tiny pastel fantasy spirit can now be picked next to the cat and dog.");
            en.Add("Klik dock untuk meminimalkan", "Click the Dock to minimize");
            en.Add("Klik aplikasi yang sedang di depan untuk meminimalkannya, seperti taskbar Windows.", "Click the app in front to minimize it, like the Windows taskbar.");
            // What's new 2.15.0 ("Rak di Dock" is translated with the Dock strings)
            en.Add("Seret berkas, gambar, atau teks ke dock untuk menahannya, lalu seret ke aplikasi lain. Atur di Pengaturan › Dock.", "Drag files, pictures, or text onto the dock to hold them, then drag them into another app. Set it in Settings › Dock.");
            en.Add("Lempar pet", "Throw your pet");
            en.Add("Lepas pet sambil menyeret untuk melemparnya: ia melayang, memantul, lalu kembali ke tempatnya.", "Let go of your pet mid-drag to throw it: it flies, bounces, and hops back to its spot.");
            en.Add("AI mengatur semua fitur", "AI runs every feature");
            en.Add("Suruh dan pet kini bisa membaca dan mengubah tugas, catatan, kalender, pengingat, timer, media, rak, integrasi, dan pengaturan.", "Suruh and your pet can now read and change tasks, notes, calendar, reminders, timers, media, the shelf, integrations, and settings.");
            // What's new 2.14.2
            en.Add("Ingatan lebih ringan", "Lighter Memory");
            en.Add("Ingatan kini memakai jauh lebih sedikit CPU, memori, dan tulisan disk selagi layar tidak berubah. Apa yang dicatat tetap sama.", "Memory now uses far less CPU, RAM, and disk writes while the screen stays the same. What it records is unchanged.");
            // What's new 2.14.1 ("Penjaga beban" is translated with the Guard strings)
            en.Add("Aplikasi di latar yang memakai banyak CPU atau RAM selama beberapa menit muncul di island, dengan pilihan Mode hemat, Tutup, atau Abaikan. Matikan di Pengaturan › Perangkat & HP.", "Background apps using lots of CPU or RAM for minutes show on the island, with Efficiency mode, Close, or Ignore. Turn it off in Settings › Devices & Phone.");
            // What's new 2.14.0
            en.Add("AI tanpa kunci API", "AI without an API key");
            en.Add("Pakai Codex CLI atau Claude Code yang sudah masuk di laptop ini sebagai layanan AI, lengkap dengan alat Deskmate: pengingat, timer, tugas, dan Ingatan.", "Use the Codex CLI or Claude Code already signed in on this laptop as your AI provider, with Deskmate's tools: reminders, timers, tasks, and Memory.");
            en.Add("Pilih model dari daftar", "Pick the model from a list");
            en.Add("Tombol Pilih di Pengaturan › AI mengambil daftar model dari layanannya, lengkap dengan pencarian.", "The Select button in Settings › AI fetches the provider's model list, with search.");
            en.Add("Ingatan lebih cepat dan tahan lama", "Faster, longer-lasting Memory");
            en.Add("Pencarian menemukan lebih banyak, dan fakta jangka panjang berubah sedikit demi sedikit tanpa tertimpa jawaban AI yang keliru.", "Search finds more, and long-term facts change a little at a time instead of being overwritten by a wrong AI answer.");
            // What's new 2.13.1
            en.Add("Incognito Chrome dilewati", "Chrome Incognito is skipped");
            en.Add("Ingatan kini mengenali jendela Incognito dan Tamu di Chrome, yang judulnya tidak menyebut mode itu, dan tidak mencatatnya.", "Memory now recognizes Chrome Incognito and Guest windows, whose titles don't mention the mode, and doesn't record them.");
            en.Add("Lupakan benar-benar lupa", "Forget really forgets");
            en.Add("Rapat yang dilupakan tidak muncul lagi, dan layar yang sedang terbuka ikut dilupakan bila masuk rentangnya.", "Forgotten meetings no longer come back, and the screen that is open now is forgotten too when it falls in the range.");
            // What's new 2.13.0
            en.Add("Geser island dengan mouse", "Drag the island with the mouse");
            en.Add("Seret island ke mana pun di sepanjang tepi layar; posisinya tersimpan saat dilepas. Bisa dimatikan di Pengaturan › Island › Posisi.", "Drag the island anywhere along the screen edge; it stays where you let go. Turn it off in Settings › Island › Position.");
            en.Add("Ingatan lebih peka layar", "Memory reads the screen faster");
            en.Add("Ingatan membaca layar aktif dalam hitungan ratusan milidetik dan menyusunnya per bagian, jadi jawaban tentang yang sedang Anda lihat lebih tepat, bahkan tanpa AI.", "Memory reads the active screen within a few hundred milliseconds and organizes it by section, so answers about what you are looking at are more accurate, even without AI.");
            // What's new 2.12.0 ("Ingatan" is translated with the Ingatan settings)
            en.Add("Pet bisa dibawa ke mana saja", "Carry the pet anywhere");
            en.Add("Seret pet ke mana pun di layar; saat dilepas ia jatuh kembali. Pilih Area jalan › Bawah layar agar pet berjalan selebar layar dan melompat naik ke dock.", "Drag the pet anywhere on the screen; let go and it drops back down. Choose Walking area › Screen bottom to let it walk the full width and hop up onto the Dock.");
            en.Add("Bar aksi pet", "Pet action bar");
            en.Add("Klik pet untuk membuka tombol besar: elus, lompat, kejar, tidur, tanya, chat, dan pengaturan.", "Click the pet for big buttons: pet, hop, chase, nap, ask, chat, and settings.");
            en.Add("Pet yang bereaksi", "A pet that reacts");
            en.Add("Pet kaget oleh notifikasi, senang saat pekerjaan selesai, bergoyang saat musik, tenang saat fokus, dan tidur saat Anda pergi.", "The pet startles at notifications, cheers when work finishes, bobs to music, stays calm during focus, and naps while you're away.");
            en.Add("Teman pet online", "Pet friends online");
            en.Add("Sampai empat pet berbagi satu dock lewat internet, lengkap dengan chat, emote, dan pet buatan sendiri.", "Up to four pets share one Dock over the internet, with chat, emotes, and custom pets.");
            en.Add("Deskmate bisa mengingat apa yang Anda kerjakan di laptop ini dan menjawab pertanyaan tentangnya. Mati secara bawaan; nyalakan di Pengaturan › Ingatan.", "Deskmate can remember what you work on, on this laptop, and answer questions about it. Off by default; turn it on in Settings › Memory.");
            en.Add("Pemasang per pengguna", "Per-user installer");
            en.Add("Deskmate kini punya installer tanpa admin yang tercantum di Pengaturan › Aplikasi dan membersihkan dirinya saat dicopot.", "Deskmate now has an installer that needs no admin, appears in Settings › Apps, and cleans up after itself when removed.");
            // What's new 2.11.x
            en.Add("Dock selalu di atas", "Dock stays on top");
            en.Add("Dock, pet, dan notch di layar lain kembali ke atas dalam sedetik bila ada aplikasi yang menutupinya.", "The Dock, the pet, and the notch on other screens come back on top within a second when an app covers them.");
            en.Add("Pengaturan pet tidak melompat", "Pet settings stay put");
            en.Add("Mengganti hewan, warna bulu, atau menyalakan pet tidak lagi menggulir halaman Pengaturan ke atas.", "Changing the animal or fur color, or turning the pet on, no longer scrolls Settings back to the top.");
            en.Add("Pet di dock", "Dock pet");
            en.Add("Kucing atau anjing piksel berjalan di atas dock, ikut naik turun saat dock bersembunyi, dan menoleh atau melompat saat disentuh kursor. Nyalakan di Pengaturan › Dock & Taskbar.", "A pixel cat or dog walks on top of the Dock, rides it up and down when it hides, and turns or hops when you point at it. Turn it on in Settings › Dock & Taskbar.");
            en.Add("Pet dari Aseprite", "Pets from Aseprite");
            en.Add("Taruh sheet.png dan pet.json hasil Export Sprite Sheet di folder pet, lalu pilih di Hewan. Warna bulu, ukuran, dan kecepatan jalan juga bisa diatur.", "Put the sheet.png and pet.json from Export Sprite Sheet in the pet folder, then pick it under Animal. Fur color, size, and walking speed can be set too.");
            // What's new 2.10.x
            en.Add("Kaca Kalem", "Calm glass");
            en.Add("Material kaca baru: warna mengikuti wallpaper, diredam, tanpa kilau, dengan garis tepi tipis yang rata.", "A new glass material: tinted from the wallpaper, muted, no sheen, with a thin even rim.");
            en.Add("Jam di kepala panel", "Clock in the panel header");
            en.Add("Panel terbuka menampilkan jam dan tanggal di pojok kiri atas, dan tab kini berada dalam satu kapsul yang lebih mudah dibaca.", "The open panel shows the time and date in the top-left corner, and the tabs now sit in one easier-to-read capsule.");
            en.Add("Ikon notifikasi asli", "Real notification icons");
            en.Add("Notifikasi Phone Link menampilkan logo aplikasi Android-nya, dan notifikasi situs web menampilkan ikon browser.", "Phone Link notifications show their Android app's logo, and website notifications show the browser's icon.");
            en.Add("Editor tugas dua kolom", "Two-column task editor");
            en.Add("Editor tugas dan catatan kini dua kolom, sakelar bergerak lebih halus, dan pencarian Pengaturan lebih cepat.", "The task and note editors now use two columns, switches move more smoothly, and Settings search is faster.");
            en.Add("Dock dan panel lebih andal", "More reliable Dock and panel");
            en.Add("Dock kini muncul di atas aplikasi maksimal ber-title bar kustom seperti ChatGPT, dan panel Clear tetap terbaca di atas jendela terang.", "The Dock now appears over maximized custom-titlebar apps like ChatGPT, and the Clear panel stays readable over bright windows.");
            // What's new 2.9.x
            en.Add("Notch di semua layar", "Notch on every display");
            en.Add("Pilih Semua layar di Ukuran dan layar › Layar. Monitor lain menampilkan notch ringkas, dan panel terbuka di layar yang Anda arahkan atau klik.", "Choose All displays in Size and display › Display. Other monitors show a compact notch, and the panel opens on the screen you point at or click.");
            en.Add("Ikon tersembunyi dari setiap Dock", "Hidden icons from every Dock");
            en.Add("Saat Dock menggantikan taskbar, tombol panah di setiap monitor membuka daftar ikon tersembunyi tepat di atas Dock itu.", "When the Dock replaces the taskbar, the arrow on every monitor opens the hidden-icon list right above that Dock.");
            en.Add("Pengaturan yang ditata ulang", "Redesigned Settings");
            en.Add("Pengaturan kini disusun seperti System Settings di macOS, dengan kunci settings.ini yang tetap sama.", "Settings are now laid out like macOS System Settings, with the same settings.ini keys.");
            en.Add("Lebih responsif", "More responsive");
            en.Add("Deskmate berjalan pada prioritas normal, animasi notch dan panel lebih ringan, dan Start langsung menampilkan aplikasi, ikon, serta Terbaru.", "Deskmate runs at normal priority, notch and panel animations are lighter, and Start shows apps, icons, and Recent right away.");
            en.Add("Kaca dan tema", "Glass and theme");
            en.Add("Kaca wall.sync kini mewarnai mengikuti wallpaper, Dock naik lebih cepat dan tenang, dan tema mengikuti Windows.", "wall.sync glass now tints to the wallpaper, the Dock rises faster and calmer, and the theme follows Windows.");
            // What's new 2.8.x
            en.Add("Gambar di catatan tugas", "Images in task notes");
            en.Add("Pilih Tambah gambar atau tempel screenshot dengan Ctrl+V. Gambar disimpan sebagai PNG, ikut tersinkron ke Google Drive privat, dan tetap bisa dibuka dari cache saat offline.", "Choose Add image or paste a screenshot with Ctrl+V. Images are stored as PNG, sync privately to Google Drive, and remain available from the offline cache.");
            en.Add("Emoji di notifikasi", "Emoji in notifications");
            en.Add("Judul dan isi notifikasi kini menampilkan emoji dengan benar, tetap rapi saat dipotong, dan tidak lagi merusak teks di sekitarnya.", "Notification titles and bodies now render emoji correctly, stay tidy when clipped, and no longer break the surrounding text.");
            en.Add("Sakelar autostart kini memakai scheduled task, mengikuti status sebenarnya, dan tetap punya jalur cadangan untuk Windows yang membatasi scheduled task.", "The autostart switch now uses a scheduled task, follows the real state, and keeps a fallback for Windows builds that restrict scheduled tasks.");
            en.Add("Keterbacaan dan kaca", "Readability and glass");
            en.Add("Teks sekunder dan kontrol nonaktif lebih mudah dibaca, kontras chip tenggat diperkuat, dan target cahaya Liquid Glass di mode terang dikalibrasi ulang.", "Secondary text and disabled controls are easier to read, overdue chip contrast is stronger, and the Liquid Glass light-mode target is recalibrated.");
            en.Add("Stabilitas", "Stability");
            en.Add("Suite E2E Tier 1–4 kini mencakup 385 pemeriksaan, termasuk skenario nyata untuk onboarding, tema, kaca, tugas, notifikasi, dock, dan indikator mini.", "The Tier 1–4 E2E suite now covers 385 checks, including real scenarios for onboarding, themes, glass, tasks, notifications, the dock, and the mini indicator.");
            // What's new 2.7.x
            en.Add("Tugas dan catatan", "Tasks and notes");
            en.Add("Editor catatan baru dengan Markdown, tabel, checklist, blok kode, dan menu blok.", "A new note editor with Markdown, tables, checklists, code blocks, and a block menu.");
            en.Add("Tugas tersinkron dua arah dengan Google Tasks, dan catatan Markdown tersimpan di folder Deskmate Catatan pada Google Drive.", "Tasks sync both ways with Google Tasks, and Markdown notes live in the Deskmate Catatan folder on Google Drive.");
            en.Add("Pindai dan sambungkan Wi-Fi serta Bluetooth langsung dari dock, dengan interaksi dan pratinjau yang lebih rapi.", "Scan and connect Wi-Fi and Bluetooth right from the dock, with tidier interactions and previews.");
            en.Add("Atur tiap aplikasi: ikuti pengaturan umum, tampilkan di Pesan saja, sembunyikan, atau jeda banner 1 atau 8 jam.", "Set each app to follow the general rules, show in Messages only, hide, or pause banners for 1 or 8 hours.");
            en.Add("Tampilan kaca", "Glass appearance");
            en.Add("Resep Liquid Glass kini seragam di island, dock, dan panel, dan sisa klip dari tampilan sebelumnya tidak lagi tertinggal.", "The Liquid Glass recipe is now consistent across the island, dock, and panels, and stale clipping from the previous view no longer lingers.");
            en.Add("Ikon, nama, dan aset visual baru; aplikasi Windows ini kini sepenuhnya memakai identitas Deskmate.", "A new icon, name, and visual assets; the Windows app now fully uses the Deskmate identity.");
            en.Add("Tur", "Tour");
            en.Add("Dua langkah baru untuk dock dan ruang kerja, tombol lebih besar, dan kontrol yang lebih ramah pembaca layar.", "Two new steps for the dock and workspace, larger buttons, and screen-reader-friendly controls.");
            en.Add("Dock di semua layar", "Dock on every display");
            en.Add("Setiap monitor punya dock sendiri, lengkap dengan pratinjau jendela dan widget.", "Every monitor gets its own dock, complete with window previews and widgets.");
            en.Add("Coret, potong, putar, balik, dan samarkan gambar sebelum dibagikan.", "Draw, crop, rotate, flip, and redact an image before sharing it.");
        }
    }
}
