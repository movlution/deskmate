using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Windows.Forms;

namespace Deskmate
{
    sealed class NoteEditor : RichTextBox
    {
        internal static readonly Regex Block = new Regex(
            @"^([ \t]*)(#{1,6}(?=[ \t]|$)|>+|[-+*] \[[ xX]\]|[0-9]+[.)]|[-+*])(?:[ \t]+|$)");
        static readonly Regex Inline = new Regex(@"(?<mark>`|\*{1,3})(?<body>[^`*
]+)\k<mark>");
        static readonly Regex Fence = new Regex(@"^[ 	]*```", RegexOptions.Multiline);
        static readonly Regex Divider = new Regex(@"^[ \t]*([-*_])(?:[ \t]*\1){2,}[ \t]*$");

        const int GetOleInterface = 0x043C, GetScrollPosition = 0x04DD, SetScrollPosition = 0x04DE;
        const int SetRedraw = 0x000B, PaintMessage = 0x000F, PrintClient = 0x0318, PasteMessage = 0x0302;
        const int TomSuspend = -9999995, TomResume = -9999994;
        static readonly Guid TextDocumentId = new Guid("8CC497C0-A1DF-11CE-8098-00AA0047BE5D");
        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        delegate int DocumentUndo(IntPtr document, int count, IntPtr actual);
        [DllImport("user32.dll", EntryPoint = "SendMessageW")]
        static extern IntPtr Send(IntPtr window, int message, IntPtr wParam, ref IntPtr lParam);
        [DllImport("user32.dll", EntryPoint = "SendMessageW")]
        static extern IntPtr Send(IntPtr window, int message, IntPtr wParam, ref Point lParam);
        [DllImport("user32.dll", EntryPoint = "SendMessageW")]
        static extern IntPtr Send(IntPtr window, int message, IntPtr wParam, IntPtr lParam);

        string styledText;
        Font styledFont;
        Color styledColor;
        internal Action OpenBlockMenu;

        public NoteEditor()
        {
            BorderStyle = BorderStyle.None;
            DetectUrls = false;
            ScrollBars = RichTextBoxScrollBars.Vertical;
            EnableAutoDragDrop = false;
            HideSelection = false;
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            styledText = null;
            Native.ThemeScroll(this);
        }

        protected override void OnKeyPress(KeyPressEventArgs e)
        {
            if (e.KeyChar == '/' && !ReadOnly && SelectionLength == 0 && OpenBlockMenu != null)
            {
                string text = Text;
                int caret = SelectionStart;
                int start = LineStart(text, caret);
                string beforeCaret = text.Substring(start, caret - start);
                Match block = Block.Match(beforeCaret);
                bool atBlockStart = string.IsNullOrWhiteSpace(beforeCaret)
                    || (block.Success && block.Length == beforeCaret.Length && char.IsWhiteSpace(beforeCaret[beforeCaret.Length - 1]));
                if (atBlockStart && !InCodeBlock(text, start))
                {
                    e.Handled = true;
                    SelectedText = "/";
                    int commandCaret = SelectionStart;
                    string commandText = Text;
                    // Let RichEdit finish handling WM_CHAR before the popup takes focus.
                    BeginInvoke(new Action(delegate
                    {
                        if (!IsDisposed && Focused && !ReadOnly && SelectionStart == commandCaret && Text == commandText)
                            OpenBlockMenu();
                    }));
                    return;
                }
            }
            base.OnKeyPress(e);
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (!ReadOnly && (keyData == (Keys.Control | Keys.B) || keyData == (Keys.Control | Keys.I)))
            {
                ToggleInline(keyData == (Keys.Control | Keys.B) ? "**" : "*");
                return true;
            }
            if (!ReadOnly && (keyData == (Keys.Control | Keys.V) || keyData == (Keys.Shift | Keys.Insert)))
            {
                PastePlainText();
                return true;
            }
            return base.ProcessCmdKey(ref msg, keyData);
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == PasteMessage && !ReadOnly) { PastePlainText(); return; }
            base.WndProc(ref m);
            if ((m.Msg != PaintMessage && m.Msg != PrintClient) || !IsHandleCreated || ReadOnly || TextLength != 0) return;
            using (var g = m.Msg == PrintClient && m.WParam != IntPtr.Zero ? Graphics.FromHdc(m.WParam) : CreateGraphics())
            {
                var bounds = new Rectangle(3, 1, Math.Max(1, ClientSize.Width - 6), Font.Height + 4);
                TextRenderer.DrawText(g, Lang.T("Ketik / untuk memilih blok"), Font, bounds, KitInk.Sub,
                    TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis | TextFormatFlags.VerticalCenter);
            }
        }

        void PastePlainText()
        {
            try
            {
                if (Clipboard.ContainsText()) SelectedText = Clipboard.GetText();
            }
            catch (ExternalException) { }
        }

        internal static int LineStart(string text, int caret)
        {
            return caret == 0 ? 0 : text.LastIndexOf('\n', caret - 1) + 1;
        }

        internal static bool InCodeBlock(string text, int lineStart)
        {
            bool code = false;
            for (Match fence = Fence.Match(text); fence.Success && fence.Index < lineStart; fence = fence.NextMatch())
                code = !code;
            return code;
        }

        internal void ToggleInline(string marker)
        {
            if (ReadOnly) return;
            int start = SelectionStart;
            string value = SelectedText;
            string text = Text;
            if (value.IndexOf('\n') >= 0 || InCodeBlock(text, LineStart(text, start))) return;
            int end = start + value.Length;
            int left = start, right = end;
            while (left > 0 && start - left < 3 && text[left - 1] == '*') left--;
            while (right < text.Length && right - end < 3 && text[right] == '*') right++;
            int wrapping = start - left;
            if (wrapping != right - end) { left = start; right = end; wrapping = 0; }
            string next = new string('*', wrapping ^ marker.Length);
            Select(left, right - left);
            SelectedText = next + value + next;
            Select(left + next.Length, value.Length);
        }

        internal void ApplyBlock(string prefix, bool fromSlash)
        {
            if (ReadOnly) return;
            string text = Text;
            int caret = SelectionStart;
            int start = LineStart(text, caret);
            int end = text.IndexOf('\n', start);
            if (end < 0) end = text.Length;
            string line = text.Substring(start, end - start);
            int indent = line.Length - line.TrimStart(' ', '\t').Length;
            Match old = Block.Match(line);
            int replace = old.Success ? old.Length - indent : 0;
            if (Divider.IsMatch(line)) replace = line.Length - indent;
            if (fromSlash)
            {
                int slash = start + indent + replace;
                if (slash != caret - 1 || text[slash] != '/') return;
                replace++;
            }
            Select(start + indent, replace);
            if (prefix == "---")
            {
                bool hasBody = start + indent + replace < end;
                if (!hasBody && end < text.Length) Select(start + indent, replace + 1);
                SelectedText = prefix + "\n";
            }
            else if (prefix == "```")
            {
                string body = line.Substring(indent + replace);
                string padding = line.Substring(0, indent);
                Select(start + indent, line.Length - indent);
                SelectedText = prefix + "\n" + padding + body + "\n" + padding + prefix;
                Select(start + indent + prefix.Length + 1 + indent + body.Length, 0);
            }
            else SelectedText = prefix;
        }

        internal void RefreshFormatting()
        {
            if (!IsHandleCreated) return;
            string text = Text;
            if (text == styledText && ReferenceEquals(Font, styledFont) && ForeColor == styledColor) return;
            if (text.Length == 0) { styledText = ""; return; }

            IntPtr ole = IntPtr.Zero, document = IntPtr.Zero;
            Guid id = TextDocumentId;
            Send(Handle, GetOleInterface, IntPtr.Zero, ref ole);
            if (ole == IntPtr.Zero) return;
            try
            {
                if (Marshal.QueryInterface(ole, ref id, out document) != 0) return;
                // ITextDocument derives from IDispatch. Undo is the 16th document method.
                IntPtr slot = Marshal.ReadIntPtr(Marshal.ReadIntPtr(document), (7 + 15) * IntPtr.Size);
                var undo = (DocumentUndo)Marshal.GetDelegateForFunctionPointer(slot, typeof(DocumentUndo));
                if (undo(document, TomSuspend, IntPtr.Zero) < 0) return;
                try { FormatText(text); }
                finally { undo(document, TomResume, IntPtr.Zero); }
                styledText = text;
                styledFont = Font;
                styledColor = ForeColor;
            }
            finally
            {
                if (document != IntPtr.Zero) Marshal.Release(document);
                Marshal.Release(ole);
            }
        }

        void FormatText(string text)
        {
            int start = SelectionStart, length = SelectionLength;
            Point scroll = Point.Empty;
            Send(Handle, GetScrollPosition, IntPtr.Zero, ref scroll);
            bool redraw = Visible;
            if (redraw) Send(Handle, SetRedraw, IntPtr.Zero, IntPtr.Zero);
            try
            {
                using (var h1 = new Font(Font.FontFamily, Font.Size * 1.52f, FontStyle.Bold))
                using (var h2 = new Font(Font.FontFamily, Font.Size * 1.3f, FontStyle.Bold))
                using (var h3 = new Font(Font.FontFamily, Font.Size * 1.12f, FontStyle.Bold))
                using (var bold = new Font(Font, FontStyle.Bold))
                using (var italic = new Font(Font, FontStyle.Italic))
                using (var boldItalic = new Font(Font, FontStyle.Bold | FontStyle.Italic))
                using (var done = new Font(Font, FontStyle.Strikeout))
                using (var mono = new Font(FontFamily.GenericMonospace, Font.Size))
                {
                    PaintRange(0, text.Length, Font, ForeColor);
                    int offset = 0;
                    bool code = false;
                    foreach (string line in text.Split('\n'))
                    {
                        string trimmed = line.TrimStart();
                        if (trimmed.StartsWith("```", StringComparison.Ordinal))
                        {
                            PaintRange(offset, line.Length, mono, KitInk.Sub);
                            code = !code;
                        }
                        else if (code) PaintRange(offset, line.Length, mono, ForeColor);
                        else if (Divider.IsMatch(line)) PaintRange(offset, line.Length, mono, KitInk.Sub);
                        else
                        {
                            Match block = Block.Match(line);
                            bool useInline = true;
                            if (block.Success)
                            {
                                string marker = block.Groups[2].Value;
                                int prefix = block.Length;
                                if (marker[0] == '#')
                                {
                                    useInline = false;
                                    Font heading = marker.Length == 1 ? h1 : marker.Length == 2 ? h2 : h3;
                                    PaintRange(offset, line.Length, heading, ForeColor);
                                    PaintRange(offset, prefix, Font, KitInk.Sub);
                                }
                                else
                                {
                                    PaintRange(offset, prefix, Font, KitInk.Sub);
                                    if (marker[0] == '>')
                                    {
                                        useInline = false;
                                        PaintRange(offset + prefix, line.Length - prefix, italic, KitInk.Sub);
                                    }
                                    if (marker.IndexOf("[x]", StringComparison.OrdinalIgnoreCase) >= 0)
                                    {
                                        useInline = false;
                                        PaintRange(offset + prefix, line.Length - prefix, done, KitInk.Sub);
                                    }
                                }
                            }
                            if (useInline)
                            {
                                foreach (Match inline in Inline.Matches(line, block.Success ? block.Length : 0))
                                {
                                    Group body = inline.Groups["body"];
                                    string mark = inline.Groups["mark"].Value;
                                    Font style = mark == "`" ? mono : mark.Length == 3 ? boldItalic : mark.Length == 2 ? bold : italic;
                                    PaintRange(offset + inline.Index, inline.Length, Font, KitInk.Sub);
                                    PaintRange(offset + body.Index, body.Length, style, ForeColor);
                                }
                            }
                        }
                        offset += line.Length + 1;
                    }
                }
            }
            finally
            {
                Select(start, length);
                Send(Handle, SetScrollPosition, IntPtr.Zero, ref scroll);
                if (redraw)
                {
                    Send(Handle, SetRedraw, new IntPtr(1), IntPtr.Zero);
                    Invalidate();
                }
            }
        }

        void PaintRange(int start, int length, Font font, Color color)
        {
            if (length <= 0) return;
            Select(start, length);
            SelectionFont = font;
            SelectionColor = color;
        }
    }
}
