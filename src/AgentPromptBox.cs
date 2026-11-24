using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows.Forms;

namespace Deskmate
{
    partial class Island
    {
        // Composer for one agent session
        class AgentPromptBox : KitWindow
        {
            [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetWindowText(IntPtr h, StringBuilder sb, int n);

            class Sent { public string Text; public DateTime Utc; }

            // Sent prompts per project, memory only
            static readonly Dictionary<string, List<Sent>> history = new Dictionary<string, List<Sent>>(StringComparer.OrdinalIgnoreCase);
            const int HistoryMax = 20, ProjectsMax = 12, TemplateMax = 30, DesignW = 560;

            readonly Island isle;
            readonly AgentSession session;
            readonly bool shared;
            readonly ChatHeader header = new ChatHeader();
            readonly ScriptWell well = new ScriptWell();
            readonly Label target = new Label(), caution = new Label(), note = new Label();
            readonly Cue cue;
            readonly KitButton send = new KitButton(), keep = new KitButton();
            readonly KitSegmented tabs = new KitSegmented();
            readonly PromptList list = new PromptList();
            readonly KeyHints hints = new KeyHints();
            readonly List<string> templates = new List<string>();
            readonly System.Windows.Forms.Timer tick = new System.Windows.Forms.Timer { Interval = 1000 };
            string termApp = "", termTitle = "", draft = "";
            int recall = -1, shape, sig;

            TextBox field { get { return well.Box; } }
            public string Prompt { get { return this.field.Text; } }

            static string TemplatesFile { get { return Path.Combine(Settings.Dir, "templat-chat.txt"); } }

            public AgentPromptBox(Island isle, AgentSession s)
            {
                this.isle = isle;
                session = s;
                shared = isle.SharesWindow(s);
                Text = Lang.T("Chat dengan agent");
                FormBorderStyle = FormBorderStyle.FixedSingle;
                MaximizeBox = MinimizeBox = false;
                StartPosition = FormStartPosition.Manual;
                TopMost = true;

                header.Isle = isle;
                header.S = s;
                field.AccessibleName = Lang.T("Isi pesan");
                field.MaxLength = PromptMax;
                field.TextChanged += delegate { Typed(); well.FitBars(); };
                field.KeyDown += FieldKey;
                cue = new Cue(field) { Text = Lang.F("Pesan untuk {0}", AgentKind(s)) };
                field.AccessibleDescription = cue.Text;

                foreach (var l in new[] { target, caution, note })
                {
                    l.UseMnemonic = false;
                    l.AutoSize = false;
                    l.AutoEllipsis = true;
                    l.TextAlign = ContentAlignment.MiddleLeft;
                }
                note.TextAlign = ContentAlignment.MiddleRight;
                caution.Text = Lang.T("Jendela ini punya beberapa sesi. Teks masuk ke tab yang sedang aktif.");
                caution.Visible = shared;

                send.Text = Lang.T("Kirim");
                send.Primary = true;
                send.Click += delegate { Submit(); };
                keep.Text = Lang.T("Simpan templat");
                keep.Click += delegate { SaveTemplate(); };
                tabs.Items = new[] { Lang.T("Riwayat"), Lang.T("Templat") };
                tabs.AccessibleName = Lang.T("Riwayat dan templat");
                tabs.Changed += i => FillList();
                list.AccessibleName = Lang.T("Pesan tersimpan");
                list.Picked += Use;
                list.Removed += RemoveTemplate;

                hints.Pairs = new[] { new[] { "Enter", Lang.T("Kirim") }, new[] { "Shift Enter", Lang.T("Baris baru") } };
                hints.AccessibleName = "Enter: " + Lang.T("Kirim") + ". Shift+Enter: " + Lang.T("Baris baru") + ". Esc: " + Lang.T("Tutup") + ".";
                well.Controls.AddRange(new Control[] { hints, keep, send });
                field.TabIndex = 0;
                send.TabIndex = 1;
                keep.TabIndex = 2;
                Controls.AddRange(new Control[] { header, well, target, caution, tabs, note, list });
                header.TabStop = false;
                well.TabIndex = 0;
                tabs.TabIndex = 1;
                list.TabIndex = 2;
                LoadTemplates();
                FillList();
                Typed();
                termApp = TerminalApp(s.Window);
                Target();
                shape = header.Shape();
                sig = Signature();
                tick.Tick += delegate { Tick(); };
                ActiveControl = field;
            }

            protected override void OnHandleCreated(EventArgs e)
            {
                base.OnHandleCreated(e);
                Relayout();
                var area = Screen.FromPoint(Cursor.Position).WorkingArea;
                Location = new Point(area.X + (area.Width - Width) / 2, area.Y + Math.Max(0, (int)((area.Height - Height) * 0.42f)));
            }

            protected override void OnShown(EventArgs e)
            {
                base.OnShown(e);
                Activate();
                field.Focus();
                tick.Start();
            }

            protected override void OnFormClosed(FormClosedEventArgs e)
            {
                tick.Stop();
                base.OnFormClosed(e);
            }

            protected override void Dispose(bool disposing)
            {
                if (disposing) tick.Dispose();
                base.Dispose(disposing);
            }

            public override void Relayout()
            {
                float s = Ui;
                Func<float, int> u = v => (int)Math.Round(v * s);
                BackColor = KitInk.Window;
                ForeColor = KitInk.Text;
                Native.DarkFrame(Handle, !Ink.Light);
                int w = u(DesignW), m = u(24);

                header.Measure();
                header.SetBounds(0, 0, w, u(header.DesignH));
                well.BackColor = KitInk.Group;
                well.SetBounds(m, header.Bottom, w - 2 * m, u(136));
                well.Place(s);
                // Bottom strip: key hints, actions
                int strip = u(48), bh = u(30), by = well.Height - u(10) - bh;
                field.SetBounds(u(16), u(12), well.Width - u(22), well.Height - u(12) - strip);
                well.FitBars();
                send.Font = UiFont(13f);
                keep.Font = UiFont(12.5f);
                int sw = Math.Max(u(80), TextRenderer.MeasureText(send.Text, send.Font).Width + u(36));
                int kw = TextRenderer.MeasureText(keep.Text, keep.Font).Width + u(28);
                send.SetBounds(well.Width - u(10) - sw, by, sw, bh);
                keep.SetBounds(send.Left - u(6) - kw, by, kw, bh);
                hints.SetBounds(u(16), by, keep.Left - u(28), bh);

                // Which terminal receives it
                int y = well.Bottom + u(8);
                foreach (var l in new[] { target, caution, note })
                {
                    l.Font = UiFont(12f);
                    l.ForeColor = KitInk.Sub;
                    l.BackColor = KitInk.Window;
                }
                target.SetBounds(m + u(4), y, w - 2 * m - u(4), u(20));
                y += u(20);
                if (shared)
                {
                    caution.SetBounds(m + u(4), y, w - 2 * m - u(4), u(20));
                    y += u(20);
                }
                y += u(20);

                // History and templates
                tabs.SetBounds(m, y, u(200), u(28));
                note.SetBounds(tabs.Right + u(12), y, w - m - u(4) - tabs.Right - u(12), u(28));
                y += u(28) + u(10);
                list.SetBounds(m, y, w - 2 * m, u(PromptList.DesignH));
                ClientSize = new Size(w, list.Bottom + u(24));
                Invalidate(true);
            }

            protected override void OnKeyDown(KeyEventArgs e)
            {
                base.OnKeyDown(e);
                // Ctrl+Enter sends from anywhere
                if (e.KeyCode == Keys.Enter && e.Control) { e.SuppressKeyPress = true; Submit(); }
            }

            void FieldKey(object sender, KeyEventArgs e)
            {
                if (e.KeyCode == Keys.Enter && !e.Shift)
                {
                    e.SuppressKeyPress = true;
                    e.Handled = true;
                    Submit();
                    return;
                }
                if (e.KeyCode == Keys.A && e.Control) { e.SuppressKeyPress = true; field.SelectAll(); return; }
                if ((e.KeyCode != Keys.Up && e.KeyCode != Keys.Down) || e.Modifiers != Keys.None) return;
                var mine = Mine();
                if (mine == null || mine.Count == 0) return;
                bool up = e.KeyCode == Keys.Up;
                // Recall only from first or last line
                int line = field.GetLineFromCharIndex(field.SelectionStart);
                if (up ? line > 0 : line < field.GetLineFromCharIndex(field.TextLength)) return;
                if (recall < 0)
                {
                    if (!up) return;
                    draft = field.Text;
                    recall = mine.Count;
                }
                e.SuppressKeyPress = true;
                e.Handled = true;
                recall = Math.Max(0, Math.Min(mine.Count, recall + (up ? -1 : 1)));
                field.Text = recall < mine.Count ? mine[recall].Text : draft;
                field.SelectionStart = field.TextLength;
                if (recall == mine.Count) recall = -1;
            }

            void Typed()
            {
                bool any = !string.IsNullOrWhiteSpace(field.Text);
                send.Enabled = any;
                keep.Enabled = any;
            }

            void Submit()
            {
                string text = CleanPrompt(field.Text);
                if (text.Length == 0) { field.Focus(); return; }
                Remember(session.Project, text, DateTime.UtcNow);
                DialogResult = DialogResult.OK;
                Close();
            }

            static void Remember(string project, string text, DateTime utc)
            {
                List<Sent> mine;
                if (!history.TryGetValue(project, out mine))
                {
                    // Few projects; the stalest goes
                    if (history.Count >= ProjectsMax)
                    {
                        string stale = null;
                        DateTime at = DateTime.MaxValue;
                        foreach (var kv in history)
                            if (kv.Value.Count == 0 || kv.Value[kv.Value.Count - 1].Utc < at) { stale = kv.Key; at = kv.Value.Count == 0 ? DateTime.MinValue : kv.Value[kv.Value.Count - 1].Utc; }
                        if (stale != null) history.Remove(stale);
                    }
                    mine = new List<Sent>();
                    history[project] = mine;
                }
                mine.RemoveAll(x => x.Text == text);
                mine.Add(new Sent { Text = text, Utc = utc });
                if (mine.Count > HistoryMax) mine.RemoveAt(0);
            }

            List<Sent> Mine()
            {
                List<Sent> mine;
                return history.TryGetValue(session.Project, out mine) ? mine : null;
            }

            void FillList()
            {
                list.Items.Clear();
                list.Trail.Clear();
                list.Removable = tabs.Selected == 1;
                if (tabs.Selected == 0)
                {
                    var mine = Mine();
                    if (mine != null)
                        for (int i = mine.Count - 1; i >= 0; i--) { list.Items.Add(mine[i].Text); list.Trail.Add(Fmt.AgoShort(mine[i].Utc)); }
                    list.Empty = Lang.T("Belum ada pesan untuk proyek ini");
                    note.Text = Lang.T("Hanya di memori sampai Deskmate ditutup");
                }
                else
                {
                    foreach (var t in templates) { list.Items.Add(t); list.Trail.Add(""); }
                    list.Empty = Lang.T("Belum ada templat");
                    note.Text = Lang.T("Hanya yang kamu simpan, di perangkat ini");
                }
                list.AccessibleDescription = note.Text + (list.Removable ? ". " + Lang.T("Delete menghapus templat") : "");
                list.Reset();
            }

            void Use(int i)
            {
                if (i < 0 || i >= list.Items.Count) return;
                recall = -1;
                field.Text = list.Items[i];
                field.SelectionStart = field.TextLength;
                field.Focus();
            }

            // Saved only when the user asks
            void SaveTemplate()
            {
                string t = CleanPrompt(field.Text);
                if (t.Length == 0) { field.Focus(); return; }
                templates.Remove(t);
                templates.Insert(0, t);
                while (templates.Count > TemplateMax) templates.RemoveAt(templates.Count - 1);
                StoreTemplates();
                tabs.Selected = 1;
                FillList();
                field.Focus();
            }

            void RemoveTemplate(int i)
            {
                if (tabs.Selected != 1 || i < 0 || i >= templates.Count) return;
                templates.RemoveAt(i);
                StoreTemplates();
                FillList();
                if (list.Focused) list.Pick(Math.Min(i, templates.Count - 1));
            }

            void LoadTemplates()
            {
                templates.Clear();
                try
                {
                    var info = new FileInfo(TemplatesFile);
                    if (!info.Exists || info.Length > 512 * 1024) return;
                    foreach (var line in File.ReadAllLines(info.FullName, Encoding.UTF8))
                    {
                        string t = CleanPrompt(line);
                        if (t.Length > 0 && !templates.Contains(t)) templates.Add(t);
                        if (templates.Count >= TemplateMax) break;
                    }
                }
                catch (Exception ex) { Log.Write("chat templates: " + ex.Message); }
            }

            void StoreTemplates()
            {
                try
                {
                    Directory.CreateDirectory(Settings.Dir);
                    File.WriteAllLines(TemplatesFile, templates, new UTF8Encoding(false));
                }
                catch (Exception ex) { Log.Write("chat templates save: " + ex.Message); }
            }

            // Friendly name of the terminal app
            static string TerminalApp(IntPtr h)
            {
                if (h == IntPtr.Zero) return "";
                uint pid;
                Native.GetWindowThreadProcessId(h, out pid);
                if (pid == 0) return "";
                string name;
                try { using (var p = Process.GetProcessById((int)pid)) name = p.ProcessName; }
                catch (Exception) { return ""; }
                switch (name.ToLowerInvariant())
                {
                    case "windowsterminal": return "Windows Terminal";
                    case "code": return "VS Code";
                    case "cursor": return "Cursor";
                    case "windsurf": return "Windsurf";
                    case "cmd": case "conhost": case "openconsole": return "Command Prompt";
                    case "powershell": case "pwsh": return "PowerShell";
                    case "wezterm-gui": return "WezTerm";
                    case "alacritty": return "Alacritty";
                    case "mintty": return "Git Bash";
                    case "tabby": return "Tabby";
                    case "warp": return "Warp";
                }
                return name;
            }

            // Which window and tab receives it
            void Target()
            {
                string title = "";
                if (session.Window != IntPtr.Zero)
                {
                    var sb = new StringBuilder(256);
                    if (GetWindowText(session.Window, sb, sb.Capacity) > 0) title = Fmt.Visible(Fmt.OneLine(sb.ToString(), 80));
                }
                if (title == termTitle && target.Text.Length > 0) return;
                termTitle = title;
                ShowTarget();
            }

            void ShowTarget()
            {
                string where = termApp.Length > 0 ? Lang.F("Dikirim ke {0}", termApp) : Lang.T("Dikirim ke terminal sesi ini");
                target.Text = termTitle.Length > 0 && termTitle != termApp ? where + " · " + termTitle : where;
                target.AccessibleName = target.Text;
            }

            int Signature()
            {
                var s = session;
                int h = (int)s.State * 31 + (s.Failed ? 7 : 0);
                h = h * 31 + s.Files.Count;
                h = h * 31 + s.Commands;
                h = h * 31 + (s.Plan == null ? -1 : s.Plan.Count * 64 + PlanDone(s));
                h = h * 31 + PlanNow(s).GetHashCode();
                h = h * 31 + s.Tool.GetHashCode() + s.Activity.GetHashCode() + s.LastMessage.GetHashCode();
                h = h * 31 + isle.quotaVersion;
                h = h * 31 + (int)(DateTime.UtcNow.Ticks / TimeSpan.TicksPerMinute);
                return h;
            }

            // Once a second while open
            void Tick()
            {
                int now = Signature();
                if (now != sig)
                {
                    sig = now;
                    int sh = header.Shape();
                    if (sh != shape) { shape = sh; Relayout(); }
                    else { header.Measure(); header.Invalidate(); }
                }
                else if (session.State == AgentState.Working) header.Invalidate(Rectangle.Round(header.Clock));
                Target();
            }
        }
    }
}
