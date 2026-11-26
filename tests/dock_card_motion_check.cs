using System;
using System.Drawing;

namespace Deskmate
{
    static class DockCardMotionCheck
    {
        public static void Run()
        {
            Check(0, 1f);
            Check(-1920, 1.5f);
            int launches = 0;
            using (var motion = new DockCardMotion(IntPtr.Zero, Rectangle.Empty, Rectangle.Empty, DockWindowAction.Launch))
            {
                motion.StageLaunch(new Bitmap(1, 1), () => launches++);
                Require(launches == 0, "Scheduling a launch allows the first frame before dispatch");
                motion.FinishPendingCommand();
                motion.FinishPendingCommand();
                Require(launches == 1, "Completion and cleanup cannot launch the app twice");
            }
            using (var motion = new DockCardMotion(IntPtr.Zero, Rectangle.Empty, Rectangle.Empty, DockWindowAction.Open))
            {
                motion.StageFocus(new Bitmap(1, 1), 0, false);
                Require(motion.PendingFocus, "An uncached open stages the app card before focus");
                motion.FinishPendingCommand(false);
                Require(!motion.PendingFocus, "A newer click retires an unsent focus request");
            }
        }

        static void Check(int origin, float dpi)
        {
            var card = new Rectangle(origin + 160, 100, 640, 400);
            var icon = new Rectangle(origin + 920, 710, 48, 48);
            var monitor = new Rectangle(origin, 0, 1024, 768);
            var maximized = Rectangle.FromLTRB(origin, 0, origin + 1024, 720);
            var withResizeBorders = Rectangle.FromLTRB(origin - 8, -8, origin + 1032, 720);
            Require(DockCardMotion.FitCardBounds(maximized, monitor, true) == maximized, "Maximized window is eligible");
            Require(DockCardMotion.FitCardBounds(monitor, monitor, true) == monitor, "Maximized window can fill monitor when taskbar is hidden");
            Require(DockCardMotion.FitCardBounds(withResizeBorders, monitor, true) == maximized, "Maximized resize borders are clipped");
            Require(DockCardMotion.FitCardBounds(withResizeBorders, monitor, false).IsEmpty, "Spanning normal window is skipped");
            maximized.Offset(1024, 0);
            Require(DockCardMotion.FitCardBounds(maximized, monitor, true).IsEmpty, "Window on another monitor is skipped");

            byte alpha;
            var before = DockCardMotion.Frame(card, icon, dpi, false, 0, out alpha);
            Require(before.Width > card.Width && before.Top < card.Top && alpha > 0, "Card starts lifted");
            Require(DockCardMotion.Frame(card, icon, dpi, false, 1, out alpha) == card && alpha == 0, "Landing aligns with the real window");
            Require(DockCardMotion.Frame(card, icon, dpi, true, 0, out alpha) == card && alpha == 255, "Minimize starts on the real window");
            var glide = DockCardMotion.Frame(card, icon, dpi, true, 0.30f, out alpha);
            Require(glide.Size == card.Size && glide.X > card.X + 30 * dpi && glide.Top > card.Top + 24 * dpi && alpha == 255,
                "The card moves clearly toward the icon before shrinking");
            var middle = DockCardMotion.Frame(card, icon, dpi, true, 0.50f, out alpha);
            Require(middle.Width < card.Width && alpha == 255, "The card shrinks after the glide");
            var end = DockCardMotion.Frame(card, icon, dpi, true, 1, out alpha);
            Require(icon.Contains(end) && alpha == 0, "Minimize ends inside the icon");
            var halfway = DockCardMotion.Frame(card, icon, dpi, true, DockCardMotion.DeckContact / 2, out alpha);
            Require(CenterY(halfway) < (CenterY(card) + CenterY(end)) * 0.5 - 12 * dpi, "The card follows an arc");
            var contact = DockCardMotion.Frame(card, icon, dpi, true, DockCardMotion.DeckContact, out alpha);
            Require(contact == end && alpha == 255, "The visible card seats in the icon before it fades");
            Require(DockCardMotion.DeckSpread(true, 0) == 0 && DockCardMotion.DeckSpread(true, 0.60f) > 0.95f
                && DockCardMotion.DeckSpread(true, DockCardMotion.DeckContact) == 0, "The deck fans out mid-flight and squares up at contact");
            Require(DockCardMotion.DeckTilt(0, true, 1, 0.60f) > 0 && DockCardMotion.DeckTilt(1, true, 1, 0.60f) < 0
                && DockCardMotion.DeckTilt(0, true, -1, 0.60f) < 0, "Front and backing cards fan in opposite directions and follow the icon side");
            Require(DockCardMotion.DeckSpread(false, 0) == 1 && DockCardMotion.DeckSpread(false, 1) == 0,
                "The opening fan settles into the window");
            var firstRestore = DockCardMotion.Frame(card, icon, dpi, false, 0, out alpha, true);
            Require(icon.Contains(firstRestore) && alpha == 255, "A staged restore starts as an opaque card at the Dock");
            var revealed = DockCardMotion.Frame(card, icon, dpi, false, DockCardMotion.RestoreReveal, out alpha, true);
            Require(revealed == card && alpha == 255, "The card covers the window when native restore is requested");
            Require(DockCardMotion.Frame(card, icon, dpi, false, 1, out alpha, true) == card && alpha == 0,
                "Staged restore fades only as the real window takes over");
            var centered = new Rectangle(card.X + card.Width / 2 - icon.Width / 2, icon.Y, icon.Width, icon.Height);
            var centeredGlide = DockCardMotion.Frame(card, centered, dpi, true, 0.30f, out alpha);
            Require(centeredGlide.Size == card.Size && centeredGlide.Top > card.Top + 24 * dpi,
                "A centered Dock icon still gets visible translation, not a center zoom");

            using (var bitmap = new Bitmap(1024, 768))
            using (var g = Graphics.FromImage(bitmap))
            {
                int previousTop = before.Top;
                double previousCenterY = CenterY(card);
                int prevWidth = card.Width;
                byte prevAlpha = 255;
                for (int i = 0; i <= 60; i++)
                {
                    float t = i / 60f;
                    var landing = DockCardMotion.Frame(card, icon, dpi, false, t, out alpha);
                    Require(landing.Top >= previousTop && landing.Width >= card.Width, "Landing descends without overshoot");
                    previousTop = landing.Top;
                    var lift = DockCardMotion.Frame(card, icon, dpi, true, t, out alpha);
                    Require(CenterY(lift) >= previousCenterY - 1, "The card center advances toward the Dock without reversing");
                    previousCenterY = CenterY(lift);
                    Require(lift.Width <= prevWidth && alpha <= prevAlpha && (t > 0.80f || alpha == 255), "The card only shrinks and fades near the Dock");
                    prevWidth = lift.Width;
                    prevAlpha = alpha;
                    Require(Math.Abs(lift.Height - lift.Width * (card.Height / (double)card.Width)) <= 1, "Lift preserves aspect ratio");
                    lift.Offset(-origin, 0);
                    g.Clear(Color.Transparent);
                    using (var brush = new SolidBrush(Color.FromArgb(alpha, Color.CornflowerBlue)))
                        g.FillRectangle(brush, lift);
                    Require(bitmap.GetPixel(lift.X + lift.Width / 2, lift.Y + lift.Height / 2).A == alpha, "Offscreen frame uses the computed opacity");
                }
                Require(Math.Abs(previousCenterY - CenterY(icon)) <= 1, "The center reaches the icon");
            }
        }

        static double CenterY(Rectangle r) { return r.Top + r.Height * 0.5; }

        static void Require(bool ok, string invariant)
        {
            if (!ok) throw new InvalidOperationException(invariant);
        }
    }
}
