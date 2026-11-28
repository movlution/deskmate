using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Threading;
using System.Windows.Forms;
using Windows.UI.Notifications.Management;

namespace Deskmate
{
    partial class Island
    {
        // System

        void OnVolume(float level, bool mute, bool first)
        {
            vol = level;
            muted = mute;
            volBar.Target = level;
            volRoller.Set(mute ? "0" : ((int)Math.Round(level * 100)).ToString());
            if (first || !volKnown) { volKnown = true; volBar.Snap(level); return; }
            if (!expanded) ShowToast(View.Volume, 1.5, 1, false, true);
            Kick();
        }

        void OnNet(NetState a, NetState b)
        {
            if (b.Captive && !a.Captive) { AskWifiLogin(b.Name); return; }
            var t = ShowToast(View.Network, 3.2, 2, false);
            if (t != null)
            {
                t.Flag = b.Online;
                if (!b.Any) { t.Title = Lang.T("Jaringan terputus"); t.Detail = a.Name; }
                else if (!b.Online) { t.Title = Lang.T("Tanpa internet"); t.Detail = b.Name; }
                else if (a.Captive || (!a.Online && a.Any && a.Name == b.Name)) { t.Title = Lang.T("Internet kembali"); t.Detail = b.Name; }
                else if (b.Wifi) { t.Title = Lang.T("Wi-Fi tersambung"); t.Detail = b.Name; }
                else { t.Title = Lang.T("Tersambung"); t.Detail = b.Name.Length > 0 ? b.Name : "LAN"; }
            }
            if (!a.Online && b.Online && tasksError != null) RefreshTasks(false);
            if (!a.Online && b.Online && sources != null) sources.Retry();
            if (!a.Online && b.Online && WeatherOn && (weather == null || weatherFails > 0)) RetryWeather();
            Kick();
        }

        // Captive portal: one click to its page
        void AskWifiLogin(string ssid)
        {
            var t = ShowToast(View.Info, 12, 2, true);
            if (t == null) return;
            t.Glyph = Ink.IcoWifi;
            t.Title = Lang.T("Wi-Fi butuh login");
            t.Detail = ssid;
            // Plain http on purpose: portals intercept it
            t.Data = new Action(delegate { OpenUrl("http://www.msftconnecttest.com/redirect"); });
            Kick();
        }

        void OnPrivacy(string[] mic, string[] cam)
        {
            string newMic = null, newCam = null;
            foreach (var x in mic) if (Array.IndexOf(micApps, x) < 0) { newMic = x; break; }
            foreach (var x in cam) if (Array.IndexOf(camApps, x) < 0) { newCam = x; break; }
            micApps = mic;
            camApps = cam;
            TrackMeeting();
            if (newCam != null || newMic != null)
            {
                var t = ShowToast(View.Privacy, 3, 2, false);
                if (t != null) { t.Flag = newCam != null; t.Title = newCam ?? newMic; }
            }
            Kick();
        }

        // Messages

        void OnNotifs(UserNotificationListenerAccessStatus access, List<NotifItem> list)
        {
            notifAccess = access;
            if (list == null || !NotifOn) { Kick(); return; }
            var visible = list.FindAll(n => n.Chat && !NotifAppHidden(n));
            // Toasts Windows doesn't keep stay here
            var live = new HashSet<uint>();
            foreach (var n in visible) live.Add(n.Id);
            foreach (var n in notifs)
                if (!live.Contains(n.Id) && !NotifAppHidden(n) && (n.Held || !NotifSource.Kept(n.AppId)))
                {
                    n.Held = true;
                    visible.Add(n);
                }
            visible.Sort((a, b) => b.Time.CompareTo(a.Time));
            // Apps re-post the same message: keep the newest copy, toast once
            var known = new HashSet<string>();
            foreach (var n in notifs) known.Add(NotifKey(n));
            var keys = new HashSet<string>();
            visible.RemoveAll(n => !keys.Add(NotifKey(n)));
            if (visible.Count > 40) visible.RemoveRange(40, visible.Count - 40);
            var fresh = new List<NotifItem>();
            if (notifPrimed)
                foreach (var n in visible)
                    if (!n.Held && !seenNotifs.Contains(n.Id) && !known.Contains(NotifKey(n)) && (DateTime.UtcNow - n.Time).TotalMinutes < 3) fresh.Add(n);
            seenNotifs.Clear();
            foreach (var n in list) seenNotifs.Add(n.Id);
            notifPrimed = true;
            // Phone copy of this message: pop once
            var pops = new List<NotifItem>();
            int added = 0;
            foreach (var n in fresh)
            {
                bool dup = SeenElsewhere(n);
                Remember(n, dup);
                if (dup)
                {
                    int twin = PhoneTwin(visible, n);
                    if (twin >= 0) visible.RemoveAt(twin);
                    continue;
                }
                added++;
                if (ShouldPop(n)) pops.Add(n);
            }
            notifs = visible;
            unread = Math.Min(unread, visible.Count);
            if (added > 0)
            {
                if (expanded && page == PageMessages) unread = 0;
                else unread += added;
            }
            if (pops.Count > 0 && !Native.DoNotDisturb())
            {
                bool burst = toast != null && toast.View == View.Notif;
                var vip = pops.Find(IsVip);
                // A call rings longer and passes quiet hours
                var call = pops.Find(IsCall);
                var t = ShowToast(View.Notif, call != null ? 25 : 5.5, vip != null || call != null ? 3 : 2, true);
                if (t != null)
                {
                    notifBurst = burst ? notifBurst + pops.Count : pops.Count;
                    t.Data = call ?? vip ?? pops[0];
                }
            }
            Kick();
        }

        static string NotifKey(NotifItem n) { return n.AppId + "\n" + n.Title + "\n" + n.Body; }

        void OpenNotifList()
        {
            if (toast != null && toast.View == View.Notif)
            {
                toast = null;
                if (toastTimer != null) toastTimer.Stop();
            }
            if (notifScroll != null) notifScroll.Snap(0);
            Show(PageMessages);
        }

        void OpenNotifApp(NotifItem item)
        {
            // Phone items: nothing to launch here
            if (item.AppId.Length > 0 && !item.AppId.StartsWith("phone:") && item.AppId.IndexOfAny(new[] { '"', '\r', '\n' }) < 0)
            {
                var it = item;
                // The sender's chat first, the app window after
                NotifOpen.AllowSetForegroundWindow(-1);
                ThreadPool.QueueUserWorkItem(delegate
                {
                    bool chat = NotifOpen.Open(it);
                    if (chat) Thread.Sleep(400);
                    // Quoted; a stray quote can't add arguments
                    try { Process.Start("explorer.exe", "\"shell:AppsFolder\\" + it.AppId + "\""); }
                    catch (Exception ex) { Log.Write("open app: " + ex.Message); }
                });
            }
            notifs.RemoveAll(n => n.Held && n.AppId == item.AppId);
            unread = 0;
            expanded = false;
            if (toast != null && toast.View == View.Notif) DismissToast();
            Kick();
        }

        void RemoveNotifs(List<NotifItem> items)
        {
            foreach (var n in items) notifs.Remove(n);
            unread = Math.Min(unread, notifs.Count);
            Kick();
            // Remember them, or the next read puts them straight back
            NotifSource.Dismiss(items);
            notifWatcher.Refresh();
        }
    }
}
