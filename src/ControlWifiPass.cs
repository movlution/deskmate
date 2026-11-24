using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Threading;
using System.Windows.Forms;
using WinTimer = System.Windows.Forms.Timer;

namespace Deskmate
{
    partial class Island
    {
        sealed class WifiPassBox : KitWindow
        {
            readonly Wifi.Net net;
            readonly Action<Wifi.Net, string> done;
            readonly KitText box = new KitText();
            readonly KitButton eye = new KitButton();
            readonly KitButton join = new KitButton { Primary = true };
            bool revealed;
            Font fBox, fJoin, fHead, fHint;

            public WifiPassBox(Wifi.Net net, Action<Wifi.Net, string> done)
            {
                this.net = net;
                this.done = done;
                FormBorderStyle = FormBorderStyle.None;
                ShowInTaskbar = false;
                TopMost = true;
                KeyPreview = true;
                Text = net.Ssid;
                box.BorderStyle = BorderStyle.None;
                box.Multiline = false;
                box.UseSystemPasswordChar = true;
                box.AccessibleName = Lang.T("Kata sandi");
                join.Text = Lang.T("Sambungkan");
                join.Enabled = false;
                UpdateEye();
                eye.Click += delegate
                {
                    revealed = !revealed;
                    box.UseSystemPasswordChar = !revealed;
                    UpdateEye();
                    box.Focus();
                };
                box.TextChanged += delegate { join.Enabled = box.Text.Length > 0; };
                join.Click += delegate { Commit(); };
                box.KeyDown += delegate (object sender, KeyEventArgs e)
                {
                    if (e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; Commit(); }
                    else if (e.KeyCode == Keys.Escape) { e.SuppressKeyPress = true; Close(); }
                };
                KeyDown += delegate (object sender, KeyEventArgs e)
                {
                    if (e.KeyCode == Keys.Escape) { e.SuppressKeyPress = true; Close(); }
                };
                Controls.Add(box);
                Controls.Add(eye);
                Controls.Add(join);
                Deactivate += delegate { if (!IsDisposed) Close(); };
            }

            void UpdateEye()
            {
                eye.Text = revealed ? "\uED1A" : "\uE7B3";
                eye.AccessibleName = Lang.T(revealed ? "Sembunyikan kata sandi" : "Tampilkan kata sandi");
            }

            protected override CreateParams CreateParams
            {
                get { var cp = base.CreateParams; cp.ClassStyle |= 0x20000; return cp; }
            }

            protected override void OnHandleCreated(EventArgs e)
            {
                base.OnHandleCreated(e);
                Relayout();
            }

            public override void Relayout()
            {
                float s = Ui;
                BackColor = KitInk.Window;
                ClientSize = new Size((int)(360 * s), (int)(128 * s));
                DisposeFonts();
                fBox = UiFont(14f);
                fJoin = UiFont(12.5f);
                fHead = UiFont(13f);
                fHint = UiFont(11.5f);
                box.Font = fBox;
                box.BackColor = KitInk.Group;
                box.ForeColor = KitInk.Text;
                box.SetBounds((int)(28 * s), (int)(48 * s), ClientSize.Width - (int)(86 * s), (int)(22 * s));
                Native.SetCue(box.Handle, Lang.T("Kata sandi"));
                eye.Font = Ink.Icons(14f * s);
                eye.SetBounds(ClientSize.Width - (int)(50 * s), (int)(44 * s), (int)(30 * s), (int)(30 * s));
                join.Font = fJoin;
                int jw = TextRenderer.MeasureText(join.Text, fJoin).Width + (int)(32 * s);
                join.Size = new Size(Math.Max((int)(104 * s), jw), (int)(30 * s));
                join.Location = new Point(ClientSize.Width - join.Width - (int)(16 * s), ClientSize.Height - (int)(42 * s));
                Invalidate();
            }

            protected override void OnShown(EventArgs e)
            {
                base.OnShown(e);
                box.Focus();
            }

            protected override void OnPaintBackground(PaintEventArgs e)
            {
                var g = e.Graphics;
                g.Clear(KitInk.Window);
                KitInk.Smooth(g);
                float s = Ui;
                KitInk.Draw(g, Ink.Str(Ink.IcoLock), Ink.Icons(12.5f * s), KitInk.Sub, new RectangleF(16 * s, 12 * s, 18 * s, 20 * s), ContentAlignment.MiddleCenter);
                if (fHead != null)
                    KitInk.Draw(g, net.Ssid, fHead, KitInk.Text, new RectangleF(38 * s, 12 * s, ClientSize.Width - 54 * s, 20 * s), ContentAlignment.MiddleLeft);
                var well = new RectangleF(16 * s, 38 * s, ClientSize.Width - 32 * s, 42 * s);
                using (var path = Ink.Round(well, 12 * s))
                {
                    using (var b = new SolidBrush(KitInk.Group)) g.FillPath(b, path);
                    using (var pen = new Pen(KitInk.Faint, 1.2f)) g.DrawPath(pen, path);
                }
                if (fHint != null)
                    KitInk.Draw(g, Lang.T("Esc batal"), fHint, KitInk.Sub, new RectangleF(18 * s, ClientSize.Height - 42 * s, 160 * s, 30 * s), ContentAlignment.MiddleLeft);
            }

            void Commit()
            {
                if (box.Text.Length == 0) return;
                string pass = box.Text;
                Close();
                done(net, pass);
            }

            void DisposeFonts()
            {
                if (fBox != null) { fBox.Dispose(); fBox = null; }
                if (fJoin != null) { fJoin.Dispose(); fJoin = null; }
                if (fHead != null) { fHead.Dispose(); fHead = null; }
                if (fHint != null) { fHint.Dispose(); fHint = null; }
            }

            protected override void Dispose(bool disposing)
            {
                if (disposing) DisposeFonts();
                base.Dispose(disposing);
            }
        }
    }
}
