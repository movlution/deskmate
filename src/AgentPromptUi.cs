using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows.Forms;

namespace Deskmate
{
    partial class Island
    {
        // Placeholder painted inside the edit
        class Cue : NativeWindow
        {
            struct RECT { public int Left, Top, Right, Bottom; }
            [DllImport("user32.dll")] static extern IntPtr SendMessage(IntPtr h, int msg, IntPtr w, ref RECT r);
            [DllImport("user32.dll")] static extern bool HideCaret(IntPtr h);
            [DllImport("user32.dll")] static extern bool ShowCaret(IntPtr h);

            readonly TextBox box;
            public string Text = "";
            bool empty = true;

            public Cue(TextBox box)
            {
                this.box = box;
                box.HandleCreated += delegate { AssignHandle(box.Handle); };
                box.HandleDestroyed += delegate { ReleaseHandle(); };
                // Edits repaint only the changed run
                box.TextChanged += delegate { bool now = box.TextLength == 0; if (now != empty) { empty = now; box.Invalidate(); } };
                if (box.IsHandleCreated) AssignHandle(box.Handle);
            }

            protected override void WndProc(ref Message m)
            {
                base.WndProc(ref m);
                // WM_PAINT, and WM_PRINTCLIENT for snapshots
                if ((m.Msg != 0x000F && m.Msg != 0x0318) || box.TextLength > 0 || Text.Length == 0) return;
                var r = new RECT();
                SendMessage(Handle, 0x00B2, IntPtr.Zero, ref r);
                var rect = Rectangle.FromLTRB(r.Left, r.Top, Math.Max(r.Left + 1, r.Right), r.Top + box.Font.Height + 2);
                var flags = TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis;
                if (m.Msg == 0x0318)
                {
                    using (var g = Graphics.FromHdc(m.WParam)) TextRenderer.DrawText(g, Text, box.Font, rect, KitInk.Faint, flags);
                    return;
                }
                HideCaret(Handle);
                using (var g = box.CreateGraphics()) TextRenderer.DrawText(g, Text, box.Font, rect, KitInk.Faint, flags);
                ShowCaret(Handle);
            }
        }

        // Key caps with short labels, inline
        class KeyHints : Control
        {
            public string[][] Pairs = new string[0][];

            public KeyHints()
            {
                SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
                TabStop = false;
                AccessibleRole = AccessibleRole.StaticText;
            }

            protected override void OnPaint(PaintEventArgs e)
            {
                var g = e.Graphics;
                KitInk.Smooth(g);
                g.Clear(KitInk.Under(this));
                var w = FindForm() as KitWindow;
                float s = w != null ? w.Ui : 1f, x = 0, h = 20 * s, y = (Height - h) / 2;
                var cf = Ink.Text(11f * s);
                var lf = Ink.Text(12f * s);
                var cap = Ink.Mix(KitInk.Group, KitInk.Text, Ink.Light ? 0.1f : 0.13f);
                foreach (var p in Pairs)
                {
                    float cw = Math.Max(h, TextRenderer.MeasureText(p[0], cf).Width + 4 * s);
                    float lw = TextRenderer.MeasureText(p[1], lf).Width;
                    // Only whole pairs; drop what won't fit
                    if (x + cw + 4 * s + lw > Width) break;
                    var r = new RectangleF(x, y, cw, h);
                    using (var path = Ink.Round(r, 6 * s))
                    using (var b = new SolidBrush(cap)) g.FillPath(b, path);
                    KitInk.Draw(g, p[0], cf, KitInk.Sub, r, ContentAlignment.MiddleCenter);
                    KitInk.Draw(g, p[1], lf, KitInk.Faint, new RectangleF(r.Right + 2 * s, 0, lw, Height), ContentAlignment.MiddleLeft);
                    x = r.Right + 2 * s + lw + 10 * s;
                }
            }
        }

        // Prompt rows: click or Enter to reuse
        class PromptList : KitControl
        {
            public readonly List<string> Items = new List<string>(), Trail = new List<string>();
            public bool Removable;
            public string Empty = "";
            public event Action<int> Picked, Removed;
            const int Rows = 4;
            const float RowH = 38;
            public const float DesignH = Rows * RowH + 8;
            int sel = -1, hover = -1, top;
            bool overX;

            public void Reset()
            {
                top = 0;
                hover = -1;
                sel = Items.Count > 0 && Focused ? 0 : -1;
                Invalidate();
                AccessibilityNotifyClients(AccessibleEvents.Reorder, -1);
            }

            RectangleF Row(int slot) { float s = Ui; return new RectangleF(0, 4 * s + slot * RowH * s, Width, RowH * s); }

            RectangleF XBox(RectangleF r) { float s = Ui, d = 24 * s; return new RectangleF(r.Right - 12 * s - d, r.Y + (r.Height - d) / 2, d, d); }

            int At(Point p)
            {
                for (int k = 0; k < Rows && top + k < Items.Count; k++) if (Row(k).Contains(p)) return top + k;
                return -1;
            }

            public void Pick(int i)
            {
                if (Items.Count == 0) { sel = -1; Invalidate(); return; }
                sel = Math.Max(0, Math.Min(Items.Count - 1, i));
                if (sel < top) top = sel;
                else if (sel >= top + Rows) top = sel - Rows + 1;
                Invalidate();
                if (!Focused) return;
                AccessibilityNotifyClients(AccessibleEvents.Selection, sel);
                AccessibilityNotifyClients(AccessibleEvents.Focus, sel);
            }

            void Fire(Action<int> h, int i) { if (h != null && i >= 0 && i < Items.Count) h(i); }

            void ScrollBy(int d)
            {
                int t = Math.Max(0, Math.Min(Math.Max(0, Items.Count - Rows), top + d));
                if (t != top) { top = t; Invalidate(); }
            }

            protected override void OnMouseMove(MouseEventArgs e)
            {
                base.OnMouseMove(e);
                int h = At(e.Location);
                bool x = h >= 0 && Removable && XBox(Row(h - top)).Contains(e.Location);
                if (h == hover && x == overX) return;
                hover = h;
                overX = x;
                Cursor = h >= 0 ? Cursors.Hand : Cursors.Default;
                Invalidate();
            }

            protected override void OnMouseLeave(EventArgs e)
            {
                base.OnMouseLeave(e);
                hover = -1;
                overX = false;
                Invalidate();
            }

            protected override void OnMouseUp(MouseEventArgs e)
            {
                base.OnMouseUp(e);
                if (e.Button != MouseButtons.Left) return;
                int i = At(e.Location);
                if (i < 0) return;
                if (Removable && XBox(Row(i - top)).Contains(e.Location)) Fire(Removed, i);
                else Fire(Picked, i);
            }

            protected override void OnMouseWheel(MouseEventArgs e)
            {
                base.OnMouseWheel(e);
                ScrollBy(e.Delta > 0 ? -1 : 1);
            }

            protected override bool IsInputKey(Keys key)
            {
                var k = key & Keys.KeyCode;
                return k == Keys.Up || k == Keys.Down || k == Keys.Home || k == Keys.End || k == Keys.Enter || k == Keys.Delete || base.IsInputKey(key);
            }

            protected override void OnGotFocus(EventArgs e)
            {
                if (sel < 0 && Items.Count > 0) sel = 0;
                base.OnGotFocus(e);
                if (sel >= 0) AccessibilityNotifyClients(AccessibleEvents.Focus, sel);
            }

            protected override void OnKeyDown(KeyEventArgs e)
            {
                base.OnKeyDown(e);
                if (Items.Count == 0) return;
                switch (e.KeyCode)
                {
                    case Keys.Up: Pick(sel - 1); break;
                    case Keys.Down: Pick(sel + 1); break;
                    case Keys.Home: Pick(0); break;
                    case Keys.End: Pick(Items.Count - 1); break;
                    case Keys.Enter: case Keys.Space: Fire(Picked, sel); break;
                    case Keys.Delete: if (Removable) Fire(Removed, sel); break;
                    default: return;
                }
                e.Handled = true;
                e.SuppressKeyPress = true;
            }

            protected override void OnPaint(PaintEventArgs e)
            {
                var g = e.Graphics;
                KitInk.Smooth(g);
                g.Clear(KitInk.Under(this));
                float s = Ui;
                var box = new RectangleF(0, 0, Width - 1, Height - 1);
                Fill(g, box, 12 * s, KitInk.Group);
                var f = Ink.Text(13.5f * s);
                if (Items.Count == 0)
                {
                    KitInk.Draw(g, Empty, Ink.Text(13f * s), KitInk.Faint, box, ContentAlignment.MiddleCenter);
                    Ring(g, box, 12 * s);
                    return;
                }
                var tf = Ink.Text(12f * s);
                bool keys = Focused && !KitInk.ByMouse;
                using (var line = new Pen(KitInk.Line, 1f))
                    for (int k = 0; k < Rows && top + k < Items.Count; k++)
                    {
                        int i = top + k;
                        var r = Row(k);
                        bool lit = i == hover || (i == sel && keys);
                        bool litAbove = k > 0 && (i - 1 == hover || (i - 1 == sel && keys));
                        if (k > 0 && !lit && !litAbove) g.DrawLine(line, 16 * s, r.Y, Width - 16 * s, r.Y);
                        if (lit) Fill(g, new RectangleF(6 * s, r.Y + 2 * s, Width - 12 * s, r.Height - 4 * s), 9 * s, KitInk.Hover);
                        float end = Width - 16 * s;
                        if (Removable && lit)
                        {
                            // Remove mark on the lit row
                            var xb = XBox(r);
                            if (i == hover && overX) Fill(g, xb, xb.Height / 2, KitInk.Line);
                            float cx = xb.X + xb.Width / 2, cy = xb.Y + xb.Height / 2, a = 4 * s;
                            using (var pen = new Pen(i == hover && overX ? KitInk.Text : KitInk.Sub, 1.6f * s) { StartCap = LineCap.Round, EndCap = LineCap.Round })
                            {
                                g.DrawLine(pen, cx - a, cy - a, cx + a, cy + a);
                                g.DrawLine(pen, cx - a, cy + a, cx + a, cy - a);
                            }
                            end = xb.X - 8 * s;
                        }
                        else if (i < Trail.Count && Trail[i].Length > 0)
                        {
                            float tw = TextRenderer.MeasureText(Trail[i], tf).Width;
                            KitInk.Draw(g, Trail[i], tf, KitInk.Faint, new RectangleF(end - tw, r.Y, tw, r.Height), ContentAlignment.MiddleRight);
                            end -= tw + 10 * s;
                        }
                        KitInk.Draw(g, Items[i], f, KitInk.Text, new RectangleF(12 * s, r.Y, end - 12 * s, r.Height), ContentAlignment.MiddleLeft);
                    }
                if (Items.Count > Rows)
                {
                    // Slim position mark when it scrolls
                    float trackH = Height - 16 * s, h = Math.Max(16 * s, trackH * Rows / Items.Count);
                    float y = 8 * s + (trackH - h) * top / Math.Max(1, Items.Count - Rows);
                    Fill(g, new RectangleF(Width - 5 * s, y, 3 * s, h), 1.5f * s, KitInk.Line);
                }
                Ring(g, box, 12 * s);
            }

            protected override AccessibleObject CreateAccessibilityInstance() { return new Access(this); }

            class Access : ControlAccessibleObject
            {
                readonly PromptList owner;
                public Access(PromptList owner) : base(owner) { this.owner = owner; }
                public override AccessibleRole Role { get { return AccessibleRole.List; } }
                public override int GetChildCount() { return owner.Items.Count; }
                public override AccessibleObject GetChild(int i) { return i >= 0 && i < owner.Items.Count ? new Item(owner, i) : null; }
                public override AccessibleObject GetSelected() { return owner.sel >= 0 ? GetChild(owner.sel) : null; }
                public override AccessibleObject GetFocused() { return owner.Focused && owner.sel >= 0 ? GetChild(owner.sel) : null; }
                public override string Description { get { return owner.Items.Count == 0 ? owner.Empty : base.Description; } }
            }

            class Item : AccessibleObject
            {
                readonly PromptList owner;
                readonly int index;
                public Item(PromptList owner, int index) { this.owner = owner; this.index = index; }
                public override string Name { get { return index < owner.Items.Count ? owner.Items[index] : ""; } }
                public override string Description { get { return index < owner.Trail.Count && owner.Trail[index].Length > 0 ? owner.Trail[index] : null; } }
                public override AccessibleRole Role { get { return AccessibleRole.ListItem; } }
                public override AccessibleObject Parent { get { return owner.AccessibilityObject; } }
                public override AccessibleStates State
                {
                    get
                    {
                        var st = AccessibleStates.Selectable | AccessibleStates.Focusable;
                        if (index == owner.sel) st |= AccessibleStates.Selected | (owner.Focused ? AccessibleStates.Focused : 0);
                        if (index < owner.top || index >= owner.top + Rows) st |= AccessibleStates.Offscreen;
                        return st;
                    }
                }
                public override Rectangle Bounds
                {
                    get
                    {
                        int slot = Math.Max(0, Math.Min(Rows - 1, index - owner.top));
                        return owner.RectangleToScreen(Rectangle.Round(owner.Row(slot)));
                    }
                }
                public override string DefaultAction { get { return Lang.T("Pakai"); } }
                public override void DoDefaultAction() { owner.Fire(owner.Picked, index); }
            }
        }
    }
}
