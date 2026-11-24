using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;

namespace Deskmate
{
    // Paste the script, pick speed and size
    class PrompterSetup : KitWindow
    {
        readonly KitPopup library = new KitPopup();
        readonly KitButton import = new KitButton(), keep = new KitButton(), remove = new KitButton(), cancel = new KitButton(), start = new KitButton();
        readonly ScriptWell well = new ScriptWell();
        readonly KitGroup group = new KitGroup();
        readonly KitRow speedRow = new KitRow(), sizeRow = new KitRow(), shadeRow = new KitRow(), mirrorRow = new KitRow();
        readonly KitSlider speed = new KitSlider(), size = new KitSlider(), shade = new KitSlider();
        readonly KitSwitch mirror = new KitSwitch();
        readonly Label title = new Label(), markHint = new Label(), count = new Label(), keysCap = new Label(), sizeHint = new Label();
        readonly KeyList keys = new KeyList();
        readonly List<string> names = new List<string>();
        readonly string lastText;
        readonly bool live;
        bool picking, ready;

        public event Action Changed;

        static string Folder { get { return Path.Combine(Settings.Dir, "naskah"); } }

        TextBox script { get { return well.Box; } }
        public string Script { get { return script.Text; } }
        public int Wpm { get { return speed.Value; } }
        public int Px { get { return size.Value; } }
        public bool Mirror { get { return mirror.Checked; } }
        public int Bg { get { return shade.Value; } }

        public PrompterSetup(string text, int wpm, int px, bool mirrored, int bgPct, bool live)
        {
            this.live = live;
            lastText = text;
            Text = Lang.T("Teleprompter");
            FormBorderStyle = FormBorderStyle.FixedSingle;
            MaximizeBox = MinimizeBox = false;
            StartPosition = FormStartPosition.Manual;
            TopMost = true;

            title.Text = Lang.T("Teleprompter");
            title.AutoSize = true;
            title.UseMnemonic = false;

            library.AccessibleName = Lang.T("Daftar naskah");
            library.Changed += i => Pick();
            Action<KitButton, string, Action> button = (b, label, act) => { b.Text = label; b.Click += delegate { act(); }; };
            button(import, Lang.T("Impor"), Import);
            button(keep, Lang.T("Simpan naskah"), Save);
            button(remove, Lang.T("Hapus"), Delete);
            button(cancel, Lang.T("Batal"), Close);
            button(start, Lang.T(live ? "Simpan" : "Mulai"), Submit);
            start.Primary = true;

            script.AccessibleName = Lang.T("Naskah");
            script.MaxLength = 200000;
            script.Text = Crlf(text);
            script.TextChanged += delegate { Count(); well.FitBars(); };
            foreach (var l in new[] { markHint, count, keysCap, sizeHint }) { l.UseMnemonic = false; l.AutoSize = false; }
            markHint.Text = Lang.T("Tulis [jeda] untuk berhenti otomatis.");
            count.TextAlign = ContentAlignment.MiddleRight;
            markHint.TextAlign = sizeHint.TextAlign = ContentAlignment.MiddleLeft;
            sizeHint.Text = Lang.T("Saat jeda, tarik tepi island untuk mengubah ukurannya.");

            Slider(speedRow, speed, Lang.T("Kecepatan"), 60, 400, 20, wpm, v => Lang.F("{0} kata/menit", v));
            Slider(sizeRow, size, Lang.T("Ukuran huruf"), 15, 26, 1, px, v => v + " px");
            Slider(shadeRow, shade, Lang.T("Latar"), 15, 100, 5, bgPct, v => v + "%");
            mirror.Checked = mirrored;
            mirror.AccessibleName = mirrorRow.Title = Lang.T("Cermin");
            mirror.AccessibleDescription = mirrorRow.Detail = Lang.T("Untuk teleprompter kaca");
            mirrorRow.Field = mirror;
            mirror.CheckedChanged += delegate { Notify(); };
            foreach (var r in new[] { speedRow, sizeRow, shadeRow, mirrorRow }) { r.Controls.Add(r.Field); group.Controls.Add(r); }

            Controls.AddRange(new Control[] { title, library, import, keep, remove, well, markHint, count, group, keysCap, keys, sizeHint, cancel, start });
            int tab = 0;
            foreach (Control c in Controls) c.TabIndex = tab++;
            Fill(null);
            Count();
            ActiveControl = script;
            ready = true;
        }

        public void SetKeys(string caption, string prefix, string[][] rows)
        {
            keysCap.Text = caption;
            keys.Prefix = prefix;
            keys.Rows = rows;
            keys.AccessibleName = caption;
            keys.AccessibleDescription = keys.Spoken();
            keys.Invalidate();
        }

        void Slider(KitRow row, KitSlider s, string name, int min, int max, int step, int value, Func<int, string> fmt)
        {
            s.Minimum = min;
            s.Maximum = max;
            s.Step = step;
            s.Value = Math.Max(min, Math.Min(max, (int)Math.Round((double)(value - min) / step) * step + min));
            s.Format = fmt;
            s.AccessibleName = row.Title = name;
            row.Detail = fmt(s.Value);
            row.Field = s;
            s.Changed += v =>
            {
                row.Detail = fmt(v);
                row.Invalidate();
                if (s == speed) Count();
                Notify();
            };
        }

        void Notify()
        {
            if (!ready) return;
            var h = Changed;
            if (h != null) h();
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            // Small screens: shrink to fit
            var area = Screen.FromPoint(Cursor.Position).WorkingArea;
            float fit = Math.Min((area.Width - 40) / (Ui * DesignW), (area.Height - 60) / (Ui * DesignH));
            if (fit < 1) Ui *= fit;
            Relayout();
            Location = new Point(area.X + (area.Width - Width) / 2, area.Y + (area.Height - Height) / 2);
        }

        const int DesignW = 820, DesignH = 632;

        public override void Relayout()
        {
            float s = Ui;
            Func<float, int> u = v => (int)Math.Round(v * s);
            BackColor = KitInk.Window;
            ForeColor = KitInk.Text;
            Native.DarkFrame(Handle, !Ink.Light);
            ClientSize = new Size(u(DesignW), u(DesignH));
            title.Font = UiFont(24f);
            title.ForeColor = KitInk.Text;
            title.BackColor = KitInk.Window;
            title.Location = new Point(u(28), u(20));
            foreach (var l in new[] { markHint, count, keysCap, sizeHint }) { l.Font = UiFont(12f); l.ForeColor = KitInk.Sub; l.BackColor = KitInk.Window; }

            // Left: library and script
            int left = u(28), leftW = u(428), top = u(74);
            int x = left + leftW;
            foreach (var b in new[] { remove, keep, import })
            {
                b.Font = UiFont(12.5f);
                int bw = TextRenderer.MeasureText(b.Text, b.Font).Width + u(32);
                x -= bw;
                b.SetBounds(x, top, bw, u(30));
                x -= u(8);
            }
            library.Font = UiFont(12.5f);
            library.SetBounds(left, top, x - left, u(30));
            int keysTop = u(358), keysH = u(198), bottom = keysTop + keysH;
            well.SetBounds(left, u(116), leftW, bottom - u(28) - u(116));
            well.Place(s);
            markHint.SetBounds(left + u(4), well.Bottom + u(6), u(260), u(20));
            count.SetBounds(left + leftW - u(164), well.Bottom + u(6), u(160), u(20));

            // Right: look rows and shortcuts
            int right = u(480), rightW = u(312), y = 0;
            foreach (Control c in group.Controls)
            {
                var r = (KitRow)c;
                r.SetBounds(0, y, rightW, u(60));
                if (r.Field is KitSlider) r.Field.Size = new Size(u(136), u(30));
                else r.Field.Size = new Size(u(46), u(26));
                r.Field.Location = new Point(r.Width - r.Field.Width - u(16), (r.Height - r.Field.Height) / 2);
                y += r.Height;
            }
            group.BackColor = KitInk.Group;
            group.SetBounds(right, top, rightW, y);
            keysCap.SetBounds(right + u(12), group.Bottom + u(18), rightW - u(12), u(20));
            keys.SetBounds(right, keysTop, rightW, keysH);

            // Footer: resize hint, then actions
            int fy = bottom + u(22), bh = u(32);
            start.Font = UiFont(13f);
            cancel.Font = UiFont(13f);
            int sw = Math.Max(u(96), TextRenderer.MeasureText(start.Text, start.Font).Width + u(36));
            int cw = Math.Max(u(84), TextRenderer.MeasureText(cancel.Text, cancel.Font).Width + u(36));
            start.SetBounds(right + rightW - sw, fy, sw, bh);
            cancel.SetBounds(start.Left - u(8) - cw, fy, cw, bh);
            sizeHint.SetBounds(left + u(4), fy, cancel.Left - left - u(20), bh);
            Invalidate(true);
        }

        void Submit()
        {
            if (string.IsNullOrWhiteSpace(script.Text)) { script.Focus(); return; }
            DialogResult = DialogResult.OK;
            Close();
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            // Ctrl+Enter starts from anywhere
            if (e.KeyCode == Keys.Enter && e.Control) { e.SuppressKeyPress = true; Submit(); }
        }

        // Word count and reading time
        void Count()
        {
            string s = script.Text;
            int words = 0, i = 0, n = s.Length;
            while (i < n)
            {
                while (i < n && char.IsWhiteSpace(s[i])) i++;
                int b = i;
                while (i < n && !char.IsWhiteSpace(s[i])) i++;
                int len = i - b;
                if (len == 0) break;
                bool mark = (len == 6 && string.Compare(s, b, "[jeda]", 0, 6, StringComparison.OrdinalIgnoreCase) == 0) ||
                            (len == 7 && string.Compare(s, b, "[pause]", 0, 7, StringComparison.OrdinalIgnoreCase) == 0);
                if (!mark) words++;
            }
            if (words == 0) { count.Text = ""; return; }
            int secs = (int)Math.Round(words * 60.0 / Math.Max(1, Wpm));
            count.Text = Lang.F("{0} kata · {1}", words, (secs / 60) + ":" + (secs % 60).ToString("00"));
        }

        // Saved scripts: pick, import, save, delete
        void Fill(string select)
        {
            picking = true;
            names.Clear();
            try
            {
                if (Directory.Exists(Folder))
                {
                    foreach (var f in Directory.GetFiles(Folder, "*.txt")) names.Add(Path.GetFileNameWithoutExtension(f));
                    names.Sort(StringComparer.CurrentCultureIgnoreCase);
                }
            }
            catch (Exception ex) { Log.Write("scripts list: " + ex.Message); }
            var items = new List<string> { Lang.T("Naskah terakhir") };
            items.AddRange(names);
            library.Items = items.ToArray();
            int at = select == null ? 0 : names.IndexOf(select) + 1;
            library.Selected = Math.Max(0, at);
            remove.Enabled = library.Selected > 0;
            picking = false;
        }

        void Pick()
        {
            remove.Enabled = library.Selected > 0;
            if (picking) return;
            if (library.Selected <= 0) { script.Text = Crlf(lastText); return; }
            try { script.Text = Crlf(File.ReadAllText(Path.Combine(Folder, names[library.Selected - 1] + ".txt"))); }
            catch (Exception ex) { Log.Write("script read: " + ex.Message); }
        }

        static string Crlf(string s) { return (s ?? "").Replace("\r\n", "\n").Replace("\n", "\r\n"); }

        // Name from the first line, safe for a file name
        static string NameFor(string text)
        {
            string first = text.Trim();
            int nl = first.IndexOfAny(new[] { '\r', '\n' });
            if (nl > 0) first = first.Substring(0, nl);
            foreach (char bad in Path.GetInvalidFileNameChars()) first = first.Replace(bad, ' ');
            first = first.Trim().TrimEnd('.');
            if (first.Length > 40) first = first.Substring(0, 40).Trim();
            return first.Length > 0 ? first : Lang.T("Naskah");
        }

        void Save()
        {
            if (string.IsNullOrWhiteSpace(script.Text)) { script.Focus(); return; }
            string name = library.Selected > 0 ? names[library.Selected - 1] : NameFor(script.Text);
            try
            {
                Directory.CreateDirectory(Folder);
                File.WriteAllText(Path.Combine(Folder, name + ".txt"), script.Text);
            }
            catch (Exception ex) { Log.Write("script save: " + ex.Message); return; }
            Fill(name);
        }

        void Delete()
        {
            if (library.Selected <= 0) return;
            try { File.Delete(Path.Combine(Folder, names[library.Selected - 1] + ".txt")); }
            catch (Exception ex) { Log.Write("script delete: " + ex.Message); }
            Fill(null);
            script.Text = Crlf(lastText);
            script.Focus();
        }

        void Import()
        {
            using (var open = new OpenFileDialog { Filter = Lang.T("Teks atau Markdown") + " (*.txt;*.md)|*.txt;*.md", Title = Lang.T("Impor naskah") })
            {
                if (open.ShowDialog(this) != DialogResult.OK) return;
                try
                {
                    var info = new FileInfo(open.FileName);
                    if (info.Length > 400000) { script.Text = ""; return; }
                    string text = File.ReadAllText(open.FileName);
                    if (open.FileName.EndsWith(".md", StringComparison.OrdinalIgnoreCase)) text = PlainMarkdown(text);
                    picking = true;
                    library.Selected = 0;
                    remove.Enabled = false;
                    picking = false;
                    script.Text = Crlf(text.Trim());
                }
                catch (Exception ex) { Log.Write("script import: " + ex.Message); }
            }
        }

        // Read-aloud text: drop Markdown syntax, keep the words
        static string PlainMarkdown(string md)
        {
            var sb = new StringBuilder();
            bool fence = false;
            foreach (var raw in md.Replace("\r\n", "\n").Split('\n'))
            {
                string line = raw.TrimEnd();
                if (line.TrimStart().StartsWith("```")) { fence = !fence; continue; }
                if (fence) continue;
                line = System.Text.RegularExpressions.Regex.Replace(line, @"^\s{0,3}#{1,6}\s+", "");
                line = System.Text.RegularExpressions.Regex.Replace(line, @"^\s*([-*+]|\d+[.)])\s+", "");
                line = System.Text.RegularExpressions.Regex.Replace(line, @"^\s*>\s?", "");
                line = System.Text.RegularExpressions.Regex.Replace(line, @"!\[[^\]]*\]\([^)]*\)", "");
                line = System.Text.RegularExpressions.Regex.Replace(line, @"\[([^\]]*)\]\([^)]*\)", "$1");
                line = System.Text.RegularExpressions.Regex.Replace(line, @"(\*\*|__|\*|_|`|~~)(.+?)\1", "$2");
                line = System.Text.RegularExpressions.Regex.Replace(line, @"<[^>]+>", "");
                if (System.Text.RegularExpressions.Regex.IsMatch(line, @"^\s*([-*_]\s*){3,}$")) line = "";
                sb.Append(line).Append('\n');
            }
            return System.Text.RegularExpressions.Regex.Replace(sb.ToString(), @"\n{3,}", "\n\n");
        }
    }
}
