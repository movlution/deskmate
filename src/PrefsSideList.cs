using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace Deskmate
{
    partial class Island
    {
        // Settings sidebar, macOS style: a Deskmate card, then colored category tiles; arrows move
        class SideList : KitControl
        {
            // { id, title, glyph, group, tile label key, card detail }
            public List<string[]> Items = new List<string[]>();
            public string Selected = "";
            public event Action<string> Picked;
            int hover = -1, lastHover = -1;
            string pillFor = null;
            readonly Spring pillY = new Spring(0, 1, 1, 0.25f);
            readonly Spring pillH = new Spring(0, 1, 1, 0.25f);
            float scrollTarget, hoverAlpha;
            int lastTick;
            readonly Timer anim = new Timer { Interval = 15 };

            public SideList()
            {
                anim.Tick += delegate
                {
                    int now = Environment.TickCount;
                    float dt = Math.Max(0.004f, Math.Min(0.05f, unchecked((uint)(now - lastTick)) / 1000f));
                    lastTick = now;
                    if (Anim.Reduced)
                    {
                        pillY.Snap(pillY.Target);
                        pillH.Snap(pillH.Target);
                        scroll = scrollTarget;
                        hoverAlpha = hover >= 0 ? 1f : 0f;
                        anim.Stop();
                        Invalidate();
                        return;
                    }
                    pillY.Step(dt);
                    pillH.Step(dt);
                    float sRate = Math.Min(1f, dt / Math.Max(0.03f, Anim.Response(0.08f)));
                    scroll += (scrollTarget - scroll) * sRate;
                    if (Math.Abs(scrollTarget - scroll) < 0.35f) scroll = scrollTarget;
                    float hWant = hover >= 0 ? 1f : 0f;
                    hoverAlpha += (hWant - hoverAlpha) * Math.Min(1f, dt / Math.Max(0.03f, Anim.Response(0.08f)));
                    if (Math.Abs(hWant - hoverAlpha) < 0.01f) hoverAlpha = hWant;
                    if (pillY.Settled && pillH.Settled && scroll == scrollTarget && hoverAlpha == hWant)
                    {
                        pillY.Snap(pillY.Target);
                        pillH.Snap(pillH.Target);
                        anim.Stop();
                    }
                    Invalidate();
                };
            }

            void KickAnim()
            {
                if (!IsHandleCreated || !Visible || Anim.Reduced)
                {
                    pillY.Snap(pillY.Target);
                    pillH.Snap(pillH.Target);
                    scroll = scrollTarget;
                    hoverAlpha = hover >= 0 ? 1f : 0f;
                    anim.Stop();
                    Invalidate();
                    return;
                }
                if (!anim.Enabled)
                {
                    lastTick = Environment.TickCount;
                    anim.Start();
                }
                Invalidate();
            }

            protected override void Dispose(bool disposing) { if (disposing) anim.Dispose(); base.Dispose(disposing); }

            float RowH { get { return 30 * Ui; } }

            static int GroupOf(string[] item) { return item.Length > 3 ? int.Parse(item[3]) : 0; }

            // The app card carries the Deskmate mark instead of a tile
            bool IsCard(int i) { return Items[i][2].Length > 0 && Items[i][2][0] == KitRow.MarkGlyph; }

            // Layout rect in unscrolled content space
            RectangleF LayoutCell(int i)
            {
                float s = Ui, y = 6 * s;
                for (int k = 0; k < i; k++)
                {
                    y += IsCard(k) ? CardH * s : RowH;
                    if (GroupOf(Items[k + 1]) != GroupOf(Items[k])) y += GroupGap * s;
                }
                float h = IsCard(i) ? CardH * s : RowH;
                return new RectangleF(10 * s, y, Width - 20 * s, h - 2 * s);
            }

            // Groups sit apart by space alone
            RectangleF Cell(int i)
            {
                var r = LayoutCell(i);
                r.Y -= scroll;
                return r;
            }

            const float GroupGap = 14, CardH = 58;

            // Paper twin of a label, in both themes: deep enough for an ivory glyph
            static Color TileOf(string[] item) { var c = item.Length > 4 ? Ink.Label(item[4], true) : Color.Empty; return c.IsEmpty ? Ink.Label("slate", true) : c; }

            // Short screens: the list scrolls
            float scroll;

            float MaxScroll()
            {
                if (Items.Count == 0) return 0;
                return Math.Max(0, LayoutCell(Items.Count - 1).Bottom + 14 * Ui - Height);
            }

            void ScrollTo(float v, bool smooth = false)
            {
                v = Math.Max(0, Math.Min(MaxScroll(), v));
                if (!smooth || Anim.Reduced || !Visible)
                {
                    if (v == scroll && v == scrollTarget) return;
                    scroll = scrollTarget = v;
                    Invalidate();
                    return;
                }
                if (v == scrollTarget) return;
                scrollTarget = v;
                KickAnim();
            }

            // Arrow keys keep the pick in view
            void Reveal(int i)
            {
                if (i < 0 || i >= Items.Count) return;
                var c = LayoutCell(i);
                if (c.Top - scrollTarget < 0) ScrollTo(c.Top - 6 * Ui, true);
                else if (c.Bottom - scrollTarget > Height) ScrollTo(c.Bottom - Height + 6 * Ui, true);
            }

            protected override void OnMouseWheel(MouseEventArgs e)
            {
                base.OnMouseWheel(e);
                ScrollTo(scrollTarget - e.Delta / 120f * RowH * 1.5f, true);
            }

            protected override void OnResize(EventArgs e) { base.OnResize(e); ScrollTo(scrollTarget); RevealSelected(); }

            public void RevealSelected() { Reveal(IndexOf(Selected)); }

            int IndexOf(string id) { for (int i = 0; i < Items.Count; i++) if (Items[i][0] == id) return i; return -1; }

            void Pick(int i)
            {
                if (i < 0 || i >= Items.Count) return;
                int prevAt = IndexOf(Selected);
                string nextId = Items[i][0];
                if (prevAt >= 0 && prevAt != i && IsHandleCreated && Visible && !Anim.Reduced)
                {
                    if (pillFor != Selected)
                    {
                        var prevR = LayoutCell(prevAt);
                        pillY.Snap(prevR.Y);
                        pillH.Snap(prevR.Height);
                    }
                    var nextR = LayoutCell(i);
                    Springs.Tune(pillY, 0.24f, 0.84f);
                    Springs.Tune(pillH, 0.24f, 0.88f);
                    pillY.Target = nextR.Y;
                    pillH.Target = nextR.Height;
                    pillFor = nextId;
                    Selected = nextId;
                    Reveal(i);
                    KickAnim();
                }
                else
                {
                    Selected = nextId;
                    pillFor = null;
                    Reveal(i);
                    Invalidate();
                }
                AccessibilityNotifyClients(AccessibleEvents.Selection, i);
                var h = Picked;
                if (h != null) h(Selected);
            }

            protected override void OnMouseMove(MouseEventArgs e)
            {
                base.OnMouseMove(e);
                int h = -1;
                for (int i = 0; i < Items.Count; i++) if (Cell(i).Contains(e.Location)) h = i;
                if (h != hover)
                {
                    hover = h;
                    if (h >= 0) lastHover = h;
                    Cursor = h >= 0 ? Cursors.Hand : Cursors.Default;
                    KickAnim();
                }
            }

            protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); hover = -1; Cursor = Cursors.Default; KickAnim(); }

            protected override void OnMouseUp(MouseEventArgs e)
            {
                base.OnMouseUp(e);
                for (int i = 0; i < Items.Count; i++) if (Cell(i).Contains(e.Location)) { Pick(i); return; }
            }

            protected override bool IsInputKey(Keys key) { return key == Keys.Up || key == Keys.Down || base.IsInputKey(key); }

            protected override void OnKeyDown(KeyEventArgs e)
            {
                base.OnKeyDown(e);
                int at = IndexOf(Selected);
                if (e.KeyCode == Keys.Up) Pick(Math.Max(0, at - 1));
                else if (e.KeyCode == Keys.Down) Pick(Math.Min(Items.Count - 1, at + 1));
            }

            protected override void OnPaint(PaintEventArgs e)
            {
                var g = e.Graphics;
                KitInk.Smooth(g);
                g.Clear(KitInk.Side);
                float s = Ui, radius = 7 * s;
                int at = IndexOf(Selected);
                if (at >= 0 && pillFor != Selected)
                {
                    var cur = LayoutCell(at);
                    pillY.Snap(cur.Y);
                    pillH.Snap(cur.Height);
                    pillFor = Selected;
                }
                // Hover wash behind unselected row
                var hoverFill = Ink.Mix(KitInk.Side, KitInk.Text, Ink.Light ? 0.05f : 0.06f);
                int hIdx = hover >= 0 ? hover : (hoverAlpha > 0.02f ? lastHover : -1);
                if (hIdx >= 0 && hIdx < Items.Count && hIdx != at)
                {
                    var hc = Cell(hIdx);
                    if (hc.Bottom >= 0 && hc.Top <= Height)
                    {
                        float ha = anim.Enabled ? hoverAlpha : (hover >= 0 ? 1f : 0f);
                        Fill(g, hc, radius, Ink.Mix(KitInk.Side, hoverFill, ha));
                    }
                }
                // Gliding accent selection pill
                if (at >= 0)
                {
                    var selRect = new RectangleF(10 * s, pillY.Value - scroll, Width - 20 * s, pillH.Value);
                    if (selRect.Bottom >= 0 && selRect.Top <= Height)
                        Fill(g, selRect, radius, KitInk.Accent);
                }
                // Selected: label in OnAccent semibold; the tile keeps its color
                Font f = Ink.Text(13f * s), fs = Ink.Semi(13f * s), small = Ink.Text(11.5f * s), icons = Ink.Icons(11.5f * s);
                for (int i = 0; i < Items.Count; i++)
                {
                    var c = Cell(i);
                    if (c.Bottom < 0 || c.Top > Height) continue;
                    bool sel = i == at;
                    var ink = sel ? KitInk.OnAccent : KitInk.Text;
                    if (IsCard(i))
                    {
                        var icon = new RectangleF(c.X + 6 * s, c.Y + (c.Height - 36 * s) / 2, 36 * s, 36 * s);
                        g.DrawIcon(KitWindow.AppIcon, Rectangle.Round(icon));
                        float tx = c.X + 52 * s;
                        KitInk.Draw(g, Items[i][1], fs, ink, new RectangleF(tx, c.Y + 8 * s, c.Right - tx - 6 * s, 20 * s), ContentAlignment.MiddleLeft);
                        string detail = Items[i].Length > 5 ? Items[i][5] : "";
                        KitInk.Draw(g, detail, small, sel ? KitInk.OnAccent : KitInk.Sub, new RectangleF(tx, c.Y + 28 * s, c.Right - tx - 6 * s, 18 * s), ContentAlignment.MiddleLeft);
                        continue;
                    }
                    var tileColor = TileOf(Items[i]);
                    var tile = new RectangleF(c.X + 6 * s, c.Y + (c.Height - 20 * s) / 2, 20 * s, 20 * s);
                    Fill(g, tile, 5 * s, tileColor);
                    KitInk.Draw(g, Items[i][2], icons, Ink.On(tileColor), tile, ContentAlignment.MiddleCenter);
                    KitInk.Draw(g, Items[i][1], sel ? fs : f, ink, new RectangleF(c.X + 36 * s, c.Y, c.Width - 40 * s, c.Height), ContentAlignment.MiddleLeft);
                }
                if (at >= 0) Ring(g, Cell(at), radius);
            }

            protected override AccessibleObject CreateAccessibilityInstance() { return new Access(this); }

            class Access : ControlAccessibleObject
            {
                readonly SideList owner;
                public Access(SideList owner) : base(owner) { this.owner = owner; }
                public override AccessibleRole Role { get { return AccessibleRole.PageTabList; } }
                public override int GetChildCount() { return owner.Items.Count; }
                public override AccessibleObject GetChild(int i) { return new Item(owner, i); }
            }

            class Item : AccessibleObject
            {
                readonly SideList owner;
                readonly int index;
                public Item(SideList owner, int index) { this.owner = owner; this.index = index; }
                public override string Name { get { return owner.Items[index][1]; } }
                // The card reads its version line too
                public override string Description { get { var it = owner.Items[index]; return it.Length > 5 ? it[5] : ""; } }
                public override AccessibleRole Role { get { return AccessibleRole.PageTab; } }
                public override AccessibleObject Parent { get { return owner.AccessibilityObject; } }
                public override AccessibleStates State { get { return AccessibleStates.Selectable | (owner.Items[index][0] == owner.Selected ? AccessibleStates.Selected : 0); } }
                public override Rectangle Bounds { get { return owner.RectangleToScreen(Rectangle.Round(owner.Cell(index))); } }
                public override void DoDefaultAction() { owner.Pick(index); }
            }
        }
    }
}
