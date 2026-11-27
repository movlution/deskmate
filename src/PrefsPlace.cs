using System;
using System.Drawing;
using System.Windows.Forms;

namespace Deskmate
{
    partial class Island
    {
        partial class PrefsWindow
        {
            // Screen sketch: five spots for the island
            class PlacePicker : KitControl
            {
                public event Action<int> Changed;
                int selected, hover = -1, pillFor = -1;
                readonly Spring px = new Spring(0, 1, 1, 0.25f);
                readonly Spring py = new Spring(0, 1, 1, 0.25f);
                readonly Spring pw = new Spring(0, 1, 1, 0.25f);
                readonly Spring ph = new Spring(0, 1, 1, 0.25f);
                readonly KitPulse pulse;
                // Reading order for arrow keys
                static readonly int[] Order = { PlaceTopLeft, PlaceTop, PlaceTopRight, PlaceLeft, PlaceRight };

                public PlacePicker()
                {
                    pulse = new KitPulse(StepAnim);
                }

                bool StepAnim(float dt)
                {
                    if (IsDisposed || !Visible || Anim.Reduced)
                    {
                        SnapPill(selected);
                        Invalidate();
                        return false;
                    }
                    px.Step(dt); py.Step(dt); pw.Step(dt); ph.Step(dt);
                    bool more = !(px.Settled && py.Settled && pw.Settled && ph.Settled);
                    if (!more) SnapPill(selected);
                    Invalidate();
                    return more;
                }

                void SnapPill(int place)
                {
                    var r = Spot(place);
                    px.Snap(r.X); py.Snap(r.Y); pw.Snap(r.Width); ph.Snap(r.Height);
                    pillFor = place;
                }

                protected override void Dispose(bool disposing) { if (disposing) pulse.Dispose(); base.Dispose(disposing); }

                public int Selected
                {
                    get { return selected; }
                    set { if (value == selected) return; selected = value; pillFor = -1; pulse.Stop(); Invalidate(); AccessibilityNotifyClients(AccessibleEvents.Selection, value); }
                }

                RectangleF Screen { get { float s = Ui; return new RectangleF(8 * s, 6 * s, Width - 16 * s, Height - 12 * s); } }

                // Island sketch at a spot, hugging its edge
                RectangleF Spot(int i)
                {
                    var sc = Screen;
                    float s = Ui, w = 38 * s, h = 10 * s, tall = 30 * s, inset = 14 * s;
                    switch (i)
                    {
                        case PlaceTopLeft: return new RectangleF(sc.X + inset, sc.Y, w, h);
                        case PlaceTopRight: return new RectangleF(sc.Right - inset - w, sc.Y, w, h);
                        // Sides stand upright, like the island there
                        case PlaceLeft: return new RectangleF(sc.X, sc.Y + sc.Height * 0.24f, h, tall);
                        case PlaceRight: return new RectangleF(sc.Right - h, sc.Y + sc.Height * 0.24f, h, tall);
                    }
                    return new RectangleF(sc.X + sc.Width / 2 - w / 2, sc.Y, w, h);
                }

                RectangleF Cell(int i)
                {
                    var r = Spot(i);
                    r.Inflate(9 * Ui, 9 * Ui);
                    return r;
                }

                int At(Point pt)
                {
                    foreach (int i in Order) if (Cell(i).Contains(pt)) return i;
                    return -1;
                }

                void Pick(int i)
                {
                    if (i < 0 || i == selected) return;
                    int prev = selected;
                    selected = i;
                    if (IsHandleCreated && Visible && !Anim.Reduced)
                    {
                        if (pillFor != prev) SnapPill(prev);
                        var dst = Spot(i);
                        Springs.Tune(px, 0.20f, 0.86f);
                        Springs.Tune(py, 0.20f, 0.86f);
                        Springs.Tune(pw, 0.20f, 0.88f);
                        Springs.Tune(ph, 0.20f, 0.88f);
                        px.Target = dst.X; py.Target = dst.Y; pw.Target = dst.Width; ph.Target = dst.Height;
                        pillFor = i;
                        pulse.Start();
                    }
                    else pillFor = -1;
                    Invalidate();
                    AccessibilityNotifyClients(AccessibleEvents.Selection, i);
                    var h = Changed;
                    if (h != null) h(i);
                }

                protected override void OnMouseMove(MouseEventArgs e)
                {
                    base.OnMouseMove(e);
                    int h = At(e.Location);
                    if (h == hover) return;
                    hover = h;
                    Cursor = h >= 0 ? Cursors.Hand : Cursors.Default;
                    Invalidate();
                }

                protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); hover = -1; Invalidate(); }

                protected override void OnMouseUp(MouseEventArgs e) { base.OnMouseUp(e); Focus(); Pick(At(e.Location)); }

                protected override bool IsInputKey(Keys k) { return k == Keys.Left || k == Keys.Right || k == Keys.Up || k == Keys.Down || base.IsInputKey(k); }

                protected override void OnKeyDown(KeyEventArgs e)
                {
                    base.OnKeyDown(e);
                    int step = e.KeyCode == Keys.Left || e.KeyCode == Keys.Up ? -1 : e.KeyCode == Keys.Right || e.KeyCode == Keys.Down ? 1 : 0;
                    if (step == 0) return;
                    int at = Array.IndexOf(Order, selected);
                    Pick(Order[(at + step + Order.Length) % Order.Length]);
                    e.Handled = true;
                }

                protected override void OnPaint(PaintEventArgs e)
                {
                    var g = e.Graphics;
                    KitInk.Smooth(g);
                    g.Clear(KitInk.Under(this));
                    float s = Ui;
                    var sc = Screen;
                    // The screen, a touch lighter than its group
                    Fill(g, sc, 9 * s, KitInk.Line);
                    if (pillFor != selected) SnapPill(selected);
                    foreach (int i in Order)
                    {
                        var r = Spot(i);
                        if (i == selected && pulse.Running)
                        {
                            // Quiet resting slot under the gliding island
                            Fill(g, r, Math.Min(r.Width, r.Height) / 2, KitInk.Faint);
                            continue;
                        }
                        if (i == selected) continue;
                        // Unchosen spots stay quiet ink
                        Fill(g, r, Math.Min(r.Width, r.Height) / 2, i == hover ? KitInk.Sub : KitInk.Faint);
                    }
                    var active = pulse.Running ? new RectangleF(px.Value, py.Value, Math.Max(4 * s, pw.Value), Math.Max(4 * s, ph.Value)) : Spot(selected);
                    Fill(g, active, Math.Min(active.Width, active.Height) / 2, KitInk.Accent);
                    // Two dots: the island's eyes
                    var dot = KitInk.BrushOf(KitInk.OnAccent);
                    bool upright = active.Height > active.Width;
                    float cy = active.Y + (upright ? active.Width * 0.9f : active.Height / 2), cx = active.X + active.Width / 2, d = (upright ? 2.1f : 2.6f) * s, gap = (upright ? 2.3f : 3.4f) * s;
                    g.FillEllipse(dot, cx - gap - d / 2, cy - d / 2, d, d);
                    g.FillEllipse(dot, cx + gap - d / 2, cy - d / 2, d, d);
                    Ring(g, Cell(selected), 10 * s);
                }

                protected override AccessibleObject CreateAccessibilityInstance() { return new Access(this); }

                class Access : ControlAccessibleObject
                {
                    readonly PlacePicker owner;
                    public Access(PlacePicker owner) : base(owner) { this.owner = owner; }
                    public override AccessibleRole Role { get { return AccessibleRole.PageTabList; } }
                    public override int GetChildCount() { return Order.Length; }
                    public override AccessibleObject GetChild(int i) { return new Item(owner, Order[i]); }
                    public override AccessibleObject GetSelected() { return new Item(owner, owner.selected); }
                    public override AccessibleObject GetFocused() { return owner.Focused ? GetSelected() : null; }
                }

                class Item : AccessibleObject
                {
                    readonly PlacePicker owner;
                    readonly int place;
                    public Item(PlacePicker owner, int place) { this.owner = owner; this.place = place; }
                    public override string Name { get { return Lang.T(PlaceNames[place]); } }
                    public override AccessibleRole Role { get { return AccessibleRole.RadioButton; } }
                    public override AccessibleObject Parent { get { return owner.AccessibilityObject; } }
                    public override AccessibleStates State
                    {
                        get { return AccessibleStates.Selectable | AccessibleStates.Focusable | (place == owner.selected ? AccessibleStates.Selected | AccessibleStates.Checked : 0); }
                    }
                    public override Rectangle Bounds { get { return owner.RectangleToScreen(Rectangle.Round(owner.Cell(place))); } }
                    public override string DefaultAction { get { return Lang.T("Pilih"); } }
                    public override void DoDefaultAction() { owner.Pick(place); }
                }
            }
        }
    }
}
