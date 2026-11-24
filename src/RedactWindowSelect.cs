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
        // Select handlers
        void HandleSelectMouseDown(Point loc)
        {
            int baseIdx = GeometryBaseIndex();
            selectDrag = SelectDragKind.None;
            selectHandle = -1;
            selectModified = false;

            if (selectedIndex >= baseIdx && selectedIndex < edits.Count)
            {
                var sel = edits[selectedIndex];
                if (sel.Kind == EditKind.Line || sel.Kind == EditKind.Arrow)
                {
                    if (DistSq(ToScreen(sel.A), loc) <= 64f * Ui * Ui) selectDrag = SelectDragKind.EndpointA;
                    else if (DistSq(ToScreen(sel.B), loc) <= 64f * Ui * Ui) selectDrag = SelectDragKind.EndpointB;
                }
                else
                {
                    var sr = ToScreen(GetEditBounds(sel));
                    int h = HitRectHandle(sr, loc, 8f * Ui);
                    bool cornerOnly = !(sel.Kind == EditKind.Rect || sel.Kind == EditKind.Ellipse || sel.Kind == EditKind.Redact);
                    if (h >= 0 && (!cornerOnly || h % 2 == 0))
                    {
                        selectDrag = SelectDragKind.Handle;
                        selectHandle = h;
                    }
                }
            }

            if (selectDrag == SelectDragKind.None)
            {
                int hit = HitTestAnnotation(loc);
                if (hit >= baseIdx)
                {
                    selectedIndex = hit;
                    selectDrag = SelectDragKind.Move;
                }
                else
                {
                    selectedIndex = -1;
                    UpdateContextBar();
                    LayoutControls();
                    Invalidate();
                    return;
                }
            }

            var target = edits[selectedIndex];
            selectOrigEdit = target.Clone();
            selectOrigBounds = GetEditBounds(target);
            selectUndoSnap = CloneEdits(edits);
            dragStartImg = ToImageUnclampedF(loc);
            dragging = true;
            Capture = true;

            SyncControlsToSelection(target);
            UpdateContextBar();
            LayoutControls();
            Invalidate();
        }

        void SyncControlsToSelection(Edit sel)
        {
            if (sel.Kind != EditKind.Redact || sel.RedactStyle == RedactMode.Block)
                inkColor.Value = Color.FromArgb(255, sel.StrokeColor.R, sel.StrokeColor.G, sel.StrokeColor.B);
            if (sel.Kind == EditKind.Rect || sel.Kind == EditKind.Ellipse || sel.Kind == EditKind.Text)
                fillToggle.Checked = sel.Filled;
        }

        void HandleSelectMouseMove(Point loc)
        {
            if (selectedIndex < GeometryBaseIndex() || selectedIndex >= edits.Count) return;
            var cur = ToImageUnclampedF(loc);
            float dx = cur.X - dragStartImg.X;
            float dy = cur.Y - dragStartImg.Y;
            if (Math.Abs(dx) > 0.25f || Math.Abs(dy) > 0.25f) selectModified = true;

            var e = edits[selectedIndex];
            bool shift = (ModifierKeys & Keys.Shift) != 0;

            if (selectDrag == SelectDragKind.Move)
            {
                if (e.Kind == EditKind.Line || e.Kind == EditKind.Arrow || e.Kind == EditKind.Rect || e.Kind == EditKind.Ellipse)
                {
                    e.A = new PointF(selectOrigEdit.A.X + dx, selectOrigEdit.A.Y + dy);
                    e.B = new PointF(selectOrigEdit.B.X + dx, selectOrigEdit.B.Y + dy);
                }
                else if (e.Kind == EditKind.Redact)
                {
                    int nx = Math.Max(0, Math.Min(work.Width - selectOrigEdit.Box.Width, selectOrigEdit.Box.X + (int)Math.Round(dx)));
                    int ny = Math.Max(0, Math.Min(work.Height - selectOrigEdit.Box.Height, selectOrigEdit.Box.Y + (int)Math.Round(dy)));
                    e.Box = new Rectangle(nx, ny, selectOrigEdit.Box.Width, selectOrigEdit.Box.Height);
                }
                else if (e.Kind == EditKind.Text || e.Kind == EditKind.Step)
                {
                    e.Pos = new PointF(selectOrigEdit.Pos.X + dx, selectOrigEdit.Pos.Y + dy);
                }
                else if (e.Kind == EditKind.Pen || e.Kind == EditKind.Highlight)
                {
                    for (int i = 0; i < e.Points.Length; i++)
                        e.Points[i] = new PointF(selectOrigEdit.Points[i].X + dx, selectOrigEdit.Points[i].Y + dy);
                }
            }
            else if (selectDrag == SelectDragKind.EndpointA)
            {
                PointF target = new PointF(selectOrigEdit.A.X + dx, selectOrigEdit.A.Y + dy);
                e.A = shift ? Snap45(e.B, target) : target;
            }
            else if (selectDrag == SelectDragKind.EndpointB)
            {
                PointF target = new PointF(selectOrigEdit.B.X + dx, selectOrigEdit.B.Y + dy);
                e.B = shift ? Snap45(e.A, target) : target;
            }
            else if (selectDrag == SelectDragKind.Handle)
            {
                ResizeEditByHandle(e, selectOrigEdit, selectOrigBounds, selectHandle, cur, shift);
            }

            RebuildWork(false);
            Invalidate();
        }

        void HandleSelectMouseUp()
        {
            if (selectModified && selectUndoSnap != null)
            {
                undoStack.Push(selectUndoSnap);
                redoStack.Clear();
                UpdateActionButtons();
            }
            selectUndoSnap = null;
            selectDrag = SelectDragKind.None;
            selectHandle = -1;
            Invalidate();
        }

        void ResizeEditByHandle(Edit e, Edit orig, RectangleF ob, int h, PointF pt, bool shift)
        {
            float l = ob.Left, t = ob.Top, r = ob.Right, b = ob.Bottom;
            float minSz = e.Kind == EditKind.Redact ? 4f : 8f;
            if (h == 0 || h == 6 || h == 7) l = Math.Min(r - minSz, pt.X);
            if (h == 2 || h == 3 || h == 4) r = Math.Max(l + minSz, pt.X);
            if (h == 0 || h == 1 || h == 2) t = Math.Min(b - minSz, pt.Y);
            if (h == 4 || h == 5 || h == 6) b = Math.Max(t + minSz, pt.Y);

            if (shift && (h == 0 || h == 2 || h == 4 || h == 6) && (e.Kind == EditKind.Rect || e.Kind == EditKind.Ellipse))
            {
                float side = Math.Max(r - l, b - t);
                if (h == 0 || h == 6) l = r - side; else r = l + side;
                if (h == 0 || h == 2) t = b - side; else b = t + side;
            }

            var nb = RectangleF.FromLTRB(l, t, r, b);
            if (e.Kind == EditKind.Rect || e.Kind == EditKind.Ellipse)
            {
                e.A = new PointF(nb.Left, nb.Top);
                e.B = new PointF(nb.Right, nb.Bottom);
            }
            else if (e.Kind == EditKind.Redact)
            {
                var clamped = Rectangle.Intersect(Rectangle.Round(nb), new Rectangle(0, 0, work.Width, work.Height));
                if (clamped.Width >= 4 && clamped.Height >= 4) e.Box = clamped;
            }
            else if (e.Kind == EditKind.Pen || e.Kind == EditKind.Highlight)
            {
                float ow = Math.Max(1f, ob.Width), oh = Math.Max(1f, ob.Height);
                for (int i = 0; i < e.Points.Length; i++)
                {
                    float nx = nb.Left + (orig.Points[i].X - ob.Left) / ow * nb.Width;
                    float ny = nb.Top + (orig.Points[i].Y - ob.Top) / oh * nb.Height;
                    e.Points[i] = new PointF(nx, ny);
                }
            }
            else if (e.Kind == EditKind.Text)
            {
                float ratio = nb.Height / Math.Max(1f, ob.Height);
                e.FontSize = Math.Max(10f, Math.Min(160f, orig.FontSize * ratio));
                e.Pos = new PointF(nb.Left, nb.Top);
            }
            else if (e.Kind == EditKind.Step)
            {
                float ratio = Math.Max(nb.Width, nb.Height) / Math.Max(1f, Math.Max(ob.Width, ob.Height));
                e.StrokeWidth = Math.Max(3f, Math.Min(36f, orig.StrokeWidth * ratio));
                e.Pos = new PointF(nb.Left + nb.Width / 2f, nb.Top + nb.Height / 2f);
            }
        }

        int HitTestAnnotation(Point screenPt)
        {
            var pt = ToImageUnclampedF(screenPt);
            float pad = 7f / Math.Max(0.05f, scale);
            int baseIdx = GeometryBaseIndex();
            for (int i = edits.Count - 1; i >= baseIdx; i--)
            {
                if (HitTestEdit(edits[i], pt, pad)) return i;
            }
            return -1;
        }

        static bool HitTestEdit(Edit e, PointF pt, float pad)
        {
            switch (e.Kind)
            {
                case EditKind.Line:
                case EditKind.Arrow:
                    return DistPointToSegment(pt, e.A, e.B) <= Math.Max(pad, e.StrokeWidth * 0.75f);
                case EditKind.Rect:
                    {
                        var r = RectangleF.FromLTRB(Math.Min(e.A.X, e.B.X), Math.Min(e.A.Y, e.B.Y), Math.Max(e.A.X, e.B.X), Math.Max(e.A.Y, e.B.Y));
                        float tol = Math.Max(pad, e.StrokeWidth * 0.7f);
                        if (e.Filled) return RectangleF.Inflate(r, tol, tol).Contains(pt);
                        if (!RectangleF.Inflate(r, tol, tol).Contains(pt)) return false;
                        return !RectangleF.Inflate(r, -tol, -tol).Contains(pt);
                    }
                case EditKind.Ellipse:
                    {
                        var r = RectangleF.FromLTRB(Math.Min(e.A.X, e.B.X), Math.Min(e.A.Y, e.B.Y), Math.Max(e.A.X, e.B.X), Math.Max(e.A.Y, e.B.Y));
                        float rx = r.Width / 2f, ry = r.Height / 2f;
                        if (rx < 1f || ry < 1f) return false;
                        float cx = r.X + rx, cy = r.Y + ry;
                        float tol = Math.Max(pad, e.StrokeWidth * 0.7f);
                        float nxOut = (pt.X - cx) / (rx + tol), nyOut = (pt.Y - cy) / (ry + tol);
                        if (nxOut * nxOut + nyOut * nyOut > 1f) return false;
                        if (e.Filled || rx <= tol || ry <= tol) return true;
                        float nxIn = (pt.X - cx) / Math.Max(0.5f, rx - tol), nyIn = (pt.Y - cy) / Math.Max(0.5f, ry - tol);
                        return nxIn * nxIn + nyIn * nyIn >= 1f;
                    }
                case EditKind.Redact:
                    return RectangleF.Inflate(e.Box, pad, pad).Contains(pt);
                case EditKind.Pen:
                case EditKind.Highlight:
                    {
                        if (e.Points == null || e.Points.Length == 0) return false;
                        float tol = Math.Max(pad, e.StrokeWidth * (e.Kind == EditKind.Highlight ? 1.3f : 0.75f));
                        if (e.Points.Length == 1) return DistSq(pt, e.Points[0]) <= tol * tol;
                        for (int i = 1; i < e.Points.Length; i++)
                            if (DistPointToSegment(pt, e.Points[i - 1], e.Points[i]) <= tol) return true;
                        return false;
                    }
                case EditKind.Text:
                    return RectangleF.Inflate(GetEditBounds(e), pad, pad).Contains(pt);
                case EditKind.Step:
                    {
                        float r = StepRadius(e) + pad;
                        return DistSq(pt, e.Pos) <= r * r;
                    }
            }
            return false;
        }

        static float DistPointToSegment(PointF p, PointF a, PointF b)
        {
            float dx = b.X - a.X, dy = b.Y - a.Y;
            float lenSq = dx * dx + dy * dy;
            if (lenSq < 0.0001f) return (float)Math.Sqrt(DistSq(p, a));
            float t = Math.Max(0f, Math.Min(1f, ((p.X - a.X) * dx + (p.Y - a.Y) * dy) / lenSq));
            float px = a.X + t * dx, py = a.Y + t * dy;
            return (float)Math.Sqrt((p.X - px) * (p.X - px) + (p.Y - py) * (p.Y - py));
        }

        static RectangleF GetEditBounds(Edit e)
        {
            switch (e.Kind)
            {
                case EditKind.Line:
                case EditKind.Arrow:
                case EditKind.Rect:
                case EditKind.Ellipse:
                    return RectangleF.FromLTRB(Math.Min(e.A.X, e.B.X), Math.Min(e.A.Y, e.B.Y), Math.Max(e.A.X, e.B.X), Math.Max(e.A.Y, e.B.Y));
                case EditKind.Redact:
                    return e.Box;
                case EditKind.Pen:
                case EditKind.Highlight:
                    {
                        if (e.Points == null || e.Points.Length == 0) return RectangleF.Empty;
                        float x0 = e.Points[0].X, y0 = e.Points[0].Y, x1 = x0, y1 = y0;
                        for (int i = 1; i < e.Points.Length; i++)
                        {
                            if (e.Points[i].X < x0) x0 = e.Points[i].X;
                            if (e.Points[i].X > x1) x1 = e.Points[i].X;
                            if (e.Points[i].Y < y0) y0 = e.Points[i].Y;
                            if (e.Points[i].Y > y1) y1 = e.Points[i].Y;
                        }
                        float hw = (e.Kind == EditKind.Highlight ? e.StrokeWidth * 2.4f : e.StrokeWidth) * 0.5f;
                        return RectangleF.FromLTRB(x0 - hw, y0 - hw, x1 + hw, y1 + hw);
                    }
                case EditKind.Text:
                    return MeasureTextBounds(e);
                case EditKind.Step:
                    {
                        float r = StepRadius(e);
                        return new RectangleF(e.Pos.X - r, e.Pos.Y - r, r * 2f, r * 2f);
                    }
            }
            return RectangleF.Empty;
        }

        static float StepRadius(Edit e)
        {
            return Math.Max(11f, e.StrokeWidth * 2.1f);
        }

        static RectangleF MeasureTextBounds(Edit e)
        {
            float fs = Math.Max(10f, e.FontSize);
            string txt = string.IsNullOrEmpty(e.TextValue) ? " " : e.TextValue;
            using (var bmp = new Bitmap(1, 1, PixelFormat.Format32bppArgb))
            using (var g = Graphics.FromImage(bmp))
            using (var f = new Font(Ink.MenuFamily, fs, FontStyle.Bold, GraphicsUnit.Pixel))
            {
                var sz = g.MeasureString(txt, f, PointF.Empty, StringFormat.GenericTypographic);
                float padX = e.Filled ? fs * 0.45f : fs * 0.15f;
                float padY = e.Filled ? fs * 0.28f : fs * 0.1f;
                return new RectangleF(e.Pos.X, e.Pos.Y, Math.Max(14f, sz.Width + padX * 2f), Math.Max(fs + padY * 2f, sz.Height + padY * 2f));
            }
        }
    }
}
