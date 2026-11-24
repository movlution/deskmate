using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace Deskmate
{
    // Settings window; every change applies live
    partial class Island
    {
        PrefsWindow prefs;

        void OpenPrefs()
        {
            if (prefs != null && !prefs.IsDisposed) { prefs.Activate(); return; }
            prefs = new PrefsWindow(this);
            prefs.Show();
            prefs.Activate();
        }

        void OpenPrefs(string section)
        {
            OpenPrefs();
            prefs.Go(section);
        }

        void SetClock24(bool h24)
        {
            settings.Set("clock.24", h24);
            settings.Save();
            Fmt.H24 = h24;
            clockRoller.Set(Fmt.Clock(DateTime.Now));
            // Cached prayer strings follow at once
            if (prayToday != null)
            {
                for (int i = 0; i < 6; i++) prayHm[i] = Fmt.Hm(prayToday[i]);
                prayTomorrowHm = Fmt.Hm(prayTomorrow);
            }
            Kick();
        }

        // theme: sistem (follows Windows), terang, gelap. "otomatis" is the
        // legacy spelling and keeps working for existing settings files.
        static string CanonicalThemeMode(string mode)
        {
            string m = (mode ?? "").Trim().ToLowerInvariant();
            if (m == "terang" || m == "light") return "terang";
            if (m == "gelap" || m == "dark") return "gelap";
            return "sistem";
        }

        string ThemeMode()
        {
            return CanonicalThemeMode(settings.Get("theme", "sistem"));
        }

        static int ThemeIndex(string mode)
        {
            string m = CanonicalThemeMode(mode);
            return m == "terang" ? 1 : m == "gelap" ? 2 : 0;
        }

        void SetThemeMode(string mode)
        {
            settings.Set("theme", CanonicalThemeMode(mode));
            settings.Save();
            ApplyThemeMode();
        }

        bool ThemeLight()
        {
            string mode = ThemeMode();
            return mode == "terang" || (mode == "sistem" && !winDark);
        }

        void ApplyThemeMode()
        {
            bool light = ThemeLight();
            if (light == Ink.Light) return;
            Ink.Apply(light);
            string curHex = settings.Get("accent", "");
            if (!string.IsNullOrEmpty(curHex))
            {
                for (int i = 0; i < Ink.DarkAccents.Length; i++)
                {
                    if (Ink.DarkAccents[i][1] == curHex || Ink.LightAccents[i][1] == curHex)
                    {
                        string newHex = light ? Ink.LightAccents[i][1] : Ink.DarkAccents[i][1];
                        if (newHex != curHex)
                        {
                            settings.Set("accent", newHex);
                            settings.Save();
                            Ink.SetAccent(newHex);
                        }
                        break;
                    }
                }
            }
            WallApply();
            ResetFrost();
            RefreshDocks();
            KitWindow.BroadcastThemeChange();
            Kick();
        }

        void SetAccentHex(string hex)
        {
            settings.Set("accent", hex);
            settings.Save();
            Ink.SetAccent(hex);
            albumAccentFor = null;
            albumTinted = false;
            AlbumAccent();
            WallApply();
            RefreshDocks();
            KitWindow.BroadcastThemeChange();
            Kick();
        }

        void SetFlag(string key, bool on)
        {
            settings.Set(key, on);
            settings.Save();
            if (key == "dock") ResetFrost();
            else if (key == "notif") notifWatcher.Enabled = NotifOn;
            else if (key == "lyrics.online") { lyrics = null; SyncLyrics(); }
            else if (key == "shots") ApplyClips();
            else if (key == "clipboard")
            {
                ApplyClips();
                if (on && Array.IndexOf(tabs, PageShelf) < 0) SetLayout("panel.tabs", TabIds(tabs) + ",shelf");
            }
            else if (key == "downloads") ApplyDownloads();
            else if (key == "tidy") ApplyTidy();
            else if (key == "capture.show") ApplyGlass();
            else if (key == "shelf" || key == "convert") InitShelfDrop();
            else if (key == "wall.sync") WallToggle(on);
            else if (key == "ingat" || key == "ingat.clip") ApplyIngat();
            else if (key.StartsWith("dockbar.", StringComparison.Ordinal)) ApplyDock();
            Kick();
        }

        void SetChoice(string key, string value)
        {
            settings.Set(key, value);
            settings.Save();
            if (key == "material") { ApplyGlass(); ResetFrost(); RefreshDocks(); WallMaterial(); }
            else if (key == "motion.reduce" || key == "motion.feel") Anim.Read(settings);
            else if (key == "share.mode") ApplyShare();
            Kick();
        }

        // A theme pack can switch the eyes off entirely
        internal bool EyesAllowed { get { return packEyes; } }

        void SetLanguage(bool english)
        {
            settings.Set("lang", english ? "en" : "id");
            settings.Save();
            Lang.English = english;
            BuildTray();
            if (chatWin != null && !chatWin.IsDisposed) chatWin.Relayout();
            Kick();
        }

        void SetLayout(string key, object value)
        {
            settings.Set(key, value);
            settings.Save();
            LoadLayout();
            ResetFrost();
            Kick();
        }

        // Open the panel while layout is being tuned
        void PreviewLayout(bool on)
        {
            if (on && !expanded) Expand(DefaultPage(false));
            else if (!on && expanded && !pointerInside) { expanded = false; Kick(); }
        }

        void PreviewPage(int pg)
        {
            if (!expanded) Expand(pg);
            else SetPage(pg, false);
        }

        // Corner inset or side height from settings
        void SetPlaceValue(string key, int v)
        {
            settings.Set(key, v);
            settings.Save();
            LoadPlace();
            if (GlassOn) NeedFrost(sh.Target);
            Kick();
        }

        // Random port; Claude's hook URL follows
        void MoveToRandomPort()
        {
            bool claude = HooksInstalled();
            int port = AgentHub.FreePort();
            if (hub != null) hub.Stop();
            settings.Set("agent.port", port);
            settings.Save();
            StartHub();
            if (claude)
            {
                string err = HookSetup.Install(port, settings.Get("agent.token", ""));
                if (err != null) ShowInfo(Ink.IcoTerminal, Lang.T("Belum terhubung"), err, true);
            }
            CheckLinks();
            ShowInfo(Ink.IcoTerminal, Lang.F("Port {0}", port), Lang.T("sesi agen baru memakainya"), true);
        }
    }
}
