using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using Microsoft.Win32;
using WinTimer = System.Windows.Forms.Timer;

namespace Deskmate
{
    partial class Island
    {
        // Rendering

        void Render(DateTime now)
        {
            float jelly = Anim.Reduced || PanelTransition ? 0 : 1;
            float W = Math.Max(10, sw.Value - jelly * Clamp(sh.Velocity * 0.012f, -6, 6));
            float H = Math.Max(6, sh.Value - jelly * Clamp(sw.Velocity * 0.005f, -3, 3));
            float R = Math.Max(0, Math.Min(sr.Value, Math.Min(W, H) / 2));
            float pb = Math.Max(0, bubble.Value);
            bool side = OnSide;
            // On a side the arrival dip pushes outward instead
            if (side) W += Math.Max(0, topGap.Value - Gap);
            float half = W / 2 + (pb > 0.001f ? BubbleGap + BubbleR * 2 : 0);
            // Room for corner mascot
            float extra = corner != null && current == View.Panel ? 20 : 0;
            int pxW = Math.Min(canvasW - (canvasW & 1), 2 * (int)Math.Ceiling((Math.Min(half, canvasHalf) + Pad + extra) * scale));
            // Docked: no gap; the arrival dip stretches the island instead
            float gap = side ? Gap : Docked ? 0 : Math.Max(0, topGap.Value);
            if (Docked && !side) H += Math.Max(0, topGap.Value - Gap);
            int pxH = Math.Min(canvasH, (int)Math.Ceiling((gap + Math.Max(H, BubbleR * 2) + Pad + extra) * scale));
            if (FlyersMoving(now) && canvasTall) pxH = canvasH;
            float cx = pxW / scale / 2f;
            var origin = CanvasAt(pxW, W, H);
            frameLeft = origin.X;
            frameTop = origin.Y;
            BeginLive(now, pxW, pxH);
            inRender = true;

            g.ResetTransform();
            // Bicubic scaling of every icon costs frames while the shell springs; full quality returns once settled
            g.InterpolationMode = sw.Settled && sh.Settled ? InterpolationMode.HighQualityBicubic : InterpolationMode.Bilinear;
            if (snapWanted) TakeSnapshot();
            snapPxW = pxW;
            g.SetClip(new Rectangle(0, 0, pxW, pxH));
            g.Clear(Color.Transparent);
            g.ResetClip();
            g.ScaleTransform(scale, scale);
            p.HalfPx = 0.5f / scale;
            p.Alpha = 1f;
            p.T = GlassOn ? Ink.Base.OnGlass() : Ink.Base;
            backdropInSurface = false;

            shell = new RectangleF(cx - W / 2, gap, W, H);
            float rowCy = gap + Math.Min(H, 38f) / 2f;
            float br = BubbleRadius(pb);
            float bx = BubbleX(pb);
            bubbleRect = pb > 0.02f ? new RectangleF(bx - br, rowCy - br, br * 2, br * 2) : RectangleF.Empty;

            double t0 = Prof.T();
            // Full drop shadow for opaque or tall views; subtle elevation shadow on glass pills (H <= 60) for depth over light wallpapers
            if (current != View.Prompter)
            {
                float shadowStr = (GlassOn && H <= 60) ? (Ink.Light ? 0.45f : 0.35f) : 1f;
                // Target radius while morphing: one bake
                // A clickable Mini should leave nearby app pixels available to the mouse.
                if (current != View.Mini || !MiniClickEnabled)
                    DrawShadow(shell, sr.Settled ? R : Math.Min(sr.Target, Math.Min(W, H) / 2), shadowStr);
                if (!bubbleRect.IsEmpty) DrawShadow(bubbleRect, br, (GlassOn && H <= 60) ? 0.3f : 0.7f);
            }
            DrawGlow(now, shell, R);
            Prof.Add("shadow", t0);

            bool gpuBridge = false;
            ClearHits();
            using (var path = ShellPath(shell, R))
            {
                if (liveRecord) KeepShell(path, R);
                if (!bubbleRect.IsEmpty)
                {
                    var sb = g.Save();
                    g.SetClip(path, CombineMode.Exclude);
                    DrawBubbleShape(rowCy, bx, br, pxW);
                    gpuBridge = bridgeOn;
                    g.Restore(sb);
                }
                if (flip < 0.999f)
                {
                    t0 = Prof.T();
                    if (current == View.Prompter) FillPrompterBg(path);
                    else FillSurface(path, shell, Ink.Base, current == View.Panel ? 0.55f : 0.42f, pxW);
                    Prof.Add("surface", t0);
                    t0 = Prof.T();
                    var st = g.Save();
                    g.SetClip(path, CombineMode.Intersect);
                    liveInShell = true;
                    DrawContent(now, GlassOn ? Ink.Base.OnGlass() : Ink.Base, flip < 0.5f);
                    DrawEdgeLight(now, path, shell);
                    liveInShell = false;
                    DrawWater(now);
                    g.Restore(st);
                    Prof.Add("content", t0);
                }
                if (flip > 0.001f)
                {
                    // Inversion spreads from where the island hangs
                    int at = PlaceNow;
                    float ox = at == PlaceLeft ? shell.X : at == PlaceRight ? shell.Right : shell.X + W / 2, oy = side ? shell.Y + H / 2 : shell.Y;
                    float rad = flip * (float)(side ? Math.Sqrt(W * W + H * H / 4) : Math.Sqrt(W * W / 4 + H * H)) + 1;
                    var st = g.Save();
                    g.SetClip(path, CombineMode.Intersect);
                    using (var circle = new GraphicsPath())
                    {
                        circle.AddEllipse(ox - rad, oy - rad, rad * 2, rad * 2);
                        g.SetClip(circle, CombineMode.Intersect);
                    }
                    FillSurface(path, shell, Ink.Flip, 0.8f, pxW);
                    DrawContent(now, GlassOn ? Ink.Flip.OnGlass() : Ink.Flip, flip >= 0.5f);
                    g.Restore(st);
                }
            }
            p.T = GlassOn ? Ink.Base.OnGlass() : Ink.Base;
            p.Alpha = 1f;
            if (!bubbleRect.IsEmpty) DrawBubbleContent(now, rowCy, bx, br, Ease.Clamp01((pb - 0.5f) / 0.5f));
            DrawCorner(now);
            DrawFlyers(now, pxW);
            t0 = Prof.T();
            FinishLive(now);
            Prof.Add("live", t0);
            ClearOffMonitor(frameLeft, frameTop, pxW, pxH);

            t0 = Prof.T();
            g.Flush(FlushIntention.Sync);
            var dst = new Native.POINT(frameLeft, frameTop);
            var size = new Native.SIZE(pxW, pxH);
            var src = new Native.POINT(0, 0);
            var blend = new Native.BLENDFUNCTION { SourceConstantAlpha = (byte)(255 * Ease.Clamp01(opacity.Value)), AlphaFormat = 1 };
            if (Native.UpdateLayeredWindow(Handle, IntPtr.Zero, ref dst, ref size, memDC, ref src, 0, ref blend, Native.ULW_ALPHA))
                CountMotionPresent();
            SyncIslandBackdrop(frameLeft, frameTop, pxW, pxH, R, gpuBridge);
            lastPresentTick = Environment.TickCount;
            presented.Set();
            Prof.Add("present", t0);
            dumpAt = new Rectangle(dst.X, dst.Y, pxW, pxH);
            lastShellPx = Rectangle.Round(new RectangleF(shell.X * scale, shell.Y * scale, shell.Width * scale, shell.Height * scale));
            lastRadius = R;
            lastLeft = dst.X;
            lastTop = dst.Y;
        }

        // Snapshot morph: last shell, half size
        Bitmap snapshot;
        Rectangle snapScreen, lastShellPx;
        // Old shell radius; inset hides its rim
        float lastRadius, snapRadius;
        const float SnapInset = 3f;
        int lastLeft, snapPxW;
        DateTime snapUtc;
        bool snapWanted;
        const double SnapSeconds = 0.14;
        // Snapshot DIB: BitBlt in, AlphaBlend out
        IntPtr snapDC, snapDib, snapOld;
        int snapW, snapH;
        [DllImport("gdi32.dll")] static extern bool BitBlt(IntPtr dst, int x, int y, int w, int h, IntPtr src, int sx, int sy, uint rop);
        [DllImport("gdi32.dll")] static extern int SelectClipRgn(IntPtr hdc, IntPtr rgn);
        [DllImport("msimg32.dll")] static extern bool AlphaBlend(IntPtr dst, int x, int y, int w, int h, IntPtr src, int sx, int sy, int sw, int sh, int blend);

        void FreeSnapshot()
        {
            if (snapshot != null) { snapshot.Dispose(); snapshot = null; }
            if (snapDC != IntPtr.Zero) { Native.SelectObject(snapDC, snapOld); Native.DeleteObject(snapDib); Native.DeleteDC(snapDC); snapDC = IntPtr.Zero; }
        }

        void TakeSnapshot()
        {
            snapWanted = false;
            var r = Rectangle.Intersect(lastShellPx, new Rectangle(0, 0, canvasW, canvasH));
            if (!windowShown || opacity.Value < 0.5f || r.Width < 8 || r.Height < 8) return;
            int w = r.Width, h = r.Height;
            if (snapDC == IntPtr.Zero || snapW != w || snapH != h)
            {
                FreeSnapshot();
                var bmi = new Native.BITMAPINFOHEADER { biSize = Marshal.SizeOf(typeof(Native.BITMAPINFOHEADER)), biWidth = w, biHeight = -h, biPlanes = 1, biBitCount = 32 };
                IntPtr bits;
                snapDC = Native.CreateCompatibleDC(memDC);
                snapDib = Native.CreateDIBSection(memDC, ref bmi, 0, out bits, IntPtr.Zero, 0);
                snapOld = Native.SelectObject(snapDC, snapDib);
                snapshot = new Bitmap(w, h, w * 4, PixelFormat.Format32bppPArgb, bits);
                snapW = w;
                snapH = h;
            }
            // Plain memory copy, no resampling
            g.Flush(FlushIntention.Sync);
            BitBlt(snapDC, 0, 0, w, h, memDC, r.X, r.Y, 0x00CC0020);
            snapScreen = new Rectangle(lastLeft + r.X, lastTop + r.Y, r.Width, r.Height);
            snapRadius = lastRadius;
            snapUtc = DateTime.UtcNow;
        }

        // Old view shrinks toward 0.92, fading
        void DrawSnapshot(DateTime now)
        {
            if (snapshot == null) return;
            bool kin = Kin(previous, current);
            double secs = (now - snapUtc).TotalSeconds;
            if (secs >= (kin ? KinSeconds : SnapSeconds)) { FreeSnapshot(); return; }
            float k = Ease.OutCubic((float)(secs / SnapSeconds)), sc = Anim.Reduced || kin ? 1 : 1 - 0.08f * Springs.Progress((float)secs, 0.24f, 1f);
            float w = snapScreen.Width * sc, h = snapScreen.Height * sc;
            float cx = snapScreen.X - frameLeft + snapScreen.Width / 2f;
            var dst = new RectangleF(cx - w / 2, snapScreen.Y - frameTop, w, h);
            // GDI AlphaBlend: premultiplied, constant fade
            int sca = (int)(255 * (1 - k) * 0.95f);
            if (kin) { SnapKin((int)Math.Round(dst.Y), sca); return; }
            if (sca <= 0) return;
            var st = g.Save();
            // Old rim and edge light stay behind
            var body = new RectangleF(dst.X / scale + SnapInset, dst.Y / scale + SnapInset, dst.Width / scale - 2 * SnapInset, dst.Height / scale - 2 * SnapInset);
            if (body.Width > 8 && body.Height > 8)
                using (var keep = Ink.Round(body, Math.Max(0, snapRadius * sc - SnapInset))) g.SetClip(keep, CombineMode.Intersect);
            SnapBlit((int)Math.Round(dst.X), (int)Math.Round(dst.Y), Math.Max(1, (int)Math.Round(dst.Width)), Math.Max(1, (int)Math.Round(dst.Height)), 0, snapW, sca);
            g.Restore(st);
        }

        // New view is fully in by then
        const double KinSeconds = 0.27;

        // Cover and bars hold; old words fade
        void SnapKin(int y, int sca)
        {
            int cap = Math.Min(snapW / 2, (int)Math.Round(38 * scale)), mid = snapW - 2 * cap;
            int l = (int)Math.Round(shell.X * scale), r = (int)Math.Round(shell.Right * scale);
            var st = g.Save();
            var body = RectangleF.Inflate(shell, -SnapInset, -SnapInset);
            using (var keep = Ink.Round(body, body.Height / 2)) g.SetClip(keep, CombineMode.Intersect);
            SnapBlit(l, y, cap, snapH, 0, cap, 255);
            SnapBlit(r - cap, y, cap, snapH, snapW - cap, cap, 255);
            if (sca > 0 && mid > 0 && r - l > 2 * cap)
            {
                g.SetClip(new RectangleF((l + cap) / scale, shell.Y, (r - l - 2 * cap) / scale, shell.Height), CombineMode.Intersect);
                SnapBlit((l + r - mid) / 2, y, mid, snapH, cap, mid, sca);
            }
            g.Restore(st);
        }

        // Snapshot columns through the GDI+ clip
        void SnapBlit(int dx, int dy, int dw, int dh, int sx, int sw2, int sca)
        {
            using (var clip = g.Clip)
            {
                IntPtr rgn = clip.IsInfinite(g) ? IntPtr.Zero : clip.GetHrgn(g);
                g.Flush(FlushIntention.Sync);
                SelectClipRgn(memDC, rgn);
                AlphaBlend(memDC, dx, dy, dw, dh, snapDC, sx, 0, sw2, snapH, sca << 16 | 1 << 24);
                SelectClipRgn(memDC, IntPtr.Zero);
                if (rgn != IntPtr.Zero) Native.DeleteObject(rgn);
            }
        }

        static float Clamp(float v, float lo, float hi) { return v < lo ? lo : v > hi ? hi : v; }

        static readonly int[] ShadowAlpha = { 10, 9, 7, 5, 3, 16, 10, 6 };
        const float ShadowMargin = 18f;
        readonly Dictionary<string, Bitmap> shadows = new Dictionary<string, Bitmap>();
        Bitmap shadowCache;

        const float ShadowCore = 16f;

        // Elevation belongs OUTSIDE the shell. The shadow layers are drawn before the glass, and the glass is a
        // thin veil (alpha ~26/255), so anything painted under it shows straight through. Because ShadowDrop pushes
        // each ring downward, the rings pile up hardest inside the bottom corners and left them reading as dark
        // blotches against a brighter top. Excluding the shell interior keeps the halo where it is wanted without
        // touching the glass fill or the shadow's character.
        static void ClipOutsideShell(Graphics g, RectangleF r, float radius)
        {
            using (var hole = Ink.Round(r, radius)) g.SetClip(hole, CombineMode.Exclude);
        }

        // 9-slice elevation shadow
        void DrawShadow(RectangleF r, float radius, float strength)
        {
            float R = (float)Math.Round(radius * 2) / 2, M = ShadowMargin;
            // Continuous corners extend past R
            float E = Ink.CornerExtent(R);
            if (r.Width < 2 * E + ShadowCore || r.Height < 2 * E + ShadowCore)
            {
                var small = g.Save();
                ClipOutsideShell(g, r, radius);
                ShadowLayers(p, r, radius, strength);
                g.Restore(small);
                return;
            }
            // Small cards: whole bitmap, stretched mid-morph
            if (r.Height <= 90 && strength >= 1f)
            {
                if (sw.Settled && sh.Settled && sr.Settled) WholeShadow(r, R, strength);
                else if (wholeShadow != null) StretchShadow(r);
                return;
            }
            shadowCache = SliceShadow(R, strength);
            // Corners whole, edges as thin ring
            int n = shadowCache.Width, k = (int)Math.Ceiling((M + E + 7) * scale), m = (int)Math.Ceiling(M * scale) + 1, mid = n / 2 - 1;
            int x0 = (int)Math.Round((r.X - M) * scale), y0 = (int)Math.Round((r.Y - M) * scale);
            int x3 = (int)Math.Round((r.Right + M) * scale), y3 = (int)Math.Round((r.Bottom + M) * scale);
            int ew = x3 - x0 - 2 * k, eh = y3 - y0 - 2 * k;
            var st = g.Save();
            g.ResetTransform();
            g.InterpolationMode = InterpolationMode.NearestNeighbor;
            g.PixelOffsetMode = PixelOffsetMode.Half;
            // Keep the rings outside the shell; the tile itself is reused, so exclude here rather than in the cache
            ClipOutsideShell(g, new RectangleF(r.X * scale, r.Y * scale, r.Width * scale, r.Height * scale), R * scale);
            Slice(0, 0, k, k, x0, y0, k, k);
            Slice(n - k, 0, k, k, x3 - k, y0, k, k);
            Slice(0, n - k, k, k, x0, y3 - k, k, k);
            Slice(n - k, n - k, k, k, x3 - k, y3 - k, k, k);
            if (ew > 0)
            {
                Slice(mid, 0, 2, m, x0 + k, y0, ew, m);
                Slice(mid, n - m, 2, m, x0 + k, y3 - m, ew, m);
            }
            if (eh > 0)
            {
                Slice(0, mid, m, 2, x0, y0 + k, m, eh);
                Slice(n - m, mid, m, 2, x3 - m, y0 + k, m, eh);
            }
            g.Restore(st);
        }

        Bitmap SliceShadow(float R, float strength)
        {
            float M = ShadowMargin;
            string key = R + "|" + strength + "|" + scale;
            Bitmap img;
            if (shadows.TryGetValue(key, out img)) return img;
            // Room for every view's radius; small bitmaps (~80 KB each)
            if (shadows.Count > 16) { foreach (var b in shadows.Values) b.Dispose(); shadows.Clear(); }
            // Square wide enough for full corner smoothing
            float E = Ink.CornerExtent(R), inner = 2 * (E + 7) + ShadowCore;
            int side = (int)Math.Ceiling((2 * M + inner) * scale);
            img = new Bitmap(side, side, PixelFormat.Format32bppPArgb);
            using (var sg = Graphics.FromImage(img))
            {
                sg.SmoothingMode = SmoothingMode.AntiAlias;
                sg.ScaleTransform(scale, scale);
                using (var sp = new Painter { G = sg }) ShadowLayers(sp, new RectangleF(M, M, inner, inner), R, strength);
            }
            shadows[key] = img;
            return img;
        }

        Bitmap wholeShadow;
        string wholeKey;

        void WholeShadow(RectangleF r, float R, float strength)
        {
            float M = ShadowMargin;
            string key = r.Width.ToString("0") + "x" + r.Height.ToString("0") + "|" + R + "|" + strength + "|" + scale;
            if (key != wholeKey)
            {
                if (wholeShadow != null) wholeShadow.Dispose();
                wholeShadow = new Bitmap((int)Math.Ceiling((r.Width + 2 * M) * scale), (int)Math.Ceiling((r.Height + 2 * M) * scale), PixelFormat.Format32bppPArgb);
                using (var sg = Graphics.FromImage(wholeShadow))
                {
                    sg.SmoothingMode = SmoothingMode.AntiAlias;
                    sg.ScaleTransform(scale, scale);
                    using (var sp = new Painter { G = sg }) ShadowLayers(sp, new RectangleF(M, M, (float)Math.Round(r.Width), (float)Math.Round(r.Height)), R, strength);
                    // Punch the shell back out: the baked rings would otherwise sit under the translucent glass
                    sg.CompositingMode = CompositingMode.SourceCopy;
                    using (var brush = new SolidBrush(Color.Transparent))
                    using (var hole = Ink.Round(new RectangleF(M, M, (float)Math.Round(r.Width), (float)Math.Round(r.Height)), R))
                        sg.FillPath(brush, hole);
                    sg.CompositingMode = CompositingMode.SourceOver;
                }
                wholeKey = key;
            }
            var st = g.Save();
            g.ResetTransform();
            g.InterpolationMode = InterpolationMode.NearestNeighbor;
            g.PixelOffsetMode = PixelOffsetMode.Half;
            g.DrawImage(wholeShadow, new Rectangle((int)Math.Round((r.X - M) * scale), (int)Math.Round((r.Y - M) * scale), wholeShadow.Width, wholeShadow.Height),
                0, 0, wholeShadow.Width, wholeShadow.Height, GraphicsUnit.Pixel);
            g.Restore(st);
        }

        void StretchShadow(RectangleF r)
        {
            float M = ShadowMargin;
            var st = g.Save();
            g.ResetTransform();
            g.InterpolationMode = InterpolationMode.Bilinear;
            g.PixelOffsetMode = PixelOffsetMode.Half;
            g.DrawImage(wholeShadow, new Rectangle((int)Math.Round((r.X - M) * scale), (int)Math.Round((r.Y - M) * scale),
                (int)Math.Round((r.Width + 2 * M) * scale), (int)Math.Round((r.Height + 2 * M) * scale)), 0, 0, wholeShadow.Width, wholeShadow.Height, GraphicsUnit.Pixel);
            g.Restore(st);
        }

        void Slice(int sx, int sy, int sw2, int sh2, int dx, int dy, int dw, int dh)
        {
            g.DrawImage(shadowCache, new Rectangle(dx, dy, dw, dh), sx, sy, sw2, sh2, GraphicsUnit.Pixel);
        }

        // Ambient wide and soft; key tight, lower
        static readonly float[] ShadowGrow = { 3f, 6f, 9f, 11.5f, 13.5f, 0.5f, 1.5f, 2.5f }, ShadowDrop = { 2f, 3f, 4f, 5f, 4.5f, 1.5f, 2f, 2.5f };

        static void ShadowLayers(Painter sp, RectangleF r, float radius, float strength)
        {
            for (int i = 0; i < ShadowAlpha.Length; i++)
            {
                float grow = ShadowGrow[i];
                var rr = new RectangleF(r.X - grow, r.Y - grow + ShadowDrop[i], r.Width + grow * 2, r.Height + grow * 2);
                sp.FillRound(rr, radius + grow, Color.FromArgb((int)(ShadowAlpha[i] * strength), 0, 0, 0));
            }
        }

        const int GlowLayers = 16;

        // Halo while an approval waits
        void DrawGlow(DateTime now, RectangleF r, float radius)
        {
            if (flip < 0.05f) return;
            float breath = Anim.Reduced ? 0.75f : 0.55f + 0.45f * (float)Math.Sin(clock.Elapsed.TotalSeconds * Math.PI * 1.3);
            Color c = Ink.Flip.Bg;
            // Thin layers: falloff without bands
            for (int i = 0; i < GlowLayers; i++)
            {
                float grow = 1f + i * 0.9f, fall = 1 - i / (float)GlowLayers;
                var rr = new RectangleF(r.X - grow, r.Y - grow, r.Width + grow * 2, r.Height + grow * 2);
                p.FillRound(rr, radius + grow, Color.FromArgb((int)(flip * breath * 36 * fall * (float)Math.Sqrt(fall)), c.R, c.G, c.B));
            }
        }

        // This frame's bridge to the bubble
        readonly GraphicsPath bridge = new GraphicsPath(FillMode.Winding);
        bool bridgeOn;
        // Neck snaps once the gap passes this
        const float BridgeSnap = 7f;

        float CapR { get { return Math.Min(shell.Height / 2, 19f); } }

        // Born inside the end cap
        float BubbleRadius(float pb) { return Anim.Reduced ? BubbleR : BubbleR * (0.5f + 0.5f * Ease.Clamp01(pb)); }

        float BubbleX(float pb)
        {
            float rest = shell.Left - BubbleGap - BubbleR;
            return Anim.Reduced ? rest : Ease.Lerp(shell.Left + CapR, rest, pb);
        }

        // Bubble joined by a thinning neck
        void DrawBubbleShape(float cy, float bx, float br, int pxW)
        {
            bridge.Reset();
            bridgeOn = false;
            float capR = CapR, capX = shell.Left + capR;
            float gapNow = shell.Left - (bx + br);
            if (!Anim.Reduced && gapNow < BridgeSnap)
            {
                float v = 0.5f * (float)Math.Pow(Ease.Clamp01(1 - gapNow / BridgeSnap), 0.6);
                bridgeOn = v > 0.02f && Metaball(bridge, capX, cy, capR, bx, cy, br, v);
            }
            if (!bridgeOn) { bridge.Reset(); bridge.AddEllipse(bx - br, cy - br, br * 2, br * 2); }
            float near = bridgeOn ? capX : bx + br, outer = bx - br, half = bridgeOn ? Math.Max(br, capR) : br;
            var bounds = new RectangleF(Math.Min(near, outer), cy - half, Math.Abs(outer - near), half * 2);
            // Reduced motion: a plain fade in place
            if (Anim.Reduced && bubble.Value < 0.98f)
            {
                float a = Ease.Clamp01(bubble.Value);
                g.FillPath(p.B(Ink.A(Ink.Base.Bg, 0.82f * a)), bridge);
                p.Alpha = a;
                DrawRim(bridge, bounds, !Ink.Base.IsDark, !GlassOn);
                p.Alpha = 1f;
                return;
            }
            FillSurface(bridge, bounds, Ink.Base, 0.42f, pxW);
        }

        static PointF On(double x, double y, double a, double r) { return new PointF((float)(x + r * Math.Cos(a)), (float)(y + r * Math.Sin(a))); }

        static double Unit(double v) { return v < -1 ? -1 : v > 1 ? 1 : v; }

        // Metaball: tangent beziers, far side arc
        static bool Metaball(GraphicsPath path, float x1, float y1, float r1, float x2, float y2, float r2, float v)
        {
            double dx = x2 - x1, dy = y2 - y1, d = Math.Sqrt(dx * dx + dy * dy);
            if (d <= Math.Abs(r1 - r2) + 0.5) return false;
            double u1 = 0, u2 = 0;
            if (d < r1 + r2)
            {
                u1 = Math.Acos(Unit((r1 * r1 + d * d - r2 * r2) / (2 * r1 * d)));
                u2 = Math.Acos(Unit((r2 * r2 + d * d - r1 * r1) / (2 * r2 * d)));
            }
            double towards = Math.Atan2(dy, dx), spread = Math.Acos(Unit((r1 - r2) / d));
            double a1 = towards + u1 + (spread - u1) * v, a2 = towards - u1 - (spread - u1) * v;
            double a3 = towards + Math.PI - u2 - (Math.PI - u2 - spread) * v, a4 = towards - Math.PI + u2 + (Math.PI - u2 - spread) * v;
            PointF p1 = On(x1, y1, a1, r1), p2 = On(x1, y1, a2, r1), p3 = On(x2, y2, a3, r2), p4 = On(x2, y2, a4, r2);
            double gx = p3.X - p1.X, gy = p3.Y - p1.Y, total = r1 + r2;
            double k = Math.Min(v * 2.4, Math.Sqrt(gx * gx + gy * gy) / total) * Math.Min(1, d * 2 / total);
            PointF h1 = On(p1.X, p1.Y, a1 - Math.PI / 2, r1 * k), h2 = On(p2.X, p2.Y, a2 + Math.PI / 2, r1 * k);
            PointF h3 = On(p3.X, p3.Y, a3 + Math.PI / 2, r2 * k), h4 = On(p4.X, p4.Y, a4 - Math.PI / 2, r2 * k);
            path.StartFigure();
            path.AddBezier(p1, h1, h3, p3);
            // p3 round the far side to p4
            float start = (float)(a3 * 180 / Math.PI), sweep = (float)((a4 - a3) * 180 / Math.PI);
            while (sweep > 0) sweep -= 360;
            while (sweep <= -360) sweep += 360;
            path.AddArc(x2 - r2, y2 - r2, r2 * 2, r2 * 2, start, sweep);
            path.AddBezier(p4, h4, h2, p2);
            path.CloseFigure();
            return true;
        }

        void DrawContent(DateTime now, Tone tone, bool registerHits)
        {
            if (GlassOn && tone != null) tone = tone.OnGlass();
            p.T = tone;
            // Clear has almost no veil, so every surface that can sit over a bright
            // window gets a contrasting shadow. Frost keeps its shadow only where
            // the ink itself is light; Liquid stays glossy and shadow-free.
            p.Shadow = GlassOn && (GlassRecipe.NeedsShadow(Kind) ||
                (Kind != GlassKind.Liquid && tone.Bg.R < 128 && current != View.Panel));
            TrackPress();
            float t = (float)(now - switchedUtc).TotalSeconds;
            if (tone == Ink.Base || (GlassOn && tone != null && tone.Bg == Ink.Base.Bg)) DrawSnapshot(now);
            float inA = PanelTransition ? (Anim.Reduced ? 1 : Ease.OutCubic(t / 0.22f)) : Ease.OutCubic((t - 0.05f) / 0.22f);
            if (current == View.Panel && !Anim.Reduced)
            {
                // Reveal a full-width layout through the shell as it grows.
                float widthRatio = shell.Width / sw.Target;
                inA = Math.Min(inA, Ease.OutCubic((widthRatio - 0.72f) / 0.24f));
                registerHits = registerHits && shell.Width >= sw.Target - 2;
            }
            float grow = PanelTransition || Anim.Reduced || Kin(previous, current) ? 0 : 1 - Springs.Progress(t - 0.05f, 0.34f, 0.86f);
            DrawViewAt(current, inA, 1 + 0.04f * grow, registerHits && inA > 0.8f && grow < 0.15f, now);
            if (registerHits) DrawFocusRing();
        }

        // Same frame, only the words differ
        static bool Kin(View a, View b) { return (a == View.Media && b == View.Lyric) || (a == View.Lyric && b == View.Media); }

        void DrawViewAt(View v, float alpha, float zoom, bool registerHits, DateTime now)
        {
            if (alpha <= 0.01f) return;
            var st = g.Save();
            float w = shell.Width, h = shell.Height;
            if (v == View.Panel) w = sw.Target;
            float x = shell.X + (shell.Width - w) / 2;
            g.TranslateTransform(x, shell.Y);
            // Settles from 1.04 about the centre
            if (Math.Abs(zoom - 1) > 0.0005f)
            {
                g.TranslateTransform(w / 2, h / 2);
                g.ScaleTransform(zoom, zoom);
                g.TranslateTransform(-w / 2, -h / 2);
            }
            p.Alpha = alpha;
            originX = x;
            originY = shell.Y;
            hitsOn = registerHits;
            try { DrawView(v, w, h, now); }
            finally
            {
                g.Restore(st);
                p.Alpha = 1f;
                hitsOn = false;
            }
        }
    }
}
