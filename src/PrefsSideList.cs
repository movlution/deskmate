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
        // Settings sidebar: icon, name; arrows move
        class SideList : KitControl
        {
            public List<string[]> Items = new List<string[]>();
            // One heading per group, above its first row
            public string[] Headers = new string[0];
            public string Selected = "";
            public event Action<string> Picked;
            int hover = -1;

            float RowH { get { return 32 * Ui; } }

            static int GroupOf(string[] item) { return item.Length > 3 ? int.Parse(item[3]) : 0; }

            // Groups sit apart under their heading
            RectangleF Cell(int i)
            {
                float s = Ui;
                int group = GroupOf(Items[i]);
                return new RectangleF(10 * s, 6 * s + i * RowH + (group * GroupGap + HeadsThrough(group) * HeaderDip) * s - scroll, Width - 20 * s, RowH - 2 * s);
            }

            const float GroupGap = 10, HeaderDip = 26;

            string HeaderOf(int group) { return group < Headers.Length ? Headers[group] ?? "" : ""; }

            // Headings above this group, its own included
            int HeadsThrough(int group)
            {
                int n = 0;
                for (int g = 0; g <= group; g++) if (HeaderOf(g).Length > 0) n++;
                return n;
            }

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
                float s = Ui;
                var active = Ink.Mix(KitInk.Hover, KitInk.Accent, 0.10f);
                var activeMark = Contrast(KitInk.Accent, active) >= 3.0 ? KitInk.Accent : KitInk.Text;
                using (var f = new Font(Ink.MenuFamily, 13.5f * s, GraphicsUnit.Pixel))
                using (var head = new Font(Ink.MenuFamily, 11.5f * s, FontStyle.Bold, GraphicsUnit.Pixel))
                using (var icons = new Font(Ink.IconFamily, 14f * s, GraphicsUnit.Pixel))
                    for (int i = 0; i < Items.Count; i++)
                    {
                        var c = Cell(i);
                        int group = GroupOf(Items[i]);
                        // First row of a group: its heading above
                        if ((i == 0 || GroupOf(Items[i - 1]) != group) && HeaderOf(group).Length > 0)
                            KitInk.Draw(g, HeaderOf(group), head, KitInk.Sub, new RectangleF(c.X + 10 * s, c.Y - HeaderDip * s + 4 * s, c.Width - 10 * s, HeaderDip * s - 6 * s), ContentAlignment.MiddleLeft);
                        bool sel = Items[i][0] == Selected;
                        if (sel || i == hover) Fill(g, c, 9 * s, sel ? active : Ink.Mix(KitInk.Side, KitInk.Hover, 0.5f));
                        if (sel)
                        {
                            var bar = new RectangleF(c.X + 3 * s, c.Y + 8 * s, 2 * s, c.Height - 16 * s);
                            Fill(g, bar, s, activeMark);
                        }
                        KitInk.Draw(g, Items[i][2], icons, sel ? KitInk.Text : KitInk.Sub, new RectangleF(c.X + 8 * s, c.Y, 22 * s, c.Height), ContentAlignment.MiddleCenter);
                        KitInk.Draw(g, Items[i][1], f, sel ? KitInk.Text : KitInk.Sub, new RectangleF(c.X + 38 * s, c.Y, c.Width - 44 * s, c.Height), ContentAlignment.MiddleLeft);
                    }
                int at = IndexOf(Selected);
                if (at >= 0) Ring(g, Cell(at), 9 * s);
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
                // Readers hear the group's heading too
                public override string Description { get { return owner.HeaderOf(GroupOf(owner.Items[index])); } }
                public override AccessibleRole Role { get { return AccessibleRole.PageTab; } }
                public override AccessibleObject Parent { get { return owner.AccessibilityObject; } }
                public override AccessibleStates State { get { return AccessibleStates.Selectable | (owner.Items[index][0] == owner.Selected ? AccessibleStates.Selected : 0); } }
                public override Rectangle Bounds { get { return owner.RectangleToScreen(Rectangle.Round(owner.Cell(index))); } }
                public override void DoDefaultAction() { owner.Pick(index); }
            }
        }
    }
}
