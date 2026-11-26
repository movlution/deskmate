using System;

namespace Deskmate
{
    static class DockWindowMotionCheck
    {
        public static void Run()
        {
            for (int i = 0; i <= 120; i++)
            {
                float t = i / 120f;
                var open = DockWindowMotion.Pose(t, true);
                var close = DockWindowMotion.Pose(t, false);
                CheckCard(open.Back); CheckCard(open.Middle); CheckCard(open.Front);
                CheckCard(close.Back); CheckCard(close.Middle); CheckCard(close.Front);
                if (i == 0 || i == 120)
                {
                    Require(IsResting(open), "The opening starts and ends with the cards out of sight");
                    Require(IsResting(close), "The closing starts and ends with the cards out of sight");
                }
            }

            var dealt = DockWindowMotion.Pose(0.10f, true);
            Require(dealt.Front.Opacity > 0.8f && dealt.Middle.Opacity < 0.1f && dealt.Back.Opacity == 0,
                "The top card deals out before the rest of the deck");
            var fan = DockWindowMotion.Pose(0.55f, true);
            Require(fan.Back.X < -0.5f && fan.Middle.X > 0.5f && fan.Front.Facing == 1 && fan.Front.Y < -1,
                "Opening separates and turns the cards face up");
            var gathering = DockWindowMotion.Pose(0.51f, false);
            Require(gathering.Back.Y > gathering.Middle.Y && gathering.Middle.Y > gathering.Front.Y,
                "Closing squares the cards in order");
            var seated = DockWindowMotion.Pose(0.79f, false);
            Require(seated.Spring > 0.9f && seated.Front.Y > -0.26f && seated.Front.Facing < 0.1f,
                "The last card settles face down as the deck presses into the icon");

            var app = new DockApp { WindowOpening = true, WindowMotionFrom = gathering };
            var reversed = DockWindowMotion.Sample(app, 0);
            Require(Equal(reversed.Back, gathering.Back) && Equal(reversed.Middle, gathering.Middle)
                && Equal(reversed.Front, gathering.Front) && reversed.Spring == gathering.Spring,
                "Reversing carries over every visible card pose");
            Require(IsResting(DockWindowMotion.Sample(app, 1)), "A reversed transition settles to rest");
        }

        static void CheckCard(DockPlayingCard card)
        {
            Require(card.Opacity >= 0 && card.Opacity <= 1 && card.Facing >= 0 && card.Facing <= 1,
                "Card opacity and turnover stay in range");
            Require(Math.Abs(card.X) <= 0.75f && Math.Abs(card.Y) <= 1.30f && Math.Abs(card.Angle) <= 29.01f,
                "The fan stays close to its source icon");
        }

        static bool IsResting(DockWindowPose pose)
        {
            return pose.Back.Opacity == 0 && pose.Middle.Opacity == 0 && pose.Front.Opacity == 0 && pose.Spring == 0;
        }

        static bool Equal(DockPlayingCard a, DockPlayingCard b)
        {
            return a.X == b.X && a.Y == b.Y && a.Angle == b.Angle && a.Scale == b.Scale
                && a.Facing == b.Facing && a.Opacity == b.Opacity;
        }

        static void Require(bool ok, string invariant)
        {
            if (!ok) throw new InvalidOperationException(invariant);
        }
    }
}
