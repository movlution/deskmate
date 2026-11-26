using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace Deskmate
{
    partial class Island
    {
        // Fluid frosted-glass right-click menu for dock apps
        sealed class AppSheet : GlassSheet
        {
            const float Pad = 12f, HeaderH = 54f, PillH = 32f, PillGap = 6f, WinRowH = 28f, RowH = 34f, SepGap = 9f, SectionGap = 8f;

            readonly DockApp app;
            readonly Spring hoverY = new Spring(0, 560, 36, 0.1f);
            readonly Spring hoverAlpha = new Spring(0, 520, 36, 0.01f);
            readonly Spring dangerTint = new Spring(0, 420, 34, 0.01f);

            int winFirst, visWins, totalWinCount, keyIdx = -1;
            bool scrollWins;
            float maxLayoutH = 900f, topSepY;
            RectangleF headerRect, listRect;

            internal sealed class MenuItemInfo
            {
                public string Id = "", Label = "";
                public char Icon;
                public bool Danger, Enabled;
                public RectangleF Rect;
            }

            sealed class PillDef
            {
                public string Id = "", Label = "";
                public char Icon;
                public bool Enabled;
                public RectangleF Rect;
            }

            sealed class WinRowDef
            {
                public string Id = "", CloseId = "", Title = "";
                public bool Active;
                public RectangleF Rect, CloseRect;
            }

            sealed class RowDef
            {
                public string Id = "", Label = "";
                public char Icon;
                public bool Danger, SepBefore, HasFileActions;
                public RectangleF Rect, CopyRect, PropsRect;
            }

            readonly List<PillDef> pills = new List<PillDef>();
            readonly List<WinRowDef> winRows = new List<WinRowDef>();
            readonly List<RowDef> rows = new List<RowDef>();

            internal readonly List<MenuItemInfo> Items = new List<MenuItemInfo>();
            internal readonly List<string> ActionIds = new List<string>();
            internal DockApp App { get { return app; } }

            [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();

            public AppSheet(Island isle, DockWindow dock, DockApp app) : base(isle, dock, "appmenu", "Deskmate App Menu")
            {
                this.app = app;
                W = 282;
            }

            protected override void Opening(Rectangle mon, int barTop)
            {
                maxLayoutH = Math.Max(140f, (barTop - mon.Top) / scale - 40f);
                H = BuildLayout(maxLayoutH);
            }

            float InnerW { get { return W - Pad * 2f; } }

            void AddItem(string id, string label, char icon, bool danger, bool enabled, RectangleF rect)
            {
                ActionIds.Add(id);
                Items.Add(new MenuItemInfo
                {
                    Id = id,
                    Label = label,
                    Icon = icon,
                    Danger = danger,
                    Enabled = enabled,
                    Rect = rect
                });
            }

            float BuildLayout(float availH)
            {
                Items.Clear();
                ActionIds.Clear();
                pills.Clear();
                winRows.Clear();
                rows.Clear();

                float y = Pad;
                headerRect = new RectangleF(Pad, y, InnerW, HeaderH);
                AddItem("header", app.Name, '\0', false, true, headerRect);
                y += HeaderH + SectionGap;

                // Prepare action row definitions first so multi-window height capping stays exact
                var pendingRows = new List<RowDef>();
                string target = app.Target.Length > 0 ? app.Target : app.Exe;
                if (target.Length > 0)
                {
                    pendingRows.Add(new RowDef
                    {
                        Id = "row:launch",
                        Label = app.Running ? Lang.T("Jendela baru") : Lang.T("Buka"),
                        Icon = '\uE7AC'
                    });
                    if (!app.Packaged && !ShellApp.IsAppsFolder(target))
                    {
                        pendingRows.Add(new RowDef
                        {
                            Id = "row:admin",
                            Label = Lang.T("Jalankan sebagai administrator"),
                            Icon = '\uE7EF'
                        });
                    }
                }
                if (app.Pinned)
                {
                    pendingRows.Add(new RowDef
                    {
                        Id = "row:pin",
                        Label = Lang.T("Lepas dari Dock"),
                        Icon = '\uE77A'
                    });
                }
                else if (target.Length > 0)
                {
                    pendingRows.Add(new RowDef
                    {
                        Id = "row:pin",
                        Label = Lang.T("Sematkan di Dock"),
                        Icon = '\uE718'
                    });
                }

                string file = ShellApp.ResolveFileTarget(app.Target, app.Exe);
                if (file.Length > 0 && !app.Packaged)
                {
                    pendingRows.Add(new RowDef
                    {
                        Id = "row:folder",
                        Label = Lang.T("Buka lokasi berkas"),
                        Icon = '\uE8DA',
                        HasFileActions = true,
                    });
                }

                if (app.Running)
                {
                    pendingRows.Add(new RowDef
                    {
                        Id = "row:close",
                        Label = app.Wins.Count > 1 ? Lang.F("Tutup semua jendela ({0})", app.Wins.Count) : Lang.T("Tutup jendela"),
                        Icon = '\uE8BB',
                        SepBefore = true
                    });
                    bool isExplorer = string.Equals(app.AppId, "Microsoft.Windows.Explorer", StringComparison.OrdinalIgnoreCase)
                        || string.Equals(Path.GetFileName(app.Exe), "explorer.exe", StringComparison.OrdinalIgnoreCase);
                    if (isExplorer)
                    {
                        pendingRows.Add(new RowDef
                        {
                            Id = "row:restartExplorer",
                            Label = Lang.T("Mulai ulang Explorer"),
                            Icon = '\uE72C'
                        });
                    }
                    else
                    {
                        pendingRows.Add(new RowDef
                        {
                            Id = "row:endtask",
                            Label = Lang.T("Akhiri tugas"),
                            Icon = '\uE711',
                            Danger = true
                        });
                    }
                }

                float actionsH = SepGap + pendingRows.Count * RowH + Pad;
                if (app.Running) actionsH += SepGap;

                if (app.Running)
                {
                    if (app.Wins.Count == 1)
                    {
                        IntPtr h = app.Wins[0].H;
                        bool isMin = ShellApp.IsMinimized(h);
                        bool isMax = ShellApp.IsMaximized(h);
                        float pw = (InnerW - PillGap * 2f) / 3f;
                        pills.Add(new PillDef
                        {
                            Id = "winFocus",
                            Label = isMin ? Lang.T("Pulihkan") : Lang.T("Fokus"),
                            Icon = isMin ? '\uE923' : '\uE8A7',
                            Enabled = true,
                            Rect = new RectangleF(Pad, y, pw, PillH)
                        });
                        pills.Add(new PillDef
                        {
                            Id = "winMin",
                            Label = Lang.T("Minimal"),
                            Icon = '\uE921',
                            Enabled = !isMin,
                            Rect = new RectangleF(Pad + pw + PillGap, y, pw, PillH)
                        });
                        pills.Add(new PillDef
                        {
                            Id = "winMax",
                            Label = isMax ? Lang.T("Normal") : Lang.T("Maksimal"),
                            Icon = isMax ? '\uE923' : '\uE922',
                            Enabled = true,
                            Rect = new RectangleF(Pad + (pw + PillGap) * 2f, y, pw, PillH)
                        });
                        for (int i = 0; i < pills.Count; i++)
                            AddItem(pills[i].Id, pills[i].Label, pills[i].Icon, false, pills[i].Enabled, pills[i].Rect);
                        y += PillH + SectionGap;
                    }
                    else
                    {
                        totalWinCount = Math.Min(app.Wins.Count, 10);
                        float bulkAndGap = 4f + PillH + SectionGap;
                        int maxFit = (int)Math.Floor((availH - y - bulkAndGap - actionsH) / WinRowH);
                        visWins = Math.Max(1, Math.Min(Math.Min(4, totalWinCount), maxFit));
                        scrollWins = totalWinCount > visWins;
                        winFirst = Math.Max(0, Math.Min(winFirst, totalWinCount - visWins));

                        IntPtr fg = GetForegroundWindow();
                        float listY0 = y;
                        float winW = scrollWins ? InnerW - 6f : InnerW;
                        for (int i = 0; i < visWins; i++)
                        {
                            int idx = winFirst + i;
                            var w = app.Wins[idx];
                            string title = dock.WindowTitle(w.H);
                            if (string.IsNullOrEmpty(title)) title = w.Title;
                            if (string.IsNullOrEmpty(title)) title = app.Name;
                            var wr = new RectangleF(Pad, y, winW, WinRowH);
                            var cr = new RectangleF(wr.Right - 24f, wr.Y + (WinRowH - 22f) / 2f, 22f, 22f);
                            var wrd = new WinRowDef
                            {
                                Id = "win:" + idx,
                                CloseId = "winClose" + idx,
                                Title = title,
                                Active = (w.H == fg),
                                Rect = wr,
                                CloseRect = cr
                            };
                            winRows.Add(wrd);
                            AddItem(wrd.Id, title, '\0', false, true, wr);
                            AddItem(wrd.CloseId, Lang.T("Tutup"), '\uE8BB', false, true, cr);
                            y += WinRowH;
                        }
                        listRect = new RectangleF(Pad, listY0, InnerW, y - listY0);
                        y += 4f;

                        bool anyNotMin = false, anyMin = false;
                        for (int i = 0; i < app.Wins.Count; i++)
                        {
                            if (ShellApp.IsMinimized(app.Wins[i].H)) anyMin = true;
                            else anyNotMin = true;
                        }
                        float pw = (InnerW - PillGap) / 2f;
                        pills.Add(new PillDef
                        {
                            Id = "winMinAll",
                            Label = Lang.T("Minimalkan semua"),
                            Icon = '\uE921',
                            Enabled = anyNotMin,
                            Rect = new RectangleF(Pad, y, pw, PillH)
                        });
                        pills.Add(new PillDef
                        {
                            Id = "winRestAll",
                            Label = Lang.T("Pulihkan semua"),
                            Icon = '\uE923',
                            Enabled = anyMin,
                            Rect = new RectangleF(Pad + pw + PillGap, y, pw, PillH)
                        });
                        for (int i = 0; i < pills.Count; i++)
                            AddItem(pills[i].Id, pills[i].Label, pills[i].Icon, false, pills[i].Enabled, pills[i].Rect);
                        y += PillH + SectionGap;
                    }
                }

                topSepY = y + SepGap / 2f;
                y += SepGap;

                for (int i = 0; i < pendingRows.Count; i++)
                {
                    var row = pendingRows[i];
                    if (row.SepBefore) y += SepGap;
                    var rr = new RectangleF(Pad, y, InnerW, RowH);
                    row.Rect = rr;
                    if (row.HasFileActions)
                    {
                        row.CopyRect = new RectangleF(rr.Right - 54f, rr.Y + 4f, 24f, 26f);
                        row.PropsRect = new RectangleF(rr.Right - 28f, rr.Y + 4f, 24f, 26f);
                    }
                    rows.Add(row);
                    AddItem(row.Id, row.Label, row.Icon, row.Danger, true, rr);
                    if (row.HasFileActions)
                    {
                        AddItem("row:copyPath", Lang.T("Salin jalur"), '\uE8C8', false, true, row.CopyRect);
                        AddItem("row:props", Lang.T("Properti"), '\uE946', false, true, row.PropsRect);
                    }
                    y += RowH;
                }

                y += Pad;
                return y;
            }

            protected override void Draw()
            {
                BuildLayout(H > 0 ? H : maxLayoutH);
                DrawHeader();
                if (app.Running)
                {
                    if (app.Wins.Count > 1) DrawWindowList();
                    DrawPills();
                }
                g.DrawLine(p.P(p.T.Line, 1f), Pad + 4f, topSepY, W - Pad - 4f, topSepY);
                DrawGlidingHighlight();
                DrawRows();
            }

            void DrawHeader()
            {
                Hit(headerRect, "header");
                bool hot = hotId == "header", down = Down("header");
                var sq = Squish("header", headerRect, 0.04f);
                p.Module(headerRect, 14f, down ? p.T.FillPressed : hot ? p.T.FillHover : p.T.Fill);

                var iconRect = new RectangleF(headerRect.X + 10f, headerRect.Y + (HeaderH - 32f) / 2f, 32f, 32f);
                Bitmap ic = dock.IconFor(app.Key);
                if (ic != null)
                {
                    p.Image(ic, iconRect, 7f);
                }
                else
                {
                    p.FillRound(iconRect, 8f, p.T.FillHover);
                    string ch = !string.IsNullOrEmpty(app.Name) ? app.Name.Substring(0, 1).ToUpperInvariant() : "?";
                    p.TextCenter(ch, Ink.Semi(13.5f), p.T.Text, iconRect.X + 16f, iconRect.Y + 16f);
                }

                if (app.Running)
                {
                    float dx = iconRect.Right - 2.5f, dy = iconRect.Bottom - 2.5f;
                    p.Circle(dx, dy, 4.8f, Ink.A(Ink.Veil, 0.92f));
                    p.Circle(dx, dy, 3.2f, p.T.Positive);
                }

                float tx = iconRect.Right + 10f;
                float tw = headerRect.Right - tx - 10f;
                p.Text(app.Name, Ink.Semi(13.5f), p.T.Text, tx, headerRect.Y + HeaderH * 0.36f, tw, StringAlignment.Near);
                string sub = app.Running
                    ? (app.Wins.Count > 1 ? Lang.F("Sedang berjalan · {0} jendela", app.Wins.Count) : Lang.T("Berjalan"))
                    : Lang.T("Disematkan");
                p.Text(sub, Ink.Small(11f), app.Running ? p.T.Sub : p.T.Faint, tx, headerRect.Y + HeaderH * 0.69f, tw, StringAlignment.Near);
                Unsquish(headerRect, sq);
            }

            void DrawPills()
            {
                var font = Ink.Semi(11f);
                for (int i = 0; i < pills.Count; i++)
                {
                    var pill = pills[i];
                    var r = pill.Rect;
                    if (pill.Enabled) Hit(r, pill.Id);
                    bool hot = pill.Enabled && hotId == pill.Id;
                    bool down = pill.Enabled && Down(pill.Id);
                    var sq = pill.Enabled ? Squish(pill.Id, r, 0.06f) : new RectangleF(0, 0, 1, 1);

                    Color bg = !pill.Enabled
                        ? Ink.A(p.T.Fill, 0.55f)
                        : down ? p.T.FillPressed : hot ? p.T.FillHover : p.T.Fill;
                    p.Module(r, 11f, bg);

                    Color fg = !pill.Enabled ? p.T.Quaternary : p.T.Text;
                    Color icColor = !pill.Enabled ? p.T.Quaternary : hot ? p.T.Text : p.T.Sub;

                    float cy = r.Y + r.Height / 2f;
                    float textW = Math.Min(r.Width - 28f, p.Measure(pill.Label, font));
                    float contentW = 14f + 4f + textW;
                    float x0 = r.X + Math.Max(7f, (r.Width - contentW) / 2f);
                    p.Icon(pill.Icon, 12f, icColor, x0 + 6f, cy);
                    p.Text(pill.Label, font, fg, x0 + 17f, cy, r.Right - (x0 + 17f) - 5f, StringAlignment.Near);

                    if (pill.Enabled) Unsquish(r, sq);
                }
            }

            void DrawWindowList()
            {
                for (int i = 0; i < winRows.Count; i++)
                {
                    var wr = winRows[i];
                    Hit(wr.Rect, wr.Id);
                    Hit(wr.CloseRect, wr.CloseId);
                    bool rowHot = hotId == wr.Id || hotId == wr.CloseId;
                    bool closeHot = hotId == wr.CloseId;
                    if (rowHot) p.FillRound(wr.Rect, 9f, pressId == wr.Id ? p.T.FillPressed : p.T.FillHover);

                    float cy = wr.Rect.Y + wr.Rect.Height / 2f;
                    if (wr.Active) p.Circle(wr.Rect.X + 11f, cy, 3f, p.T.Accent);
                    else p.Circle(wr.Rect.X + 11f, cy, 2f, Ink.A(p.T.Sub, 0.3f));

                    p.Text(wr.Title, Ink.Small(12f), wr.Active ? p.T.Text : p.T.Sub, wr.Rect.X + 21f, cy, wr.CloseRect.X - wr.Rect.X - 24f, StringAlignment.Near);

                    if (closeHot)
                    {
                        var sq = Squish(wr.CloseId, wr.CloseRect, 0.08f);
                        p.FillRound(wr.CloseRect, 7f, Down(wr.CloseId) ? p.T.FillPressed : p.T.FillHover);
                        p.Cross(wr.CloseRect.X + wr.CloseRect.Width / 2f, wr.CloseRect.Y + wr.CloseRect.Height / 2f, 8f, p.T.Text);
                        Unsquish(wr.CloseRect, sq);
                    }
                    else if (rowHot)
                    {
                        p.Cross(wr.CloseRect.X + wr.CloseRect.Width / 2f, wr.CloseRect.Y + wr.CloseRect.Height / 2f, 8f, p.T.Sub);
                    }
                }

                if (scrollWins && totalWinCount > visWins)
                {
                    float trackH = listRect.Height - 4f;
                    float thumbH = Math.Max(16f, trackH * visWins / totalWinCount);
                    float thumbY = listRect.Y + 2f + (trackH - thumbH) * winFirst / Math.Max(1, totalWinCount - visWins);
                    p.FillRound(new RectangleF(W - Pad - 3f, thumbY, 3f, thumbH), 1.5f, p.T.Line);
                }
            }

            string RowIdFor(string id)
            {
                if (string.IsNullOrEmpty(id)) return null;
                if (id == "row:copyPath" || id == "row:props") return "row:folder";
                if (id.StartsWith("row:", StringComparison.Ordinal)) return id;
                return null;
            }

            string ActiveRowId()
            {
                string id = RowIdFor(hotId);
                if (id == null && keyIdx >= 0 && keyIdx < rows.Count) id = rows[keyIdx].Id;
                return id;
            }

            void DrawGlidingHighlight()
            {
                if (hoverAlpha.Value <= 0.005f) return;
                var pillRect = new RectangleF(Pad, hoverY.Value, InnerW, RowH);
                string activeRow = ActiveRowId();
                var sq = activeRow != null ? Squish(activeRow, pillRect, 0.03f) : new RectangleF(0, 0, 1, 1);
                Color mixed = Ink.Mix(p.T.FillHover, Ink.A(p.T.Negative, 0.22f), Ease.Clamp01(dangerTint.Value));
                p.FillRound(pillRect, 11f, Ink.A(mixed, Ease.Clamp01(hoverAlpha.Value)));
                if (activeRow != null) Unsquish(pillRect, sq);
            }

            void DrawRows()
            {
                for (int i = 0; i < rows.Count; i++)
                {
                    var row = rows[i];
                    if (row.SepBefore)
                    {
                        float sepY = row.Rect.Y - SepGap / 2f;
                        g.DrawLine(p.P(p.T.Line, 1f), Pad + 4f, sepY, W - Pad - 4f, sepY);
                    }

                    Hit(row.Rect, row.Id);
                    var sq = Squish(row.Id, row.Rect, 0.03f);
                    float cy = row.Rect.Y + row.Rect.Height / 2f;
                    bool isHot = ActiveRowId() == row.Id;
                    Color fg = row.Danger ? p.T.Negative : p.T.Text;
                    Color icColor = row.Danger ? p.T.Negative : (isHot ? p.T.Text : p.T.Sub);

                    p.Icon(row.Icon, 13.5f, icColor, row.Rect.X + 18f, cy);
                    float maxTextW = row.HasFileActions ? row.CopyRect.X - (row.Rect.X + 36f) - 6f : row.Rect.Width - 46f;
                    p.Text(row.Label, Ink.Semi(12.5f), fg, row.Rect.X + 36f, cy, maxTextW, StringAlignment.Near);
                    Unsquish(row.Rect, sq);

                    if (row.HasFileActions)
                    {
                        DrawTrailingIcon(row.CopyRect, "row:copyPath", '\uE8C8', Lang.T("Salin jalur"));
                        DrawTrailingIcon(row.PropsRect, "row:props", '\uE946', Lang.T("Properti"));
                    }
                }
            }

            void DrawTrailingIcon(RectangleF r, string id, char glyph, string tip)
            {
                Hit(r, id);
                bool hot = hotId == id;
                bool down = Down(id);
                if (hot) Tip(r, tip);
                var sq = Squish(id, r, 0.08f);
                if (hot) p.FillRound(r, 7f, down ? p.T.FillPressed : p.T.FillHover);
                p.Icon(glyph, 12.5f, hot ? p.T.Text : p.T.Sub, r.X + r.Width / 2f, r.Y + r.Height / 2f);
                Unsquish(r, sq);
            }

            protected override void HoverMoved(string id)
            {
                if (id != null) keyIdx = -1;
                string rowId = RowIdFor(id);
                if (rowId == null && keyIdx >= 0 && keyIdx < rows.Count) rowId = rows[keyIdx].Id;

                RowDef targetRow = null;
                if (rowId != null)
                {
                    for (int i = 0; i < rows.Count; i++)
                    {
                        if (rows[i].Id == rowId) { targetRow = rows[i]; break; }
                    }
                }

                if (targetRow != null)
                {
                    if (hoverAlpha.Value < 0.02f) hoverY.Snap(targetRow.Rect.Y);
                    hoverY.Target = targetRow.Rect.Y;
                    hoverAlpha.Target = 1f;
                    dangerTint.Target = targetRow.Danger ? 1f : 0f;
                }
                else
                {
                    hoverAlpha.Target = 0f;
                    dangerTint.Target = 0f;
                }
                Animate();
            }

            protected override bool Animating(float dt)
            {
                if (Anim.Reduced)
                {
                    hoverY.Snap(hoverY.Target);
                    hoverAlpha.Snap(hoverAlpha.Target);
                    dangerTint.Snap(dangerTint.Target);
                    return false;
                }
                hoverY.Step(dt);
                hoverAlpha.Step(dt);
                dangerTint.Step(dt);
                return !hoverY.Settled || !hoverAlpha.Settled || !dangerTint.Settled;
            }

            protected override void Wheel(string id, int delta)
            {
                if (!scrollWins || totalWinCount <= visWins) return;
                int step = delta > 0 ? -1 : 1;
                int next = Math.Max(0, Math.Min(totalWinCount - visWins, winFirst + step));
                if (next != winFirst)
                {
                    winFirst = next;
                    BuildLayout(maxLayoutH);
                }
            }

            protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
            {
                if (Leaving) return true;
                if (keyData == Keys.Escape) { Done(); return true; }
                if (rows.Count > 0)
                {
                    if (keyData == Keys.Up)
                    {
                        keyIdx = keyIdx < 0 ? rows.Count - 1 : (keyIdx - 1 + rows.Count) % rows.Count;
                        ApplyKeyFocus();
                        return true;
                    }
                    if (keyData == Keys.Down)
                    {
                        keyIdx = keyIdx < 0 ? 0 : (keyIdx + 1) % rows.Count;
                        ApplyKeyFocus();
                        return true;
                    }
                    if (keyData == Keys.Enter && keyIdx >= 0 && keyIdx < rows.Count)
                    {
                        Clicked(rows[keyIdx].Id, MouseButtons.Left);
                        return true;
                    }
                }
                return base.ProcessCmdKey(ref msg, keyData);
            }

            void ApplyKeyFocus()
            {
                if (keyIdx < 0 || keyIdx >= rows.Count) return;
                var row = rows[keyIdx];
                hotId = row.Id;
                if (hoverAlpha.Value < 0.02f) hoverY.Snap(row.Rect.Y);
                hoverY.Target = row.Rect.Y;
                hoverAlpha.Target = 1f;
                dangerTint.Target = row.Danger ? 1f : 0f;
                Animate();
                Render();
            }

            protected override void Clicked(string id, MouseButtons button)
            {
                if (button != MouseButtons.Left || string.IsNullOrEmpty(id)) return;
                if (id == "header")
                {
                    dock.FocusOrOpenApp(app);
                    Done();
                    return;
                }

                if (app.Running && app.Wins.Count == 1)
                {
                    IntPtr h = app.Wins[0].H;
                    if (id == "winFocus") { dock.FocusWindow(app, h); Done(); return; }
                    if (id == "winMin") { if (!ShellApp.IsMinimized(h)) { dock.MinimizeWindow(h); Done(); } return; }
                    if (id == "winMax") { if (ShellApp.IsMaximized(h)) ShellApp.Restore(h); else ShellApp.Maximize(h); Done(); return; }
                }

                if (id.StartsWith("win:", StringComparison.Ordinal))
                {
                    int idx;
                    if (int.TryParse(id.Substring(4), out idx) && idx >= 0 && idx < app.Wins.Count)
                    {
                        dock.FocusWindow(app, app.Wins[idx].H);
                        Done();
                    }
                    return;
                }

                if (id.StartsWith("winClose", StringComparison.Ordinal))
                {
                    int idx;
                    if (int.TryParse(id.Substring(8), out idx) && idx >= 0 && idx < app.Wins.Count)
                    {
                        if (dock.CloseWindow(app.Wins[idx].H)) Done();
                    }
                    return;
                }

                if (id == "winMinAll")
                {
                    for (int i = 0; i < app.Wins.Count; i++) ShellApp.Minimize(app.Wins[i].H);
                    Done();
                    return;
                }

                if (id == "winRestAll")
                {
                    for (int i = 0; i < app.Wins.Count; i++) ShellApp.Restore(app.Wins[i].H);
                    Done();
                    return;
                }

                if (id == "row:launch") { dock.LaunchApp(app, false); Done(); return; }
                if (id == "row:admin") { dock.LaunchApp(app, true); Done(); return; }
                if (id == "row:pin")
                {
                    if (app.Pinned) dock.UnpinApp(app);
                    else dock.PinApp(app);
                    Done();
                    return;
                }

                if (id == "row:folder")
                {
                    string file = ShellApp.ResolveFileTarget(app.Target, app.Exe);
                    if (file.Length > 0) dock.RevealFile(file);
                    Done();
                    return;
                }

                if (id == "row:copyPath")
                {
                    string file = ShellApp.ResolveFileTarget(app.Target, app.Exe);
                    if (file.Length > 0)
                    {
                        try { Clipboard.SetText(file); } catch (Exception) { }
                        dock.ShowCopiedToast(file);
                    }
                    Done();
                    return;
                }

                if (id == "row:props")
                {
                    string file = ShellApp.ResolveFileTarget(app.Target, app.Exe);
                    if (file.Length > 0) ShellApp.ShowProperties(file);
                    Done();
                    return;
                }

                if (id == "row:close")
                {
                    if (app.Wins.Count == 1) dock.CloseWindow(app.Wins[0].H);
                    else for (int i = 0; i < app.Wins.Count; i++) ShellApp.Close(app.Wins[i].H);
                    Done();
                    return;
                }

                if (id == "row:restartExplorer")
                {
                    System.Threading.ThreadPool.QueueUserWorkItem(delegate
                    {
                        ShellApp.RestartExplorer();
                        if (!dock.IsDisposed && dock.IsHandleCreated)
                            dock.BeginInvoke(new Action(delegate { dock.RescanApps(); }));
                    });
                    Done();
                    return;
                }

                if (id == "row:endtask")
                {
                    dock.EndApp(app);
                    Done();
                    return;
                }
            }
        }
    }
}
