using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;

namespace Deskmate
{
    // "tadi pagi", "kemarin", "last week": a local time range, and the words that carry no meaning
    static class IngatTime
    {
        static readonly HashSet<string> stop = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            // Indonesian
            "apa", "yang", "saya", "aku", "kamu", "gue", "gw", "tadi", "kemarin", "lusa", "hari", "ini", "itu", "pagi", "siang", "sore", "malam",
            "minggu", "bulan", "lalu", "kapan", "sekarang", "saat", "baru", "saja", "barusan", "terakhir", "selama", "semua", "sama",
            "di", "ke", "dari", "dan", "atau", "untuk", "buat", "dengan", "soal", "tentang", "mengenai", "sedang", "lagi", "sudah", "pernah",
            "baca", "kubaca", "dibaca", "buka", "kubuka", "dibuka", "membuka", "kerja", "kerjakan", "kukerjakan", "dikerjakan", "mengerjakan",
            "lakukan", "kulakukan", "dilakukan", "ngapain", "lihat", "kulihat", "dilihat", "melihat", "tampil", "terlihat", "jelaskan", "ceritakan",
            "rangkum", "rangkuman", "ringkas", "ringkasan", "rekap", "aktivitas", "kegiatan", "konteks", "memori", "ingatan",
            "layar", "jendela", "aplikasi", "halaman", "mana", "siapa", "berapa", "ada", "tidak", "bisa", "jam", "menit", "mnt", "detik", "pukul",
            "dong", "ya", "sih", "nih", "kok",
            // English
            "what", "which", "the", "a", "an", "i", "im", "am", "me", "my", "you", "did", "do", "does", "was", "were", "is", "are",
            "today", "yesterday", "this", "that", "morning", "afternoon", "evening", "night", "week", "month", "last", "ago", "when",
            "now", "right", "just", "current", "currently", "earlier", "recently", "have", "has", "been", "all",
            "about", "on", "in", "at", "to", "of", "for", "and", "or", "with", "from",
            "read", "reading", "open", "opened", "opening", "work", "worked", "working", "doing", "look", "looking", "see", "seeing", "saw", "shown", "showing",
            "summarize", "summary", "recap", "explain", "activity", "activities", "context", "memory",
            "screen", "window", "app", "application", "page", "minutes", "minute", "mins", "min", "hours", "hour", "days", "day",
        };

        static readonly Regex SpanWordsRx = new Regex(
            @"\b\d+\s*(menit|mnt|minutes?|mins?|jam|hours?|hrs?|hari|days?)\s*(terakhir|yang lalu|lalu|ago|last)?\b",
            RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

        public static bool Stop(string w) { return stop.Contains(w); }

        public static string StripSpans(string q)
        {
            return string.IsNullOrEmpty(q) ? "" : SpanWordsRx.Replace(q, " ");
        }

        // How far back "tadi" reaches at the least, whatever the clock says
        const int RecentHours = 6;

        // Null range: no time named, search everything
        public static bool Range(string q, DateTime nowLocal, out DateTime fromLocal, out DateTime toLocal)
        {
            string s = " " + (q ?? "").ToLowerInvariant() + " ";
            var today = nowLocal.Date;
            fromLocal = DateTime.MinValue; toLocal = nowLocal;
            Func<string, bool> has = w => Regex.IsMatch(s, "[^\\p{L}]" + Regex.Escape(w) + "[^\\p{L}]");

            DateTime day = DateTime.MinValue;
            if (has("kemarin lusa") || has("2 hari lalu") || has("dua hari lalu")) day = today.AddDays(-2);
            else if (has("kemarin") || has("yesterday")) day = today.AddDays(-1);
            else if (has("hari ini") || has("today") || has("tadi") || has("barusan") || has("earlier") || has("this morning") || has("tonight")) day = today;

            var ago = Regex.Match(s, "(\\d+)\\s*(hari|days?)\\s*(lalu|yang lalu|ago)");
            if (ago.Success) day = today.AddDays(-int.Parse(ago.Groups[1].Value, CultureInfo.InvariantCulture));

            if (has("minggu lalu") || has("last week")) { fromLocal = today.AddDays(-7 - (((int)today.DayOfWeek + 6) % 7)); toLocal = fromLocal.AddDays(7); return true; }
            if (has("minggu ini") || has("this week")) { fromLocal = today.AddDays(-(((int)today.DayOfWeek + 6) % 7)); toLocal = nowLocal; return true; }
            if (has("bulan lalu") || has("last month")) { var first = new DateTime(today.Year, today.Month, 1); fromLocal = first.AddMonths(-1); toLocal = first; return true; }
            if (has("bulan ini") || has("this month")) { fromLocal = new DateTime(today.Year, today.Month, 1); toLocal = nowLocal; return true; }

            var mins = Regex.Match(s, "(\\d+)\\s*(menit|mnt|minutes?|mins?)\\s*(terakhir|lalu|yang lalu|ago|last)?");
            if (mins.Success && day == DateTime.MinValue) { fromLocal = nowLocal.AddMinutes(-int.Parse(mins.Groups[1].Value, CultureInfo.InvariantCulture)); toLocal = nowLocal; return true; }

            var hours = Regex.Match(s, "(\\d+)\\s*(jam|hours?|hrs?)\\s*(terakhir|lalu|yang lalu|ago|last)?");
            if (hours.Success && day == DateTime.MinValue) { fromLocal = nowLocal.AddHours(-int.Parse(hours.Groups[1].Value, CultureInfo.InvariantCulture)); toLocal = nowLocal; return true; }

            if (day == DateTime.MinValue) return false;
            fromLocal = day; toLocal = day.AddDays(1);
            // Part of the day narrows it
            bool part = true;
            if (has("pagi") || has("morning")) { fromLocal = day.AddHours(4); toLocal = day.AddHours(11); }
            else if (has("siang") || has("afternoon")) { fromLocal = day.AddHours(11); toLocal = day.AddHours(15); }
            else if (has("sore")) { fromLocal = day.AddHours(15); toLocal = day.AddHours(18.5); }
            else if (has("malam") || has("evening") || has("night") || has("tonight")) { fromLocal = day.AddHours(18); toLocal = day.AddDays(1).AddHours(3); }
            else part = false;
            // A part of today that has not started yet means the last one: "tadi malam" at 00:30 is last night
            if (day == today && fromLocal > nowLocal) { fromLocal = fromLocal.AddDays(-1); toLocal = toLocal.AddDays(-1); }
            // "Tadi" and "barusan" mean a while ago, not since midnight: right after 00:00 they still reach the evening before
            if (!part && day == today && (has("tadi") || has("barusan") || has("earlier")))
            {
                var recent = nowLocal.AddHours(-RecentHours);
                if (recent < fromLocal) fromLocal = recent;
            }
            if (toLocal > nowLocal && fromLocal <= nowLocal) toLocal = nowLocal;
            return true;
        }
    }
}
