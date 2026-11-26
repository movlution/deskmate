using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;

namespace Deskmate
{
    partial class Island
    {
        sealed partial class DockWindow
        {
            readonly List<KeyValuePair<RectangleF, string>> hits = new List<KeyValuePair<RectangleF, string>>();
            readonly Dictionary<string, Action> acts = new Dictionary<string, Action>(), menus = new Dictionary<string, Action>();
            readonly Dictionary<string, Bitmap> restIcons = new Dictionary<string, Bitmap>(StringComparer.OrdinalIgnoreCase);
            int restPx;
            readonly Spring labelShow = new Spring(0, 1, 1, 0.002f);
            string labelText = "";
            readonly CompGlass.PillSpec[] gpuBarPills = new CompGlass.PillSpec[1];
            readonly PointF[] deckPip = new PointF[4];
            int gpuBarCount;
            bool gpuBarText;

            static RectangleF Inset(RectangleF r, float d) { return new RectangleF(r.X + d, r.Y + d, r.Width - d * 2, r.Height - d * 2); }
            static RectangleF Inset(RectangleF r, float dx, float dy) { return new RectangleF(r.X + dx, r.Y + dy, r.Width - dx * 2, r.Height - dy * 2); }

            void Render()
            {
                if (!shown || closing) return;
                Place();
                if (g == null) return;
                drawnSig = Signature();
                hits.Clear();
                acts.Clear();
                menus.Clear();
                gpuBarCount = 0;
                gpuBarText = false;
                p.T = Glass ? Ink.Base.OnGlass() : Ink.Base;
                p.Alpha = 1;
                p.HalfPx = 0.5f / scale;
                g.ResetTransform();
                g.SetClip(new Rectangle(0, 0, cw, ch));
                g.Clear(Color.Transparent);
                g.ResetClip();
                g.ScaleTransform(scale, scale);
                Compose();
                bool atEdge = reveal.Value < 0.02f && reveal.Target == 0;
                if (atEdge) DrawSensor();
                else
                {
                    Hit(centerPill, "pill", null, null);
                    // Text on the bar needs the guarded glass; icons alone get the vivid one
                    bool text = !leftPill.IsEmpty || !rightPill.IsEmpty;
                    if (Full) DrawBar(new RectangleF(-1, barTop, cw / scale + 2, BarH + Edge + 2), 0, true);
                    else DrawBar(centerPill, Radius, text);
                    Dividers();
                    DrawSpan(leftPill, true);
                    DrawSpan(rightPill, false);
                    DrawCenter();
                    DrawTop();
                }
                // Keep the transparent strip below a floating bar in the hover target.
                // Otherwise the bar rises, the pointer is no longer over an opaque
                // pixel, and auto-hide pulls it down while the pointer has not moved.
                if (!atEdge && HideMode != "never") DrawEdgeHit();
                Present();
            }

            void Present()
            {
                g.Flush(FlushIntention.Sync);
                var at = WindowOrigin();
                var dst = new Native.POINT(at.X, at.Y);
                var size = new Native.SIZE(cw, ch);
                var src = new Native.POINT(0, 0);
                var blend = new Native.BLENDFUNCTION { SourceConstantAlpha = 255, AlphaFormat = 1 };
                Native.UpdateLayeredWindow(Handle, IntPtr.Zero, ref dst, ref size, memDC, ref src, 0, ref blend, Native.ULW_ALPHA);
                Bounds = new Rectangle(at.X, at.Y, cw, ch);
                SyncDockBackdrop(at.X, at.Y);
                if (isle != null) isle.PetFollow(this);
                if (dumpDir != null) DumpDock();
            }

            void SyncDockBackdrop(int winX, int winY)
            {
                if (!GpuGlass || !shown || closing || gpuBarCount == 0)
                {
                    if (dockBackdrop != null) dockBackdrop.HideBackdrop();
                    return;
                }
                if (dockBackdrop == null) dockBackdrop = new CompGlass.CompBackdrop();
                GlassKind kind = isle != null ? isle.Kind : GlassKind.Frost;
                // bar: true asks for the denser tint. The bar spans whatever window is behind it, so it needs to
                // hide background detail that the island can safely let through over wallpaper.
                var spec = CompGlass.Spec(kind, gpuBarText, scale, 1f, true);
                dockBackdrop.Sync(Handle, new Rectangle(winX, winY, cw, ch), true, spec, gpuBarPills, gpuBarCount, null, null, scale, "", clipToPill: true);
            }

            // DESKMATE_DUMP: the dock skips screenshots too
            void DumpDock()
            {
                try
                {
                    Directory.CreateDirectory(dumpDir);
                    using (var copy = canvas.Clone(new Rectangle(0, 0, cw, ch), PixelFormat.Format32bppArgb)) copy.Save(Path.Combine(dumpDir, "dock.png"), ImageFormat.Png);
                }
                catch (Exception ex) { Log.Write("dock dump: " + ex.Message); }
            }

            // Hidden: a 2 px edge the pointer can still find
            void DrawSensor()
            {
                float h = 2 / scale;
                g.FillRectangle(p.B(Color.FromArgb(1, 0, 0, 0)), 0, ch / scale - h, cw / scale, h);
                Hit(new RectangleF(0, ch / scale - h, cw / scale, h), "sensor", null, null);
            }

            // Shown: the gap under the bar points at the desktop, but it is still
            // dock. A 1-alpha strip keeps it hit-testable without being visible.
            void DrawEdgeHit()
            {
                float h = EdgeHitH;
                g.FillRectangle(p.B(Color.FromArgb(1, 0, 0, 0)), 0, ch / scale - h, cw / scale, h);
                Hit(new RectangleF(0, ch / scale - h, cw / scale, h), "sensor", null, null);
            }

            void DrawBar(RectangleF r, float radius, bool text)
            {
                if (r.IsEmpty) return;
                using (var path = radius > 0 ? Ink.Round(r, radius) : Box(r))
                {
                    var st = g.Save();
                    g.SetClip(path, CombineMode.Intersect);
                    bool gpu = GpuGlass;
                    bool glass = !gpu && plate != null && Glass;
                    if (gpu)
                    {
                        gpuBarPills[0] = new CompGlass.PillSpec(r.X * scale, r.Y * scale, r.Width * scale, r.Height * scale, radius * scale);
                        gpuBarCount = 1;
                        gpuBarText |= text;
                    }
                    GlassKind kind = isle != null ? isle.Kind : GlassKind.Frost;
                    var img = glass ? (text ? plate.Text : plate.Image) : null;
                    // Wallpaper tint is a flat colour, not a live scene: magnifying
                    // it only adds seams near the rim. Keep the tint smooth.
                    bool tinted = glass && isle != null && isle.WallGlassOn;
                    if (glass) Backdrop(img, r, 1f);
                    // Only Liquid bends the backdrop at its rim; Clear stays a flat
                    // pane and Frost stays matte.
                    if (glass && !tinted && kind == GlassKind.Liquid && radius > 0) Lens(img, r, radius, kind);
                    // Solid until glass arrives; over glass only a light veil (alpha >= 10 keeps hit-testing active)
                    float veilA = (gpu || glass) ? GlassRecipe.Overlay(kind, false, true) : 0.9f;
                    g.FillPath(p.B(Ink.A(Ink.Veil, veilA)), path);
                    Sheen(r, kind);
                    g.Restore(st);
                    // The tinted dock has a feathered edge; a crisp rim would draw
                    // a second, harder outline over it.
                    if (!tinted) RimLight(r, path, kind);
                    Rim(r, radius);
                }
            }

            // The plate under r, scaled about r's centre (1 = plain)
            void Backdrop(Bitmap img, RectangleF r, float s)
            {
                var o = WindowOrigin();
                var inner = g.Save();
                g.ResetTransform();
                g.InterpolationMode = s == 1f ? InterpolationMode.NearestNeighbor : InterpolationMode.Bilinear;
                g.PixelOffsetMode = PixelOffsetMode.Half;
                var dst = Rectangle.FromLTRB((int)Math.Floor(r.Left * scale), (int)Math.Floor(r.Top * scale), (int)Math.Ceiling(r.Right * scale), (int)Math.Ceiling(r.Bottom * scale));
                float cx = (r.X + r.Width / 2) * scale + o.X, cy = (r.Y + r.Height / 2) * scale + o.Y;
                var src = new RectangleF((dst.X + o.X - cx) / s + cx - plate.Rect.X, (dst.Y + o.Y - cy) / s + cy - plate.Rect.Y, dst.Width / s, dst.Height / s);
                dst.Offset(-offX, -offY);
                g.DrawImage(img, dst, src.X, src.Y, src.Width, src.Height, GraphicsUnit.Pixel);
                g.Restore(inner);
            }

            // Thick glass bends light at its rim: along the top and bottom edges
            // the backdrop shows a little magnified, as through a lens
            void Lens(Bitmap img, RectangleF r, float radius, GlassKind kind)
            {
                float rim = Math.Min(7f, r.Height * 0.13f), x0 = r.X + radius * 0.55f, x1 = r.Right - radius * 0.55f;
                if (x1 - x0 < 4) return;
                LensStrip(img, r, new RectangleF(x0, r.Y, x1 - x0, rim), kind);
                LensStrip(img, r, new RectangleF(x0, r.Bottom - rim, x1 - x0, rim), kind);
            }

            void LensStrip(Bitmap img, RectangleF bar, RectangleF strip, GlassKind kind)
            {
                const float s = 1.14f;
                var o = WindowOrigin();
                var dst = Rectangle.FromLTRB((int)Math.Floor(strip.Left * scale), (int)Math.Floor(strip.Top * scale), (int)Math.Ceiling(strip.Right * scale), (int)Math.Ceiling(strip.Bottom * scale));
                float cx = (bar.X + bar.Width / 2) * scale + o.X, cy = (bar.Y + bar.Height / 2) * scale + o.Y;
                var src = new RectangleF((dst.X + o.X - cx) / s + cx - plate.Rect.X, (dst.Y + o.Y - cy) / s + cy - plate.Rect.Y, dst.Width / s, dst.Height / s);
                var st = g.Save();
                g.ResetTransform();
                g.InterpolationMode = InterpolationMode.Bilinear;
                g.PixelOffsetMode = PixelOffsetMode.Half;
                dst.Offset(-offX, -offY);
                g.DrawImage(img, dst, src.X, src.Y, src.Width, src.Height, GraphicsUnit.Pixel);
                g.FillRectangle(p.B(Ink.A(Ink.Veil, GlassRecipe.Overlay(kind, false, true))), dst);
                g.Restore(st);
            }

            // One specular line: clear along the top edge, softer along the bottom. Two stops only, because a
            // bright stop in the middle lit the bar's waist and read as a seam across the dock.
            void RimLight(RectangleF r, GraphicsPath path, GlassKind kind)
            {
                using (var lg = new LinearGradientBrush(new RectangleF(r.X, r.Y, r.Width, Math.Max(1f, r.Height)), p.T.Highlight, p.T.Highlight, 90f))
                {
                    var cb = new ColorBlend(2);
                    cb.Colors = new[] { Color.FromArgb(GlassRecipe.DockRimTop(kind, Ink.Light), p.T.Highlight), Color.FromArgb(GlassRecipe.DockRimBottom(kind, Ink.Light), p.T.Highlight) };
                    cb.Positions = new[] { 0f, 1f };
                    lg.InterpolationColors = cb;
                    using (var pen = new Pen(lg, 1f)) g.DrawPath(pen, path);
                }
            }

            static GraphicsPath Box(RectangleF r)
            {
                var gp = new GraphicsPath();
                gp.AddRectangle(r);
                return gp;
            }

            // A single wash of light from above, spread over the whole height so it never ends in a visible line.
            // There is deliberately no glow tracking the pointer: that lobe was centred on the bar's waist with a
            // radius taller than the bar, so it painted a pale seam across the dock wherever the cursor sat. The
            // per-icon plate already marks the pointer, which is what a dock is supposed to do.
            void Sheen(RectangleF r, GlassKind kind)
            {
                using (var lg = new LinearGradientBrush(new RectangleF(r.X, r.Y - 1, r.Width, r.Height + 2), Color.FromArgb(GlassRecipe.Sheen(kind, Ink.Light), p.T.Highlight), Color.FromArgb(0, p.T.Highlight), 90f))
                    g.FillRectangle(lg, r.X, r.Y, r.Width, r.Height);
            }

            // Top edge catches the light near the pointer
            void Rim(RectangleF r, float radius)
            {
                if (!RevealReady || hover.Value < 0.01f || mouseX < r.Left || mouseX > r.Right) return;
                float span = Math.Max(60, r.Height * 1.5f);
                float a = Math.Max(r.Left + radius * 0.7f, mouseX - span), b = Math.Min(r.Right - radius * 0.7f, mouseX + span);
                if (b - a < 2) return;
                using (var lg = new LinearGradientBrush(new PointF(mouseX - span, 0), new PointF(mouseX + span, 0), Color.FromArgb(0, p.T.Highlight), Color.FromArgb(0, p.T.Highlight)))
                {
                    var blend = new ColorBlend(3);
                    int peak = (int)(hover.Value * (Ink.Light ? 230 : 150));
                    blend.Colors = new[] { Color.FromArgb(0, p.T.Highlight), Color.FromArgb(peak, p.T.Highlight), Color.FromArgb(0, p.T.Highlight) };
                    blend.Positions = new[] { 0f, 0.5f, 1f };
                    lg.InterpolationColors = blend;
                    using (var pen = new Pen(lg, 1.3f)) g.DrawLine(pen, a, r.Top + 0.65f, b, r.Top + 0.65f);
                }
            }

            void Hit(RectangleF r, string id, Action act, Action menu)
            {
                hits.Add(new KeyValuePair<RectangleF, string>(r, id));
                if (act != null) acts[id] = act;
                if (menu != null) menus[id] = menu;
            }

            void DrawCenter()
            {
                foreach (var s in slots)
                {
                    if (s.Show < 0.01f || s.Id == "gap") continue;
                    if (s.Sep)
                    {
                        float cx = s.Icon.X + s.Icon.Width / 2;
                        g.DrawLine(p.P(Ink.A(p.T.Line, s.Show * 1.6f), 1f), cx, s.Icon.Top, cx, s.Icon.Bottom);
                        continue;
                    }
                    if (s.App == null) DrawSys(s);
                    else DrawApp(s);
                }
            }

            void DrawSys(Slot s)
            {
                string id = "sys:" + s.Id;
                bool hot = hoverId == id;
                float press = SysPressValue(s.Id);
                var r = s.Icon;
                if (press > 0.001f) r = Inset(r, r.Width * (0.055f * press));
                Action act = null, menu = null;
                switch (s.Id)
                {
                    case "start": act = delegate { ToggleSheet("start"); }; menu = QuickMenu; break;
                    case "taskview": act = delegate { ShellVerb("WindowSwitcher"); }; break;
                    case "work": act = delegate { isle.OpenWork(); }; menu = WorkMenu; break;
                    case "shelf": act = OpenHeld; menu = ShelfMenu; break;
                }
                Hit(s.Hit, id, act, menu);
                hot |= keyMode && keySel == id;
                bool startOpen = s.Id == "start" && sheet != null && sheet.Kind == "start";
                var plateRect = OnBar(s.Icon, 0, press);
                bool dropOn = s.Id == "shelf" && fileOver && hot;
                Color baseFill = dropOn ? Ink.Mix(p.T.FillHover, Ink.A(p.T.Accent, 0.4f), 0.6f) : startOpen ? Ink.Mix(p.T.FillHover, Ink.A(p.T.Accent, 0.32f), 0.4f) : hot ? p.T.FillHover : p.T.Fill;
                Color tileFill = press > 0.001f ? Ink.Mix(baseFill, p.T.FillPressed, press) : baseFill;
                p.FillRound(plateRect, Math.Min(plateRect.Width, plateRect.Height) * PlateK, tileFill);
                if (keyMode && keySel == id) FocusRing(s.Icon);
                if (s.Id == "start" && !Anim.Reduced && startPulseUtc > DateTime.MinValue)
                {
                    double pt = (DateTime.UtcNow - startPulseUtc).TotalSeconds;
                    if (pt >= 0 && pt < 0.38)
                    {
                        float pu = (float)(pt / 0.38), eu = Ease.OutCubic(pu);
                        float grow = r.Width * (0.08f + 0.36f * eu);
                        var halo = new RectangleF(r.X - grow / 2, r.Y - grow / 2, r.Width + grow, r.Height + grow);
                        p.StrokeRound(halo, halo.Width * 0.30f, Ink.A(p.T.Text, 0.28f * (1 - pu)), 1.8f * (1 - pu * 0.5f));
                    }
                }
                float cx = r.X + r.Width / 2, cy = r.Y + r.Height / 2, u = r.Width / 42f;
                bool active = (s.Id == "work" && isle.WorkOpen && GetForegroundWindow() == isle.WorkHwnd) || startOpen;
                Color ink = hot || active ? p.T.Text : Ink.Mix(p.T.Sub, p.T.Text, 0.55f);
                switch (s.Id)
                {
                    case "start":
                        float q = 6.6f * u, gap = 1.7f * u;
                        for (int i = 0; i < 4; i++)
                            p.FillRound(new RectangleF(cx - q - gap / 2 + (i % 2) * (q + gap), cy - q - gap / 2 + (i / 2) * (q + gap), q, q), 1.4f * u, ink);
                        break;
                    case "taskview": p.Icon('\uE7C4', 16 * u, ink, cx, cy); break;
                    case "work":
                        DrawWindowStack(workSlotApp, Glyph(r), DateTime.UtcNow, s.Show);
                        p.Icon('\uE73A', 21 * u, ink, cx, cy);
                        Indicator(workDot.Value, false, s.Icon);
                        break;
                    case "shelf": DrawShelfGlyph(s, r, ink, hot); break;
                }
            }

            void DrawApp(Slot s)
            {
                var a = s.App;
                if (a == dragApp) return;
                var app = a;
                Hit(s.Hit, s.Id, delegate { OpenApp(app, false, true); }, delegate { AppMenu(app); });
                bool focus = keyMode && keySel == s.Id;
                bool hot = hoverId == s.Id && dragApp == null, down = pressId == s.Id && dragApp == null;
                var now = DateTime.UtcNow;
                Plate(OnBar(s.Icon, HopLift(a, now), down), hot, down, s.Show);
                if (focus) FocusRing(s.Icon);
                var r = Glyph(down ? Inset(s.Icon, s.Icon.Width * 0.05f) : s.Icon);
                DrawWindowStack(a, r, now, s.Show);
                float sx, sy;
                HopScale(a, now, out sx, out sy);
                float rw = r.Width * sx, rh = r.Height * sy;
                r = new RectangleF(r.X + (r.Width - rw) / 2, r.Bottom - rh, rw, rh);
                if (!Anim.Reduced && DockWindowMotion.Progress(a, now) >= 1)
                {
                    double lt = a.LaunchUtc > DateTime.MinValue ? (now - a.LaunchUtc).TotalSeconds : 9;
                    double pt = a.PulseUtc > DateTime.MinValue ? (now - a.PulseUtc).TotalSeconds : 9;
                    if (a.Hop >= 0 && lt >= 0 && lt < 0.62)
                    {
                        float u = (float)(lt / 0.62), eu = Ease.OutCubic(u);
                        float grow = r.Width * (0.14f + 0.52f * eu);
                        var halo = new RectangleF(r.X - grow / 2, r.Y - grow / 2, r.Width + grow, r.Height + grow);
                        p.StrokeRound(halo, halo.Width * 0.30f, Ink.A(p.T.Text, 0.34f * (1 - u)), 2.2f * (1 - u * 0.5f));
                    }
                    else if (pt >= 0 && pt < 0.38)
                    {
                        float u = (float)(pt / 0.38), eu = Ease.OutCubic(u);
                        float grow = r.Width * (0.08f + 0.36f * eu);
                        var halo = new RectangleF(r.X - grow / 2, r.Y - grow / 2, r.Width + grow, r.Height + grow);
                        p.StrokeRound(halo, halo.Width * 0.30f, Ink.A(p.T.Text, 0.28f * (1 - u)), 1.8f * (1 - u * 0.5f));
                    }
                }
                DrawIconImage(a, r, s.Show);
                Indicator(a, s.Icon);
                if (a.Badge > 0 && s.Show > 0.5f) Badge(s.Icon, a.Badge);
            }

            void DrawWindowStack(DockApp app, RectangleF icon, DateTime now, float visibility)
            {
                float t = DockWindowMotion.Progress(app, now);
                if (t < 0 || t >= 1) return;
                var pose = DockWindowMotion.Sample(app, t);
                float unit = icon.Width, cx = icon.Left + unit * 0.5f, cy = icon.Top + icon.Height * 0.5f;
                DrawPlayingCard(pose.Back, cx, cy, unit, visibility, 0);
                DrawPlayingCard(pose.Middle, cx, cy, unit, visibility, 1);
                DrawPlayingCard(pose.Front, cx, cy, unit, visibility, 2);
            }

            void DrawPlayingCard(DockPlayingCard card, float cx, float cy, float unit, float visibility, int depth)
            {
                float alpha = visibility * card.Opacity;
                if (alpha <= 0.001f) return;
                // Keep a narrow visible edge through the turnover instead of hiding the card.
                float turn = 1 - 0.64f * (float)Math.Sin(Math.PI * card.Facing);
                float width = unit * 0.74f * card.Scale, height = unit * 1.04f * card.Scale;
                var state = g.Save();
                try
                {
                    g.TranslateTransform(cx + card.X * unit, cy + card.Y * unit);
                    g.RotateTransform(card.Angle);
                    g.ScaleTransform(turn, 1);
                    var face = new RectangleF(-width * 0.5f, -height * 0.5f, width, height);
                    float radius = width * 0.15f;
                    var shadow = face;
                    shadow.Offset(0, unit * 0.047f);
                    p.FillRound(shadow, radius, Ink.Scrim((int)(alpha * 54)));
                    var back = Ink.Mix(p.T.Accent, p.T.OnAccent, (2 - depth) * 0.12f);
                    var fill = Ink.Mix(back, Ink.Sheen(255), card.Facing);
                    p.FillRound(face, radius, Ink.A(fill, alpha));
                    var mark = Ink.Mix(p.T.OnAccent, Ink.Mix(Ink.Scrim(255), p.T.Accent, 0.37f), card.Facing);
                    float hiddenFace = 1 - card.Facing;
                    if (hiddenFace > 0.001f)
                    {
                        float inset = width * 0.13f;
                        p.StrokeRound(Inset(face, inset), Math.Max(1, radius - inset),
                            Ink.A(mark, alpha * hiddenFace * 0.43f), Math.Max(0.75f, unit * 0.025f));
                    }
                    DrawDeckDiamond(0, 0, width * 0.16f, height * 0.16f, Ink.A(mark, alpha * 0.87f));
                    var pip = Ink.A(mark, alpha * card.Facing * 0.83f);
                    DrawDeckDiamond(-width * 0.30f, -height * 0.31f, width * 0.057f, height * 0.051f, pip);
                    DrawDeckDiamond(width * 0.30f, height * 0.31f, width * 0.057f, height * 0.051f, pip);
                }
                finally { g.Restore(state); }
            }

            void DrawDeckDiamond(float x, float y, float rx, float ry, Color color)
            {
                deckPip[0] = new PointF(x, y - ry);
                deckPip[1] = new PointF(x + rx, y);
                deckPip[2] = new PointF(x, y + ry);
                deckPip[3] = new PointF(x - rx, y);
                g.FillPolygon(p.B(color), deckPip);
            }

            // Apps and system buttons sit on one tile shape
            void Tile(RectangleF slot, bool hot, bool down, float alpha)
            {
                Plate(Inset(slot, slot.Width * (down ? 0.1f : 0.07f)), hot, down, alpha);
            }

            void Plate(RectangleF plate, bool hot, bool down, float alpha)
            {
                p.Alpha = Ease.Clamp01(alpha);
                p.FillRound(plate, Math.Min(plate.Width, plate.Height) * PlateK, down ? p.T.FillPressed : hot ? p.T.FillHover : p.T.Fill);
                p.Alpha = 1;
            }

            // Icon may rise; plate stays put
            RectangleF OnBar(RectangleF slot, float lift, bool down)
            {
                return OnBar(slot, lift, down ? 1f : 0f);
            }

            RectangleF OnBar(RectangleF slot, float lift, float press)
            {
                var plate = Inset(new RectangleF(slot.X, slot.Y + lift, slot.Width, slot.Height), slot.Width * (0.07f + 0.03f * press));
                float top = Math.Max(plate.Y, barTop + 4);
                return RectangleF.FromLTRB(plate.Left, Math.Min(top, plate.Bottom - 8), plate.Right, plate.Bottom);
            }

            // Keyboard focus: a ring the eye finds at once
            void FocusRing(RectangleF slot)
            {
                var ring = Inset(slot, slot.Width * 0.02f);
                p.StrokeRound(ring, ring.Width * 0.32f, p.T.Text, 2f);
            }

            static RectangleF Glyph(RectangleF slot)
            {
                float s = slot.Width * GlyphK;
                return new RectangleF(slot.X + (slot.Width - s) / 2, slot.Y + (slot.Height - s) / 2, s, s);
            }

            void DrawIconImage(DockApp a, RectangleF r, float alpha)
            {
                Bitmap ic = IconFor(a.Key);
                if (ic == null)
                {
                    // No icon: the name's first letter on a tile
                    p.Alpha = alpha;
                    p.TextCenter(a.Name.Length > 0 ? a.Name.Substring(0, 1).ToUpperInvariant() : "?", Ink.Semi(r.Width * 0.62f), p.T.Text, r.X + r.Width / 2, r.Y + r.Height / 2);
                    p.Alpha = 1;
                    return;
                }
                // At rest: an exact pixel copy, crisp at any scale
                if (alpha > 0.995f && Math.Abs(r.Width - iconSize * GlyphK) < 0.02f)
                {
                    var rest = RestIcon(a.Key, ic);
                    if (rest != null) { DrawExact(rest, r); return; }
                }
                if (alpha > 0.995f)
                {
                    // Magnified: from a copy near the drawn size, cheap to scale
                    var big = BigIcon(a.Key, ic);
                    var keep = g.InterpolationMode;
                    g.InterpolationMode = InterpolationMode.Bilinear;
                    g.DrawImage(big ?? ic, r);
                    g.InterpolationMode = keep;
                    return;
                }
                using (var ia = new ImageAttributes())
                {
                    ia.SetColorMatrix(new ColorMatrix { Matrix33 = Ease.Clamp01(alpha) });
                    var dst = new Rectangle((int)Math.Round(r.X * scale) - offX, (int)Math.Round(r.Y * scale) - offY, Math.Max(1, (int)Math.Round(r.Width * scale)), Math.Max(1, (int)Math.Round(r.Height * scale)));
                    var st = g.Save();
                    g.ResetTransform();
                    g.DrawImage(ic, dst, 0, 0, ic.Width, ic.Height, GraphicsUnit.Pixel, ia);
                    g.Restore(st);
                }
            }

            Bitmap RestIcon(string key, Bitmap src)
            {
                int px = Math.Max(8, (int)Math.Round(iconSize * GlyphK * scale));
                if (px != restPx)
                {
                    foreach (var b in restIcons.Values) if (b != null) b.Dispose();
                    restIcons.Clear();
                    restPx = px;
                }
                Bitmap bmp;
                if (restIcons.TryGetValue(key, out bmp)) return bmp;
                bmp = new Bitmap(px, px, PixelFormat.Format32bppPArgb);
                using (var bg = Graphics.FromImage(bmp))
                {
                    bg.InterpolationMode = InterpolationMode.HighQualityBicubic;
                    bg.PixelOffsetMode = PixelOffsetMode.HighQuality;
                    bg.DrawImage(src, new Rectangle(0, 0, px, px));
                }
                restIcons[key] = bmp;
                return bmp;
            }

            readonly Dictionary<string, Bitmap> bigIcons = new Dictionary<string, Bitmap>(StringComparer.OrdinalIgnoreCase);
            int bigPx;

            Bitmap BigIcon(string key, Bitmap src)
            {
                int px = Math.Max(8, (int)Math.Ceiling(iconSize * GlyphK * Math.Max(1, Mag) * scale));
                if (px >= src.Width) return null;
                if (px != bigPx)
                {
                    foreach (var b in bigIcons.Values) if (b != null) b.Dispose();
                    bigIcons.Clear();
                    bigPx = px;
                }
                Bitmap bmp;
                if (bigIcons.TryGetValue(key, out bmp)) return bmp;
                bmp = new Bitmap(px, px, PixelFormat.Format32bppPArgb);
                using (var bg = Graphics.FromImage(bmp))
                {
                    bg.InterpolationMode = InterpolationMode.HighQualityBicubic;
                    bg.PixelOffsetMode = PixelOffsetMode.HighQuality;
                    bg.DrawImage(src, new Rectangle(0, 0, px, px));
                }
                bigIcons[key] = bmp;
                return bmp;
            }

        }
    }
}
