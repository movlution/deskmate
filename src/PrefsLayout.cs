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
        partial class PrefsWindow
        {
            // Check now; found build installs here
            KitRow UpdateRow()
            {
                KitRow row = null;
                KitButton b = null;
                Action show = delegate
                {
                    if (row.IsDisposed) return;
                    bool found = island.newVersion != null;
                    b.Text = found ? (island.newAsset != null ? Lang.T("Perbarui") : "GitHub") : Lang.T("Cek pembaruan");
                    b.Primary = found;
                    b.Enabled = !island.checking && !island.updating;
                    b.Width = TextRenderer.MeasureText(b.Text, b.Font).Width + (int)(32 * Ui);
                    row.Detail = island.UpdateText();
                    b.AccessibleDescription = row.Detail;
                    if (row.Parent != null) PlaceField(row);
                    b.Invalidate();
                    row.Invalidate();
                };
                b = Button(Lang.T("Cek pembaruan"), false, delegate
                {
                    if (island.newVersion != null) island.InstallUpdate(this);
                    else island.CheckUpdate(true);
                });
                row = Row(Lang.T("Pembaruan"), island.UpdateText(), b);
                show();
                island.UpdateChanged += show;
                row.Disposed += delegate { island.UpdateChanged -= show; };
                return row;
            }

            // Tab row: grip | icon, name, switch
            readonly Dictionary<string, Control> tabFocus = new Dictionary<string, Control>();

            KitRow TabRow(int page)
            {
                int at = Array.IndexOf(island.tabs, page);
                string name = Lang.T(PageNames[page]);
                var sw = new KitSwitch { Checked = at >= 0, Size = new Size((int)(38 * Ui), (int)(22 * Ui)) };
                sw.CheckedChanged += delegate
                {
                    if (building) return;
                    var list = new List<int>(island.tabs);
                    if (sw.Checked && !list.Contains(page)) list.Add(page);
                    else if (!sw.Checked && list.Count > 1) list.Remove(page);
                    island.SetLayout("panel.tabs", island.TabIds(list));
                    Preview();
                    RefillTabs(page + "sw");
                };
                tabFocus[page + "sw"] = sw;
                var r = Row(name, null, sw);
                r.Lead = 38;
                if (at < 0) return r;
                r.Divider = true;
                var grip = new TabGrip { Size = new Size((int)(30 * Ui), (int)(30 * Ui)), AccessibleName = Lang.F("Urutan {0}", name), AccessibleDescription = Lang.T("Seret, atau tekan panah atas dan bawah") };
                grip.Location = new Point((int)(5 * Ui), (r.Height - grip.Height) / 2);
                grip.Dropped = to => MoveTabTo(page, to);
                grip.Nudge = step => MoveTabTo(page, at + step);
                r.Controls.Add(grip);
                tabFocus[page + "grip"] = grip;
                return r;
            }

            // Screen sketch; the slider below follows the pick
            KitRow PlaceRow()
            {
                var picker = new PlacePicker { Selected = island.placeMode, Size = new Size((int)(212 * Ui), (int)(104 * Ui)) };
                var row = Row(Lang.T("Letak"), Lang.T(PlaceNames[island.placeMode]), picker);
                row.Height = (int)(128 * Ui);
                row.Glyph = '';
                picker.AccessibleName = Lang.T("Letak");
                picker.Changed += i =>
                {
                    if (building) return;
                    island.SetPlace(i);
                    row.Detail = Lang.T(PlaceNames[i]);
                    row.Invalidate();
                    RefillTabs("place");
                };
                tabFocus["place"] = picker;
                return row;
            }

            // Card row: grip | icon, name, note, switch
            KitRow CardRow(HomeCard card)
            {
                int at = Array.IndexOf(island.homeCards, card);
                string name = Lang.T(HomeNames[(int)card]), key = "h" + (int)card;
                var sw = new KitSwitch { Checked = at >= 0, Size = new Size((int)(38 * Ui), (int)(22 * Ui)) };
                sw.CheckedChanged += delegate
                {
                    if (building) return;
                    var list = new List<HomeCard>(island.homeCards);
                    if (sw.Checked && !list.Contains(card)) list.Add(card);
                    else if (!sw.Checked) list.Remove(card);
                    island.SetHomeCards(list);
                    Preview(PageHome);
                    RefillTabs(key + "sw");
                };
                tabFocus[key + "sw"] = sw;
                var r = Row(name, Lang.T(HomeNotes[(int)card]), sw);
                r.Glyph = HomeGlyphs[(int)card];
                r.Lead = 38;
                if (at < 0) return r;
                r.Divider = true;
                var grip = new TabGrip { Size = new Size((int)(30 * Ui), (int)(30 * Ui)), AccessibleName = Lang.F("Urutan {0}", name), AccessibleDescription = Lang.T("Seret, atau tekan panah atas dan bawah") };
                grip.Location = new Point((int)(5 * Ui), (r.Height - grip.Height) / 2);
                grip.Dropped = to => MoveCardTo(card, to);
                grip.Nudge = step => MoveCardTo(card, at + step);
                r.Controls.Add(grip);
                tabFocus[key + "grip"] = grip;
                return r;
            }

            void MoveCardTo(HomeCard card, int to)
            {
                var list = new List<HomeCard>(island.homeCards);
                int i = list.IndexOf(card);
                if (i < 0 || to < 0 || to >= list.Count || to == i) return;
                list.RemoveAt(i);
                list.Insert(to, card);
                island.SetHomeCards(list);
                Preview(PageHome);
                RefillTabs("h" + (int)card + "grip");
            }

            void MoveTabTo(int page, int to)
            {
                var list = new List<int>(island.tabs);
                int i = list.IndexOf(page);
                if (i < 0 || to < 0 || to >= list.Count || to == i) return;
                list.RemoveAt(i);
                list.Insert(to, page);
                island.SetLayout("panel.tabs", island.TabIds(list));
                Preview();
                RefillTabs(page + "grip");
            }

            // Rebuild rows; scroll and focus stay
            void RefillTabs(string focusKey)
            {
                var at = content.AutoScrollPosition;
                bool refocus = ContainsFocus;
                BeginInvoke(new Action(delegate
                {
                    Fill();
                    content.AutoScrollPosition = new Point(-at.X, -at.Y);
                    Control c;
                    if (refocus && tabFocus.TryGetValue(focusKey, out c) && c.Visible) c.Focus();
                }));
            }

            // Six-dot handle: drag, or arrow keys; sibling rows smoothly glide into place
            class TabGrip : KitControl
            {
                public Action<int> Dropped, Nudge;
                KitRow row;
                List<KitRow> peers;
                readonly Dictionary<KitRow, float> peerPos = new Dictionary<KitRow, float>();
                readonly Dictionary<KitRow, int> peerTarget = new Dictionary<KitRow, int>();
                readonly Timer settleAnim = new Timer { Interval = 15 };
                int grabY, startTop, baseTop, slot, target, lastTick;
                bool dragging;

                public TabGrip()
                {
                    Cursor = Cursors.SizeNS;
                    settleAnim.Tick += delegate
                    {
                        if (!dragging || row == null || row.Parent == null) { settleAnim.Stop(); return; }
                        int now = Environment.TickCount;
                        float dt = Math.Max(0.004f, Math.Min(0.05f, unchecked((uint)(now - lastTick)) / 1000f));
                        lastTick = now;
                        float rate = Math.Min(1f, dt / Math.Max(0.025f, Anim.Response(0.06f)));
                        bool moving = false;
                        foreach (var kv in new List<KeyValuePair<KitRow, int>>(peerTarget))
                        {
                            var p = kv.Key;
                            float cur = peerPos.ContainsKey(p) ? peerPos[p] : p.Top;
                            float want = kv.Value;
                            cur = Anim.Reduced ? want : cur + (want - cur) * rate;
                            if (Math.Abs(want - cur) < 0.6f) cur = want;
                            else moving = true;
                            peerPos[p] = cur;
                            int rounded = (int)Math.Round(cur);
                            if (p.Top != rounded) p.Top = rounded;
                        }
                        if (!moving) settleAnim.Stop();
                        row.Parent.Invalidate();
                    };
                }

                protected override void Dispose(bool disposing) { if (disposing) settleAnim.Dispose(); base.Dispose(disposing); }

                protected override bool IsInputKey(Keys k) { return k == Keys.Up || k == Keys.Down || base.IsInputKey(k); }

                protected override void OnKeyDown(KeyEventArgs e)
                {
                    base.OnKeyDown(e);
                    if ((e.KeyCode != Keys.Up && e.KeyCode != Keys.Down) || Nudge == null) return;
                    e.Handled = true;
                    Nudge(e.KeyCode == Keys.Up ? -1 : 1);
                }

                static bool Shown(KitRow r)
                {
                    foreach (Control c in r.Controls) if (c is TabGrip) return true;
                    return false;
                }

                // Group space: scrolling keeps the row under the pointer
                int GroupY(MouseEventArgs e) { return row.Parent.PointToClient(PointToScreen(e.Location)).Y; }

                protected override void OnMouseDown(MouseEventArgs e)
                {
                    base.OnMouseDown(e);
                    if (e.Button != MouseButtons.Left || Parent == null) return;
                    row = Parent as KitRow;
                    if (row == null || row.Parent == null) return;
                    peers = new List<KitRow>();
                    foreach (Control c in row.Parent.Controls)
                    {
                        var r = c as KitRow;
                        if (r != null && Shown(r)) peers.Add(r);
                    }
                    peers.Sort((a, b) => a.Top.CompareTo(b.Top));
                    peerPos.Clear();
                    peerTarget.Clear();
                    foreach (var p in peers) { peerPos[p] = p.Top; peerTarget[p] = p.Top; }
                    slot = target = peers.IndexOf(row);
                    baseTop = peers[0].Top;
                    startTop = row.Top;
                    grabY = GroupY(e);
                    dragging = true;
                    Capture = true;
                    row.BringToFront();
                    Lift(true);
                }

                protected override void OnMouseMove(MouseEventArgs e)
                {
                    base.OnMouseMove(e);
                    if (!dragging) return;
                    int h = row.Height, max = baseTop + (peers.Count - 1) * h;
                    int top = Math.Max(baseTop, Math.Min(max, startTop + GroupY(e) - grabY));
                    row.Top = top;
                    target = Math.Max(0, Math.Min(peers.Count - 1, (top - baseTop + h / 2) / h));
                    // Others smoothly close ranks around the gap
                    int k = 0;
                    bool needAnim = false;
                    foreach (var p in peers)
                    {
                        if (p == row) continue;
                        if (k == target) k++;
                        int want = baseTop + k * h;
                        peerTarget[p] = want;
                        if (Anim.Reduced) { p.Top = want; peerPos[p] = want; }
                        else if (Math.Abs(p.Top - want) > 0) needAnim = true;
                        k++;
                    }
                    if (needAnim && !settleAnim.Enabled)
                    {
                        lastTick = Environment.TickCount;
                        settleAnim.Start();
                    }
                    row.Parent.Invalidate();
                }

                protected override void OnMouseUp(MouseEventArgs e)
                {
                    base.OnMouseUp(e);
                    Drop();
                }

                protected override void OnMouseCaptureChanged(EventArgs e)
                {
                    base.OnMouseCaptureChanged(e);
                    if (!Capture) Drop();
                }

                void Drop()
                {
                    if (!dragging) return;
                    dragging = false;
                    Capture = false;
                    settleAnim.Stop();
                    foreach (var kv in peerTarget) kv.Key.Top = kv.Value;
                    Lift(false);
                    row.Top = baseTop + target * row.Height;
                    if (target != slot && Dropped != null) Dropped(target);
                }

                // Held row takes the hover tint
                void Lift(bool on)
                {
                    row.BackColor = on ? KitInk.Hover : Color.Transparent;
                    row.Invalidate(true);
                }

                protected override void OnPaint(PaintEventArgs e)
                {
                    var g = e.Graphics;
                    KitInk.Smooth(g);
                    g.Clear(KitInk.Under(this));
                    float s = Ui, cx = Width / 2f, cy = Height / 2f, d = 3f * s;
                    if (Hovered && !dragging) Fill(g, new RectangleF(0.5f, 0.5f, Width - 1, Height - 1), Height / 2f, KitInk.Line);
                    var b = KitInk.BrushOf(dragging || Hovered || Focused ? KitInk.Text : KitInk.Sub);
                    for (int x = -1; x <= 1; x += 2)
                        for (int y = -1; y <= 1; y++)
                            g.FillEllipse(b, cx + x * 3f * s - d / 2, cy + y * 5f * s - d / 2, d, d);
                    Ring(g, new RectangleF(1, 1, Width - 2, Height - 2), Height / 2f);
                }
            }

            static bool? cachedAutoStart;

            Control StartupSwitch()
            {
                var s = new KitSwitch { Size = new Size((int)(38 * Ui), (int)(22 * Ui)) };
                if (indexing) return s;
                bool syncing = false;
                if (cachedAutoStart.HasValue) s.Checked = cachedAutoStart.Value;
                if (!Settings.TestMode)
                {
                    // Query schtasks off the UI thread so opening Umum or searching never stalls
                    System.Threading.ThreadPool.QueueUserWorkItem(delegate
                    {
                        bool actual;
                        try { actual = AutoStart.Enabled; } catch (Exception) { return; }
                        cachedAutoStart = actual;
                        if (IsDisposed || s.IsDisposed) return;
                        try
                        {
                            BeginInvoke(new Action(delegate
                            {
                                if (IsDisposed || s.IsDisposed || s.Checked == actual) return;
                                syncing = true; s.Checked = actual; syncing = false;
                            }));
                        }
                        catch (Exception) { }
                    });
                }
                s.CheckedChanged += delegate
                {
                    if (building || syncing || Settings.TestMode) return;
                    bool want = s.Checked;
                    s.Enabled = false;
                    System.Threading.ThreadPool.QueueUserWorkItem(delegate
                    {
                        try { AutoStart.Enabled = want; } catch (Exception ex) { Log.Write("autostart: " + ex.Message); }
                        bool actual = want;
                        try { actual = AutoStart.Enabled; } catch (Exception) { }
                        cachedAutoStart = actual;
                        if (IsDisposed || s.IsDisposed) return;
                        try
                        {
                            BeginInvoke(new Action(delegate
                            {
                                if (IsDisposed || s.IsDisposed) return;
                                // Reflect the real state so a failed change never leaves the switch lying.
                                if (actual != s.Checked) { syncing = true; s.Checked = actual; syncing = false; }
                                s.Enabled = true;
                            }));
                        }
                        catch (Exception) { }
                    });
                };
                return s;
            }

            Control AgentButton(bool present, bool on, Action act)
            {
                var b = Button(on ? Lang.T("Lepas") : Lang.T("Hubungkan"), !on && present, act);
                b.Enabled = present;
                return b;
            }

            protected override void Dispose(bool disposing)
            {
                if (disposing)
                {
                    searchTimer.Dispose();
                    preview.Dispose();
                    entranceTimer.Dispose();
                    settings.FlushSave();
                }
                base.Dispose(disposing);
            }
        }
    }
}
