using System.Collections.Generic;

namespace Deskmate
{
    static partial class Lang
    {
        // Registered last; words other tables already translate are left to them
        static void AddPet(Dictionary<string, string> en)
        {
            AddPetText(en, "Pet di dock", "Dock pet");
            AddPetText(en, "Pet", "Pet");
            AddPetText(en, "Hewan kecil yang berjalan di atas dock, atau di tepi bawah layar tanpa dock", "A little animal that walks on top of the dock, or along the bottom of the screen without one");
            AddPetText(en, "Hewan", "Animal");
            AddPetText(en, "Kucing", "Cat");
            AddPetText(en, "Anjing", "Dog");
            AddPetText(en, "Chao", "Chao");
            AddPetText(en, "Gagal dimuat, memakai kucing", "Could not load, showing the cat");
            AddPetText(en, "Warna bulu", "Fur color");
            AddPetText(en, "Oranye", "Orange");
            AddPetText(en, "Cokelat", "Brown");
            AddPetText(en, "Abu-abu", "Gray");
            AddPetText(en, "Hitam", "Black");
            AddPetText(en, "Krem", "Cream");
            AddPetText(en, "Pet buatan sendiri memakai warna dari gambarnya", "Your own pets use the colors in their image");
            AddPetText(en, "Ukuran", "Size");
            AddPetText(en, "Kecil", "Small");
            AddPetText(en, "Sedang", "Medium");
            AddPetText(en, "Besar", "Large");
            AddPetText(en, "Kecepatan jalan", "Walking speed");
            AddPetText(en, "Area jalan", "Walking area");
            AddPetText(en, "Bawah layar", "Screen bottom");
            AddPetText(en, "Di bawah layar, pet berjalan selebar layar dan melompat naik ke dock", "Along the screen bottom the pet walks the full width and hops up onto the dock");
            AddPetText(en, "Reaksi", "Reactions");
            AddPetText(en, "Kejar kursor otomatis", "Automatic cursor chase");
            AddPetText(en, "Sapukan kursor cepat ke samping di sepanjang dock untuk mengajak pet berlari sebentar", "Sweep the cursor quickly along the dock to make the pet chase briefly");
            AddPetText(en, "Kaget, senang, bergoyang saat musik, dan tidur saat Anda pergi", "Startled, happy, bobs to music, and naps while you're away");
            AddPetText(en, "Pet buatan sendiri", "Your own pets");
            AddPetText(en, "Folder pet", "Pet folder");
            AddPetText(en, "Per pet: sheet.png dan pet.json dari Aseprite; tag walk wajib, pose lain opsional", "Per pet: sheet.png and pet.json from Aseprite; walk tag required, other poses optional");
            AddPetText(en, "Buka folder", "Open folder");
            AddPetText(en, "Yuk, minum air!", "Time for a sip!");
            AddPetText(en, "Lihat jauh dulu, ya.", "Look into the distance.");
            AddPetText(en, "Yuk, regangkan badan!", "Let’s stretch!");
            AddPetText(en, "+1 gelas", "+1 glass");
            AddPetText(en, "Tunda 15 menit", "Snooze 15 min");
            // Load problems, shown under the animal picker
            AddPetText(en, "butuh sheet.png dan pet.json", "needs sheet.png and pet.json");
            AddPetText(en, "berkas terlalu besar", "file too large");
            AddPetText(en, "pet.json tidak terbaca", "pet.json could not be read");
            AddPetText(en, "sheet.png tidak terbaca", "sheet.png could not be read");
            AddPetText(en, "jumlah frame tidak sesuai", "between 1 and 128 frames are supported");
            AddPetText(en, "ukuran frame tidak sesuai", "all frames need one size, up to 256 px");
            AddPetText(en, "frame di luar gambar", "a frame lies outside the image");
            AddPetText(en, "butuh tag walk", "needs a walk tag");
            AddPetText(en, "folder pet tidak ditemukan", "pet folder not found");
            // Multiplayer pet link & chat
            AddPetText(en, "Teman pet (Online)", "Pet friends (Online)");
            AddPetText(en, "Nama tampilan", "Display name");
            AddPetText(en, "Muncul di atas gelembung chat pet Anda", "Shown above your pet's chat bubble");
            AddPetText(en, "Terima custom pet dari teman", "Receive custom pets from friends");
            AddPetText(en, "Unduh sprite custom pet teman secara otomatis dan simpan di cache", "Automatically download friends' custom pet sprites and cache them locally");
            AddPetText(en, "Jadi host (Internet)", "Host room (Internet)");
            AddPetText(en, "PC Anda menjadi server sementara lewat Cloudflare Quick Tunnel (maks 4 pet)", "Your PC acts as a temporary server via Cloudflare Quick Tunnel (up to 4 pets)");
            AddPetText(en, "Mulai host", "Start host");
            AddPetText(en, "Pasang & Host", "Install & Host");
            AddPetText(en, "Berhenti", "Stop");
            AddPetText(en, "Salin kode", "Copy code");
            AddPetText(en, "Kode undangan disalin", "Invite code copied");
            AddPetText(en, "Gabung ke teman", "Join a friend");
            AddPetText(en, "Tempel kode undangan dari host lalu tekan Gabung", "Paste an invite code from the host and click Join");
            AddPetText(en, "Gabung", "Join");
            AddPetText(en, "Putuskan", "Disconnect");
            AddPetText(en, "Membuka Cloudflare Tunnel...", "Opening Cloudflare Tunnel...");
            AddPetText(en, "Memulai server lokal...", "Starting local server...");
            AddPetText(en, "Menunggu teman bergabung...", "Waiting for friends to join...");
            AddPetText(en, "Menghubungkan ke host...", "Connecting to host...");
            AddPetText(en, "Terhubung", "Connected");
            AddPetText(en, "Terhubung dengan {0}", "Connected with {0}");
            AddPetText(en, "Host terputus", "Host disconnected");
            AddPetText(en, "Mengunduh cloudflared...", "Downloading cloudflared...");
            AddPetText(en, "Gagal mengunduh cloudflared", "Failed to download cloudflared");
            AddPetText(en, "tanda tangan cloudflared.exe tidak valid", "cloudflared.exe is not validly signed by Cloudflare");
            AddPetText(en, "Berhenti menjadi host untuk bergabung ke teman lain", "Stop hosting to join another friend");
            AddPetText(en, "Putuskan koneksi terlebih dahulu untuk menjadi host", "Disconnect first to host a room");
            // Action bar above the pet
            AddPetText(en, "Aksi pet", "Pet actions");
            AddPetText(en, "Elus", "Pet");
            AddPetText(en, "Lompat", "Hop");
            AddPetText(en, "Kejar", "Chase");
            AddPetText(en, "Tidur", "Nap");
            AddPetText(en, "Chat", "Chat");
            AddPetText(en, "Atur", "Settings");
            AddPetText(en, "Kirim pesan lewat pet", "Send message via pet");
            AddPetText(en, "Kirim", "Send");
            AddPetText(en, "Pesan baru dari pet", "New message from pet");
            AddPetText(en, "Teman", "Friend");
            AddPetText(en, "Chat room", "Room chat");
            AddPetText(en, "Belum ada pesan. Sapa temanmu!", "No messages yet. Say hi!");
            AddPetText(en, "{0} masuk room", "{0} joined");
            AddPetText(en, "{0} keluar room", "{0} left");
            AddPetText(en, "Rusuh di layar teman", "Mess with your friend's screen");
            AddPetText(en, "Rusuh lagi dalam {0} dtk", "Mess again in {0}s");
            AddPetText(en, "Bakar", "Burn");
            AddPetText(en, "Retakkan", "Crack");
            AddPetText(en, "Lempar cat", "Paint");
            AddPetText(en, "Bekukan", "Freeze");
            AddPetText(en, "Membakar layarmu!", "Set your screen on fire!");
            AddPetText(en, "Meretakkan layarmu!", "Cracked your screen!");
            AddPetText(en, "Melempar cat ke layarmu!", "Threw paint at your screen!");
            AddPetText(en, "Membekukan layarmu!", "Froze your screen!");
            AddPetText(en, "Izinkan teman rusuh di layar", "Let friends mess with your screen");
            AddPetText(en, "Efek singkat seperti api atau salju yang bisa diklik tembus; tidak muncul saat berbagi layar", "Short effects like fire or snow that clicks pass through; never shown while sharing your screen");
        }

        static void AddPetText(Dictionary<string, string> en, string id, string text)
        {
            if (!en.ContainsKey(id)) en.Add(id, text);
        }
    }
}
