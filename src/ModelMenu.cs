using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace Deskmate
{
    // Searchable list of the models a provider offers; many servers list hundreds
    sealed class ModelMenu : ToolStripDropDown
    {
        const int MaxRows = 12;

        readonly TaskAddBox query = new TaskAddBox();
        readonly ListBox results = new ListBox();
        readonly List<KeyValuePair<string, string>> models;
        readonly ToolStripControlHost resultHost;
        readonly Action<string> apply;
        readonly Font rowFont, noteFont;
        readonly int rowHeight;
        readonly string current;

        sealed class Item
        {
            public readonly string Id, Note;
            public Item(string id, string note) { Id = id; Note = note ?? ""; }
            public override string ToString() { return Id; }
        }

        public ModelMenu(float scale, List<KeyValuePair<string, string>> models, string current, Action<string> apply)
        {
            this.models = models;
            this.current = current ?? "";
            this.apply = apply;
            int width = (int)(340 * scale);
            rowHeight = (int)(28 * scale);
            rowFont = Ink.Text(13 * scale);
            noteFont = Ink.Text(11 * scale);
            Renderer = new MenuLook();
            Padding = new Padding((int)(7 * scale));

            string cue = Lang.F("Cari dari {0} model", models.Count);
            query.AccessibleName = cue;
            query.Font = rowFont;
            query.ForeColor = KitInk.Text;
            query.BackColor = KitInk.Group;
            query.BorderStyle = BorderStyle.FixedSingle;
            query.HandleCreated += delegate { Native.SetCue(query.Handle, cue + "…"); };
            query.Width = width;

            results.AccessibleName = Lang.T("Model");
            results.DrawMode = DrawMode.OwnerDrawFixed;
            results.IntegralHeight = false;
            results.ItemHeight = rowHeight;
            results.Font = rowFont;
            results.BackColor = KitInk.Window;
            results.ForeColor = KitInk.Text;
            results.BorderStyle = BorderStyle.None;
            results.Width = width;
            results.DrawItem += DrawModel;
            results.MouseUp += delegate(object sender, MouseEventArgs e)
            {
                if (e.Button == MouseButtons.Left && results.IndexFromPoint(e.Location) >= 0) Choose();
            };
            results.KeyDown += OnListKey;
            query.KeyDown += OnQueryKey;
            query.TextChanged += delegate { Filter(); };
            Items.Add(new ToolStripControlHost(query) { AutoSize = false, Size = query.Size, Margin = new Padding(0, 0, 0, 5) });
            results.Height = rowHeight * Math.Max(1, Math.Min(MaxRows, models.Count));
            resultHost = new ToolStripControlHost(results) { AutoSize = false, Size = results.Size, Margin = Padding.Empty };
            Items.Add(resultHost);
            Filter();
            Opened += delegate { query.Focus(); };
        }

        // Every word must appear: "son 5" finds claude-sonnet-5-5
        internal static bool Matches(string id, string note, string q)
        {
            foreach (var word in q.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries))
                if (id.IndexOf(word, StringComparison.OrdinalIgnoreCase) < 0 && note.IndexOf(word, StringComparison.OrdinalIgnoreCase) < 0) return false;
            return true;
        }

        void Filter()
        {
            string q = query.Text.Trim();
            results.BeginUpdate();
            results.Items.Clear();
            int pick = 0;
            foreach (var m in models)
            {
                if (!Matches(m.Key, m.Value ?? "", q)) continue;
                if (m.Key.Equals(current, StringComparison.OrdinalIgnoreCase)) pick = results.Items.Count;
                results.Items.Add(new Item(m.Key, m.Value));
            }
            // A name typed in full can be used even when the list does not have it
            if (q.Length > 0 && q.IndexOf(' ') < 0 && !HasExact(q)) results.Items.Add(new Item(q, Lang.T("pakai nama ini")));
            if (results.Items.Count != 0) results.SelectedIndex = q.Length > 0 ? 0 : pick;
            else results.Items.Add(Lang.T("Model tidak ditemukan"));
            int rows = Math.Max(1, Math.Min(MaxRows, results.Items.Count));
            results.Height = rowHeight * rows;
            resultHost.Height = rowHeight * rows;
            results.EndUpdate();
        }

        bool HasExact(string q)
        {
            foreach (var m in models) if (m.Key.Equals(q, StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        void OnQueryKey(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Down || e.KeyCode == Keys.Up || e.KeyCode == Keys.PageDown || e.KeyCode == Keys.PageUp)
            {
                int n = results.Items.Count;
                if (n > 0 && results.SelectedIndex >= 0)
                {
                    int step = e.KeyCode == Keys.Down ? 1 : e.KeyCode == Keys.Up ? -1 : e.KeyCode == Keys.PageDown ? MaxRows : -MaxRows;
                    int at = results.SelectedIndex + step;
                    results.SelectedIndex = Math.Abs(step) == 1 ? (at + n) % n : Math.Max(0, Math.Min(n - 1, at));
                }
                e.SuppressKeyPress = true;
            }
            else OnListKey(sender, e);
        }

        void OnListKey(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; Choose(); }
            else if (e.KeyCode == Keys.Escape) { e.SuppressKeyPress = true; Close(ToolStripDropDownCloseReason.Keyboard); }
        }

        void Choose()
        {
            var selected = results.SelectedItem as Item;
            if (selected == null) return;
            Close(ToolStripDropDownCloseReason.ItemClicked);
            apply(selected.Id);
        }

        void DrawModel(object sender, DrawItemEventArgs e)
        {
            bool selected = (e.State & DrawItemState.Selected) != 0;
            using (var background = new SolidBrush(selected ? KitInk.Hover : KitInk.Window))
                e.Graphics.FillRectangle(background, e.Bounds);
            if (e.Index < 0) return;
            var item = results.Items[e.Index] as Item;
            if (item == null)
            {
                TextRenderer.DrawText(e.Graphics, results.Items[e.Index].ToString(), rowFont, e.Bounds, KitInk.Sub,
                    TextFormatFlags.VerticalCenter | TextFormatFlags.HorizontalCenter | TextFormatFlags.NoPrefix);
                return;
            }
            int pad = e.Bounds.Height / 3;
            bool isCurrent = item.Id.Equals(current, StringComparison.OrdinalIgnoreCase);
            var mark = new Rectangle(e.Bounds.X, e.Bounds.Y, pad * 2, e.Bounds.Height);
            if (isCurrent)
                TextRenderer.DrawText(e.Graphics, "✓", rowFont, mark, KitInk.Text, TextFormatFlags.VerticalCenter | TextFormatFlags.HorizontalCenter | TextFormatFlags.NoPrefix);
            int noteWidth = item.Note.Length == 0 ? 0 : Math.Min(e.Bounds.Width * 2 / 5, TextRenderer.MeasureText(item.Note, noteFont).Width + pad);
            var label = new Rectangle(mark.Right, e.Bounds.Y, Math.Max(0, e.Bounds.Width - mark.Width - noteWidth - pad), e.Bounds.Height);
            TextRenderer.DrawText(e.Graphics, item.Id, rowFont, label, KitInk.Text,
                TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
            if (noteWidth > 0)
            {
                var note = new Rectangle(e.Bounds.Right - noteWidth - pad / 2, e.Bounds.Y, noteWidth, e.Bounds.Height);
                TextRenderer.DrawText(e.Graphics, item.Note, noteFont, note, KitInk.Sub,
                    TextFormatFlags.VerticalCenter | TextFormatFlags.Right | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
            }
        }
    }
}
