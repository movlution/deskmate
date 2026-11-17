using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace Deskmate
{
    sealed class NoteBlockMenu : ToolStripDropDown
    {
        sealed class Block
        {
            public readonly string Label, Hint, Prefix, Keywords;
            public Block(string label, string hint, string prefix, string keywords)
            {
                Label = label; Hint = hint; Prefix = prefix; Keywords = keywords;
            }
            public override string ToString() { return Label; }
        }

        readonly TaskAddBox query = new TaskAddBox();
        readonly ListBox results = new ListBox();
        readonly List<Block> blocks;
        readonly ToolStripControlHost resultHost;
        readonly Action<string> apply;
        readonly Font rowFont, hintFont;
        readonly int rowHeight;

        public NoteBlockMenu(float scale, Action<string> apply) : this(scale, apply, false) { }

        public NoteBlockMenu(float scale, Action<string> apply, bool includeTable)
        {
            this.apply = apply;
            blocks = new List<Block>
            {
                new Block(Lang.T("Teks biasa"), "Aa", "", "text paragraph paragraf"),
                new Block(Lang.T("Judul 1"), "H1", "# ", "heading h1"),
                new Block(Lang.T("Judul 2"), "H2", "## ", "heading h2"),
                new Block(Lang.T("Judul 3"), "H3", "### ", "heading h3"),
                new Block(Lang.T("Daftar berpoin"), "\u2022", "- ", "list bullet"),
                new Block(Lang.T("Daftar bernomor"), "1.", "1. ", "number ordered list"),
                new Block(Lang.T("Checklist"), "\u2610", "- [ ] ", "todo task tugas"),
                new Block(Lang.T("Kutipan"), "\u201c", "> ", "quote"),
                new Block(Lang.T("Blok kode"), "</>", "```", "code"),
                new Block(Lang.T("Garis pemisah"), "\u2500", "---", "divider line separator")
            };
            if (includeTable) blocks.Insert(7, new Block(Lang.T("Tabel"), "▦", "|", "table tabel"));

            int width = (int)(242 * scale);
            rowHeight = (int)(28 * scale);
            rowFont = Ink.Text(13 * scale);
            hintFont = Ink.Semi(11 * scale);
            Renderer = new MenuLook();
            Padding = new Padding((int)(7 * scale));

            query.AccessibleName = Lang.T("Cari blok");
            query.Font = rowFont;
            query.ForeColor = KitInk.Text;
            query.BackColor = KitInk.Group;
            query.BorderStyle = BorderStyle.FixedSingle;
            query.HandleCreated += delegate { Native.SetCue(query.Handle, Lang.T("Cari blok") + "…"); };
            query.Width = width;

            results.AccessibleName = Lang.T("Jenis blok");
            results.DrawMode = DrawMode.OwnerDrawFixed;

            results.IntegralHeight = false;
            results.ItemHeight = rowHeight;
            results.Font = rowFont;
            results.BackColor = KitInk.Window;
            results.ForeColor = KitInk.Text;
            results.BorderStyle = BorderStyle.None;
            results.Width = width;
            results.DrawItem += DrawBlock;
            results.MouseUp += delegate(object sender, MouseEventArgs e)
            {
                if (e.Button == MouseButtons.Left && results.IndexFromPoint(e.Location) >= 0) Choose();
            };
            results.KeyDown += OnListKey;
            query.KeyDown += OnQueryKey;
            query.TextChanged += delegate { Filter(); };
            Items.Add(new ToolStripControlHost(query) { AutoSize = false, Size = query.Size, Margin = new Padding(0, 0, 0, 5) });
            results.Height = rowHeight * blocks.Count;
            resultHost = new ToolStripControlHost(results) { AutoSize = false, Size = results.Size, Margin = Padding.Empty };
            Items.Add(resultHost);
            Filter();
            Opened += delegate { query.Focus(); };
        }

        void Filter()
        {
            string value = query.Text.Trim();
            results.BeginUpdate();
            results.Items.Clear();
            foreach (var block in blocks)
                if (block.Label.IndexOf(value, StringComparison.CurrentCultureIgnoreCase) >= 0
                    || block.Keywords.IndexOf(value, StringComparison.OrdinalIgnoreCase) >= 0)
                    results.Items.Add(block);
            if (results.Items.Count != 0) results.SelectedIndex = 0;
            else results.Items.Add(Lang.T("Blok tidak ditemukan"));
            resultHost.Height = rowHeight * results.Items.Count;
            results.EndUpdate();
        }

        void OnQueryKey(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Down || e.KeyCode == Keys.Up)
            {
                if (results.Items.Count > 0)
                {
                    int step = e.KeyCode == Keys.Down ? 1 : -1;
                    results.SelectedIndex = (results.SelectedIndex + step + results.Items.Count) % results.Items.Count;
                }
                e.SuppressKeyPress = true;
            }
            else OnListKey(sender, e);
        }

        void OnListKey(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Enter)
            {
                e.SuppressKeyPress = true;
                Choose();
            }
            else if (e.KeyCode == Keys.Escape)
            {
                e.SuppressKeyPress = true;
                Close(ToolStripDropDownCloseReason.Keyboard);
            }
        }

        void Choose()
        {
            var selected = results.SelectedItem as Block;
            if (selected == null) return;
            Close(ToolStripDropDownCloseReason.ItemClicked);
            apply(selected.Prefix);
        }

        void DrawBlock(object sender, DrawItemEventArgs e)
        {
            bool selected = (e.State & DrawItemState.Selected) != 0;
            using (var background = new SolidBrush(selected ? KitInk.Hover : KitInk.Window))
                e.Graphics.FillRectangle(background, e.Bounds);
            if (e.Index < 0) return;
            var block = results.Items[e.Index] as Block;
            if (block == null)
            {
                TextRenderer.DrawText(e.Graphics, results.Items[e.Index].ToString(), rowFont, e.Bounds, KitInk.Sub,
                    TextFormatFlags.VerticalCenter | TextFormatFlags.HorizontalCenter | TextFormatFlags.NoPrefix);
                return;
            }
            int iconWidth = (int)(e.Bounds.Height * 1.5f);
            var icon = new Rectangle(e.Bounds.X, e.Bounds.Y, iconWidth, e.Bounds.Height);
            var label = new Rectangle(icon.Right, e.Bounds.Y, Math.Max(0, e.Bounds.Width - iconWidth), e.Bounds.Height);
            TextRenderer.DrawText(e.Graphics, block.Hint, hintFont, icon, KitInk.Sub,
                TextFormatFlags.VerticalCenter | TextFormatFlags.HorizontalCenter | TextFormatFlags.NoPrefix);
            TextRenderer.DrawText(e.Graphics, block.Label, rowFont, label, KitInk.Text,
                TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
        }

    }
}
