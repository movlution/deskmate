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
        const int WH_KEYBOARD_LL = 13;
        const int WM_KEYDOWN = 0x0100, WM_KEYUP = 0x0101, WM_SYSKEYDOWN = 0x0104, WM_SYSKEYUP = 0x0105;
        const int ComboMinHits = 3;
        const float ComboWarmupWindowSec = 0.95f;
        const float ComboHoldSec = 2.2f;
        const float ComboFinishHoldSec = 1.35f;

        [StructLayout(LayoutKind.Sequential)]
        struct ComboKbdHook
        {
            public int Vk, Scan, Flags, Time;
            public IntPtr Extra;
        }

        sealed class ComboSpark
        {
            public float X, Y, Vx, Vy, Size, Life, MaxLife, Spin, Angle;
            public Color Color;
            public int Kind; // 0 = circle, 1 = 4-point star, 2 = diamond
        }

        sealed class ComboCallout
        {
            public string Text;
            public float X, Y, Vy, Life, MaxLife;
            public Color Color;
        }

        IntPtr comboHook = IntPtr.Zero;
        KeyHookProc comboHookProc;
        readonly bool[] comboKeyDown = new bool[256];
        readonly List<double> comboKeyTimes = new List<double>();
        readonly List<ComboSpark> comboSparks = new List<ComboSpark>();
        readonly List<ComboCallout> comboCallouts = new List<ComboCallout>();
        readonly Random comboRng = new Random();
        sealed class ComboShard
        {
            public float X, Y, Vx, Vy, W, H, Angle, Spin, Life, MaxLife;
            public Color Color;
        }

        readonly Spring comboPop = new Spring(0, 720, 28f, 0.002f);
        readonly Spring comboRing = new Spring(0, 260, 28f, 0.002f);
        readonly Spring comboShake = new Spring(0, 900, 27f, 0.002f);
        readonly List<ComboShard> comboShards = new List<ComboShard>();
        sealed class ComboConfettiPiece
        {
            public float X, Y, Vx, Vy, W, H, Angle, Spin, Flutter, FlutterSpd, Life, MaxLife;
            public Color Color;
            public bool Round;
        }
        readonly List<ComboConfettiPiece> comboConfetti = new List<ComboConfettiPiece>();

        int comboCount;
        int comboBest;
        int comboLastRun;
        int comboLastTier;
        int comboWpm;
        int comboPeakWpm;
        float comboTimeLeft;
        float comboFinishLeft;
        float comboBrokenLeft;
        float comboRankFlash;
        float comboGoldFlash;
        float comboShieldCrack;
        float comboEnterSmash;
        float comboRing2;
        double comboLastHitSec = -100;
        double comboLastEnterSec = -100;
        string comboLastKey = "";
        string comboCalloutWord = "";
        bool comboActive;
        int comboShields;
        int comboBackStreak;
        int comboCleanStreak;
        int comboRunXp;
        int comboLastRunXp;
        bool comboPbPlayedThisRun;
        bool combo100xCountedThisRun;
        int comboInitialBestThisRun;
        ToolStripMenuItem trayComboItem;

        // Persistent RPG, quest, badges & sprint state
        bool comboStatsLoaded;
        bool comboStatsDirty;
        int comboXp;
        int comboAllTimeBest;
        int comboAllTimePeakWpm;
        int comboTotalChars;
        string comboDailyDate = "";
        int comboDailyChars;
        int comboDaily100x;
        bool comboDailyQuestDone;
        readonly HashSet<string> comboBadges = new HashSet<string>();
        int comboSprintBestWpm;

        // 30-second Typing Sprint state
        bool sprintActive;
        float sprintLeft;
        float sprintDoneLeft;
        int sprintChars;
        int sprintFinalWpm;
        volatile bool comboPaused;
        IntPtr comboForeground;
        int comboForegroundChecked;

        static readonly string[] ComboBadgeOrder =
        {
            "first_blood", "speed_demon", "unstoppable",
            "no_bs_ninja", "critical_finisher", "sprint_pro",
            "deep_focus", "priority_sweep"
        };

        public bool ComboOn
        {
            get { return settings != null && settings.GetBool("keys.combo", false); }
        }

        public bool ComboSoundOn
        {
            get { return settings != null && settings.GetBool("keys.combo.sound", false); }
        }

        public string ComboSoundProfile
        {
            get { return settings != null ? (settings.Get("keys.combo.sound.profile", "arcade") ?? "arcade") : "arcade"; }
        }

        public bool ComboFeverOn
        {
            get { return settings == null || settings.GetBool("keys.combo.fever", true); }
        }

        public bool ComboFeverActive
        {
            get { return comboActive && ComboFeverOn && ComboTierFor(comboCount) >= 5; }
        }

        public string ComboPenaltyMode
        {
            get { return settings != null ? (settings.Get("keys.combo.penalty", "shield") ?? "shield") : "shield"; }
        }

        public bool ComboEnterOn
        {
            get { return settings == null || settings.GetBool("keys.combo.enter", true); }
        }

        public bool ComboRpgOn
        {
            get { return settings == null || settings.GetBool("keys.combo.rpg", true); }
        }

        public int ComboQuestCharsGoal
        {
            get { return settings != null ? Math.Max(100, settings.GetInt("keys.combo.quest.chars", 5000)) : 5000; }
        }

        public int ComboQuest100xGoal
        {
            get { return settings != null ? Math.Max(1, settings.GetInt("keys.combo.quest.combos", 3)) : 3; }
        }

        public int ComboCount { get { return comboCount; } }
        public int ComboBest { get { return comboBest; } }
        public int ComboTier { get { return ComboTierFor(comboActive ? comboCount : comboLastRun); } }
        public int ComboWpm { get { return comboWpm; } }

        public int ComboXp { get { EnsureComboStatsLoaded(); return comboXp; } }
        public int ComboLevel { get { EnsureComboStatsLoaded(); return ComboLevelFor(comboXp); } }
        public string ComboLevelTitle { get { return ComboLevelTitleFor(ComboLevel); } }
        public float ComboLevelProgress { get { EnsureComboStatsLoaded(); return ComboLevelProgressFor(comboXp); } }
        public int ComboAllTimeBest { get { EnsureComboStatsLoaded(); return comboAllTimeBest; } }
        public int ComboAllTimePeakWpm { get { EnsureComboStatsLoaded(); return comboAllTimePeakWpm; } }
        public int ComboTotalChars { get { EnsureComboStatsLoaded(); return comboTotalChars; } }
        public int ComboDailyChars { get { EnsureComboStatsLoaded(); return comboDailyChars; } }
        public int ComboDaily100x { get { EnsureComboStatsLoaded(); return comboDaily100x; } }
        public bool ComboDailyQuestDone { get { EnsureComboStatsLoaded(); return comboDailyQuestDone; } }
        public int ComboBadgesCount { get { EnsureComboStatsLoaded(); return comboBadges.Count; } }
        public int ComboBadgesTotal { get { return ComboBadgeOrder.Length; } }
        public int ComboSprintBestWpm { get { EnsureComboStatsLoaded(); return comboSprintBestWpm; } }
        public bool TypingSprintActive { get { return sprintActive; } }
        public float TypingSprintLeft { get { return sprintLeft; } }

        public bool HasComboBadge(string id)
        {
            EnsureComboStatsLoaded();
            return !string.IsNullOrEmpty(id) && comboBadges.Contains(id);
        }

        public string ComboUnlockedBadgesSummary()
        {
            EnsureComboStatsLoaded();
            if (comboBadges.Count == 0) return Lang.T("Belum ada lencana");
            var list = new List<string>();
            for (int i = 0; i < ComboBadgeOrder.Length; i++)
            {
                if (comboBadges.Contains(ComboBadgeOrder[i]))
                    list.Add(ComboBadgeTitle(ComboBadgeOrder[i]));
            }
            return string.Join(", ", list.ToArray());
        }

        public bool ComboShowing
        {
            get { return !comboPaused && ((ComboOn && (comboActive || comboFinishLeft > 0.01f || comboBrokenLeft > 0.01f)) || sprintActive || sprintDoneLeft > 0.01f); }
        }
        void StartCombo()
        {
            EnsureComboStatsLoaded();
            RefreshComboPause(true);
            SyncComboHook();
        }

        void RefreshComboPause(bool immediate = false)
        {
            if (!IsHandleCreated || IsDisposed || Disposing) return;
            if (!ComboOn && !sprintActive && !comboPaused) return;
            IntPtr foreground = Native.GetForegroundWindow();
            int now = Environment.TickCount;
            if (!immediate && foreground == comboForeground && (uint)(now - comboForegroundChecked) < 150) return;
            comboForeground = foreground;
            comboForegroundChecked = now;

            int state;
            bool paused = (Native.SHQueryUserNotificationState(out state) == 0 && state == 3)
                || Native.IsFullscreenWindow(foreground, Native.MonitorFromWindow(foreground, 2));
            var rule = paused ? null : FrontRule();
            ApplyComboPause(paused || (rule != null && rule.Mode == ExceptCombo));
        }

        void ApplyComboPause(bool paused)
        {
            if (paused == comboPaused) return;
            comboPaused = paused;
            if (paused)
            {
                sprintDoneLeft = 0;
                ClearComboAudioVoices();
            }
            SyncComboHook();
        }

        public void ReloadComboSettings()
        {
            EnsureComboStatsLoaded();
            CheckDailyQuest();
            SyncComboHook();
            Kick();
        }

        public void SyncComboHook()
        {
            if (!comboPaused && (ComboOn || sprintActive))
            {
                if (comboHook == IntPtr.Zero && !IsDisposed)
                {
                    comboHookProc = ComboHookCallback;
                    comboHook = SetWindowsHookEx(WH_KEYBOARD_LL, comboHookProc, GetModuleHandle(null), 0);
                }
                if (ComboSoundOn && !IsDisposed)
                {
                    EnsureComboAudio();
                    EnsureMixerStarted();
                }
            }
            else
            {
                StopComboHook();
                ResetCombo();
            }
            if (trayComboItem != null && !trayComboItem.IsDisposed)
                trayComboItem.Checked = ComboOn;
            Kick();
        }

        void StopComboHook()
        {
            if (comboHook != IntPtr.Zero)
            {
                UnhookWindowsHookEx(comboHook);
                comboHook = IntPtr.Zero;
            }
            comboHookProc = null;
            Array.Clear(comboKeyDown, 0, comboKeyDown.Length);
        }

        public void ResetCombo()
        {
            comboCount = 0;
            comboLastRun = 0;
            comboLastTier = 0;
            comboWpm = 0;
            comboTimeLeft = 0;
            comboFinishLeft = 0;
            comboBrokenLeft = 0;
            comboRankFlash = 0;
            comboGoldFlash = 0;
            comboShieldCrack = 0;
            comboEnterSmash = 0;
            comboRing2 = 0;
            comboActive = false;
            comboShields = 0;
            comboBackStreak = 0;
            comboCleanStreak = 0;
            comboRunXp = 0;
            comboPbPlayedThisRun = false;
            combo100xCountedThisRun = false;
            comboInitialBestThisRun = 0;
            comboLastKey = "";
            comboCalloutWord = "";
            comboKeyTimes.Clear();
            comboSparks.Clear();
            comboShards.Clear();
            comboCallouts.Clear();
            comboConfetti.Clear();
            Array.Clear(comboKeyDown, 0, comboKeyDown.Length);
            comboPop.Snap(0);
            comboRing.Snap(0);
            comboShake.Snap(0);
        }

        void EnsureComboStatsLoaded()
        {
            if (comboStatsLoaded) return;
            comboStatsLoaded = true;
            if (settings == null) return;
            comboXp = Math.Max(0, settings.GetInt("combo.xp", 0));
            comboAllTimeBest = Math.Max(0, settings.GetInt("combo.best", 0));
            comboAllTimePeakWpm = Math.Max(0, settings.GetInt("combo.peak_wpm", 0));
            comboTotalChars = Math.Max(0, settings.GetInt("combo.total_chars", 0));
            comboSprintBestWpm = Math.Max(0, settings.GetInt("combo.sprint.best_wpm", 0));
            string today = DateTime.Now.ToString("yyyy-MM-dd");
            comboDailyDate = settings.Get("combo.daily.date", today) ?? today;
            if (comboDailyDate != today)
            {
                comboDailyDate = today;
                comboDailyChars = 0;
                comboDaily100x = 0;
                comboDailyQuestDone = false;
                comboStatsDirty = true;
            }
            else
            {
                comboDailyChars = Math.Max(0, settings.GetInt("combo.daily.chars", 0));
                comboDaily100x = Math.Max(0, settings.GetInt("combo.daily.100x", 0));
                comboDailyQuestDone = settings.GetBool("combo.daily.done", false);
            }
            comboBadges.Clear();
            string rawBadges = settings.Get("combo.badges", "") ?? "";
            if (rawBadges.Length > 0)
            {
                string[] parts = rawBadges.Split(',');
                for (int i = 0; i < parts.Length; i++)
                {
                    string b = parts[i].Trim();
                    if (b.Length > 0) comboBadges.Add(b);
                }
            }
        }

        public void FlushComboStats()
        {
            if (!comboStatsDirty || settings == null) return;
            comboStatsDirty = false;
            settings.Set("combo.xp", comboXp.ToString());
            settings.Set("combo.best", comboAllTimeBest.ToString());
            settings.Set("combo.peak_wpm", comboAllTimePeakWpm.ToString());
            settings.Set("combo.total_chars", comboTotalChars.ToString());
            settings.Set("combo.daily.date", comboDailyDate ?? DateTime.Now.ToString("yyyy-MM-dd"));
            settings.Set("combo.daily.chars", comboDailyChars.ToString());
            settings.Set("combo.daily.100x", comboDaily100x.ToString());
            settings.Set("combo.daily.done", comboDailyQuestDone ? "1" : "0");
            settings.Set("combo.sprint.best_wpm", comboSprintBestWpm.ToString());
            var list = new List<string>(comboBadges);
            settings.Set("combo.badges", string.Join(",", list.ToArray()));
            settings.Save();
        }

        public void ResetComboStats()
        {
            StopTypingSprint();
            ResetCombo();
            EnsureComboStatsLoaded();
            comboXp = 0;
            comboBest = 0;
            comboAllTimeBest = 0;
            comboLastRunXp = 0;
            comboPeakWpm = 0;
            comboAllTimePeakWpm = 0;
            comboTotalChars = 0;
            comboDailyDate = DateTime.Now.ToString("yyyy-MM-dd");
            comboDailyChars = 0;
            comboDaily100x = 0;
            comboDailyQuestDone = false;
            comboBadges.Clear();
            comboSprintBestWpm = 0;
            comboStatsDirty = true;
            FlushComboStats();
            Kick();
        }

        public static int ComboLevelFor(int xp)
        {
            if (xp <= 0) return 1;
            int lv = 1;
            int rem = xp;
            while (true)
            {
                int need = 100 + (lv - 1) * 60;
                if (rem < need) break;
                rem -= need;
                lv++;
            }
            return lv;
        }

        public static float ComboLevelProgressFor(int xp)
        {
            if (xp <= 0) return 0f;
            int lv = 1;
            int rem = xp;
            while (true)
            {
                int need = 100 + (lv - 1) * 60;
                if (rem < need) return (float)rem / need;
                rem -= need;
                lv++;
            }
        }

        public static string ComboLevelTitleFor(int level)
        {
            if (level >= 25) return Lang.T("Dewa Keyboard");
            if (level >= 15) return Lang.T("Master Ketik");
            if (level >= 10) return Lang.T("Cyber Typist");
            if (level >= 6) return Lang.T("Arcade Striker");
            if (level >= 3) return Lang.T("Pengetik Gesit");
            return Lang.T("Pemula");
        }

        static string ComboBadgeTitle(string id)
        {
            switch (id)
            {
                case "first_blood": return Lang.T("First Blood (10x)");
                case "speed_demon": return Lang.T("Speed Demon (100 WPM)");
                case "unstoppable": return Lang.T("Unstoppable (250x)");
                case "no_bs_ninja": return Lang.T("No-Backspace Ninja");
                case "critical_finisher": return Lang.T("Critical Finisher");
                case "sprint_pro": return Lang.T("Sprint Pro (80+ WPM)");
                case "deep_focus": return Lang.T("Deep Worker (Fokus)");
                case "priority_sweep": return Lang.T("3/3 Prioritas Tuntas");
                default: return id;
            }
        }

        void UnlockComboBadge(string id)
        {
            if (!ComboRpgOn) return;
            EnsureComboStatsLoaded();
            if (comboBadges.Contains(id)) return;
            comboBadges.Add(id);
            comboStatsDirty = true;
            var t = ShowToast(View.Info, 3.0, 2, false, true);
            if (t != null)
            {
                t.Glyph = '\uE734';
                t.Title = Lang.T("Lencana Terbuka!");
                t.Detail = ComboBadgeTitle(id);
            }
            FlushComboStats();
        }

        void AddComboXp(int amount)
        {
            if (!ComboRpgOn || amount <= 0) return;
            EnsureComboStatsLoaded();
            int prevLv = ComboLevelFor(comboXp);
            comboXp += amount;
            comboRunXp += amount;
            comboStatsDirty = true;
            int newLv = ComboLevelFor(comboXp);
            if (newLv > prevLv)
            {
                var t = ShowToast(View.Info, 2.8, 2, false, true);
                if (t != null)
                {
                    t.Glyph = '\uE7FC';
                    t.Title = Lang.F("Naik Level! Lv.{0}", newLv);
                    t.Detail = ComboLevelTitleFor(newLv);
                }
            }
        }

        internal void GrantProductivityXp(int amount)
        {
            if (!ComboRpgOn || amount <= 0) return;
            AddComboXp(amount);
            FlushComboStats();
        }

        void CheckDailyQuest()
        {
            if (!ComboRpgOn || comboDailyQuestDone) return;
            if (comboDailyChars >= ComboQuestCharsGoal && comboDaily100x >= ComboQuest100xGoal)
            {
                comboDailyQuestDone = true;
                comboStatsDirty = true;
                celebrateUtc = DateTime.UtcNow;
                AddComboXp(150);
                var t = ShowToast(View.Info, 3.4, 2, false, true);
                if (t != null)
                {
                    t.Glyph = '\uE73E';
                    t.Title = Lang.T("Target Harian Tercapai!");
                    t.Detail = "+150 XP";
                }
                FlushComboStats();
            }
        }

        public void StartTypingSprint()
        {
            EnsureComboStatsLoaded();
            sprintLeft = 30f;
            sprintActive = true;
            sprintDoneLeft = 0f;
            sprintChars = 0;
            sprintFinalWpm = 0;
            expanded = false;
            RefreshComboPause(true);
            SyncComboHook();
            Kick();
        }

        public void StopTypingSprint()
        {
            if (!sprintActive && sprintDoneLeft <= 0f) return;
            sprintActive = false;
            sprintDoneLeft = 0f;
            sprintLeft = 0f;
            SyncComboHook();
            Kick();
        }

        public void ToggleCombo()
        {
            bool next = !ComboOn;
            settings.Set("keys.combo", next ? "1" : "0");
            settings.Save();
            RefreshComboPause(true);
            SyncComboHook();
            if (prefs != null && !prefs.IsDisposed) prefs.Relayout();
            var t = ShowToast(View.Info, 2.8, 2, false, true);
            if (t != null)
            {
                t.Glyph = '\uE7FC';
                t.Title = Lang.T(next ? "Combo Ketikan aktif" : "Combo Ketikan mati");
                t.Detail = Lang.T(next ? "Ketik cepat untuk memicu combo & efek seru!" : "Gamifikasi ketikan dinonaktifkan");
            }
            Kick();
        }
    }
}
