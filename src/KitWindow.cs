using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace Deskmate
{
    // Window: theme colours, dark caption, manual DPI
    class KitWindow : Form
    {
        public float Ui = 1f;
        Timer fadeTimer;

        [DllImport("user32.dll")] static extern uint GetDpiForWindow(IntPtr hwnd);

        static readonly List<WeakReference> openWindows = new List<WeakReference>();

        public KitWindow()
        {
            AutoScaleMode = AutoScaleMode.None;
            BackColor = KitInk.Window;
            ForeColor = KitInk.Text;
            KeyPreview = true;
            ShowInTaskbar = true;
            Icon = AppIcon;
            lock (openWindows) openWindows.Add(new WeakReference(this));
        }

        protected override void OnClosed(EventArgs e)
        {
            StopFade();
            base.OnClosed(e);
            lock (openWindows)
            {
                for (int i = openWindows.Count - 1; i >= 0; i--)
                {
                    if (openWindows[i].Target == this) openWindows.RemoveAt(i);
                }
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                StopFade();
                lock (openWindows)
                {
                    for (int i = openWindows.Count - 1; i >= 0; i--)
                    {
                        if (openWindows[i].Target == this) openWindows.RemoveAt(i);
                    }
                }
            }
            base.Dispose(disposing);
        }

        public static void BroadcastThemeChange()
        {
            var targets = new List<KitWindow>();
            lock (openWindows)
            {
                for (int i = openWindows.Count - 1; i >= 0; i--)
                {
                    var target = openWindows[i].Target as KitWindow;
                    if (target == null || target.IsDisposed)
                        openWindows.RemoveAt(i);
                    else if (!targets.Contains(target))
                        targets.Add(target);
                }
            }
            foreach (Form f in Application.OpenForms)
            {
                var kw = f as KitWindow;
                if (kw != null && !kw.IsDisposed && !targets.Contains(kw))
                    targets.Add(kw);
            }
            foreach (var w in targets)
            {
                if (w.IsDisposed) continue;
                Action act = () =>
                {
                    if (w.IsDisposed) return;
                    if (w.IsHandleCreated) Native.DarkFrame(w.Handle, !Ink.Light);
                    w.BackColor = KitInk.Window;
                    w.ForeColor = KitInk.Text;
                    w.Relayout();
                    RefreshTree(w);
                    w.Invalidate(true);
                };
                if (w.InvokeRequired) w.BeginInvoke(act);
                else act();
            }
        }

        static void RefreshTree(Control parent)
        {
            if (parent is KitGroup) parent.BackColor = KitInk.Group;
            foreach (Control c in parent.Controls)
            {
                if (c is KitGroup) c.BackColor = KitInk.Group;
                c.Invalidate();
                RefreshTree(c);
            }
        }

        internal static readonly Icon AppIcon = LoadAppIcon(SystemInformation.IconSize);

        internal static Icon LoadAppIcon(Size size)
        {
            try
            {
                using (var stream = typeof(KitWindow).Assembly.GetManifestResourceStream("Deskmate.ico"))
                    if (stream != null) return new Icon(stream, size);
            }
            catch (Exception ex) { Log.Write("app icon: " + ex.Message); }
            return (Icon)SystemIcons.Application.Clone();
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            try { Ui = GetDpiForWindow(Handle) / 96f; } catch (EntryPointNotFoundException) { }
            Native.DarkFrame(Handle, !Ink.Light);
            Native.RoundCorners(Handle);
        }

        // Soft arrival unless motion is reduced
        protected override void OnLoad(EventArgs e)
        {
            if (!Anim.Reduced) Opacity = 0;
            base.OnLoad(e);
        }

        void StopFade()
        {
            if (fadeTimer != null)
            {
                fadeTimer.Stop();
                fadeTimer.Dispose();
                fadeTimer = null;
            }
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            if (Opacity >= 1) return;
            StopFade();
            var start = DateTime.UtcNow;
            fadeTimer = new Timer { Interval = 15 };
            fadeTimer.Tick += delegate
            {
                if (IsDisposed || Disposing || !IsHandleCreated) { StopFade(); return; }
                double k = Math.Min(1, (DateTime.UtcNow - start).TotalMilliseconds / 160);
                Opacity = 1 - (1 - k) * (1 - k);
                if (k >= 1) StopFade();
            };
            fadeTimer.Start();
        }

        protected override bool ProcessDialogKey(Keys key)
        {
            if (key == Keys.Escape) { Close(); return true; }
            return base.ProcessDialogKey(key);
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == Native.WM_DPICHANGED)
            {
                Ui = ((int)m.WParam & 0xFFFF) / 96f;
                var r = (Native.RECT)Marshal.PtrToStructure(m.LParam, typeof(Native.RECT));
                Bounds = Rectangle.FromLTRB(r.Left, r.Top, r.Right, r.Bottom);
                Relayout();
                return;
            }
            base.WndProc(ref m);
        }

        // Rebuild on scale, language, theme change
        public virtual void Relayout() { }

        public Font UiFont(float px) { return new Font(Ink.MenuFamily, px * Ui, GraphicsUnit.Pixel); }

        [DllImport("user32.dll")] static extern uint GetDpiForSystem();

        // Scale pixel-laid dialogs to system DPI
        public static void ScaleToDpi(Form f)
        {
            float s = 1f;
            try { s = GetDpiForSystem() / 96f; } catch (EntryPointNotFoundException) { }
            if (s < 1.01f) return;
            f.AutoScaleMode = AutoScaleMode.None;
            f.ClientSize = new Size((int)(f.ClientSize.Width * s), (int)(f.ClientSize.Height * s));
            ScaleTree(f, s);
            if (f.MinimumSize.Width > 0) f.MinimumSize = new Size((int)(f.MinimumSize.Width * s), (int)(f.MinimumSize.Height * s));
        }

        static void ScaleTree(Control parent, float s)
        {
            parent.Font = new Font(parent.Font.FontFamily, parent.Font.Size * s, parent.Font.Style, parent.Font.Unit);
            foreach (Control c in parent.Controls)
            {
                if (!(c.Parent is Form) || c.Dock == DockStyle.None)
                    c.Bounds = new Rectangle((int)(c.Left * s), (int)(c.Top * s), (int)(c.Width * s), (int)(c.Height * s));
                ScaleTree(c, s);
            }
        }
    }

    // Confirmation or notice in the app's own colours, never a native MessageBox
    sealed class KitAsk : KitWindow
    {
        readonly Label text = new Label { AutoSize = false };
        readonly KitButton yes = new KitButton { Primary = true };
        readonly KitButton no = new KitButton { Text = "Batal" };
        readonly KitButton ok = new KitButton { Text = "Tutup", Visible = false };
        bool two;
        Font uiFont;

        // Esc and the close box mean no; only an explicit confirm proceeds
        public static bool Confirm(Form owner, string message, string confirm = "Hapus", string dismiss = "Batal", string title = null)
        {
            using (var d = new KitAsk(message, true, confirm, dismiss, title))
                return d.ShowDialog(owner ?? Form.ActiveForm) == DialogResult.Yes;
        }

        public static void Notice(Form owner, string message, string title = null, string close = "Tutup")
        {
            using (var d = new KitAsk(message, false, "", "", title, close))
                d.ShowDialog(owner ?? Form.ActiveForm);
        }

        KitAsk(string message, bool question, string confirm, string dismiss)
            : this(message, question, confirm, dismiss, null, "Tutup")
        {
        }

        KitAsk(string message, bool question, string confirm, string dismiss, string title, string close = "Tutup")
        {
            two = question;
            Text = string.IsNullOrEmpty(title) ? Lang.T(question ? "Konfirmasi" : "Pemberitahuan") : title;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = MinimizeBox = false;
            StartPosition = FormStartPosition.CenterParent;
            ShowInTaskbar = false;
            text.Text = message;
            text.AccessibleName = message;
            yes.Text = Lang.T(string.IsNullOrEmpty(confirm) ? "Ya" : confirm);
            no.Text = Lang.T(string.IsNullOrEmpty(dismiss) ? "Batal" : dismiss);
            ok.Text = Lang.T(string.IsNullOrEmpty(close) ? "Tutup" : close);
            yes.Click += delegate { DialogResult = DialogResult.Yes; Close(); };
            no.Click += delegate { DialogResult = DialogResult.No; Close(); };
            ok.Click += delegate { DialogResult = DialogResult.OK; Close(); };
            Controls.AddRange(new Control[] { text, yes, no, ok });
            // Visibility is a post-parenting property; set it once the buttons are in the tree
            yes.Visible = two;
            no.Visible = two;
            ok.Visible = !two;
        }

        protected override void OnHandleCreated(EventArgs e) { base.OnHandleCreated(e); Relayout(); }

        public override void Relayout()
        {
            if (!IsHandleCreated) return;
            if (uiFont == null) { uiFont = UiFont(14f); text.Font = UiFont(14f); yes.Font = no.Font = ok.Font = UiFont(13f); }
            Font = uiFont; BackColor = KitInk.Window; ForeColor = KitInk.Text;
            float s = Ui;
            int w = (int)(420 * s);
            int textW = w - (int)(40 * s);
            var size = TextRenderer.MeasureText(text.Text, text.Font, new Size(textW, 0), TextFormatFlags.WordBreak);
            int textH = Math.Max((int)(24 * s), size.Height);
            int rowY = textH + (int)(58 * s);
            text.SetBounds((int)(20 * s), (int)(22 * s), textW, textH);
            no.Visible = two;
            int right = w - (int)(20 * s) - (int)(96 * s);
            if (two)
            {
                no.SetBounds(right, rowY, (int)(96 * s), (int)(34 * s));
                yes.SetBounds(right - (int)(108 * s), rowY, (int)(96 * s), (int)(34 * s));
            }
            ok.SetBounds(right, rowY, (int)(96 * s), (int)(34 * s));
            ClientSize = new Size(w, rowY + (int)(34 * s) + (int)(20 * s));
            ActiveControl = two ? (Control)yes : ok;
        }

        protected override void Dispose(bool disposing) { base.Dispose(disposing); if (disposing && uiFont != null) uiFont.Dispose(); }
    }
}
