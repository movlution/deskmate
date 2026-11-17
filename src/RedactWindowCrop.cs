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
        // Crop handlers
        void HandleCropMouseDown(Point loc)
        {
            if (pendingCrop.Width >= 8 && pendingCrop.Height >= 8)
            {
                var r = ToScreen(pendingCrop);
                int h = HitRectHandle(r, loc, 9f * Ui);
                if (h >= 0)
                {
                    dragging = true;
                    Capture = true;
                    cropDragHandle = h;
                    cropOrigBox = pendingCrop;
                    dragStartImg = ToImage(loc);
                    return;
                }
                if (r.Contains(loc))
                {
                    dragging = true;
                    Capture = true;
                    cropDragHandle = 8;
                    cropOrigBox = pendingCrop;
                    dragStartImg = ToImage(loc);
                    return;
                }
            }
            if (!canvas.Contains(loc)) return;
            dragging = true;
            Capture = true;
            cropDragHandle = 9;
            dragStartImg = ToImage(loc);
            pendingCrop = Rectangle.Empty;
            UpdateContextBar();
            LayoutControls();
            Invalidate();
        }

        void HandleCropMouseMove(Point loc)
        {
            var pt = ToImage(loc);
            if (cropDragHandle == 9)
            {
                int sx = (int)dragStartImg.X, sy = (int)dragStartImg.Y;
                pendingCrop = Rectangle.FromLTRB(Math.Min(sx, pt.X), Math.Min(sy, pt.Y), Math.Max(sx, pt.X), Math.Max(sy, pt.Y));
            }
            else if (cropDragHandle == 8)
            {
                int dx = pt.X - (int)dragStartImg.X;
                int dy = pt.Y - (int)dragStartImg.Y;
                int nx = Math.Max(0, Math.Min(work.Width - cropOrigBox.Width, cropOrigBox.X + dx));
                int ny = Math.Max(0, Math.Min(work.Height - cropOrigBox.Height, cropOrigBox.Y + dy));
                pendingCrop = new Rectangle(nx, ny, cropOrigBox.Width, cropOrigBox.Height);
            }
            else if (cropDragHandle >= 0 && cropDragHandle <= 7)
            {
                pendingCrop = ResizeRectByHandle(cropOrigBox, cropDragHandle, pt, work.Width, work.Height, 8);
            }
            Invalidate();
        }

        void HandleCropMouseUp()
        {
            cropDragHandle = -1;
            if (pendingCrop.Width < 8 || pendingCrop.Height < 8)
                pendingCrop = Rectangle.Empty;
            UpdateContextBar();
            LayoutControls();
            Invalidate();
        }

        static Rectangle ResizeRectByHandle(Rectangle orig, int h, Point pt, int maxW, int maxH, int minSz)
        {
            int l = orig.Left, t = orig.Top, r = orig.Right, b = orig.Bottom;
            if (h == 0 || h == 6 || h == 7) l = Math.Max(0, Math.Min(r - minSz, pt.X));
            if (h == 2 || h == 3 || h == 4) r = Math.Min(maxW, Math.Max(l + minSz, pt.X));
            if (h == 0 || h == 1 || h == 2) t = Math.Max(0, Math.Min(b - minSz, pt.Y));
            if (h == 4 || h == 5 || h == 6) b = Math.Min(maxH, Math.Max(t + minSz, pt.Y));
            return Rectangle.FromLTRB(l, t, r, b);
        }

        void ApplyPendingCrop()
        {
            if (pendingCrop.Width < 8 || pendingCrop.Height < 8) return;
            var box = Rectangle.Intersect(pendingCrop, new Rectangle(0, 0, work.Width, work.Height));
            pendingCrop = Rectangle.Empty;
            if (box.Width >= 8 && box.Height >= 8)
                AddEdit(new Edit { Kind = EditKind.Crop, Box = box });
            UpdateContextBar();
            LayoutControls();
            Invalidate();
        }

        void ClearPendingCrop()
        {
            if (pendingCrop.IsEmpty) return;
            pendingCrop = Rectangle.Empty;
            UpdateContextBar();
            LayoutControls();
            Invalidate();
        }
    }
}
