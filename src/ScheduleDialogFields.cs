using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.Threading;
using System.Windows.Forms;

namespace Deskmate
{
    partial class Island
    {
        sealed partial class GoogleEventDialog
        {
            sealed class RoundedField : Panel
            {
                public readonly KitText Box = new KitText();
                readonly Label cue = new Label { AutoSize = false };
                bool focused, invalid;
                float uiScale = 1f;

                public bool Invalid
                {
                    get { return invalid; }
                    set { if (invalid != value) { invalid = value; Invalidate(); } }
                }

                public RoundedField()
                {
                    SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
                    Box.BorderStyle = BorderStyle.None;
                    Box.Multiline = false;
                    Box.MaxLength = 500;
                    cue.Cursor = Cursors.IBeam;
                    cue.Click += delegate { Box.Focus(); };
                    Click += delegate { Box.Focus(); };
                    Box.GotFocus += delegate { focused = true; UpdateCue(); Invalidate(); };
                    Box.LostFocus += delegate { focused = false; UpdateCue(); Invalidate(); };
                    Box.TextChanged += delegate { UpdateCue(); };
                    Controls.Add(Box);
                    Controls.Add(cue);
                    cue.BringToFront();
                }

                public void UpdateCue()
                {
                    cue.Visible = Box.Text.Length == 0 && !focused;
                }

                public void Place(float s, Font font, string placeholderText)
                {
                    uiScale = s;
                    BackColor = KitInk.Window;
                    Box.Font = font;
                    Box.BackColor = KitInk.Group;
                    Box.ForeColor = KitInk.Text;
                    cue.Font = font;
                    cue.BackColor = KitInk.Group;
                    cue.ForeColor = KitInk.Faint;
                    cue.Text = placeholderText;
                    int pad = (int)Math.Round(12 * s);
                    int th = Math.Max(16, Box.PreferredHeight);
                    int ty = Math.Max(4, (Height - th) / 2);
                    int tw = Math.Max(20, Width - 2 * pad);
                    Box.SetBounds(pad, ty, tw, th);
                    cue.SetBounds(pad, ty, tw, th);
                    UpdateCue();
                    Invalidate();
                }

                protected override void OnPaintBackground(PaintEventArgs e)
                {
                    var g = e.Graphics;
                    KitInk.Smooth(g);
                    g.Clear(KitInk.Window);
                    var r = new RectangleF(0.75f, 0.75f, Width - 1.5f, Height - 1.5f);
                    float rad = Math.Min(10f * uiScale, r.Height / 2f);
                    using (var path = Ink.Round(r, rad))
                    {
                        using (var b = new SolidBrush(KitInk.Group)) g.FillPath(b, path);
                        Color border = invalid ? Ink.Base.Negative : focused ? KitInk.Accent : KitInk.Line;
                        using (var pen = new Pen(border, focused ? 1.6f : 1f)) g.DrawPath(pen, path);
                    }
                }
            }

            sealed class EventPillButton : Button
            {
                public bool Active;
                public bool Negative;
                public bool Subtle;
                public float Radius = -1f;
                bool isHovered, isPressed;

                public EventPillButton()
                {
                    SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.SupportsTransparentBackColor, true);
                    FlatStyle = FlatStyle.Flat;
                    FlatAppearance.BorderSize = 0;
                    BackColor = Color.Transparent;
                    UseVisualStyleBackColor = false;
                    Cursor = Cursors.Hand;
                }

                protected override void OnMouseEnter(EventArgs e) { base.OnMouseEnter(e); isHovered = true; Invalidate(); }
                protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); isHovered = false; isPressed = false; Invalidate(); }
                protected override void OnMouseDown(MouseEventArgs e)
                {
                    KitInk.ByMouse = true;
                    if (e.Button == MouseButtons.Left) { isPressed = true; Invalidate(); }
                    base.OnMouseDown(e);
                }
                protected override void OnMouseUp(MouseEventArgs e)
                {
                    base.OnMouseUp(e);
                    if (isPressed) { isPressed = false; Invalidate(); }
                }
                protected override void OnKeyUp(KeyEventArgs e) { if (KitInk.ByMouse) { KitInk.ByMouse = false; Invalidate(); } base.OnKeyUp(e); }
                protected override void OnGotFocus(EventArgs e) { base.OnGotFocus(e); Invalidate(); }
                protected override void OnLostFocus(EventArgs e) { base.OnLostFocus(e); Invalidate(); }

                protected override void OnPaint(PaintEventArgs e)
                {
                    var g = e.Graphics;
                    KitInk.Smooth(g);
                    g.Clear(KitInk.Under(this));
                    var kw = FindForm() as KitWindow;
                    float s = kw != null ? kw.Ui : 1f;
                    if (s <= 0) s = 1f;
                    bool ringOn = Focused && ShowFocusCues && !KitInk.ByMouse;
                    var r = ringOn ? new RectangleF(3f * s, 3f * s, Width - 6f * s, Height - 6f * s)
                                   : new RectangleF(0.75f, 0.75f, Width - 1.5f, Height - 1.5f);
                    if (isPressed && Enabled) r.Offset(0, 0.75f * s);

                    Color bg;
                    Color border = Color.Empty;
                    Color fg;
                    if (!Enabled)
                    {
                        bg = KitInk.Group;
                        border = KitInk.Line;
                        fg = KitInk.Faint;
                    }
                    else if (Negative)
                    {
                        bg = isPressed ? Ink.Mix(Ink.Base.Negative, KitInk.Window, 0.75f)
                                       : isHovered ? Ink.Mix(Ink.Base.Negative, KitInk.Window, 0.84f)
                                       : Ink.Mix(Ink.Base.Negative, KitInk.Window, 0.92f);
                        border = Color.FromArgb(isHovered ? 180 : 100, Ink.Base.Negative);
                        fg = Ink.Base.Negative;
                    }
                    else if (Active)
                    {
                        bg = isPressed ? Ink.Mix(KitInk.Accent, KitInk.OnAccent, 0.22f)
                                       : isHovered ? Ink.Mix(KitInk.Accent, KitInk.OnAccent, 0.1f)
                                       : KitInk.Accent;
                        fg = KitInk.OnAccent;
                    }
                    else
                    {
                        bg = isPressed ? KitInk.Hover : isHovered ? KitInk.Hover : KitInk.Group;
                        border = KitInk.Line;
                        fg = isHovered ? KitInk.Text : (Subtle ? KitInk.Sub : KitInk.Text);
                    }

                    float rad = Radius > 0 ? Math.Min(Radius * s, r.Height / 2f) : r.Height / 2f;
                    using (var path = Ink.Round(r, rad))
                    {
                        using (var b = new SolidBrush(bg)) g.FillPath(b, path);
                        if (border != Color.Empty)
                            using (var pen = new Pen(border, 1f)) g.DrawPath(pen, path);
                    }
                    KitInk.Draw(g, Text, Font, fg, r, ContentAlignment.MiddleCenter);
                    if (ringOn)
                    {
                        var ring = new RectangleF(1f, 1f, Width - 2f, Height - 2f);
                        float rrad = Radius > 0 ? Math.Min(Radius * s + 2f, ring.Height / 2f) : ring.Height / 2f;
                        using (var path = Ink.Round(ring, rrad))
                        using (var pen = new Pen(KitInk.Accent, 1.6f)) g.DrawPath(pen, path);
                    }
                }
            }

        }
    }
}
