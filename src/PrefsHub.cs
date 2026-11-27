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
                public Color TileColor;
                public int StatusWidth;
                public Bitmap RestCache;
                public int CacheW, CacheH;

                public void ClearCache()
                {
                    if (RestCache != null)
                    {
                        RestCache.Dispose();
                        RestCache = null;
                    }
                    CacheW = CacheH = 0;
                }
            }

            readonly List<Card> cards = new List<Card>();
            readonly List<DeckHeader> deckHeaders = new List<DeckHeader>();
            string[] deckTitles = new string[0];
            public event Action<string> Picked;

            struct DeckHeader
            {
                public string Title;
                public string UpperTitle;
                public float Y;
            }

            int hoverIndex = -1;
            int pressIndex = -1;
            int focusIndex = 0;
            int maxEntranceRow;
            int cacheThemeKey;
            Bitmap cachedAppIcon;
            int cachedAppIconPx;
            Point lastMousePt = new Point(int.MinValue, int.MinValue);
            float scroll, scrollTarget, contentHeight;
            bool entranceActive;
            double entranceStart;
            readonly KitPulse pulse;

            // Content column bounds (in control coordinates)
            float padX, colW;

            public bool HasCards { get { return cards.Count > 0; } }

            public SettingsHub()
            {
                TabStop = true;
                pulse = new KitPulse(StepAnim);
            }

            int CurrentThemeKey()
            {
                unchecked
                {
                    int h = KitInk.Window.ToArgb();
                    h = h * 397 ^ KitInk.Group.ToArgb();
                    h = h * 397 ^ KitInk.Accent.ToArgb();
                    h = h * 397 ^ KitInk.Line.ToArgb();
                    h = h * 397 ^ KitInk.Text.ToArgb();
                    h = h * 397 ^ (Ink.Light ? 1 : 0);
                    h = h * 397 ^ (int)Math.Round(Ui * 100f);
                    return h;
                }
            }

            public void InvalidateCaches()
            {
                for (int i = 0; i < cards.Count; i++) cards[i].ClearCache();
                if (cachedAppIcon != null) { cachedAppIcon.Dispose(); cachedAppIcon = null; cachedAppIconPx = 0; }
                cacheThemeKey = 0;
            }

            void RefreshCardMetrics(Card c)
            {
                Color tile = string.IsNullOrEmpty(c.TileKey) ? KitInk.Accent : Ink.Label(c.TileKey, true);
                if (tile.IsEmpty) tile = Ink.Label("slate", true);
                c.TileColor = tile;
                c.StatusWidth = string.IsNullOrEmpty(c.Status)
                    ? 0
                    : TextRenderer.MeasureText(c.Status, Ink.Semi(10.5f * Ui)).Width;
            }

            void EnsureThemeCacheValid()
            {
                int key = CurrentThemeKey();
                if (key == cacheThemeKey) return;
                InvalidateCaches();
                cacheThemeKey = key;
                for (int i = 0; i < cards.Count; i++) RefreshCardMetrics(cards[i]);
            }

            public void Bind(List<Card> items, string[] decks, string focusId)
            {
                InvalidateCaches();
                cards.Clear();
                if (items != null) cards.AddRange(items);
                deckTitles = decks ?? new string[0];
                hoverIndex = -1;
                pressIndex = -1;
                int idx = IndexOf(focusId);
                focusIndex = idx >= 0 ? idx : 0;
                cacheThemeKey = CurrentThemeKey();
                for (int i = 0; i < cards.Count; i++) RefreshCardMetrics(cards[i]);
                LayoutCards();
                Invalidate();
            }

            public void UpdateStatuses(Func<string, string> statusOf)
            {
                if (statusOf == null) return;
                bool anyChanged = false;
                for (int i = 0; i < cards.Count; i++)
                {
                    string next = statusOf(cards[i].Id) ?? "";
                    if (cards[i].Status != next)
                    {
                        cards[i].Status = next;
                        RefreshCardMetrics(cards[i]);
                        cards[i].ClearCache();
                        anyChanged = true;
                    }
                }
                if (anyChanged) Invalidate();
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
                if (cards.Count > 0 && contentHeight <= 0) LayoutCards();
                if (idx >= 0) RevealCard(idx, false);
                if (Anim.Reduced || !IsHandleCreated || !Visible)
                {
                    entranceActive = false;
                    Invalidate();
                    return;
                }
                entranceActive = true;
                entranceStart = KitAnim.Now;
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
                pulse.Start();
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
                bool any = false;
                for (int i = 0; i < cards.Count; i++)
                {
                    float want = i == active ? 1f : 0f;
                    if (Math.Abs(cards[i].Hover.Target - want) > 0.001f)
                    {
                        if (want > 0.5f) Springs.Tune(cards[i].Hover, 0.14f, 0.88f);
                        else Springs.Tune(cards[i].Hover, 0.18f, 0.94f);
                        cards[i].Hover.Target = want;
                        any = true;
                    }
                }
                if (any) KickAnim();
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
                pulse.Stop();
                Invalidate();
            }

            Rectangle DirtyCardBounds(Card c)
            {
                float s = Ui;
                var r = ScreenRectOf(c);
                float pad = 14f * s;
                return Rectangle.Ceiling(RectangleF.Inflate(r, pad, pad));
            }

            bool StepAnim(float dt)
            {
                if (IsDisposed || !Visible || Anim.Reduced)
                {
                    SnapAll();
                    return false;
                }
                bool busy = false;
                bool fullDirty = false;
                Rectangle dirty = Rectangle.Empty;

                // Smooth wheel inertia (frame-rate independent exponential decay)
                if (Math.Abs(scrollTarget - scroll) > 0.35f)
                {
                    scroll += (scrollTarget - scroll) * KitAnim.Decay(dt, 0.056f);
                    if (Math.Abs(scrollTarget - scroll) <= 0.35f) scroll = scrollTarget;
                    else busy = true;
                    fullDirty = true;
                    if (KitInk.ByMouse && lastMousePt.X != int.MinValue && ClientRectangle.Contains(lastMousePt))
                    {
                        int hit = HitCard(lastMousePt);
                        if (hit != hoverIndex) { hoverIndex = hit; Cursor = hoverIndex >= 0 ? Cursors.Hand : Cursors.Default; SyncCardSprings(); }
                    }
                }
                else if (scroll != scrollTarget)
                {
                    scroll = scrollTarget;
                    fullDirty = true;
                }

                // Per-card hover & press springs (dirty-rect tracked)
                for (int i = 0; i < cards.Count; i++)
                {
                    var c = cards[i];
                    bool moved = false;
                    if (!c.Hover.Settled) { c.Hover.Step(dt); moved = true; busy = true; }
                    else if (c.Hover.Value != c.Hover.Target) { c.Hover.Snap(c.Hover.Target); moved = true; }
                    if (!c.Press.Settled) { c.Press.Step(dt); moved = true; busy = true; }
                    else if (c.Press.Value != c.Press.Target) { c.Press.Snap(c.Press.Target); moved = true; }
                    if (moved && !fullDirty)
                    {
                        var cr = DirtyCardBounds(c);
                        dirty = dirty.IsEmpty ? cr : Rectangle.Union(dirty, cr);
                    }
                }

                // Staggered entrance cascade (lightweight cached blits)
                if (entranceActive)
                {
                    float elapsed = (float)(KitAnim.Now - entranceStart);
                    float totalDur = Anim.Response(0.18f) + maxEntranceRow * 0.015f;
                    fullDirty = true;
                    if (elapsed >= totalDur) entranceActive = false;
                    else busy = true;
                }

                if (fullDirty) Invalidate();
                else if (!dirty.IsEmpty) Invalidate(dirty);
                return busy;
            }

            protected override void Dispose(bool disposing)
            {
                if (disposing)
                {
                    pulse.Dispose();
                    InvalidateCaches();
                }
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
                            deckHeaders.Add(new DeckHeader { Title = title, UpperTitle = title.ToUpperInvariant(), Y = y });
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
                    Springs.Tune(cards[pressIndex].Press, 0.14f, 0.88f);
                    cards[pressIndex].Press.Target = 0f;
                }
                pressIndex = idx;
                if (pressIndex >= 0 && pressIndex < cards.Count)
                {
                    Springs.Tune(cards[pressIndex].Press, 0.09f, 0.92f);
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

            Bitmap AppIconBitmap(int px)
            {
                px = Math.Max(16, px);
                if (cachedAppIcon != null && cachedAppIconPx == px) return cachedAppIcon;
                if (cachedAppIcon != null) cachedAppIcon.Dispose();
                cachedAppIcon = new Bitmap(px, px, System.Drawing.Imaging.PixelFormat.Format32bppPArgb);
                using (var ig = Graphics.FromImage(cachedAppIcon))
                {
                    ig.InterpolationMode = InterpolationMode.HighQualityBicubic;
                    ig.DrawIcon(KitWindow.AppIcon, new Rectangle(0, 0, px, px));
                }
                cachedAppIconPx = px;
                return cachedAppIcon;
            }

            Bitmap EnsureRestCache(Card c, float s)
            {
                int cw = Math.Max(4, (int)Math.Ceiling(c.Rect.Width) + 2);
                int ch = Math.Max(4, (int)Math.Ceiling(c.Rect.Height) + 2);
                if (c.RestCache != null && c.CacheW == cw && c.CacheH == ch) return c.RestCache;
                c.ClearCache();
                var bmp = new Bitmap(cw, ch, System.Drawing.Imaging.PixelFormat.Format32bppPArgb);
                using (var bg = Graphics.FromImage(bmp))
                {
                    KitInk.Smooth(bg);
                    bg.Clear(KitInk.Window);
                    DrawCardBody(bg, c, new RectangleF(1f, 1f, c.Rect.Width, c.Rect.Height), 0f, false, s);
                }
                c.RestCache = bmp;
                c.CacheW = cw;
                c.CacheH = ch;
                return bmp;
            }

            protected override void OnPaint(PaintEventArgs e)
            {
                var g = e.Graphics;
                EnsureThemeCacheValid();
                KitInk.Smooth(g);
                var clip = e.ClipRectangle;
                g.FillRectangle(KitInk.BrushOf(KitInk.Window), clip);
                float s = Ui;
                double now = entranceActive ? KitAnim.Now : 0;

                // Deck headers (clip-culled)
                Font deckFont = Ink.Semi(11f * s);
                for (int i = 0; i < deckHeaders.Count; i++)
                {
                    float hy = deckHeaders[i].Y - scroll;
                    if (hy + 22 * s < clip.Top || hy > clip.Bottom) continue;
                    KitInk.Draw(g, deckHeaders[i].UpperTitle ?? deckHeaders[i].Title, deckFont, KitInk.Faint, new RectangleF(padX + 2 * s, hy, colW, 18 * s), ContentAlignment.MiddleLeft);
                }

                // Draw non-hovered cards first, then hovered/focused card on top so elevation shadow never clips under neighbors
                int topCard = ActiveCardIndex();
                for (int pass = 0; pass < 2; pass++)
                {
                    for (int i = 0; i < cards.Count; i++)
                    {
                        bool isTop = i == topCard;
                        if ((pass == 0 && isTop) || (pass == 1 && !isTop)) continue;
                        DrawCard(g, i, s, clip, now);
                    }
                }

                // Subtle custom scrollbar thumb on right edge when scrollable
                float maxS = MaxScroll();
                if (maxS > 1f && clip.Right >= Width - (int)Math.Ceiling(10 * s))
                {
                    float trackH = Height - 16 * s;
                    float thumbH = Math.Max(32 * s, trackH * (Height / contentHeight));
                    float thumbY = 8 * s + (scroll / maxS) * (trackH - thumbH);
                    var thumb = new RectangleF(Width - 7 * s, thumbY, 4 * s, thumbH);
                    KitInk.FillRound(g, thumb, 2 * s, Ink.Mix(KitInk.Window, KitInk.Text, Ink.Light ? 0.18f : 0.22f));
                }
            }

            void DrawCard(Graphics g, int idx, float s, Rectangle clip, double now)
            {
                var c = cards[idx];
                var r = ScreenRectOf(c);

                // Staggered lightweight entrance glide (pure translation + soft window-color fade veil, zero text shimmer)
                float entranceEased = 1f;
                if (entranceActive && !Anim.Reduced)
                {
                    float elapsed = (float)(now - entranceStart);
                    float delay = c.EntranceRow * 0.015f;
                    float t = Ease.Clamp01((elapsed - delay) / Math.Max(0.06f, Anim.Response(0.18f)));
                    entranceEased = Ease.OutQuart(t);
                    r.Y += (1f - entranceEased) * 10f * s;
                }

                if (r.Bottom + 16 * s < clip.Top || r.Top - 16 * s > clip.Bottom ||
                    r.Right + 16 * s < clip.Left || r.Left - 16 * s > clip.Right) return;

                float h = Math.Max(0f, Math.Min(1.15f, c.Hover.Value));
                float p = Math.Max(0f, Math.Min(1.15f, c.Press.Value));
                bool kbFocus = Focused && !KitInk.ByMouse && idx == focusIndex;

                // Fast path: blit cached resting bitmap (< 0.03ms per card, zero GDI TextRenderer roundtrips!)
                if (h <= 0.002f && p <= 0.002f && !kbFocus)
                {
                    var bmp = EnsureRestCache(c, s);
                    int dx = (int)Math.Round(r.X) - 1;
                    int dy = (int)Math.Round(r.Y) - 1;
                    g.DrawImageUnscaled(bmp, dx, dy);
                    if (entranceEased < 0.995f)
                    {
                        int veilA = (int)Math.Round((1f - entranceEased) * 195f);
                        if (veilA > 2)
                            g.FillRectangle(KitInk.BrushOf(Color.FromArgb(veilA, KitInk.Window)), dx, dy, bmp.Width, bmp.Height);
                    }
                    return;
                }

                // Interactive active card path (only 1-2 cards at a time, clip-culled to dirty rect)
                float scale = 1f + 0.012f * h - 0.018f * p;
                float liftY = -1.8f * s * h + 1.2f * s * p;
                float nw = r.Width * scale, nh = r.Height * scale;
                var drawR = new RectangleF(
                    r.X + (r.Width - nw) / 2f,
                    r.Y + (r.Height - nh) / 2f + liftY,
                    nw, nh);

                DrawCardBody(g, c, drawR, h, kbFocus, s);
            }

            void DrawCardBody(Graphics g, Card c, RectangleF drawR, float active, bool kbFocus, float s)
            {
                float radius = (c.Featured ? 14f : 12f) * s;
                Color tile = c.TileColor.IsEmpty ? KitInk.Accent : c.TileColor;

                // Contact elevation shadow when lifted
                if (active > 0.06f)
                    KitInk.Lift(g, drawR, radius, s * (0.6f + 0.6f * active));

                // Card surface with subtle category tint wash on hover/focus (never gaudy neon)
                float tintAmt = (c.Featured ? 0.045f : 0.02f) + (Ink.Light ? 0.055f : 0.095f) * active;
                Color cardBg = Ink.Mix(KitInk.Group, tile, Math.Min(0.18f, tintAmt));
                KitInk.FillRound(g, drawR, radius, cardBg);
                // Hero and wide cards carry their category scene along the right edge
                if (c.Featured || c.Wide) SettingsArt.DrawScene(g, c.Id, drawR, radius);

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
                    var icoBmp = AppIconBitmap((int)Math.Round(iconSize));
                    g.DrawImageUnscaled(icoBmp, (int)Math.Round(iconRect.X), (int)Math.Round(iconRect.Y));
                }
                else if (!SettingsArt.DrawTile(g, c.Id, iconRect))
                {
                    KitInk.FillRound(g, iconRect, (c.Featured ? 11f : 9f) * s, tile);
                    KitInk.Draw(g, Ink.Str(c.Glyph), Ink.Icons((c.Featured ? 19f : 16f) * s), Ink.On(tile), iconRect, ContentAlignment.MiddleCenter);
                }

                // Chevron, status pill, and palette dots aligned on one crisp horizontal axis
                float pillH = 20f * s;
                float pillY = iconY + (iconSize - pillH) / 2f;
                float chevShift = 3.5f * s * active;
                var chevRect = new RectangleF(drawR.Right - 28f * s + chevShift, pillY, 18f * s, pillH);
                Color chevColor = active > 0.05f ? Ink.Mix(KitInk.Faint, tile, active) : KitInk.Faint;
                KitInk.Draw(g, "\uE76C", Ink.Icons(10.5f * s), chevColor, chevRect, ContentAlignment.MiddleCenter);

                // Live status pill (top-right, left of chevron) — width cached, zero MeasureText in OnPaint
                float statusLeft = drawR.Right - 30f * s;
                if (!string.IsNullOrEmpty(c.Status))
                {
                    Font sf = Ink.Semi(10.5f * s);
                    float pillW = Math.Min(drawR.Width * 0.48f, c.StatusWidth + 14f * s);
                    float pillX = drawR.Right - 30f * s - pillW;
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
                float tx = iconX;
                float tw = drawR.Width - (tx - drawR.X) - pad;
                if (c.Featured || c.Wide)
                    tw = Math.Max(80f * s, Math.Min(tw, drawR.Right - SettingsArt.SceneReserve(c.Id, drawR.Height) - tx));
                float titleY = iconRect.Bottom + 8f * s;
                if (c.Featured)
                {
                    KitInk.Draw(g, c.Title, Ink.Semi(15f * s), KitInk.Text, new RectangleF(tx, titleY, tw, 22f * s), ContentAlignment.MiddleLeft);
                    KitInk.Draw(g, c.Subtitle, Ink.Text(11.5f * s), KitInk.Sub, new RectangleF(tx, titleY + 21f * s, tw, 18f * s), ContentAlignment.MiddleLeft);
                }
                else
                {
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
