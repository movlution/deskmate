using System.Collections.Generic;

namespace Deskmate
{
    static partial class Lang
    {
        static void AddGuard(Dictionary<string, string> en)
        {
            // Penjaga beban; shared words like "Tutup" already live elsewhere
            AddOnce(en, "Penjaga beban", "Load guard");
            AddOnce(en, "Aplikasi di latar yang memakai banyak CPU atau RAM selama beberapa menit: mode hemat atau tutup", "Background apps using lots of CPU or RAM for minutes: efficiency mode or close");
            AddOnce(en, "Mode hemat", "Efficiency mode");
            AddOnce(en, "Abaikan", "Ignore");
            AddOnce(en, "{0} memakai {1}% CPU", "{0} is using {1}% CPU");
            AddOnce(en, "{0} memakai {1} RAM", "{0} is using {1} of RAM");
            AddOnce(en, "Sejak {0} menit, saat tidak dipakai", "For {0} minutes, while not in use");
            AddOnce(en, "Memori hampir penuh, aplikasinya di latar", "Memory is almost full, app is in the background");
            AddOnce(en, "{0} dalam mode hemat", "{0} is in efficiency mode");
            AddOnce(en, "Kembali normal saat kamu membukanya", "Back to normal when you open it");
            AddOnce(en, "{0} tidak bisa diatur", "{0} can't be changed");
            AddOnce(en, "Aplikasinya berjalan sebagai administrator", "The app runs as administrator");
            AddOnce(en, "{0} tidak punya jendela", "{0} has no window");
            AddOnce(en, "Tutup lewat aplikasinya atau Task Manager", "Close it from the app or Task Manager");
        }

        static void AddOnce(Dictionary<string, string> en, string id, string text)
        {
            if (!en.ContainsKey(id)) en.Add(id, text);
        }
    }
}
