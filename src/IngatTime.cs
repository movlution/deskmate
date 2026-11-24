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
            "apa", "yang", "saya", "aku", "kamu", "tadi", "kemarin", "lusa", "hari", "ini", "itu", "pagi", "siang", "sore", "malam", "minggu", "bulan", "lalu", "kapan",
            "di", "ke", "dari", "dan", "atau", "untuk", "dengan", "soal", "tentang", "mengenai", "sedang", "lagi", "sudah", "pernah", "baca", "buka", "kerja", "kerjakan",
            "dikerjakan", "lakukan", "dilakukan", "mana", "siapa", "berapa", "ada", "tidak", "bisa", "jam", "pukul", "terakhir", "barusan", "dong", "ya", "sih", "nih", "kok",
            // English
            "what", "which", "the", "a", "an", "i", "me", "my", "you", "did", "do", "was", "were", "is", "are", "today", "yesterday", "this", "that", "morning", "afternoon",
            "evening", "night", "week", "month", "last", "ago", "when", "about", "on", "in", "at", "to", "of", "for", "and", "or", "with", "read", "open", "opened", "work",
            "worked", "working", "doing", "earlier", "recently", "have", "has", "been",
        };

        public static bool Stop(string w) { return stop.Contains(w); }

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

            var hours = Regex.Match(s, "(\\d+)\\s*(jam|hours?)\\s*(terakhir|lalu|yang lalu|ago|last)?");
            if (hours.Success && day == DateTime.MinValue) { fromLocal = nowLocal.AddHours(-int.Parse(hours.Groups[1].Value, CultureInfo.InvariantCulture)); toLocal = nowLocal; return true; }

            if (day == DateTime.MinValue) return false;
            fromLocal = day; toLocal = day.AddDays(1);
            // Part of the day narrows it
            if (has("pagi") || has("morning")) { fromLocal = day.AddHours(4); toLocal = day.AddHours(11); }
            else if (has("siang") || has("afternoon")) { fromLocal = day.AddHours(11); toLocal = day.AddHours(15); }
            else if (has("sore")) { fromLocal = day.AddHours(15); toLocal = day.AddHours(18.5); }
            else if (has("malam") || has("evening") || has("night") || has("tonight")) { fromLocal = day.AddHours(18); toLocal = day.AddDays(1).AddHours(3); }
            if (toLocal > nowLocal && fromLocal <= nowLocal) toLocal = nowLocal;
            return true;
        }
    }
}
