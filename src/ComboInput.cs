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
        IntPtr ComboHookCallback(int code, IntPtr msg, IntPtr info)
        {
            if (code >= 0 && !comboPaused && (ComboOn || sprintActive))
            {
                try
                {
                    var k = (ComboKbdHook)Marshal.PtrToStructure(info, typeof(ComboKbdHook));
                    // Ignore synthetic/injected events (LLKHF_INJECTED = 0x10)
                    if ((k.Flags & 0x10) == 0)
                    {
                        int m = msg.ToInt32();
                        int vk = k.Vk;
                        if (m == WM_KEYUP || m == WM_SYSKEYUP)
                        {
                            if (vk >= 0 && vk < 256) comboKeyDown[vk] = false;
                        }
                        else if (m == WM_KEYDOWN || m == WM_SYSKEYDOWN)
                        {
                            if (vk >= 0 && vk < 256 && !comboKeyDown[vk])
                            {
                                comboKeyDown[vk] = true;
                                bool sysAlt = m == WM_SYSKEYDOWN && !(comboKeyDown[0x11] || comboKeyDown[0xA2] || comboKeyDown[0xA3]);
                                if (!sysAlt && !IsComboShortcutHeld() && IsTypingKey(vk))
                                {
                                    int capturedVk = vk;
                                    Post(delegate { OnComboKeystroke(capturedVk); });
                                }
                            }
                        }
                    }
                }
                catch (Exception) { }
            }
            return CallNextHookEx(comboHook, code, msg, info);
        }

        bool IsComboShortcutHeld()
        {
            // Win key (LWin 0x5B, RWin 0x5C)
            if (comboKeyDown[0x5B] || comboKeyDown[0x5C]) return true;
            bool ctrl = comboKeyDown[0x11] || comboKeyDown[0xA2] || comboKeyDown[0xA3];
            bool alt = comboKeyDown[0x12] || comboKeyDown[0xA4] || comboKeyDown[0xA5];
            // Allow Ctrl+Alt (AltGr on international layouts), block standalone Ctrl or Alt shortcuts
            return ctrl != alt;
        }

        public static bool IsTypingKey(int vk)
        {
            if (vk <= 0 || vk >= 256) return false;
            // Letters A-Z and digits 0-9
            if ((vk >= 0x30 && vk <= 0x39) || (vk >= 0x41 && vk <= 0x5A)) return true;
            // Numpad 0-9 and operators
            if (vk >= 0x60 && vk <= 0x6F) return true;
            // Space, Enter, Backspace, Tab, Delete
            if (vk == 0x20 || vk == 0x0D || vk == 0x08 || vk == 0x09 || vk == 0x2E) return true;
            // OEM punctuation keys (; = , - . / ` [ \ ] ') and VK_PACKET (IME/Unicode)
            if (vk >= 0xBA && vk <= 0xC0) return true;
            if (vk >= 0xDB && vk <= 0xDF) return true;
            if (vk == 0xE2 || vk == 0xE7) return true;
            return false;
        }

        public static string KeyCapLabel(int vk)
        {
            if (vk >= 0x41 && vk <= 0x5A) return ((char)vk).ToString();
            if (vk >= 0x30 && vk <= 0x39) return ((char)vk).ToString();
            if (vk >= 0x60 && vk <= 0x69) return ((char)('0' + (vk - 0x60))).ToString();
            switch (vk)
            {
                case 0x20: return "SPC";
                case 0x0D: return "⏎";
                case 0x08: return "⌫";
                case 0x09: return "TAB";
                case 0x2E: return "DEL";
                case 0x6A: return "*";
                case 0x6B: return "+";
                case 0x6C: return "⏎";
                case 0x6D: return "-";
                case 0x6E: return ".";
                case 0x6F: return "/";
                case 0xBA: return ";";
                case 0xBB: return "=";
                case 0xBC: return ",";
                case 0xBD: return "-";
                case 0xBE: return ".";
                case 0xBF: return "/";
                case 0xC0: return "`";
                case 0xDB: return "[";
                case 0xDC:
                case 0xE2: return "\\";
                case 0xDD: return "]";
                case 0xDE: return "'";
                case 0xE7: return "IME";
                default: return "KEY";
            }
        }

        public bool RegisterComboKeystroke(int vk)
        {
            RefreshComboPause();
            if (comboPaused || (!ComboOn && !sprintActive)) return false;
            if (vk < 0 || vk >= 256) return false;
            if (comboKeyDown[vk]) return false;
            comboKeyDown[vk] = true;
            if (IsComboShortcutHeld() || !IsTypingKey(vk)) return false;
            OnComboKeystroke(vk);
            return true;
        }

        public void ReleaseComboKey(int vk)
        {
            if (vk >= 0 && vk < 256) comboKeyDown[vk] = false;
        }

        void OnComboKeystroke(int vk)
        {
            RefreshComboPause();
            if (comboPaused || (!ComboOn && !sprintActive) || dormant || hiddenByUser || keyboardMode) return;
            EnsureComboStatsLoaded();
            double now = clock.Elapsed.TotalSeconds;
            double dtSinceLast = now - comboLastHitSec;

            bool isPenaltyKey = (vk == 0x08 || vk == 0x2E);
            if (isPenaltyKey)
            {
                comboCleanStreak = 0;
                if (!comboActive) return;
                comboLastKey = KeyCapLabel(vk);
                if (ComboPenaltyMode == "off")
                {
                    PlayComboKeySound(comboCount, false);
                    return;
                }
                comboLastKey = KeyCapLabel(vk);
                if (ComboPenaltyMode == "shield" && comboShields > 0)
                {
                    comboShields--;
                    comboShieldCrack = 1f;
                    comboBackStreak++;
                    comboShake.Snap((comboBackStreak & 1) == 0 ? 4.2f : -4.2f);
                    comboShake.Target = 0;
                    SpawnComboCallout(Lang.T("SHIELD RETAK!"), Color.FromArgb(255, 255, 190, 60));
                    PlayComboShieldCrackSound();
                    Kick();
                    return;
                }
                comboBackStreak++;
                comboTimeLeft -= (0.55f + Math.Min(0.4f, (comboBackStreak - 1) * 0.1f));
                comboShake.Snap((comboBackStreak & 1) == 0 ? 5.2f : -5.2f);
                comboShake.Target = 0;
                SpawnComboCallout("-DRAIN", Color.FromArgb(255, 255, 80, 90));
                if (comboTimeLeft <= 0f)
                {
                    BreakCombo();
                    return;
                }
                Kick();
                return;
            }

            // If not yet active and gap was too slow, start fresh warmup and clear any stale key states
            if (!comboActive && dtSinceLast > ComboWarmupWindowSec)
            {
                comboCount = 0;
                comboKeyTimes.Clear();
                Array.Clear(comboKeyDown, 0, comboKeyDown.Length);
                if (vk >= 0 && vk < 256) comboKeyDown[vk] = true;
            }
            if (!comboActive && comboCount == 0)
            {
                comboWpm = 0;
                comboPeakWpm = 0;
            }

            comboLastHitSec = now;
            comboCount++;
            if (comboCount > comboBest) comboBest = comboCount;
            comboLastKey = KeyCapLabel(vk);

            if (!isPenaltyKey)
            {
                comboCleanStreak++;
                comboTotalChars++;
                comboDailyChars++;
                comboStatsDirty = true;
                if (sprintActive)
                {
                    sprintChars++;
                }
            }
            else
            {
                comboCleanStreak = 0;
            }

            // Rolling 3.5-second window for live WPM calculation (5 chars = 1 word)
            comboKeyTimes.Add(now);
            double cutoff = now - 3.5;
            while (comboKeyTimes.Count > 0 && comboKeyTimes[0] < cutoff)
                comboKeyTimes.RemoveAt(0);
            double span = comboKeyTimes.Count > 1 ? Math.Max(0.45, now - comboKeyTimes[0]) : 0.6;
            double cps = comboKeyTimes.Count / span;
            int rawWpm = (int)Math.Round((cps * 60.0) / 5.0);
            comboWpm = Math.Max(15, Math.Min(320, comboWpm == 0 ? rawWpm : (int)Math.Round(comboWpm * 0.6 + rawWpm * 0.4)));

            if (comboCount < ComboMinHits && !comboActive)
            {
                PlayComboKeySound(comboCount, false);
                return;
            }

            bool justActivated = !comboActive;
            comboActive = true;
            comboFinishLeft = 0;
            comboBrokenLeft = 0;
            comboLastRun = comboCount;
            if (comboWpm > comboPeakWpm) comboPeakWpm = comboWpm;
            if (comboWpm > comboAllTimePeakWpm) { comboAllTimePeakWpm = comboWpm; comboStatsDirty = true; }
            if (justActivated)
            {
                comboShields = ComboPenaltyMode == "shield" ? 1 : 0;
                comboBackStreak = 0;
                comboRunXp = 0;
                comboPbPlayedThisRun = false;
                combo100xCountedThisRun = false;
                comboInitialBestThisRun = comboAllTimeBest;
            }
            int tier = ComboTierFor(comboCount);
            int mult = ComboFeverOn ? (tier >= 6 ? 4 : tier >= 5 ? 2 : 1) : 1;
            // Slightly tighter drain window at extreme tiers for arcade excitement
            float hold = tier >= 5 ? 1.85f : tier >= 3 ? 2.0f : ComboHoldSec;
            comboTimeLeft = hold;

            // Persistent all-time best & PB fanfare
            if (comboCount > comboAllTimeBest)
            {
                if (!comboPbPlayedThisRun && comboInitialBestThisRun >= 10 && comboCount > comboInitialBestThisRun)
                {
                    comboPbPlayedThisRun = true;
                    PlayComboPbFanfare();
                    SpawnComboCallout("NEW RECORD!", Color.FromArgb(255, 255, 220, 70));
                    float cw = OnSide ? SideW : ComboWidth();
                    float ch = OnSide ? 84f : ComboHeight();
                    SpawnComboConfetti(36, cw * 0.4f, ch * 0.5f, 340f, true, true);
                    if (!Anim.Reduced) celebrateUtc = DateTime.UtcNow;
                }
                comboAllTimeBest = comboCount;
                comboStatsDirty = true;
            }

            // XP & Badges & Daily Quest
            AddComboXp(1 * mult);
            if (comboCount >= 10) UnlockComboBadge("first_blood");
            if (comboWpm >= 100) UnlockComboBadge("speed_demon");
            if (comboCount >= 250) UnlockComboBadge("unstoppable");
            if (comboCleanStreak >= 50) UnlockComboBadge("no_bs_ninja");
            if (comboCount >= 100 && !combo100xCountedThisRun)
            {
                combo100xCountedThisRun = true;
                comboDaily100x++;
                comboStatsDirty = true;
            }
            CheckDailyQuest();

            // Per-keystroke in-memory audio
            PlayComboKeySound(comboCount, false);
            // Spring pop & island recoil on every hit
            comboPop.Snap(Math.Min(1.35f, 0.72f + tier * 0.1f));
            comboPop.Target = 0;
            comboPop.Velocity = 4.5f + tier * 1.2f;

            float dir = (comboCount & 1) == 0 ? 1f : -1f;
            comboShake.Snap(dir * Math.Min(4.5f, 1.2f + tier * 0.55f));
            comboShake.Target = 0;

            if (!Anim.Reduced && current == View.Combo)
            {
                float alongV = Math.Min(110f, 32f + tier * 12f);
                float acrossV = Math.Min(48f, 14f + tier * 5f);
                sw.Velocity += OnSide ? acrossV : alongV;
                sh.Velocity += OnSide ? alongV : acrossV;
            }

            Color tc = ComboTierColor(tier, now);
            int sparkCount = justActivated ? 8 : (2 + Math.Min(5, tier));
            SpawnComboSparks(sparkCount, false, tc);

            // Check for rank-up or milestone burst
            if (tier > comboLastTier)
            {
                comboLastTier = tier;
                comboRankFlash = 1f;
                comboRing.Snap(0.05f);
                comboRing.Target = 1f;
                comboCalloutWord = tier >= 6 ? "GODLIKE!!!" : tier == 5 ? "ULTRA!!" : tier == 4 ? "AWESOME!" : tier == 3 ? "GREAT!!" : tier == 2 ? "NICE!" : "COMBO!";
                SpawnComboCallout(comboCalloutWord, tc);
                SpawnComboSparks(14 + tier * 3, true, tc);
                if (tier >= 2)
                {
                    float cw = OnSide ? SideW : ComboWidth();
                    float ch = OnSide ? 84f : ComboHeight();
                    SpawnComboConfetti(12 + tier * 5, cw * 0.5f, ch * 0.5f, 320f, false, tier >= 4);
                }

                if (!Anim.Reduced)
                {
                    float alongV = 180f + tier * 25f;
                    float acrossV = 75f + tier * 10f;
                    sw.Velocity += OnSide ? acrossV : alongV;
                    sh.Velocity += OnSide ? alongV : acrossV;
                    topGap.Velocity += 55f;
                    if (tier >= 3) celebrateUtc = DateTime.UtcNow;
                }

                EyesReact(tier >= 4 ? EyeMood.Surprised : EyeMood.Happy, 0);
                PlayComboTierUpSound(tier);
            }
            else if (comboCount % 25 == 0)
            {
                comboRankFlash = 0.75f;
                comboRing.Snap(0.1f);
                comboRing.Target = 1f;
                SpawnComboCallout(comboCount + " HIT STREAK!", tc);
                SpawnComboSparks(12, true, tc);
                float cw = OnSide ? SideW : ComboWidth();
                float ch = OnSide ? 84f : ComboHeight();
                SpawnComboConfetti(22, cw * 0.5f, ch * 0.5f, 300f, false, false);
            }
            else if (comboCount % 5 == 0)
            {
                string[] words = { "FAST!", "CRIT!", "FIRE!", "CLEAN!", "SHARP!" };
                SpawnComboCallout(words[(comboCount / 5) % words.Length], tc);
            }

            // Clean streak 25x milestone: PERFECT CHAIN!
            if (!isPenaltyKey && comboCleanStreak > 0 && comboCleanStreak % 25 == 0)
            {
                comboGoldFlash = 1f;
                AddComboXp(20 * mult);
                if (ComboPenaltyMode == "shield" && comboShields < 2) comboShields++;
                SpawnComboCallout("PERFECT CHAIN!", Color.FromArgb(255, 255, 214, 10));
                float cw = OnSide ? SideW : ComboWidth();
                float ch = OnSide ? 84f : ComboHeight();
                SpawnComboConfetti(22, cw * 0.45f, ch * 0.5f, 280f, true, false);
            }

            // Enter finisher move: CRITICAL FINISH!
            if (vk == 0x0D && ComboEnterOn && comboActive && comboCount >= 25 && (now - comboLastEnterSec) >= 1.2)
            {
                comboLastEnterSec = now;
                comboEnterSmash = 1f;
                comboRankFlash = 0.85f;
                comboRing.Snap(0.04f);
                comboRing.Target = 1f;
                comboRing2 = 0.01f;
                comboTimeLeft = Math.Min(hold, comboTimeLeft + 0.35f);
                AddComboXp(25 * mult);
                UnlockComboBadge("critical_finisher");
                SpawnComboCallout("CRITICAL FINISH!", Color.FromArgb(255, 255, 140, 40));
                SpawnComboConfetti(28, OnSide ? SideW * 0.5f : ComboWidth() - 34f, OnSide ? 42f : ComboHeight() * 0.5f, 300f, false, true);
                if (!Anim.Reduced) celebrateUtc = DateTime.UtcNow;
                if (!Anim.Reduced)
                {
                    float kx = (OnSide ? SideW * 0.5f : ComboWidth() - 34f);
                    float ky = (OnSide ? 42f : ComboHeight() * 0.5f);
                    for (int i = 0; i < 16; i++)
                    {
                        if (comboSparks.Count >= 64) comboSparks.RemoveAt(0);
                        float vx = ((i & 1) == 0 ? 1f : -1f) * (35f + (float)comboRng.NextDouble() * 85f);
                        float vy = -(45f + (float)comboRng.NextDouble() * 95f);
                        comboSparks.Add(new ComboSpark
                        {
                            X = kx, Y = ky, Vx = vx, Vy = vy,
                            Size = 2.6f + (float)comboRng.NextDouble() * 2.4f,
                            Life = 0f, MaxLife = 0.5f + (float)comboRng.NextDouble() * 0.25f,
                            Spin = ((float)comboRng.NextDouble() - 0.5f) * 12f,
                            Angle = (float)(comboRng.NextDouble() * Math.PI),
                            Color = (i % 2 == 0) ? Color.FromArgb(255, 255, 200, 70) : Color.FromArgb(255, 255, 110, 40),
                            Kind = i % 3
                        });
                    }
                }
                PlayComboCriticalSound();
            }

            Kick();
        }

        void BreakCombo()
        {
            comboTimeLeft = 0f;
            comboActive = false;
            comboLastRun = comboCount;
            comboBrokenLeft = 1.15f;
            comboLastRunXp = comboRunXp;
            SpawnComboShards(ComboTierColor(ComboTierFor(comboCount), clock.Elapsed.TotalSeconds));
            comboCount = 0;
            comboLastTier = 0;
            comboWpm = 0;
            comboPeakWpm = 0;
            comboKeyTimes.Clear();
            Array.Clear(comboKeyDown, 0, comboKeyDown.Length);
            PlayComboBrokenSound();
            FlushComboStats();
            Kick();
        }
    }
}
