using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.Runtime.InteropServices;

namespace Deskmate
{
    // Drawing helpers: global alpha, active tone
    class Painter : IDisposable
    {
        public Graphics G;
        // Text and glyphs snap to whole pixels; shapes sit half a pixel off
        public float HalfPx;
        public float Alpha = 1f;
        public Tone T = Ink.Base;
        public bool Shadow; // legibility on see-through glass
        readonly SolidBrush brush = new SolidBrush(Color.White);
        readonly Pen pen = new Pen(Color.White, 1f);
        readonly StringFormat line, wrap;
        readonly ImageAttributes imageAttr = new ImageAttributes();
        readonly ColorMatrix matrix = new ColorMatrix();
        // One path reset per shape
        readonly GraphicsPath shape = new GraphicsPath();
        // Gradients remapped per use, never rebuilt
        readonly LinearGradientBrush glow, lip, split;
        readonly Blend glowBlend = new Blend(3), lipBlend = new Blend(4), splitBlend = new Blend(4);
        readonly Color[] pair = new Color[2];
        // Widths per font, cleared when G changes
        readonly Dictionary<Font, Dictionary<string, float>> widths = new Dictionary<Font, Dictionary<string, float>>(new Ink.SameFont());
        Graphics widthsFor;
        int widthCount;

        public void Dispose()
        {
            brush.Dispose();
            pen.Dispose();
            line.Dispose();
            wrap.Dispose();
            imageAttr.Dispose();
            shape.Dispose();
            glow.Dispose();
            lip.Dispose();
            split.Dispose();
        }

        public Painter()
        {
            pen.StartCap = LineCap.Round;
            pen.EndCap = LineCap.Round;
            pen.LineJoin = LineJoin.Round;
            line = (StringFormat)StringFormat.GenericTypographic.Clone();
            line.FormatFlags |= StringFormatFlags.NoWrap | StringFormatFlags.MeasureTrailingSpaces;
            line.Trimming = StringTrimming.EllipsisCharacter;
            line.LineAlignment = StringAlignment.Center;
            wrap = (StringFormat)StringFormat.GenericTypographic.Clone();
            wrap.FormatFlags |= StringFormatFlags.LineLimit;
            wrap.Trimming = StringTrimming.EllipsisCharacter;
            var unit = new RectangleF(0, 0, Ink.GradientSpan, Ink.GradientSpan);
            glow = new LinearGradientBrush(unit, Color.White, Color.White, LinearGradientMode.Horizontal) { WrapMode = WrapMode.TileFlipXY };
            lip = new LinearGradientBrush(unit, Color.White, Color.White, LinearGradientMode.Horizontal) { WrapMode = WrapMode.TileFlipXY };
            split = new LinearGradientBrush(unit, Color.White, Color.White, LinearGradientMode.Horizontal) { WrapMode = WrapMode.TileFlipXY };
            glowBlend.Positions = new[] { 0f, 0.45f, 1f };
            glowBlend.Factors = new[] { 0f, 1f, 1f };
            glow.Blend = glowBlend;
            lipBlend.Positions = new[] { 0f, 0.3f, 0.7f, 1f };
            lipBlend.Factors = new[] { 1f, 0f, 0f, 1f };
            lip.Blend = lipBlend;
            splitBlend.Positions = new[] { 0f, 0.5f, 0.51f, 1f };
            splitBlend.Factors = new[] { 0f, 0f, 1f, 1f };
        }

        public Brush B(Color c) { brush.Color = Ink.A(c, Alpha); return brush; }

        public Pen P(Color c, float width)
        {
            pen.Color = Ink.A(c, Alpha);
            pen.Width = width;
            return pen;
        }

        public void FillRound(RectangleF r, float radius, Color c)
        {
            shape.Reset();
            Ink.AddRound(shape, r, radius);
            G.FillPath(B(c), shape);
        }

        public void FillRound(RectangleF r, float radius, Brush b)
        {
            shape.Reset();
            Ink.AddRound(shape, r, radius);
            G.FillPath(b, shape);
        }

        public void StrokeRound(RectangleF r, float radius, Color c, float width)
        {
            shape.Reset();
            Ink.AddRound(shape, r, radius);
            G.DrawPath(P(c, width), shape);
        }

        public void Circle(float cx, float cy, float r, Color c) { G.FillEllipse(B(c), cx - r, cy - r, r * 2, r * 2); }

        static void MapV(LinearGradientBrush b, float y, float h)
        {
            b.ResetTransform();
            b.TranslateTransform(0, y);
            b.ScaleTransform(1, Math.Max(0.01f, h) / Ink.GradientSpan);
            b.RotateTransform(90);
        }

        static void MapH(LinearGradientBrush b, float x, float w)
        {
            b.ResetTransform();
            b.TranslateTransform(x, 0);
            b.ScaleTransform(Math.Max(0.01f, w) / Ink.GradientSpan, 1);
        }

        // Module: fill, top inner light, faint lip
        public void Module(RectangleF r, float radius, Color fill)
        {
            shape.Reset();
            Ink.AddRound(shape, r, radius);
            if (!T.IsDark)
            {
                // Paper: the fill alone, one clean warm tone step; no gradient, no lip
                brush.Color = Ink.A(fill, Alpha);
                G.FillPath(brush, shape);
                return;
            }
            pair[0] = Ink.A(Ink.Over(T.Highlight, fill), Alpha);
            pair[1] = Ink.A(fill, Alpha);
            glow.LinearColors = pair;
            MapV(glow, r.Y, r.Height);
            G.FillPath(glow, shape);
            if (T.Lip.A == 0) return;
            float e = Ink.CornerExtent(Math.Min(radius, Math.Min(r.Width, r.Height) / 2)), w = r.Width - 2 * e;
            if (w < 8) return;
            pair[0] = Ink.A(T.Lip, Alpha);
            pair[1] = Color.FromArgb(0, T.Lip);
            lip.LinearColors = pair;
            MapH(lip, r.X + e, w);
            G.FillRectangle(lip, r.X + e, r.Bottom - 1.4f, w, 0.9f);
        }

        // Hard colour step at x, no clip
        public Brush Split(float x, float w, float at, Color left, Color right)
        {
            if (at <= x + 0.05f) return B(right);
            if (at >= x + w - 0.05f) return B(left);
            float e = Math.Min(0.02f, 0.9f / Math.Max(1, w));
            float f = Math.Max(0.001f, Math.Min(0.998f - e, (at - x) / w));
            splitBlend.Positions[1] = f;
            splitBlend.Positions[2] = f + e;
            split.Blend = splitBlend;
            pair[0] = Ink.A(left, Alpha);
            pair[1] = Ink.A(right, Alpha);
            split.LinearColors = pair;
            MapH(split, x, w);
            return split;
        }

        public float Measure(string s, Font f)
        {
            if (string.IsNullOrEmpty(s)) return 0;
            if (G == null || f == null) return s.Length * (f != null ? f.Size * 0.6f : 7.5f);
            if (!ReferenceEquals(widthsFor, G) || widthCount > 4096) { widths.Clear(); widthsFor = G; widthCount = 0; }
            Dictionary<string, float> byText;
            if (!widths.TryGetValue(f, out byText)) widths[f] = byText = new Dictionary<string, float>();
            float w;
            if (byText.TryGetValue(s, out w)) return w;
            try { w = G.MeasureString(s, f, PointF.Empty, line).Width; }
            catch (ArgumentException) { return s.Length * (f.Size * 0.6f); }
            byText[s] = w;
            widthCount++;
            return w;
        }

        // Capital height centred on cy
        RectangleF LineBox(Font f, float x, float cy, float maxWidth, StringAlignment align)
        {
            float h = f.Size * 2f;
            float left = align == StringAlignment.Near ? x : align == StringAlignment.Far ? x - maxWidth : x - maxWidth / 2;
            return new RectangleF(left + HalfPx, cy - h / 2 + Ink.CapShift(f) + HalfPx, maxWidth, h);
        }

        // One line, centred on cy, ellipsized
        public void Text(string s, Font f, Color c, float x, float cy, float maxWidth, StringAlignment align)
        {
            if (string.IsNullOrEmpty(s) || maxWidth < 4) return;
            line.Alignment = align;
            var box = LineBox(f, x, cy, maxWidth, align);
            if (Shadow && c.A > 60)
            {
                G.DrawString(s, f, B(ShadowOf(c, 1f)), new RectangleF(box.X, box.Y + 1f, box.Width, box.Height), line);
                G.DrawString(s, f, B(ShadowOf(c, 0.45f)), new RectangleF(box.X + 1f, box.Y + 2f, box.Width, box.Height), line);
            }
            G.DrawString(s, f, B(c), box, line);
        }

        // Light ink gets a dark shadow; dark ink on Clear glass gets a light one.
        Color ShadowOf(Color c, float amount)
        {
            bool lightInk = c.R > 128;
            int a = (int)((lightInk ? 165 : 140) * amount * c.A / 255f);
            return lightInk ? Color.FromArgb(a, 0, 0, 0) : Color.FromArgb(a, 255, 255, 255);
        }

        // Notification-only font fallback; the general UI text renderer stays unchanged.
        internal static bool NotifEmoji(string s, int i)
        {
            int c = char.IsSurrogatePair(s, i) ? char.ConvertToUtf32(s, i) : s[i];
            return (c >= 0x1F000 && c <= 0x1FAFF) || (c >= 0x2600 && c <= 0x27BF) ||
                   (c >= 0x2300 && c <= 0x23FF) || (c >= 0x2B00 && c <= 0x2BFF) ||
                   c == 0x00A9 || c == 0x00AE || c == 0x203C || c == 0x2049 ||
                   c == 0x2122 || c == 0x2139 || c == 0x3030 || c == 0x303D ||
                   c == 0x3297 || c == 0x3299 ||
                   (i + 1 < s.Length && (s[i + 1] == '\uFE0F' || s[i + 1] == '\u20E3'));
        }

        internal static int NotifElementLength(string s, int start)
        {
            int end = start + System.Globalization.StringInfo.GetNextTextElement(s, start).Length;
            int first = char.IsSurrogatePair(s, start) ? char.ConvertToUtf32(s, start) : s[start];
            while (end < s.Length)
            {
                int c = char.IsSurrogatePair(s, end) ? char.ConvertToUtf32(s, end) : s[end];
                if (c == 0x200D && end + 1 < s.Length)
                {
                    end++;
                    end += System.Globalization.StringInfo.GetNextTextElement(s, end).Length;
                }
                else if (c == 0xFE0F || c == 0xFE0E || c == 0x20E3 ||
                         (c >= 0x1F3FB && c <= 0x1F3FF) || (c >= 0xE0020 && c <= 0xE007F))
                    end += c > 0xFFFF ? 2 : 1;
                else if (first >= 0x1F1E6 && first <= 0x1F1FF && end == start + 2 &&
                         c >= 0x1F1E6 && c <= 0x1F1FF)
                    end += 2;
                else break;
            }
            return end - start;
        }

        public void NotifText(string s, Font f, Color c, float x, float cy, float maxWidth, StringAlignment align)
        {
            if (string.IsNullOrEmpty(s) || maxWidth < 4) return;
            bool hasEmoji = false;
            for (int i = 0; i < s.Length; i += NotifElementLength(s, i))
                if (NotifEmoji(s, i)) { hasEmoji = true; break; }
            if (!hasEmoji) { Text(s, f, c, x, cy, maxWidth, align); return; }

            using (var emoji = new Font("Segoe UI Emoji", f.Size, FontStyle.Regular, f.Unit))
            using (var fmt = (StringFormat)line.Clone())
            {
                fmt.Alignment = StringAlignment.Near;
                fmt.Trimming = StringTrimming.None;
                var runs = new List<string>();
                var faces = new List<Font>();
                for (int i = 0; i < s.Length;)
                {
                    int len = NotifElementLength(s, i);
                    Font face = NotifEmoji(s, i) ? emoji : f;
                    string part = s.Substring(i, len);
                    int last = runs.Count - 1;
                    if (last >= 0 && ReferenceEquals(faces[last], face)) runs[last] += part;
                    else { runs.Add(part); faces.Add(face); }
                    i += len;
                }
                var widths = new List<float>();
                float total = 0;
                for (int i = 0; i < runs.Count; i++)
                {
                    float width = G.MeasureString(runs[i], faces[i], PointF.Empty, fmt).Width;
                    widths.Add(width); total += width;
                }
                float dots = G.MeasureString("\u2026", f, PointF.Empty, fmt).Width;
                bool trim = total > maxWidth;
                // ponytail: bounded notification lines; trim by whole emoji, not UTF-16 code units.
                while (trim && runs.Count > 0 && total + dots > maxWidth)
                {
                    int last = runs.Count - 1, cut = 0;
                    for (int i = 0; i < runs[last].Length; i += NotifElementLength(runs[last], i)) cut = i;
                    total -= widths[last];
                    runs[last] = runs[last].Substring(0, cut);
                    if (cut == 0) { runs.RemoveAt(last); faces.RemoveAt(last); widths.RemoveAt(last); }
                    else
                    {
                        widths[last] = G.MeasureString(runs[last], faces[last], PointF.Empty, fmt).Width;
                        total += widths[last];
                    }
                }
                if (trim && dots <= maxWidth) { runs.Add("\u2026"); faces.Add(f); widths.Add(dots); total += dots; }
                float left = align == StringAlignment.Near ? x : align == StringAlignment.Far ? x - total : x - total / 2;
                var clip = LineBox(f, x, cy, maxWidth, align);
                var state = G.Save();
                try
                {
                    G.IntersectClip(clip);
                    for (int i = 0; i < runs.Count; i++)
                    {
                        var box = LineBox(faces[i], left, cy, Math.Max(widths[i] + 4, maxWidth), StringAlignment.Near);
                        if (Shadow && c.A > 60)
                        {
                            G.DrawString(runs[i], faces[i], B(ShadowOf(c, 1f)),
                                new RectangleF(box.X, box.Y + 1, box.Width, box.Height), fmt);
                            G.DrawString(runs[i], faces[i], B(ShadowOf(c, 0.45f)),
                                new RectangleF(box.X + 1, box.Y + 2, box.Width, box.Height), fmt);
                        }
                        G.DrawString(runs[i], faces[i], B(c), box, fmt);
                        left += widths[i];
                    }
                }
                finally { G.Restore(state); }
            }
        }

        // Same line with any brush, like Split
        public void Text(string s, Font f, Brush b, float x, float cy, float maxWidth, StringAlignment align)
        {
            if (string.IsNullOrEmpty(s) || maxWidth < 4) return;
            line.Alignment = align;
            G.DrawString(s, f, b, LineBox(f, x, cy, maxWidth, align), line);
        }

        public void Text(string s, Font f, Color c, float x, float cy) { Text(s, f, c, x, cy, 2000, StringAlignment.Near); }
        public void TextRight(string s, Font f, Color c, float right, float cy) { Text(s, f, c, right, cy, 2000, StringAlignment.Far); }
        public void TextCenter(string s, Font f, Color c, float cx, float cy) { Text(s, f, c, cx, cy, 2000, StringAlignment.Center); }

        public void TextBox(string s, Font f, Color c, RectangleF r) { TextBox(s, f, c, r, StringAlignment.Near); }

        // Height of s wrapped to width
        public float WrapHeight(string s, Font f, float width)
        {
            if (string.IsNullOrEmpty(s) || G == null) return 0;
            wrap.Alignment = StringAlignment.Near;
            return G.MeasureString(s, f, new SizeF(Math.Max(10, width), 100000), wrap).Height;
        }

        public void TextBox(string s, Font f, Color c, RectangleF r, StringAlignment align)
        {
            if (string.IsNullOrEmpty(s)) return;
            wrap.Alignment = align;
            G.DrawString(s, f, B(c), r, wrap);
        }

        // Wrapped text centred both ways in r
        public void TextBoxCenter(string s, Font f, Color c, RectangleF r)
        {
            if (string.IsNullOrEmpty(s)) return;
            wrap.Alignment = StringAlignment.Center;
            wrap.LineAlignment = StringAlignment.Center;
            G.DrawString(s, f, B(c), new RectangleF(r.X, r.Y - f.Size * Ink.CapLift, r.Width, r.Height), wrap);
            wrap.LineAlignment = StringAlignment.Near;
        }

        public void Icon(char glyph, float px, Color c, float cx, float cy) { Icon(glyph, px, B(c), cx, cy); }

        public void Icon(char glyph, float px, Brush b, float cx, float cy)
        {
            line.Alignment = StringAlignment.Center;
            var n = Ink.IconNudge(glyph, px);
            G.DrawString(Ink.Str(glyph), Ink.Icons(px), b, new RectangleF(cx - px + n.X + HalfPx, cy - px + n.Y + HalfPx, px * 2, px * 2), line);
        }

        // Cached vector symbol at cx, cy
        public void Symbol(Sym kind, float cx, float cy, float size, Color c)
        {
            float stroke;
            var path = Ink.SymbolPath(kind, size, out stroke);
            G.TranslateTransform(cx, cy);
            if (stroke > 0) G.DrawPath(P(c, stroke), path);
            else G.FillPath(B(c), path);
            G.TranslateTransform(-cx, -cy);
        }

        public void Image(Image img, RectangleF r, float radius)
        {
            matrix.Matrix33 = Alpha;
            imageAttr.SetColorMatrix(matrix);
            shape.Reset();
            Ink.AddRound(shape, r, radius);
            using (var tb = new TextureBrush(img, new Rectangle(0, 0, img.Width, img.Height), imageAttr))
            {
                tb.WrapMode = WrapMode.Clamp;
                tb.TranslateTransform(r.X, r.Y);
                tb.ScaleTransform(r.Width / img.Width, r.Height / img.Height);
                G.FillPath(tb, shape);
            }
        }

        // Orbit: track plus comet-tail arc
        public void Orbit(float cx, float cy, float r, float phase, Color c)
        {
            var rect = new RectangleF(cx - r, cy - r, r * 2, r * 2);
            G.DrawEllipse(P(Ink.A(c, 0.22f), 1.8f), rect);
            float start = phase * 360f - 90;
            for (int i = 0; i < 6; i++)
            {
                float a = 1f - i / 6f;
                G.DrawArc(P(Ink.A(c, a), 2.2f), rect, start - i * 14, 16);
            }
        }

        public void Check(float cx, float cy, float s, Color c, float progress)
        {
            var a = new PointF(cx - s * 0.42f, cy + s * 0.02f);
            var b = new PointF(cx - s * 0.12f, cy + s * 0.32f);
            var d = new PointF(cx + s * 0.46f, cy - s * 0.32f);
            float p1 = Ease.Clamp01(progress / 0.4f), p2 = Ease.Clamp01((progress - 0.4f) / 0.6f);
            var pen = P(c, s * 0.16f);
            G.DrawLine(pen, a, new PointF(a.X + (b.X - a.X) * p1, a.Y + (b.Y - a.Y) * p1));
            if (p2 > 0) G.DrawLine(pen, b, new PointF(b.X + (d.X - b.X) * p2, b.Y + (d.Y - b.Y) * p2));
        }

        public void Cross(float cx, float cy, float s, Color c)
        {
            var pen = P(c, s * 0.15f);
            G.DrawLine(pen, cx - s * 0.36f, cy - s * 0.36f, cx + s * 0.36f, cy + s * 0.36f);
            G.DrawLine(pen, cx + s * 0.36f, cy - s * 0.36f, cx - s * 0.36f, cy + s * 0.36f);
        }

        // Legacy sizes mapped onto the symbols
        public void Play(float cx, float cy, float s, Color c) { Symbol(Sym.Play, cx, cy, 1.22f * s, c); }

        public void Pause(float cx, float cy, float s, Color c) { Symbol(Sym.Pause, cx, cy, 1.19f * s, c); }

        public void Skip(float cx, float cy, float s, Color c, bool forward) { Symbol(forward ? Sym.Next : Sym.Prev, cx, cy, 1.1f * s, c); }

        public void Battery(float x, float cy, float level, Color c, bool charging)
        {
            var body = new RectangleF(x, cy - 7f, 28, 14);
            StrokeRound(body, 4f, Ink.A(c, 0.55f), 1.4f);
            FillRound(new RectangleF(body.Right + 1.6f, cy - 2.6f, 2.2f, 5.2f), 1f, Ink.A(c, 0.55f));
            float w = Math.Max(2f, (body.Width - 5) * Ease.Clamp01(level));
            FillRound(new RectangleF(body.X + 2.5f, body.Y + 2.5f, w, body.Height - 5), 2f, c);
            if (charging) Icon(Ink.IcoBolt, 10f, T.Bg, body.X + body.Width / 2, cy);
        }
    }

    // Odometer digits that roll on change
    class Roller
    {
        string shown = "", prev = "";
        DateTime changedUtc = DateTime.MinValue;
        const double Duration = 0.42;

        public void Set(string s)
        {
            if (s == shown) return;
            prev = shown;
            shown = s;
            changedUtc = DateTime.UtcNow;
        }

        public string Value { get { return shown; } }

        public bool Moving(DateTime now) { return (now - changedUtc).TotalSeconds < Duration; }

        public float Width(Painter p, Font f) { return CharsWidth(p, f, shown); }

        static float CharW(Painter p, Font f, char c)
        {
            return char.IsDigit(c) ? p.Measure("0", f) : p.Measure(Ink.Str(c), f);
        }

        static float CharsWidth(Painter p, Font f, string s)
        {
            float w = 0;
            foreach (char c in s) w += CharW(p, f, c);
            return w;
        }

        public void Draw(Painter p, Font f, Color c, float x, float cy, StringAlignment align, DateTime now)
        {
            float total = CharsWidth(p, f, shown);
            float left = align == StringAlignment.Near ? x : align == StringAlignment.Far ? x - total : x - total / 2;
            float raw = (float)((now - changedUtc).TotalSeconds / Duration);
            float t = Anim.Reduced ? Ease.OutCubic(raw) : Ease.OutBack(raw);
            float lineH = f.Size * 1.05f;
            int offset = prev.Length - shown.Length;
            float cx = left;
            bool rolling = raw < 1;
            var state = rolling ? p.G.Save() : null;
            if (rolling) p.G.IntersectClip(new RectangleF(left - 2, cy - lineH * 0.62f, total + 4, lineH * 1.24f));
            float alpha = p.Alpha;
            for (int i = 0; i < shown.Length; i++)
            {
                char now_c = shown[i];
                int j = i + offset;
                char old_c = j >= 0 && j < prev.Length ? prev[j] : ' ';
                float cw = CharW(p, f, now_c);
                string s = Ink.Str(now_c);
                if (old_c != now_c && t < 1f)
                {
                    p.Alpha = alpha * Ease.Clamp01(t * 1.4f);
                    p.Text(s, f, c, cx, cy + (1 - t) * lineH, cw + 6, StringAlignment.Near);
                    p.Alpha = alpha * Ease.Clamp01(1 - t * 1.6f);
                    p.Text(Ink.Str(old_c), f, c, cx, cy - t * lineH, cw + 6, StringAlignment.Near);
                    p.Alpha = alpha;
                }
                else p.Text(s, f, c, cx, cy, cw + 6, StringAlignment.Near);
                cx += cw;
            }
            if (rolling) p.G.Restore(state);
        }
    }
}
