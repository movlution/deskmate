using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.Runtime.InteropServices;

namespace Deskmate
{
    // Spring presets: response seconds, damping ratio
    static class Springs
    {
        public const float SnappyResponse = 0.26f, SnappyDamping = 0.86f;
        public const float SmoothResponse = 0.42f, SmoothDamping = 1f;
        public const float BouncyResponse = 0.42f, BouncyDamping = 0.75f;
        public const float InteractiveResponse = 0.16f, InteractiveDamping = 0.86f;

        public static void Tune(Spring s, float response, float damping)
        {
            float w = (float)(2 * Math.PI / Math.Max(0.05f, Anim.Response(response)));
            s.K = w * w;
            s.D = 2 * Anim.Damping(damping) * w;
        }

        public static void Snappy(Spring s) { Tune(s, SnappyResponse, SnappyDamping); }
        public static void Smooth(Spring s) { Tune(s, SmoothResponse, SmoothDamping); }
        public static void Bouncy(Spring s) { Tune(s, BouncyResponse, BouncyDamping); }
        public static void Interactive(Spring s) { Tune(s, InteractiveResponse, InteractiveDamping); }

        // Step response after t seconds
        public static float Progress(float t, float response, float damping)
        {
            if (t <= 0) return 0;
            damping = Anim.Damping(damping);
            double w = 2 * Math.PI / Math.Max(0.05f, Anim.Response(response));
            if (damping >= 1) return (float)(1 - (1 + w * t) * Math.Exp(-w * t));
            double wd = w * Math.Sqrt(1 - damping * damping), e = Math.Exp(-damping * w * t);
            return (float)(1 - e * (Math.Cos(wd * t) + damping * w / wd * Math.Sin(wd * t)));
        }

        // Settled within a hair
        public static bool Done(float t, float response) { return t > response * 1.6f; }
    }
}
