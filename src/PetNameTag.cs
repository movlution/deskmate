using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.Windows.Forms;

namespace Deskmate
{
    // In a pet room every pet wears its owner's name above its head, so a desk full of
    // cats can be told apart: a friend's pet shows the friend's name, this one the user's own.
    partial class Island
    {
        void RefreshPetNameTags()
        {
            if (pet != null && !pet.IsDisposed) pet.RefreshNameTag();
            foreach (var w in remotePets.Values)
                if (!w.IsDisposed) w.RefreshNameTag();
        }

        sealed partial class PetWindow
        {
            PetNameTag nameTag;

            string NameTagText
            {
                get
                {
                    if (Isle == null || !Isle.PetRoomConnected) return null;
                    string n = IsRemote ? PeerName : Isle.PetDisplayName;
                    return string.IsNullOrEmpty(n) ? Lang.T("Teman") : n;
                }
            }

            public void RefreshNameTag() { PlaceNameTag(); }

            void PlaceNameTag()
            {
                string text = closing || hidden || !placed || sheet == null || drawnX == int.MinValue ? null : NameTagText;
                // A bubble or bar above the head already covers that spot (and a chat bubble names the sender)
                if (text != null && ((chatBubble != null && !chatBubble.IsDisposed) || actionBar != null || wellness != null)) text = null;
                if (text == null)
                {
                    if (nameTag != null && !nameTag.IsDisposed) nameTag.Conceal();
                    return;
                }
                if (nameTag == null || nameTag.IsDisposed) nameTag = new PetNameTag();
                int gap = Math.Max(2, (int)Math.Round(3 * scale));
                nameTag.Put(text, scale, DrawnCenterX, drawnY + TopPx - gap, this);
            }

            void CloseNameTag()
            {
                if (nameTag != null && !nameTag.IsDisposed) { nameTag.Close(); nameTag.Dispose(); }
                nameTag = null;
            }
        }

        // A small dark pill with the name. Click-through, never takes focus; redrawn only when the text or scale changes.
        internal sealed class PetNameTag : Form
        {
            IntPtr memDC, hbmp, oldBmp;
            string text;
            float scale;
            int w, h, x = int.MinValue, y = int.MinValue;
            bool visible;

            // For the passive check: the drawn pill and whether it is on screen
            internal Size TagSize { get { return new Size(w, h); } }
            internal bool Showing { get { return visible; } }

            public PetNameTag()
            {
                FormBorderStyle = FormBorderStyle.None;
                ShowInTaskbar = false;
                StartPosition = FormStartPosition.Manual;
                AutoScaleMode = AutoScaleMode.None;
                Text = "Deskmate Pet Name";
                Bounds = new Rectangle(-32000, -32000, 1, 1);
            }

            protected override CreateParams CreateParams
            {
                get
                {
                    var cp = base.CreateParams;
                    cp.Style = Native.WS_POPUP;
                    cp.ExStyle |= Native.WS_EX_LAYERED | Native.WS_EX_TRANSPARENT | Native.WS_EX_TOOLWINDOW | Native.WS_EX_NOACTIVATE | Native.WS_EX_TOPMOST;
                    return cp;
                }
            }

            protected override bool ShowWithoutActivation { get { return true; } }
            protected override void OnPaintBackground(PaintEventArgs e) { }
            protected override void OnPaint(PaintEventArgs e) { }

            protected override void WndProc(ref Message m)
            {
                if (m.Msg == Native.WM_MOUSEACTIVATE) { m.Result = (IntPtr)Native.MA_NOACTIVATE; return; }
                base.WndProc(ref m);
            }

            // centerX, bottom: screen px of the spot just above the pet's head
            public void Put(string text, float scale, int centerX, int bottom, Form owner)
            {
                bool art = text != this.text || scale != this.scale || memDC == IntPtr.Zero;
                if (art) Build(text, scale);
                if (memDC == IntPtr.Zero) return;
                int nx = centerX - w / 2, ny = bottom - h;
                if (!IsHandleCreated) Show(owner);
                if (art || nx != x || ny != y)
                {
                    var dst = new Native.POINT(nx, ny);
                    var size = new Native.SIZE(w, h);
                    var src = new Native.POINT(0, 0);
                    var blend = new Native.BLENDFUNCTION { SourceConstantAlpha = 255, AlphaFormat = 1 };
                    Native.UpdateLayeredWindow(Handle, IntPtr.Zero, ref dst, ref size, memDC, ref src, 0, ref blend, Native.ULW_ALPHA);
                    x = nx;
                    y = ny;
                }
                if (!visible) { Native.ShowWindow(Handle, Native.SW_SHOWNOACTIVATE); visible = true; }
            }

            public void Conceal()
            {
                if (!visible) return;
                visible = false;
                if (IsHandleCreated) Native.ShowWindow(Handle, Native.SW_HIDE);
            }

            void Build(string text, float scale)
            {
                Free();
                this.text = text;
                this.scale = scale;
                Font font = Ink.Semi(11f * scale);
                using (var sf = new StringFormat(StringFormat.GenericTypographic) { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center, FormatFlags = StringFormatFlags.NoWrap })
                {
                    SizeF t;
                    using (var probe = new Bitmap(1, 1))
                    using (var pg = Graphics.FromImage(probe))
                    {
                        pg.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
                        t = pg.MeasureString(text, font, PointF.Empty, sf);
                    }
                    int padX = Math.Max(4, (int)Math.Round(7 * scale)), padY = Math.Max(1, (int)Math.Round(2 * scale));
                    w = (int)Math.Ceiling(t.Width) + padX * 2;
                    h = (int)Math.Ceiling(Math.Max(t.Height, font.Height)) + padY * 2;
                    using (var bmp = new Bitmap(w, h, PixelFormat.Format32bppArgb))
                    {
                        using (var g = Graphics.FromImage(bmp))
                        {
                            g.SmoothingMode = SmoothingMode.AntiAlias;
                            g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
                            g.Clear(Color.Transparent);
                            float r = (h - 1) / 2f;
                            using (var path = new GraphicsPath())
                            {
                                path.AddArc(0.5f, 0.5f, r * 2, r * 2, 90, 180);
                                path.AddArc(w - 1.5f - r * 2, 0.5f, r * 2, r * 2, 270, 180);
                                path.CloseFigure();
                                using (var fill = new SolidBrush(Color.FromArgb(205, 22, 22, 26)))
                                    g.FillPath(fill, path);
                                using (var edge = new Pen(Color.FromArgb(60, 255, 255, 255), 1f))
                                    g.DrawPath(edge, path);
                            }
                            using (var ink = new SolidBrush(Color.White))
                                g.DrawString(text, font, ink, new RectangleF(0, 0, w, h), sf);
                        }
                        hbmp = bmp.GetHbitmap(Color.FromArgb(0));
                    }
                }
                IntPtr screen = Native.GetDC(IntPtr.Zero);
                memDC = Native.CreateCompatibleDC(screen);
                Native.ReleaseDC(IntPtr.Zero, screen);
                oldBmp = Native.SelectObject(memDC, hbmp);
            }

            void Free()
            {
                if (memDC == IntPtr.Zero) return;
                Native.SelectObject(memDC, oldBmp);
                Native.DeleteObject(hbmp);
                Native.DeleteDC(memDC);
                memDC = hbmp = oldBmp = IntPtr.Zero;
            }

            protected override void OnFormClosed(FormClosedEventArgs e)
            {
                Free();
                base.OnFormClosed(e);
            }
        }
    }
}
