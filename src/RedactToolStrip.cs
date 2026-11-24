using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Windows.Forms;

namespace Deskmate
{
    // Crisp GDI+ vector tool strip matching Deskmate's KitControl language
    class RedactToolStrip : KitControl
    {
        struct ToolItem
        {
            public ToolMode Mode;
            public string LabelKey;
            public char Shortcut;
        }

        static readonly ToolItem[] Items =
        {
            new ToolItem { Mode = ToolMode.Select, LabelKey = "Pilih", Shortcut = 'V' },
            new ToolItem { Mode = ToolMode.Arrow, LabelKey = "Panah", Shortcut = 'A' },
            new ToolItem { Mode = ToolMode.Line, LabelKey = "Garis", Shortcut = 'L' },
            new ToolItem { Mode = ToolMode.Rect, LabelKey = "Kotak", Shortcut = 'R' },
            new ToolItem { Mode = ToolMode.Ellipse, LabelKey = "Elips", Shortcut = 'O' },
            new ToolItem { Mode = ToolMode.Pen, LabelKey = "Coret", Shortcut = 'P' },
            new ToolItem { Mode = ToolMode.Highlight, LabelKey = "Stabilo", Shortcut = 'H' },
            new ToolItem { Mode = ToolMode.Text, LabelKey = "Teks", Shortcut = 'T' },
            new ToolItem { Mode = ToolMode.Step, LabelKey = "Langkah", Shortcut = 'N' },
            new ToolItem { Mode = ToolMode.Redact, LabelKey = "Samarkan", Shortcut = 'B' },
            new ToolItem { Mode = ToolMode.Crop, LabelKey = "Potong", Shortcut = 'C' },
        };

        readonly ToolTip tip = new ToolTip { InitialDelay = 350, ReshowDelay = 120, AutoPopDelay = 5000 };
        int selected;
        int hovered = -1;

        public event Action<ToolMode> ToolChanged;

        public ToolMode SelectedTool
        {
            get { return Items[selected].Mode; }
            set
            {
                int idx = IndexOf(value);
                if (idx >= 0 && idx != selected)
                {
                    selected = idx;
                    Invalidate();
                    AccessibilityNotifyClients(AccessibleEvents.Selection, selected);
                }
            }
        }

        public int PreferredWidth(float ui)
        {
            return (int)Math.Ceiling(Items.Length * 32 * ui + 8 * ui);
        }

        static int IndexOf(ToolMode m)
        {
            for (int i = 0; i < Items.Length; i++)
                if (Items[i].Mode == m) return i;
            return 0;
        }

        public void Pick(ToolMode m)
        {
            PickIndex(IndexOf(m));
        }

        void PickIndex(int i)
        {
            if (i < 0 || i >= Items.Length || i == selected) return;
            selected = i;
            Invalidate();
            AccessibilityNotifyClients(AccessibleEvents.Selection, selected);
            var h = ToolChanged;
            if (h != null) h(Items[i].Mode);
        }

        RectangleF Cell(int i)
        {
            float pad = 3f * Ui;
            float avail = Math.Max(1f, Width - 1f - pad * 2f);
            float w = avail / Items.Length;
            return new RectangleF(pad + i * w, pad, w, Math.Max(1f, Height - 1f - pad * 2f));
        }

        int HitCell(Point p)
        {
            for (int i = 0; i < Items.Length; i++)
                if (Cell(i).Contains(p)) return i;
            return -1;
        }

        string TipFor(int i)
        {
            return Lang.T(Items[i].LabelKey) + " (" + Items[i].Shortcut + ")";
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            int hit = HitCell(e.Location);
            if (hit != hovered)
            {
                hovered = hit;
                tip.SetToolTip(this, hit >= 0 ? TipFor(hit) : "");
                Invalidate();
            }
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            if (hovered != -1)
            {
                hovered = -1;
                tip.SetToolTip(this, "");
                Invalidate();
            }
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button != MouseButtons.Left) return;
            int hit = HitCell(e.Location);
            if (hit >= 0) PickIndex(hit);
        }

        protected override bool IsInputKey(Keys key)
        {
            return key == Keys.Left || key == Keys.Right || key == Keys.Home || key == Keys.End || base.IsInputKey(key);
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            if (e.KeyCode == Keys.Left) PickIndex((selected - 1 + Items.Length) % Items.Length);
            else if (e.KeyCode == Keys.Right) PickIndex((selected + 1) % Items.Length);
            else if (e.KeyCode == Keys.Home) PickIndex(0);
            else if (e.KeyCode == Keys.End) PickIndex(Items.Length - 1);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            KitInk.Smooth(g);
            g.Clear(KitInk.Under(this));
            var track = new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f);
            float rad = track.Height / 2f;
            Fill(g, track, rad, KitInk.Line);

            float s = Ui;
            for (int i = 0; i < Items.Length; i++)
            {
                var c = Cell(i);
                var pill = RectangleF.Inflate(c, -1f * s, 0f);
                if (i == selected)
                    Fill(g, pill, pill.Height / 2f, KitInk.Raised);
                else if (i == hovered)
                    Fill(g, pill, pill.Height / 2f, KitInk.Hover);

                Color fg = i == selected ? KitInk.Text : (i == hovered ? KitInk.Text : KitInk.Sub);
                DrawIcon(g, Items[i].Mode, c, fg, s);
            }

            Ring(g, track, rad);
        }

        static void DrawIcon(Graphics g, ToolMode mode, RectangleF cell, Color fg, float s)
        {
            float cx = cell.X + cell.Width / 2f;
            float cy = cell.Y + cell.Height / 2f;
            float u = Math.Min(cell.Width, cell.Height) * 0.58f;
            float x0 = cx - u / 2f;
            float y0 = cy - u / 2f;
            float stroke = Math.Max(1.3f, 1.55f * s);

            switch (mode)
            {
                case ToolMode.Select:
                    {
                        var pts = new[]
                        {
                            new PointF(x0 + u * 0.22f, y0 + u * 0.12f),
                            new PointF(x0 + u * 0.22f, y0 + u * 0.86f),
                            new PointF(x0 + u * 0.42f, y0 + u * 0.67f),
                            new PointF(x0 + u * 0.56f, y0 + u * 0.90f),
                            new PointF(x0 + u * 0.68f, y0 + u * 0.84f),
                            new PointF(x0 + u * 0.54f, y0 + u * 0.60f),
                            new PointF(x0 + u * 0.80f, y0 + u * 0.58f),
                        };
                        using (var b = new SolidBrush(fg)) g.FillPolygon(b, pts);
                        break;
                    }
                case ToolMode.Arrow:
                    {
                        float ax = x0 + u * 0.18f, ay = y0 + u * 0.82f;
                        float bx = x0 + u * 0.82f, by = y0 + u * 0.18f;
                        using (var p = new Pen(fg, stroke) { StartCap = LineCap.Round, EndCap = LineCap.Round })
                            g.DrawLine(p, ax, ay, bx - u * 0.18f, by + u * 0.18f);
                        var head = new[]
                        {
                            new PointF(bx, by),
                            new PointF(bx - u * 0.36f, by + u * 0.04f),
                            new PointF(bx - u * 0.04f, by + u * 0.36f),
                        };
                        using (var b = new SolidBrush(fg)) g.FillPolygon(b, head);
                        break;
                    }
                case ToolMode.Line:
                    {
                        using (var p = new Pen(fg, stroke) { StartCap = LineCap.Round, EndCap = LineCap.Round })
                            g.DrawLine(p, x0 + u * 0.18f, y0 + u * 0.82f, x0 + u * 0.82f, y0 + u * 0.18f);
                        using (var b = new SolidBrush(fg))
                        {
                            float r = 1.6f * s;
                            g.FillEllipse(b, x0 + u * 0.18f - r, y0 + u * 0.82f - r, r * 2, r * 2);
                            g.FillEllipse(b, x0 + u * 0.82f - r, y0 + u * 0.18f - r, r * 2, r * 2);
                        }
                        break;
                    }
                case ToolMode.Rect:
                    {
                        var r = new RectangleF(x0 + u * 0.14f, y0 + u * 0.20f, u * 0.72f, u * 0.60f);
                        using (var path = Ink.Round(r, 2.2f * s))
                        using (var p = new Pen(fg, stroke))
                            g.DrawPath(p, path);
                        break;
                    }
                case ToolMode.Ellipse:
                    {
                        var r = new RectangleF(x0 + u * 0.14f, y0 + u * 0.18f, u * 0.72f, u * 0.64f);
                        using (var p = new Pen(fg, stroke))
                            g.DrawEllipse(p, r);
                        break;
                    }
                case ToolMode.Pen:
                    {
                        using (var p = new Pen(fg, stroke) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round })
                        {
                            g.DrawBezier(p,
                                new PointF(x0 + u * 0.15f, y0 + u * 0.76f),
                                new PointF(x0 + u * 0.36f, y0 + u * 0.18f),
                                new PointF(x0 + u * 0.60f, y0 + u * 0.86f),
                                new PointF(x0 + u * 0.85f, y0 + u * 0.24f));
                        }
                        break;
                    }
                case ToolMode.Highlight:
                    {
                        using (var b = new SolidBrush(Color.FromArgb(110, fg)))
                            g.FillRectangle(b, x0 + u * 0.14f, y0 + u * 0.68f, u * 0.72f, u * 0.20f);
                        var nib = new[]
                        {
                            new PointF(x0 + u * 0.26f, y0 + u * 0.62f),
                            new PointF(x0 + u * 0.62f, y0 + u * 0.14f),
                            new PointF(x0 + u * 0.78f, y0 + u * 0.28f),
                            new PointF(x0 + u * 0.42f, y0 + u * 0.74f),
                        };
                        using (var p = new Pen(fg, stroke) { LineJoin = LineJoin.Round })
                            g.DrawPolygon(p, nib);
                        break;
                    }
                case ToolMode.Text:
                    {
                        using (var p = new Pen(fg, stroke) { StartCap = LineCap.Round, EndCap = LineCap.Round })
                        {
                            g.DrawLine(p, x0 + u * 0.18f, y0 + u * 0.22f, x0 + u * 0.82f, y0 + u * 0.22f);
                            g.DrawLine(p, cx, y0 + u * 0.22f, cx, y0 + u * 0.84f);
                            g.DrawLine(p, x0 + u * 0.36f, y0 + u * 0.84f, x0 + u * 0.64f, y0 + u * 0.84f);
                        }
                        break;
                    }
                case ToolMode.Step:
                    {
                        var r = new RectangleF(x0 + u * 0.14f, y0 + u * 0.14f, u * 0.72f, u * 0.72f);
                        using (var b = new SolidBrush(fg)) g.FillEllipse(b, r);
                        Color inv = KitInk.Window;
                        if (Island.Contrast(inv, fg) < 3.0)
                            inv = Island.Contrast(Color.White, fg) >= Island.Contrast(Color.Black, fg) ? Color.White : Color.Black;
                        using (var f = new Font(Ink.MenuFamily, Math.Max(8f, u * 0.56f), FontStyle.Bold, GraphicsUnit.Pixel))
                        using (var b = new SolidBrush(inv))
                        using (var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
                            g.DrawString("1", f, b, new RectangleF(r.X, r.Y + 0.5f * s, r.Width, r.Height), sf);
                        break;
                    }
                case ToolMode.Redact:
                    {
                        float cellSz = u * 0.24f;
                        float gap = u * 0.04f;
                        float sx = cx - (cellSz * 3 + gap * 2) / 2f;
                        float sy = cy - (cellSz * 3 + gap * 2) / 2f;
                        using (var b1 = new SolidBrush(fg))
                        using (var b2 = new SolidBrush(Color.FromArgb(120, fg)))
                        {
                            for (int row = 0; row < 3; row++)
                                for (int col = 0; col < 3; col++)
                                    g.FillRectangle((row + col) % 2 == 0 ? b1 : b2,
                                        sx + col * (cellSz + gap), sy + row * (cellSz + gap), cellSz, cellSz);
                        }
                        break;
                    }
                case ToolMode.Crop:
                    {
                        using (var p = new Pen(fg, stroke) { StartCap = LineCap.Round, EndCap = LineCap.Round })
                        {
                            g.DrawLine(p, x0 + u * 0.26f, y0 + u * 0.10f, x0 + u * 0.26f, y0 + u * 0.74f);
                            g.DrawLine(p, x0 + u * 0.26f, y0 + u * 0.74f, x0 + u * 0.90f, y0 + u * 0.74f);
                            g.DrawLine(p, x0 + u * 0.10f, y0 + u * 0.26f, x0 + u * 0.74f, y0 + u * 0.26f);
                            g.DrawLine(p, x0 + u * 0.74f, y0 + u * 0.26f, x0 + u * 0.74f, y0 + u * 0.90f);
                        }
                        break;
                    }
            }
        }

        protected override AccessibleObject CreateAccessibilityInstance()
        {
            return new StripAccess(this);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) tip.Dispose();
            base.Dispose(disposing);
        }

        class StripAccess : ControlAccessibleObject
        {
            readonly RedactToolStrip owner;
            public StripAccess(RedactToolStrip owner) : base(owner) { this.owner = owner; }
            public override AccessibleRole Role { get { return AccessibleRole.ToolBar; } }
            public override int GetChildCount() { return Items.Length; }
            public override AccessibleObject GetChild(int i) { return new ItemAccess(owner, i); }
            public override AccessibleObject GetSelected() { return GetChild(owner.selected); }
            public override AccessibleObject GetFocused() { return owner.Focused ? GetChild(owner.selected) : null; }
        }

        class ItemAccess : AccessibleObject
        {
            readonly RedactToolStrip owner;
            readonly int index;
            public ItemAccess(RedactToolStrip owner, int index) { this.owner = owner; this.index = index; }
            public override string Name { get { return owner.TipFor(index); } }
            public override AccessibleRole Role { get { return AccessibleRole.RadioButton; } }
            public override AccessibleObject Parent { get { return owner.AccessibilityObject; } }
            public override AccessibleStates State
            {
                get
                {
                    return AccessibleStates.Selectable | AccessibleStates.Focusable |
                        (index == owner.selected ? AccessibleStates.Selected | AccessibleStates.Checked : 0);
                }
            }
            public override Rectangle Bounds { get { return owner.RectangleToScreen(Rectangle.Round(owner.Cell(index))); } }
            public override string DefaultAction { get { return Lang.T("Pilih"); } }
            public override void DoDefaultAction() { owner.PickIndex(index); }
        }
    }
}
