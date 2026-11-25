using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Windows.Forms;

namespace Deskmate
{
    // Dock tray: anything dropped on the dock waits there, ready to be
    // dragged into another app (an upload field, a chat, a mail)
    partial class Island
    {
        readonly List<string> dockHeld = new List<string>();
        const int DockHeldMax = 20;

        // Paths only; the files stay where they are
        void HoldInDock(IEnumerable<string> paths)
        {
            var added = new List<string>();
            foreach (var raw in paths)
            {
                if (!ShelfSafe(raw)) continue;
                string full;
                try { full = Path.GetFullPath(raw); } catch (Exception) { continue; }
                dockHeld.RemoveAll(x => string.Equals(x, full, StringComparison.OrdinalIgnoreCase));
                dockHeld.Insert(0, full);
                added.Add(full);
            }
            if (dockHeld.Count > DockHeldMax) dockHeld.RemoveRange(DockHeldMax, dockHeld.Count - DockHeldMax);
            // The island's Rak keeps a copy, so a delivered file is still at hand
            if (added.Count > 0 && ShelfOn) AddToShelf(added);
            DockHeldChanged();
        }

        void ReleaseDockHeld(IEnumerable<string> paths)
        {
            foreach (var f in paths) dockHeld.RemoveAll(x => string.Equals(x, f, StringComparison.OrdinalIgnoreCase));
            DockHeldChanged();
        }

        void DockHeldChanged()
        {
            foreach (var dock in dockWins.Values) dock.HeldChanged();
        }

        Bitmap DockHeldThumb(string path)
        {
            var item = shelf.Find(s => string.Equals(s.Path, path, StringComparison.OrdinalIgnoreCase));
            return item != null ? item.Thumb : null;
        }

        sealed partial class DockWindow
        {
            readonly Spring shelfShow = ShelfSpring();
            // Something is being dragged over this dock; the dock's own tray is being dragged out
            bool fileOver, shelfDragOut;

            static Spring ShelfSpring()
            {
                var s = new Spring(0, 1, 1, 0.002f);
                Tune(s, 300, 0.82f);
                return s;
            }

            bool ShelfSlotOn { get { return S != null && S.GetBool("dockbar.shelf", true); } }
            List<string> Held { get { return isle.dockHeld; } }
            bool ShelfWanted { get { return ShelfSlotOn && (fileOver || shelfDragOut || Held.Count > 0); } }

            public void HeldChanged()
            {
                if (closing || !IsHandleCreated) return;
                Animate();
                Render();
            }

            void ShelfSlots()
            {
                float show = Ease.Clamp01(shelfShow.Value);
                if (!ShelfWanted && show < 0.01f) return;
                if (slots.Count > 0) slots.Add(new Slot { Id = "shelfsep", Sep = true, Show = show });
                slots.Add(new Slot { Id = "shelf", Show = show });
            }

            bool StepShelf(float dt)
            {
                shelfShow.Target = ShelfWanted ? 1 : 0;
                if (Anim.Reduced) shelfShow.Snap(shelfShow.Target);
                else shelfShow.Step(dt);
                return !shelfShow.Settled;
            }

            static bool Holdable(IDataObject d)
            {
                return d != null && (d.GetDataPresent(DataFormats.FileDrop) || d.GetDataPresent(DataFormats.Bitmap) ||
                                     d.GetDataPresent(DataFormats.UnicodeText) || d.GetDataPresent(DataFormats.Text));
            }

            // Files as they are; a picture or text becomes a small file to hand on
            static List<string> DropPaths(IDataObject d)
            {
                var list = new List<string>();
                try
                {
                    var files = d.GetDataPresent(DataFormats.FileDrop) ? d.GetData(DataFormats.FileDrop) as string[] : null;
                    if (files != null && files.Length > 0) { list.AddRange(files); return list; }
                    var bmp = d.GetDataPresent(DataFormats.Bitmap) ? d.GetData(DataFormats.Bitmap) as Image : null;
                    if (bmp != null)
                    {
                        string f = DropFile("Gambar", ".png");
                        bmp.Save(f, ImageFormat.Png);
                        list.Add(f);
                        return list;
                    }
                    string text = d.GetData(DataFormats.UnicodeText) as string ?? d.GetData(DataFormats.Text) as string;
                    if (!string.IsNullOrEmpty(text) && text.Trim().Length > 0)
                    {
                        string f = DropFile("Teks", ".txt");
                        File.WriteAllText(f, text, System.Text.Encoding.UTF8);
                        list.Add(f);
                    }
                }
                catch (Exception ex) { Log.Write("dock hold: " + ex.Message); }
                return list;
            }

            static string DropFile(string stem, string ext)
            {
                string dir = Path.Combine(Path.GetTempPath(), "Deskmate", "Dock");
                Directory.CreateDirectory(dir);
                // Yesterday's leftovers go
                try
                {
                    foreach (var old in Directory.GetFiles(dir))
                        if ((DateTime.UtcNow - File.GetLastWriteTimeUtc(old)).TotalDays > 1) File.Delete(old);
                }
                catch (Exception) { }
                string name = Lang.T(stem) + " " + DateTime.Now.ToString("yyyy-MM-dd HHmmss");
                string f = Path.Combine(dir, name + ext);
                for (int i = 2; File.Exists(f); i++) f = Path.Combine(dir, name + " (" + i + ")" + ext);
                return f;
            }

            // Press on the tray and move: the held files leave with the pointer
            void ShelfDragOut()
            {
                var paths = Held.FindAll(f => File.Exists(f) || Directory.Exists(f));
                pressId = null;
                SysPress("shelf").Target = 0;
                if (paths.Count == 0) { isle.ReleaseDockHeld(new List<string>(Held)); return; }
                shelfDragOut = true;
                labelShow.Target = 0;
                CloseCard();
                var done = DragDropEffects.None;
                try { done = DoDragDrop(new DataObject(DataFormats.FileDrop, paths.ToArray()), DragDropEffects.Copy | DragDropEffects.Link); }
                catch (Exception ex) { Log.Write("dock drag out: " + ex.Message); }
                finally { shelfDragOut = false; }
                // Delivered: the dock lets go; the island's Rak still has them
                if (done != DragDropEffects.None) isle.ReleaseDockHeld(paths);
                inside = false;
                hover.Target = 0;
                SetHover(null);
                if (HideMode != "never") { hideLater.Stop(); hideLater.Start(); }
                Animate();
                Render();
            }

            void OpenHeld()
            {
                if (isle.ShelfOn)
                {
                    isle.SetSegment(PageShelf, 0, true);
                    isle.Show(PageShelf);
                }
                else if (Held.Count > 0) ShowInFolder(Held[0]);
            }

            void ShelfMenu()
            {
                var m = new ContextMenuStrip();
                m.Items.Add(new ToolStripMenuItem(Lang.T("Ditahan di Dock")) { Enabled = false, Tag = new MenuItemMeta { Header = true } });
                m.Items.Add(new ToolStripSeparator());
                int n = 0;
                foreach (var f in Held)
                {
                    if (++n > 8) break;
                    string file = f;
                    m.Items.Add(new ToolStripMenuItem(Path.GetFileName(f.TrimEnd('\\')), null, delegate { ShowInFolder(file); }) { Tag = new MenuItemMeta { Glyph = '' } });
                }
                if (Held.Count > 0) m.Items.Add(new ToolStripSeparator());
                if (isle.ShelfOn) m.Items.Add(new ToolStripMenuItem(Lang.T("Buka Rak"), null, delegate { OpenHeld(); }) { Tag = new MenuItemMeta { Glyph = IcoShelf } });
                if (Held.Count > 0) m.Items.Add(new ToolStripMenuItem(Lang.T("Kosongkan"), null, delegate { isle.ReleaseDockHeld(new List<string>(Held)); }) { Tag = new MenuItemMeta { Glyph = '' } });
                ShowMenu(m);
            }

            string ShelfLabel()
            {
                if (fileOver && !shelfDragOut) return Lang.T("Lepas untuk menahan di Dock");
                if (Held.Count == 0) return Lang.T("Rak Dock");
                return Held.Count == 1 ? Lang.F("{0} · seret ke aplikasi", Path.GetFileName(Held[0].TrimEnd('\\'))) : Lang.F("{0} berkas · seret ke aplikasi", Held.Count);
            }

            // Empty: a dashed target. Holding: the newest file, with a count
            void DrawShelfGlyph(Slot s, RectangleF r, Color ink, bool hot)
            {
                float cx = r.X + r.Width / 2, cy = r.Y + r.Height / 2, u = r.Width / 42f;
                var box = Glyph(r);
                if (Held.Count == 0)
                {
                    using (var pen = new Pen(Ink.A(ink, (hot ? 0.75f : 0.5f) * Ease.Clamp01(s.Show)), 1.4f) { DashPattern = new[] { 3f, 2.5f } })
                    using (var path = Ink.Round(box, box.Width * 0.28f)) g.DrawPath(pen, path);
                    p.Icon(IcoShelf, 15 * u, ink, cx, cy);
                    return;
                }
                string first = Held[0];
                var thumb = isle.DockHeldThumb(first);
                // A second sheet peeks out behind when there are more
                if (Held.Count > 1) p.FillRound(new RectangleF(box.X + 3 * u, box.Y - 3 * u, box.Width, box.Height), box.Width * 0.24f, Ink.A(ink, 0.25f));
                if (thumb != null) p.Image(thumb, box, box.Width * 0.24f);
                else
                {
                    p.FillRound(box, box.Width * 0.24f, Ink.Mix(p.T.Fill, ink, 0.18f));
                    string ext = Directory.Exists(first) ? "" : Path.GetExtension(first).TrimStart('.').ToUpperInvariant();
                    if (ext.Length > 4) ext = ext.Substring(0, 4);
                    if (ext.Length > 0) p.TextCenter(ext, Ink.Semi(Math.Max(7f, box.Height * 0.3f)), ink, box.X + box.Width / 2, box.Y + box.Height / 2);
                    else p.Icon(Directory.Exists(first) ? '' : '', box.Height * 0.5f, ink, box.X + box.Width / 2, box.Y + box.Height / 2);
                }
                if (Held.Count > 1 && s.Show > 0.5f) HeldBadge(s.Icon, Held.Count);
            }

            void HeldBadge(RectangleF r, int n)
            {
                string t = n > 99 ? "99+" : n.ToString();
                var f = Ink.SmallSemi(11f);
                float h = 17, w = Math.Max(h, p.Measure(t, f) + 9);
                var b = new RectangleF(r.Right - w * 0.7f, r.Top - h * 0.28f, w, h);
                p.FillRound(Inset(b, -1.2f), (h + 2.4f) / 2, Ink.A(p.T.Bg, 0.55f));
                p.FillRound(b, h / 2, p.T.Accent);
                p.TextCenter(t, f, p.T.OnAccent, b.X + b.Width / 2, b.Y + h / 2);
            }
        }
    }
}
