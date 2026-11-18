using System;
using System.Drawing;

namespace Deskmate
{
    // One material vocabulary shared by the island, dock, cards, and sheets.
    enum GlassKind { Clear, Liquid, Frost, Solid }

    // Every surface takes its tint, blur, chrome, and wallpaper fade from this
    // recipe. GPU and software paths read the same numbers so a material keeps
    // its identity when CompGlass is unavailable.
    static class GlassRecipe
    {
        // Frost carries a little more neutral veil than the adaptive guard's
        // base tint. The guard still owns contrast; this only makes the surface
        // read as frosted rather than as a flat sheet of the wallpaper colour.
        public const float FrostExtra = 0.16f;

        public static GlassKind FromName(string name, bool glass)
        {
            if (name == "clear") return GlassKind.Clear;
            if (name == "liquid") return GlassKind.Liquid;
            if (name == "frost") return GlassKind.Frost;
            if (name == "solid") return GlassKind.Solid;
            return glass ? GlassKind.Liquid : GlassKind.Solid;
        }

        public static GlassKind FromFlags(bool liquid, bool clear)
        {
            return clear ? GlassKind.Clear : liquid ? GlassKind.Liquid : GlassKind.Frost;
        }

        public static bool IsGlass(GlassKind kind) { return kind != GlassKind.Solid; }

        // Denser frost, thinner liquid, and a very thin clear pane. The dock bar
        // is denser than the island because it can sit over arbitrary windows.
        public static int TintAlpha(GlassKind kind, bool light, bool panelOrSheet, bool bar)
        {
            if (kind == GlassKind.Solid) return 255;
            if (kind == GlassKind.Clear)
                return light ? (bar ? 72 : panelOrSheet ? 64 : 46)
                             : (bar ? 96 : panelOrSheet ? 82 : 58);
            if (kind == GlassKind.Liquid)
                return light ? (bar ? 212 : panelOrSheet ? 184 : 168)
                             : (bar ? 212 : panelOrSheet ? 184 : 164);
            // Frost
            return light ? (bar ? 232 : panelOrSheet ? 224 : 218)
                         : (bar ? 236 : panelOrSheet ? 224 : 214);
        }

        public static Color Tint(GlassKind kind, Color veil, bool light, bool panelOrSheet, bool bar)
        {
            Color c = TintBase(kind, veil, light);
            return Color.FromArgb(TintAlpha(kind, light, panelOrSheet, bar), c.R, c.G, c.B);
        }

        // A tint colour without its alpha. Clear lets the backdrop set the hue;
        // Liquid pushes that hue away from grey; Frost pulls it into a neutral
        // smoked or milky pane.
        public static Color TintBase(GlassKind kind, Color veil, bool light)
        {
            if (kind == GlassKind.Clear) return veil;
            if (kind == GlassKind.Liquid)
            {
                // Live GPU glass has no wallpaper hue, so the veil is the theme's
                // neutral base. A little accent keeps Liquid visibly coloured
                // without turning every material into the same smoked pane.
                Color vivid = Saturate(veil, 1.32f);
                Color mixed = Saturate(Ink.Mix(vivid, Ink.Base.Accent, light ? 0.22f : 0.24f), 1.10f);
                // A pale accent must still leave white text readable over a bright
                // window; deep Liquid takes a little black so the veil is not
                // washed out by its own saturation.
                return light ? mixed : Ink.Mix(mixed, Color.Black, 0.26f);
            }
            if (kind == GlassKind.Frost) return FrostTint(veil, light);
            return Ink.Base.Bg;
        }

        public static Color FrostTint(Color veil, bool light)
        {
            // Mid-neutral, not near-black: Frost should read as a smoky, milky
            // pane and stay clearly lighter than the deep Liquid surface.
            Color neutral = light ? Color.FromArgb(255, 226, 230, 236)
                                  : Color.FromArgb(255, 88, 94, 102);
            return Ink.Mix(Saturate(veil, 0.34f), neutral, light ? 0.66f : 0.62f);
        }

        public static float Blur(GlassKind kind, bool panelOrSheet, bool bar, float scale)
        {
            float px;
            if (kind == GlassKind.Clear) px = bar ? 46f : panelOrSheet ? 38f : 28f;
            else if (kind == GlassKind.Liquid) px = bar ? 28f : panelOrSheet ? 20f : 14f;
            else if (kind == GlassKind.Frost) px = bar ? 44f : panelOrSheet ? 34f : 28f;
            else px = 0f;
            return px * scale;
        }

        public static float Saturation(GlassKind kind)
        {
            if (kind == GlassKind.Clear) return 1.30f;
            if (kind == GlassKind.Liquid) return 1.62f;
            if (kind == GlassKind.Frost) return 0.58f;
            return 1f;
        }

        // Wallpaper-tinted plates are faded once so the desktop can show through.
        // Clear is the most transparent; Frost stays nearly opaque. Dock cores
        // never use this; FadeCore keeps their interior solid.
        public static float WallOpacity(GlassKind kind, bool panelOrSheet, bool bar)
        {
            if (kind == GlassKind.Clear) return panelOrSheet ? 0.68f : bar ? 0.74f : 0.70f;
            if (kind == GlassKind.Liquid) return panelOrSheet ? 0.84f : 0.84f;
            if (kind == GlassKind.Frost) return panelOrSheet ? 0.93f : 0.92f;
            return 1f;
        }

        // Light veil painted by the WinForms layer over the GPU or baked glass.
        public static float Overlay(GlassKind kind, bool panelOrSheet, bool bar)
        {
            if (kind == GlassKind.Clear) return panelOrSheet ? 0.025f : bar ? 0.020f : 0.012f;
            if (kind == GlassKind.Liquid) return panelOrSheet ? 0.055f : bar ? 0.050f : 0.028f;
            return panelOrSheet ? 0.085f : bar ? 0.075f : 0.045f;
        }

        // Extra veil over a baked software plate. Frost needs the most because
        // its neutral tint is what separates it from Liquid on a tinted desktop.
        public static float PlateVeil(GlassKind kind)
        {
            if (kind == GlassKind.Clear) return 0.10f;
            if (kind == GlassKind.Liquid) return 0.32f;
            return 0.42f;
        }

        // Clear carries almost no veil of its own; the blurred desktop supplies
        // the colour and the text shadow supplies legibility.
        public static float ClearVeil(bool panel, bool light)
        {
            return panel ? (light ? 0.06f : 0.08f) : (light ? 0.04f : 0.05f);
        }

        public static int InnerLight(GlassKind kind, bool light)
        {
            if (kind == GlassKind.Clear) return light ? 58 : 26;
            if (kind == GlassKind.Liquid) return light ? 50 : 20;
            if (kind == GlassKind.Frost) return light ? 68 : 34;
            return 0;
        }

        public static int InnerLine(GlassKind kind, bool light)
        {
            if (kind == GlassKind.Clear) return light ? 12 : 28;
            if (kind == GlassKind.Liquid) return light ? 20 : 38;
            if (kind == GlassKind.Frost) return light ? 10 : 16;
            return 0;
        }

        public static int RimTop(GlassKind kind, bool light)
        {
            if (kind == GlassKind.Clear) return light ? 224 : 176;
            if (kind == GlassKind.Liquid) return light ? 206 : 132;
            if (kind == GlassKind.Frost) return light ? 150 : 64;
            return 46;
        }

        public static int RimSide(GlassKind kind, bool light)
        {
            if (kind == GlassKind.Clear) return light ? 72 : 34;
            if (kind == GlassKind.Liquid) return light ? 60 : 24;
            if (kind == GlassKind.Frost) return light ? 38 : 14;
            return 10;
        }

        public static int RimLip(GlassKind kind, bool light)
        {
            if (kind == GlassKind.Clear) return light ? 140 : 68;
            if (kind == GlassKind.Liquid) return light ? 126 : 52;
            if (kind == GlassKind.Frost) return light ? 92 : 30;
            return 22;
        }

        // Dock rim: slightly softer than the island because it crosses window
        // content and must not draw a hard second outline over a feathered edge.
        public static int DockRimTop(GlassKind kind, bool light)
        {
            if (kind == GlassKind.Clear) return light ? 168 : 122;
            if (kind == GlassKind.Liquid) return light ? 190 : 118;
            if (kind == GlassKind.Frost) return light ? 146 : 78;
            return 0;
        }

        public static int DockRimBottom(GlassKind kind, bool light)
        {
            if (kind == GlassKind.Clear) return light ? 44 : 36;
            if (kind == GlassKind.Liquid) return light ? 54 : 40;
            if (kind == GlassKind.Frost) return light ? 32 : 22;
            return 0;
        }

        public static int Sheen(GlassKind kind, bool light)
        {
            if (kind == GlassKind.Clear) return light ? 24 : 8;
            if (kind == GlassKind.Liquid) return light ? 78 : 28;
            if (kind == GlassKind.Frost) return light ? 42 : 14;
            return 0;
        }

        // Top wash for cards and sheets.
        public static int TopWash(GlassKind kind, bool light)
        {
            if (kind == GlassKind.Clear) return light ? 50 : 12;
            if (kind == GlassKind.Liquid) return light ? 82 : 28;
            if (kind == GlassKind.Frost) return light ? 62 : 18;
            return 0;
        }

        public static int Border(GlassKind kind, bool light)
        {
            if (kind == GlassKind.Clear) return light ? 62 : 32;
            if (kind == GlassKind.Liquid) return light ? 80 : 48;
            if (kind == GlassKind.Frost) return light ? 54 : 26;
            return light ? 30 : 20;
        }

        public static float Shelf(GlassKind kind)
        {
            if (kind == GlassKind.Clear) return 0.18f;
            if (kind == GlassKind.Liquid) return 0.30f;
            if (kind == GlassKind.Frost) return 0.42f;
            return 0.35f;
        }

        // Clear glass can sit over a bright window even in the dark theme. Its
        // text gets a contrasting shadow instead of a denser veil.
        public static bool NeedsShadow(GlassKind kind) { return kind == GlassKind.Clear; }

        public static Color Saturate(Color c, float amount)
        {
            float grey = 0.2126f * c.R + 0.7152f * c.G + 0.0722f * c.B;
            return Color.FromArgb(c.A,
                Clamp(grey + (c.R - grey) * amount),
                Clamp(grey + (c.G - grey) * amount),
                Clamp(grey + (c.B - grey) * amount));
        }

        static int Clamp(float v)
        {
            return v < 0f ? 0 : v > 255f ? 255 : (int)(v + 0.5f);
        }
    }
}
