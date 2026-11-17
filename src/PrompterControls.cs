using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;

namespace Deskmate
{
    // Rounded well around a plain multiline TextBox
    class ScriptWell : Panel
    {
        public readonly TextBox Box = new KitText();
        float ui = 1;

        [DllImport("uxtheme.dll", CharSet = CharSet.Unicode)] static extern int SetWindowTheme(IntPtr hwnd, string app, string idList);

        public ScriptWell()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            Box.BorderStyle = BorderStyle.None;
            Box.Multiline = true;
            Box.AcceptsReturn = true;
            Box.WordWrap = true;
            Box.ScrollBars = ScrollBars.None;
            Box.GotFocus += delegate { Invalidate(); };
            Box.LostFocus += delegate { Invalidate(); };
            // Scrollbar follows the field tone
            Box.HandleCreated += delegate { try { SetWindowTheme(Box.Handle, KitInk.Group.GetBrightness() > 0.5f ? "Explorer" : "DarkMode_Explorer", null); } catch (Exception) { } };
            MouseDown += delegate { Box.Focus(); };
            Controls.Add(Box);
        }

        public void Place(float ui)
        {
            this.ui = ui;
            Box.Font = new Font(Ink.MenuFamily, 14f * ui, GraphicsUnit.Pixel);
            Box.BackColor = KitInk.Group;
            Box.ForeColor = KitInk.Text;
            Box.SetBounds((int)(16 * ui), (int)(12 * ui), Width - (int)(22 * ui), Height - (int)(24 * ui));
            FitBars();
            Invalidate();
        }

        // Scrollbar only once the script overflows
        public void FitBars()
        {
            bool over;
            if (Box.IsHandleCreated) over = (Box.GetLineFromCharIndex(Box.TextLength) + 1) * Box.Font.Height > Box.ClientSize.Height;
            else
            {
                // No handle yet: measure the wrap
                var flags = TextFormatFlags.WordBreak | TextFormatFlags.TextBoxControl | TextFormatFlags.NoPrefix;
                over = TextRenderer.MeasureText(Box.Text, Box.Font, new Size(Box.Width, int.MaxValue), flags).Height > Box.Height;
            }
            var want = over ? ScrollBars.Vertical : ScrollBars.None;
            if (Box.ScrollBars != want) Box.ScrollBars = want;
        }

        protected override void OnPaintBackground(PaintEventArgs e)
        {
            var g = e.Graphics;
            KitInk.Smooth(g);
            g.Clear(KitInk.Under(this));
            var r = new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f);
            using (var path = Ink.Round(r, 12 * ui))
            {
                using (var b = new SolidBrush(KitInk.Group)) g.FillPath(b, path);
                // Quiet at rest, clear ring on focus
                using (var pen = new Pen(Box.Focused ? KitInk.Faint : Ink.Mix(KitInk.Group, KitInk.Text, 0.07f), Box.Focused ? 1.4f : 1f)) g.DrawPath(pen, path);
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing && Box.Font != null) Box.Font.Dispose();
            base.Dispose(disposing);
        }
    }

    // Shortcut rows: action left, key caps right
    class KeyList : Control
    {
        public string[][] Rows = new string[0][];
        // Shared modifiers, spoken before each key
        public string Prefix = "";

        public KeyList()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            TabStop = false;
            AccessibleRole = AccessibleRole.StaticText;
        }

        float Ui { get { var w = FindForm() as KitWindow; return w != null ? w.Ui : 1f; } }

        public string Spoken()
        {
            var sb = new StringBuilder();
            foreach (var r in Rows)
            {
                sb.Append(r[0]).Append(": ").Append(Prefix).Append(r[1]);
                for (int i = 2; i < r.Length; i++) sb.Append(" / ").Append(r[i]);
                sb.Append(". ");
            }
            return sb.ToString().Trim();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            KitInk.Smooth(g);
            g.Clear(KitInk.Under(this));
            float s = Ui, rowH = (Height - 12 * s) / Math.Max(1, Rows.Length);
            using (var path = Ink.Round(new RectangleF(0, 0, Width - 1, Height - 1), 12 * s))
            using (var b = new SolidBrush(KitInk.Group)) g.FillPath(b, path);
            using (var f = new Font(Ink.MenuFamily, 13f * s, GraphicsUnit.Pixel))
            using (var kf = new Font(Ink.MenuFamily, 11.5f * s, GraphicsUnit.Pixel))
            using (var cap = new SolidBrush(KitInk.Line))
                for (int i = 0; i < Rows.Length; i++)
                {
                    float y = 6 * s + i * rowH, x = Width - 16 * s;
                    // Caps laid right to left
                    for (int k = Rows[i].Length - 1; k >= 1; k--)
                    {
                        float w = Math.Max(22 * s, TextRenderer.MeasureText(Rows[i][k], kf).Width + 8 * s);
                        var r = new RectangleF(x - w, y + (rowH - 22 * s) / 2, w, 22 * s);
                        using (var cp = Ink.Round(r, 6 * s)) g.FillPath(cap, cp);
                        KitInk.Draw(g, Rows[i][k], kf, KitInk.Text, r, ContentAlignment.MiddleCenter);
                        x = r.X - 5 * s;
                    }
                    KitInk.Draw(g, Rows[i][0], f, KitInk.Text, new RectangleF(16 * s, y, x - 24 * s, rowH), ContentAlignment.MiddleLeft);
                }
        }
    }
}
