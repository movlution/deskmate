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
        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (!viewport.Contains(e.Location) && !canvas.Contains(e.Location)) return;

            if (inlineTextActive)
                CommitInlineText();

            // Give focus to form so Space/arrow/delete shortcuts work cleanly after button clicks
            if (ActiveControl != null && !inlineTextActive) Focus();

            if (e.Button == MouseButtons.Middle || (e.Button == MouseButtons.Left && spaceDown))
            {
                panning = true;
                panStartMouse = e.Location;
                panStartOffset = pan;
                Capture = true;
                Cursor = Cursors.SizeAll;
                return;
            }

            if (e.Button != MouseButtons.Left) return;

            var t = ActiveTool;
            if (t == ToolMode.Crop)
            {
                HandleCropMouseDown(e.Location);
                return;
            }

            if (t == ToolMode.Select)
            {
                HandleSelectMouseDown(e.Location);
                return;
            }

            if (!canvas.Contains(e.Location)) return;

            if (t == ToolMode.Text)
            {
                OpenInlineText(ToImageF(e.Location), -1);
                return;
            }

            if (t == ToolMode.Step)
            {
                var p = ToImageF(e.Location);
                AddEdit(new Edit
                {
                    Kind = EditKind.Step,
                    Pos = p,
                    StrokeColor = inkColor.Value,
                    StrokeWidth = CurrentStrokeWidth(),
                    StepNumber = NextStepNumber()
                });
                UpdateHint();
                return;
            }

            dragging = true;
            Capture = true;
            if (t == ToolMode.Pen || t == ToolMode.Highlight)
            {
                stroke.Clear();
                stroke.Add(ToImageF(e.Location));
                Invalidate();
                return;
            }
            if (t == ToolMode.Redact)
            {
                var pt = ToImage(e.Location);
                dragStartImg = pt;
                dragBox = Rectangle.Empty;
                return;
            }
            dragStartImg = ToImageF(e.Location);
            dragEndImg = dragStartImg;
            Invalidate();
        }

        protected override void OnMouseDoubleClick(MouseEventArgs e)
        {
            base.OnMouseDoubleClick(e);
            if (e.Button != MouseButtons.Left || !viewport.Contains(e.Location)) return;

            if (ActiveTool == ToolMode.Crop && pendingCrop.Width >= 8 && pendingCrop.Height >= 8)
            {
                if (ToScreen(pendingCrop).Contains(e.Location)) ApplyPendingCrop();
                return;
            }

            if (ActiveTool == ToolMode.Select)
            {
                int hit = HitTestAnnotation(e.Location);
                if (hit >= GeometryBaseIndex() && edits[hit].Kind == EditKind.Text)
                {
                    selectedIndex = hit;
                    OpenInlineText(edits[hit].Pos, hit);
                }
            }
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);

            if (panning)
            {
                pan = new PointF(panStartOffset.X + (e.X - panStartMouse.X), panStartOffset.Y + (e.Y - panStartMouse.Y));
                UpdateCanvas();
                Invalidate();
                return;
            }

            if (ActiveTool == ToolMode.Crop)
            {
                if (dragging) HandleCropMouseMove(e.Location);
                else UpdateViewportCursor(e.Location);
                return;
            }

            if (ActiveTool == ToolMode.Select)
            {
                if (dragging) HandleSelectMouseMove(e.Location);
                else UpdateViewportCursor(e.Location);
                return;
            }

            UpdateViewportCursor(e.Location);
            if (!dragging) return;

            var t = ActiveTool;
            if (t == ToolMode.Pen || t == ToolMode.Highlight)
            {
                var pt = ToImageF(e.Location);
                bool shift = (ModifierKeys & Keys.Shift) != 0;
                if (t == ToolMode.Highlight && shift && stroke.Count > 0)
                {
                    var s0 = stroke[0];
                    stroke.Clear();
                    stroke.Add(s0);
                    stroke.Add(SnapAxis(s0, pt));
                }
                else
                {
                    if (stroke.Count == 0) stroke.Add(pt);
                    else
                    {
                        var last = stroke[stroke.Count - 1];
                        float dx = pt.X - last.X, dy = pt.Y - last.Y, minStep = 0.75f / Math.Max(0.05f, scale);
                        if (dx * dx + dy * dy >= minStep * minStep) stroke.Add(pt);
                    }
                }
                Invalidate();
                return;
            }

            if (t == ToolMode.Redact)
            {
                var p = ToImage(e.Location);
                int fx = (int)Math.Round(dragStartImg.X), fy = (int)Math.Round(dragStartImg.Y);
                dragBox = Rectangle.FromLTRB(Math.Min(fx, p.X), Math.Min(fy, p.Y), Math.Max(fx, p.X), Math.Max(fy, p.Y));
                Invalidate();
                return;
            }

            var cur = ToImageF(e.Location);
            if ((ModifierKeys & Keys.Shift) != 0)
            {
                if (t == ToolMode.Line || t == ToolMode.Arrow) cur = Snap45(dragStartImg, cur);
                else if (t == ToolMode.Rect || t == ToolMode.Ellipse) cur = SnapSquare(dragStartImg, cur);
            }
            dragEndImg = cur;
            Invalidate();
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (panning)
            {
                panning = false;
                Capture = false;
                UpdateViewportCursor(e.Location);
                return;
            }

            if (!dragging) return;
            dragging = false;
            Capture = false;

            var t = ActiveTool;
            if (t == ToolMode.Crop)
            {
                HandleCropMouseUp();
                return;
            }
            if (t == ToolMode.Select)
            {
                HandleSelectMouseUp();
                return;
            }

            if (t == ToolMode.Pen || t == ToolMode.Highlight)
            {
                var pt = ToImageF(e.Location);
                bool shift = (ModifierKeys & Keys.Shift) != 0;
                if (t == ToolMode.Highlight && shift && stroke.Count > 0)
                {
                    var s0 = stroke[0];
                    stroke.Clear();
                    stroke.Add(s0);
                    stroke.Add(SnapAxis(s0, pt));
                }
                else if (stroke.Count == 0) stroke.Add(pt);
                else
                {
                    var last = stroke[stroke.Count - 1];
                    if (Math.Abs(pt.X - last.X) > 0.05f || Math.Abs(pt.Y - last.Y) > 0.05f) stroke.Add(pt);
                }
                var pts = stroke.ToArray();
                stroke.Clear();
                if (pts.Length > 0)
                {
                    AddEdit(new Edit
                    {
                        Kind = t == ToolMode.Highlight ? EditKind.Highlight : EditKind.Pen,
                        Points = pts,
                        StrokeColor = inkColor.Value,
                        StrokeWidth = CurrentStrokeWidth()
                    });
                }
                else Invalidate();
                return;
            }

            if (t == ToolMode.Redact)
            {
                if (dragBox.Width >= 4 && dragBox.Height >= 4)
                {
                    AddEdit(new Edit
                    {
                        Kind = EditKind.Redact,
                        RedactStyle = (RedactMode)Math.Max(0, Math.Min(2, redactMode.Selected)),
                        Box = dragBox,
                        StrokeColor = inkColor.Value
                    });
                }
                else Invalidate();
                return;
            }

            float dx = dragEndImg.X - dragStartImg.X;
            float dy = dragEndImg.Y - dragStartImg.Y;
            if (t == ToolMode.Line || t == ToolMode.Arrow)
            {
                if (dx * dx + dy * dy >= 9f)
                {
                    AddEdit(new Edit
                    {
                        Kind = t == ToolMode.Arrow ? EditKind.Arrow : EditKind.Line,
                        A = dragStartImg,
                        B = dragEndImg,
                        StrokeColor = inkColor.Value,
                        StrokeWidth = CurrentStrokeWidth()
                    });
                }
                else Invalidate();
                return;
            }

            if (t == ToolMode.Rect || t == ToolMode.Ellipse)
            {
                if (Math.Abs(dx) >= 4f && Math.Abs(dy) >= 4f)
                {
                    AddEdit(new Edit
                    {
                        Kind = t == ToolMode.Rect ? EditKind.Rect : EditKind.Ellipse,
                        A = new PointF(Math.Min(dragStartImg.X, dragEndImg.X), Math.Min(dragStartImg.Y, dragEndImg.Y)),
                        B = new PointF(Math.Max(dragStartImg.X, dragEndImg.X), Math.Max(dragStartImg.Y, dragEndImg.Y)),
                        StrokeColor = inkColor.Value,
                        StrokeWidth = CurrentStrokeWidth(),
                        Filled = fillToggle.Checked
                    });
                }
                else Invalidate();
            }
        }

        void UpdateViewportCursor(Point loc)
        {
            if (!viewport.Contains(loc)) { Cursor = Cursors.Default; return; }
            if (spaceDown) { Cursor = Cursors.SizeAll; return; }

            var t = ActiveTool;
            if (t == ToolMode.Crop)
            {
                if (pendingCrop.Width >= 8 && pendingCrop.Height >= 8)
                {
                    var r = ToScreen(pendingCrop);
                    int h = HitRectHandle(r, loc, 8f * Ui);
                    if (h >= 0) { Cursor = CursorForHandle(h); return; }
                    if (r.Contains(loc)) { Cursor = Cursors.SizeAll; return; }
                }
                Cursor = canvas.Contains(loc) ? Cursors.Cross : Cursors.Default;
                return;
            }

            if (t == ToolMode.Select)
            {
                if (selectedIndex >= GeometryBaseIndex() && selectedIndex < edits.Count)
                {
                    var sel = edits[selectedIndex];
                    if (sel.Kind == EditKind.Line || sel.Kind == EditKind.Arrow)
                    {
                        if (DistSq(ToScreen(sel.A), loc) <= 64f * Ui * Ui || DistSq(ToScreen(sel.B), loc) <= 64f * Ui * Ui)
                        {
                            Cursor = Cursors.SizeAll;
                            return;
                        }
                    }
                    else
                    {
                        var sr = ToScreen(GetEditBounds(sel));
                        int h = HitRectHandle(sr, loc, 8f * Ui);
                        bool cornerOnly = !(sel.Kind == EditKind.Rect || sel.Kind == EditKind.Ellipse || sel.Kind == EditKind.Redact);
                        if (h >= 0 && (!cornerOnly || h % 2 == 0))
                        {
                            Cursor = CursorForHandle(h);
                            return;
                        }
                    }
                }
                int hit = HitTestAnnotation(loc);
                Cursor = hit >= 0 ? Cursors.SizeAll : Cursors.Default;
                return;
            }

            if (t == ToolMode.Text)
            {
                Cursor = canvas.Contains(loc) ? Cursors.IBeam : Cursors.Default;
                return;
            }

            Cursor = canvas.Contains(loc) ? Cursors.Cross : Cursors.Default;
        }

        static Cursor CursorForHandle(int h)
        {
            switch (h)
            {
                case 0:
                case 4: return Cursors.SizeNWSE;
                case 2:
                case 6: return Cursors.SizeNESW;
                case 1:
                case 5: return Cursors.SizeNS;
                case 3:
                case 7: return Cursors.SizeWE;
            }
            return Cursors.Default;
        }

        static int HitRectHandle(RectangleF r, Point loc, float tol)
        {
            var pts = GetRectHandles(r);
            float tolSq = tol * tol;
            for (int i = 0; i < pts.Length; i++)
                if (DistSq(pts[i], loc) <= tolSq) return i;
            return -1;
        }

        static float DistSq(PointF a, PointF b)
        {
            float dx = a.X - b.X, dy = a.Y - b.Y;
            return dx * dx + dy * dy;
        }
    }
}
