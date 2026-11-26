using System;
using System.Drawing;
using System.Windows.Forms;

namespace Deskmate
{
    // Snackbar at the bottom of the work window: a short message and an optional Undo.
    // It never takes the focus. Fading is drawn as a blend toward the surface under it,
    // so it stays one child control without a layered window. Reduced motion shows and hides at once
    sealed class WorkToast : KitControl
    {
        public Func<Color> Back;
        string message = "", action = "";
        Action onAction;
        float shown;
        bool leaving;
        KitGlide glide;
        int deadline;
        bool hotAction, hotClose, pressAction;
        readonly Timer anim = new Timer { Interval = 15 };
        const int LifeMs = 5000;

        public WorkToast()
        {
            SetStyle(ControlStyles.StandardClick | ControlStyles.StandardDoubleClick | ControlStyles.Selectable, false);
            TabStop = false;
            Visible = false;
            AccessibleRole = AccessibleRole.Alert;
            anim.Tick += delegate { Step(); };
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) anim.Dispose();
            base.Dispose(disposing);
        }

        public bool Showing { get { return Visible && !leaving; } }
        public bool HasAction { get { return Showing && onAction != null; } }

        // Width that fits the text; the window places it
        public Size Measure(int maxWidth)
        {
            float s = Ui;
            var flags = TextFormatFlags.NoPadding | TextFormatFlags.SingleLine;
            int w = TextRenderer.MeasureText(message, Ink.Text(13f * s), new Size(int.MaxValue, 1000), flags).Width + (int)(40 * s);
            if (action.Length > 0) w += ActionWidth() + (int)(Ink.Sp8 * s);
            w += (int)(30 * s);
            return new Size(Math.Max((int)(220 * s), Math.Min(maxWidth, w)), (int)(44 * s));
        }

        int ActionWidth()
        {
            float s = Ui;
            return TextRenderer.MeasureText(action, Ink.Semi(13f * s), new Size(int.MaxValue, 1000), TextFormatFlags.NoPadding | TextFormatFlags.SingleLine).Width + (int)(Ink.Sp24 * s);
        }

        public event Action Placing;

        public void Show(string text, string actionText, Action act)
        {
            message = text ?? "";
            action = act != null ? (actionText ?? "") : "";
            onAction = act;
            AccessibleName = action.Length > 0 ? message + ", " + action : message;
            leaving = false;
            deadline = Environment.TickCount + LifeMs;
            if (Placing != null) Placing();
            bool wasVisible = Visible;
            Visible = true;
            BringToFront();
            if (!wasVisible) { shown = 0; glide.Begin(0); }
            else glide.Begin(shown);
            if (Anim.Reduced) shown = 1;
            anim.Start();
            Invalidate();
            AccessibilityNotifyClients(AccessibleEvents.NameChange, -1);
        }

        public void Dismiss()
        {
            if (!Visible || leaving) return;
            leaving = true;
            onAction = null;
            glide.Begin(shown);
            if (Anim.Reduced) { Finish(); return; }
            anim.Start();
        }

        // Undo now; false when there is nothing to undo
        public bool RunAction()
        {
            if (!HasAction) return false;
            var a = onAction;
            onAction = null;
            Dismiss();
            a();
            return true;
        }

        void Finish()
        {
            anim.Stop();
            shown = 0;
            leaving = false;
            Visible = false;
        }

        void Step()
        {
            if (leaving)
            {
                shown = glide.Sample(0, 0.14f);
                if (shown <= 0) { Finish(); return; }
            }
            else
            {
                shown = glide.Sample(1, 0.22f);
                // A held pointer keeps the message up, like most snackbars
                if (Hovered) deadline = Math.Max(deadline, Environment.TickCount + 1500);
                if (unchecked(Environment.TickCount - deadline) >= 0) { Dismiss(); return; }
            }
            Invalidate();
        }

        RectangleF ActionRect()
        {
            float s = Ui;
            if (action.Length == 0) return RectangleF.Empty;
            float w = ActionWidth();
            return new RectangleF(CloseRect().Left - w - Ink.Sp4 * s, (Height - 32 * s) / 2f, w, 32 * s);
        }

        RectangleF CloseRect()
        {
            float s = Ui;
            return new RectangleF(Width - 38 * s, (Height - 30 * s) / 2f, 30 * s, 30 * s);
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            bool a = ActionRect().Contains(e.Location), c = CloseRect().Contains(e.Location);
            if (a != hotAction || c != hotClose) { hotAction = a; hotClose = c; Invalidate(); }
            Cursor = a || c ? Cursors.Hand : Cursors.Default;
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            hotAction = hotClose = pressAction = false;
            base.OnMouseLeave(e);
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            // Not base: a toast never takes the focus away from the editor or the list
            if (e.Button == MouseButtons.Left && ActionRect().Contains(e.Location)) { pressAction = true; Invalidate(); }
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            bool wasPressed = pressAction;
            pressAction = false;
            if (e.Button != MouseButtons.Left) return;
            if (wasPressed && ActionRect().Contains(e.Location)) RunAction();
            else if (CloseRect().Contains(e.Location)) Dismiss();
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            KitInk.Smooth(g);
            Color under = Back != null ? Back() : KitInk.Group;
            g.Clear(under);
            if (shown <= 0) return;
            float s = Ui;
            float a = Ease.OutCubic(shown);
            // Rises a few pixels while it fades in, sinks a little as it leaves
            float rise = (1 - a) * 10 * s;
            var pill = new RectangleF(1, 1 + rise, Width - 2.5f, Height - 2.5f);
            float radius = Ink.RadiusControl * s + 2 * s;

            // Inverse surface on paper, a lifted tone at night: readable over rows and text
            Color surface = Ink.Light ? KitInk.Text : Ink.Mix(KitInk.Group, KitInk.Text, 0.17f);
            Color ink = Ink.Light ? KitInk.Window : KitInk.Text;
            Color accent = Ink.Light ? Ink.Mix(KitInk.Accent, Color.White, 0.55f) : KitInk.Accent;
            KitInk.FillRound(g, pill, radius, Ink.Mix(under, surface, a));

            var close = CloseRect(); close.Offset(0, rise);
            var act = ActionRect(); act.Offset(0, rise);
            float textRight = act.IsEmpty ? close.Left : act.Left;
            KitInk.Draw(g, message, Ink.Text(13f * s), Ink.Mix(under, ink, a),
                new RectangleF(pill.X + 18 * s, pill.Y, Math.Max(10, textRight - pill.X - 22 * s), pill.Height), ContentAlignment.MiddleLeft);

            float slotR = Ink.RadiusControl * s - 2 * s;
            if (!act.IsEmpty)
            {
                if (hotAction || pressAction)
                    KitInk.FillRound(g, act, slotR, Ink.Mix(under, Ink.Mix(surface, ink, pressAction ? 0.18f : 0.11f), a));
                KitInk.Draw(g, action, Ink.Semi(13f * s), Ink.Mix(under, accent, a), act, ContentAlignment.MiddleCenter);
            }
            if (hotClose) KitInk.FillRound(g, close, slotR, Ink.Mix(under, Ink.Mix(surface, ink, 0.11f), a));
            KitInk.Draw(g, "", Ink.Icons(11f * s), Ink.Mix(under, Ink.Mix(surface, ink, 0.7f), a), close, ContentAlignment.MiddleCenter);

            // Remaining time as a hairline along the bottom, so the undo window is visible
            if (!leaving && onAction != null && !Anim.Reduced)
            {
                float left = Math.Max(0f, Math.Min(1f, unchecked(deadline - Environment.TickCount) / (float)LifeMs));
                float w = (pill.Width - 2 * radius) * left;
                if (w > 1) g.FillRectangle(KitInk.BrushOf(Ink.Mix(under, Ink.Mix(surface, accent, 0.6f), a)), pill.X + radius, pill.Bottom - 2.5f * s, w, 1.5f * s);
            }
        }
    }
}
