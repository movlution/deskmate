using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.Runtime.InteropServices;

namespace Deskmate
{
    // Calm tinted neutrals: violet-charcoal night, ivory paper; Base/Flip swap inverts
    class Tone
    {
        // Labels: primary, secondary, tertiary; fills and separator
        public Color Bg, Text, Sub, Faint, Fill, FillHover, Line;
        // Disabled ink and tracks; held module
        public Color Quaternary, FillPressed;
        // Module light: top glow, bottom lip
        public Color Highlight, Lip;
        // Accent: pill, bars, progress; ink on top of it
        public Color Accent, OnAccent, Progress;
        // Semantic: mint positive, soft coral negative, amber warning
        public Color Positive, Negative, Warning;

        public bool IsDark { get { return Bg.R + Bg.G + Bg.B < 384; } }

        // Glass can sit on a bright wallpaper: raise secondary ink so the
        // violet-charcoal canvas still keeps 4.5:1 after the wallpaper tint,
        // and lift night ink nearly to white where a white desktop bleeds through.
        public Tone OnGlass()
        {
            var t = (Tone)MemberwiseClone();
            if (IsDark) t.Text = Ink.Mix(Text, Color.White, 0.6f);
            t.Sub = Color.FromArgb(Math.Max((int)Sub.A, 240), Sub);
            return t;
        }

        // Ivory on violet charcoal: brand plum calmed to a near-neutral night
        public static readonly Color Night = Color.FromArgb(255, 0x1C, 0x1A, 0x22), Ivory = Color.FromArgb(255, 0xF5, 0xF2, 0xEC), Plum = Color.FromArgb(255, 0x21, 0x1C, 0x2A);

        public static Tone Dark()
        {
            return Defaults(new Tone
            {
                Bg = Night,
                Text = Ivory,
                Sub = Color.FromArgb(158, Ivory),
                // 148/255 keeps tertiary text at 4.5:1 on the surface
                Faint = Color.FromArgb(148, Ivory),
                Fill = Color.FromArgb(18, Ivory),
                FillHover = Color.FromArgb(28, Ivory),
                Line = Color.FromArgb(23, Ivory),
            });
        }

        static Tone Defaults(Tone t)
        {
            t.Accent = t.Text;
            t.OnAccent = t.Bg;
            t.Progress = t.Sub;
            bool dark = t.IsDark;
            t.Positive = dark ? Color.FromArgb(255, 0x80, 0xD0, 0xA8) : Color.FromArgb(255, 0x2B, 0x75, 0x52);
            t.Negative = dark ? Color.FromArgb(255, 0xF0, 0xA0, 0x90) : Color.FromArgb(255, 0xAE, 0x46, 0x39);
            t.Warning = dark ? Color.FromArgb(255, 0xE5, 0xC0, 0x70) : Color.FromArgb(255, 0x7F, 0x5F, 0x14);
            Derive(t);
            return t;
        }

        // Inks that follow text and fill
        public static void Derive(Tone t)
        {
            bool dark = t.IsDark;
            t.Quaternary = Ink.A(t.Text, 0.18f);
            t.FillPressed = Ink.A(t.Fill, dark ? 0.57f : 2.2f);
            t.Highlight = dark ? Color.FromArgb(23, 255, 255, 255) : Color.FromArgb(110, 255, 255, 255);
            t.Lip = dark ? Color.FromArgb(16, 255, 255, 255) : Color.FromArgb(150, 255, 255, 255);
        }

        public static Tone Paper()
        {
            return Defaults(new Tone
            {
                Bg = Ivory,
                Text = Plum,
                Sub = Color.FromArgb(170, Plum),
                Faint = Color.FromArgb(158, Plum),
                Fill = Color.FromArgb(14, Plum),
                FillHover = Color.FromArgb(24, Plum),
                Line = Color.FromArgb(24, Plum),
            });
        }
    }

    // Vector symbols drawn as cached paths
    enum Sym { Play, Pause, Next, Prev, Stop, Check, Plus, Minus, Xmark, ChevronDown, ChevronUp, ChevronRight }

    static class Ink
    {
        static Ink()
        {
            Init();
        }

        public static Tone Base = Tone.Dark(), Flip = Tone.Paper();
        public static bool Light;

        static Color? accent;
        // Glass veil: wallpaper-tinted while wallpaper sync is on, else the tone's own background
        public static Color? VeilTint;
        public static Color Veil { get { return VeilTint ?? Base.Bg; } }
        // Theme pack colors; "card." keys style the light cards
        static Dictionary<string, string> pack = new Dictionary<string, string>();

        // Calm presets: dusty pastels on the night canvas, deep inks on paper. Same slot = same name.
        public static readonly string[][] DarkAccents =
        {
            new[] { "Biru", "#8FB1E6" },
            new[] { "Ungu", "#AE9DEB" },
            new[] { "Merah muda", "#E59CBC" },
            new[] { "Merah", "#EC9488" },
            new[] { "Oranye", "#E8A877" },
            new[] { "Kuning", "#E2C67A" },
            new[] { "Hijau", "#7ECDA5" },
            new[] { "Grafit", "" },
        };

        public static readonly string[][] LightAccents =
        {
            new[] { "Biru", "#3B66AD" },
            new[] { "Ungu", "#6650B8" },
            new[] { "Merah muda", "#A3456E" },
            new[] { "Merah", "#AE4639" },
            new[] { "Oranye", "#975323" },
            new[] { "Kuning", "#78601A" },
            new[] { "Hijau", "#2C7552" },
            new[] { "Grafit", "" },
        };

        // New installs start on the brand's calm violet; a saved pick, Grafit included, always wins
        public const string DefaultAccent = "#AE9DEB";

        // Saturated presets from before the calm palette, same slots as above
        static readonly string[] legacyDark = { "#4C8DFF", "#A78BFA", "#FF6B9A", "#FF5252", "#FF9F43", "#FFD24A", "#3DD68C" };
        static readonly string[] legacyLight = { "#1668E8", "#6C3CE0", "#C2185B", "#C5221F", "#A84B00", "#855E00", "#107C41" };

        // A saved legacy preset keeps its slot in the calm palette; anything else is returned as is
        public static string UpgradeAccent(string hex)
        {
            for (int i = 0; i < legacyDark.Length; i++)
            {
                if (string.Equals(hex, legacyDark[i], StringComparison.OrdinalIgnoreCase)) return DarkAccents[i][1];
                if (string.Equals(hex, legacyLight[i], StringComparison.OrdinalIgnoreCase)) return LightAccents[i][1];
            }
            return hex;
        }

        // A preset's twin in the other theme; custom colours pass through
        static Color Counterpart(Color c, string[][] from, string[][] to)
        {
            Color p;
            for (int i = 0; i < from.Length; i++)
                if (ParseRgb(from[i][1], out p) && p.ToArgb() == c.ToArgb() && ParseRgb(to[i][1], out p)) return p;
            return c;
        }

        static bool ParseRgb(string hex, out Color c)
        {
            c = Color.Empty;
            int v;
            if (hex == null || hex.Length != 7 || hex[0] != '#' || !int.TryParse(hex.Substring(1), System.Globalization.NumberStyles.HexNumber, null, out v)) return false;
            c = Color.FromArgb(255, (v >> 16) & 255, (v >> 8) & 255, v & 255);
            return true;
        }

        // Categorical labels for user-chosen tags: key (stored), name (Lang key), night twin, paper twin.
        // Same dusty family as the accents; every twin is >= 4.9:1 on Window and Group.
        public static readonly string[][] Labels =
        {
            new[] { "amber", "Amber", "#E6BF6E", "#86621A" },
            new[] { "emerald", "Zamrud", "#7ECDA5", "#2C7552" },
            new[] { "sky", "Biru", "#8CC4E0", "#2D6C8C" },
            new[] { "violet", "Ungu", "#AE9DEB", "#6650B8" },
            new[] { "rose", "Mawar", "#E59CBC", "#A3456E" },
            new[] { "coral", "Merah", "#EC9488", "#AE4639" },
            new[] { "slate", "Grafit", "#ABA8BC", "#5B5770" },
        };

        // A label's colour in the current theme; Empty for "" or an unknown key
        public static Color Label(string key) { return Label(key, Light); }

        public static Color Label(string key, bool light)
        {
            Color c;
            foreach (var row in Labels)
                if (string.Equals(row[0], key, StringComparison.OrdinalIgnoreCase) && ParseRgb(row[light ? 3 : 2], out c)) return c;
            return Color.Empty;
        }

        // Whichever brand ink wins on a filled swatch: tinted night or ivory, by WCAG contrast
        public static Color On(Color fill) { return Island.Contrast(fill, Tone.Night) >= Island.Contrast(fill, Tone.Ivory) ? Tone.Night : Tone.Ivory; }

        // Markers drawn onto user images (screenshots, photos): theme-independent, vivid but not neon,
        // so a stroke survives both white documents and dark apps. Name (Lang key), hex.
        public static readonly string[][] Markers =
        {
            new[] { "Merah", "#E5484D" }, new[] { "Kuning", "#F5C451" }, new[] { "Hijau", "#3FB27F" }, new[] { "Biru", "#3E7BDB" },
            new[] { "Oranye", "#F0883E" }, new[] { "Ungu", "#8C6CE0" }, new[] { "Putih", "#FFFFFF" }, new[] { "Hitam", "#17141D" },
        };

        // Shade laid over content: night black on the dark theme, plum on paper
        public static Color Scrim(int alpha) { return Color.FromArgb(Math.Max(0, Math.Min(255, alpha)), Light ? Tone.Plum : Color.FromArgb(0x0B, 0x0A, 0x0E)); }

        // Light laid over content: ivory on the dark theme, paper white on light
        public static Color Sheen(int alpha) { return Color.FromArgb(Math.Max(0, Math.Min(255, alpha)), Light ? Color.White : Color.FromArgb(0xFB, 0xF9, 0xF5)); }

        // Celebration effects (combo, edge light): joyful but softened to sit beside the calm palette
        public static readonly Color FxSky = Color.FromArgb(255, 0x6E, 0xC6, 0xEA), FxMint = Color.FromArgb(255, 0x6B, 0xD6, 0x9C),
            FxGold = Color.FromArgb(255, 0xF4, 0xCD, 0x5C), FxApricot = Color.FromArgb(255, 0xF4, 0xA3, 0x61),
            FxRose = Color.FromArgb(255, 0xF0, 0x7A, 0x98), FxCoral = Color.FromArgb(255, 0xF0, 0x80, 0x7F),
            FxLavender = Color.FromArgb(255, 0xB5, 0x95, 0xEC), FxCream = Color.FromArgb(255, 0xFB, 0xF3, 0xDE);

        // Confetti mix, cool to warm
        public static readonly Color[] FxConfetti = { FxSky, FxMint, FxGold, FxApricot, FxRose, FxLavender, FxCream };

        public static void SetPack(Dictionary<string, string> colors) { pack = colors ?? new Dictionary<string, string>(); }

        static void Override(Tone t, string prefix)
        {
            Color c;
            if (Hex(prefix + "bg", out c)) t.Bg = c;
            if (Hex(prefix + "text", out c)) t.Text = c;
            if (Hex(prefix + "sub", out c)) t.Sub = c;
            if (Hex(prefix + "faint", out c)) t.Faint = c;
            if (Hex(prefix + "fill", out c)) t.Fill = c;
            if (Hex(prefix + "fillHover", out c)) t.FillHover = c;
            if (Hex(prefix + "line", out c)) t.Line = c;
            if (Hex(prefix + "accent", out c)) t.Accent = t.Progress = c;
            if (Hex(prefix + "onAccent", out c)) t.OnAccent = c;
            if (Hex(prefix + "progress", out c)) t.Progress = c;
            if (Hex(prefix + "positive", out c)) t.Positive = c;
            if (Hex(prefix + "negative", out c)) t.Negative = c;
            if (Hex(prefix + "warning", out c)) t.Warning = c;
            Tone.Derive(t);
            if (Hex(prefix + "quaternary", out c)) t.Quaternary = c;
            if (Hex(prefix + "fillPressed", out c)) t.FillPressed = c;
            if (Hex(prefix + "highlight", out c)) t.Highlight = c;
            if (Hex(prefix + "lip", out c)) t.Lip = c;
        }

        // "#RRGGBB" or "#AARRGGBB"
        static bool Hex(string key, out Color c)
        {
            c = Color.Empty;
            string v;
            if (!pack.TryGetValue(key, out v) || v.Length < 7 || v[0] != '#') return false;
            uint n;
            if (!uint.TryParse(v.Substring(1), System.Globalization.NumberStyles.HexNumber, null, out n)) return false;
            c = v.Length == 9 ? Color.FromArgb((int)(n >> 24), (int)((n >> 16) & 255), (int)((n >> 8) & 255), (int)(n & 255)) : Color.FromArgb(255, (int)((n >> 16) & 255), (int)((n >> 8) & 255), (int)(n & 255));
            return true;
        }

        // Bumped on look change; caches compare
        public static int Epoch;

        public static void Apply(bool light)
        {
            Epoch++;
            Light = light;
            Base = light ? Tone.Paper() : Tone.Dark();
            Flip = light ? Tone.Dark() : Tone.Paper();
            if (accent.HasValue)
            {
                var c = light ? Counterpart(accent.Value, DarkAccents, LightAccents) : Counterpart(accent.Value, LightAccents, DarkAccents);
                if (light && Island.Contrast(c, Base.Bg) < 3.5)
                    c = Island.EnforceContrast(c, Base.Bg, 3.5);
                Base.Accent = Base.Progress = c;
                // Ink on the accent: the tinted night or ivory, whichever wins on WCAG contrast;
                // mid-tone customs that neither carries to 4.5:1 are pushed further out.
                Base.OnAccent = Island.EnforceContrast(On(c), c, 4.5);
            }
            Override(Base, "");
            Override(Flip, "card.");
        }

        // "#RRGGBB", or empty for monochrome
        public static void SetAccent(string hex)
        {
            Color c;
            accent = ParseRgb(hex, out c) ? c : (Color?)null;
            Apply(Light);
        }

        // Segoe Fluent Icons code points
        public const char IcoMic = '\uE720', IcoCam = '\uE714', IcoWifi = '\uE701', IcoBolt = '\uE945', IcoBell = '\uEA8F';
        public const char IcoVol0 = '\uE992', IcoVol1 = '\uE993', IcoVol2 = '\uE994', IcoVol3 = '\uE995', IcoMute = '\uE74F';
        public const char IcoCalendar = '\uE787', IcoRefresh = '\uE72C', IcoLock = '\uE72E', IcoGlobe = '\uE774', IcoPlug = '\uE83E';
        public const char IcoNoNet = '\uF384', IcoTerminal = '\uE756', IcoOpen = '\uE8A7';
        public const char IcoBluetooth = '\uE702', IcoQuiet = '\uE708', IcoHide = '\uED1A', IcoSun = '\uE706', IcoTimer = '\uE916';

        // GDI+ samples gradients at build size
        public const float GradientSpan = 1024;
        // Lifts text so caps centre on cy
        public const float CapLift = 0.05f;

        // Spacing scale, 1x px
        public const float Sp4 = 4, Sp8 = 8, Sp12 = 12, Sp16 = 16, Sp20 = 20, Sp24 = 24, Sp32 = 32;
        // Concentric radii: shell, module, card, control; nested thumbs subtract their inset
        public const float RadiusShell = 32, RadiusModule = 20, RadiusCard = 16, RadiusControl = 12;
        // Settings groups and the page hero card; 14 reads soft beside 12 px controls without
        // out-rounding the 25 px nest limit of the hero tile (11 + 14 inset)
        public const float RadiusList = 14;
        // Module grid: shell inset, gap, inner padding
        public const float ModuleInset = 12, ModuleGap = 10, ModulePad = 16;

        const int FDisplay = 0, FText = 1, FSemi = 2, FSmall = 3, FMono = 4, FIcons = 5, FSmallSemi = 6;
        static readonly string[] families = new string[7];

        public static string MenuFamily { get { return families[FText]; } }
        public static string IconFamily { get { return families[FIcons]; } }
        // Integer keys: no string per lookup
        static readonly Dictionary<int, Font> fonts = new Dictionary<int, Font>();

        public static void Init()
        {
            // Ask for the few families we use; enumerating every installed font costs seconds on a cold logon
            var have = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
            families[FDisplay] = Pick(have, "Segoe UI Variable Display Semibold", "Segoe UI Variable Display Semib", "Segoe UI Semibold", "Segoe UI");
            families[FText] = Pick(have, "Segoe UI Variable Text", "Segoe UI");
            families[FSemi] = Pick(have, "Segoe UI Variable Text Semibold", "Segoe UI Semibold", "Segoe UI");
            families[FSmall] = Pick(have, "Segoe UI Variable Small", "Segoe UI");
            families[FMono] = Pick(have, "Cascadia Mono", "Consolas", "Courier New");
            families[FIcons] = Pick(have, "Segoe Fluent Icons", "Segoe MDL2 Assets", "Segoe UI Symbol");
            families[FSmallSemi] = Pick(have, "Segoe UI Variable Small Semibold", "Segoe UI Variable Small Semibol", "Segoe UI Semibold", "Segoe UI");
        }

        static string Pick(Dictionary<string, bool> have, params string[] names)
        {
            foreach (var n in names)
            {
                if (Installed(have, n)) return n;
                if (n.Length > 31 && Installed(have, n.Substring(0, 31))) return n.Substring(0, 31);
            }
            return System.Drawing.SystemFonts.MessageBoxFont.FontFamily.Name;
        }

        // Exact family match only: GDI+ throws for a name it does not have
        static bool Installed(Dictionary<string, bool> have, string name)
        {
            bool ok;
            if (have.TryGetValue(name, out ok)) return ok;
            try
            {
                using (var f = new FontFamily(name)) ok = string.Equals(f.Name, name, StringComparison.OrdinalIgnoreCase);
            }
            catch (ArgumentException) { ok = false; }
            have[name] = ok;
            return ok;
        }

        public static Font Display(float px) { return Get(FDisplay, px); }
        public static Font Text(float px) { return Get(FText, px); }
        public static Font Semi(float px) { return Get(FSemi, px); }
        public static Font Small(float px) { return Get(FSmall, px); }
        public static Font SmallSemi(float px) { return Get(FSmallSemi, px); }
        public static Font Mono(float px) { return Get(FMono, px); }
        public static Font Icons(float px) { return Get(FIcons, px); }

        // Type scale roles
        public static Font Hero { get { return Get(FDisplay, 40f); } }
        public static Font Title1 { get { return Get(FDisplay, 22f); } }
        public static Font Title2 { get { return Get(FSemi, 17f); } }
        public static Font Headline { get { return Get(FSemi, 14.5f); } }
        public static Font Body { get { return Get(FText, 13.5f); } }
        public static Font Callout { get { return Get(FText, 12.5f); } }
        public static Font Caption { get { return Get(FSmall, 11f); } }

        static Font Get(int family, float px)
        {
            int key = family * 100000 + (int)Math.Round(px * 10);
            Font f;
            if (!fonts.TryGetValue(key, out f))
            {
                f = new Font(families[family] ?? "Segoe UI", px, FontStyle.Regular, GraphicsUnit.Pixel);
                fonts[key] = f;
            }
            return f;
        }

        // One-char strings, made once
        static readonly Dictionary<char, string> chars = new Dictionary<char, string>();

        public static string Str(char c)
        {
            string s;
            if (!chars.TryGetValue(c, out s)) { s = c.ToString(); chars[c] = s; }
            return s;
        }

        // Glyph ink centred, not its em box
        static readonly Dictionary<long, PointF> nudges = new Dictionary<long, PointF>();
        static StringFormat nudgeFormat;

        public static PointF IconNudge(char glyph, float px)
        {
            // Speaker glyphs share one centre: no jump
            char shape = (glyph >= IcoVol0 && glyph <= IcoVol3) || glyph == IcoMute ? IcoVol3 : glyph;
            long key = ((long)shape << 24) | (long)Math.Round(px * 8);
            PointF n;
            if (nudges.TryGetValue(key, out n)) return n;
            if (nudgeFormat == null)
            {
                nudgeFormat = (StringFormat)StringFormat.GenericTypographic.Clone();
                nudgeFormat.FormatFlags |= StringFormatFlags.NoWrap;
                nudgeFormat.Alignment = StringAlignment.Center;
                nudgeFormat.LineAlignment = StringAlignment.Center;
            }
            try
            {
                using (var fam = new FontFamily(IconFamily))
                using (var path = new GraphicsPath())
                {
                    path.AddString(Str(shape), fam, 0, px, new RectangleF(-px, -px, px * 2, px * 2), nudgeFormat);
                    var r = path.GetBounds();
                    n = r.Width > 0 ? new PointF(-(r.X + r.Width / 2), -(r.Y + r.Height / 2)) : PointF.Empty;
                }
            }
            catch (Exception) { n = PointF.Empty; }
            nudges[key] = n;
            return n;
        }

        // Fonts are cached: compare by reference, skip GDI+ name lookups
        internal sealed class SameFont : IEqualityComparer<Font>
        {
            public bool Equals(Font a, Font b) { return ReferenceEquals(a, b); }
            public int GetHashCode(Font f) { return System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(f); }
        }

        static readonly Dictionary<Font, float> capShifts = new Dictionary<Font, float>(new SameFont());

        // Offset that puts a capital's middle on the line centre
        public static float CapShift(Font f)
        {
            float shift;
            if (capShifts.TryGetValue(f, out shift)) return shift;
            if (nudgeFormat == null) IconNudge(' ', 1);
            var fmt = (StringFormat)nudgeFormat.Clone();
            fmt.Alignment = StringAlignment.Near;
            float h = f.Size * 2;
            try
            {
                using (var fam = new FontFamily(f.Name))
                using (var path = new GraphicsPath())
                {
                    path.AddString("H", fam, (int)f.Style, f.Size, new RectangleF(0, -h / 2, 1000, h), fmt);
                    var r = path.GetBounds();
                    shift = r.Height > 0 ? -(r.Y + r.Height / 2) : -f.Size * CapLift;
                }
            }
            catch (Exception) { shift = -f.Size * CapLift; }
            finally { fmt.Dispose(); }
            capShifts[f] = shift;
            return shift;
        }

        static readonly string[] percents = new string[101];

        public static string Percent(int v)
        {
            v = Math.Max(0, Math.Min(100, v));
            return percents[v] ?? (percents[v] = v + "%");
        }

        public static Color A(Color c, float a)
        {
            return Color.FromArgb(Math.Max(0, Math.Min(255, (int)(c.A * a))), c.R, c.G, c.B);
        }

        public static Color Mix(Color a, Color b, float t)
        {
            t = Ease.Clamp01(t);
            return Color.FromArgb((int)(a.A + (b.A - a.A) * t), (int)(a.R + (b.R - a.R) * t), (int)(a.G + (b.G - a.G) * t), (int)(a.B + (b.B - a.B) * t));
        }

        // Top over under, straight alpha
        public static Color Over(Color top, Color under)
        {
            float at = top.A / 255f, au = under.A / 255f;
            float a = at + au * (1 - at);
            if (a < 0.002f) return Color.FromArgb(0, under);
            float k = au * (1 - at);
            return Color.FromArgb((int)(a * 255 + 0.5f), (int)((top.R * at + under.R * k) / a + 0.5f), (int)((top.G * at + under.G * k) / a + 0.5f), (int)((top.B * at + under.B * k) / a + 0.5f));
        }

        // Edge notch: flared shoulders, continuous corners
        public static GraphicsPath Notch(RectangleF r, float radius, float shoulder)
        {
            var path = new GraphicsPath();
            AddNotch(path, r, radius, shoulder);
            return path;
        }

        public static void AddNotch(GraphicsPath path, RectangleF r, float radius, float shoulder)
        {
            float top = r.Y - 2, left = r.Left, right = r.Right, bottom = r.Bottom;
            float s = Math.Max(0.5f, Math.Min(shoulder, r.Height / 3));
            radius = Math.Max(0.5f, Math.Min(radius, Math.Min(r.Width / 2, bottom - top - s)));
            var k = CornerOf(radius, Smoothing, Math.Min(r.Width / 2, bottom - top - s));
            // Flare wider than tall
            float fw = s * 1.35f;
            path.AddLine(left - fw, top, right + fw, top);
            path.AddBezier(right + fw, top, right + fw * 0.4f, top, right, top + s * 0.45f, right, top + s);
            AddCorner(path, k, right, bottom, 1);
            AddCorner(path, k, left, bottom, 2);
            path.AddBezier(left, top + s, left, top + s * 0.45f, left - fw * 0.4f, top, left - fw, top);
            path.CloseFigure();
        }

        // Notch on a side edge: the top notch, axes swapped
        public static GraphicsPath SideNotch(RectangleF r, float radius, float shoulder, bool right)
        {
            var path = new GraphicsPath();
            AddNotch(path, new RectangleF(r.Y, 0, r.Height, r.Width), radius, shoulder);
            using (var m = right ? new Matrix(0, 1, -1, 0, r.Right, 0) : new Matrix(0, 1, 1, 0, r.Left, 0)) path.Transform(m);
            return path;
        }

        public static GraphicsPath Round(RectangleF r, float radius)
        {
            var p = new GraphicsPath();
            AddRound(p, r, radius);
            return p;
        }

        // Corner smoothing; 0.6 matches iOS
        public const float Smoothing = 0.6f;

        // Corner curve length along each edge
        public static float CornerExtent(float radius) { return (1 + Smoothing) * radius; }

        // Squircle into a caller's reusable path
        public static void AddRound(GraphicsPath p, RectangleF r, float radius)
        {
            float budget = Math.Min(r.Width, r.Height) / 2f;
            radius = Math.Min(radius, budget);
            if (radius < 0.5f) { p.AddRectangle(r); return; }
            var k = CornerOf(radius, Smoothing, budget);
            AddCorner(p, k, r.Right, r.Top, 0);
            AddCorner(p, k, r.Right, r.Bottom, 1);
            AddCorner(p, k, r.Left, r.Bottom, 2);
            AddCorner(p, k, r.Left, r.Top, 3);
            p.CloseFigure();
        }

        struct Corner { public float R, P, A, B, C, D, Arc, Alpha; }

        static double Rad(double deg) { return deg * Math.PI / 180; }

        // figma-squircle parameters; smoothing yields to space
        static Corner CornerOf(float radius, float smoothing, float budget)
        {
            float s = smoothing, len = (1 + s) * radius;
            if (len > budget) { s = Math.Max(0, budget / radius - 1); len = Math.Max(radius, budget); }
            float arcMeasure = 90 * (1 - s);
            float arc = (float)(Math.Sin(Rad(arcMeasure / 2)) * radius * Math.Sqrt(2));
            float alpha = (90 - arcMeasure) / 2;
            float p34 = radius * (float)Math.Tan(Rad(alpha / 2));
            float beta = 45 * s;
            float c = p34 * (float)Math.Cos(Rad(beta));
            float d = c * (float)Math.Tan(Rad(beta));
            float b = Math.Max(0, (len - arc - c - d) / 3);
            return new Corner { R = radius, P = len, A = 2 * b, B = b, C = c, D = d, Arc = arc, Alpha = alpha };
        }

        // q clockwise from 0 top-right
        static void AddCorner(GraphicsPath p, Corner k, float cx, float cy, int q)
        {
            // u along incoming edge, v outgoing
            float ux = q == 0 ? 1 : q == 2 ? -1 : 0, uy = q == 1 ? 1 : q == 3 ? -1 : 0;
            float vx = -uy, vy = ux;
            float sx = cx - ux * k.P, sy = cy - uy * k.P;
            float ex = cx - ux * (k.D + k.Arc) + vx * k.D, ey = cy - uy * (k.D + k.Arc) + vy * k.D;
            if (k.A + k.B + k.C > 0.01f)
                p.AddBezier(sx, sy, sx + ux * k.A, sy + uy * k.A, sx + ux * (k.A + k.B), sy + uy * (k.A + k.B), ex, ey);
            float ox = cx - ux * k.R + vx * k.R, oy = cy - uy * k.R + vy * k.R;
            p.AddArc(ox - k.R, oy - k.R, 2 * k.R, 2 * k.R, 270 + 90 * q + k.Alpha, 90 - 2 * k.Alpha);
            float ax = cx - ux * k.D + vx * (k.D + k.Arc), ay = cy - uy * k.D + vy * (k.D + k.Arc);
            if (k.A + k.B + k.C > 0.01f)
                p.AddBezier(ax, ay, ax + ux * k.D + vx * k.C, ay + uy * k.D + vy * k.C, ax + ux * k.D + vx * (k.B + k.C), ay + uy * k.D + vy * (k.B + k.C), cx + vx * k.P, cy + vy * k.P);
        }

        sealed class SymbolShape { public GraphicsPath Path; public float Stroke; }
        static readonly Dictionary<int, SymbolShape> symbols = new Dictionary<int, SymbolShape>();

        // Centred on 0,0; stroke 0 means filled
        public static GraphicsPath SymbolPath(Sym kind, float size, out float stroke)
        {
            int key = (int)kind * 100000 + (int)Math.Round(size * 8);
            SymbolShape s;
            if (!symbols.TryGetValue(key, out s))
            {
                if (symbols.Count > 256)
                {
                    foreach (var old in symbols.Values) old.Path.Dispose();
                    symbols.Clear();
                }
                s = BuildSymbol(kind, (float)Math.Round(size * 8) / 8);
                symbols[key] = s;
            }
            stroke = s.Stroke;
            return s.Path;
        }

        // SF Symbols weight: filled, rounded joins
        static SymbolShape BuildSymbol(Sym kind, float s)
        {
            var path = new GraphicsPath(FillMode.Winding);
            float stroke = 0;
            switch (kind)
            {
                case Sym.Play:
                    // Shifted right: optical centre
                    Rounded(path, new[] { new PointF(-0.30f * s, -0.43f * s), new PointF(0.51f * s, 0), new PointF(-0.30f * s, 0.43f * s) }, 0.1f * s);
                    break;
                case Sym.Pause:
                    AddRound(path, new RectangleF(-0.36f * s, -0.42f * s, 0.26f * s, 0.84f * s), 0.085f * s);
                    AddRound(path, new RectangleF(0.10f * s, -0.42f * s, 0.26f * s, 0.84f * s), 0.085f * s);
                    break;
                case Sym.Next:
                case Sym.Prev:
                    int dir = kind == Sym.Next ? 1 : -1;
                    for (int i = 0; i < 2; i++)
                    {
                        float x0 = dir * (-0.5f * s + i * 0.5f * s), x1 = x0 + dir * 0.53f * s;
                        Rounded(path, new[] { new PointF(x0, -0.31f * s), new PointF(x1, 0), new PointF(x0, 0.31f * s) }, 0.07f * s);
                    }
                    break;
                case Sym.Stop:
                    AddRound(path, new RectangleF(-0.36f * s, -0.36f * s, 0.72f * s, 0.72f * s), 0.17f * s);
                    break;
                case Sym.Check:
                    stroke = 0.15f * s;
                    path.AddLines(new[] { new PointF(-0.36f * s, 0.02f * s), new PointF(-0.11f * s, 0.27f * s), new PointF(0.37f * s, -0.29f * s) });
                    break;
                case Sym.Plus:
                    stroke = 0.13f * s;
                    path.AddLine(-0.36f * s, 0, 0.36f * s, 0);
                    path.StartFigure();
                    path.AddLine(0, -0.36f * s, 0, 0.36f * s);
                    break;
                case Sym.Minus:
                    stroke = 0.13f * s;
                    path.AddLine(-0.36f * s, 0, 0.36f * s, 0);
                    break;
                case Sym.Xmark:
                    stroke = 0.13f * s;
                    path.AddLine(-0.3f * s, -0.3f * s, 0.3f * s, 0.3f * s);
                    path.StartFigure();
                    path.AddLine(0.3f * s, -0.3f * s, -0.3f * s, 0.3f * s);
                    break;
                case Sym.ChevronDown:
                case Sym.ChevronUp:
                    stroke = 0.12f * s;
                    float f = kind == Sym.ChevronDown ? 1 : -1;
                    path.AddLines(new[] { new PointF(-0.3f * s, -0.13f * s * f), new PointF(0, 0.15f * s * f), new PointF(0.3f * s, -0.13f * s * f) });
                    break;
                default:
                    stroke = 0.12f * s;
                    path.AddLines(new[] { new PointF(-0.13f * s, -0.3f * s), new PointF(0.15f * s, 0), new PointF(-0.13f * s, 0.3f * s) });
                    break;
            }
            return new SymbolShape { Path = path, Stroke = stroke };
        }

        // Polygon with circular corners of radius r
        static void Rounded(GraphicsPath path, PointF[] pts, float r)
        {
            int n = pts.Length;
            path.StartFigure();
            for (int i = 0; i < n; i++)
            {
                PointF a = pts[(i + n - 1) % n], b = pts[i], c = pts[(i + 1) % n];
                float ux = a.X - b.X, uy = a.Y - b.Y, la = (float)Math.Sqrt(ux * ux + uy * uy);
                float vx = c.X - b.X, vy = c.Y - b.Y, lc = (float)Math.Sqrt(vx * vx + vy * vy);
                ux /= la; uy /= la; vx /= lc; vy /= lc;
                double theta = Math.Acos(Math.Max(-1, Math.Min(1, ux * vx + uy * vy)));
                double phi = Math.PI - theta;
                float d = Math.Min((float)(r / Math.Tan(theta / 2)), Math.Min(la, lc) * 0.45f);
                // Bezier handle as a share of d
                float k = phi < 0.01 ? 0 : (float)(4.0 / 3.0 * Math.Tan(phi / 4) / Math.Tan(phi / 2));
                var t1 = new PointF(b.X + ux * d, b.Y + uy * d);
                var t2 = new PointF(b.X + vx * d, b.Y + vy * d);
                path.AddBezier(t1, new PointF(t1.X + (b.X - t1.X) * k, t1.Y + (b.Y - t1.Y) * k), new PointF(t2.X + (b.X - t2.X) * k, t2.Y + (b.Y - t2.Y) * k), t2);
            }
            path.CloseFigure();
        }

        // Covers stay monochrome: luminance-only copy
        public static Bitmap Grayscale(Image src, int size, bool keepColor = false)
        {
            var bmp = new Bitmap(size, size, PixelFormat.Format32bppPArgb);
            var m = new ColorMatrix(new[]
            {
                new[] { 0.2126f, 0.2126f, 0.2126f, 0, 0 },
                new[] { 0.7152f, 0.7152f, 0.7152f, 0, 0 },
                new[] { 0.0722f, 0.0722f, 0.0722f, 0, 0 },
                new[] { 0f, 0, 0, 1, 0 },
                new[] { 0f, 0, 0, 0, 1 },
            });
            using (var attr = new ImageAttributes())
            using (var g = Graphics.FromImage(bmp))
            {
                if (!keepColor) attr.SetColorMatrix(m);
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                int side = Math.Min(src.Width, src.Height);
                var from = new Rectangle((src.Width - side) / 2, (src.Height - side) / 2, side, side);
                g.DrawImage(src, new Rectangle(0, 0, size, size), from.X, from.Y, from.Width, from.Height, GraphicsUnit.Pixel, attr);
            }
            // Content colour rides along with the copy
            var tone = ContentTone(src);
            if (tone.A > 0) bmp.Tag = tone;
            return bmp;
        }

        // Colour kept by Grayscale, else Empty
        public static Color ToneOf(Image img)
        {
            return img != null && img.Tag is Color ? (Color)img.Tag : Color.Empty;
        }

        // Most saturated mid-luminance hue; Empty when grey
        public static Color ContentTone(Image src)
        {
            const int N = 24, Bins = 18;
            var weight = new float[Bins];
            var sum = new float[Bins * 3];
            try
            {
                using (var tiny = new Bitmap(N, N, PixelFormat.Format32bppArgb))
                {
                    using (var tg = Graphics.FromImage(tiny))
                    {
                        tg.InterpolationMode = InterpolationMode.HighQualityBilinear;
                        tg.DrawImage(src, new Rectangle(0, 0, N, N));
                    }
                    var data = tiny.LockBits(new Rectangle(0, 0, N, N), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
                    var px = new byte[data.Stride * N];
                    Marshal.Copy(data.Scan0, px, 0, px.Length);
                    tiny.UnlockBits(data);
                    for (int y = 0; y < N; y++)
                        for (int x = 0; x < N; x++)
                        {
                            int i = y * data.Stride + x * 4;
                            if (px[i + 3] < 128) continue;
                            float b = px[i] / 255f, g = px[i + 1] / 255f, r = px[i + 2] / 255f;
                            float max = Math.Max(r, Math.Max(g, b)), min = Math.Min(r, Math.Min(g, b)), chroma = max - min;
                            if (chroma < 0.1f) continue;
                            float l = (max + min) / 2, mid = Math.Max(0, 1 - Math.Abs(l - 0.5f) * 1.7f);
                            float w = chroma * mid;
                            float h = max == r ? (g - b) / chroma : max == g ? 2 + (b - r) / chroma : 4 + (r - g) / chroma;
                            int bin = ((int)Math.Floor((h < 0 ? h + 6 : h) / 6 * Bins)) % Bins;
                            weight[bin] += w;
                            sum[bin * 3] += r * w; sum[bin * 3 + 1] += g * w; sum[bin * 3 + 2] += b * w;
                        }
                }
            }
            catch (Exception ex) { Log.Write("tone: " + ex.Message); return Color.Empty; }
            int best = 0;
            for (int i = 1; i < Bins; i++) if (weight[i] + 0.5f * (weight[(i + 1) % Bins] + weight[(i + Bins - 1) % Bins]) > weight[best] + 0.5f * (weight[(best + 1) % Bins] + weight[(best + Bins - 1) % Bins])) best = i;
            // Grey covers: no content colour
            if (weight[best] < N * N * 0.012f) return Color.Empty;
            float wb = weight[best];
            return Color.FromArgb(255, (int)(sum[best * 3] / wb * 255), (int)(sum[best * 3 + 1] / wb * 255), (int)(sum[best * 3 + 2] / wb * 255));
        }

        // Content colour readable on the glass
        public static Color Readable(Color c, bool light)
        {
            float r = c.R / 255f, g = c.G / 255f, b = c.B / 255f;
            float max = Math.Max(r, Math.Max(g, b)), min = Math.Min(r, Math.Min(g, b)), chroma = max - min;
            float l = (max + min) / 2, s = chroma < 0.001f ? 0 : chroma / (1 - Math.Abs(2 * l - 1));
            float h = chroma < 0.001f ? 0 : max == r ? (g - b) / chroma : max == g ? 2 + (b - r) / chroma : 4 + (r - g) / chroma;
            if (h < 0) h += 6;
            // Sky blues stay calm
            float cap = h > 2.9f && h < 3.6f ? 0.5f : 0.78f;
            s = Math.Max(0.42f, Math.Min(cap, s));
            l = light ? Math.Max(0.34f, Math.Min(0.46f, l)) : Math.Max(0.66f, Math.Min(0.78f, l));
            float q = (1 - Math.Abs(2 * l - 1)) * s, x = q * (1 - Math.Abs(h % 2 - 1)), m = l - q / 2;
            float rr = 0, gg = 0, bb = 0;
            if (h < 1) { rr = q; gg = x; } else if (h < 2) { rr = x; gg = q; } else if (h < 3) { gg = q; bb = x; }
            else if (h < 4) { gg = x; bb = q; } else if (h < 5) { rr = x; bb = q; } else { rr = q; bb = x; }
            return Color.FromArgb(255, (int)((rr + m) * 255 + 0.5f), (int)((gg + m) * 255 + 0.5f), (int)((bb + m) * 255 + 0.5f));
        }
    }
}
