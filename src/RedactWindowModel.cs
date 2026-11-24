using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace Deskmate
{
    partial class RedactWindow : KitWindow
    {
        // Geometry boundary & lightweight Undo/Redo/Reset
        static bool IsGeometryEdit(EditKind k)
        {
            return k == EditKind.Crop || k == EditKind.Turn || k == EditKind.MirrorX || k == EditKind.MirrorY || k == EditKind.Frame;
        }

        int GeometryBaseIndex()
        {
            for (int i = edits.Count - 1; i >= 0; i--)
                if (IsGeometryEdit(edits[i].Kind)) return i + 1;
            return 0;
        }

        int NextStepNumber()
        {
            int max = 0;
            int start = Math.Max(GeometryBaseIndex(), Math.Min(stepFloorIndex, edits.Count));
            for (int i = start; i < edits.Count; i++)
                if (edits[i].Kind == EditKind.Step && edits[i].StepNumber > max) max = edits[i].StepNumber;
            return max + 1;
        }

        static List<Edit> CloneEdits(List<Edit> src)
        {
            var list = new List<Edit>(src.Count);
            for (int i = 0; i < src.Count; i++) list.Add(src[i].Clone());
            return list;
        }

        void PushUndo()
        {
            undoStack.Push(CloneEdits(edits));
            redoStack.Clear();
            UpdateActionButtons();
        }

        void AddEdit(Edit e)
        {
            if (inlineTextActive) CommitInlineText();
            PushUndo();
            edits.Add(e);
            var before = work.Size;
            if (IsGeometryEdit(e.Kind))
            {
                selectedIndex = -1;
                cachedBaseCount = -1;
                RebuildWork(true);
            }
            else
            {
                work = Apply(work, e);
                if (work.Size != before) { fitMode = true; pan = PointF.Empty; UpdateCanvas(); }
                UpdateHint();
                Invalidate();
            }
            UpdateActionButtons();
        }

        void Undo()
        {
            if (undoStack.Count == 0) return;
            CancelPendingOps();
            redoStack.Push(CloneEdits(edits));
            edits.Clear();
            edits.AddRange(undoStack.Pop());
            if (stepFloorIndex > edits.Count) stepFloorIndex = 0;
            selectedIndex = -1;
            cachedBaseCount = -1;
            RebuildWork(true);
        }

        void Redo()
        {
            if (redoStack.Count == 0) return;
            CancelPendingOps();
            undoStack.Push(CloneEdits(edits));
            edits.Clear();
            edits.AddRange(redoStack.Pop());
            if (stepFloorIndex > edits.Count) stepFloorIndex = 0;
            selectedIndex = -1;
            cachedBaseCount = -1;
            RebuildWork(true);
        }

        void ResetAll()
        {
            CancelPendingOps();
            if (edits.Count == 0) return;
            PushUndo();
            edits.Clear();
            stepFloorIndex = 0;
            selectedIndex = -1;
            cachedBaseCount = -1;
            RebuildWork(true);
        }

        void CancelPendingOps()
        {
            if (inlineTextActive) CancelInlineText();
            dragging = false;
            panning = false;
            Capture = false;
            stroke.Clear();
            dragBox = Rectangle.Empty;
            pendingCrop = Rectangle.Empty;
        }

        void RebuildWork(bool resetFitOnSizeChange)
        {
            var before = work.Size;
            int geomIdx = GeometryBaseIndex();
            if (cachedBaseCount != geomIdx || baseWork == null)
            {
                if (baseWork != null) baseWork.Dispose();
                baseWork = new Bitmap(original);
                for (int i = 0; i < geomIdx; i++)
                    baseWork = Apply(baseWork, edits[i]);
                cachedBaseCount = geomIdx;
            }

            work.Dispose();
            work = new Bitmap(baseWork);
            for (int i = geomIdx; i < edits.Count; i++)
                work = Apply(work, edits[i]);

            // Sync framePopup selection to current latest Frame edit
            FramePreset activeFrame = FramePreset.None;
            for (int i = edits.Count - 1; i >= 0; i--)
                if (edits[i].Kind == EditKind.Frame) { activeFrame = edits[i].Frame; break; }
            framePopup.Selected = (int)activeFrame;

            if (resetFitOnSizeChange && work.Size != before)
            {
                fitMode = true;
                pan = PointF.Empty;
            }
            UpdateContextBar();
            UpdateActionButtons();
            LayoutControls();
            Invalidate();
        }

        void UpdateActionButtons()
        {
            undoBtn.Enabled = undoStack.Count > 0;
            redoBtn.Enabled = redoStack.Count > 0;
            resetBtn.Enabled = edits.Count > 0;
        }

        static Bitmap Apply(Bitmap b, Edit e)
        {
            switch (e.Kind)
            {
                case EditKind.Redact:
                    if (e.RedactStyle == RedactMode.Pixelate) Pixelate(b, e.Box);
                    else if (e.RedactStyle == RedactMode.Blur)
                    {
                        int rad = Math.Max(10, Math.Min(22, Math.Min(e.Box.Width, e.Box.Height) / 4));
                        BoxBlur(b, e.Box, rad);
                        BoxBlur(b, e.Box, rad);
                    }
                    else
                    {
                        var r = Rectangle.Intersect(e.Box, new Rectangle(0, 0, b.Width, b.Height));
                        if (r.Width > 0 && r.Height > 0)
                            using (var g = Graphics.FromImage(b))
                            using (var br = new SolidBrush(Color.FromArgb(255, e.StrokeColor)))
                                g.FillRectangle(br, r);
                    }
                    return b;

                case EditKind.Pen:
                    using (var g = Graphics.FromImage(b))
                        DrawStroke(g, e.Points, e.StrokeColor, e.StrokeWidth, 1f, PointF.Empty);
                    return b;

                case EditKind.Highlight:
                    using (var g = Graphics.FromImage(b))
                        DrawStroke(g, e.Points, HighlightColor(e.StrokeColor), e.StrokeWidth * 2.4f, 1f, PointF.Empty);
                    return b;

                case EditKind.Line:
                case EditKind.Arrow:
                case EditKind.Rect:
                case EditKind.Ellipse:
                case EditKind.Text:
                case EditKind.Step:
                    using (var g = Graphics.FromImage(b))
                        DrawEditVector(g, e, 1f, PointF.Empty);
                    return b;

                case EditKind.Crop:
                    {
                        var box = Rectangle.Intersect(e.Box, new Rectangle(0, 0, b.Width, b.Height));
                        if (box.Width < 2 || box.Height < 2) return b;
                        var cut = b.Clone(box, PixelFormat.Format32bppArgb);
                        b.Dispose();
                        return cut;
                    }
                case EditKind.Turn: b.RotateFlip(RotateFlipType.Rotate90FlipNone); return b;
                case EditKind.MirrorX: b.RotateFlip(RotateFlipType.RotateNoneFlipX); return b;
                case EditKind.MirrorY: b.RotateFlip(RotateFlipType.RotateNoneFlipY); return b;
                case EditKind.Frame: return ApplyFrame(b, e.Frame);
            }
            return b;
        }

        static void DrawEditVector(Graphics g, Edit e, float scale, PointF origin)
        {
            var prevSmooth = g.SmoothingMode;
            var prevOffset = g.PixelOffsetMode;
            var prevText = g.TextRenderingHint;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
            try
            {
                float w = Math.Max(1f, e.StrokeWidth * scale);
                PointF a = new PointF(origin.X + e.A.X * scale, origin.Y + e.A.Y * scale);
                PointF b = new PointF(origin.X + e.B.X * scale, origin.Y + e.B.Y * scale);

                switch (e.Kind)
                {
                    case EditKind.Line:
                        using (var p = new Pen(e.StrokeColor, w) { StartCap = LineCap.Round, EndCap = LineCap.Round })
                            g.DrawLine(p, a, b);
                        break;

                    case EditKind.Arrow:
                        {
                            float dx = b.X - a.X, dy = b.Y - a.Y;
                            float dist = (float)Math.Sqrt(dx * dx + dy * dy);
                            if (dist < 1f) break;
                            float ux = dx / dist, uy = dy / dist;
                            float headLen = Math.Min(dist * 0.65f, Math.Max(10f * scale, w * 3.3f));
                            float headHalfW = headLen * 0.44f;
                            PointF shaftEnd = new PointF(b.X - ux * headLen * 0.72f, b.Y - uy * headLen * 0.72f);
                            using (var p = new Pen(e.StrokeColor, w) { StartCap = LineCap.Round, EndCap = LineCap.Flat })
                                g.DrawLine(p, a, shaftEnd);
                            PointF basePt = new PointF(b.X - ux * headLen, b.Y - uy * headLen);
                            PointF left = new PointF(basePt.X - uy * headHalfW, basePt.Y + ux * headHalfW);
                            PointF right = new PointF(basePt.X + uy * headHalfW, basePt.Y - ux * headHalfW);
                            using (var br = new SolidBrush(e.StrokeColor))
                                g.FillPolygon(br, new[] { b, left, right });
                            break;
                        }

                    case EditKind.Rect:
                        {
                            var r = RectangleF.FromLTRB(Math.Min(a.X, b.X), Math.Min(a.Y, b.Y), Math.Max(a.X, b.X), Math.Max(a.Y, b.Y));
                            if (e.Filled)
                                using (var br = new SolidBrush(Color.FromArgb(68, e.StrokeColor))) g.FillRectangle(br, r);
                            using (var p = new Pen(e.StrokeColor, w) { LineJoin = LineJoin.Round })
                                g.DrawRectangle(p, r.X, r.Y, r.Width, r.Height);
                            break;
                        }

                    case EditKind.Ellipse:
                        {
                            var r = RectangleF.FromLTRB(Math.Min(a.X, b.X), Math.Min(a.Y, b.Y), Math.Max(a.X, b.X), Math.Max(a.Y, b.Y));
                            if (e.Filled)
                                using (var br = new SolidBrush(Color.FromArgb(68, e.StrokeColor))) g.FillEllipse(br, r);
                            using (var p = new Pen(e.StrokeColor, w))
                                g.DrawEllipse(p, r);
                            break;
                        }

                    case EditKind.Text:
                        {
                            if (string.IsNullOrEmpty(e.TextValue)) break;
                            float fs = Math.Max(8f, e.FontSize * scale);
                            float px = origin.X + e.Pos.X * scale;
                            float py = origin.Y + e.Pos.Y * scale;
                            using (var f = new Font(Ink.MenuFamily, fs, FontStyle.Bold, GraphicsUnit.Pixel))
                            {
                                var sz = g.MeasureString(e.TextValue, f, PointF.Empty, StringFormat.GenericTypographic);
                                float padX = e.Filled ? fs * 0.45f : fs * 0.15f;
                                float padY = e.Filled ? fs * 0.28f : fs * 0.1f;
                                var box = new RectangleF(px, py, Math.Max(14f * scale, sz.Width + padX * 2f), Math.Max(fs + padY * 2f, sz.Height + padY * 2f));
                                if (e.Filled)
                                {
                                    using (var path = Ink.Round(box, Math.Min(10f * scale, box.Height * 0.28f)))
                                    using (var br = new SolidBrush(Color.FromArgb(240, e.StrokeColor)))
                                        g.FillPath(br, path);
                                    Color ink = ContrastInk(e.StrokeColor);
                                    using (var br = new SolidBrush(ink))
                                        g.DrawString(e.TextValue, f, br, new PointF(px + padX, py + padY), StringFormat.GenericTypographic);
                                }
                                else
                                {
                                    Color shadow = Island.Contrast(Color.Black, e.StrokeColor) >= 2.0 ? Color.FromArgb(120, 0, 0, 0) : Color.FromArgb(120, 255, 255, 255);
                                    float off = Math.Max(1f, fs * 0.06f);
                                    using (var sh = new SolidBrush(shadow))
                                        g.DrawString(e.TextValue, f, sh, new PointF(px + padX + off, py + padY + off), StringFormat.GenericTypographic);
                                    using (var br = new SolidBrush(e.StrokeColor))
                                        g.DrawString(e.TextValue, f, br, new PointF(px + padX, py + padY), StringFormat.GenericTypographic);
                                }
                            }
                            break;
                        }

                    case EditKind.Step:
                        {
                            float r = StepRadius(e) * scale;
                            float cx = origin.X + e.Pos.X * scale;
                            float cy = origin.Y + e.Pos.Y * scale;
                            var shadowRect = new RectangleF(cx - r, cy - r + 1.8f * scale, r * 2f, r * 2f);
                            using (var sh = new SolidBrush(Color.FromArgb(65, 0, 0, 0))) g.FillEllipse(sh, shadowRect);
                            var circle = new RectangleF(cx - r, cy - r, r * 2f, r * 2f);
                            using (var br = new SolidBrush(Color.FromArgb(255, e.StrokeColor))) g.FillEllipse(br, circle);
                            Color ink = ContrastInk(e.StrokeColor);
                            using (var ring = new Pen(Color.FromArgb(170, ink), Math.Max(1f, 1.4f * scale))) g.DrawEllipse(ring, circle);
                            string num = e.StepNumber.ToString();
                            float fs = num.Length > 1 ? r * 0.92f : r * 1.1f;
                            using (var f = new Font(Ink.MenuFamily, Math.Max(8f, fs), FontStyle.Bold, GraphicsUnit.Pixel))
                            using (var br = new SolidBrush(ink))
                            using (var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
                                g.DrawString(num, f, br, new RectangleF(circle.X, circle.Y + 0.5f * scale, circle.Width, circle.Height), sf);
                            break;
                        }
                }
            }
            finally
            {
                g.SmoothingMode = prevSmooth;
                g.PixelOffsetMode = prevOffset;
                g.TextRenderingHint = prevText;
            }
        }

        static Color ContrastInk(Color bg)
        {
            Color white = Color.FromArgb(255, 255, 255, 255);
            Color dark = Color.FromArgb(255, 18, 18, 22);
            return Island.Contrast(white, bg) >= Island.Contrast(dark, bg) ? white : dark;
        }

        static Bitmap ApplyFrame(Bitmap src, FramePreset preset)
        {
            if (preset == FramePreset.None) return src;
            int pad = Math.Max(28, Math.Min(96, (int)Math.Round(Math.Min(src.Width, src.Height) * 0.085f)));
            int outW = src.Width + pad * 2;
            int outH = src.Height + pad * 2;
            float radius = Math.Max(10f, Math.Min(22f, Math.Min(src.Width, src.Height) * 0.025f));

            var dst = new Bitmap(outW, outH, PixelFormat.Format32bppArgb);
            using (var g = Graphics.FromImage(dst))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g.PixelOffsetMode = PixelOffsetMode.HighQuality;

                var full = new RectangleF(0, 0, outW, outH);
                Color c1, c2, c3;
                switch (preset)
                {
                    case FramePreset.Card:
                        c1 = Color.FromArgb(255, 246, 247, 250);
                        c2 = Color.FromArgb(255, 234, 237, 244);
                        c3 = Color.FromArgb(255, 226, 230, 240);
                        break;
                    case FramePreset.BlueGrad:
                        c1 = Color.FromArgb(255, 59, 130, 246);
                        c2 = Color.FromArgb(255, 99, 102, 241);
                        c3 = Color.FromArgb(255, 6, 182, 212);
                        break;
                    case FramePreset.SunsetGrad:
                        c1 = Color.FromArgb(255, 249, 115, 22);
                        c2 = Color.FromArgb(255, 236, 72, 153);
                        c3 = Color.FromArgb(255, 139, 92, 246);
                        break;
                    case FramePreset.MintGrad:
                        c1 = Color.FromArgb(255, 16, 185, 129);
                        c2 = Color.FromArgb(255, 20, 184, 166);
                        c3 = Color.FromArgb(255, 14, 165, 233);
                        break;
                    default: // Dark
                        c1 = Color.FromArgb(255, 32, 36, 44);
                        c2 = Color.FromArgb(255, 21, 24, 30);
                        c3 = Color.FromArgb(255, 13, 15, 19);
                        break;
                }

                using (var lg = new LinearGradientBrush(full, c1, c3, 40f))
                {
                    var cb = new ColorBlend(3);
                    cb.Colors = new[] { c1, c2, c3 };
                    cb.Positions = new[] { 0f, 0.52f, 1f };
                    lg.InterpolationColors = cb;
                    g.FillRectangle(lg, full);
                }

                var imgRect = new RectangleF(pad, pad, src.Width, src.Height);

                // Soft multi-pass drop shadow
                float shY = pad * 0.14f;
                int[] alphas = { 4, 8, 13, 19, 26 };
                float[] spreads = { 1.0f, 0.72f, 0.48f, 0.28f, 0.14f };
                float maxSpread = Math.Max(10f, pad * 0.45f);
                for (int i = 0; i < alphas.Length; i++)
                {
                    float sp = maxSpread * spreads[i];
                    var sr = new RectangleF(imgRect.X - sp * 0.6f, imgRect.Y + shY - sp * 0.35f, imgRect.Width + sp * 1.2f, imgRect.Height + sp * 1.25f);
                    using (var spath = Ink.Round(sr, radius + sp * 0.35f))
                    using (var sb = new SolidBrush(Color.FromArgb(alphas[i], 0, 0, 0)))
                        g.FillPath(sb, spath);
                }

                using (var clipPath = Ink.Round(imgRect, radius))
                {
                    var st = g.Save();
                    g.SetClip(clipPath, CombineMode.Intersect);
                    g.DrawImage(src, imgRect);
                    g.Restore(st);

                    Color rim = preset == FramePreset.Card ? Color.FromArgb(36, 0, 0, 0) : Color.FromArgb(46, 255, 255, 255);
                    using (var pen = new Pen(rim, 1.2f)) g.DrawPath(pen, clipPath);
                }
            }
            src.Dispose();
            return dst;
        }

        internal static void DrawStroke(Graphics g, PointF[] pts, Color color, float width, float scale, PointF origin)
        {
            if (pts == null || pts.Length == 0) return;
            float w = Math.Max(1f, width * scale);
            bool moved = false;
            for (int i = 1; i < pts.Length; i++)
            {
                float dx = pts[i].X - pts[0].X, dy = pts[i].Y - pts[0].Y;
                if (dx * dx + dy * dy > 0.04f) { moved = true; break; }
            }
            var prevSmooth = g.SmoothingMode;
            var prevOffset = g.PixelOffsetMode;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            try
            {
                if (!moved)
                {
                    float cx = origin.X + pts[0].X * scale, cy = origin.Y + pts[0].Y * scale;
                    using (var b = new SolidBrush(color)) g.FillEllipse(b, cx - w / 2f, cy - w / 2f, w, w);
                    return;
                }
                var mapped = new PointF[pts.Length];
                for (int i = 0; i < pts.Length; i++) mapped[i] = new PointF(origin.X + pts[i].X * scale, origin.Y + pts[i].Y * scale);
                using (var pen = new Pen(color, w) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round })
                    g.DrawLines(pen, mapped);
            }
            finally
            {
                g.SmoothingMode = prevSmooth;
                g.PixelOffsetMode = prevOffset;
            }
        }

        void CopyOut()
        {
            if (inlineTextActive) CommitInlineText();
            try { Clipboard.SetImage(work); }
            catch (Exception ex) { Log.Write("redact copy: " + ex.Message); return; }
            var h = Copied;
            if (h != null) h();
            Close();
        }

        void SaveOut()
        {
            if (inlineTextActive) CommitInlineText();
            using (var d = new SaveFileDialog
            {
                Filter = "PNG|*.png|JPEG|*.jpg;*.jpeg|BMP|*.bmp",
                FileName = "gambar-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".png",
                RestoreDirectory = true
            })
            {
                if (d.ShowDialog(this) != DialogResult.OK) return;
                try
                {
                    string ext = (Path.GetExtension(d.FileName) ?? "").ToLowerInvariant();
                    ImageFormat fmt = ImageFormat.Png;
                    if (d.FilterIndex == 2 || ext == ".jpg" || ext == ".jpeg") fmt = ImageFormat.Jpeg;
                    else if (d.FilterIndex == 3 || ext == ".bmp") fmt = ImageFormat.Bmp;

                    if (fmt == ImageFormat.Jpeg || fmt == ImageFormat.Bmp)
                    {
                        using (var flat = new Bitmap(work.Width, work.Height, PixelFormat.Format24bppRgb))
                        using (var g = Graphics.FromImage(flat))
                        {
                            g.Clear(Color.White);
                            g.DrawImageUnscaled(work, 0, 0);
                            flat.Save(d.FileName, fmt);
                        }
                    }
                    else
                    {
                        work.Save(d.FileName, fmt);
                    }
                }
                catch (Exception ex) { Log.Write("redact save: " + ex.Message); return; }
            }
            Close();
        }

        // Blocks too big to read text back
        internal static void Pixelate(Bitmap bmp, Rectangle r)
        {
            r.Intersect(new Rectangle(0, 0, bmp.Width, bmp.Height));
            if (r.Width < 2 || r.Height < 2) return;
            int block = Math.Max(10, Math.Min(48, Math.Min(r.Width, r.Height) / 3));
            var data = bmp.LockBits(r, ImageLockMode.ReadWrite, PixelFormat.Format32bppArgb);
            try
            {
                int stride = data.Stride;
                var buf = new byte[stride * r.Height];
                Marshal.Copy(data.Scan0, buf, 0, buf.Length);
                for (int by = 0; by < r.Height; by += block)
                    for (int bx = 0; bx < r.Width; bx += block)
                    {
                        int ex = Math.Min(r.Width, bx + block), ey = Math.Min(r.Height, by + block);
                        long sb = 0, sg = 0, sr = 0, sa = 0, n = 0;
                        for (int y = by; y < ey; y++)
                            for (int x = bx, i = y * stride + bx * 4; x < ex; x++, i += 4) { sb += buf[i]; sg += buf[i + 1]; sr += buf[i + 2]; sa += buf[i + 3]; n++; }
                        byte b = (byte)(sb / n), gr = (byte)(sg / n), rd = (byte)(sr / n), a = (byte)(sa / n);
                        for (int y = by; y < ey; y++)
                            for (int x = bx, i = y * stride + bx * 4; x < ex; x++, i += 4) { buf[i] = b; buf[i + 1] = gr; buf[i + 2] = rd; buf[i + 3] = a; }
                    }
                Marshal.Copy(buf, 0, data.Scan0, buf.Length);
            }
            finally { bmp.UnlockBits(data); }
        }

        // Fast 2-pass separable LockBits box blur (sliding window O(W * H))
        internal static void BoxBlur(Bitmap bmp, Rectangle r, int radius)
        {
            r.Intersect(new Rectangle(0, 0, bmp.Width, bmp.Height));
            if (r.Width < 2 || r.Height < 2 || radius < 1) return;
            int rad = Math.Max(1, Math.Min(60, radius));
            var data = bmp.LockBits(r, ImageLockMode.ReadWrite, PixelFormat.Format32bppArgb);
            try
            {
                int w = r.Width, h = r.Height, stride = data.Stride;
                var src = new byte[stride * h];
                var tmp = new byte[stride * h];
                Marshal.Copy(data.Scan0, src, 0, src.Length);

                // Pass 1: Horizontal (src -> tmp)
                for (int y = 0; y < h; y++)
                {
                    int row = y * stride;
                    int sb = 0, sg = 0, sr = 0, sa = 0, count = 0;
                    int rightEnd = Math.Min(w - 1, rad);
                    for (int kx = 0; kx <= rightEnd; kx++)
                    {
                        int idx = row + kx * 4;
                        sb += src[idx]; sg += src[idx + 1]; sr += src[idx + 2]; sa += src[idx + 3];
                        count++;
                    }
                    for (int x = 0; x < w; x++)
                    {
                        int outIdx = row + x * 4;
                        tmp[outIdx] = (byte)(sb / count);
                        tmp[outIdx + 1] = (byte)(sg / count);
                        tmp[outIdx + 2] = (byte)(sr / count);
                        tmp[outIdx + 3] = (byte)(sa / count);

                        int remX = x - rad;
                        if (remX >= 0)
                        {
                            int idx = row + remX * 4;
                            sb -= src[idx]; sg -= src[idx + 1]; sr -= src[idx + 2]; sa -= src[idx + 3];
                            count--;
                        }
                        int addX = x + rad + 1;
                        if (addX < w)
                        {
                            int idx = row + addX * 4;
                            sb += src[idx]; sg += src[idx + 1]; sr += src[idx + 2]; sa += src[idx + 3];
                            count++;
                        }
                    }
                }

                // Pass 2: Vertical (tmp -> src)
                for (int x = 0; x < w; x++)
                {
                    int col = x * 4;
                    int sb = 0, sg = 0, sr = 0, sa = 0, count = 0;
                    int botEnd = Math.Min(h - 1, rad);
                    for (int ky = 0; ky <= botEnd; ky++)
                    {
                        int idx = ky * stride + col;
                        sb += tmp[idx]; sg += tmp[idx + 1]; sr += tmp[idx + 2]; sa += tmp[idx + 3];
                        count++;
                    }
                    for (int y = 0; y < h; y++)
                    {
                        int outIdx = y * stride + col;
                        src[outIdx] = (byte)(sb / count);
                        src[outIdx + 1] = (byte)(sg / count);
                        src[outIdx + 2] = (byte)(sr / count);
                        src[outIdx + 3] = (byte)(sa / count);

                        int remY = y - rad;
                        if (remY >= 0)
                        {
                            int idx = remY * stride + col;
                            sb -= tmp[idx]; sg -= tmp[idx + 1]; sr -= tmp[idx + 2]; sa -= tmp[idx + 3];
                            count--;
                        }
                        int addY = y + rad + 1;
                        if (addY < h)
                        {
                            int idx = addY * stride + col;
                            sb += tmp[idx]; sg += tmp[idx + 1]; sr += tmp[idx + 2]; sa += tmp[idx + 3];
                            count++;
                        }
                    }
                }

                Marshal.Copy(src, 0, data.Scan0, src.Length);
            }
            finally { bmp.UnlockBits(data); }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                tips.Dispose();
                if (checkerBrush != null) checkerBrush.Dispose();
                if (checkerBmp != null) checkerBmp.Dispose();
                if (baseWork != null) baseWork.Dispose();
                original.Dispose();
                work.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
