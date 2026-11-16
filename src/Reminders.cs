using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace Deskmate
{
    // Prayer times computed locally, nothing fetched
    static class PrayTimes
    {
        public static readonly string[] Names = { "Subuh", "Terbit", "Dzuhur", "Ashar", "Maghrib", "Isya" };

        // Angles, Asr factor and the high-latitude rule come from the region's method
        public static DateTime[] For(DateTime day, double lat, double lng, double tz, PrayParams prm)
        {
            if (prm == null) prm = PrayMethods.Pick("", lat);
            double jd = Julian(day.Year, day.Month, day.Day) - lng / (15 * 24.0);
            // Inside the polar circles the sun can miss the horizon for the whole day, and then
            // no sunrise or sunset exists to anchor the schedule. Nearest-latitude rule: keep the
            // longitude and use the closest latitude that still has one.
            double latAt = UsableLat(jd, lat);
            double fajr = AngleTime(jd, prm.Fajr, 5 / 24.0, true, latAt);
            double sunrise = AngleTime(jd, 0.833, 6 / 24.0, true, latAt);
            double dhuhr = MidDay(jd, 12 / 24.0);
            double asr = AsrTime(jd, prm.AsrFactor, 13 / 24.0, latAt);
            double sunset = AngleTime(jd, 0.833, 18 / 24.0, false, latAt);
            // Umm al-Qura and Qatar count Isha in minutes, not degrees
            double isha = prm.IshaMinutes > 0 ? sunset + prm.IshaMinutes / 60.0 : AngleTime(jd, prm.Isha, 18 / 24.0, false, latAt);
            if (prm.HighLat > 0) HighLat(fajr, sunrise, sunset, prm, ref fajr, ref isha);
            double[] hours = { fajr, sunrise, dhuhr, asr, sunset, isha };
            // Ihtiyat: the small margin printed schedules carry. Kemenag and MUIS keep it, others do not
            int pad = prm.Safety;
            int[] safety = { pad, -pad, pad, pad, pad, pad };
            var result = new DateTime[6];
            for (int i = 0; i < 6; i++)
            {
                double h = hours[i] + tz - lng / 15;
                result[i] = day.Date.AddHours(h).AddMinutes(safety[i]);
                // Round up, like printed schedules
                if (result[i].Second > 0 || result[i].Millisecond > 0) result[i] = result[i].AddSeconds(60 - result[i].Second).AddMilliseconds(-result[i].Millisecond);
            }
            return result;
        }

        // The latitude to compute sunrise and sunset at. Polar day and polar night have no
        // crossing at all, which would collapse sunrise onto sunset and break the order, so
        // the nearest latitude that does have one is used, keeping the place's own longitude.
        static double UsableLat(double jd, double lat)
        {
            if (Horizon(jd, lat, Math.Sign(lat))) return lat;
            double step = lat > 0 ? -0.5 : 0.5;
            for (int i = 1; i <= 100; i++)
            {
                double probe = lat + step * i;
                if (Math.Abs(probe) > 90) break;
                if (Horizon(jd, probe, Math.Sign(probe))) return probe;
            }
            return lat;
        }

        // True when the sun's centre can reach the horizon angle for some hour of the day
        static bool Horizon(double jd, double lat, int sign)
        {
            double decl, eqt;
            Sun(jd + 6 / 24.0, out decl, out eqt);
            double denom = Math.Cos(Rad(decl)) * Math.Cos(Rad(lat));
            if (Math.Abs(denom) < 1e-6) return false;
            double cos = (-Math.Sin(Rad(0.833)) - Math.Sin(Rad(decl)) * Math.Sin(Rad(lat))) / denom;
            // A clear crossing, not a scrape along the horizon
            return cos > -0.999 && cos < 0.999;
        }

        // Past roughly 48 degrees the twilight angle is never reached in summer.
        // The night is then split by a fixed portion, which is what published tables there do.
        static void HighLat(double rawFajr, double sunrise, double sunset, PrayParams prm, ref double fajr, ref double isha)
        {
            double night = 24 - (sunset - sunrise);
            if (night <= 0 || night >= 24) return;
            double portion = prm.HighLat == 2 ? 1 / 2.0 : prm.HighLat == 3 ? prm.Fajr / 60.0 : 1 / 7.0;
            double ishaPortion = prm.HighLat == 3 ? (prm.IshaMinutes > 0 ? 1 / 7.0 : prm.Isha / 60.0) : portion;
            double safeFajr = sunrise - portion * night;
            double safeIsha = sunset + ishaPortion * night;
            if (rawFajr < safeFajr) fajr = safeFajr;
            if (isha > safeIsha) isha = safeIsha;
        }

        static double Julian(int y, int m, int d)
        {
            if (m <= 2) { y -= 1; m += 12; }
            double a = Math.Floor(y / 100.0), b = 2 - a + Math.Floor(a / 4);
            return Math.Floor(365.25 * (y + 4716)) + Math.Floor(30.6001 * (m + 1)) + d + b - 1524.5;
        }

        static double Rad(double d) { return d * Math.PI / 180; }
        static double Deg(double r) { return r * 180 / Math.PI; }
        static double Fix(double a, double b) { a = a - b * Math.Floor(a / b); return a < 0 ? a + b : a; }

        // Declination and equation of time
        static void Sun(double jd, out double decl, out double eqt)
        {
            double d = jd - 2451545.0;
            double g = Fix(357.529 + 0.98560028 * d, 360), q = Fix(280.459 + 0.98564736 * d, 360);
            double l = Fix(q + 1.915 * Math.Sin(Rad(g)) + 0.020 * Math.Sin(Rad(2 * g)), 360);
            double e = 23.439 - 0.00000036 * d;
            double ra = Deg(Math.Atan2(Math.Cos(Rad(e)) * Math.Sin(Rad(l)), Math.Cos(Rad(l)))) / 15;
            eqt = q / 15 - Fix(ra, 24);
            decl = Deg(Math.Asin(Math.Sin(Rad(e)) * Math.Sin(Rad(l))));
        }

        static double MidDay(double jd, double t)
        {
            double decl, eqt;
            Sun(jd + t, out decl, out eqt);
            return Fix(12 - eqt, 24);
        }

        static double AngleTime(double jd, double angle, double t, bool before, double lat)
        {
            double decl, eqt;
            Sun(jd + t, out decl, out eqt);
            double noon = MidDay(jd, t);
            double cos = (-Math.Sin(Rad(angle)) - Math.Sin(Rad(decl)) * Math.Sin(Rad(lat))) / (Math.Cos(Rad(decl)) * Math.Cos(Rad(lat)));
            double span = Deg(Math.Acos(Math.Max(-1, Math.Min(1, cos)))) / 15;
            return noon + (before ? -span : span);
        }

        static double AsrTime(double jd, double factor, double t, double lat)
        {
            double decl, eqt;
            Sun(jd + t, out decl, out eqt);
            double angle = -Deg(Math.Atan(1 / (factor + Math.Tan(Rad(Math.Abs(lat - decl))))));
            return AngleTime(jd, angle, t, false, lat);
        }
    }

    // City presets: name, latitude, longitude, UTC offset
    static class Cities
    {
        // "Name|lat|lng|tz|cc|Country|tzid", the form a searched place is saved in.
        // The four-part form from older versions still parses, with an empty region.
        public const char Sep = '|';

        public static string Pack(string name, double lat, double lng, double tz, string cc, string country, string tzid, string province = "")
        {
            var inv = System.Globalization.CultureInfo.InvariantCulture;
            return name.Replace(Sep, ' ') + Sep + lat.ToString("0.####", inv) + Sep + lng.ToString("0.####", inv) + Sep +
                   tz.ToString("0.##", inv) + Sep + (cc ?? "").Replace(Sep, ' ') + Sep + (country ?? "").Replace(Sep, ' ') + Sep +
                   (tzid ?? "").Replace(Sep, ' ') + Sep + (province ?? "").Replace(Sep, ' ');
        }

        public static bool IsPacked(string s) { return s != null && s.IndexOf(Sep) > 0; }

        public static string NameOf(string s)
        {
            if (!IsPacked(s)) return s ?? "";
            return s.Substring(0, s.IndexOf(Sep));
        }

        // {name, lat, lng, offset, cc, country, tzid}
        public static object[] Parse(string s)
        {
            if (!IsPacked(s)) return null;
            var parts = s.Split(Sep);
            if (parts.Length < 4) return null;
            double lat, lng, tz;
            var inv = System.Globalization.CultureInfo.InvariantCulture;
            if (!double.TryParse(parts[1], System.Globalization.NumberStyles.Float, inv, out lat)) return null;
            if (!double.TryParse(parts[2], System.Globalization.NumberStyles.Float, inv, out lng)) return null;
            if (!double.TryParse(parts[3], System.Globalization.NumberStyles.Float, inv, out tz)) return null;
            string cc = parts.Length > 4 ? parts[4] : "", country = parts.Length > 5 ? parts[5] : "", tzid = parts.Length > 6 ? parts[6] : "";
            string province = parts.Length > 7 ? parts[7] : "";
            return new object[] { parts[0], lat, lng, tz, cc, country, tzid, province };
        }

        // Region line for the notch: "Garut · Jawa Barat", or "Mecca · Saudi Arabia"
        public static string RegionOf(object[] c)
        {
            if (c == null) return "";
            // A province is more useful than the country once it is known
            string province = c.Length > 7 ? (string)c[7] : "";
            if (!string.IsNullOrEmpty(province)) return province;
            string country = c.Length > 5 ? (string)c[5] : "";
            return country ?? "";
        }

        public static string ProvinceOf(object[] c)
        {
            return c != null && c.Length > 7 ? (string)c[7] ?? "" : "";
        }

        public static string CountryOf(object[] c)
        {
            if (c == null || c.Length < 6) return "";
            return (string)c[5] ?? "";
        }

        public static string TzidOf(object[] c)
        {
            if (c == null || c.Length < 7) return "";
            return (string)c[6] ?? "";
        }

        // Method presets key off the country, so the code is what matters, not the display name
        public static string CountryCode(object[] c)
        {
            if (c == null || c.Length < 5) return "";
            return (string)c[4] ?? "";
        }

        // The offset this place really uses on the given day, summer time included
        public static double OffsetOn(object[] c, DateTime day)
        {
            if (c == null) return 0;
            string tzid = c.Length > 6 ? (string)c[6] : "";
            return PrayZone.Offset(tzid, Convert.ToDouble(c[3]), day);
        }

        public static object[] Find(string name)
        {
            if (string.IsNullOrEmpty(name)) return null;
            string bare = Bare(name);
            foreach (var c in List)
                if (string.Equals((string)c[0], bare, StringComparison.OrdinalIgnoreCase)) return Widen(c);
            // A world region picked from the offline list
            foreach (var z in PrayRegions.All)
                if (string.Equals(z.City, bare, StringComparison.OrdinalIgnoreCase)) return FromRegion(z);
            return null;
        }

        // A row of the world list turned into the same shape a searched place has
        public static object[] FromRegion(ZoneChoice z)
        {
            return new object[] { z.City, z.Lat, z.Lng, PrayZone.Offset(z.Tzid, 0, DateTime.Today), z.Code, z.Country, z.Tzid, "" };
        }

        // Built-in rows carry five fields; the rest of the code reads eight.
        // The built-in list is Indonesian, and Indonesia keeps one offset all year.
        static object[] Widen(object[] c)
        {
            if (c.Length >= 8) return c;
            string province = c.Length > 4 ? (string)c[4] : "";
            return new object[] { c[0], c[1], c[2], c[3], "ID", "Indonesia", "", province };
        }

        // What a place is shown as. The province is what tells two same-named districts
        // apart, so it belongs in the list rather than in a hidden field. Accepts both a
        // raw built-in row and a widened one, where the province moved to the end.
        public static string LabelOf(object[] c)
        {
            if (c == null) return "";
            string name = (string)c[0];
            string province = c.Length >= 8 ? (string)c[7] : c.Length > 4 ? (string)c[4] : "";
            return string.IsNullOrEmpty(province) ? name : name + " · " + province;
        }

        // A picked label carries its province; only the name before it identifies the place
        static string Bare(string typed)
        {
            int dot = typed.IndexOf(" · ", StringComparison.Ordinal);
            return dot > 0 ? typed.Substring(0, dot).Trim() : typed.Trim();
        }

        // True when the text starts any word of the phrase, ignoring case
        static bool WordStarts(string phrase, string typed)
        {
            if (string.IsNullOrEmpty(phrase) || string.IsNullOrEmpty(typed)) return false;
            if (phrase.StartsWith(typed, StringComparison.OrdinalIgnoreCase)) return true;
            for (int i = 1; i < phrase.Length; i++)
                if (phrase[i - 1] == ' ' && string.Compare(phrase, i, typed, 0, typed.Length, StringComparison.OrdinalIgnoreCase) == 0) return true;
            return false;
        }

        // A place object turned back into the stored form, province included
        public static string PackOf(object[] c)
        {
            if (c == null) return "";
            return Pack((string)c[0], Convert.ToDouble(c[1]), Convert.ToDouble(c[2]), Convert.ToDouble(c[3]),
                        CountryCode(c), CountryOf(c), TzidOf(c), ProvinceOf(c));
        }

        // Already offered under some province, so the world list should not repeat it
        static bool HasBare(List<string> hits, string name)
        {
            foreach (var h in hits)
                if (string.Equals(Bare(h), name, StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        // Up to three names matching what has been typed so far
        public static List<string> Suggest(string typed, int max)
        {
            var hits = new List<string>(max);
            typed = (typed ?? "").Trim();
            if (typed.Length == 0) return hits;
            // Typing a province outright means that province, so its places lead
            foreach (var c in List)
            {
                if (hits.Count >= max) break;
                string province = c.Length > 4 ? (string)c[4] : "";
                if (!string.Equals(province, typed, StringComparison.OrdinalIgnoreCase)) continue;
                string lb = LabelOf(c);
                if (!hits.Contains(lb)) hits.Add(lb);
            }
            if (hits.Count >= max) return hits;
            // Names starting with the text next: they are what people expect
            foreach (var c in List)
            {
                if (hits.Count >= max) break;
                if (((string)c[0]).StartsWith(typed, StringComparison.OrdinalIgnoreCase))
                {
                    string lb = LabelOf(c);
                    if (!hits.Contains(lb)) hits.Add(lb);
                }
            }
            // A province name is a natural way in: "Bali" should reach Denpasar, not Balige.
            // Matched at word starts only, or "gar" would pull in Nusa Tenggara Barat.
            if (hits.Count >= max) return hits;
            foreach (var c in List)
            {
                if (hits.Count >= max) break;
                string province = c.Length > 4 ? (string)c[4] : "";
                if (!WordStarts(province, typed)) continue;
                string lb = LabelOf(c);
                if (!hits.Contains(lb)) hits.Add(lb);
            }
            if (hits.Count >= max) return hits;
            foreach (var c in List)
            {
                if (hits.Count >= max) break;
                if (((string)c[0]).IndexOf(typed, StringComparison.OrdinalIgnoreCase) > 0)
                {
                    string lb = LabelOf(c);
                    if (!hits.Contains(lb)) hits.Add(lb);
                }
            }
            // The world list, by city or by country, so a region can be found with no network
            if (hits.Count >= max) return hits;
            foreach (var z in PrayRegions.All)
            {
                if (hits.Count >= max) break;
                if (HasBare(hits, z.City)) continue;
                if (z.City.StartsWith(typed, StringComparison.OrdinalIgnoreCase) ||
                    z.Country.IndexOf(typed, StringComparison.OrdinalIgnoreCase) >= 0) hits.Add(z.City);
            }
            return hits;
        }

        // Built-in names first, then anything already searched and saved
        public static List<string> Names()
        {
            var names = new List<string>(List.Length + extra.Count);
            foreach (var c in List) names.Add((string)c[0]);
            foreach (var e in extra) names.Add(e);
            return names;
        }

        static readonly List<string> extra = new List<string>();

        public static void Remember(string packed)
        {
            if (!IsPacked(packed)) return;
            if (extra.Contains(packed)) return;
            extra.Add(packed);
        }

        // Online lookup: any place in the world, with its own zone
        public static List<object[]> Search(string query, out string error)
        {
            error = null;
            var found = new List<object[]>();
            if (string.IsNullOrEmpty(query) || query.Trim().Length < 3) return found;
            // Open-Meteo geocoding: same provider as the weather card, no key, returns the IANA zone
            string url = "https://geocoding-api.open-meteo.com/v1/search?count=8&language=en&format=json&name=" +
                         Uri.EscapeDataString(query.Trim());
            try
            {
                var r = Fetch.Send("GET", url, null, null, 128 * 1024, 8000);
                if (!r.Ok) { error = "HTTP " + r.Status; return Offline(query, ref error); }
                var root = Json.Parse(r.Text);
                var arr = Json.Arr(root, "results");
                foreach (var o in arr)
                {
                    string name = Json.Str(o, "name");
                    double lat = Json.Num(o, "latitude", double.MinValue), lng = Json.Num(o, "longitude", double.MinValue);
                    if (name.Length == 0 || lat == double.MinValue || lng == double.MinValue) continue;
                    string cc = Json.Str(o, "country_code"), country = Json.Str(o, "country"), tzid = Json.Str(o, "timezone");
                    if (country.Length == 0) country = cc;
                    // The offset is only a starting point; the zone id keeps summer time correct later
                    double tz = PrayZone.Offset(tzid, 0, DateTime.Today);
                    found.Add(new object[] { name, lat, lng, tz, cc, country, tzid });
                }
            }
            catch (Exception ex) { error = ex.GetType().Name; }
            if (found.Count == 0) return Offline(query, ref error);
            return found;
        }

        // Last resort: the built-in world list, so a region can still be chosen offline
        static List<object[]> Offline(string query, ref string error)
        {
            var found = new List<object[]>();
            foreach (var z in PrayRegions.Match(query, 8)) found.Add(FromRegion(z));
            // The list answered, so the network failure is no longer news
            if (found.Count > 0) error = null;
            return found;
        }

        public static readonly object[][] List =
        {
new object[] { "Banda Aceh", 5.5483, 95.3238, 7, "Aceh" }, new object[] { "Lhokseumawe", 5.1801, 97.1507, 7, "Aceh" }, new object[] { "Langsa", 4.4683, 97.9683, 7, "Aceh" },
            new object[] { "Sabang", 5.8894, 95.3222, 7, "Aceh" }, new object[] { "Meulaboh", 4.1363, 96.1285, 7, "Aceh" }, new object[] { "Tapaktuan", 3.2622, 97.1786, 7, "Aceh" },
            new object[] { "Kutacane", 3.4766, 97.8128, 7, "Aceh" }, new object[] { "Takengon", 4.6269, 96.8444, 7, "Aceh" }, new object[] { "Sigli", 5.3847, 95.9608, 7, "Aceh" },
            new object[] { "Calang", 4.6309, 95.5861, 7, "Aceh" }, new object[] { "Blangpidie", 3.7564, 96.8981, 7, "Aceh" }, new object[] { "Medan", 3.5952, 98.6722, 7, "Sumatera Utara" },
            new object[] { "Binjai", 3.6001, 98.4855, 7, "Sumatera Utara" }, new object[] { "Pematang Siantar", 2.9595, 99.0687, 7, "Sumatera Utara" }, new object[] { "Tebing Tinggi", 3.3285, 99.1625, 7, "Sumatera Utara" },
            new object[] { "Tanjungbalai", 2.9667, 99.8, 7, "Sumatera Utara" }, new object[] { "Rantau Prapat", 2.3329, 100.0483, 7, "Sumatera Utara" }, new object[] { "Sibolga", 1.7427, 98.7792, 7, "Sumatera Utara" },
            new object[] { "Padang Sidempuan", 1.3739, 99.2681, 7, "Sumatera Utara" }, new object[] { "Gunungsitoli", 1.2884, 97.6144, 7, "Sumatera Utara" }, new object[] { "Kabanjahe", 3.1001, 98.4908, 7, "Sumatera Utara" },
            new object[] { "Balige", 2.3328, 99.0667, 7, "Sumatera Utara" }, new object[] { "Tarutung", 2.0167, 98.9667, 7, "Sumatera Utara" }, new object[] { "Kisaran", 2.9833, 99.6167, 7, "Sumatera Utara" },
            new object[] { "Limapuluh", 3.1667, 99.4667, 7, "Sumatera Utara" }, new object[] { "Stabat", 3.7333, 98.45, 7, "Sumatera Utara" }, new object[] { "Lubuk Pakam", 3.5586, 98.8742, 7, "Sumatera Utara" },
            new object[] { "Padang", -0.9471, 100.4172, 7, "Sumatera Barat" }, new object[] { "Bukittinggi", -0.3055, 100.3692, 7, "Sumatera Barat" }, new object[] { "Payakumbuh", -0.2294, 100.6317, 7, "Sumatera Barat" },
            new object[] { "Solok", -0.7893, 100.6543, 7, "Sumatera Barat" }, new object[] { "Padang Panjang", -0.4711, 100.4006, 7, "Sumatera Barat" }, new object[] { "Pariaman", -0.6261, 100.1206, 7, "Sumatera Barat" },
            new object[] { "Painan", -1.3492, 100.5736, 7, "Sumatera Barat" }, new object[] { "Lubuk Sikaping", 0.1394, 100.1653, 7, "Sumatera Barat" }, new object[] { "Batusangkar", -0.4556, 100.5778, 7, "Sumatera Barat" },
            new object[] { "Sijunjung", -0.6853, 100.9512, 7, "Sumatera Barat" }, new object[] { "Pasaman", 0.2, 100.2, 7, "Sumatera Barat" }, new object[] { "Pulau Punjung", -1.0, 101.3, 7, "Sumatera Barat" },
            new object[] { "Pekanbaru", 0.5071, 101.4478, 7, "Riau" }, new object[] { "Dumai", 1.6819, 101.4472, 7, "Riau" }, new object[] { "Bengkalis", 1.4667, 102.0833, 7, "Riau" },
            new object[] { "Bagansiapiapi", 2.1667, 100.8167, 7, "Riau" }, new object[] { "Bangkinang", 0.3333, 101.0167, 7, "Riau" }, new object[] { "Pasir Pengaraian", 0.8667, 100.3, 7, "Riau" },
            new object[] { "Tembilahan", -0.3167, 103.1667, 7, "Riau" }, new object[] { "Rengat", -0.3667, 102.55, 7, "Riau" }, new object[] { "Teluk Kuantan", -0.5333, 101.5667, 7, "Riau" },
            new object[] { "Siak Sri Indrapura", 0.8, 102.0, 7, "Riau" }, new object[] { "Selat Panjang", 0.9833, 102.7, 7, "Riau" }, new object[] { "Jambi", -1.6101, 103.6131, 7, "Jambi" },
            new object[] { "Sungai Penuh", -2.0564, 101.3906, 7, "Jambi" }, new object[] { "Kuala Tungkal", -0.8167, 103.4667, 7, "Jambi" }, new object[] { "Muara Bungo", -1.4833, 102.1167, 7, "Jambi" },
            new object[] { "Sarolangun", -2.3, 102.7, 7, "Jambi" }, new object[] { "Muara Tebo", -1.4667, 102.3667, 7, "Jambi" }, new object[] { "Bangko", -2.0833, 102.2667, 7, "Jambi" },
            new object[] { "Muara Bulian", -1.7167, 103.2667, 7, "Jambi" }, new object[] { "Palembang", -2.9761, 104.7754, 7, "Sumatera Selatan" }, new object[] { "Prabumulih", -3.4328, 104.2357, 7, "Sumatera Selatan" },
            new object[] { "Lubuk Linggau", -3.2965, 102.8614, 7, "Sumatera Selatan" }, new object[] { "Pagar Alam", -4.0247, 103.2486, 7, "Sumatera Selatan" }, new object[] { "Baturaja", -4.1333, 104.1667, 7, "Sumatera Selatan" },
            new object[] { "Indralaya", -3.2333, 104.65, 7, "Sumatera Selatan" }, new object[] { "Kayuagung", -3.3833, 104.8333, 7, "Sumatera Selatan" }, new object[] { "Lahat", -3.8, 103.5333, 7, "Sumatera Selatan" },
            new object[] { "Muara Enim", -3.65, 103.7667, 7, "Sumatera Selatan" }, new object[] { "Sekayu", -2.8667, 103.85, 7, "Sumatera Selatan" }, new object[] { "Pangkalan Balai", -2.9, 104.4, 7, "Sumatera Selatan" },
            new object[] { "Bandar Lampung", -5.4292, 105.261, 7, "Lampung" }, new object[] { "Metro", -5.1131, 105.3067, 7, "Lampung" }, new object[] { "Kalianda", -5.7333, 105.5833, 7, "Lampung" },
            new object[] { "Liwa", -5.0333, 104.0833, 7, "Lampung" }, new object[] { "Kota Agung", -5.5, 104.6333, 7, "Lampung" }, new object[] { "Pringsewu", -5.35, 104.9667, 7, "Lampung" },
            new object[] { "Gedong Tataan", -5.3833, 105.1, 7, "Lampung" }, new object[] { "Blambangan Umpu", -4.5, 104.55, 7, "Lampung" }, new object[] { "Serang", -6.12, 106.1503, 7, "Banten" },
            new object[] { "Cilegon", -6.0027, 106.0114, 7, "Banten" }, new object[] { "Pandeglang", -6.3086, 106.1053, 7, "Banten" }, new object[] { "Rangkasbitung", -6.3592, 106.2497, 7, "Banten" },
            new object[] { "Tangerang", -6.1783, 106.6319, 7, "Banten" }, new object[] { "Tangerang Selatan", -6.2886, 106.7175, 7, "Banten" }, new object[] { "Ciputat", -6.3111, 106.7453, 7, "Jawa Barat" },
            new object[] { "Balaraja", -6.2, 106.4333, 7, "Jawa Barat" }, new object[] { "Curug", -6.25, 106.5667, 7, "Banten" }, new object[] { "Jakarta", -6.2088, 106.8456, 7, "DKI Jakarta" },
            new object[] { "Jakarta Utara", -6.1214, 106.7741, 7, "DKI Jakarta" }, new object[] { "Jakarta Pusat", -6.1862, 106.834, 7, "DKI Jakarta" }, new object[] { "Jakarta Timur", -6.225, 106.9, 7, "DKI Jakarta" },
            new object[] { "Jakarta Selatan", -6.2615, 106.8106, 7, "DKI Jakarta" }, new object[] { "Jakarta Barat", -6.1683, 106.7589, 7, "DKI Jakarta" }, new object[] { "Kepulauan Seribu", -5.6, 106.55, 7, "DKI Jakarta" },
            new object[] { "Depok", -6.4025, 106.7942, 7, "Jawa Barat" }, new object[] { "Bogor", -6.595, 106.8166, 7, "Jawa Barat" }, new object[] { "Bekasi", -6.2383, 106.9756, 7, "Jawa Barat" },
            new object[] { "Cibinong", -6.4817, 106.8544, 7, "Jawa Barat" }, new object[] { "Cikarang", -6.2672, 107.1478, 7, "Jawa Barat" }, new object[] { "Cirebon", -6.7063, 108.557, 7, "Jawa Barat" },
            new object[] { "Karawang", -6.3015, 107.3062, 7, "Jawa Barat" }, new object[] { "Purwakarta", -6.5569, 107.4433, 7, "Jawa Barat" }, new object[] { "Subang", -6.5714, 107.7583, 7, "Jawa Barat" },
            new object[] { "Sumedang", -6.8586, 107.9206, 7, "Jawa Barat" }, new object[] { "Garut", -7.2128, 107.9021, 7, "Jawa Barat" }, new object[] { "Tasikmalaya", -7.3195, 108.2208, 7, "Jawa Barat" },
            new object[] { "Ciamis", -7.3258, 108.3531, 7, "Jawa Barat" }, new object[] { "Banjar", -7.3667, 108.5333, 7, "Jawa Barat" }, new object[] { "Sukabumi", -6.9277, 106.93, 7, "Jawa Barat" },
            new object[] { "Cianjur", -6.8167, 107.1333, 7, "Jawa Barat" }, new object[] { "Indramayu", -6.3265, 108.3244, 7, "Jawa Barat" }, new object[] { "Majalengka", -6.8361, 108.2278, 7, "Jawa Barat" },
            new object[] { "Kuningan", -6.9758, 108.4836, 7, "Jawa Barat" }, new object[] { "Bandung", -6.9175, 107.6191, 7, "Jawa Barat" }, new object[] { "Bandung Barat", -6.8333, 107.4833, 7, "Jawa Barat" },
            new object[] { "Ngamprah", -6.8333, 107.5, 7, "Jawa Barat" }, new object[] { "Soreang", -7.0167, 107.5167, 7, "Jawa Barat" }, new object[] { "Lembang", -6.8118, 107.6178, 7, "Jawa Barat" },
            new object[] { "Singaparna", -7.35, 108.1167, 7, "Jawa Barat" }, new object[] { "Majalaya", -7.05, 107.75, 7, "Jawa Barat" }, new object[] { "Cicalengka", -6.9833, 107.8333, 7, "Jawa Barat" },
            new object[] { "Semarang", -6.9667, 110.4167, 7, "Jawa Tengah" }, new object[] { "Salatiga", -7.3305, 110.5084, 7, "Jawa Tengah" }, new object[] { "Magelang", -7.4698, 110.2177, 7, "Jawa Tengah" },
            new object[] { "Surakarta", -7.5755, 110.8243, 7, "Jawa Tengah" }, new object[] { "Yogyakarta", -7.7956, 110.3695, 7, "DI Yogyakarta" }, new object[] { "Purwokerto", -7.4217, 109.2344, 7, "Jawa Tengah" },
            new object[] { "Tegal", -6.8694, 109.1402, 7, "Jawa Tengah" }, new object[] { "Pekalongan", -6.8886, 109.6753, 7, "Jawa Tengah" }, new object[] { "Kudus", -6.8048, 110.8405, 7, "Jawa Tengah" },
            new object[] { "Pati", -6.7559, 111.0388, 7, "Jawa Tengah" }, new object[] { "Jepara", -6.5892, 110.6711, 7, "Jawa Tengah" }, new object[] { "Klaten", -7.7056, 110.6064, 7, "Jawa Tengah" },
            new object[] { "Boyolali", -7.5258, 110.5961, 7, "Jawa Tengah" }, new object[] { "Sragen", -7.4264, 111.0219, 7, "Jawa Tengah" }, new object[] { "Wonogiri", -7.8142, 110.9239, 7, "Jawa Tengah" },
            new object[] { "Karanganyar", -7.6, 110.95, 7, "Jawa Tengah" }, new object[] { "Purwodadi", -7.0833, 110.9167, 7, "Jawa Tengah" }, new object[] { "Blora", -6.9697, 111.4186, 7, "Jawa Tengah" },
            new object[] { "Rembang", -6.7078, 111.3419, 7, "Jawa Tengah" }, new object[] { "Cilacap", -7.7267, 109.0092, 7, "Jawa Tengah" }, new object[] { "Kebumen", -7.6686, 109.6539, 7, "Jawa Tengah" },
            new object[] { "Purbalingga", -7.3889, 109.3636, 7, "Jawa Tengah" }, new object[] { "Banjarnegara", -7.3958, 109.6947, 7, "Jawa Tengah" }, new object[] { "Wonosobo", -7.3636, 109.9, 7, "Jawa Tengah" },
            new object[] { "Temanggung", -7.3167, 110.1833, 7, "Jawa Tengah" }, new object[] { "Ambarawa", -7.2667, 110.4, 7, "Jawa Tengah" }, new object[] { "Kendal", -6.9211, 110.2025, 7, "Jawa Tengah" },
            new object[] { "Demak", -6.8931, 110.6397, 7, "Jawa Tengah" }, new object[] { "Batang", -6.9078, 109.7303, 7, "Jawa Tengah" }, new object[] { "Pemalang", -6.8914, 109.3808, 7, "Jawa Tengah" },
            new object[] { "Brebes", -6.8722, 109.0431, 7, "Jawa Tengah" }, new object[] { "Slawi", -6.9833, 109.1333, 7, "Jawa Tengah" }, new object[] { "Kajen", -7.0333, 109.5667, 7, "Jawa Tengah" },
            new object[] { "Mungkid", -7.55, 110.2333, 7, "Jawa Tengah" }, new object[] { "Wates", -7.8833, 110.15, 7, "DI Yogyakarta" }, new object[] { "Bantul", -7.8889, 110.3286, 7, "DI Yogyakarta" },
            new object[] { "Wonosari", -7.9655, 110.6022, 7, "DI Yogyakarta" }, new object[] { "Sleman", -7.7167, 110.35, 7, "DI Yogyakarta" }, new object[] { "Gunungkidul", -7.95, 110.6, 7, "DI Yogyakarta" },
            new object[] { "Surabaya", -7.2575, 112.7521, 7, "Jawa Timur" }, new object[] { "Malang", -7.9666, 112.6326, 7, "Jawa Timur" }, new object[] { "Kediri", -7.8167, 112.0167, 7, "Jawa Timur" },
            new object[] { "Madiun", -7.6298, 111.5239, 7, "Jawa Timur" }, new object[] { "Blitar", -8.0955, 112.1613, 7, "Jawa Timur" }, new object[] { "Mojokerto", -7.4703, 112.4336, 7, "Jawa Timur" },
            new object[] { "Pasuruan", -7.6469, 112.9075, 7, "Jawa Timur" }, new object[] { "Probolinggo", -7.7543, 113.2159, 7, "Jawa Timur" }, new object[] { "Jember", -8.1689, 113.7022, 7, "Jawa Timur" },
            new object[] { "Banyuwangi", -8.2192, 114.3692, 7, "Jawa Timur" }, new object[] { "Sidoarjo", -7.4478, 112.7183, 7, "Jawa Timur" }, new object[] { "Gresik", -7.156, 112.6531, 7, "Jawa Timur" },
            new object[] { "Bangkalan", -7.0455, 112.7378, 7, "Jawa Timur" }, new object[] { "Sampang", -7.1872, 113.2394, 7, "Jawa Timur" }, new object[] { "Pamekasan", -7.1568, 113.4746, 7, "Jawa Timur" },
            new object[] { "Sumenep", -7.0122, 113.8581, 7, "Jawa Timur" }, new object[] { "Tuban", -6.8976, 112.0649, 7, "Jawa Timur" }, new object[] { "Bojonegoro", -7.1502, 111.8817, 7, "Jawa Timur" },
            new object[] { "Lamongan", -7.1196, 112.4159, 7, "Jawa Timur" }, new object[] { "Jombang", -7.5461, 112.2261, 7, "Jawa Timur" }, new object[] { "Nganjuk", -7.6011, 111.9036, 7, "Jawa Timur" },
            new object[] { "Ngawi", -7.4093, 111.4469, 7, "Jawa Timur" }, new object[] { "Magetan", -7.65, 111.3333, 7, "Jawa Timur" }, new object[] { "Ponorogo", -7.8653, 111.4664, 7, "Jawa Timur" },
            new object[] { "Trenggalek", -8.05, 111.7, 7, "Jawa Timur" }, new object[] { "Tulungagung", -8.0657, 111.9022, 7, "Jawa Timur" }, new object[] { "Pacitan", -8.1942, 111.1, 7, "Jawa Timur" },
            new object[] { "Bondowoso", -7.9136, 113.8214, 7, "Jawa Timur" }, new object[] { "Situbondo", -7.7058, 114.0097, 7, "Jawa Timur" }, new object[] { "Lumajang", -8.1333, 113.2167, 7, "Jawa Timur" },
            new object[] { "Batu", -7.8672, 112.5239, 7, "Jawa Timur" }, new object[] { "Kepanjen", -8.1333, 112.5667, 7, "Jawa Timur" }, new object[] { "Caruban", -7.5667, 111.6667, 7, "Jawa Timur" },
            new object[] { "Kertosono", -7.6, 112.1, 7, "Jawa Timur" }, new object[] { "Denpasar", -8.6705, 115.2126, 8, "Bali" }, new object[] { "Singaraja", -8.1121, 115.0882, 8, "Bali" },
            new object[] { "Tabanan", -8.5411, 115.1216, 8, "Bali" }, new object[] { "Gianyar", -8.5435, 115.325, 8, "Bali" }, new object[] { "Klungkung", -8.5333, 115.4, 8, "Bali" },
            new object[] { "Bangli", -8.4542, 115.3544, 8, "Bali" }, new object[] { "Karangasem", -8.45, 115.6167, 8, "Bali" }, new object[] { "Negara", -8.35, 114.6333, 8, "Bali" },
            new object[] { "Mataram", -8.5833, 116.1167, 8, "Nusa Tenggara Barat" }, new object[] { "Sumbawa Besar", -8.5, 117.4167, 8, "Nusa Tenggara Barat" }, new object[] { "Dompu", -8.5333, 118.4667, 8, "Nusa Tenggara Barat" },
            new object[] { "Bima", -8.4604, 118.7268, 8, "Nusa Tenggara Barat" }, new object[] { "Kupang", -10.1772, 123.607, 8, "Nusa Tenggara Timur" }, new object[] { "Soe", -9.8667, 124.2833, 8, "Nusa Tenggara Timur" },
            new object[] { "Atambua", -9.1061, 124.8925, 8, "Nusa Tenggara Timur" }, new object[] { "Ende", -8.8432, 121.6625, 8, "Nusa Tenggara Timur" }, new object[] { "Maumere", -8.6199, 122.2111, 8, "Nusa Tenggara Timur" },
            new object[] { "Larantuka", -8.35, 122.9833, 8, "Nusa Tenggara Timur" }, new object[] { "Waingapu", -9.65, 120.2667, 8, "Nusa Tenggara Timur" }, new object[] { "Ruteng", -8.6, 120.4667, 8, "Nusa Tenggara Timur" },
            new object[] { "Bajawa", -8.7833, 120.9833, 8, "Nusa Tenggara Timur" }, new object[] { "Labuan Bajo", -8.4964, 119.8877, 8, "Nusa Tenggara Timur" }, new object[] { "Kalabahi", -8.2833, 124.5, 8, "Nusa Tenggara Timur" },
            new object[] { "Pontianak", -0.0263, 109.3425, 7, "Kalimantan Barat" }, new object[] { "Singkawang", 0.9061, 108.9847, 7, "Kalimantan Barat" }, new object[] { "Sintang", 0.0722, 111.4944, 7, "Kalimantan Barat" },
            new object[] { "Putussibau", 0.8333, 112.9333, 7, "Kalimantan Barat" }, new object[] { "Ketapang", -1.8592, 109.9853, 7, "Kalimantan Barat" }, new object[] { "Sambas", 1.3667, 109.3, 7, "Kalimantan Barat" },
            new object[] { "Mempawah", 0.3667, 108.9667, 7, "Kalimantan Barat" }, new object[] { "Bengkayang", 0.8167, 109.4833, 7, "Kalimantan Barat" }, new object[] { "Nanga Pinoh", -0.3333, 111.75, 7, "Kalimantan Barat" },
            new object[] { "Sekadau", 0.0167, 110.9, 7, "Kalimantan Barat" }, new object[] { "Kubu Raya", -0.2, 109.3, 7, "Kalimantan Barat" }, new object[] { "Banjarmasin", -3.3186, 114.5944, 8, "Kalimantan Selatan" },
            new object[] { "Banjarbaru", -3.4404, 114.8308, 8, "Kalimantan Selatan" }, new object[] { "Martapura", -3.4167, 114.85, 8, "Kalimantan Selatan" }, new object[] { "Barabai", -2.5833, 115.3833, 8, "Kalimantan Selatan" },
            new object[] { "Amuntai", -2.4167, 115.25, 8, "Kalimantan Selatan" }, new object[] { "Tanjung", -2.1667, 115.4, 8, "Kalimantan Selatan" }, new object[] { "Kotabaru", -3.2333, 116.2, 8, "Kalimantan Selatan" },
            new object[] { "Pelaihari", -3.8, 114.7667, 8, "Kalimantan Selatan" }, new object[] { "Marabahan", -2.9833, 114.7833, 8, "Kalimantan Selatan" }, new object[] { "Rantau", -2.9333, 115.15, 8, "Kalimantan Selatan" },
            new object[] { "Palangkaraya", -2.2083, 113.9167, 7, "Kalimantan Tengah" }, new object[] { "Sampit", -2.5333, 112.95, 7, "Kalimantan Tengah" }, new object[] { "Pangkalan Bun", -2.6833, 111.6167, 7, "Kalimantan Tengah" },
            new object[] { "Kuala Kapuas", -3.0, 114.3833, 7, "Kalimantan Tengah" }, new object[] { "Muara Teweh", -0.95, 114.8833, 7, "Kalimantan Tengah" }, new object[] { "Buntok", -1.7333, 114.8333, 7, "Kalimantan Tengah" },
            new object[] { "Puruk Cahu", -0.6333, 114.55, 7, "Kalimantan Tengah" }, new object[] { "Kasongan", -1.8833, 113.4, 7, "Kalimantan Tengah" }, new object[] { "Nanga Bulik", -2.1667, 111.4667, 7, "Kalimantan Tengah" },
            new object[] { "Tamiang Layang", -2.1167, 115.1667, 7, "Kalimantan Tengah" }, new object[] { "Sukamara", -2.7833, 111.2, 7, "Kalimantan Tengah" }, new object[] { "Balikpapan", -1.2379, 116.8529, 8, "Kalimantan Timur" },
            new object[] { "Samarinda", -0.5022, 117.1536, 8, "Kalimantan Timur" }, new object[] { "Bontang", 0.1246, 117.4925, 8, "Kalimantan Timur" }, new object[] { "Tarakan", 3.3273, 117.5719, 8, "Kalimantan Utara" },
            new object[] { "Tenggarong", -0.4, 116.9833, 8, "Kalimantan Timur" }, new object[] { "Sendawar", -0.1667, 115.7, 8, "Kalimantan Timur" }, new object[] { "Ujoh Bilang", 0.2, 115.5, 8, "Kalimantan Timur" },
            new object[] { "Tanjung Redeb", 2.15, 117.4833, 8, "Kalimantan Timur" }, new object[] { "Malinau", 3.5833, 116.65, 8, "Kalimantan Utara" }, new object[] { "Nunukan", 4.1333, 117.6667, 8, "Kalimantan Timur" },
            new object[] { "Tanah Grogot", -1.9, 116.2, 8, "Kalimantan Timur" }, new object[] { "Penajam", -1.4, 116.6, 8, "Kalimantan Timur" }, new object[] { "Tanjung Selor", 2.8333, 117.3667, 8, "Kalimantan Utara" },
            new object[] { "Long Bagun", 0.2, 114.7, 8, "Kalimantan Timur" }, new object[] { "Makassar", -5.1477, 119.4327, 8, "Sulawesi Selatan" }, new object[] { "Parepare", -4.0135, 119.6255, 8, "Sulawesi Selatan" },
            new object[] { "Palopo", -3.0, 120.2, 8, "Sulawesi Selatan" }, new object[] { "Bulukumba", -5.55, 120.2, 8, "Sulawesi Selatan" }, new object[] { "Watampone", -4.5333, 120.3333, 8, "Sulawesi Selatan" },
            new object[] { "Sinjai", -5.1833, 120.15, 8, "Sulawesi Selatan" }, new object[] { "Maros", -5.0, 119.5667, 8, "Sulawesi Selatan" }, new object[] { "Sungguminasa", -5.2, 119.45, 8, "Sulawesi Selatan" },
            new object[] { "Takalar", -5.4167, 119.4333, 8, "Sulawesi Selatan" }, new object[] { "Jeneponto", -5.6833, 119.6833, 8, "Sulawesi Selatan" }, new object[] { "Bantaeng", -5.5333, 119.95, 8, "Sulawesi Selatan" },
            new object[] { "Selayar", -6.1167, 120.4667, 8, "Sulawesi Selatan" }, new object[] { "Barru", -4.4167, 119.6167, 8, "Sulawesi Selatan" }, new object[] { "Pangkajene", -4.8333, 119.55, 8, "Sulawesi Selatan" },
            new object[] { "Pinrang", -3.7833, 119.65, 8, "Sulawesi Selatan" }, new object[] { "Enrekang", -3.5667, 119.7667, 8, "Sulawesi Selatan" }, new object[] { "Sidenreng", -3.95, 119.8, 8, "Sulawesi Selatan" },
            new object[] { "Majene", -3.5333, 118.9667, 8, "Sulawesi Barat" }, new object[] { "Polewali", -3.4167, 119.3333, 8, "Sulawesi Barat" }, new object[] { "Mamasa", -2.95, 119.3833, 8, "Sulawesi Barat" },
            new object[] { "Mamuju", -2.6806, 118.8861, 8, "Sulawesi Barat" }, new object[] { "Masamba", -2.55, 120.3167, 8, "Sulawesi Selatan" }, new object[] { "Makale", -3.1, 119.85, 8, "Sulawesi Selatan" },
            new object[] { "Kendari", -3.945, 122.4989, 8, "Sulawesi Tenggara" }, new object[] { "Baubau", -5.4667, 122.6167, 8, "Sulawesi Tenggara" }, new object[] { "Kolaka", -4.0667, 121.5833, 8, "Sulawesi Tenggara" },
            new object[] { "Raha", -4.8333, 122.7167, 8, "Sulawesi Tenggara" }, new object[] { "Wangiwangi", -5.3167, 123.2, 9, "Sulawesi Tenggara" }, new object[] { "Rumbia", -4.7, 122.0, 8, "Sulawesi Tenggara" },
            new object[] { "Andolo", -4.5, 122.2, 8, "Sulawesi Tenggara" }, new object[] { "Manado", 1.4748, 124.8421, 8, "Sulawesi Utara" }, new object[] { "Bitung", 1.4451, 125.1969, 8, "Sulawesi Utara" },
            new object[] { "Tomohon", 1.3244, 124.8389, 8, "Sulawesi Utara" }, new object[] { "Kotamobagu", 0.7333, 124.3167, 8, "Sulawesi Utara" }, new object[] { "Tondano", 1.3053, 124.9119, 8, "Sulawesi Utara" },
            new object[] { "Airmadidi", 1.4167, 124.9833, 8, "Sulawesi Utara" }, new object[] { "Lolak", 0.8833, 124.05, 8, "Sulawesi Utara" }, new object[] { "Tahuna", 3.6167, 125.4833, 9, "Sulawesi Utara" },
            new object[] { "Melonguane", 4.0, 126.7, 9, "Sulawesi Utara" }, new object[] { "Gorontalo", 0.5435, 123.0568, 8, "Gorontalo" }, new object[] { "Limboto", 0.6333, 122.9833, 8, "Gorontalo" },
            new object[] { "Marisa", 0.4833, 122.0333, 8, "Gorontalo" }, new object[] { "Atinggola", 0.9167, 123.1167, 8, "Gorontalo" }, new object[] { "Suwawa", 0.5333, 123.2, 8, "Gorontalo" },
            new object[] { "Tilamuta", 0.6333, 122.3667, 8, "Gorontalo" }, new object[] { "Ambon", -3.6954, 128.1814, 9, "Maluku" }, new object[] { "Tual", -5.6406, 132.745, 9, "Maluku" },
            new object[] { "Masohi", -3.3, 128.9667, 9, "Maluku" }, new object[] { "Namlea", -3.25, 127.0833, 9, "Maluku" }, new object[] { "Dobo", -5.7667, 134.2167, 9, "Maluku" },
            new object[] { "Dataran Hunimoa", -3.3, 132.0, 9, "Maluku" }, new object[] { "Saumlaki", -7.9833, 131.3, 9, "Maluku" }, new object[] { "Tobelo", 1.7333, 128.0, 9, "Maluku Utara" },
            new object[] { "Labuha", -0.6333, 127.4833, 9, "Maluku Utara" }, new object[] { "Kaimana", -3.6667, 133.75, 9, "Papua Barat" }, new object[] { "Ternate", 0.7904, 127.3833, 9, "Maluku Utara" },
            new object[] { "Sofifi", 0.7333, 127.5667, 9, "Maluku Utara" }, new object[] { "Tidore", 0.6833, 127.4167, 9, "Maluku Utara" }, new object[] { "Jailolo", 1.0667, 127.5, 9, "Maluku Utara" },
            new object[] { "Tobelo Utara", 1.75, 128.0167, 9, "Maluku Utara" }, new object[] { "Sanana", -2.0333, 125.9667, 9, "Maluku Utara" }, new object[] { "Bobong", -1.9333, 124.3833, 9, "Maluku Utara" },
            new object[] { "Maba", 0.7, 128.3, 9, "Maluku Utara" }, new object[] { "Jayapura", -2.5916, 140.669, 9, "Papua" }, new object[] { "Sentani", -2.5667, 140.5167, 9, "Papua" },
            new object[] { "Biak", -1.1833, 136.0833, 9, "Papua" }, new object[] { "Manokwari", -0.8615, 134.062, 9, "Papua Barat" }, new object[] { "Sorong", -0.8762, 131.2558, 9, "Papua Barat Daya" },
            new object[] { "Merauke", -8.4667, 140.3333, 9, "Papua Selatan" }, new object[] { "Timika", -4.5461, 136.8861, 9, "Papua Tengah" }, new object[] { "Nabire", -3.3667, 135.4833, 9, "Papua Tengah" },
            new object[] { "Wamena", -4.1, 138.95, 9, "Papua Pegunungan" }, new object[] { "Fakfak", -2.9333, 132.3, 9, "Papua Barat" }, new object[] { "Serui", -1.8833, 136.2333, 9, "Papua" },
            new object[] { "Waropen", -2.3, 136.7, 9, "Papua" }, new object[] { "Tembagapura", -4.1333, 137.1, 9, "Papua Tengah" }, new object[] { "Oksibil", -4.8667, 140.6333, 9, "Papua Pegunungan" },
            new object[] { "Tanah Merah", -6.1, 140.3, 9, "Papua Selatan" }, new object[] { "Boven Digoel", -6.0, 140.5, 9, "Papua Selatan" }, new object[] { "Salor", -2.6333, 140.6, 9, "Papua" },
            new object[] { "Sarmi", -1.85, 138.75, 9, "Papua" }, new object[] { "Yahukimo", -4.5, 139.4, 9, "Papua Pegunungan" }, new object[] { "Paniai", -3.9, 136.4, 9, "Papua Tengah" },
            new object[] { "Mimika", -4.6, 136.8, 9, "Papua Tengah" }, new object[] { "Asmat", -5.5, 138.5, 9, "Papua Selatan" }, new object[] { "Sofifi Utara", 0.8, 127.6, 9, "Maluku Utara" },
            new object[] { "Tidore Kepulauan", 0.65, 127.4, 9, "Maluku Utara" },
        };
    }

    enum RemindKind { None, Drink, Eyes, Stretch }

    // Pengingat: prayer, water, eyes, moving
    partial class Island
    {
        [StructLayout(LayoutKind.Sequential)]
        struct LASTINPUTINFO { public uint cbSize, dwTime; }
        [DllImport("user32.dll")] static extern bool GetLastInputInfo(ref LASTINPUTINFO info);

        static double IdleSeconds()
        {
            var li = new LASTINPUTINFO { cbSize = (uint)Marshal.SizeOf(typeof(LASTINPUTINFO)) };
            if (!GetLastInputInfo(ref li)) return 0;
            return (uint)(Environment.TickCount - (int)li.dwTime) / 1000.0;
        }

        DateTime[] prayToday;
        DateTime prayDay = DateTime.MinValue;
        int prayAnnounced = -1, prayWarned = -1;
        DateTime lastDrinkUtc = DateTime.UtcNow, lastEyesUtc = DateTime.UtcNow, lastStretchUtc = DateTime.UtcNow, snoozeUntilUtc = DateTime.MinValue;
        DateTime restUntilUtc = DateTime.MinValue;
        RemindKind remindShown;
        bool wellLoaded;

        // Well clocks live in settings: a restart must not zero a countdown
        static readonly string[] WellLastKeys = { "drink.last", "eyes.last", "stretch.last" };
        static readonly DateTime UnixEpoch = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        // Intervals in minutes
        const int EyesEvery = 20;
        int DrinkEvery { get { return Math.Max(15, settings.GetInt("drink.every", 60)); } }
        int StretchEvery { get { return Math.Max(30, settings.GetInt("stretch.every", 60)); } }

        DateTime WellLast(int i)
        {
            long sec;
            if (!long.TryParse(settings.Get(WellLastKeys[i], ""), NumberStyles.Integer, CultureInfo.InvariantCulture, out sec) || sec <= 0) return DateTime.MinValue;
            try { return UnixEpoch.AddSeconds(sec); }
            catch (ArgumentOutOfRangeException) { return DateTime.MinValue; }
        }

        // Save is opt-in: a quiet per-tick reset writes no disk
        void SetWell(int i, DateTime atUtc, bool save)
        {
            settings.Set(WellLastKeys[i], (long)(atUtc - UnixEpoch).TotalSeconds);
            if (save) settings.Save();
        }

        // Once per launch: continue from what was stored, so an overdue nudge is still due
        void LoadWellClocks()
        {
            if (wellLoaded) return;
            wellLoaded = true;
            var now = DateTime.UtcNow;
            var stored = new DateTime[3];
            for (int i = 0; i < 3; i++)
            {
                var at = WellLast(i);
                // Epoch, clock skew, or older than a day: no usable history, start fresh
                stored[i] = at == DateTime.MinValue || at > now || (now - at).TotalHours >= 24 ? now : at;
            }
            lastDrinkUtc = stored[0];
            lastEyesUtc = stored[1];
            lastStretchUtc = stored[2];
        }

        bool PrayOn { get { return settings.GetBool("pray", false) && PrayCity() != null; } }

        // A picked city, or a searched place saved as "name|lat|lng|tz"
        object[] PrayCity()
        {
            string want = settings.Get("pray.city", "");
            if (want.Length == 0) return null;
            int bar = want.LastIndexOf('|');
            if (bar > 0) return Cities.Parse(want) ?? Cities.Find(want.Substring(0, bar));
            return Cities.Find(want);
        }

        readonly string[] prayHm = new string[6];
        string prayTomorrowHm = "", prayFor = "", todayKey = "";
        DateTime prayTomorrow, todayKeyDay;

        // The place's own wall clock. Times are computed for the city, so every comparison
        // must use this rather than the laptop's clock: Jakarta and Mecca differ by four hours.
        DateTime PrayNow() { return DateTime.SpecifyKind(DateTime.UtcNow.AddHours(prayOffset), DateTimeKind.Unspecified); }

        // Today's times plus sunrise, cached daily.
        // The key is the stored place text compared by value: a searched place parses into a
        // fresh string on every call, so an identity test would miss and reset the announce
        // counters each tick, which re-fires the adhan while the window is open.
        DateTime[] Prayers()
        {
            var city = PrayCity();
            if (city == null) return null;
            string want = settings.Get("pray.city", "");
            if (prayToday == null || !string.Equals(want, prayFor, StringComparison.Ordinal)) prayOffset = Cities.OffsetOn(city, DateTime.UtcNow);
            // The city's date, not the laptop's: across the dateline they differ
            var today = PrayNow().Date;
            if (prayToday == null || prayDay != today || !string.Equals(want, prayFor, StringComparison.Ordinal))
            {
                double lat = Convert.ToDouble(city[1]), lng = Convert.ToDouble(city[2]);
                // The zone follows the calendar, so summer time needs no separate setting
                double tz = Cities.OffsetOn(city, today);
                var prm = PrayMethods.Pick(Cities.CountryCode(city), lat);
                prayToday = PrayTimes.For(today, lat, lng, tz, prm);
                // Tomorrow can sit on the other side of a clock change
                prayTomorrow = PrayTimes.For(today.AddDays(1), lat, lng, Cities.OffsetOn(city, today.AddDays(1)), prm)[0];
                for (int i = 0; i < 6; i++) prayHm[i] = Fmt.Hm(prayToday[i]);
                prayTomorrowHm = Fmt.Hm(prayTomorrow);
                prayDay = today;
                prayFor = want;
                prayAnnounced = prayWarned = -1;
                prayParams = prm;
                prayOffset = tz;
            }
            return prayToday;
        }

        // What the notch and settings show about the place in use
        PrayParams prayParams;
        double prayOffset;

        // Next prayer; after Isya, tomorrow's Subuh
        int NextPrayer(out DateTime at)
        {
            at = DateTime.MaxValue;
            var times = Prayers();
            if (times == null) return -1;
            // The city's clock, since the times belong to the city
            var now = PrayNow();
            for (int i = 0; i < 6; i++)
                if (i != 1 && times[i] > now) { at = times[i]; return i; }
            at = prayTomorrow;
            return 0;
        }

        // Day stamp built once per day
        string TodayKey()
        {
            if (todayKeyDay != DateTime.Today)
            {
                todayKeyDay = DateTime.Today;
                todayKey = todayKeyDay.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            }
            return todayKey;
        }

        int DrinkCount()
        {
            if (settings.Get("drink.day", "") != TodayKey()) return 0;
            return settings.GetInt("drink.count", 0);
        }

        int DrinkGoal { get { return Math.Max(1, Math.Min(20, settings.GetInt("drink.goal", 8))); } }

        void Drank()
        {
            lastDrinkUtc = DateTime.UtcNow;
            settings.Set("drink.day", TodayKey());
            settings.Set("drink.count", DrinkCount() + 1);
            SetWell(0, lastDrinkUtc, false);
            settings.Save();
            if (toast != null && toast.View == View.Remind) DismissToast();
            Kick();
        }

        void Snooze(int minutes)
        {
            snoozeUntilUtc = DateTime.UtcNow.AddMinutes(minutes);
            if (toast != null && toast.View == View.Remind) DismissToast();
        }

        // Each clock tick
        bool RemindTick(DateTime nowUtc)
        {
            bool redraw = false;
            var now = DateTime.Now;
            LoadWellClocks();
            // In use: input within 5 minutes
            double idle = IdleSeconds();
            if (PrayOn)
            {
                var times = Prayers();
                // The adhan fires on the city's clock; the wellness hours below stay on the laptop's
                var pnow = PrayNow();
                int before = Math.Max(0, settings.GetInt("pray.before", 10));
                for (int i = 0; i < 6; i++)
                {
                    if (i == 1) continue;
                    if (before > 0 && prayWarned < i && pnow >= times[i].AddMinutes(-before) && pnow < times[i])
                    {
                        prayWarned = i;
                        redraw |= ShowWorship(i, false, settings.Get("sound.pray.before", ""));
                    }
                    if (prayAnnounced < i && pnow >= times[i] && pnow < times[i].AddMinutes(10))
                    {
                        prayAnnounced = prayWarned = i;
                        redraw |= ShowWorship(i, true, settings.Get("sound.pray", ""));
                    }
                }
            }
            redraw |= IbadahTick(now);
            // Nudges wait: away, fullscreen, or quiet
            if (idle > 120 || fullscreen || Quiet || PrompterOn || nowUtc < snoozeUntilUtc || toast != null) return redraw;
            int hour = now.Hour;
            bool nudged = false;
            if (settings.GetBool("drink", false) && hour >= 7 && hour < 22 && DrinkCount() < DrinkGoal &&
                (nowUtc - lastDrinkUtc).TotalMinutes >= DrinkEvery)
                nudged = Nudge(RemindKind.Drink, ref lastDrinkUtc, 0);
            if (!nudged && settings.GetBool("eyes", false) && (nowUtc - lastEyesUtc).TotalMinutes >= EyesEvery)
                nudged = Nudge(RemindKind.Eyes, ref lastEyesUtc, 1);
            if (!nudged && settings.GetBool("stretch", false) && (nowUtc - lastStretchUtc).TotalMinutes >= StretchEvery)
                nudged = Nudge(RemindKind.Stretch, ref lastStretchUtc, 2);
            return redraw || nudged;
        }

        // The clock moves only once the toast really shows: a refused nudge retries next tick
        bool Nudge(RemindKind kind, ref DateTime last, int well)
        {
            var t = ShowToast(View.Remind, 20, 2, true);
            if (t == null) return false;
            var now = DateTime.UtcNow;
            last = now;
            SetWell(well, now, true);
            remindShown = kind;
            t.Data = kind;
            // Not held: a nudge is ambient news, so it closes itself and stops blocking other toasts
            PlayRemind(settings.Get("sound." + WellKeys[well], ""));
            if (kind == RemindKind.Eyes) restUntilUtc = now.AddSeconds(20);
            if (!Anim.Reduced) topGap.Velocity += 80;
            return true;
        }

        // Compact views

        const char IcoPray = '';
        static readonly string[] Secs = MakeSecs();
        readonly Roller prayRoller = new Roller(), eyesRoller = new Roller();
        Action actCity, actPraySwitch, actDrinkPlus, actLater, actRemindOk, actRemindDone;
        readonly Action[] wellActs = new Action[3];
        Func<DateTime, double> prayLeftPaint, eyesLeftPaint;
        Action<DateTime> eyesRingPaint;
        static readonly string[] WellKeys = { "drink", "eyes", "stretch" }, WellIds = { "swdrink", "sweyes", "swstretch" };

        static string[] MakeSecs()
        {
            var s = new string[21];
            for (int i = 0; i < s.Length; i++) s[i] = i.ToString(CultureInfo.InvariantCulture);
            return s;
        }

        // Delegates built once, never per frame
        void RemindActs()
        {
            if (actCity != null) return;
            actCity = CityMenu;
            actPraySwitch = delegate
            {
                if (PrayCity() == null) { CityMenu(); return; }
                settings.Toggle("pray", false);
                Kick();
            };
            actDrinkPlus = Drank;
            for (int i = 0; i < 3; i++)
            {
                int k = i;
                wellActs[i] = delegate { ToggleWell(k); };
            }
            actLater = delegate { Snooze(15); };
            actRemindOk = delegate
            {
                if (toast != null && toast.Data is RemindKind && (RemindKind)toast.Data == RemindKind.Drink) Drank();
                else DismissToast();
            };
            actRemindDone = DismissToast;
            prayLeftPaint = PaintPrayLeft;
            eyesLeftPaint = PaintEyesLeft;
            eyesRingPaint = PaintEyesRing;
        }

        void ToggleWell(int i)
        {
            string key = WellKeys[i];
            settings.Toggle(key, false);
            // Start counting from now, not from launch, and remember that across restarts
            if (settings.GetBool(key, false))
            {
                var now = DateTime.UtcNow;
                if (i == 0) lastDrinkUtc = now;
                if (i == 1) lastEyesUtc = now;
                if (i == 2) lastStretchUtc = now;
                SetWell(i, now, true);
            }
            Kick();
        }

        // Leading glyph in a round tile
        void LeadTile(float cy, char glyph)
        {
            p.Module(new RectangleF(11, cy - 15, 30, 30), 15, p.T.Fill);
            p.Icon(glyph, 15f, p.T.Text, 26, cy);
        }

        int prayTitleKey = -1, prayTailKey = -1;
        string prayTitleText = "", prayTailText = "";

        float PrayWidth()
        {
            if (toast == null || p.G == null) return 280;
            return Clamp(p.Measure(PrayTitle(), Ink.Headline) + p.Measure(PrayTail(), Ink.Semi(13f)) + 52 + 44 + DoneW + 12, 300, 460);
        }

        string PrayTitle()
        {
            bool due = toast != null && toast.Flag;
            var custom = toast != null ? toast.Data as IbadahItem : null;
            if (custom != null) return due ? custom.Name : Lang.F("{0} sebentar lagi", custom.Name);
            int i = toast != null && toast.Data is int ? (int)toast.Data : 0;
            int key = i * 4 + (due ? 2 : 0) + (Lang.English ? 1 : 0);
            if (key != prayTitleKey)
            {
                prayTitleKey = key;
                string name = Lang.T(PrayTimes.Names[i]);
                prayTitleText = due ? Lang.F("Waktu {0}", name) : Lang.F("{0} sebentar lagi", name);
            }
            return prayTitleText;
        }

        // Time when due, else minutes left
        string PrayTail()
        {
            var custom = toast != null ? toast.Data as IbadahItem : null;
            if (custom != null)
            {
                if (toast.Flag) return custom.Hm;
                return Fmt.Left(custom.At(DateTime.Now) - DateTime.Now);
            }
            var times = Prayers();
            if (times == null || toast == null || !(toast.Data is int)) return "";
            int i = (int)toast.Data;
            if (toast.Flag) return prayHm[i];
            var left = times[i] - PrayNow();
            int key = i * 100000 + Math.Max(0, (int)left.TotalMinutes) * 2 + (Lang.English ? 1 : 0);
            if (key != prayTailKey) { prayTailKey = key; prayTailText = Fmt.Left(left); }
            return prayTailText;
        }

        void DrawPray(float w, float h, DateTime now)
        {
            if (toast == null || !(toast.Data is int || toast.Data is IbadahItem)) return;
            RemindActs();
            float cy = h / 2;
            LeadTile(cy, IcoPray);
            var done = new RectangleF(w - 14 - DoneW, cy - RemindPillH / 2, DoneW, RemindPillH);
            string tail = PrayTail();
            float tw = tail.Length > 0 ? p.Measure(tail, Ink.Semi(13f)) + 12 : 0;
            p.Text(PrayTitle(), Ink.Headline, p.T.Text, 52, cy, done.X - 12 - 52 - tw, StringAlignment.Near);
            if (tw > 0) p.TextRight(tail, Ink.Semi(13f), p.T.Sub, done.X - 12, cy);
            Pill(done, "prayDone", Lang.T("Selesai"), PillKind.Primary, actRemindDone);
        }

        const float RemindPillH = 30, LaterW = 66, DoneW = 72;

        float RemindWidth()
        {
            if (p.G == null) return 360;
            var k = remindShown;
            float text = Math.Max(p.Measure(RemindTitle(k), Ink.Headline), p.Measure(RemindDetail(k), Ink.Callout));
            float tail = k == RemindKind.Eyes ? 44 : LaterW + DoneW + 8 + 14;
            return Clamp(52 + text + 16 + tail, 300, 440);
        }

        string RemindTitle(RemindKind k)
        {
            if (k == RemindKind.Drink) return Lang.T("Waktunya minum");
            if (k == RemindKind.Eyes) return Lang.T("Istirahatkan mata");
            return Lang.T("Waktunya bergerak");
        }

        int drinkDetailKey = -1;
        string drinkDetail = "";

        string RemindDetail(RemindKind k)
        {
            if (k == RemindKind.Drink)
            {
                int key = DrinkCount() * 1000 + DrinkGoal * 2 + (Lang.English ? 1 : 0);
                if (key != drinkDetailKey) { drinkDetailKey = key; drinkDetail = Lang.F("{0} dari {1} gelas", DrinkCount(), DrinkGoal); }
                return drinkDetail;
            }
            if (k == RemindKind.Eyes) return Lang.T("pandang jauh 20 detik");
            return Lang.T("berdiri dan regangkan badan");
        }

        static char RemindGlyph(RemindKind k) { return k == RemindKind.Drink ? '' : k == RemindKind.Eyes ? '' : ''; }

        float remindCy, remindRight;

        void DrawRemind(float w, float h, DateTime now)
        {
            if (toast == null || !(toast.Data is RemindKind)) return;
            RemindActs();
            var k = (RemindKind)toast.Data;
            float cy = h / 2, tx = 52;
            remindCy = cy;
            if (k == RemindKind.Eyes)
            {
                AddHit(new RectangleF(0, 0, w, h), "remindDone", actRemindDone);
                // Ring and seconds count down alone
                p.Icon(RemindGlyph(k), 13f, p.T.Text, 26, cy);
                LiveRect(new RectangleF(11, cy - 15, 30, 30), 15f, eyesRingPaint);
                remindRight = w - 20;
                LiveRect(new RectangleF(w - 64, cy - 14, 46, 28), eyesLeftPaint);
                p.Text(RemindTitle(k), Ink.Headline, p.T.Text, tx, cy - 9, w - tx - 70, StringAlignment.Near);
                p.Text(RemindDetail(k), Ink.Callout, p.T.Sub, tx, cy + 10, w - tx - 70, StringAlignment.Near);
                return;
            }
            LeadTile(cy, RemindGlyph(k));
            var done = new RectangleF(w - 14 - DoneW, cy - RemindPillH / 2, DoneW, RemindPillH);
            var later = new RectangleF(done.X - 8 - LaterW, done.Y, LaterW, RemindPillH);
            float room = later.X - 12 - tx;
            p.Text(RemindTitle(k), Ink.Headline, p.T.Text, tx, cy - 9, room, StringAlignment.Near);
            p.Text(RemindDetail(k), Ink.Callout, p.T.Sub, tx, cy + 10, room, StringAlignment.Near);
            Pill(later, "remindLater", Lang.T("Nanti"), PillKind.Secondary, actLater);
            Pill(done, "remindOk", Lang.T("Sudah"), PillKind.Primary, actRemindOk);
        }

        void PaintEyesRing(DateTime t)
        {
            float left = (float)Math.Max(0, (restUntilUtc - t).TotalSeconds);
            Ring(26, remindCy, 13.5f, left / 20, p.T.Accent, 2.4f);
        }

        // Whole seconds roll, then wait
        double PaintEyesLeft(DateTime t)
        {
            double left = Math.Max(0, (restUntilUtc - t).TotalSeconds);
            int n = Math.Min(20, (int)Math.Ceiling(left));
            eyesRoller.Set(Secs[n]);
            eyesRoller.Draw(p, Ink.Display(17f), p.T.Text, remindRight, remindCy, StringAlignment.Far, t);
            if (eyesRoller.Moving(t)) return 1 / 60.0;
            if (n == 0) return 0;
            double wait = left - Math.Floor(left);
            return (wait < 0.001 ? 1 : wait) + 0.004;
        }

        // Page

        // The extra 14px carries the region line: the method is chosen for the user, so the card names it
        const float PrayCardH = 116, PrayEmptyH = 56, WellRowH = 40, WellPad = 4;

        float RemindHeight()
        {
            float pray = PrayCity() != null ? PrayCardH : PrayEmptyH;
            return Math.Min(MaxH, PanelTop + 2 + pray + Ink.ModuleGap + 3 * WellRowH + 2 * WellPad + Ink.ModuleInset);
        }

        void DrawRemindPage(float w, float h, DateTime now)
        {
            RemindActs();
            float x0 = Ink.ModuleInset, full = w - 2 * x0, y = PanelTop + 2, keep = p.Alpha, dy;
            bool city = PrayCity() != null;
            float ph = city ? PrayCardH : PrayEmptyH;
            p.Alpha = keep * RowIn(0, now, out dy);
            if (city) DrawPrayCard(new RectangleF(x0, y + dy, full, ph));
            else DrawPrayEmpty(new RectangleF(x0, y + dy, full, ph));
            y += ph + Ink.ModuleGap;
            p.Alpha = keep * RowIn(1, now, out dy);
            var well = new RectangleF(x0, y + dy, full, 3 * WellRowH + 2 * WellPad);
            Module(well, Ink.RadiusModule);
            for (int i = 0; i < 3; i++)
                DrawWellRow(new RectangleF(well.X + 6, well.Y + WellPad + i * WellRowH, well.Width - 12, WellRowH), i, i < 2);
            p.Alpha = keep;
        }

        // No city yet: one row, one action
        void DrawPrayEmpty(RectangleF r)
        {
            Module(r, Ink.RadiusModule);
            float cy = r.Y + r.Height / 2, pad = Ink.ModulePad;
            p.Icon(IcoPray, 16f, p.T.Text, r.X + pad + 8, cy);
            string label = Lang.T("Pilih daerah");
            float bw = p.Measure(label, Ink.Semi(13f)) + 32;
            var pill = new RectangleF(r.Right - pad - bw, cy - 16, bw, 32);
            p.Text(Lang.T("Waktu sholat"), Ink.Headline, p.T.Text, r.X + pad + 28, cy, pill.X - 12 - r.X - pad - 28, StringAlignment.Near);
            Pill(pill, "prayCity", label, PillKind.Primary, actCity);
        }

        static readonly int[] PrayRow = { 0, 2, 3, 4, 5 };
        float prayLeftX, prayLeftY;
        int prayLeftKey = -1;
        string prayLeftText = "";
        DateTime prayNextUtc;

        void DrawPrayCard(RectangleF r)
        {
            Module(r, Ink.RadiusModule);
            var times = Prayers();
            if (times == null) return;
            DateTime nextAt;
            int next = NextPrayer(out nextAt);
            bool today = nextAt.Date == PrayNow().Date;
            float pad = Ink.ModulePad, hy = r.Y + 27, x = r.X + pad;
            // Header right: current city and reminder switch
            var sw = new RectangleF(r.Right - pad - 38, hy - 11, 38, 22);
            string name = (string)PrayCity()[0];
            var cityFont = Ink.Semi(12.5f);
            float cityRight = sw.X - 12, cityWidth = Math.Min(120, p.Measure(name, cityFont) + 2);
            p.Text(name, cityFont, p.T.Sub, cityRight, hy, cityWidth, StringAlignment.Far);
            Switch(sw, "praySwitch", settings.GetBool("pray", false), actPraySwitch);
            // Hero: name, time, minutes left
            string hero = Lang.T(PrayTimes.Names[next]), at = today ? prayHm[next] : prayTomorrowHm;
            float heroRight = cityRight - cityWidth - 12;
            float room = heroRight - x, nw = Math.Min(room, p.Measure(hero, Ink.Title1)), tw = p.Measure(at, Ink.Title1);
            p.Text(hero, Ink.Title1, p.T.Text, x, hy, room, StringAlignment.Near);
            float lx = x + nw + 8;
            // Narrow panel: the row shows the time
            if (lx + tw + 10 + 76 <= heroRight)
            {
                p.Text(at, Ink.Title1, p.T.Sub, lx, hy, tw + 2, StringAlignment.Near);
                lx += tw + 10;
            }
            // The city's wall clock turned into an instant, so the live countdown can subtract UTC
            prayNextUtc = DateTime.SpecifyKind(nextAt, DateTimeKind.Utc).AddHours(-prayOffset);
            prayLeftX = lx;
            prayLeftY = hy + 1.5f;
            LiveRect(new RectangleF(lx - 2, prayLeftY - 10, Math.Max(10, heroRight + 4 - lx), 20), prayLeftPaint);
            // Five times: past dims, next takes accent
            var nowLocal = PrayNow();
            float top = r.Y + 52, cellW = (r.Width - 16) / PrayRow.Length;
            for (int k = 0; k < PrayRow.Length; k++)
            {
                int i = PrayRow[k];
                float cx = r.X + 8 + cellW * (k + 0.5f);
                bool isNext = today && i == next, past = times[i] <= nowLocal;
                if (isNext) p.FillRound(new RectangleF(cx - cellW / 2 + 2, top, cellW - 4, 40), Ink.RadiusControl, Ink.A(p.T.Accent, 0.16f));
                Color ink = isNext ? p.T.Text : past ? p.T.Faint : p.T.Sub;
                p.TextCenter(Lang.T(PrayTimes.Names[i]), Ink.Caption, isNext ? p.T.Accent : ink, cx, top + 12);
                p.TextCenter(prayHm[i], Ink.Semi(13.5f), isNext ? p.T.Text : past ? p.T.Faint : p.T.Text, cx, top + 28);
            }
            // Why these times: the region, the convention it implies, and the zone in force today
            p.Text(PrayRegionLine(), Ink.Small(11f), p.T.Faint, r.X + 8, top + 50, r.Width - 16, StringAlignment.Near);
        }

        int prayRegionKey = -1;
        string prayRegionText = "";

        // Cached on what the line is built from, so a zone or place change rebuilds it
        string PrayRegionLine()
        {
            var c = PrayCity();
            if (c == null || prayParams == null) return "";
            string want = settings.Get("pray.city", "");
            int quarter = (int)Math.Round(prayOffset * 4);
            int key = (want.GetHashCode() * 397) ^ quarter ^ (prayParams.Key != null ? prayParams.Key.GetHashCode() : 0);
            if (key == prayRegionKey && prayRegionText.Length > 0) return prayRegionText;
            prayRegionKey = key;
            string region = Cities.RegionOf(c);
            string line = (region.Length > 0 ? region + " · " : "") + prayParams.Name + " · " + PrayZone.Label(prayOffset);
            if (prayParams.AsrFactor == 2) line += " · " + Lang.T("Ashar Hanafi");
            if (prayParams.HighLat > 0) line += " · " + Lang.T("lintang tinggi");
            prayRegionText = line;
            return line;
        }

        // Minutes left; rolls, then sleeps
        double PaintPrayLeft(DateTime t)
        {
            var left = prayNextUtc - t;
            if (left.Ticks <= 0) return 0;
            int key = (int)left.TotalMinutes * 2 + (Lang.English ? 1 : 0);
            if (key != prayLeftKey) { prayLeftKey = key; prayLeftText = Lang.F("dalam {0}", Fmt.Left(left)); }
            prayRoller.Set(prayLeftText);
            prayRoller.Draw(p, Ink.Callout, p.T.Sub, prayLeftX, prayLeftY, StringAlignment.Near, t);
            if (prayRoller.Moving(t)) return 1 / 60.0;
            return left.TotalSeconds - Math.Floor(left.TotalMinutes) * 60 + 0.02;
        }

        // The city is chosen in Settings; the island just opens that page
        void CityMenu() { OpenPrefs("remind"); }


        void UseCity(string packed)
        {
            if (string.IsNullOrEmpty(packed)) return;
            settings.Set("pray.city", packed);
            settings.Set("pray", true);
            settings.Save();
            prayToday = null;
            Kick();
            // New coordinates: refresh within the minute instead of waiting 30
            weatherAskedUtc = DateTime.MinValue;
            TickWeather();
        }


        // Keyboard opens under the control
        Point MenuAt(string id)
        {
            if (keyboardMode)
                foreach (var h in hits)
                    if (h.Id == id) return PointToScreen(new Point((int)(h.Rect.X * scale), (int)(h.Rect.Bottom * scale)));
            return Control.MousePosition;
        }


        // Slim row: glyph, title, switch
        void DrawWellRow(RectangleF r, int i, bool separator)
        {
            float cy = r.Y + r.Height / 2, tx = r.X + 38;
            var kind = i == 0 ? RemindKind.Drink : i == 1 ? RemindKind.Eyes : RemindKind.Stretch;
            p.Icon(RemindGlyph(kind), 15f, p.T.Text, r.X + 18, cy);
            var sw = new RectangleF(r.Right - 10 - 38, cy - 11, 38, 22);
            float right = sw.X - 14;
            if (i == 0)
            {
                // +1, then glasses as dots
                var plus = new RectangleF(right - 40, cy - 13, 40, 26);
                Pill(plus, "drinkPlus", "+1", PillKind.Secondary, actDrinkPlus);
                right = plus.X - 12;
                right -= DrinkDots(right, cy) + 12;
            }
            Switch(sw, WellIds[i], settings.GetBool(WellKeys[i], false), wellActs[i]);
            string title = i == 0 ? Lang.T("Minum air") : i == 1 ? Lang.T("Istirahat mata 20-20-20") : Lang.T("Peregangan");
            p.Text(title, Ink.Body, p.T.Text, tx, cy, right - tx, StringAlignment.Near);
            if (separator) p.G.DrawLine(p.P(p.T.Line, 1f), tx, r.Bottom - 0.5f, r.Right - 12, r.Bottom - 0.5f);
        }

        // Dots up to 12, else a bar
        float DrinkDots(float right, float cy)
        {
            int goal = DrinkGoal, n = Math.Min(goal, DrinkCount());
            if (goal <= 12)
            {
                const float d = 6, gap = 4;
                float width = goal * d + (goal - 1) * gap, x = right - width;
                for (int k = 0; k < goal; k++)
                    p.Circle(x + k * (d + gap) + d / 2, cy, d / 2, k < n ? p.T.Accent : p.T.Quaternary);
                return width;
            }
            var bar = new RectangleF(right - 96, cy - 2.5f, 96, 5);
            p.FillRound(bar, 2.5f, p.T.Quaternary);
            if (n > 0) p.FillRound(bar, 2.5f, p.Split(bar.X, bar.Width, bar.X + bar.Width * n / goal, p.T.Accent, Color.Transparent));
            return 96;
        }
    }
}

