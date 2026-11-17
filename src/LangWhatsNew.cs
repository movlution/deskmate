using System;
using System.Collections.Generic;
using System.Globalization;

namespace Deskmate
{
    static partial class Lang
    {
        static void AddWhatsNew(Dictionary<string, string> en)
        {
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
