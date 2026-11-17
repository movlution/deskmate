using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace Deskmate
{
    // Sits inside the settings window as a child control: a child can never take
    // focus away from the text box the way a separate top-level window did
    class CitySuggest : Panel, IMessageFilter
    {
        readonly float ui;
        string[] items = new string[0];
        int hot = -1;
        Action<string> picked;
        Control anchorCtl;
        bool filtering;

        // A click inside the card belongs to a row; anywhere else dismisses the list
        public bool ContainsScreen(Point pt)
        {
            return Visible && RectangleToScreen(ClientRectangle).Contains(pt);
        }

        void Hook()
        {
            if (filtering) return;
            Application.AddMessageFilter(this);
            filtering = true;
        }

        void Unhook()
        {
            if (!filtering) return;
            Application.RemoveMessageFilter(this);
            filtering = false;
        }

        // The box loses focus the moment the pointer goes down on the list, and the old
        // Leave handler hid the card before the click could land on a row. Filtering the
        // press lets a row keep its click and still closes the list on an outside press.
        public bool PreFilterMessage(ref Message m)
        {
            if (!Visible) return false;
            if (m.Msg != 0x0201 && m.Msg != 0x0204 && m.Msg != 0x0207) return false;
            return OutsidePress(ScreenOf(m.LParam));
        }

        // Screen point carried by a mouse message: low word x, high word y, signed
        static Point ScreenOf(IntPtr l)
        {
            int v = l.ToInt32();
            return new Point((short)(v & 0xFFFF), (short)((v >> 16) & 0xFFFF));
        }

        // True when the press landed outside both the list and the box it belongs to
        internal bool OutsidePress(Point screen)
        {
            if (ContainsScreen(screen)) return false;
            var a = anchorCtl;
            if (a != null && !a.IsDisposed && a.RectangleToScreen(a.ClientRectangle).Contains(screen)) return false;
            Hide2();
            return false; // never swallow: the press still lands where it was aimed
        }

        protected override void OnVisibleChanged(EventArgs e)
        {
            base.OnVisibleChanged(e);
            if (Visible) Hook(); else Unhook();
        }

        public CitySuggest(float ui)
        {
            this.ui = ui;
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            BackColor = KitInk.Window;
            // Never a tab stop, never focusable, so typing never leaves the box
            TabStop = false;
            SetStyle(ControlStyles.Selectable, false);
            Visible = false;
        }

        protected override bool IsInputKey(Keys key) { return false; }

        public void Bind(string[] names)
        {
            items = names ?? new string[0];
            hot = -1;
            Height = (int)((Math.Min(items.Length, 8) * 30 + 8) * ui);
            Invalidate();
        }

        // The anchor sits several panels deep, so convert through screen space
        public void ShowUnder(Control anchor, Action<string> onPick)
        {
            picked = onPick;
            anchorCtl = anchor;
            var host = Parent;
            if (host == null) return;
            var at = host.PointToClient(anchor.PointToScreen(Point.Empty));
            // Wide enough for a full "City · Province" label, but never wider than the page
            int room = Math.Max((int)(160 * ui), host.ClientSize.Width - 16);
            Width = Math.Min(Math.Max(anchor.Width, (int)(240 * ui)), room);
            // Flush with one edge of the field it belongs to: left when it fits, else right,
            // so a clamped list still reads as attached to its box instead of floating off it
            int x = at.X;
            if (x + Width > host.ClientSize.Width - 8) x = Math.Max(8, at.X + anchor.Width - Width);
            // Last guard: whatever the anchor did, the list stays on the page
            if (x + Width > host.ClientSize.Width - 8) x = Math.Max(0, host.ClientSize.Width - 8 - Width);
            int y = at.Y + anchor.Height + (int)(4 * ui);
            if (y + Height > host.ClientSize.Height - 8) y = Math.Max(4, at.Y - Height - (int)(4 * ui));
            Location = new Point(x, y);
            Visible = true;
            BringToFront();
            Invalidate();
        }

        public void Hide2() { Visible = false; hot = -1; }

        public void Step(int delta)
        {
            if (items.Length == 0) return;
            if (hot < 0) hot = delta > 0 ? 0 : items.Length - 1;
            else hot = (hot + delta + items.Length) % items.Length;
            Invalidate();
        }

        public string Selected()
        {
            if (items.Length == 0) return null;
            return hot >= 0 && hot < items.Length ? items[hot] : items[0];
        }

        protected override void OnPaintBackground(PaintEventArgs e) { }

        // Hover band for a row: inset from the card so it never touches the rim
        RectangleF RowRect(int i)
        {
            float pad = 4 * ui;
            return new RectangleF(pad, i * 30 * ui + pad, Width - pad * 2, 30 * ui);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            KitInk.Smooth(g);
            // The hairline sits half a pixel inside the bounds: a pen is centred on its
            // path, so stroking the full rectangle would clip half the width and render
            // a smeared grey edge instead of a crisp line
            var card = new RectangleF(0.5f, 0.5f, Width - 1, Height - 1);
            using (var path = Ink.Round(card, 10 * ui))
            {
                using (var b = new SolidBrush(KitInk.Window)) g.FillPath(b, path);
                // Rows are clipped to the card, so a hover band cannot square off the corners
                var old = g.Clip;
                g.SetClip(path);
                using (var f = new Font(Ink.MenuFamily, 12.5f * ui, GraphicsUnit.Pixel))
                {
                    for (int i = 0; i < items.Length; i++)
                    {
                        var r = RowRect(i);
                        if (i == hot)
                            using (var hb = new SolidBrush(KitInk.Hover))
                            using (var hp = Ink.Round(r, 7 * ui))
                                g.FillPath(hb, hp);
                        // Centred by the shared text helper, which also ellipsizes a long label
                        KitInk.Draw(g, items[i], f, KitInk.Text,
                            new RectangleF(r.X + 10 * ui, r.Y, r.Width - 20 * ui, r.Height), ContentAlignment.MiddleLeft);
                    }
                }
                g.Clip = old;
                using (var pen = new Pen(KitInk.Line, 1f)) g.DrawPath(pen, path);
            }
        }

        int RowAt(Point p)
        {
            int i = (int)((p.Y / ui - 4) / 30);
            return i >= 0 && i < items.Length ? i : -1;
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            int at = RowAt(e.Location);
            if (at != hot) { hot = at; Invalidate(); }
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            if (hot != -1) { hot = -1; Invalidate(); }
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            int at = RowAt(e.Location);
            if (at < 0 || picked == null) return;
            string pick = items[at];
            Visible = false;
            picked(pick);
        }
    }
}
