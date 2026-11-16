using System;
using System.Collections.Generic;
using System.Globalization;

namespace Deskmate
{
    // Hisab parameters. One preset follows the region, so the user only picks a place.
    class PrayParams
    {
        public string Key = "mwl", Name = "Muslim World League";
        // Twilight angles in degrees
        public double Fajr = 18, Isha = 17;
        // Isha counted in minutes after Maghrib; the angle is ignored when set
        public int IshaMinutes;
        // Asr shadow factor: 1 standard, 2 Hanafi
        public int AsrFactor = 1;
        // Unmet twilight at high latitude: 0 none, 1 seventh of the night, 2 middle, 3 angle based
        public int HighLat;
        // Ihtiyat margin in minutes, as printed schedules in some countries carry it
        public int Safety;

        public PrayParams Copy() { return (PrayParams)MemberwiseClone(); }
    }

    static class PrayMethods
    {
        // Method names are proper nouns, so they are never translated
        public static readonly PrayParams[] All =
        {
            new PrayParams { Key = "kemenag", Name = "Kemenag", Fajr = 20, Isha = 18, Safety = 2 },
            new PrayParams { Key = "singapore", Name = "MUIS Singapura", Fajr = 20, Isha = 18, Safety = 2 },
            new PrayParams { Key = "mwl", Name = "Muslim World League", Fajr = 18, Isha = 17 },
            new PrayParams { Key = "isna", Name = "ISNA", Fajr = 15, Isha = 15 },
            new PrayParams { Key = "egypt", Name = "Egyptian General Authority", Fajr = 19.5, Isha = 17.5 },
            new PrayParams { Key = "makkah", Name = "Umm al-Qura", Fajr = 18.5, IshaMinutes = 90 },
            new PrayParams { Key = "karachi", Name = "University of Karachi", Fajr = 18, Isha = 18, AsrFactor = 2 },
            new PrayParams { Key = "dubai", Name = "Dubai", Fajr = 18.2, Isha = 18.2 },
            new PrayParams { Key = "kuwait", Name = "Kuwait", Fajr = 18, Isha = 17.5 },
            new PrayParams { Key = "qatar", Name = "Qatar", Fajr = 18, IshaMinutes = 90 },
            new PrayParams { Key = "turkey", Name = "Diyanet", Fajr = 18, Isha = 17 },
            new PrayParams { Key = "france", Name = "UOIF", Fajr = 12, Isha = 12 },
            new PrayParams { Key = "russia", Name = "Dewan Mufti Rusia", Fajr = 16, Isha = 15 },
            new PrayParams { Key = "tehran", Name = "Tehran", Fajr = 17.7, Isha = 14 },
            new PrayParams { Key = "jafari", Name = "Jafari", Fajr = 16, Isha = 14 },
            new PrayParams { Key = "moonsight", Name = "Moonsighting Committee", Fajr = 18, Isha = 18 },
        };

        public static PrayParams Find(string key)
        {
            if (string.IsNullOrEmpty(key)) return null;
            foreach (var m in All) if (string.Equals(m.Key, key, StringComparison.OrdinalIgnoreCase)) return m;
            return null;
        }

        static readonly Dictionary<string, string> countryName = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        // Proper name of a method, for the settings row and the notch caption
        public static string NameOf(string key)
        {
            var m = Find(key);
            return m != null ? m.Name : "Muslim World League";
        }

        // The convention each country's authority actually publishes against.
        // An empty country is old data from before regions were worldwide, which was Indonesia only.
        public static string KeyFor(string country)
        {
            switch ((country ?? "").ToUpperInvariant())
            {
                case "": case "ID": return "kemenag";
                case "MY": case "SG": case "BN": return "singapore";
                case "SA": return "makkah";
                case "AE": return "dubai";
                case "KW": return "kuwait";
                case "QA": case "BH": return "qatar";
                case "PK": case "IN": case "BD": case "AF": case "LK": case "NP": case "MV": return "karachi";
                case "US": case "CA": return "isna";
                case "EG": case "SD": case "LY": case "TN": case "DZ": case "MA": case "MR": return "egypt";
                case "TR": return "turkey";
                case "FR": return "france";
                case "RU": case "KZ": case "UZ": case "KG": case "TJ": case "AZ": case "TM": return "russia";
                case "IR": return "tehran";
            }
            return "mwl";
        }

        // The region alone decides the method: there is nothing else for the user to choose.
        // Past 48 degrees the twilight angle can go unmet in summer, so the night portion applies.
        public static PrayParams Pick(string country, double lat)
        {
            var p = Find(KeyFor(country)).Copy();
            p.HighLat = Math.Abs(lat) >= 48 ? 1 : 0;
            return p;
        }
    }

    // A place keeps its IANA zone, so summer time follows the calendar instead of a frozen offset
    static class PrayZone
    {
        public static double Offset(string tzid, double fallback, DateTime day)
        {
            var tz = Resolve(tzid);
            if (tz == null) return fallback;
            try
            {
                // This day's noon at the place, as an instant to ask the zone about
                var probe = DateTime.SpecifyKind(day.Date.AddHours(12) - TimeSpan.FromHours(fallback), DateTimeKind.Utc);
                return tz.GetUtcOffset(probe).TotalHours;
            }
            catch (Exception) { return fallback; }
        }

        static System.TimeZoneInfo Resolve(string tzid)
        {
            if (string.IsNullOrEmpty(tzid)) return null;
            // .NET Framework on Windows wants a Windows id; ICU maps the IANA one
            string win = Ics.WindowsId(tzid);
            if (string.IsNullOrEmpty(win)) return null;
            try { return System.TimeZoneInfo.FindSystemTimeZoneById(win); }
            catch (Exception) { return null; }
        }

        // UTC+7, UTC+5:30
        public static string Label(double offset)
        {
            var ts = TimeSpan.FromHours(offset);
            int h = (int)ts.TotalHours, m = Math.Abs(ts.Minutes);
            return "UTC" + (ts < TimeSpan.Zero ? "-" : "+") + Math.Abs(h).ToString(CultureInfo.InvariantCulture) +
                   (m == 0 ? "" : ":" + m.ToString("00", CultureInfo.InvariantCulture));
        }
    }

    // A city standing for each zone, so a region still resolves when the lookup cannot run.
    // Every entry carries its IANA id, so summer time follows the calendar as usual.
    class ZoneChoice
    {
        public string City, Country, Code, Tzid;
        public double Lat, Lng;
    }

    static class PrayRegions
    {
        // Ordered by longitude east to west, which reads like a map
        public static readonly ZoneChoice[] All =
        {
            new ZoneChoice { City = "Auckland", Country = "Selandia Baru", Code = "NZ", Tzid = "Pacific/Auckland", Lat = -36.8485, Lng = 174.7633 },
            new ZoneChoice { City = "Sydney", Country = "Australia", Code = "AU", Tzid = "Australia/Sydney", Lat = -33.8688, Lng = 151.2093 },
            new ZoneChoice { City = "Tokyo", Country = "Jepang", Code = "JP", Tzid = "Asia/Tokyo", Lat = 35.6762, Lng = 139.6503 },
            new ZoneChoice { City = "Seoul", Country = "Korea Selatan", Code = "KR", Tzid = "Asia/Seoul", Lat = 37.5665, Lng = 126.9780 },
            new ZoneChoice { City = "Shanghai", Country = "Tiongkok", Code = "CN", Tzid = "Asia/Shanghai", Lat = 31.2304, Lng = 121.4737 },
            new ZoneChoice { City = "Hong Kong", Country = "Hong Kong", Code = "HK", Tzid = "Asia/Hong_Kong", Lat = 22.3193, Lng = 114.1694 },
            new ZoneChoice { City = "Singapura", Country = "Singapura", Code = "SG", Tzid = "Asia/Singapore", Lat = 1.3521, Lng = 103.8198 },
            new ZoneChoice { City = "Kuala Lumpur", Country = "Malaysia", Code = "MY", Tzid = "Asia/Kuala_Lumpur", Lat = 3.1390, Lng = 101.6869 },
            new ZoneChoice { City = "Jakarta", Country = "Indonesia", Code = "ID", Tzid = "Asia/Jakarta", Lat = -6.2088, Lng = 106.8456 },
            new ZoneChoice { City = "Bangkok", Country = "Thailand", Code = "TH", Tzid = "Asia/Bangkok", Lat = 13.7563, Lng = 100.5018 },
            new ZoneChoice { City = "Yangon", Country = "Myanmar", Code = "MM", Tzid = "Asia/Yangon", Lat = 16.8661, Lng = 96.1951 },
            new ZoneChoice { City = "Dhaka", Country = "Bangladesh", Code = "BD", Tzid = "Asia/Dhaka", Lat = 23.8103, Lng = 90.4125 },
            new ZoneChoice { City = "Kathmandu", Country = "Nepal", Code = "NP", Tzid = "Asia/Kathmandu", Lat = 27.7172, Lng = 85.3240 },
            new ZoneChoice { City = "Delhi", Country = "India", Code = "IN", Tzid = "Asia/Kolkata", Lat = 28.6139, Lng = 77.2090 },
            new ZoneChoice { City = "Karachi", Country = "Pakistan", Code = "PK", Tzid = "Asia/Karachi", Lat = 24.8607, Lng = 67.0011 },
            new ZoneChoice { City = "Dubai", Country = "Uni Emirat Arab", Code = "AE", Tzid = "Asia/Dubai", Lat = 25.2048, Lng = 55.2708 },
            new ZoneChoice { City = "Tehran", Country = "Iran", Code = "IR", Tzid = "Asia/Tehran", Lat = 35.6892, Lng = 51.3890 },
            new ZoneChoice { City = "Makkah", Country = "Arab Saudi", Code = "SA", Tzid = "Asia/Riyadh", Lat = 21.4225, Lng = 39.8262 },
            new ZoneChoice { City = "Istanbul", Country = "Turki", Code = "TR", Tzid = "Europe/Istanbul", Lat = 41.0082, Lng = 28.9784 },
            new ZoneChoice { City = "Kairo", Country = "Mesir", Code = "EG", Tzid = "Africa/Cairo", Lat = 30.0444, Lng = 31.2357 },
            new ZoneChoice { City = "Johannesburg", Country = "Afrika Selatan", Code = "ZA", Tzid = "Africa/Johannesburg", Lat = -26.2041, Lng = 28.0473 },
            new ZoneChoice { City = "Berlin", Country = "Jerman", Code = "DE", Tzid = "Europe/Berlin", Lat = 52.5200, Lng = 13.4050 },
            new ZoneChoice { City = "Paris", Country = "Prancis", Code = "FR", Tzid = "Europe/Paris", Lat = 48.8566, Lng = 2.3522 },
            new ZoneChoice { City = "London", Country = "Inggris", Code = "GB", Tzid = "Europe/London", Lat = 51.5074, Lng = -0.1278 },
            new ZoneChoice { City = "Moskwa", Country = "Rusia", Code = "RU", Tzid = "Europe/Moscow", Lat = 55.7558, Lng = 37.6173 },
            new ZoneChoice { City = "Oslo", Country = "Norwegia", Code = "NO", Tzid = "Europe/Oslo", Lat = 59.9139, Lng = 10.7522 },
            new ZoneChoice { City = "Sao Paulo", Country = "Brasil", Code = "BR", Tzid = "America/Sao_Paulo", Lat = -23.5505, Lng = -46.6333 },
            new ZoneChoice { City = "New York", Country = "Amerika Serikat", Code = "US", Tzid = "America/New_York", Lat = 40.7128, Lng = -74.0060 },
            new ZoneChoice { City = "Chicago", Country = "Amerika Serikat", Code = "US", Tzid = "America/Chicago", Lat = 41.8781, Lng = -87.6298 },
            new ZoneChoice { City = "Los Angeles", Country = "Amerika Serikat", Code = "US", Tzid = "America/Los_Angeles", Lat = 34.0522, Lng = -118.2437 },
            new ZoneChoice { City = "Toronto", Country = "Kanada", Code = "CA", Tzid = "America/Toronto", Lat = 43.6532, Lng = -79.3832 },
        };

        public static ZoneChoice Find(string tzid)
        {
            if (string.IsNullOrEmpty(tzid)) return null;
            foreach (var z in All) if (string.Equals(z.Tzid, tzid, StringComparison.OrdinalIgnoreCase)) return z;
            return null;
        }

        // Offline match by what the user typed: the city, or a country name
        public static List<ZoneChoice> Match(string typed, int max)
        {
            var hits = new List<ZoneChoice>(max);
            typed = (typed ?? "").Trim();
            if (typed.Length < 3) return hits;
            foreach (var z in All)
            {
                if (hits.Count >= max) break;
                if (z.City.StartsWith(typed, StringComparison.OrdinalIgnoreCase)) hits.Add(z);
            }
            if (hits.Count >= max) return hits;
            foreach (var z in All)
            {
                if (hits.Count >= max) break;
                if (hits.Contains(z)) continue;
                if (z.City.IndexOf(typed, StringComparison.OrdinalIgnoreCase) >= 0 ||
                    z.Country.IndexOf(typed, StringComparison.OrdinalIgnoreCase) >= 0) hits.Add(z);
            }
            return hits;
        }
    }
}
