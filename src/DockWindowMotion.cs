using System;
using System.Runtime.InteropServices;

namespace Deskmate
{
    struct DockPlayingCard
    {
        public float X, Y, Angle, Scale, Facing, Opacity;

        public static DockPlayingCard Blend(DockPlayingCard from, DockPlayingCard to, float t)
        {
            // The travel of an invisible card does not need to be interrupted.
            if (from.Opacity <= 0.001f) return to;
            return new DockPlayingCard
            {
                X = Ease.Lerp(from.X, to.X, t), Y = Ease.Lerp(from.Y, to.Y, t),
                Angle = Ease.Lerp(from.Angle, to.Angle, t), Scale = Ease.Lerp(from.Scale, to.Scale, t),
                Facing = Ease.Lerp(from.Facing, to.Facing, t), Opacity = Ease.Lerp(from.Opacity, to.Opacity, t)
            };
        }
    }

    struct DockWindowPose
    {
        public DockPlayingCard Back, Middle, Front;
        public float Spring;
    }

    // Windows presents the real window. The Dock's card stack follows the same
    // state change without introducing a second window or delaying activation.
    static class DockWindowMotion
    {
        public const string Setting = "dockbar.cardmotion";
        internal const float OpenSeconds = 0.76f, CloseSeconds = 0.68f;

        [DllImport("user32.dll")] static extern bool SystemParametersInfo(uint action, uint param, out int value, uint flags);

        public static bool Enabled(Settings settings)
        {
            int animations;
            return !Anim.Reduced && settings != null && settings.GetBool(Setting, true)
                && (!SystemParametersInfo(0x1042, 0, out animations, 0) || animations != 0);
        }

        public static float Progress(DockApp app, DateTime now)
        {
            return Anim.Reduced || app.WindowMotionUtc == DateTime.MinValue ? 1f
                : (float)((now - app.WindowMotionUtc).TotalSeconds / Anim.Response(app.WindowOpening ? OpenSeconds : CloseSeconds));
        }

        internal static DockWindowPose Pose(float progress, bool opening)
        {
            float t = Ease.Clamp01(progress);
            if (t == 0 || t == 1) return new DockWindowPose();
            float spring = opening
                ? 0.35f * Pulse(t, 0, 0.10f, 0.24f) - 0.72f * Pulse(t, 0.14f, 0.30f, 0.50f)
                : Pulse(t, 0.66f, 0.79f, 0.90f) - 0.25f * Pulse(t, 0.85f, 0.93f, 1);
            return new DockWindowPose
            {
                Back = Card(t, opening, 0),
                Middle = Card(t, opening, 1),
                Front = Card(t, opening, 2),
                Spring = spring
            };
        }

        static DockPlayingCard Card(float t, bool opening, int depth)
        {
            float lift, fan, facing, opacity;
            int side = depth == 0 ? -1 : depth == 1 ? 1 : 0;
            if (opening)
            {
                // Deal the top card first, then the middle and the back.
                float deal = (2 - depth) * 0.081f;
                float gather = Ease.InOutCubic((t - 0.61f - depth * 0.035f) / 0.27f);
                lift = Ease.OutCubic((t - deal) / 0.32f) * (1 - gather);
                fan = Ease.InOutCubic((t - deal - 0.055f) / 0.26f) * (1 - gather);
                facing = Ease.InOutCubic((t - deal - 0.18f) / 0.16f);
                opacity = Ease.InOutCubic((t - deal) / 0.11f) * (1 - Ease.InOutCubic((t - 0.74f) / 0.26f));
            }
            else
            {
                // Square the fan one card at a time. The top card taps the deck last.
                float gather = Ease.InOutCubic((t - 0.21f - depth * 0.095f) / 0.39f);
                lift = 1 - gather;
                fan = 1 - gather;
                facing = 1 - Ease.InOutCubic((gather - 0.08f) / 0.75f);
                opacity = Ease.InOutCubic((t - (2 - depth) * 0.026f) / 0.16f)
                    * (1 - Ease.InOutCubic((t - 0.69f - depth * 0.066f) / 0.17f));
            }
            return new DockPlayingCard
            {
                X = side * 0.66f * fan + 0.08f * lift,
                Y = -lift * (1.12f + (side == 0 ? 0.09f : -0.06f)) - depth * 0.036f,
                Angle = side * 25f * fan + 4f * lift,
                Scale = 0.62f + 0.38f * lift,
                Facing = facing,
                Opacity = opacity
            };
        }

        static float Pulse(float t, float start, float peak, float end)
        {
            return Ease.InOutCubic((t - start) / (peak - start)) * (1 - Ease.InOutCubic((t - peak) / (end - peak)));
        }

        public static DockWindowPose Sample(DockApp app, float progress)
        {
            var pose = Pose(progress, app.WindowOpening);
            var from = app.WindowMotionFrom;
            if (progress >= 0.20f) return pose;
            float blend = Ease.InOutCubic(progress / 0.20f);
            return new DockWindowPose
            {
                Back = DockPlayingCard.Blend(from.Back, pose.Back, blend),
                Middle = DockPlayingCard.Blend(from.Middle, pose.Middle, blend),
                Front = DockPlayingCard.Blend(from.Front, pose.Front, blend),
                Spring = from.Spring == 0 ? pose.Spring : Ease.Lerp(from.Spring, pose.Spring, blend)
            };
        }
    }
}
