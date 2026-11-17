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
}
