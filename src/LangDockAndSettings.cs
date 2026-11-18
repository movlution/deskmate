using System;
using System.Collections.Generic;
using System.Globalization;

namespace Deskmate
{
    static partial class Lang
    {
        static void AddDockAndSettings(Dictionary<string, string> en)
        {
            // Dock
            en.Add("Aplikasi dan widget di tepi bawah layar, di atas taskbar", "Apps and widgets at the bottom of the screen, above the taskbar");
            en.Add("Sisakan ruang untuk dock", "Keep room for the dock");
            en.Add("Jendela yang dimaksimalkan berhenti di atas dock", "Maximized windows stop above the dock");
            en.Add("Perbesar ikon", "Magnify icons");
            en.Add("Ikon membesar seperti gelombang saat kursor lewat", "Icons swell in a wave as the pointer passes");
            en.Add("Widget", "Widgets");
            en.Add("Kuota AI dan agen", "AI quota and agents");
            en.Add("Jam dan tanggal; klik membuka Ringkasan", "Time and date; a click opens Overview");
            en.Add("Sampul dan tombol putar; judul saat disorot", "Cover and play button; title on hover");
            en.Add("Ikon dan suhu; detail saat disorot", "Icon and temperature; details on hover");
            en.Add("Pemakaian tertinggi dan agen yang bekerja", "Highest usage and working agents");
            en.Add("Butuh kota di Pengaturan › Sistem", "Needs a city in Settings › System");
            en.Add("Hanya di laptop", "Laptops only");
            en.Add("CPU dan RAM, dibaca tiap 3 detik", "CPU and RAM, read every 3 seconds");
            en.Add("Aplikasi tersemat", "Pinned apps");
            en.Add("Klik kanan ikon aplikasi di dock, lalu Sematkan di Dock", "Right-click an app in the dock, then Pin to Dock");
            en.Add("Lepas semua", "Unpin all");
            en.Add("Buka jendela baru", "New window");
            en.Add("Sematkan di Dock", "Pin to Dock");
            en.Add("Lepas dari Dock", "Unpin from Dock");
            en.Add("Tutup jendela", "Close window");
            en.Add("Tutup {0} jendela", "Close {0} windows");
            en.Add("{0} agen", "{0} agents");
            en.Add("{0} bekerja", "{0} working");
            en.Add("Ganti taskbar Windows", "Replace the Windows taskbar");
            en.Add("Taskbar Windows disembunyikan; Start, pencarian, dan jam pindah ke dock", "The Windows taskbar hides; Start, search, and the clock move to the dock");
            en.Add("Tampilkan taskbar Windows", "Show the Windows taskbar");
            en.Add("Sembunyikan taskbar Windows", "Hide the Windows taskbar");
            en.Add("Selalu, selama menggantikan taskbar", "Always, while it replaces the taskbar");
            en.Add("Ringkasan pilihanmu", "Your Overview");
            en.Add("Pilih dan urutkan kartunya, termasuk cuaca, baterai, clipboard, dan sholat. Panel yang lebar memakai tiga kolom.", "Pick and order its cards, including weather, battery, clipboard, and prayer. Wide panels use three columns.");
            en.Add("Ngobrol dengan Deskmate", "Chat with Deskmate");
            en.Add("Island di mana saja", "Island anywhere");
            en.Add("Atas tengah, pojok atas, atau sisi kiri dan kanan layar dengan island berdiri tegak. Saat diam, island bisa menampilkan jam, tanggal, baterai, atau suhu di sampingnya.", "Top centre, a top corner, or the left or right edge, where the island stands upright. When idle, it can show the time, date, battery, or temperature beside it.");
            en.Add("Suruh Deskmate punya jendela obrolan yang ingat percakapannya, dan bisa memakai Groq, OpenAI, Claude, Gemini, OpenRouter, DeepSeek, Mistral, AI lokal di laptop sendiri, atau server sendiri.", "Ask Deskmate has a chat window that remembers the conversation, and can use Groq, OpenAI, Claude, Gemini, OpenRouter, DeepSeek, Mistral, local AI on your own laptop, or your own server.");
            en.Add("Bar kaca di tepi bawah berisi aplikasi, pratinjau jendela, dan widget, yang bisa menggantikan taskbar Windows sepenuhnya. Nyalakan di Pengaturan › Dock.", "A glass bar along the bottom with your apps, live window previews, and widgets, which can fully replace the Windows taskbar. Turn it on in Settings › Dock.");
            en.Add("Ubah format lebih lengkap", "A fuller converter");
            en.Add("PDF ke Word dan teks, OCR untuk foto dan hasil scan, kompres PDF, gambar, video, dan audio, gabung PDF dan gambar jadi satu, serta slide ke gambar.", "PDF to Word and text, OCR for photos and scans, compress PDFs, images, video, and audio, merge PDFs and images into one, and slides to pictures.");
            en.Add("Notifikasi lebih rapi", "Tidier notifications");
            en.Add("Deskmate bisa jadi penerima utama banner chat, panggilan, dan screenshot. Pesan dari HP dan laptop tampil sekali, dan isinya disamarkan saat berbagi layar.", "Deskmate can take over banners for chats, calls, and screenshots. Messages from phone and laptop show once, and their text is hidden while you share your screen.");
            en.Add("Screenshot dan Downloads", "Screenshots and Downloads");
            en.Add("Screenshot bisa disamarkan, dipotong, diputar, dan dibalik. Berkas lepas di Downloads bisa dirapikan per jenis dan diurungkan selama satu jam.", "Screenshots can be blurred, cropped, rotated, and flipped. Loose files in Downloads can be sorted by type, with an hour to undo.");
            en.Add("Perintah berisiko selalu ditanya ulang, island merangkum hasil kerja agent, klik notifikasinya membuka sesinya di tab Agen, dan kuota semua agent kini satu sakelar.", "Risky commands always ask again, the island sums up what an agent did, a click on its notification opens the session in the Agents tab, and every agent's quota is now one switch.");
            en.Add("Pengaturan baru", "New Settings");
            en.Add("Dikelompokkan per kategori: pilih satu untuk melihat daftar halamannya. Aksesibilitas, Pintasan, dan Tentang langsung ada di samping.", "Grouped by category: pick one to see its pages. Accessibility, Shortcuts, and About sit right in the sidebar.");
            en.Add("Gulir roda mouse selalu bisa", "Scrolling the wheel always works");
            en.Add("Bahasa, jam, layanan AI, HP, dan integrasi", "Language, clock, AI services, phone, and integrations");
            en.Add("Tema, warna, dan paket tema", "Theme, colors, and theme packs");
            en.Add("Letak, perilaku, isi, dan panel island", "The island's placement, behavior, content, and panel");
            en.Add("Musik, pesan, sistem, rak, agen, dikte, dan pengingat", "Music, messages, system, shelf, agents, dictation, and reminders");
            en.Add("Aksesibilitas, pintasan, dan tentang Deskmate", "Accessibility, shortcuts, and about Deskmate");
            // Settings sections, headings, notes
            en.Add("Fitur", "Features");
            en.Add("Bantuan", "Help");
            en.Add("Dasar", "Basics");
            en.Add("Tema dan warna", "Theme and color");
            en.Add("Letak dan bentuk", "Placement and shape");
            en.Add("Perilaku", "Behavior");
            en.Add("Dock dan taskbar", "Dock and taskbar");
            en.Add("Bahasa dan jam", "Language and clock");
            en.Add("Bentuk", "Shape");
            en.Add("Ukuran dan layar", "Size and screen");
            en.Add("Panel", "Panel");
            en.Add("Isi samping", "Sides");
            en.Add("Bahasa, jam, pembaruan, dan data Deskmate", "Language, clock, updates, and Deskmate's data");
            en.Add("Layanan AI untuk dikte, ringkasan, dan Suruh Deskmate", "AI services for dictation, summaries, and Ask Deskmate");
            en.Add("Notifikasi, kode OTP, dan berkas dari HP", "Notifications, OTP codes, and files from your phone");
            en.Add("Tugas dan jadwal dari Kulia, Notion, Todoist, dan iCal", "Tasks and schedules from Kulia, Notion, Todoist, and iCal");
            en.Add("Tema, material kaca, dan warna aksen", "Theme, glass material, and accent color");
            en.Add("Gambar, sapaan, dan suara dari paket tema", "Pictures, greetings, and sounds from theme packs");
            en.Add("Di mana island tinggal dan bagaimana ia menempel", "Where the island lives and how it meets the edge");
            en.Add("Kapan island menyingkir dan bagaimana panel terbuka", "When the island steps aside and how the panel opens");
            en.Add("Isi island saat tidak ada aktivitas", "What the island shows when nothing is going on");
            en.Add("Kartu yang tampil di Ringkasan dan urutannya", "Which Overview cards show, and their order");
            en.Add("Tab di panel island dan urutannya", "The island panel's tabs and their order");
            en.Add("Bar aplikasi dan widget, atau pengganti taskbar", "A bar of apps and widgets, or a taskbar replacement");
            en.Add("Lirik dan visualizer", "Lyrics and visualizer");
            en.Add("Notifikasi pesan dan siapa yang menampilkannya", "Message notifications, and who shows them");
            en.Add("HUD keyboard, perangkat, dan aktivitas sistem", "Keyboard HUDs, devices, and system activity");
            en.Add("Rak, ubah format, clipboard, dan screenshot", "Shelf, conversions, clipboard, and screenshots");
            en.Add("Claude Code, Codex, dan agent lain", "Claude Code, Codex, and other agents");
            en.Add("Bicara, lalu teksnya ditempel", "Speak, and the text is pasted");
            en.Add("Sholat, cuaca, minum, mata, dan peregangan", "Prayer, weather, water, eyes, and stretching");
            en.Add("Pembaca layar, gerak, dan Senyap", "Screen readers, motion, and Quiet");
            en.Add("Pintasan keyboard Deskmate", "Deskmate's keyboard shortcuts");
            en.Add("Versi, saran, dan pembuat Deskmate", "Version, feedback, and who made Deskmate");
        }
    }
}
