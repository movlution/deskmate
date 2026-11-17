// Compile: build.cmd <temporary-output.exe> tests\notification_emoji_check.cs
// Load assembly without launching it, then call Deskmate.NotificationEmojiCheck.Run().
using System;
using System.Drawing;

namespace Deskmate
{
    public static class NotificationEmojiCheck
    {
        static void Check(bool value, string label)
        {
            if (!value) throw new Exception(label);
        }

        public static string Run()
        {
            string[] emojis = { "\U0001F3A4", "\U0001F50A", "\U0001F469\u200D\U0001F4BB",
                "\U0001F44D\U0001F3FD", "\U0001F1EE\U0001F1E9", "1\uFE0F\u20E3" };
            foreach (string emoji in emojis)
            {
                Check(Painter.NotifEmoji(emoji, 0), "Emoji detection");
                Check(Painter.NotifElementLength(emoji, 0) == emoji.Length, "Whole emoji boundary");
                Check(Fmt.OneLine("\u2068" + emoji + "\u2069", 300) == emoji, "Joiners preserved");
            }
            Check(!Painter.NotifEmoji("Hello", 0), "Ordinary text");
            Check(Fmt.OneLine("\u2068Raven\u2069\n message", 300) == "Raven message", "Direction controls removed");
            Check(Fmt.OneLine("ab\U0001F3A4", 3) == "ab", "Truncation keeps whole emoji");
            Check(Fmt.OneLine("a\U0001F469\u200D\U0001F4BB", 4) == "a\U0001F469", "Truncation drops trailing joiner");
            Check(Fmt.OneLine("\U0001F3A4", 0) == "", "Empty truncation");
            using (var image = new Bitmap(420, 80))
            using (var g = Graphics.FromImage(image))
            using (var p = new Painter())
            using (var font = new Font("Segoe UI", 13, FontStyle.Regular, GraphicsUnit.Pixel))
            {
                p.G = g;
                foreach (var align in new[] { StringAlignment.Near, StringAlignment.Center, StringAlignment.Far })
                foreach (float width in new[] { 4f, 18f, 70f, 300f })
                {
                    g.Clear(Color.Transparent);
                    var transform = g.Transform;
                    var clip = g.ClipBounds;
                    p.Alpha = 0.6f;
                    p.Shadow = true;
                    p.NotifText("Discord " + string.Join(" ", emojis) + " message", font, Color.White, 50, 35, width, align);
                    Check(g.ClipBounds == clip, "Graphics clip restored");
                    Check(g.Transform.Equals(transform), "Graphics transform restored");
                    transform.Dispose();
                }
                g.Clear(Color.Transparent);
                p.NotifText("\U0001F3A4", font, Color.White, 20, 35, 100, StringAlignment.Near);
                bool painted = false;
                for (int y = 0; y < image.Height; y++)
                    for (int x = 0; x < image.Width; x++) painted |= image.GetPixel(x, y).A != 0;
                Check(painted, "Emoji produces visible pixels");
            }
            return "PASS: emoji boundaries, sanitizer, clipping, alignment and offscreen rendering";
        }
    }
}
