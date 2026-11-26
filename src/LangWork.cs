using System.Collections.Generic;

namespace Deskmate
{
    static partial class Lang
    {
        // Tasks and notes window: smart views, search, undo toasts, quick menus
        static void AddWork(Dictionary<string, string> en)
        {
            AddOnce(en, "Hari ini", "Today");
            AddOnce(en, "Terjadwal", "Planned");
            AddOnce(en, "Cari tugas", "Search tasks");
            AddOnce(en, "Opsi daftar", "List options");
            AddOnce(en, "Cari judul atau detail tugas…", "Search task titles or details…");
            AddOnce(en, "Tidak ada tugas yang cocok", "No matching tasks");
            AddOnce(en, "Coba kata kunci lain", "Try another keyword");
            AddOnce(en, "Tidak ada tugas untuk hari ini", "Nothing due today");
            AddOnce(en, "Tugas bertenggat hari ini atau terlambat muncul di sini", "Tasks due today or overdue show up here");
            AddOnce(en, "Belum ada tugas terjadwal", "No planned tasks yet");
            AddOnce(en, "Beri tenggat, mis. \"Kirim laporan @besok 10:00\"", "Add a due date, e.g. \"Send report @tomorrow 10:00\"");
            AddOnce(en, "Tidak ada hasil", "No results");
            AddOnce(en, "{0} hasil", "{0} results");
            AddOnce(en, "Tambah tugas untuk hari ini…", "Add a task for today…");
            AddOnce(en, "Tambah tugas… (mis. Rapat @besok 10:00)", "Add a task… (e.g. Meeting @tomorrow 10:00)");
            AddOnce(en, "Tugas selesai", "Task completed");
            AddOnce(en, "{0} tugas selesai", "{0} tasks completed");
            AddOnce(en, "Urungkan", "Undo");
            AddOnce(en, "Tugas dihapus", "Task deleted");
            AddOnce(en, "Tugas dan {0} subtugas dihapus", "Task and {0} subtasks deleted");
            AddOnce(en, "1 tugas selesai dihapus", "1 completed task deleted");
            AddOnce(en, "{0} tugas selesai dihapus", "{0} completed tasks deleted");
            AddOnce(en, "Dipindahkan ke {0}", "Moved to {0}");
            AddOnce(en, "Tenggat", "Due date");
            AddOnce(en, "Minggu depan", "Next week");
            AddOnce(en, "Hapus tenggat", "Remove due date");
            AddOnce(en, "Pilih tanggal…", "Pick a date…");
            AddOnce(en, "Pindah ke daftar", "Move to list");
            AddOnce(en, "Duplikat", "Duplicate");
            AddOnce(en, "Salin teks", "Copy text");
            AddOnce(en, "Salin isi", "Copy contents");
            AddOnce(en, "Disalin ke papan klip", "Copied to clipboard");
            AddOnce(en, "Catatan dihapus", "Note deleted");
            AddOnce(en, "Catatan diarsipkan", "Note archived");
            AddOnce(en, "Catatan dipulihkan", "Note restored");
            AddOnce(en, "Tanpa judul", "Untitled");
            AddOnce(en, "{0} (salinan)", "{0} (copy)");
        }
    }
}
