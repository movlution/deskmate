using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace Deskmate
{
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
}
