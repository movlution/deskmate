using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;

namespace Deskmate
{
    partial class Island
    {
        // Multi-line box with its own placeholder
        class ChatInput : KitText
        {
            public string Cue = "";
            [DllImport("user32.dll")] static extern IntPtr SendMessage(IntPtr h, int msg, IntPtr w, IntPtr l);

            public ChatInput()
            {
                Multiline = true;
                AcceptsReturn = true;
                WordWrap = true;
                BorderStyle = BorderStyle.None;
                ScrollBars = ScrollBars.None;
            }

            // Margins at zero: the placeholder sits where text starts
            protected override void OnHandleCreated(EventArgs e)
            {
                base.OnHandleCreated(e);
                SendMessage(Handle, 0x00D3, (IntPtr)3, IntPtr.Zero);
            }

            public int LineCount() { return IsHandleCreated ? Math.Max(1, (int)SendMessage(Handle, 0x00BA, IntPtr.Zero, IntPtr.Zero)) : 1; }

            protected override void OnKeyDown(KeyEventArgs e)
            {
                // Multi-line boxes skip Ctrl+A on their own
                if (e.Control && e.KeyCode == Keys.A) { SelectAll(); e.SuppressKeyPress = true; return; }
                base.OnKeyDown(e);
            }

            protected override void OnTextChanged(EventArgs e) { base.OnTextChanged(e); if (TextLength <= 1) Invalidate(); }

            protected override void WndProc(ref Message m)
            {
                base.WndProc(ref m);
                if (m.Msg != 0x000F || TextLength > 0 || Cue.Length == 0) return;
                using (var g = Graphics.FromHwnd(Handle))
                    TextRenderer.DrawText(g, Cue, Font, new Point(0, 0), KitInk.Sub, BackColor, TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding | TextFormatFlags.SingleLine);
            }
        }
    }
}
