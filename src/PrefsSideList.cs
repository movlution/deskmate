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
            int hover = -1;

            float RowH { get { return 30 * Ui; } }

            static int GroupOf(string[] item) { return item.Length > 3 ? int.Parse(item[3]) : 0; }

            // The app card carries the Deskmate mark instead of a tile
            bool IsCard(int i) { return Items[i][2].Length > 0 && Items[i][2][0] == KitRow.MarkGlyph; }

            // Groups sit apart by space alone
            RectangleF Cell(int i)
            {
                float s = Ui, y = 6 * s;
                for (int k = 0; k < i; k++)
                {
                    y += IsCard(k) ? CardH * s : RowH;
                    if (GroupOf(Items[k + 1]) != GroupOf(Items[k])) y += GroupGap * s;
                }
                float h = IsCard(i) ? CardH * s : RowH;
                return new RectangleF(10 * s, y - scroll, Width - 20 * s, h - 2 * s);
            }

            const float GroupGap = 14, CardH = 58;

            // Paper twin of a label, in both themes: deep enough for an ivory glyph
            static Color TileOf(string[] item) { var c = item.Length > 4 ? Ink.Label(item[4], true) : Color.Empty; return c.IsEmpty ? Ink.Label("slate", true) : c; }

            // Short screens: the list scrolls
            float scroll;

            float MaxScroll()
            {
                if (Items.Count == 0) return 0;
                return Math.Max(0, Cell(Items.Count - 1).Bottom + scroll + 14 * Ui - Height);
            }

            void ScrollTo(float v)
            {
                v = Math.Max(0, Math.Min(MaxScroll(), v));
                if (v == scroll) return;
                scroll = v;
                Invalidate();
            }

            // Arrow keys keep the pick in view
            void Reveal(int i)
            {
                if (i < 0 || i >= Items.Count) return;
                var c = Cell(i);
                if (c.Top < 0) ScrollTo(scroll + c.Top - 6 * Ui);
                else if (c.Bottom > Height) ScrollTo(scroll + c.Bottom - Height + 6 * Ui);
            }

            protected override void OnMouseWheel(MouseEventArgs e)
            {
                base.OnMouseWheel(e);
                ScrollTo(scroll - e.Delta / 120f * RowH * 1.5f);
            }

            protected override void OnResize(EventArgs e) { base.OnResize(e); ScrollTo(scroll); RevealSelected(); }

            public void RevealSelected() { Reveal(IndexOf(Selected)); }

            int IndexOf(string id) { for (int i = 0; i < Items.Count; i++) if (Items[i][0] == id) return i; return -1; }

            void Pick(int i)
            {
                if (i < 0 || i >= Items.Count) return;
                Selected = Items[i][0];
                Reveal(i);
                Invalidate();
                AccessibilityNotifyClients(AccessibleEvents.Selection, i);
                var h = Picked;
                if (h != null) h(Selected);
            }

            protected override void OnMouseMove(MouseEventArgs e)
            {
                base.OnMouseMove(e);
                int h = -1;
                for (int i = 0; i < Items.Count; i++) if (Cell(i).Contains(e.Location)) h = i;
                if (h != hover) { hover = h; Invalidate(); }
            }

            protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); hover = -1; Invalidate(); }

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
                // Selected: filled with the accent, label in OnAccent semibold; the tile keeps its color
                var hoverFill = Ink.Mix(KitInk.Side, KitInk.Text, Ink.Light ? 0.05f : 0.06f);
                Font f = Ink.Text(13f * s), fs = Ink.Semi(13f * s), small = Ink.Text(11.5f * s), icons = Ink.Icons(11.5f * s);
                for (int i = 0; i < Items.Count; i++)
                {
                    var c = Cell(i);
                    if (c.Bottom < 0 || c.Top > Height) continue;
                    bool sel = Items[i][0] == Selected;
                    if (sel || i == hover) Fill(g, c, radius, sel ? KitInk.Accent : hoverFill);
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
                int at = IndexOf(Selected);
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
