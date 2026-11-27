using System;
using System.Drawing;
using System.Windows.Forms;

namespace Deskmate
{
    partial class Island
    {
        partial class PrefsWindow
        {
            // Curated Hub order so Prev/Next footer navigation follows the exact visual flow of the Main Menu Hub
            static readonly string[] HubOrder =
            {
                "look", "island", "dock", "general",
                "messages", "work", "remind",
                "ai", "agents", "ingat", "files", "system",
                "game", "keys", "about",
            };

            Color SectionTileColor(string page)
            {
                if (page == "search") return KitInk.Accent;
                var sec = FindSection(page);
                if (sec != null && sec[4].Length > 0)
                {
                    var c = Ink.Label(sec[4], true);
                    if (!c.IsEmpty) return c;
                }
                return KitInk.Accent;
            }

            // Category Hero Banner: matches the SettingsHub Featured card language
            // with category tint wash, crisp border, left vertical accent bar, elevated icon tile, and live status pill.
            class PageHeader : KitControl
            {
                public char Glyph;
                public string Title = "", Note = "", Status = "";
                public Color Tile;
                public bool ShowPaletteDots;

                public PageHeader() { TabStop = false; }

                protected override void OnPaint(PaintEventArgs e)
                {
                    var g = e.Graphics;
                    KitInk.Smooth(g);
                    g.Clear(KitInk.Under(this));
                    float s = Ui;
                    Color tile = Tile.IsEmpty ? KitInk.Accent : Tile;
                    var card = new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f);
                    float radius = 14f * s;

                    // Category-tinted surface wash + crisp accented border
                    Color bg = Ink.Mix(KitInk.Group, tile, Ink.Light ? 0.06f : 0.105f);
                    Color edge = Ink.Mix(KitInk.Line, tile, Ink.Light ? 0.36f : 0.44f);
                    KitInk.FillRound(g, card, radius, bg);
                    KitInk.StrokeRound(g, card, radius, edge, 1.2f * s);

                    // Left vertical accent bar (matching Hub Featured Hero cards)
                    float barH = card.Height * 0.48f;
                    var bar = new RectangleF(card.X + 5f * s, card.Y + (card.Height - barH) / 2f, 3.5f * s, barH);
                    KitInk.FillRound(g, bar, 1.75f * s, tile);

                    // Elevated category icon tile
                    float iconSize = 46f * s;
                    var iconRect = new RectangleF(19f * s, (Height - iconSize) / 2f, iconSize, iconSize);
                    if (Glyph == KitRow.MarkGlyph || Tile.IsEmpty)
                    {
                        g.DrawIcon(KitWindow.AppIcon, Rectangle.Round(iconRect));
                    }
                    else
                    {
                        KitInk.Lift(g, iconRect, 12f * s, s * 0.75f);
                        KitInk.FillRound(g, iconRect, 12f * s, tile);
                        KitInk.Draw(g, Ink.Str(Glyph), Ink.Icons(22f * s), Ink.On(tile), iconRect, ContentAlignment.MiddleCenter);
                    }

                    // Live status pill (top-right) + optional 3-dot palette preview
                    float rightLimit = Width - 16f * s;
                    float pillH = 22f * s;
                    float pillY = 17f * s;
                    if (!string.IsNullOrEmpty(Status))
                    {
                        Font sf = Ink.Semi(11f * s);
                        int sw = TextRenderer.MeasureText(Status, sf).Width;
                        float pillW = Math.Min(Width * 0.36f, sw + 18f * s);
                        float pillX = rightLimit - pillW;
                        if (pillX > iconRect.Right + 110f * s)
                        {
                            var pillR = new RectangleF(pillX, pillY, pillW, pillH);
                            Color pillBg = Ink.Mix(bg, tile, Ink.Light ? 0.15f : 0.25f);
                            Color pillEdge = Ink.Mix(KitInk.Line, tile, 0.35f);
                            KitInk.FillRound(g, pillR, pillH / 2f, pillBg);
                            KitInk.StrokeRound(g, pillR, pillH / 2f, pillEdge, 1f * s);
                            KitInk.Draw(g, Status, sf, KitInk.Text, pillR, ContentAlignment.MiddleCenter);
                            rightLimit = pillX - 10f * s;
                        }
                    }

                    if (ShowPaletteDots && rightLimit > iconRect.Right + 150f * s)
                    {
                        float dot = 10f * s, dotGap = 4.5f * s;
                        float dy = pillY + (pillH - dot) / 2f;
                        float dx = rightLimit - (dot * 3 + dotGap * 2) - 2f * s;
                        Color[] dots = { KitInk.Window, KitInk.Group, KitInk.Accent };
                        for (int k = 0; k < 3; k++)
                        {
                            var dr = new RectangleF(dx + k * (dot + dotGap), dy, dot, dot);
                            g.FillEllipse(KitInk.BrushOf(dots[k]), dr);
                            g.DrawEllipse(KitInk.PenOf(KitInk.Line, 1f * s), dr);
                        }
                        rightLimit = dx - 10f * s;
                    }

                    // Display title + readable subtitle
                    float tx = iconRect.Right + 15f * s;
                    float titleW = Math.Max(60f * s, rightLimit - tx);
                    float noteW = Math.Max(60f * s, Width - tx - 16f * s);
                    KitInk.Draw(g, Title, Ink.Semi(17.5f * s), KitInk.Text, new RectangleF(tx, 16f * s, titleW, 25f * s), ContentAlignment.MiddleLeft);
                    KitInk.Draw(g, Note, Ink.Text(12f * s), KitInk.Sub, new RectangleF(tx, 43f * s, noteW, 20f * s), ContentAlignment.MiddleLeft);
                }
            }

            // Section deck heading: category accent pill + real child Label (for a11y/tests) + trailing hairline rule
            sealed class DeckCaption : Panel
            {
                readonly Color tile;
                readonly float ui;
                readonly Label titleLabel;

                public DeckCaption(string text, Color tile, float ui)
                {
                    this.tile = tile.IsEmpty ? KitInk.Accent : tile;
                    this.ui = ui;
                    SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
                    BackColor = KitInk.Window;
                    TabStop = false;

                    titleLabel = new Label
                    {
                        Text = text,
                        Font = Ink.Semi(13f * ui),
                        ForeColor = KitInk.Text,
                        BackColor = KitInk.Window,
                        AutoSize = true,
                        UseMnemonic = false,
                    };
                    int lblH = titleLabel.PreferredHeight;
                    Height = Math.Max((int)(26 * ui), lblH + (int)(4 * ui));
                    titleLabel.Location = new Point((int)(12 * ui), Math.Max(0, (Height - lblH) / 2));
                    Controls.Add(titleLabel);
                }

                protected override void OnPaint(PaintEventArgs e)
                {
                    var g = e.Graphics;
                    KitInk.Smooth(g);
                    g.Clear(KitInk.Window);
                    float s = ui;

                    // Vertical accent pill on the left
                    float pillH = 13f * s;
                    var pill = new RectangleF(2f * s, (Height - pillH) / 2f, 3.5f * s, pillH);
                    KitInk.FillRound(g, pill, 1.75f * s, tile);

                    // Subtle trailing hairline rule after the text
                    float ruleX = titleLabel.Right + 10f * s;
                    float ruleY = Height / 2f;
                    if (ruleX < Width - 20f * s)
                    {
                        Color ruleColor = Ink.Mix(KitInk.Window, KitInk.Line, 0.85f);
                        g.DrawLine(KitInk.PenOf(ruleColor, 1f * s), ruleX, ruleY, Width - 4f * s, ruleY);
                    }
                }
            }

            // Icon-enhanced spring-gliding chapter switcher for merged pages (Island, Tugas, AI, Sistem, Gamifikasi)
            sealed class PartTabStrip : KitControl
            {
                public string[] Labels = new string[0];
                public char[] Glyphs = new char[0];
                public Color[] Tiles = new Color[0];
                int selected, hot = -1;
                float glide = -1;
                readonly Spring glideSpring = new Spring(0, 1, 1, 0.002f);
                int lastTick;
                Timer anim;
                public event Action<int> Changed;

                public int Selected
                {
                    get { return selected; }
                    set
                    {
                        int n = Labels.Length == 0 ? 0 : Math.Max(0, Math.Min(Labels.Length - 1, value));
                        if (n == selected && glide >= 0) return;
                        selected = n;
                        if (glide < 0 || !IsHandleCreated || !Visible || Anim.Reduced)
                        {
                            glide = selected;
                            glideSpring.Snap(selected);
                            if (anim != null) anim.Stop();
                        }
                        else
                        {
                            glideSpring.Value = glide;
                            Springs.Tune(glideSpring, 0.22f, 0.82f);
                            glideSpring.Target = selected;
                            EnsureAnim();
                            if (!anim.Enabled) { lastTick = Environment.TickCount; anim.Start(); }
                        }
                        Invalidate();
                    }
                }

                void EnsureAnim()
                {
                    if (anim != null) return;
                    anim = new Timer { Interval = 15 };
                    anim.Tick += delegate
                    {
                        int now = Environment.TickCount;
                        float dt = Math.Max(0.004f, Math.Min(0.05f, unchecked((uint)(now - lastTick)) / 1000f));
                        lastTick = now;
                        if (Anim.Reduced)
                        {
                            glide = selected;
                            glideSpring.Snap(selected);
                            anim.Stop();
                            Invalidate();
                            return;
                        }
                        glideSpring.Target = selected;
                        glideSpring.Step(dt);
                        glide = glideSpring.Value;
                        if (glideSpring.Settled)
                        {
                            glide = selected;
                            glideSpring.Snap(selected);
                            anim.Stop();
                        }
                        Invalidate();
                    };
                }

                void Pick(int i)
                {
                    if (i < 0 || i >= Labels.Length || i == selected) return;
                    Selected = i;
                    AccessibilityNotifyClients(AccessibleEvents.Selection, selected);
                    var h = Changed;
                    if (h != null) h(selected);
                }

                RectangleF TabRect(int i)
                {
                    float s = Ui, pad = 3f * s;
                    float segW = Labels.Length > 0 ? (Width - pad * 2f) / Labels.Length : 0f;
                    return new RectangleF(pad + i * segW, pad, segW, Math.Max(1f, Height - pad * 2f));
                }

                protected override AccessibleObject CreateAccessibilityInstance() { return new Access(this); }

                sealed class Access : ControlAccessibleObject
                {
                    readonly PartTabStrip owner;
                    public Access(PartTabStrip owner) : base(owner) { this.owner = owner; }
                    public override AccessibleRole Role { get { return AccessibleRole.PageTabList; } }
                    public override int GetChildCount() { return owner.Labels.Length; }
                    public override AccessibleObject GetChild(int i) { return i >= 0 && i < owner.Labels.Length ? new Item(owner, i) : null; }
                    public override AccessibleObject GetSelected() { return GetChild(owner.selected); }
                    public override AccessibleObject GetFocused() { return owner.Focused ? GetSelected() : null; }
                }

                sealed class Item : AccessibleObject
                {
                    readonly PartTabStrip owner;
                    readonly int index;
                    public Item(PartTabStrip owner, int index) { this.owner = owner; this.index = index; }
                    public override string Name { get { return index < owner.Labels.Length ? owner.Labels[index] : ""; } }
                    public override AccessibleRole Role { get { return AccessibleRole.PageTab; } }
                    public override AccessibleObject Parent { get { return owner.AccessibilityObject; } }
                    public override AccessibleStates State
                    {
                        get
                        {
                            return AccessibleStates.Selectable | AccessibleStates.Focusable |
                                (index == owner.selected ? AccessibleStates.Selected : 0) |
                                (owner.Focused && index == owner.selected ? AccessibleStates.Focused : 0);
                        }
                    }
                    public override Rectangle Bounds { get { return owner.RectangleToScreen(Rectangle.Round(owner.TabRect(index))); } }
                    public override string DefaultAction { get { return Lang.T("Pilih"); } }
                    public override void DoDefaultAction() { owner.Pick(index); }
                }

                int Hit(Point pt)
                {
                    if (Labels.Length == 0 || Width <= 0) return -1;
                    int idx = (int)(pt.X / ((float)Width / Labels.Length));
                    return Math.Max(0, Math.Min(Labels.Length - 1, idx));
                }

                protected override void OnMouseMove(MouseEventArgs e)
                {
                    base.OnMouseMove(e);
                    int idx = Hit(e.Location);
                    if (idx != hot) { hot = idx; Cursor = hot >= 0 ? Cursors.Hand : Cursors.Default; Invalidate(); }
                }

                protected override void OnMouseLeave(EventArgs e)
                {
                    base.OnMouseLeave(e);
                    hot = -1;
                    Cursor = Cursors.Default;
                    Invalidate();
                }

                protected override void OnMouseDown(MouseEventArgs e)
                {
                    base.OnMouseDown(e);
                    if (e.Button != MouseButtons.Left) return;
                    Focus();
                    Pick(Hit(e.Location));
                }

                protected override bool IsInputKey(Keys k)
                {
                    return k == Keys.Left || k == Keys.Right || k == Keys.Home || k == Keys.End || base.IsInputKey(k);
                }

                protected override void OnKeyDown(KeyEventArgs e)
                {
                    base.OnKeyDown(e);
                    if (Labels.Length < 2) return;
                    if (e.KeyCode == Keys.Left) { Pick(Math.Max(0, selected - 1)); e.Handled = true; }
                    else if (e.KeyCode == Keys.Right) { Pick(Math.Min(Labels.Length - 1, selected + 1)); e.Handled = true; }
                    else if (e.KeyCode == Keys.Home) { Pick(0); e.Handled = true; }
                    else if (e.KeyCode == Keys.End) { Pick(Labels.Length - 1); e.Handled = true; }
                }

                protected override void OnPaint(PaintEventArgs e)
                {
                    var g = e.Graphics;
                    KitInk.Smooth(g);
                    g.Clear(KitInk.Under(this));
                    if (Labels.Length == 0) return;
                    float s = Ui;
                    var track = new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f);
                    float trackRad = 11f * s;
                    Color trackBg = Ink.Light ? Ink.Mix(KitInk.Window, Color.Black, 0.055f) : Ink.Mix(KitInk.Window, KitInk.Text, 0.05f);
                    KitInk.FillRound(g, track, trackRad, trackBg);
                    KitInk.StrokeRound(g, track, trackRad, KitInk.Line, 1f * s);

                    float pos = glide < 0 ? selected : glide;
                    float segW = (Width - 6f * s) / Labels.Length;
                    float pad = 3f * s;
                    var pill = new RectangleF(pad + pos * segW, pad, segW, Height - pad * 2f);
                    Color activeTile = selected >= 0 && selected < Tiles.Length && !Tiles[selected].IsEmpty ? Tiles[selected] : KitInk.Accent;
                    Color pillBg = Ink.Mix(KitInk.Raised, activeTile, Ink.Light ? 0.08f : 0.16f);
                    Color pillEdge = Ink.Mix(KitInk.Line, activeTile, 0.45f);
                    KitInk.Lift(g, pill, 8f * s, s * 0.5f);
                    KitInk.FillRound(g, pill, 8f * s, pillBg);
                    KitInk.StrokeRound(g, pill, 8f * s, pillEdge, 1f * s);

                    Font fActive = Ink.Semi(12.5f * s);
                    Font fIdle = Ink.Text(12.5f * s);
                    for (int i = 0; i < Labels.Length; i++)
                    {
                        var r = new RectangleF(pad + i * segW, pad, segW, Height - pad * 2f);
                        bool isSel = i == selected;
                        bool isHot = i == hot && !isSel;
                        if (isHot)
                            KitInk.FillRound(g, RectangleF.Inflate(r, -2f * s, -2f * s), 7f * s, Ink.Mix(trackBg, KitInk.Text, 0.05f));

                        Color segTile = i < Tiles.Length && !Tiles[i].IsEmpty ? Tiles[i] : KitInk.Accent;
                        char glyph = i < Glyphs.Length ? Glyphs[i] : '\0';
                        Font f = isSel ? fActive : fIdle;
                        int tw = TextRenderer.MeasureText(Labels[i], f).Width;
                        float iconW = glyph != '\0' ? 18f * s : 0f;
                        float gap = glyph != '\0' ? 5f * s : 0f;
                        float totalW = Math.Min(r.Width - 12f * s, iconW + gap + tw);
                        float sx = r.X + (r.Width - totalW) / 2f;

                        if (glyph != '\0')
                        {
                            Color gInk = isSel
                                ? (Ink.Light ? Ink.Mix(segTile, Color.Black, 0.14f) : Ink.Mix(segTile, Color.White, 0.25f))
                                : (isHot ? KitInk.Text : KitInk.Sub);
                            KitInk.Draw(g, Ink.Str(glyph), Ink.Icons(13.5f * s), gInk, new RectangleF(sx, r.Y, iconW, r.Height), ContentAlignment.MiddleCenter);
                            sx += iconW + gap;
                        }
                        Color textInk = isSel ? KitInk.Text : (isHot ? KitInk.Text : KitInk.Sub);
                        KitInk.Draw(g, Labels[i], f, textInk, new RectangleF(sx, r.Y, Math.Max(20f * s, r.Right - sx - 4f * s), r.Height), ContentAlignment.MiddleLeft);
                    }
                    Ring(g, track, trackRad);
                }

                protected override void Dispose(bool disposing)
                {
                    if (disposing && anim != null) anim.Dispose();
                    base.Dispose(disposing);
                }
            }

            // Console Deck Footer: tactile Prev Category / Menu Utama / Next Category cards at the bottom of every sub-menu
            sealed class SubMenuFooter : KitControl
            {
                readonly string prevId, prevTitle, nextId, nextTitle;
                readonly char prevGlyph, nextGlyph;
                readonly Color prevTile, nextTile;
                int hoverSlot = -1; // 0=prev, 1=hub, 2=next
                int pressSlot = -1;
                int focusSlot = 1;
                public event Action<string> Picked;
                public event Action GoHub;

                public SubMenuFooter(string prevId, string prevTitle, char prevGlyph, Color prevTile,
                                     string nextId, string nextTitle, char nextGlyph, Color nextTile)
                {
                    this.prevId = prevId;
                    this.prevTitle = prevTitle;
                    this.prevGlyph = prevGlyph;
                    this.prevTile = prevTile;
                    this.nextId = nextId;
                    this.nextTitle = nextTitle;
                    this.nextGlyph = nextGlyph;
                    this.nextTile = nextTile;
                    TabStop = true;
                }

                void ActivateSlot(int slot)
                {
                    if (slot == 0 && Picked != null) Picked(prevId);
                    else if (slot == 1 && GoHub != null) GoHub();
                    else if (slot == 2 && Picked != null) Picked(nextId);
                }

                RectangleF SlotRect(int slot)
                {
                    RectangleF l, m, r;
                    SlotRects(out l, out m, out r);
                    return slot == 0 ? l : slot == 1 ? m : r;
                }

                protected override AccessibleObject CreateAccessibilityInstance() { return new Access(this); }

                sealed class Access : ControlAccessibleObject
                {
                    readonly SubMenuFooter owner;
                    public Access(SubMenuFooter owner) : base(owner) { this.owner = owner; }
                    public override AccessibleRole Role { get { return AccessibleRole.Grouping; } }
                    public override int GetChildCount() { return 3; }
                    public override AccessibleObject GetChild(int i) { return i >= 0 && i < 3 ? new SlotItem(owner, i) : null; }
                    public override AccessibleObject GetFocused() { return owner.Focused ? GetChild(owner.focusSlot) : null; }
                }

                sealed class SlotItem : AccessibleObject
                {
                    readonly SubMenuFooter owner;
                    readonly int slot;
                    public SlotItem(SubMenuFooter owner, int slot) { this.owner = owner; this.slot = slot; }
                    public override string Name
                    {
                        get
                        {
                            if (slot == 0) return Lang.T("Sebelumnya") + ": " + owner.prevTitle;
                            if (slot == 1) return Lang.T("Menu Utama");
                            return Lang.T("Berikutnya") + ": " + owner.nextTitle;
                        }
                    }
                    public override AccessibleRole Role { get { return AccessibleRole.PushButton; } }
                    public override AccessibleObject Parent { get { return owner.AccessibilityObject; } }
                    public override AccessibleStates State
                    {
                        get
                        {
                            return AccessibleStates.Focusable |
                                (owner.Focused && owner.focusSlot == slot ? AccessibleStates.Focused : 0);
                        }
                    }
                    public override Rectangle Bounds { get { return owner.RectangleToScreen(Rectangle.Round(owner.SlotRect(slot))); } }
                    public override string DefaultAction { get { return Lang.T("Buka"); } }
                    public override void DoDefaultAction() { owner.ActivateSlot(slot); }
                }

                void SlotRects(out RectangleF leftR, out RectangleF midR, out RectangleF rightR)
                {
                    float s = Ui;
                    float top = 16f * s;
                    float h = Height - top;
                    float gap = 10f * s;
                    float midW = Math.Min(138f * s, Width * 0.24f);
                    float sideW = Math.Max(80f * s, (Width - midW - gap * 2f) / 2f);
                    leftR = new RectangleF(0.5f, top, sideW, h - 1f);
                    midR = new RectangleF(leftR.Right + gap, top, midW, h - 1f);
                    rightR = new RectangleF(midR.Right + gap, top, Math.Max(80f * s, Width - midR.Right - gap - 0.5f), h - 1f);
                }

                int HitSlot(Point pt)
                {
                    RectangleF l, m, r;
                    SlotRects(out l, out m, out r);
                    if (l.Contains(pt)) return 0;
                    if (m.Contains(pt)) return 1;
                    if (r.Contains(pt)) return 2;
                    return -1;
                }

                protected override void OnMouseMove(MouseEventArgs e)
                {
                    base.OnMouseMove(e);
                    int h = HitSlot(e.Location);
                    if (h != hoverSlot) { hoverSlot = h; Cursor = h >= 0 ? Cursors.Hand : Cursors.Default; Invalidate(); }
                }

                protected override void OnMouseLeave(EventArgs e)
                {
                    base.OnMouseLeave(e);
                    if (hoverSlot >= 0 || pressSlot >= 0) { hoverSlot = pressSlot = -1; Cursor = Cursors.Default; Invalidate(); }
                }

                protected override void OnMouseDown(MouseEventArgs e)
                {
                    base.OnMouseDown(e);
                    if (e.Button != MouseButtons.Left) return;
                    pressSlot = HitSlot(e.Location);
                    if (pressSlot >= 0) focusSlot = pressSlot;
                    Invalidate();
                }

                protected override void OnMouseUp(MouseEventArgs e)
                {
                    base.OnMouseUp(e);
                    int hit = HitSlot(e.Location);
                    bool fire = e.Button == MouseButtons.Left && hit >= 0 && hit == pressSlot;
                    pressSlot = -1;
                    Invalidate();
                    if (fire) ActivateSlot(hit);
                }

                protected override bool IsInputKey(Keys k)
                {
                    return k == Keys.Left || k == Keys.Right || k == Keys.Home || k == Keys.End || k == Keys.Enter || k == Keys.Space || base.IsInputKey(k);
                }

                protected override void OnKeyDown(KeyEventArgs e)
                {
                    base.OnKeyDown(e);
                    if (e.KeyCode == Keys.Left) { focusSlot = Math.Max(0, focusSlot - 1); Invalidate(); e.Handled = true; }
                    else if (e.KeyCode == Keys.Right) { focusSlot = Math.Min(2, focusSlot + 1); Invalidate(); e.Handled = true; }
                    else if (e.KeyCode == Keys.Home) { focusSlot = 0; Invalidate(); e.Handled = true; }
                    else if (e.KeyCode == Keys.End) { focusSlot = 2; Invalidate(); e.Handled = true; }
                    else if (e.KeyCode == Keys.Enter || e.KeyCode == Keys.Space) { ActivateSlot(focusSlot); e.Handled = true; }
                }

                protected override void OnPaint(PaintEventArgs e)
                {
                    var g = e.Graphics;
                    KitInk.Smooth(g);
                    g.Clear(KitInk.Window);
                    float s = Ui;

                    // Subtle top separator
                    g.DrawLine(KitInk.PenOf(KitInk.Line, 1f * s), 0, 2f * s, Width - 1, 2f * s);

                    RectangleF l, m, r;
                    SlotRects(out l, out m, out r);

                    DrawSideCard(g, l, false, prevTitle, prevGlyph, prevTile, hoverSlot == 0, pressSlot == 0, s);
                    DrawHubButton(g, m, hoverSlot == 1, pressSlot == 1, s);
                    DrawSideCard(g, r, true, nextTitle, nextGlyph, nextTile, hoverSlot == 2, pressSlot == 2, s);
                    Ring(g, SlotRect(focusSlot), 11f * s);
                }

                void DrawHubButton(Graphics g, RectangleF r, bool hover, bool press, float s)
                {
                    float rad = 11f * s;
                    Color bg = press ? Ink.Mix(KitInk.Group, KitInk.Accent, 0.14f)
                        : hover ? Ink.Mix(KitInk.Group, KitInk.Accent, Ink.Light ? 0.06f : 0.11f)
                        : KitInk.Group;
                    Color edge = hover || press ? Ink.Mix(KitInk.Line, KitInk.Accent, 0.55f) : KitInk.Line;
                    KitInk.FillRound(g, r, rad, bg);
                    KitInk.StrokeRound(g, r, rad, edge, 1f * s);
                    string label = Lang.T("Menu Utama");
                    Font f = Ink.Semi(12f * s);
                    int tw = TextRenderer.MeasureText(label, f).Width;
                    float iconW = 16f * s, gap = 5f * s;
                    float totalW = Math.Min(r.Width - 14f * s, iconW + gap + tw);
                    float sx = r.X + (r.Width - totalW) / 2f;
                    KitInk.Draw(g, "\uE80F", Ink.Icons(13f * s), hover ? KitInk.Accent : KitInk.Sub, new RectangleF(sx, r.Y, iconW, r.Height), ContentAlignment.MiddleCenter);
                    KitInk.Draw(g, label, f, KitInk.Text, new RectangleF(sx + iconW + gap, r.Y, Math.Max(20f * s, r.Right - (sx + iconW + gap) - 6f * s), r.Height), ContentAlignment.MiddleLeft);
                }

                void DrawSideCard(Graphics g, RectangleF r, bool isNext, string title, char glyph, Color tile, bool hover, bool press, float s)
                {
                    Color accent = tile.IsEmpty ? KitInk.Accent : tile;
                    float rad = 11f * s;
                    Color bg = press ? Ink.Mix(KitInk.Group, accent, Ink.Light ? 0.12f : 0.20f)
                        : hover ? Ink.Mix(KitInk.Group, accent, Ink.Light ? 0.06f : 0.12f)
                        : KitInk.Group;
                    Color edge = hover || press ? Ink.Mix(KitInk.Line, accent, 0.60f) : KitInk.Line;
                    KitInk.FillRound(g, r, rad, bg);
                    KitInk.StrokeRound(g, r, rad, edge, 1f * s);

                    float badge = 28f * s;
                    float by = r.Y + (r.Height - badge) / 2f;
                    string hint = isNext ? Lang.T("Berikutnya") : Lang.T("Sebelumnya");

                    if (!isNext)
                    {
                        // ← [badge] Sebelumnya / Title
                        KitInk.Draw(g, "\uE72B", Ink.Icons(11.5f * s), hover ? accent : KitInk.Faint, new RectangleF(r.X + 8f * s, r.Y, 18f * s, r.Height), ContentAlignment.MiddleCenter);
                        var br = new RectangleF(r.X + 30f * s, by, badge, badge);
                        DrawBadge(g, br, glyph, tile, s);
                        float tx = br.Right + 10f * s;
                        float tw = Math.Max(20f * s, r.Right - tx - 10f * s);
                        KitInk.Draw(g, hint, Ink.Text(10f * s), KitInk.Faint, new RectangleF(tx, r.Y + 7f * s, tw, 14f * s), ContentAlignment.MiddleLeft);
                        KitInk.Draw(g, title, Ink.Semi(12.5f * s), KitInk.Text, new RectangleF(tx, r.Y + 21f * s, tw, 18f * s), ContentAlignment.MiddleLeft);
                    }
                    else
                    {
                        // Berikutnya / Title [badge] →
                        KitInk.Draw(g, "\uE72A", Ink.Icons(11.5f * s), hover ? accent : KitInk.Faint, new RectangleF(r.Right - 26f * s, r.Y, 18f * s, r.Height), ContentAlignment.MiddleCenter);
                        var br = new RectangleF(r.Right - 30f * s - badge, by, badge, badge);
                        DrawBadge(g, br, glyph, tile, s);
                        float tx = r.X + 12f * s;
                        float tw = Math.Max(20f * s, br.Left - tx - 10f * s);
                        KitInk.Draw(g, hint, Ink.Text(10f * s), KitInk.Faint, new RectangleF(tx, r.Y + 7f * s, tw, 14f * s), ContentAlignment.MiddleRight);
                        KitInk.Draw(g, title, Ink.Semi(12.5f * s), KitInk.Text, new RectangleF(tx, r.Y + 21f * s, tw, 18f * s), ContentAlignment.MiddleRight);
                    }
                }

                static void DrawBadge(Graphics g, RectangleF br, char glyph, Color tile, float s)
                {
                    if (glyph == KitRow.MarkGlyph || tile.IsEmpty)
                        g.DrawIcon(KitWindow.AppIcon, Rectangle.Round(br));
                    else
                    {
                        KitInk.FillRound(g, br, 8f * s, tile);
                        KitInk.Draw(g, Ink.Str(glyph), Ink.Icons(13.5f * s), Ink.On(tile), br, ContentAlignment.MiddleCenter);
                    }
                }
            }

            // Friendly empty state card for search queries with 0 matches
            sealed class SearchEmptyCard : Panel
            {
                readonly float ui;
                readonly Label titleLabel;
                readonly Label tipLabel;

                public SearchEmptyCard(string title, string tip, float ui)
                {
                    this.ui = ui;
                    SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
                    BackColor = KitInk.Group;
                    TabStop = false;

                    titleLabel = new Label
                    {
                        Text = title,
                        Font = Ink.Semi(14.5f * ui),
                        ForeColor = KitInk.Text,
                        BackColor = KitInk.Group,
                        TextAlign = ContentAlignment.MiddleCenter,
                        AutoSize = false,
                        UseMnemonic = false,
                        Height = (int)(24 * ui),
                    };
                    tipLabel = new Label
                    {
                        Text = tip,
                        Font = Ink.Text(12f * ui),
                        ForeColor = KitInk.Sub,
                        BackColor = KitInk.Group,
                        TextAlign = ContentAlignment.MiddleCenter,
                        AutoSize = false,
                        UseMnemonic = false,
                        Height = (int)(20 * ui),
                    };
                    Controls.Add(titleLabel);
                    Controls.Add(tipLabel);
                    Height = (int)(136 * ui);
                }

                public void Reflow()
                {
                    int pad = (int)(20 * ui);
                    int w = Math.Max(40, Width - pad * 2);
                    titleLabel.SetBounds(pad, (int)(70 * ui), w, (int)(24 * ui));
                    tipLabel.SetBounds(pad, (int)(96 * ui), w, (int)(20 * ui));
                    Invalidate();
                }

                protected override void OnResize(EventArgs e)
                {
                    base.OnResize(e);
                    Reflow();
                }

                protected override void OnPaint(PaintEventArgs e)
                {
                    var g = e.Graphics;
                    KitInk.Smooth(g);
                    g.Clear(KitInk.Window);
                    float s = ui;
                    var card = new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f);
                    KitInk.FillRound(g, card, 13f * s, KitInk.Group);
                    KitInk.StrokeRound(g, card, 13f * s, KitInk.Line, 1f * s);
                    float iconSz = 40f * s;
                    var ir = new RectangleF((Width - iconSz) / 2f, 20f * s, iconSz, iconSz);
                    KitInk.FillRound(g, ir, 11f * s, KitInk.Hover);
                    KitInk.Draw(g, "\uE721", Ink.Icons(18f * s), KitInk.Sub, ir, ContentAlignment.MiddleCenter);
                }
            }
        }
    }
}
