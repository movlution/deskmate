using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace Deskmate
{
    partial class Island
    {
        void SpawnComboShards(Color c)
        {
            if (Anim.Reduced) return;
            comboShards.Clear();
            for (int i = 0; i < 16; i++)
            {
                double ang = (i / 16.0) * Math.PI * 2.0 + comboRng.NextDouble() * 0.35;
                float spd = 55f + (float)comboRng.NextDouble() * 95f;
                comboShards.Add(new ComboShard
                {
                    X = 48f + ((float)comboRng.NextDouble() - 0.5f) * 32f,
                    Y = 24f + ((float)comboRng.NextDouble() - 0.5f) * 14f,
                    Vx = (float)Math.Cos(ang) * spd,
                    Vy = (float)Math.Sin(ang) * spd - 35f,
                    W = 4f + (float)comboRng.NextDouble() * 4f,
                    H = 3f + (float)comboRng.NextDouble() * 3f,
                    Angle = (float)(comboRng.NextDouble() * Math.PI),
                    Spin = ((float)comboRng.NextDouble() - 0.5f) * 14f,
                    Life = 0f,
                    MaxLife = 0.7f + (float)comboRng.NextDouble() * 0.35f,
                    Color = (i % 3 == 0) ? Ink.FxCoral : c
                });
            }
        }

        void SpawnComboSparks(int count, bool burst, Color baseCol)
        {
            if (Anim.Reduced) return;
            bool side = OnSide;
            float w = side ? SideW : ComboWidth(), h = side ? 84f : ComboHeight();
            for (int i = 0; i < count; i++)
            {
                if (comboSparks.Count >= 64) comboSparks.RemoveAt(0);
                double ang = comboRng.NextDouble() * Math.PI * 2.0;
                float spd = (burst ? 58f : 28f) + (float)comboRng.NextDouble() * (burst ? 95f : 52f);
                float ox = side
                    ? (w * 0.5f + ((float)comboRng.NextDouble() - 0.5f) * 18f)
                    : (burst ? (w * 0.35f + (float)comboRng.NextDouble() * (w * 0.3f)) : (54f + (float)comboRng.NextDouble() * 44f));
                float oy = h * 0.5f + ((float)comboRng.NextDouble() - 0.5f) * (side ? 28f : 16f);
                Color c = (i % 3 == 0) ? Ink.FxCream : baseCol;
                comboSparks.Add(new ComboSpark
                {
                    X = ox,
                    Y = oy,
                    Vx = (float)Math.Cos(ang) * spd,
                    Vy = (float)Math.Sin(ang) * spd - (burst ? 18f : 8f),
                    Size = (burst ? 3.2f : 2.2f) + (float)comboRng.NextDouble() * 2.4f,
                    Life = 0f,
                    MaxLife = (burst ? 0.55f : 0.38f) + (float)comboRng.NextDouble() * 0.25f,
                    Spin = ((float)comboRng.NextDouble() - 0.5f) * 10f,
                    Angle = (float)(comboRng.NextDouble() * Math.PI),
                    Color = c,
                    Kind = i % 3
                });
            }
        }

        // Softened celebration palette from Ink.FxConfetti: joyful, never neon
        static readonly Color[] ComboConfettiPalette = Ink.FxConfetti;

        static readonly Color[] ComboConfettiGoldPalette =
        {
            Ink.FxGold, Ink.FxCream,
            Ink.Mix(Ink.FxGold, Color.White, 0.28f),
            Ink.FxApricot,
            Ink.Mix(Ink.FxGold, Color.White, 0.45f)
        };

        void SpawnComboConfetti(int count, float ox, float oy, float spreadX, bool goldTheme, bool tierBoost)
        {
            if (Anim.Reduced || count <= 0) return;
            Color[] pal = goldTheme ? ComboConfettiGoldPalette : ComboConfettiPalette;
            float boost = tierBoost ? 1.15f : 1.0f;
            bool side = OnSide;
            float effSpreadX = side ? Math.Min(spreadX, SideW * 1.35f) : spreadX;
            for (int i = 0; i < count; i++)
            {
                if (comboConfetti.Count >= 96) comboConfetti.RemoveAt(0);
                bool round = (i % 5 == 0);
                float w = round ? (3.2f + (float)comboRng.NextDouble() * 2.0f) : (4.2f + (float)comboRng.NextDouble() * 3.4f);
                float h = round ? w : (2.4f + (float)comboRng.NextDouble() * 2.0f);
                float scatterY = ((float)comboRng.NextDouble() - 0.5f) * 20f;
                comboConfetti.Add(new ComboConfettiPiece
                {
                    X = ox + ((float)comboRng.NextDouble() - 0.5f) * (side ? 12f : 24f),
                    Y = side ? Math.Max(8f, Math.Min(76f, oy + scatterY)) : Math.Max(6f, Math.Min(38f, oy + scatterY)),
                    Vx = ((float)comboRng.NextDouble() - 0.5f) * effSpreadX * boost,
                    Vy = (side ? (-35f - (float)comboRng.NextDouble() * 75f) : (-20f - (float)comboRng.NextDouble() * 55f)) * boost,
                    W = w,
                    H = h,
                    Angle = (float)(comboRng.NextDouble() * Math.PI * 2.0),
                    Spin = ((float)comboRng.NextDouble() - 0.5f) * 14f,
                    Flutter = (float)(comboRng.NextDouble() * Math.PI * 2.0),
                    FlutterSpd = 9f + (float)comboRng.NextDouble() * 7f,
                    Life = 0f,
                    MaxLife = 0.85f + (float)comboRng.NextDouble() * 0.45f,
                    Color = pal[comboRng.Next(pal.Length)],
                    Round = round
                });
            }
        }

        void DrawComboConfetti()
        {
            if (Anim.Reduced || comboConfetti.Count == 0) return;
            for (int i = 0; i < comboConfetti.Count; i++)
            {
                var cf = comboConfetti[i];
                float t = Ease.Clamp01(cf.Life / Math.Max(0.01f, cf.MaxLife));
                float a = (1f - t * t) * 0.92f * p.Alpha;
                if (a <= 0.02f) continue;
                float flipScale = 0.25f + 0.75f * Math.Abs((float)Math.Cos(cf.Flutter));
                var st = g.Save();
                g.TranslateTransform(cf.X, cf.Y);
                g.RotateTransform(cf.Angle * 57.29578f);
                using (var b = new SolidBrush(Ink.A(cf.Color, a)))
                {
                    if (cf.Round)
                        g.FillEllipse(b, -cf.W * 0.5f, -cf.H * 0.5f, cf.W, cf.H);
                    else
                    {
                        float rh = cf.H * flipScale;
                        g.FillRectangle(b, -cf.W * 0.5f, -rh * 0.5f, cf.W, rh);
                    }
                }
                g.Restore(st);
            }
        }

        void SpawnComboCallout(string text, Color c)
        {
            if (Anim.Reduced || string.IsNullOrEmpty(text)) return;
            if (comboCallouts.Count >= 4) comboCallouts.RemoveAt(0);
            float w = ComboWidth();
            float chosenY = 39f;
            float bestMinDist = -1f;
            for (int slot = 0; slot < 4; slot++)
            {
                float candY = 9f + slot * 10f;
                float minDist = 1e6f;
                bool clash = false;
                for (int j = 0; j < comboCallouts.Count; j++)
                {
                    float d = Math.Abs(comboCallouts[j].Y - candY);
                    if (d < minDist) minDist = d;
                    if (d < 8.5f) clash = true;
                }
                if (!clash)
                {
                    chosenY = candY;
                    break;
                }
                if (minDist > bestMinDist)
                {
                    bestMinDist = minDist;
                    chosenY = candY;
                }
            }
            comboCallouts.Add(new ComboCallout
            {
                Text = text,
                X = w - 108f + ((float)comboRng.NextDouble() - 0.5f) * 16f,
                Y = chosenY,
                Vy = -22f,
                Life = 0f,
                MaxLife = 0.65f,
                Color = c
            });
        }

        public static int ComboTierFor(int hits)
        {
            if (hits >= 200) return 6;
            if (hits >= 100) return 5;
            if (hits >= 50) return 4;
            if (hits >= 25) return 3;
            if (hits >= 10) return 2;
            if (hits >= ComboMinHits) return 1;
            return 0;
        }

        public static string ComboRankLabel(int tier)
        {
            switch (tier)
            {
                case 6: return "GODLIKE!!!";
                case 5: return "ULTRA!!";
                case 4: return "AWESOME!";
                case 3: return "GREAT!!";
                case 2: return "NICE!";
                default: return "COMBO";
            }
        }

        public static string ComboRankSubtitle(int tier)
        {
            switch (tier)
            {
                case 6: return Lang.T("Dewa Ketik!");
                case 5: return Lang.T("Tak terhentikan!");
                case 4: return Lang.T("Kecepatan kilat!");
                case 3: return Lang.T("Jari menari!");
                case 2: return Lang.T("Ketikan cepat!");
                default: return Lang.T("Mulai panas!");
            }
        }

        public static Color ComboTierColor(int tier, double animSec)
        {
            switch (tier)
            {
                case 6: return ComboRainbow(animSec * 1.8);
                case 5: return Ink.FxRose;
                case 4: return Ink.FxApricot;
                case 3: return Ink.FxGold;
                case 2: return Ink.FxMint;
                default: return Ink.FxSky;
            }
        }

        // A calm rainbow: a smooth walk around Ink.FxConfetti, no pure-saturation neon
        static Color ComboRainbow(double t)
        {
            Color[] pal = Ink.FxConfetti;
            double p = (t - Math.Floor(t)) * (pal.Length - 1);
            int i = (int)p;
            return Ink.Mix(pal[i], pal[(i + 1) % pal.Length], (float)(p - i));
        }

        float ComboDrainFraction()
        {
            if (!comboActive) return 0f;
            int tier = ComboTierFor(comboCount);
            float hold = tier >= 5 ? 1.85f : tier >= 3 ? 2.0f : ComboHoldSec;
            return Ease.Clamp01(comboTimeLeft / hold);
        }

        void StepCombo(float dt)
        {
            if (comboPaused) return;
            comboPop.Step(dt);
            comboRing.Step(dt);
            comboShake.Step(dt);

            if (comboRankFlash > 0f)
                comboRankFlash = Math.Max(0f, comboRankFlash - dt * 2.4f);
            if (comboGoldFlash > 0f)
                comboGoldFlash = Math.Max(0f, comboGoldFlash - dt * 2.2f);
            if (comboShieldCrack > 0f)
                comboShieldCrack = Math.Max(0f, comboShieldCrack - dt * 2.8f);
            if (comboEnterSmash > 0f)
                comboEnterSmash = Math.Max(0f, comboEnterSmash - dt * 3.0f);
            if (comboRing2 > 0f)
            {
                comboRing2 += dt * 1.9f;
                if (comboRing2 >= 1f) comboRing2 = 0f;
            }
            if (comboBrokenLeft > 0f)
            {
                comboBrokenLeft = Math.Max(0f, comboBrokenLeft - dt);
                if (comboBrokenLeft <= 0.01f)
                {
                    comboBrokenLeft = 0f;
                    comboWpm = 0;
                    comboPeakWpm = 0;
                }
            }

            for (int i = comboShards.Count - 1; i >= 0; i--)
            {
                var sh = comboShards[i];
                sh.Life += dt;
                if (sh.Life >= sh.MaxLife) { comboShards.RemoveAt(i); continue; }
                sh.X += sh.Vx * dt;
                sh.Y += sh.Vy * dt;
                sh.Vy += 190f * dt;
                sh.Angle += sh.Spin * dt;
            }
            for (int i = comboSparks.Count - 1; i >= 0; i--)
            {
                var s = comboSparks[i];
                s.Life += dt;
                if (s.Life >= s.MaxLife)
                {
                    comboSparks.RemoveAt(i);
                    continue;
                }
                s.X += s.Vx * dt;
                s.Y += s.Vy * dt;
                s.Vy += 110f * dt; // subtle gravity
                s.Vx *= (float)Math.Pow(0.25, dt);
                s.Angle += s.Spin * dt;
            }
            for (int i = comboConfetti.Count - 1; i >= 0; i--)
            {
                var cf = comboConfetti[i];
                cf.Life += dt;
                if (cf.Life >= cf.MaxLife)
                {
                    comboConfetti.RemoveAt(i);
                    continue;
                }
                cf.X += cf.Vx * dt;
                cf.Y += cf.Vy * dt;
                cf.Vx *= (float)Math.Pow(0.28, dt);
                cf.Vy += 58f * dt;
                cf.Vy *= (float)Math.Pow(0.45, dt);
                cf.Angle += cf.Spin * dt;
                cf.Flutter += cf.FlutterSpd * dt;
            }

            for (int i = comboCallouts.Count - 1; i >= 0; i--)
            {
                var c = comboCallouts[i];
                c.Life += dt;
                if (c.Life >= c.MaxLife)
                {
                    comboCallouts.RemoveAt(i);
                    continue;
                }
                c.Y += c.Vy * dt;
            }

            if (comboActive)
            {
                comboTimeLeft -= dt;
                if (comboTimeLeft <= 0f)
                {
                    comboTimeLeft = 0f;
                    comboActive = false;
                    comboLastRun = comboCount;
                    comboLastRunXp = comboRunXp;
                    // Show a brief finisher summary if the user reached at least Tier 2 (10+ hits)
                    if (comboCount >= 10)
                    {
                        comboFinishLeft = ComboFinishHoldSec;
                        comboPop.Snap(0.65f);
                        comboPop.Target = 0f;
                    }
                    comboCount = 0;
                    comboLastTier = 0;
                    comboKeyTimes.Clear();
                    Array.Clear(comboKeyDown, 0, comboKeyDown.Length);
                    FlushComboStats();
                    Kick();
                }
            }
            else if (comboFinishLeft > 0f)
            {
                comboFinishLeft = Math.Max(0f, comboFinishLeft - dt);
                if (comboFinishLeft <= 0.01f)
                {
                    comboFinishLeft = 0f;
                    comboWpm = 0;
                    comboPeakWpm = 0;
                }
            }

            if (sprintActive)
            {
                sprintLeft -= dt;
                if (sprintLeft <= 0f)
                {
                    sprintLeft = 0f;
                    sprintActive = false;
                    sprintDoneLeft = 3.2f;
                    sprintFinalWpm = (int)Math.Round((sprintChars / 5.0) / 0.5);
                    bool newRec = sprintFinalWpm > comboSprintBestWpm && sprintFinalWpm > 0;
                    if (newRec)
                    {
                        comboSprintBestWpm = sprintFinalWpm;
                        comboStatsDirty = true;
                        PlayComboPbFanfare();
                    }
                    if (newRec || sprintFinalWpm >= 40)
                    {
                        SpawnComboConfetti(30, OnSide ? SideW * 0.5f : 176f, OnSide ? 42f : 24f, OnSide ? 54f : 320f, newRec, true);
                        if (!Anim.Reduced) celebrateUtc = DateTime.UtcNow;
                    }
                    AddComboXp(Math.Max(20, sprintFinalWpm));
                    if (sprintFinalWpm >= 80) UnlockComboBadge("sprint_pro");
                    SyncComboHook();
                    FlushComboStats();
                    Kick();
                }
            }
            else if (sprintDoneLeft > 0f)
            {
                sprintDoneLeft = Math.Max(0f, sprintDoneLeft - dt);
            }
        }

        bool ComboAnimating()
        {
            if (comboPaused) return false;
            return comboActive || comboFinishLeft > 0.01f || comboBrokenLeft > 0.01f || comboSparks.Count > 0 || comboConfetti.Count > 0 || comboShards.Count > 0 || comboCallouts.Count > 0 || comboGoldFlash > 0.01f || comboShieldCrack > 0.01f || comboEnterSmash > 0.01f || sprintActive || sprintDoneLeft > 0.01f || !comboPop.Settled;
        }

        float ComboWidth()
        {
            if (sprintActive || sprintDoneLeft > 0.01f) return 352f;
            int shown = comboActive ? comboCount : comboLastRun;
            int tier = ComboTierFor(shown);
            bool broken = comboBrokenLeft > 0.01f;
            string rank = comboActive ? ComboRankLabel(tier) : (broken ? "BROKEN!" : "MAX COMBO");
            string sub = comboActive
                ? ComboRankSubtitle(tier) + " · " + comboWpm + " WPM"
                : (broken
                    ? Lang.T("COMBO BROKEN!")
                    : (ComboRpgOn ? Lang.F("Lv.{0} · {1} (+{2} XP)", ComboLevel, ComboLevelTitle, comboLastRunXp) : Lang.F("Selesai! · Puncak {0} WPM", Math.Max(comboWpm, comboPeakWpm))));
            float rankW = (p != null && p.G != null ? p.Measure(rank, Ink.Semi(10.5f)) : rank.Length * 7.2f) + 20f;
            float subW = p != null && p.G != null ? p.Measure(sub, Ink.Small(11f)) : sub.Length * 6.2f;
            float midW = Math.Max(rankW + 46f, subW + 8f);
            float numW = shown >= 1000 ? 96f : shown >= 100 ? 86f : 74f;
            return Math.Max(326f, Math.Min(416f, numW + midW + 78f));
        }

        float ComboHeight()
        {
            return 48f;
        }

        string DescribeCombo()
        {
            int shown = comboActive ? comboCount : comboLastRun;
            int tier = ComboTierFor(shown);
            return ComboRankLabel(tier) + " " + shown + "x, " + comboWpm + " WPM";
        }

        void DrawCombo(float w, float h, DateTime now)
        {
            if (sprintActive || sprintDoneLeft > 0.01f)
            {
                DrawTypingSprint(w, h);
                return;
            }
            int shown = comboActive ? comboCount : comboLastRun;
            int tier = ComboTierFor(shown);
            Color tc = comboBrokenLeft > 0.01f ? p.T.Negative : ComboTierColor(tier, clock.Elapsed.TotalSeconds);
            Color tcInk = p.T.IsDark ? tc : EnforceContrast(tc, p.T.Bg, 3.5);
            float pop = Math.Max(0f, comboPop.Value);
            float shakeX = Anim.Reduced ? 0f : comboShake.Value;
            float cy = h / 2f - 1f;

            // 1. Ambient arcade glow on left & right wings
            float glowAlpha = Math.Min(0.42f, 0.14f + tier * 0.04f + comboRankFlash * 0.22f) * p.Alpha;
            float gw = Math.Max(4f, w - 4f), gh = Math.Max(4f, h - 4f);
            var glowRect = new RectangleF(2, 2, gw, gh);
            using (var lg = new LinearGradientBrush(
                new PointF(0, 0), new PointF(Math.Max(8f, w), 0),
                Ink.A(tc, glowAlpha),
                Ink.A(tc, glowAlpha * 0.35f)))
            {
                p.FillRound(glowRect, gh / 2f, lg);
            }

            // 2. Expanding shockwave ring on rank-up / milestone
            if (!Anim.Reduced && comboRing.Value > 0.01f && comboRing.Value < 0.99f)
            {
                float rt = Ease.Clamp01(comboRing.Value);
                float rw = Math.Max(12f, 48f + rt * (w - 56f));
                float rh = Math.Max(8f, 24f + rt * (h - 26f));
                var rr = new RectangleF(48f - rw * 0.25f, cy - rh / 2f, rw, rh);
                p.StrokeRound(rr, rh / 2f, Ink.A(tc, (1f - rt) * 0.55f), 1.8f);
            }

            // 3. Particle sparks (circles, 4-point stars, diamonds)
            if (!Anim.Reduced && comboRing2 > 0.01f && comboRing2 < 0.99f)
            {
                float rt2 = Ease.Clamp01(comboRing2);
                float rw2 = Math.Max(16f, 56f + rt2 * (w - 40f));
                float rh2 = Math.Max(10f, 26f + rt2 * (h - 18f));
                var rr2 = new RectangleF(48f - rw2 * 0.25f, cy - rh2 / 2f, rw2, rh2);
                p.StrokeRound(rr2, rh2 / 2f, Ink.A(Ink.FxGold, (1f - rt2) * 0.65f), 2.2f);
            }
            if (!Anim.Reduced && comboShards.Count > 0)
            {
                for (int i = 0; i < comboShards.Count; i++)
                {
                    var sh = comboShards[i];
                    float st = Ease.Clamp01(sh.Life / Math.Max(0.01f, sh.MaxLife));
                    float sa = (1f - st * st) * 0.9f;
                    if (sa <= 0.02f) continue;
                    p.FillRound(new RectangleF(sh.X - sh.W * 0.5f, sh.Y - sh.H * 0.5f, sh.W, sh.H), 1f, Ink.A(sh.Color, sa));
                }
            }
            DrawComboConfetti();
            if (!Anim.Reduced && comboSparks.Count > 0)
            {
                for (int i = 0; i < comboSparks.Count; i++)
                {
                    var s = comboSparks[i];
                    float t = Ease.Clamp01(s.Life / Math.Max(0.01f, s.MaxLife));
                    float a = (1f - t * t) * 0.9f;
                    float r = s.Size * (1f - t * 0.4f);
                    if (r <= 0.3f || a <= 0.02f) continue;
                    Color sc = Ink.A(s.Color, a);
                    if (s.Kind == 1)
                        DrawComboStar(s.X, s.Y, r * 1.45f, s.Angle, sc);
                    else if (s.Kind == 2)
                        DrawComboDiamond(s.X, s.Y, r * 1.25f, sc);
                    else
                        p.Circle(s.X, s.Y, r, sc);
                }
            }

            // 4. Left Section: Combo Hit Counter ("42x")
            float leftW = shown >= 1000 ? 94f : shown >= 100 ? 84f : 74f;
            float numCx = 14f + leftW * 0.5f + shakeX;
            float fontPt = Math.Min(21f, (shown >= 100 ? 15.5f : 17f) + pop * 3.2f);
            string numText = shown + "x";

            // Subtle pill capsule behind the combo counter
            float pillH = Math.Max(8f, h - 16f);
            var numPill = new RectangleF(10f, Math.Max(2f, (h - pillH) * 0.5f - 1f), leftW, pillH);
            p.FillRound(numPill, pillH / 2f, Ink.A(tc, 0.16f + Math.Min(0.18f, pop * 0.15f)));
            p.StrokeRound(numPill, pillH / 2f, Ink.A(tc, 0.38f + Math.Min(0.35f, pop * 0.3f)), 1f);

            // Glow shadow + crisp number
            if (tier >= 2)
                p.TextCenter(numText, Ink.Semi(fontPt), Ink.A(tcInk, 0.35f), numCx + 0.8f, cy + 0.8f);
            Color numColor = tier >= 3 ? Ink.Mix(p.T.Text, tcInk, 0.35f) : p.T.Text;
            p.TextCenter(numText, Ink.Semi(fontPt), numColor, numCx, cy - 0.5f);
            if (ComboFeverActive)
            {
                double sec = clock.Elapsed.TotalSeconds;
                for (int f = 0; f < 3; f++)
                {
                    float pulse = Anim.Reduced ? 0.5f : (0.5f + 0.5f * (float)Math.Sin(sec * 9.0 + f * 2.1));
                    float fw = 4f + pulse * 4f;
                    float fh = 8f + pulse * 7f;
                    float fy = numPill.Y + 4f + f * 7f - fh * 0.3f;
                    Color fc = Ink.A(tc, 0.35f + pulse * 0.35f);
                    p.FillRound(new RectangleF(numPill.X - fw * 0.6f, fy, fw, fh), fw * 0.5f, fc);
                    p.FillRound(new RectangleF(numPill.Right - fw * 0.4f, fy, fw, fh), fw * 0.5f, fc);
                }
            }
            if (comboGoldFlash > 0.01f)
                p.FillRound(numPill, pillH / 2f, Ink.A(Ink.FxGold, comboGoldFlash * 0.35f));
            if (comboShieldCrack > 0.01f)
                p.FillRound(numPill, pillH / 2f, Ink.A(p.T.Negative, comboShieldCrack * 0.35f));
            if (ComboPenaltyMode == "shield" && comboActive)
            {
                for (int s = 0; s < 2; s++)
                {
                    Color sc = s < comboShields ? Ink.FxSky : Ink.A(p.T.Text, 0.16f);
                    p.Circle(numPill.Right - 6f - s * 7f, numPill.Y + 5f, 2.2f, sc);
                }
            }

            // 5. Center Section: Rank Badge Pill + Subtitle & WPM
            float midX = 16f + leftW;
            float rightReserve = 66f;
            float availMid = Math.Max(80f, w - midX - rightReserve);

            string rankText = comboActive ? ComboRankLabel(tier) : (comboBrokenLeft > 0.01f ? "BROKEN!" : "MAX COMBO");
            var rankFont = Ink.Semi(9.5f);
            float badgeW = p.Measure(rankText, rankFont) + 14f;
            var badgeRect = new RectangleF(midX, 7.5f, badgeW, 16.5f);
            p.FillRound(badgeRect, 8.2f, tc);
            Color onBadge = Ink.On(tc);
            p.TextCenter(rankText, rankFont, onBadge, badgeRect.X + badgeRect.Width / 2f, badgeRect.Y + badgeRect.Height / 2f);

            // Secondary flame/streak tag next to the rank badge
            if (ComboFeverActive)
            {
                string multTag = tier >= 6 ? "🔥 ×4" : "🔥 ×2";
                p.Text(multTag, Ink.Semi(9f), tcInk, badgeRect.Right + 6f, badgeRect.Y + badgeRect.Height / 2f, Math.Max(20f, availMid - badgeW - 6f), StringAlignment.Near);
            }
            else if (comboActive && comboBest > shown && comboBest >= 10)
            {
                string bestTag = Lang.F("TERBAIK {0}x", comboBest);
                p.Text(bestTag, Ink.Semi(8.5f), p.T.Sub, badgeRect.Right + 6f, badgeRect.Y + badgeRect.Height / 2f, Math.Max(20f, availMid - badgeW - 6f), StringAlignment.Near);
            }
            else if (comboActive && tier >= 3)
            {
                p.Text("🔥 STREAK", Ink.Semi(9f), tcInk, badgeRect.Right + 6f, badgeRect.Y + badgeRect.Height / 2f, Math.Max(20f, availMid - badgeW - 6f), StringAlignment.Near);
            }

            string subText = comboBrokenLeft > 0.01f
                ? Lang.T("COMBO BROKEN!")
                : comboActive
                    ? ComboRankSubtitle(tier) + " · " + comboWpm + " WPM"
                    : (ComboRpgOn ? Lang.F("Lv.{0} · {1} (+{2} XP)", ComboLevel, ComboLevelTitle, comboLastRunXp) : Lang.F("Selesai! · Puncak {0} WPM", Math.Max(comboWpm, comboPeakWpm)));
            p.Text(subText, Ink.Small(11f), comboBrokenLeft > 0.01f ? p.T.Negative : p.T.Sub, midX, h - 14.5f, availMid, StringAlignment.Near);

            // 6. Floating Callouts ("NICE!", "FAST!", "CRIT!")
            if (!Anim.Reduced && comboCallouts.Count > 0)
            {
                for (int i = 0; i < comboCallouts.Count; i++)
                {
                    var co = comboCallouts[i];
                    float ct = Ease.Clamp01(co.Life / Math.Max(0.01f, co.MaxLife));
                    float ca = 1f - ct * ct;
                    Color coInk = p.T.IsDark ? co.Color : EnforceContrast(co.Color, p.T.Bg, 3.5);
                    p.TextCenter(co.Text, Ink.Semi(9f), Ink.A(coInk, ca), co.X, co.Y);
                }
            }

            // 7. Right Section: Tactile Keycap Hit Indicator
            float capW = 44f;
            float capH = 26f;
            float capPop = pop * 2.2f + comboEnterSmash * 4.5f;
            var capRect = new RectangleF(w - capW - 12f - capPop * 0.5f, cy - capH / 2f - capPop * 0.5f, capW + capPop, capH + capPop);
            p.FillRound(capRect, 8f, Ink.A(tc, comboActive ? 0.22f : 0.12f));
            p.StrokeRound(capRect, 8f, Ink.A(tc, comboActive ? 0.65f : 0.3f), 1.2f);
            string keyText = comboActive && comboLastKey.Length > 0 ? comboLastKey : "⚡";
            p.TextCenter(keyText, Ink.Semi(10.5f), p.T.Text, capRect.X + capRect.Width / 2f, capRect.Y + capRect.Height / 2f);

            // 8. Bottom Combo Timer Drain Bar
            float frac = comboActive ? ComboDrainFraction() : (comboFinishLeft > 0.01f ? (comboFinishLeft / ComboFinishHoldSec) : 0f);
            if (!comboActive && comboFinishLeft > 0.01f && ComboRpgOn)
            {
                float xpBarX = 16f, xpBarW = Math.Max(8f, w - 32f), xpBarY = h - 4.2f;
                p.FillRound(new RectangleF(xpBarX, xpBarY, xpBarW, 2.4f), 1.2f, Ink.A(p.T.Text, 0.12f));
                float xpFill = Math.Max(4f, xpBarW * Ease.Clamp01(ComboLevelProgress));
                p.FillRound(new RectangleF(xpBarX, xpBarY, xpFill, 2.4f), 1.2f, Ink.FxGold);
            }
            else if (frac > 0.005f)
            {
                float barX = 16f, barW = Math.Max(8f, w - 32f), barY = h - 4.2f;
                p.FillRound(new RectangleF(barX, barY, barW, 2.4f), 1.2f, Ink.A(p.T.Text, 0.12f));
                float fillW = Math.Max(4f, barW * Ease.Clamp01(frac));
                p.FillRound(new RectangleF(barX, barY, fillW, 2.4f), 1.2f, tc);
                if (comboActive && !Anim.Reduced)
                    p.Circle(barX + fillW, barY + 1.2f, 2.6f, p.T.Text);
            }
        }

        void DrawTypingSprint(float w, float h)
        {
            Color sc = Ink.FxSky;
            Color scInk = p.T.IsDark ? sc : EnforceContrast(sc, p.T.Bg, 3.5);
            float cy = h / 2f - 1f;
            if (sprintActive)
            {
                double elapsed = Math.Max(1.0, 30.0 - sprintLeft);
                int liveWpm = (int)Math.Round((sprintChars / 5.0) / (elapsed / 60.0));
                var pill = new RectangleF(10f, 8f, 74f, h - 18f);
                p.FillRound(pill, pill.Height / 2f, Ink.A(sc, 0.18f));
                p.StrokeRound(pill, pill.Height / 2f, Ink.A(sc, 0.5f), 1f);
                p.TextCenter(sprintLeft.ToString("0.0") + "s", Ink.Semi(15f), scInk, pill.X + pill.Width / 2f, cy - 0.5f);
                p.Text(Lang.T("SPRINT 30s"), Ink.Semi(9.5f), scInk, 94f, 14f, 120f, StringAlignment.Near);
                p.Text(liveWpm + " WPM · " + sprintChars + " " + Lang.T("huruf"), Ink.Small(11f), p.T.Sub, 94f, h - 15f, 165f, StringAlignment.Near);
                var stopRect = new RectangleF(w - 58f, cy - 12f, 46f, 24f);
                p.FillRound(stopRect, 12f, Ink.A(p.T.Negative, 0.18f));
                p.StrokeRound(stopRect, 12f, Ink.A(p.T.Negative, 0.55f), 1f);
                p.TextCenter(Lang.T("Stop"), Ink.Semi(9.5f), p.T.Text, stopRect.X + stopRect.Width / 2f, stopRect.Y + stopRect.Height / 2f);
                AddHit(stopRect, "comboSprintStop", StopTypingSprint);
                float frac = Ease.Clamp01(sprintLeft / 30f);
                p.FillRound(new RectangleF(16f, h - 4.2f, Math.Max(8f, w - 32f), 2.4f), 1.2f, Ink.A(p.T.Text, 0.12f));
                p.FillRound(new RectangleF(16f, h - 4.2f, Math.Max(4f, (w - 32f) * frac), 2.4f), 1.2f, sc);
            }
            else
            {
                p.TextCenter(sprintFinalWpm + " WPM", Ink.Semi(16f), scInk, 60f, cy);
                p.Text(Lang.F("Sprint Selesai! · Rekor {0} WPM", comboSprintBestWpm), Ink.Small(11f), p.T.Text, 114f, cy, w - 126f, StringAlignment.Near);
            }
                DrawComboConfetti();
        }

        // Upright rendering when the island sits on the left or right screen edge
        void SideCombo(float w, float h, DateTime now)
        {
            if (sprintActive || sprintDoneLeft > 0.01f)
            {
                float scx = w / 2f;
                p.Circle(scx, 18f, 11f, Ink.A(Ink.FxSky, 0.24f));
                p.Icon('\uE916', 11f, Ink.FxSky, scx, 18f);
                p.TextCenter(sprintActive ? sprintLeft.ToString("0.0") + "s" : (sprintFinalWpm + ""), Ink.Semi(11f), p.T.Text, scx, 42f);
                p.TextCenter(sprintActive ? "SPRINT" : "WPM", Ink.Semi(7.5f), p.T.Sub, scx, 58f);
                if (sprintActive)
                {
                    AddHit(new RectangleF(0, 0, w, h), "comboSprintStop", StopTypingSprint);
                    float barW = Math.Max(6f, w - 12f);
                    p.FillRound(new RectangleF(6f, h - 8f, barW, 3f), 1.5f, Ink.A(p.T.Text, 0.14f));
                    p.FillRound(new RectangleF(6f, h - 8f, Math.Max(3f, barW * Ease.Clamp01(sprintLeft / 30f)), 3f), 1.5f, Ink.FxSky);
                }
                DrawComboConfetti();
                return;
            }
            int shown = comboActive ? comboCount : comboLastRun;
            int tier = ComboTierFor(shown);
            Color tc = comboBrokenLeft > 0.01f ? p.T.Negative : ComboTierColor(tier, clock.Elapsed.TotalSeconds);
            Color tcInk = p.T.IsDark ? tc : EnforceContrast(tc, p.T.Bg, 3.5);
            if (ComboFeverActive)
            {
                float pulse = Anim.Reduced ? 0.4f : (0.35f + 0.35f * (float)Math.Sin(clock.Elapsed.TotalSeconds * 8.0));
                p.StrokeRound(new RectangleF(2f, 2f, w - 4f, h - 4f), 10f, Ink.A(tc, pulse), 2.0f);
            }
            float pop = Math.Max(0f, comboPop.Value);
            float cx = w / 2f;

            if (!Anim.Reduced && comboShards.Count > 0)
            {
                for (int i = 0; i < comboShards.Count; i++)
                {
                    var sh = comboShards[i];
                    float st = Ease.Clamp01(sh.Life / Math.Max(0.01f, sh.MaxLife));
                    float sa = (1f - st * st) * 0.9f;
                    if (sa <= 0.02f) continue;
                    p.FillRound(new RectangleF(sh.X - sh.W * 0.5f, sh.Y - sh.H * 0.5f, sh.W, sh.H), 1f, Ink.A(sh.Color, sa));
                }
            }
            DrawComboConfetti();
            if (!Anim.Reduced && comboSparks.Count > 0)
            {
                for (int i = 0; i < comboSparks.Count; i++)
                {
                    var s = comboSparks[i];
                    float t = Ease.Clamp01(s.Life / Math.Max(0.01f, s.MaxLife));
                    float a = (1f - t * t) * 0.85f;
                    float r = s.Size * (1f - t * 0.4f);
                    if (r <= 0.3f || a <= 0.02f) continue;
                    p.Circle(s.X, s.Y, r, Ink.A(s.Color, a));
                }
            }

            p.Circle(cx, 18f, 11f + pop * 1.4f, Ink.A(tc, 0.22f));
            p.Icon('\uE945', 11f, tcInk, cx, 18f);
            p.TextCenter(shown + "x", Ink.Semi(Math.Min(13f, 11f + pop * 1.6f)), p.T.Text, cx, 42f);
            if (comboBrokenLeft > 0.01f)
            {
                p.TextCenter("BROKEN", Ink.Semi(7.5f), tcInk, cx, 64f);
            }
            else
            {
                int wpmShown = comboActive ? comboWpm : Math.Max(comboWpm, comboPeakWpm);
                p.TextCenter(wpmShown + "", Ink.Small(9.5f), p.T.Sub, cx, 58f);
                p.TextCenter("WPM", Ink.Semi(7.5f), tcInk, cx, 70f);
            }

            float frac = comboActive ? ComboDrainFraction() : (comboFinishLeft / ComboFinishHoldSec);
            if (frac > 0.01f)
            {
                float barW = Math.Max(6f, w - 12f);
                p.FillRound(new RectangleF(6f, h - 8f, barW, 3f), 1.5f, Ink.A(p.T.Text, 0.14f));
                p.FillRound(new RectangleF(6f, h - 8f, Math.Max(3f, barW * Ease.Clamp01(frac)), 3f), 1.5f, tc);
            }
        }

        void DrawComboStar(float cx, float cy, float r, float angle, Color c)
        {
            var pts = new PointF[8];
            for (int i = 0; i < 8; i++)
            {
                double a = angle + i * Math.PI / 4.0;
                float rad = (i & 1) == 0 ? r : r * 0.36f;
                pts[i] = new PointF(cx + (float)Math.Cos(a) * rad, cy + (float)Math.Sin(a) * rad);
            }
            g.FillPolygon(p.B(c), pts);
        }

        void DrawComboDiamond(float cx, float cy, float r, Color c)
        {
            var pts = new[]
            {
                new PointF(cx, cy - r),
                new PointF(cx + r * 0.75f, cy),
                new PointF(cx, cy + r),
                new PointF(cx - r * 0.75f, cy)
            };
            g.FillPolygon(p.B(c), pts);
        }
    }
}
