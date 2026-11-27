using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace Deskmate
{
    partial class Island
    {
        // Game-platform / console-style main menu hub for Settings:
        // Asymmetrical Bento decks with Hero cards, live status badges, spring hover/press lift,
        // spatial arrow-key navigation, staggered spring entrance, and smooth wheel inertia.
        class SettingsHub : KitControl
        {
            public sealed class Card
            {
                public string Id = "";
                public string Title = "";
                public string Subtitle = "";
                public string Status = "";
                public char Glyph = '\uE713';
                public string TileKey = "slate";
                public int Deck;
                public bool Featured;
                public bool Wide;
                public bool ShowPaletteDots;
                public RectangleF Rect;
                public readonly Spring Hover = new Spring(0, 1, 1, 0.004f);
                public readonly Spring Press = new Spring(0, 1, 1, 0.004f);
                public int EntranceRow;
            }

            readonly List<Card> cards = new List<Card>();
            readonly List<DeckHeader> deckHeaders = new List<DeckHeader>();
            string[] deckTitles = new string[0];
            public event Action<string> Picked;

            struct DeckHeader
            {
                public string Title;
                public float Y;
            }

            int hoverIndex = -1;
            int pressIndex = -1;
            int focusIndex = 0;
            int maxEntranceRow;
            Point lastMousePt = new Point(int.MinValue, int.MinValue);
            float scroll, scrollTarget, contentHeight;
            int lastTick;
            bool entranceActive;
            int entranceStart;
            readonly Timer anim = new Timer { Interval = 15 };

            // Content column bounds (in control coordinates)
            float padX, colW;

            public SettingsHub()
            {
                TabStop = true;
                anim.Tick += delegate { StepAnim(); };
            }

            public void Bind(List<Card> items, string[] decks, string focusId)
            {
                cards.Clear();
                if (items != null) cards.AddRange(items);
                deckTitles = decks ?? new string[0];
                hoverIndex = -1;
                pressIndex = -1;
                int idx = IndexOf(focusId);
                focusIndex = idx >= 0 ? idx : 0;
                LayoutCards();
                Invalidate();
            }

            public void UpdateStatuses(Func<string, string> statusOf)
            {
                if (statusOf == null) return;
                for (int i = 0; i < cards.Count; i++)
                    cards[i].Status = statusOf(cards[i].Id) ?? "";
                Invalidate();
            }

            public void PlayEntrance(string highlightId)
            {
                int idx = IndexOf(highlightId);
                if (idx >= 0) focusIndex = idx;
                hoverIndex = -1;
                pressIndex = -1;
                for (int i = 0; i < cards.Count; i++)
                {
                    cards[i].Hover.Snap(0);
                    cards[i].Press.Snap(0);
                }
                LayoutCards();
                if (idx >= 0) RevealCard(idx, false);
                if (Anim.Reduced || !IsHandleCreated || !Visible)
                {
                    entranceActive = false;
                    Invalidate();
                    return;
                }
                entranceActive = true;
                entranceStart = Environment.TickCount;
                KickAnim();
            }

            int IndexOf(string id)
            {
                if (string.IsNullOrEmpty(id)) return -1;
                for (int i = 0; i < cards.Count; i++)
                    if (cards[i].Id == id) return i;
                return -1;
            }

            void KickAnim()
            {
                if (!IsHandleCreated || !Visible || Anim.Reduced)
                {
                    SnapAll();
                    return;
                }
                if (!anim.Enabled)
                {
                    lastTick = Environment.TickCount;
                    anim.Start();
                }
                Invalidate();
            }

            int ActiveCardIndex()
            {
                if (hoverIndex >= 0 && hoverIndex < cards.Count) return hoverIndex;
                if (Focused && !KitInk.ByMouse && focusIndex >= 0 && focusIndex < cards.Count) return focusIndex;
                return -1;
            }

            void SyncCardSprings()
            {
                int active = ActiveCardIndex();
                for (int i = 0; i < cards.Count; i++)
                {
                    float want = i == active ? 1f : 0f;
                    if (Math.Abs(cards[i].Hover.Target - want) > 0.001f)
                    {
                        if (want > 0.5f) Springs.Tune(cards[i].Hover, 0.16f, 0.78f);
                        else Springs.Tune(cards[i].Hover, 0.20f, 0.88f);
                        cards[i].Hover.Target = want;
                    }
                }
                KickAnim();
            }

            void SnapAll()
            {
                entranceActive = false;
                scroll = scrollTarget;
                int active = ActiveCardIndex();
                for (int i = 0; i < cards.Count; i++)
                {
                    cards[i].Hover.Snap(i == active ? 1f : 0f);
                    cards[i].Press.Snap(i == pressIndex ? 1f : 0f);
                }
                anim.Stop();
                Invalidate();
            }

            void StepAnim()
            {
                if (IsDisposed || !Visible || Anim.Reduced)
                {
                    SnapAll();
                    return;
                }
                int now = Environment.TickCount;
                float dt = Math.Max(0.004f, Math.Min(0.05f, unchecked((uint)(now - lastTick)) / 1000f));
                lastTick = now;
                bool busy = false;

                // Smooth wheel inertia
                if (Math.Abs(scrollTarget - scroll) > 0.35f)
                {
                    float rate = Math.Min(1f, dt / Math.Max(0.03f, Anim.Response(0.075f)));
                    scroll += (scrollTarget - scroll) * rate;
                    if (KitInk.ByMouse && lastMousePt.X != int.MinValue && ClientRectangle.Contains(lastMousePt))
                    {
                        int hit = HitCard(lastMousePt);
                        if (hit != hoverIndex) { hoverIndex = hit; Cursor = hoverIndex >= 0 ? Cursors.Hand : Cursors.Default; SyncCardSprings(); }
                    }
                    busy = true;
                }
                else scroll = scrollTarget;

                // Per-card hover & press springs
                for (int i = 0; i < cards.Count; i++)
                {
                    var c = cards[i];
                    if (!c.Hover.Settled) { c.Hover.Step(dt); busy = true; }
                    else c.Hover.Snap(c.Hover.Target);
                    if (!c.Press.Settled) { c.Press.Step(dt); busy = true; }
                    else c.Press.Snap(c.Press.Target);
                }

                // Staggered entrance cascade
                if (entranceActive)
                {
                    float elapsed = unchecked((uint)(now - entranceStart)) / 1000f;
                    float totalDur = Anim.Response(0.24f) + maxEntranceRow * 0.022f;
                    if (elapsed >= totalDur) entranceActive = false;
                    else busy = true;
                }

                if (!busy) anim.Stop();
                Invalidate();
            }

            protected override void Dispose(bool disposing)
            {
                if (disposing) anim.Dispose();
                base.Dispose(disposing);
            }

            protected override void OnResize(EventArgs e)
            {
                base.OnResize(e);
                LayoutCards();
                ClampScroll(false);
                Invalidate();
            }

            float MaxScroll() { return Math.Max(0f, contentHeight - Height); }

            void ClampScroll(bool smooth)
            {
                float clamped = Math.Max(0f, Math.Min(MaxScroll(), scrollTarget));
                if (!smooth || Anim.Reduced || !Visible)
                {
                    scroll = scrollTarget = clamped;
                }
                else
                {
                    scrollTarget = clamped;
                    KickAnim();
                }
            }

            void RevealCard(int i, bool smooth)
            {
                if (i < 0 || i >= cards.Count) return;
                float s = Ui;
                var r = cards[i].Rect;
                float top = r.Top - 46 * s;
                float bot = r.Bottom + 20 * s;
                float target = scrollTarget;
                if (top < target) target = top;
                else if (bot > target + Height) target = bot - Height;
                target = Math.Max(0f, Math.Min(MaxScroll(), target));
                if (!smooth || Anim.Reduced || !Visible)
                {
                    scroll = scrollTarget = target;
                    Invalidate();
                }
                else if (Math.Abs(target - scrollTarget) > 0.5f)
                {
                    scrollTarget = target;
                    KickAnim();
                }
            }

            // Responsive asymmetrical Bento layout:
            // Wide (>= 740dp): Deck 0 = 2 Hero + 2 Wide; Deck 1 = 3 cols; Deck 2 = 2+1 featured + 3 cols; Deck 3 = 3 cols
            // Narrow (< 740dp): 2-column Bento layout with Hero cards taller
            void LayoutCards()
            {
                deckHeaders.Clear();
                if (cards.Count == 0 || Width <= 20) { contentHeight = 0; return; }
                float s = Ui;
                float maxW = 920f * s;
                float margin = Math.Max(22f * s, (Width - maxW) / 2f);
                colW = Math.Max(260f * s, Width - margin * 2f);
                padX = margin;
                float gap = 12f * s;
                bool wideLayout = colW >= 700f * s;

                float y = 18f * s;
                int rowCounter = 0;
                int curDeck = -1;
                int i = 0;
                while (i < cards.Count)
                {
                    int deck = cards[i].Deck;
                    if (deck != curDeck)
                    {
                        if (curDeck >= 0) y += 16f * s;
                        curDeck = deck;
                        string title = deck >= 0 && deck < deckTitles.Length ? deckTitles[deck] : "";
                        if (!string.IsNullOrEmpty(title))
                        {
                            deckHeaders.Add(new DeckHeader { Title = title, Y = y });
                            y += 24f * s;
                        }
                    }

                    // Collect all cards in this deck
                    int start = i;
                    while (i < cards.Count && cards[i].Deck == deck) i++;
                    int count = i - start;

                    if (deck == 0)
                    {
                        // Deck 0: Hero pair (look, island) + Wide pair (dock, general)
                        int pos = start;
                        if (count >= 2 && cards[pos].Featured && cards[pos + 1].Featured)
                        {
                            float w2 = (colW - gap) / 2f;
                            float h = 120f * s;
                            cards[pos].Rect = new RectangleF(padX, y, w2, h);
                            cards[pos].EntranceRow = rowCounter;
                            cards[pos + 1].Rect = new RectangleF(padX + w2 + gap, y, w2, h);
                            cards[pos + 1].EntranceRow = rowCounter;
                            y += h + gap;
                            rowCounter++;
                            pos += 2;
                        }
                        while (pos < i)
                        {
                            int take = Math.Min(2, i - pos);
                            float w = take == 2 ? (colW - gap) / 2f : colW;
                            float h = 98f * s;
                            for (int k = 0; k < take; k++)
                            {
                                cards[pos + k].Rect = new RectangleF(padX + k * (w + gap), y, w, h);
                                cards[pos + k].EntranceRow = rowCounter;
                            }
                            y += h + gap;
                            rowCounter++;
                            pos += take;
                        }
                    }
                    else if (deck == 2 && wideLayout && count >= 2 && cards[start].Featured)
                    {
                        // Asymmetrical Bento row: Featured AI card spans 2/3, Coding Agents takes 1/3
                        float unit = (colW - gap * 2f) / 3f;
                        float hFeat = 116f * s;
                        cards[start].Rect = new RectangleF(padX, y, unit * 2f + gap, hFeat);
                        cards[start].EntranceRow = rowCounter;
                        cards[start + 1].Rect = new RectangleF(padX + unit * 2f + gap * 2f, y, unit, hFeat);
                        cards[start + 1].EntranceRow = rowCounter;
                        y += hFeat + gap;
                        rowCounter++;

                        int pos = start + 2;
                        while (pos < i)
                        {
                            int cols = Math.Min(3, i - pos);
                            float w = (colW - gap * (cols - 1)) / cols;
                            float h = 100f * s;
                            for (int k = 0; k < cols; k++)
                            {
                                cards[pos + k].Rect = new RectangleF(padX + k * (w + gap), y, w, h);
                                cards[pos + k].EntranceRow = rowCounter;
                            }
                            y += h + gap;
                            rowCounter++;
                            pos += cols;
                        }
                    }
                    else
                    {
                        // Standard 3-col (wide) or 2-col (narrow) deck
                        int maxCols = wideLayout ? 3 : 2;
                        int pos = start;
                        while (pos < i)
                        {
                            int rem = i - pos;
                            int cols = Math.Min(maxCols, rem);
                            float w = (colW - gap * (cols - 1)) / cols;
                            float h = (cards[pos].Featured ? 116f : 100f) * s;
                            for (int k = 0; k < cols; k++)
                            {
                                cards[pos + k].Rect = new RectangleF(padX + k * (w + gap), y, w, h);
                                cards[pos + k].EntranceRow = rowCounter;
                            }
                            y += h + gap;
                            rowCounter++;
                            pos += cols;
                        }
                    }
                }
                maxEntranceRow = Math.Max(0, rowCounter - 1);
                contentHeight = y + 18f * s;
            }

            RectangleF ScreenRectOf(Card c)
            {
                var r = c.Rect;
                r.Y -= scroll;
                return r;
            }

            int HitCard(Point pt)
            {
                for (int i = 0; i < cards.Count; i++)
                    if (ScreenRectOf(cards[i]).Contains(pt)) return i;
                return -1;
            }

            void SetHover(int idx)
            {
                if (idx == hoverIndex) return;
                hoverIndex = idx;
                Cursor = hoverIndex >= 0 ? Cursors.Hand : Cursors.Default;
                SyncCardSprings();
            }

            void SetPress(int idx)
            {
                if (idx == pressIndex) return;
                if (pressIndex >= 0 && pressIndex < cards.Count)
                {
                    Springs.Tune(cards[pressIndex].Press, 0.14f, 0.82f);
                    cards[pressIndex].Press.Target = 0f;
                }
                pressIndex = idx;
                if (pressIndex >= 0 && pressIndex < cards.Count)
                {
                    Springs.Tune(cards[pressIndex].Press, 0.10f, 0.90f);
                    cards[pressIndex].Press.Target = 1f;
                }
                KickAnim();
            }

            void ActivateCard(int idx)
            {
                if (idx < 0 || idx >= cards.Count) return;
                focusIndex = idx;
                string id = cards[idx].Id;
                var h = Picked;
                if (h != null) h(id);
            }

            protected override void OnMouseWheel(MouseEventArgs e)
            {
                base.OnMouseWheel(e);
                float maxS = MaxScroll();
                if (maxS <= 0) return;
                float step = Math.Max(48f * Ui, SystemInformation.MouseWheelScrollLines * 26f * Ui);
                scrollTarget = Math.Max(0f, Math.Min(maxS, scrollTarget - (e.Delta / 120f) * step));
                if (Anim.Reduced)
                {
                    scroll = scrollTarget;
                    if (ClientRectangle.Contains(e.Location)) SetHover(HitCard(e.Location));
                    Invalidate();
                }
                else KickAnim();
            }

            protected override void OnMouseMove(MouseEventArgs e)
            {
                base.OnMouseMove(e);
                if (e.Location != lastMousePt)
                {
                    lastMousePt = e.Location;
                    KitInk.ByMouse = true;
                }
                SetHover(HitCard(e.Location));
            }

            protected override void OnMouseLeave(EventArgs e)
            {
                base.OnMouseLeave(e);
                lastMousePt = new Point(int.MinValue, int.MinValue);
                SetHover(-1);
                SetPress(-1);
            }

            protected override void OnGotFocus(EventArgs e)
            {
                base.OnGotFocus(e);
                SyncCardSprings();
                Invalidate();
            }

            protected override void OnLostFocus(EventArgs e)
            {
                base.OnLostFocus(e);
                SyncCardSprings();
                Invalidate();
            }

            protected override void OnMouseDown(MouseEventArgs e)
            {
                base.OnMouseDown(e);
                if (e.Button != MouseButtons.Left) return;
                Focus();
                int hit = HitCard(e.Location);
                if (hit >= 0) focusIndex = hit;
                SetPress(hit);
            }

            protected override void OnMouseUp(MouseEventArgs e)
            {
                base.OnMouseUp(e);
                int hit = HitCard(e.Location);
                bool fire = e.Button == MouseButtons.Left && hit >= 0 && hit == pressIndex;
                SetPress(-1);
                if (fire) ActivateCard(hit);
            }

            protected override bool IsInputKey(Keys key)
            {
                return key == Keys.Up || key == Keys.Down || key == Keys.Left || key == Keys.Right ||
                       key == Keys.Home || key == Keys.End || key == Keys.PageUp || key == Keys.PageDown ||
                       key == Keys.Enter || key == Keys.Space || base.IsInputKey(key);
            }

            // Spatial D-pad navigation: pick the closest card in the requested direction
            int SpatialNeighbor(int from, int dx, int dy)
            {
                if (from < 0 || from >= cards.Count) return 0;
                var src = cards[from].Rect;
                float sx = src.X + src.Width / 2f, sy = src.Y + src.Height / 2f;
                int best = -1;
                float bestScore = float.MaxValue;
                for (int i = 0; i < cards.Count; i++)
                {
                    if (i == from) continue;
                    var r = cards[i].Rect;
                    float cx = r.X + r.Width / 2f, cy = r.Y + r.Height / 2f;
                    float vx = cx - sx, vy = cy - sy;
                    if (dx != 0)
                    {
                        if (Math.Sign(vx) != dx) continue;
                        if (Math.Abs(vy) > Math.Max(src.Height, r.Height) * 0.75f) continue;
                        float score = Math.Abs(vx) + Math.Abs(vy) * 3f;
                        if (score < bestScore) { bestScore = score; best = i; }
                    }
                    else
                    {
                        if (Math.Sign(vy) != dy) continue;
                        float score = Math.Abs(vy) * 1.4f + Math.Abs(vx) * 0.9f;
                        if (score < bestScore) { bestScore = score; best = i; }
                    }
                }
                return best >= 0 ? best : from;
            }

            protected override void OnKeyDown(KeyEventArgs e)
            {
                base.OnKeyDown(e);
                if (cards.Count == 0) return;
                KitInk.ByMouse = false;
                if (e.KeyCode == Keys.Enter || e.KeyCode == Keys.Space)
                {
                    ActivateCard(focusIndex);
                    e.Handled = true;
                    return;
                }
                int next = focusIndex;
                if (e.KeyCode == Keys.Left) next = SpatialNeighbor(focusIndex, -1, 0);
                else if (e.KeyCode == Keys.Right) next = SpatialNeighbor(focusIndex, 1, 0);
                else if (e.KeyCode == Keys.Up) next = SpatialNeighbor(focusIndex, 0, -1);
                else if (e.KeyCode == Keys.Down) next = SpatialNeighbor(focusIndex, 0, 1);
                else if (e.KeyCode == Keys.Home) next = 0;
                else if (e.KeyCode == Keys.End) next = cards.Count - 1;
                else if (e.KeyCode == Keys.PageUp) next = SpatialNeighbor(SpatialNeighbor(focusIndex, 0, -1), 0, -1);
                else if (e.KeyCode == Keys.PageDown) next = SpatialNeighbor(SpatialNeighbor(focusIndex, 0, 1), 0, 1);
                else return;
                hoverIndex = -1;
                Cursor = Cursors.Default;
                if (next != focusIndex)
                {
                    focusIndex = next;
                    RevealCard(focusIndex, true);
                }
                SyncCardSprings();
                e.Handled = true;
            }

            protected override void OnPaint(PaintEventArgs e)
            {
                var g = e.Graphics;
                KitInk.Smooth(g);
                g.Clear(KitInk.Window);
                float s = Ui;

                // Deck headers
                Font deckFont = Ink.Semi(11f * s);
                for (int i = 0; i < deckHeaders.Count; i++)
                {
                    float hy = deckHeaders[i].Y - scroll;
                    if (hy + 24 * s < 0 || hy > Height) continue;
                    string label = deckHeaders[i].Title.ToUpperInvariant();
                    KitInk.Draw(g, label, deckFont, KitInk.Faint, new RectangleF(padX + 2 * s, hy, colW, 18 * s), ContentAlignment.MiddleLeft);
                }

                // Draw non-hovered cards first, then hovered/focused card on top so elevation shadow never clips under neighbors
                int topCard = ActiveCardIndex();
                for (int pass = 0; pass < 2; pass++)
                {
                    for (int i = 0; i < cards.Count; i++)
                    {
                        bool isTop = i == topCard;
                        if ((pass == 0 && isTop) || (pass == 1 && !isTop)) continue;
                        DrawCard(g, i, s);
                    }
                }

                // Subtle custom scrollbar thumb on right edge when scrollable
                float maxS = MaxScroll();
                if (maxS > 1f)
                {
                    float trackH = Height - 16 * s;
                    float thumbH = Math.Max(32 * s, trackH * (Height / contentHeight));
                    float thumbY = 8 * s + (scroll / maxS) * (trackH - thumbH);
                    var thumb = new RectangleF(Width - 7 * s, thumbY, 4 * s, thumbH);
                    KitInk.FillRound(g, thumb, 2 * s, Ink.Mix(KitInk.Window, KitInk.Text, Ink.Light ? 0.18f : 0.22f));
                }
            }

            void DrawCard(Graphics g, int idx, float s)
            {
                var c = cards[idx];
                var r = ScreenRectOf(c);

                // Staggered spring entrance offset
                float entranceScale = 1f;
                if (entranceActive && !Anim.Reduced)
                {
                    float elapsed = unchecked((uint)(Environment.TickCount - entranceStart)) / 1000f;
                    float delay = c.EntranceRow * 0.022f;
                    float t = Ease.Clamp01((elapsed - delay) / Math.Max(0.08f, Anim.Response(0.22f)));
                    float eased = Ease.OutCubic(t);
                    r.Y += (1f - eased) * 16f * s;
                    entranceScale = 0.965f + 0.035f * eased;
                }

                if (r.Bottom < -24 * s || r.Top > Height + 24 * s) return;

                float h = Math.Max(0f, Math.Min(1.25f, c.Hover.Value));
                float p = Math.Max(0f, Math.Min(1.25f, c.Press.Value));
                bool kbFocus = Focused && !KitInk.ByMouse && idx == focusIndex;
                float active = h;

                // Tactile console scale & lift: subtle grow on hover/focus, crisp compress on press
                float scale = entranceScale * (1f + 0.022f * active - 0.028f * p);
                float liftY = -2.2f * s * active + 1.5f * s * p;
                float nw = r.Width * scale, nh = r.Height * scale;
                var drawR = new RectangleF(
                    r.X + (r.Width - nw) / 2f,
                    r.Y + (r.Height - nh) / 2f + liftY,
                    nw, nh);

                float radius = (c.Featured ? 14f : 12f) * s;
                Color tile = string.IsNullOrEmpty(c.TileKey) ? KitInk.Accent : Ink.Label(c.TileKey, true);
                if (tile.IsEmpty) tile = Ink.Label("slate", true);

                // Contact elevation shadow when lifted
                if (active > 0.06f)
                    KitInk.Lift(g, drawR, radius, s * (0.7f + 0.7f * active));

                // Card surface with subtle category tint wash on hover/focus (never gaudy neon)
                float tintAmt = (c.Featured ? 0.045f : 0.02f) + (Ink.Light ? 0.055f : 0.095f) * active;
                Color cardBg = Ink.Mix(KitInk.Group, tile, Math.Min(0.18f, tintAmt));
                KitInk.FillRound(g, drawR, radius, cardBg);

                // Crisp border: hairline normally, category accent on hover/focus
                Color edgeColor = active > 0.04f
                    ? Ink.Mix(KitInk.Line, tile, Math.Min(1f, 0.75f * active))
                    : KitInk.Line;
                float edgeW = (1f + 0.65f * active) * s;
                KitInk.StrokeRound(g, drawR, radius, edgeColor, edgeW);

                // Featured Hero cards get a refined left vertical accent bar
                if (c.Featured)
                {
                    float barH = drawR.Height * (0.38f + 0.18f * active);
                    var bar = new RectangleF(drawR.X + 4.5f * s, drawR.Y + (drawR.Height - barH) / 2f, 3f * s, barH);
                    KitInk.FillRound(g, bar, 1.5f * s, Ink.Mix(KitInk.Line, tile, 0.55f + 0.45f * active));
                }

                // Icon tile
                float pad = (c.Featured ? 14f : 13f) * s;
                float iconSize = (c.Featured ? 40f : 34f) * s;
                float iconX = drawR.X + pad + (c.Featured ? 4f * s : 0f);
                float iconY = drawR.Y + pad;
                var iconRect = new RectangleF(iconX, iconY, iconSize, iconSize);
                if (c.Glyph == KitRow.MarkGlyph)
                {
                    g.DrawIcon(KitWindow.AppIcon, Rectangle.Round(iconRect));
                }
                else
                {
                    KitInk.FillRound(g, iconRect, (c.Featured ? 11f : 9f) * s, tile);
                    KitInk.Draw(g, Ink.Str(c.Glyph), Ink.Icons((c.Featured ? 19f : 16f) * s), Ink.On(tile), iconRect, ContentAlignment.MiddleCenter);
                }

                // Chevron, status pill, and palette dots aligned on one crisp horizontal axis
                float pillH = 20f * s;
                float pillY = iconY + (iconSize - pillH) / 2f;
                float chevShift = 4f * s * active;
                var chevRect = new RectangleF(drawR.Right - 28f * s + chevShift, pillY, 18f * s, pillH);
                Color chevColor = active > 0.05f ? Ink.Mix(KitInk.Faint, tile, active) : KitInk.Faint;
                KitInk.Draw(g, "\uE76C", Ink.Icons(10.5f * s), chevColor, chevRect, ContentAlignment.MiddleCenter);

                // Live status pill (top-right, left of chevron)
                float statusLeft = drawR.Right - 30f * s;
                if (!string.IsNullOrEmpty(c.Status))
                {
                    Font sf = Ink.Semi(10.5f * s);
                    int sw = TextRenderer.MeasureText(c.Status, sf).Width;
                    float pillW = Math.Min(drawR.Width * 0.48f, sw + 14f * s);
                    float pillX = drawR.Right - 30f * s - pillW;
                    // Don't collide with icon
                    if (pillX > iconRect.Right + 8f * s)
                    {
                        var pillR = new RectangleF(pillX, pillY, pillW, pillH);
                        Color pillBg = Ink.Mix(cardBg, tile, Ink.Light ? 0.13f : 0.22f);
                        KitInk.FillRound(g, pillR, pillH / 2f, pillBg);
                        KitInk.Draw(g, c.Status, sf, KitInk.Text, pillR, ContentAlignment.MiddleCenter);
                        statusLeft = pillX - 6f * s;
                    }
                }

                // Live 3-dot theme preview on the Appearance hero card
                if (c.ShowPaletteDots && statusLeft > iconRect.Right + 54f * s)
                {
                    float dot = 9f * s, dotGap = 4f * s;
                    float dy = pillY + (pillH - dot) / 2f;
                    float dx = statusLeft - (dot * 3 + dotGap * 2) - 4f * s;
                    Color[] dots = { KitInk.Window, KitInk.Group, KitInk.Accent };
                    for (int k = 0; k < 3; k++)
                    {
                        var dr = new RectangleF(dx + k * (dot + dotGap), dy, dot, dot);
                        g.FillEllipse(KitInk.BrushOf(dots[k]), dr);
                        g.DrawEllipse(KitInk.PenOf(KitInk.Line, 1f * s), dr);
                    }
                }

                // Title & subtitle
                if (c.Featured)
                {
                    float tx = iconX;
                    float tw = drawR.Width - (tx - drawR.X) - pad;
                    float titleY = iconRect.Bottom + 8f * s;
                    KitInk.Draw(g, c.Title, Ink.Semi(15f * s), KitInk.Text, new RectangleF(tx, titleY, tw, 22f * s), ContentAlignment.MiddleLeft);
                    KitInk.Draw(g, c.Subtitle, Ink.Text(11.5f * s), KitInk.Sub, new RectangleF(tx, titleY + 21f * s, tw, 18f * s), ContentAlignment.MiddleLeft);
                }
                else
                {
                    float tx = iconX;
                    float tw = drawR.Width - (tx - drawR.X) - pad;
                    float titleY = iconRect.Bottom + 8f * s;
                    KitInk.Draw(g, c.Title, Ink.Semi(13.5f * s), KitInk.Text, new RectangleF(tx, titleY, tw, 20f * s), ContentAlignment.MiddleLeft);
                    KitInk.Draw(g, c.Subtitle, Ink.Text(11f * s), KitInk.Sub, new RectangleF(tx, titleY + 19f * s, tw, 16f * s), ContentAlignment.MiddleLeft);
                }

                // Keyboard focus ring
                if (kbFocus)
                    KitInk.FocusRing(g, drawR, radius, KitInk.Window, s);
            }

            protected override AccessibleObject CreateAccessibilityInstance() { return new HubAccess(this); }

            class HubAccess : ControlAccessibleObject
            {
                readonly SettingsHub owner;
                public HubAccess(SettingsHub owner) : base(owner) { this.owner = owner; }
                public override AccessibleRole Role { get { return AccessibleRole.List; } }
                public override int GetChildCount() { return owner.cards.Count; }
                public override AccessibleObject GetChild(int i) { return new CardAccess(owner, i); }
            }

            class CardAccess : AccessibleObject
            {
                readonly SettingsHub owner;
                readonly int index;
                public CardAccess(SettingsHub owner, int index) { this.owner = owner; this.index = index; }
                public override string Name { get { return owner.cards[index].Title; } }
                public override string Description { get { return owner.cards[index].Subtitle; } }
                public override AccessibleRole Role { get { return AccessibleRole.PushButton; } }
                public override AccessibleStates State
                {
                    get
                    {
                        var st = AccessibleStates.Focusable;
                        if (owner.focusIndex == index) st |= AccessibleStates.Focused | AccessibleStates.Selected;
                        return st;
                    }
                }
                public override Rectangle Bounds
                {
                    get
                    {
                        var r = Rectangle.Round(owner.ScreenRectOf(owner.cards[index]));
                        return owner.RectangleToScreen(r);
                    }
                }
                public override void DoDefaultAction() { owner.ActivateCard(index); }
            }
        }
    }
}
