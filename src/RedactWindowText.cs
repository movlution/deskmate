using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace Deskmate
{
    partial class RedactWindow : KitWindow
    {
        // Inline KitText for Text tool & double-click edit
        void EnsureInlineText()
        {
            if (inlineText != null) return;
            inlineText = new KitText
            {
                Multiline = true,
                AcceptsReturn = true,
                WordWrap = false,
                BorderStyle = BorderStyle.FixedSingle,
                Visible = false
            };
            inlineText.TextChanged += delegate { if (inlineTextActive) PositionInlineText(); };
            inlineText.LostFocus += delegate { if (inlineTextActive) CommitInlineText(); };
            Controls.Add(inlineText);
        }

        void OpenInlineText(PointF imgPos, int existingIndex)
        {
            EnsureInlineText();
            editingTextIndex = existingIndex;
            textImgPos = imgPos;
            inlineTextActive = true;
            if (existingIndex >= 0)
            {
                var e = edits[existingIndex];
                inkColor.Value = Color.FromArgb(255, e.StrokeColor.R, e.StrokeColor.G, e.StrokeColor.B);
                fillToggle.Checked = e.Filled;
                inlineText.Text = (e.TextValue ?? "").Replace("\n", "\r\n");
            }
            else
            {
                inlineText.Text = "";
            }
            ApplyInlineTextStyle();
            inlineText.Visible = true;
            inlineText.BringToFront();
            PositionInlineText();
            inlineText.Focus();
            inlineText.SelectionStart = inlineText.Text.Length;
            inlineText.SelectionLength = 0;
        }

        void ApplyInlineTextStyle()
        {
            if (inlineText == null) return;
            float imgFs = editingTextIndex >= 0 ? edits[editingTextIndex].FontSize : CurrentFontSize();
            float scrFs = Math.Max(12f, Math.Min(48f, imgFs * scale));
            inlineText.Font = new Font(Ink.MenuFamily, scrFs, FontStyle.Bold, GraphicsUnit.Pixel);
            if (fillToggle.Checked)
            {
                inlineText.BackColor = inkColor.Value;
                inlineText.ForeColor = ContrastInk(inkColor.Value);
            }
            else
            {
                inlineText.BackColor = KitInk.Window;
                inlineText.ForeColor = Island.Contrast(inkColor.Value, KitInk.Window) >= 2.8 ? inkColor.Value : KitInk.Text;
            }
            PositionInlineText();
        }

        void PositionInlineText()
        {
            if (inlineText == null || !inlineTextActive) return;
            var sp = ToScreen(textImgPos);
            string sample = string.IsNullOrEmpty(inlineText.Text) ? "Abc" : inlineText.Text + " ";
            var measured = TextRenderer.MeasureText(sample, inlineText.Font, new Size(Math.Max(120, viewport.Width - 20), viewport.Height), TextFormatFlags.NoPrefix);
            int w = Math.Max((int)Math.Round(110 * Ui), measured.Width + (int)Math.Round(24 * Ui));
            int h = Math.Max(inlineText.Font.Height + (int)Math.Round(14 * Ui), measured.Height + (int)Math.Round(14 * Ui));
            int x = Math.Max(viewport.Left + 4, Math.Min(viewport.Right - w - 4, (int)Math.Round(sp.X)));
            int y = Math.Max(viewport.Top + 4, Math.Min(viewport.Bottom - h - 4, (int)Math.Round(sp.Y)));
            inlineText.SetBounds(x, y, Math.Min(w, viewport.Width - 8), Math.Min(h, viewport.Height - 8));
        }

        void CommitInlineText()
        {
            if (!inlineTextActive) return;
            inlineTextActive = false;
            string raw = inlineText.Text ?? "";
            string txt = raw.Replace("\r\n", "\n").TrimEnd('\r', '\n');
            inlineText.Visible = false;
            Focus();

            int idx = editingTextIndex;
            editingTextIndex = -1;

            if (idx >= GeometryBaseIndex() && idx < edits.Count)
            {
                if (txt.Trim().Length == 0)
                {
                    PushUndo();
                    edits.RemoveAt(idx);
                    selectedIndex = -1;
                    RebuildWork(false);
                    UpdateContextBar();
                    LayoutControls();
                    Invalidate();
                    return;
                }
                var existing = edits[idx];
                if (existing.TextValue != txt || existing.StrokeColor != inkColor.Value || existing.Filled != fillToggle.Checked)
                {
                    PushUndo();
                    existing.TextValue = txt;
                    existing.StrokeColor = inkColor.Value;
                    existing.Filled = fillToggle.Checked;
                    RebuildWork(false);
                    Invalidate();
                }
                return;
            }

            if (txt.Trim().Length == 0) return;
            AddEdit(new Edit
            {
                Kind = EditKind.Text,
                Pos = textImgPos,
                TextValue = txt,
                StrokeColor = inkColor.Value,
                FontSize = CurrentFontSize(),
                Filled = fillToggle.Checked
            });
        }

        void CancelInlineText()
        {
            if (!inlineTextActive) return;
            inlineTextActive = false;
            editingTextIndex = -1;
            inlineText.Visible = false;
            Focus();
            Invalidate();
        }

        void DeleteSelected()
        {
            if (ActiveTool != ToolMode.Select || selectedIndex < GeometryBaseIndex() || selectedIndex >= edits.Count) return;
            PushUndo();
            edits.RemoveAt(selectedIndex);
            selectedIndex = -1;
            RebuildWork(false);
            UpdateContextBar();
            LayoutControls();
            Invalidate();
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keys)
        {
            // Guard shortcuts while inline KitText is active/focused
            if (inlineTextActive || (inlineText != null && inlineText.Focused))
            {
                if (keys == Keys.Escape) { CancelInlineText(); return true; }
                if (keys == Keys.Enter) { CommitInlineText(); return true; }
                if (keys == (Keys.Shift | Keys.Enter))
                {
                    inlineText.SelectedText = Environment.NewLine;
                    return true;
                }
                return base.ProcessCmdKey(ref msg, keys);
            }

            if (keys == Keys.Space && !spaceDown && !(ActiveControl is ButtonBase))
            {
                spaceDown = true;
                UpdateViewportCursor(PointToClient(Cursor.Position));
                return true;
            }

            if (keys == (Keys.Control | Keys.Z)) { Undo(); return true; }
            if (keys == (Keys.Control | Keys.Y) || keys == (Keys.Control | Keys.Shift | Keys.Z)) { Redo(); return true; }
            if (keys == (Keys.Control | Keys.R)) { AddEdit(new Edit { Kind = EditKind.Turn }); return true; }
            if (keys == (Keys.Control | Keys.C)) { CopyOut(); return true; }
            if (keys == (Keys.Control | Keys.S)) { SaveOut(); return true; }
            if (keys == (Keys.Control | Keys.D0) || keys == (Keys.Control | Keys.NumPad0)) { ZoomFit(); return true; }
            if (keys == (Keys.Control | Keys.Oemplus) || keys == (Keys.Control | Keys.Add)) { StepZoom(1.25f); return true; }
            if (keys == (Keys.Control | Keys.OemMinus) || keys == (Keys.Control | Keys.Subtract)) { StepZoom(1f / 1.25f); return true; }

            if (keys == Keys.Enter)
            {
                if (ActiveTool == ToolMode.Crop && pendingCrop.Width >= 8 && pendingCrop.Height >= 8)
                {
                    ApplyPendingCrop();
                    return true;
                }
                if (ActiveControl is KitButton && ActiveControl != copyBtn)
                    return base.ProcessCmdKey(ref msg, keys);
                CopyOut();
                return true;
            }

            if (keys == Keys.Delete || keys == Keys.Back)
            {
                if (ActiveTool == ToolMode.Select && selectedIndex >= GeometryBaseIndex() && selectedIndex < edits.Count)
                {
                    DeleteSelected();
                    return true;
                }
            }

            if (keys == Keys.Escape)
            {
                if (panning) { panning = false; Capture = false; UpdateViewportCursor(PointToClient(Cursor.Position)); return true; }
                if (dragging) { dragging = false; Capture = false; stroke.Clear(); dragBox = Rectangle.Empty; Invalidate(); return true; }
                if (ActiveTool == ToolMode.Crop && !pendingCrop.IsEmpty) { ClearPendingCrop(); return true; }
                if (ActiveTool == ToolMode.Select && selectedIndex >= 0) { selectedIndex = -1; UpdateContextBar(); LayoutControls(); Invalidate(); return true; }
                Close();
                return true;
            }

            // Single-key tool shortcuts (when no modifiers held)
            if ((keys & Keys.Modifiers) == Keys.None)
            {
                switch (keys)
                {
                    case Keys.V: toolStrip.Pick(ToolMode.Select); return true;
                    case Keys.A: toolStrip.Pick(ToolMode.Arrow); return true;
                    case Keys.L: toolStrip.Pick(ToolMode.Line); return true;
                    case Keys.R: toolStrip.Pick(ToolMode.Rect); return true;
                    case Keys.O: toolStrip.Pick(ToolMode.Ellipse); return true;
                    case Keys.P: toolStrip.Pick(ToolMode.Pen); return true;
                    case Keys.H: toolStrip.Pick(ToolMode.Highlight); return true;
                    case Keys.T: toolStrip.Pick(ToolMode.Text); return true;
                    case Keys.N: toolStrip.Pick(ToolMode.Step); return true;
                    case Keys.B: toolStrip.Pick(ToolMode.Redact); return true;
                    case Keys.C: toolStrip.Pick(ToolMode.Crop); return true;
                }
            }

            return base.ProcessCmdKey(ref msg, keys);
        }

        protected override void OnKeyUp(KeyEventArgs e)
        {
            base.OnKeyUp(e);
            if (e.KeyCode == Keys.Space && spaceDown)
            {
                spaceDown = false;
                UpdateViewportCursor(PointToClient(Cursor.Position));
            }
        }
    }
}
