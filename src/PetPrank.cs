using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Windows.Forms;

namespace Deskmate
{
    // Values travel in the "prank" packet; add new ones at the end
    enum PetPrank { Fire = 0, Crack = 1, Paint = 2, Snow = 3 }

    // Harmless mischief a friend's pet plays on this screen: a click-through overlay that never
    // takes focus or the pointer, and clears itself after a few seconds.
    partial class Island
    {
        PetPrankOverlay prankOverlay;
        DateTime prankUntilUtc = DateTime.MinValue;

        internal bool PetAllowPranks { get { return settings.GetBool("pet.link.prank", true); } }

        static bool IsPrank(int kind) { return kind >= 0 && kind <= (int)PetPrank.Snow; }

        static string PrankLabel(PetPrank kind)
        {
            switch (kind)
            {
                case PetPrank.Fire: return Lang.T("Bakar");
                case PetPrank.Crack: return Lang.T("Retakkan");
                case PetPrank.Paint: return Lang.T("Lempar cat");
                default: return Lang.T("Bekukan");
            }
        }

        static string PrankLine(PetPrank kind)
        {
            switch (kind)
            {
                case PetPrank.Fire: return Lang.T("Membakar layarmu!");
                case PetPrank.Crack: return Lang.T("Meretakkan layarmu!");
                case PetPrank.Paint: return Lang.T("Melempar cat ke layarmu!");
                default: return Lang.T("Membekukan layarmu!");
            }
        }

        void SendPetPrank(string peerId, PetPrank kind)
        {
            if (petRoom == null || petRoom.Mode != PetRoomMode.Connected || string.IsNullOrEmpty(peerId)) return;
            petRoom.SendPrank(peerId, (int)kind);
            if (pet != null && !pet.IsDisposed) pet.PopEmote(PetEmote.Bang);
            PetWindow win;
            if (remotePets.TryGetValue(peerId, out win) && !win.IsDisposed)
            {
                if (pet != null && !pet.IsDisposed) pet.FacePoint(win.DrawnCenterX);
                win.PopEmote(PetEmote.Bang);
            }
        }

        void OnRemotePrankReceived(string peerId, int kind)
        {
            if (!IsPrank(kind) || !PetAllowPranks || !PetOn) return;
            PetWindow win;
            if (!remotePets.TryGetValue(peerId, out win) || win.IsDisposed) return;
            // One at a time, with a short breather, so a room can't stack them up
            if (DateTime.UtcNow < prankUntilUtc) return;
            if (prankOverlay != null && !prankOverlay.IsDisposed) return;

            var p = (PetPrank)kind;
            PetPeerProfile prof;
            string sender = remoteProfiles.TryGetValue(peerId, out prof) ? prof.Name : (win.PeerName ?? Lang.T("Teman"));
            win.PopEmote(PetEmote.Bang);
            win.ShowChatBubble(sender, PrankLine(p), Sharing, () => OpenPetChatInput(win));

            // Never over a shared screen, while Deskmate is hidden, or for reduced motion
            if (Sharing || dormant || hiddenByUser || Anim.Reduced) return;
            var dock = dockWin != null && !dockWin.IsDisposed ? dockWin : null;
            var screen = dock != null && dock.PetScreen != null ? dock.PetScreen : ChosenScreen();
            if (screen == null) return;
            prankUntilUtc = DateTime.UtcNow.AddSeconds(PetPrankOverlay.Duration + 4);
            try
            {
                prankOverlay = new PetPrankOverlay(p, screen.Bounds, peerId.GetHashCode() ^ Environment.TickCount);
                prankOverlay.FormClosed += delegate { prankOverlay = null; };
                prankOverlay.Play();
            }
            catch (Exception ex)
            {
                Log.Write("pet prank: " + ex.Message);
                if (prankOverlay != null && !prankOverlay.IsDisposed) prankOverlay.Dispose();
                prankOverlay = null;
            }
        }

        void ClosePrankOverlay()
        {
            if (prankOverlay != null && !prankOverlay.IsDisposed) prankOverlay.Close();
            prankOverlay = null;
        }

        sealed class PetPrankOverlay : Form
        {
            public const double Duration = 6.0;
            const double FadeIn = 0.25, FadeOut = 1.1;

            readonly PetPrank kind;
            readonly Rectangle area;
            readonly Random rng;
            readonly float k;
            readonly Stopwatch clock = new Stopwatch();
            readonly Timer timer = new Timer { Interval = 33 };
            IntPtr memDC, dib, oldBmp, bits;
            Bitmap canvas;
            Graphics g;
            double lastT;
            int staticStage = -1;

            public PetPrankOverlay(PetPrank kind, Rectangle screen, int seed)
            {
                this.kind = kind;
                rng = new Random(seed);
                // The fire only needs the lower part of the screen, which keeps the bitmap small
                area = kind == PetPrank.Fire
                    ? new Rectangle(screen.Left, screen.Bottom - screen.Height * 11 / 20, screen.Width, screen.Height * 11 / 20)
                    : screen;
                k = Math.Max(0.5f, screen.Height / 1080f);

                FormBorderStyle = FormBorderStyle.None;
                ShowInTaskbar = false;
                StartPosition = FormStartPosition.Manual;
                AutoScaleMode = AutoScaleMode.None;
                Text = "Deskmate prank";
                Bounds = area;
                timer.Tick += delegate { Tick(); };

                switch (kind)
                {
                    case PetPrank.Crack: SetupCracks(); break;
                    case PetPrank.Paint: SetupPaint(); break;
                    case PetPrank.Snow: SetupSnow(); break;
                }
            }

            protected override bool ShowWithoutActivation { get { return true; } }

            protected override CreateParams CreateParams
            {
                get
                {
                    var cp = base.CreateParams;
                    // Layered + transparent: every click, scroll and hover passes to the windows underneath
                    cp.ExStyle |= Native.WS_EX_LAYERED | Native.WS_EX_TRANSPARENT | Native.WS_EX_NOACTIVATE |
                                  Native.WS_EX_TOOLWINDOW | Native.WS_EX_TOPMOST;
                    return cp;
                }
            }

            protected override void WndProc(ref Message m)
            {
                if (m.Msg == 0x84) { m.Result = (IntPtr)(-1); return; } // HTTRANSPARENT
                if (m.Msg == Native.WM_MOUSEACTIVATE) { m.Result = (IntPtr)Native.MA_NOACTIVATE; return; }
                base.WndProc(ref m);
            }

            public void Play()
            {
                Build();
                CreateHandle();
                clock.Start();
                Render(0);
                Native.ShowWindow(Handle, Native.SW_SHOWNOACTIVATE);
                Native.SetWindowPos(Handle, Native.HWND_TOPMOST, 0, 0, 0, 0,
                    Native.SWP_NOMOVE | Native.SWP_NOSIZE | Native.SWP_NOACTIVATE);
                timer.Start();
            }

            void Build()
            {
                int w = Math.Max(1, area.Width), h = Math.Max(1, area.Height);
                var bmi = new Native.BITMAPINFOHEADER
                {
                    biSize = System.Runtime.InteropServices.Marshal.SizeOf(typeof(Native.BITMAPINFOHEADER)),
                    biWidth = w,
                    biHeight = -h,
                    biPlanes = 1,
                    biBitCount = 32
                };
                IntPtr screen = Native.GetDC(IntPtr.Zero);
                memDC = Native.CreateCompatibleDC(screen);
                Native.ReleaseDC(IntPtr.Zero, screen);
                dib = Native.CreateDIBSection(memDC, ref bmi, 0, out bits, IntPtr.Zero, 0);
                if (dib == IntPtr.Zero || bits == IntPtr.Zero) throw new InvalidOperationException("no bitmap for the overlay");
                oldBmp = Native.SelectObject(memDC, dib);
                canvas = new Bitmap(w, h, w * 4, PixelFormat.Format32bppPArgb, bits);
                g = Graphics.FromImage(canvas);
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.CompositingMode = CompositingMode.SourceOver;
            }

            void Tick()
            {
                if (IsDisposed) return;
                double t = clock.Elapsed.TotalSeconds;
                if (t >= Duration) { Close(); return; }
                Render(t);
            }

            void Render(double t)
            {
                float dt = (float)Math.Max(0, Math.Min(0.1, t - lastT));
                lastT = t;
                bool redraw = true;
                switch (kind)
                {
                    case PetPrank.Fire: DrawFire(t, dt); break;
                    case PetPrank.Crack: redraw = DrawCracks(t); break;
                    case PetPrank.Paint: DrawPaint(t); break;
                    default: DrawSnow(t, dt); break;
                }
                if (redraw) g.Flush(FlushIntention.Sync);

                double fade = Math.Min(1, t / FadeIn) * Math.Min(1, Math.Max(0, (Duration - t) / FadeOut));
                var dst = new Native.POINT(area.Left, area.Top);
                var src = new Native.POINT(0, 0);
                var size = new Native.SIZE(area.Width, area.Height);
                var blend = new Native.BLENDFUNCTION { BlendOp = 0, BlendFlags = 0, SourceConstantAlpha = (byte)Math.Round(255 * fade), AlphaFormat = 1 };
                Native.UpdateLayeredWindow(Handle, IntPtr.Zero, ref dst, ref size, memDC, ref src, 0, ref blend, Native.ULW_ALPHA);
            }

            float R(float a, float b) { return a + (float)rng.NextDouble() * (b - a); }

            // ---- Fire: flames licking up from the bottom edge, with embers ----

            struct Flame { public float X, Y, Vx, Vy, Size, Life, Max, Phase; public bool Ember; }
            readonly List<Flame> flames = new List<Flame>();
            float spawnDebt;

            void DrawFire(double t, float dt)
            {
                int w = area.Width, h = area.Height;
                // Grows for a second, then the fire runs out of fuel near the end
                float fuel = (float)(Math.Min(1, t / 1.0) * Math.Min(1, Math.Max(0, (Duration - 0.8 - t) / 1.2)));
                spawnDebt += dt * 480f * (w / 1920f) * fuel;
                while (spawnDebt >= 1 && flames.Count < 900)
                {
                    spawnDebt -= 1;
                    bool ember = rng.NextDouble() < 0.12;
                    flames.Add(new Flame
                    {
                        X = R(-20 * k, w + 20 * k),
                        Y = h + R(0, 18 * k),
                        Vx = R(-14, 14) * k,
                        Vy = -(ember ? R(220, 420) : R(110, 240)) * k * (0.6f + 0.4f * fuel),
                        Size = ember ? R(2.5f, 5f) * k : R(36, 80) * k,
                        Max = ember ? R(1.4f, 2.4f) : R(0.7f, 1.35f),
                        Phase = R(0, 6.28f),
                        Ember = ember
                    });
                }
                if (spawnDebt > 1) spawnDebt = 1;

                g.Clear(Color.Transparent);
                // Heat glow along the bottom
                int glowH = Math.Max(2, (int)(h * (0.35f + 0.25f * fuel)));
                var glowRect = new Rectangle(0, h - glowH, w, glowH);
                using (var lg = new LinearGradientBrush(new Rectangle(0, h - glowH - 1, w, glowH + 2),
                    Color.FromArgb(0, 255, 90, 10), Color.FromArgb((int)(170 * fuel + 30), 255, 110, 20), LinearGradientMode.Vertical))
                    g.FillRectangle(lg, glowRect);

                float flick = (float)t * 7f;
                using (var brush = new SolidBrush(Color.White))
                {
                    for (int i = flames.Count - 1; i >= 0; i--)
                    {
                        var f = flames[i];
                        f.Life += dt;
                        if (f.Life >= f.Max || f.Y < -f.Size) { flames.RemoveAt(i); continue; }
                        f.X += (f.Vx + (float)Math.Sin(flick + f.Phase) * 26 * k) * dt;
                        f.Y += f.Vy * dt;
                        flames[i] = f;
                        float u = f.Life / f.Max;
                        if (f.Ember)
                        {
                            brush.Color = Color.FromArgb((int)(240 * (1 - u)), 255, 220, 120);
                            g.FillEllipse(brush, f.X, f.Y, f.Size, f.Size);
                            continue;
                        }
                        float s = f.Size * (1 - 0.65f * u);
                        brush.Color = FlameColor(u);
                        g.FillEllipse(brush, f.X - s / 2, f.Y - s, s, s * 2f);
                    }
                }
            }

            // White-yellow core, through orange and red, into thin smoke
            static Color FlameColor(float u)
            {
                if (u < 0.18f) return Lerp(Color.FromArgb(210, 255, 245, 190), Color.FromArgb(200, 255, 190, 60), u / 0.18f);
                if (u < 0.45f) return Lerp(Color.FromArgb(200, 255, 190, 60), Color.FromArgb(175, 255, 105, 20), (u - 0.18f) / 0.27f);
                if (u < 0.75f) return Lerp(Color.FromArgb(175, 255, 105, 20), Color.FromArgb(120, 200, 40, 15), (u - 0.45f) / 0.3f);
                return Lerp(Color.FromArgb(120, 200, 40, 15), Color.FromArgb(0, 50, 35, 30), (u - 0.75f) / 0.25f);
            }

            static Color Lerp(Color a, Color b, float t)
            {
                t = Math.Max(0, Math.Min(1, t));
                return Color.FromArgb(
                    (int)(a.A + (b.A - a.A) * t), (int)(a.R + (b.R - a.R) * t),
                    (int)(a.G + (b.G - a.G) * t), (int)(a.B + (b.B - a.B) * t));
            }

            // ---- Cracked glass: two hits, drawn once each ----

            sealed class Impact { public PointF At; public List<PointF[]> Rays = new List<PointF[]>(); public List<PointF[]> Rings = new List<PointF[]>(); public double When; }
            readonly List<Impact> impacts = new List<Impact>();

            void SetupCracks()
            {
                for (int n = 0; n < 2; n++)
                {
                    var imp = new Impact
                    {
                        At = new PointF(R(area.Width * 0.2f, area.Width * 0.8f), R(area.Height * 0.2f, area.Height * 0.75f)),
                        When = n * 0.7
                    };
                    int rays = rng.Next(13, 20);
                    float baseAng = R(0, 6.28f);
                    var angles = new float[rays];
                    var lens = new float[rays];
                    for (int i = 0; i < rays; i++)
                    {
                        angles[i] = baseAng + i * 6.2832f / rays + R(-0.18f, 0.18f);
                        lens[i] = R(220, 720) * k * (n == 0 ? 1f : 0.7f);
                        var pts = new List<PointF> { imp.At };
                        float a = angles[i], d = 0;
                        while (d < lens[i])
                        {
                            d += R(22, 60) * k;
                            a += R(-0.22f, 0.22f);
                            pts.Add(new PointF(imp.At.X + (float)Math.Cos(a) * d, imp.At.Y + (float)Math.Sin(a) * d));
                        }
                        imp.Rays.Add(pts.ToArray());
                    }
                    // Jagged rings joining the rays at a few distances
                    float[] radii = { 28 * k, 70 * k, 140 * k, 230 * k };
                    foreach (float r in radii)
                    {
                        var ring = new List<PointF>();
                        for (int i = 0; i < rays; i++)
                        {
                            if (lens[i] < r) { if (ring.Count > 1) imp.Rings.Add(ring.ToArray()); ring = new List<PointF>(); continue; }
                            float rr = r * R(0.85f, 1.15f);
                            ring.Add(new PointF(imp.At.X + (float)Math.Cos(angles[i]) * rr, imp.At.Y + (float)Math.Sin(angles[i]) * rr));
                        }
                        if (ring.Count > 1)
                        {
                            if (ring.Count == rays) ring.Add(ring[0]);
                            imp.Rings.Add(ring.ToArray());
                        }
                    }
                    impacts.Add(imp);
                }
            }

            bool DrawCracks(double t)
            {
                int stage = 0;
                for (int i = 0; i < impacts.Count; i++) if (t >= impacts[i].When) stage = i + 1;
                if (stage == staticStage) return false;
                staticStage = stage;
                g.Clear(Color.Transparent);
                using (var shade = new Pen(Color.FromArgb(110, 0, 0, 0), 3.2f * k) { LineJoin = LineJoin.Round })
                using (var line = new Pen(Color.FromArgb(225, 255, 255, 255), 1.3f * k) { LineJoin = LineJoin.Round })
                using (var shard = new SolidBrush(Color.FromArgb(40, 255, 255, 255)))
                using (var hole = new SolidBrush(Color.FromArgb(90, 20, 24, 30)))
                {
                    for (int n = 0; n < stage; n++)
                    {
                        var imp = impacts[n];
                        float c = 16 * k;
                        g.FillEllipse(hole, imp.At.X - c / 2, imp.At.Y - c / 2, c, c);
                        foreach (var ray in imp.Rays)
                            if (ray.Length >= 3) g.FillPolygon(shard, new[] { ray[0], ray[1], ray[2] });
                        foreach (var ray in imp.Rays) g.DrawLines(shade, ray);
                        foreach (var ring in imp.Rings) g.DrawLines(shade, ring);
                        foreach (var ray in imp.Rays) g.DrawLines(line, ray);
                        foreach (var ring in imp.Rings) g.DrawLines(line, ring);
                    }
                }
                return true;
            }

            // ---- Paint: splats thrown one after another, dripping down ----

            sealed class Splat { public double When; public PointF At; public float Size; public Color Color; public PointF[] Blob; public RectangleF[] Drops; public float[] DripX, DripW, DripLen; }
            readonly List<Splat> splats = new List<Splat>();

            void SetupPaint()
            {
                Color[] palette =
                {
                    Color.FromArgb(235, 255, 70, 140), Color.FromArgb(235, 120, 220, 60), Color.FromArgb(235, 40, 190, 255),
                    Color.FromArgb(235, 160, 80, 255), Color.FromArgb(235, 255, 205, 40), Color.FromArgb(235, 255, 120, 30)
                };
                int count = 7;
                for (int n = 0; n < count; n++)
                {
                    var s = new Splat
                    {
                        When = n * 0.32,
                        At = new PointF(R(area.Width * 0.08f, area.Width * 0.92f), R(area.Height * 0.08f, area.Height * 0.8f)),
                        Size = R(70, 150) * k,
                        Color = palette[rng.Next(palette.Length)]
                    };
                    int pts = 22;
                    s.Blob = new PointF[pts];
                    for (int i = 0; i < pts; i++)
                    {
                        float a = i * 6.2832f / pts;
                        float r = s.Size * (i % 2 == 0 ? R(0.75f, 1.25f) : R(0.55f, 0.9f));
                        if (rng.NextDouble() < 0.2) r *= 1.45f;
                        s.Blob[i] = new PointF((float)Math.Cos(a) * r, (float)Math.Sin(a) * r);
                    }
                    int drops = rng.Next(8, 15);
                    s.Drops = new RectangleF[drops];
                    for (int i = 0; i < drops; i++)
                    {
                        float a = R(0, 6.28f), d = s.Size * R(1.3f, 2.4f), r = s.Size * R(0.05f, 0.16f);
                        s.Drops[i] = new RectangleF((float)Math.Cos(a) * d - r, (float)Math.Sin(a) * d - r, r * 2, r * 2);
                    }
                    int drips = rng.Next(2, 5);
                    s.DripX = new float[drips]; s.DripW = new float[drips]; s.DripLen = new float[drips];
                    for (int i = 0; i < drips; i++)
                    {
                        s.DripX[i] = R(-0.6f, 0.6f) * s.Size;
                        s.DripW[i] = R(0.12f, 0.22f) * s.Size;
                        s.DripLen[i] = R(1.2f, 3.2f) * s.Size;
                    }
                    splats.Add(s);
                }
            }

            void DrawPaint(double t)
            {
                g.Clear(Color.Transparent);
                using (var brush = new SolidBrush(Color.White))
                using (var shine = new SolidBrush(Color.FromArgb(70, 255, 255, 255)))
                {
                    foreach (var s in splats)
                    {
                        double age = t - s.When;
                        if (age < 0) continue;
                        // Pops slightly past full size, then settles
                        float pop = age < 0.12 ? (float)(age / 0.12) * 1.12f : age < 0.22 ? 1.12f - (float)((age - 0.12) / 0.1) * 0.12f : 1f;
                        // A zero scale is a singular matrix GDI+ refuses
                        if (pop < 0.05f) continue;
                        var state = g.Save();
                        g.TranslateTransform(s.At.X, s.At.Y);
                        g.ScaleTransform(pop, pop);
                        brush.Color = s.Color;
                        float grow = (float)Math.Min(1, Math.Max(0, (age - 0.2) / 3.0));
                        grow = 1 - (1 - grow) * (1 - grow);
                        for (int i = 0; i < s.DripX.Length; i++)
                        {
                            float len = s.DripLen[i] * grow, w = s.DripW[i];
                            if (len <= 1) continue;
                            g.FillRectangle(brush, s.DripX[i] - w / 2, 0, w, len);
                            g.FillEllipse(brush, s.DripX[i] - w * 0.65f, len - w * 0.65f, w * 1.3f, w * 1.3f);
                        }
                        g.FillClosedCurve(brush, s.Blob, FillMode.Winding, 0.35f);
                        foreach (var d in s.Drops) g.FillEllipse(brush, d);
                        g.FillEllipse(shine, -s.Size * 0.45f, -s.Size * 0.5f, s.Size * 0.4f, s.Size * 0.28f);
                        g.Restore(state);
                    }
                }
            }

            // ---- Snow: flakes drifting down while frost creeps in from the edges ----

            struct Flake { public float X, Y, Vy, Size, Phase, Sway; }
            Flake[] flakes;

            void SetupSnow()
            {
                int n = (int)Math.Min(700, 320 * (area.Width / 1920f) * (area.Height / 1080f) + 60);
                flakes = new Flake[n];
                for (int i = 0; i < n; i++)
                    flakes[i] = new Flake
                    {
                        X = R(0, area.Width),
                        Y = R(-area.Height * 0.5f, area.Height),
                        Vy = R(70, 190) * k,
                        Size = R(2.5f, 8f) * k,
                        Phase = R(0, 6.28f),
                        Sway = R(10, 40) * k
                    };
            }

            void DrawSnow(double t, float dt)
            {
                int w = area.Width, h = area.Height;
                g.Clear(Color.Transparent);
                float frost = (float)Math.Min(1, t / 2.5);
                int edge = Math.Max(2, (int)(Math.Min(w, h) * (0.06f + 0.1f * frost)));
                int a = (int)(150 * frost);
                FillEdge(new Rectangle(0, 0, w, edge), a, LinearGradientMode.Vertical, false);
                FillEdge(new Rectangle(0, h - edge, w, edge), a, LinearGradientMode.Vertical, true);
                FillEdge(new Rectangle(0, 0, edge, h), a, LinearGradientMode.Horizontal, false);
                FillEdge(new Rectangle(w - edge, 0, edge, h), a, LinearGradientMode.Horizontal, true);

                using (var brush = new SolidBrush(Color.FromArgb(225, 255, 255, 255)))
                {
                    for (int i = 0; i < flakes.Length; i++)
                    {
                        var f = flakes[i];
                        f.Y += f.Vy * dt;
                        if (f.Y > h + 10) { f.Y = -10; f.X = R(0, w); }
                        flakes[i] = f;
                        if (f.Y < -f.Size) continue;
                        float x = f.X + (float)Math.Sin(t * 1.6 + f.Phase) * f.Sway;
                        g.FillEllipse(brush, x, f.Y, f.Size, f.Size);
                    }
                }
            }

            void FillEdge(Rectangle r, int alpha, LinearGradientMode mode, bool towardEnd)
            {
                if (r.Width <= 0 || r.Height <= 0 || alpha <= 0) return;
                Color ice = Color.FromArgb(alpha, 215, 240, 255), clear = Color.FromArgb(0, 215, 240, 255);
                var gr = new Rectangle(r.X - 1, r.Y - 1, r.Width + 2, r.Height + 2);
                using (var lg = new LinearGradientBrush(gr, towardEnd ? clear : ice, towardEnd ? ice : clear, mode))
                    g.FillRectangle(lg, r);
            }

            protected override void OnFormClosed(FormClosedEventArgs e)
            {
                timer.Stop();
                Free();
                base.OnFormClosed(e);
            }

            void Free()
            {
                if (g != null) { g.Dispose(); g = null; }
                if (canvas != null) { canvas.Dispose(); canvas = null; }
                if (memDC != IntPtr.Zero)
                {
                    if (oldBmp != IntPtr.Zero) Native.SelectObject(memDC, oldBmp);
                    Native.DeleteDC(memDC);
                    memDC = IntPtr.Zero;
                }
                if (dib != IntPtr.Zero) { Native.DeleteObject(dib); dib = IntPtr.Zero; }
                bits = IntPtr.Zero;
            }

            protected override void Dispose(bool disposing)
            {
                if (disposing) timer.Dispose();
                Free();
                base.Dispose(disposing);
            }
        }
    }
}
