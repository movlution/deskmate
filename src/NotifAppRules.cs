using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text.Json;
using System.Windows.Forms;

namespace Deskmate
{
    partial class Island
    {
        const string NotifAppRulesKey = "notif.appRules";
        static readonly JsonSerializerOptions NotifAppRulesJsonOptions = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            IncludeFields = true
        };

        public sealed class NotifAppRule
        {
            public string Name { get; set; }
            public string Mode { get; set; }
            public long UntilUtc { get; set; }
        }

        Dictionary<string, NotifAppRule> notifAppRules;

        Dictionary<string, NotifAppRule> NotifAppRules
        {
            get
            {
                if (notifAppRules != null) return notifAppRules;
                notifAppRules = new Dictionary<string, NotifAppRule>(StringComparer.OrdinalIgnoreCase);
                try
                {
                    string raw = settings.Get(NotifAppRulesKey, "");
                    if (raw.Length > 0)
                    {
                        var saved = JsonSerializer.Deserialize<Dictionary<string, NotifAppRule>>(raw, NotifAppRulesJsonOptions);
                        if (saved != null)
                            foreach (var pair in saved)
                                if (!string.IsNullOrWhiteSpace(pair.Key) && pair.Value != null)
                                    notifAppRules[pair.Key] = pair.Value;
                    }
                }
                catch (Exception ex) { Log.Write("notif app rules: " + ex.Message); }
                return notifAppRules;
            }
        }

        static bool HasNotifApp(string id) { return !string.IsNullOrWhiteSpace(id) && id != "phone:"; }

        NotifAppRule RuleFor(string id)
        {
            NotifAppRule rule;
            return HasNotifApp(id) && NotifAppRules.TryGetValue(id, out rule) ? rule : null;
        }

        bool NotifAppHidden(NotifItem item)
        {
            var rule = RuleFor(item.AppId);
            return rule != null && rule.Mode == "hidden";
        }

        bool NotifAppSilenced(NotifItem item)
        {
            var rule = RuleFor(item.AppId);
            return rule != null && (rule.Mode == "hidden" || rule.Mode == "inbox" || rule.UntilUtc > DateTime.UtcNow.Ticks);
        }

        void SetNotifAppRule(string id, string name, string mode, int hours)
        {
            if (mode == null) NotifAppRules.Remove(id);
            else NotifAppRules[id] = new NotifAppRule
            {
                Name = name, Mode = mode, UntilUtc = hours > 0 ? DateTime.UtcNow.AddHours(hours).Ticks : 0
            };
            settings.Set(NotifAppRulesKey, Json.Encode(NotifAppRules));
            settings.Save();

            if (mode == "hidden")
            {
                notifs.RemoveAll(n => NotifAppHidden(n));
                unread = Math.Min(unread, notifs.Count);
            }
            heldToasts.RemoveAll(t => t.View == View.Notif && t.Data is NotifItem && NotifAppSilenced((NotifItem)t.Data));
            var active = toast != null && toast.View == View.Notif ? toast.Data as NotifItem : null;
            if (active != null && NotifAppSilenced(active)) DismissToast();
            notifWatcher.Refresh();
            Kick();
        }

        static string NotifAppName(NotifItem item)
        {
            return string.IsNullOrWhiteSpace(item.AppName) ? item.AppId : item.AppName;
        }

        List<KeyValuePair<string, string>> NotifApps(string selectedId, string selectedName)
        {
            var apps = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var pair in NotifAppRules)
                apps[pair.Key] = string.IsNullOrWhiteSpace(pair.Value.Name) ? pair.Key : pair.Value.Name;
            foreach (var item in notifs)
                if (HasNotifApp(item.AppId)) apps[item.AppId] = NotifAppName(item);
            if (HasNotifApp(selectedId)) apps[selectedId] = selectedName;
            var sorted = new List<KeyValuePair<string, string>>(apps);
            sorted.Sort((a, b) =>
            {
                bool aSelected = StringComparer.OrdinalIgnoreCase.Equals(a.Key, selectedId);
                bool bSelected = StringComparer.OrdinalIgnoreCase.Equals(b.Key, selectedId);
                if (aSelected != bSelected) return aSelected ? -1 : 1;
                int byName = StringComparer.CurrentCultureIgnoreCase.Compare(a.Value, b.Value);
                return byName != 0 ? byName : StringComparer.OrdinalIgnoreCase.Compare(a.Key, b.Key);
            });
            return sorted;
        }

        const string NotifDefault = "Ikuti pengaturan umum";
        const string NotifPauseHour = "Jeda banner 1 jam";
        const string NotifPauseEightHours = "Jeda banner 8 jam";
        const string NotifInbox = "Tampilkan di Pesan saja";
        const string NotifHidden = "Sembunyikan dari Deskmate";

        string NotifAppRuleStatus(string id)
        {
            var rule = RuleFor(id);
            if (rule != null)
            {
                if (rule.Mode == "inbox") return Lang.T(NotifInbox);
                if (rule.Mode == "hidden") return Lang.T(NotifHidden);
                if (rule.UntilUtc > DateTime.UtcNow.Ticks && rule.UntilUtc <= DateTime.MaxValue.Ticks)
                    return Lang.F("Banner dijeda sampai {0}", new DateTime(rule.UntilUtc, DateTimeKind.Utc).ToLocalTime().ToString("g"));
            }
            return Lang.T(NotifDefault);
        }

        ContextMenuStrip BuildNotifAppMenu(string id, string name, Action changed, bool fromIsland)
        {
            var menu = new ContextMenuStrip();
            menu.Items.Add(new ToolStripMenuItem(name) { Enabled = false });
            menu.Items.Add(new ToolStripSeparator());
            var rule = RuleFor(id);
            if (rule != null && rule.Mode == "pause" && rule.UntilUtc > DateTime.UtcNow.Ticks)
                menu.Items.Add(new ToolStripMenuItem(NotifAppRuleStatus(id)) { Enabled = false });
            var status = NotifAppRuleStatus(id);
            Action<string, string, int> add = (label, mode, hours) =>
            {
                var option = new ToolStripMenuItem(Lang.T(label)) { Checked = status == Lang.T(label) };
                option.Click += delegate
                {
                    SetNotifAppRule(id, name, mode, hours);
                    if (changed != null) changed();
                };
                menu.Items.Add(option);
            };
            add(NotifDefault, null, 0);
            add(NotifPauseHour, "pause", 1);
            add(NotifPauseEightHours, "pause", 8);
            add(NotifInbox, "inbox", 0);
            add(NotifHidden, "hidden", 0);
            bool windows = !id.StartsWith("phone:", StringComparison.OrdinalIgnoreCase);
            if (fromIsland || windows) menu.Items.Add(new ToolStripSeparator());
            if (fromIsland)
                menu.Items.Add(Lang.T("Aturan notifikasi aplikasi…"), null, delegate
                {
                    OpenPrefs();
                    prefs.ShowNotifApp(id, name);
                });
            if (windows)
                menu.Items.Add(Lang.T("Buka pengaturan Windows"), null, delegate
                {
                    try { Process.Start(new ProcessStartInfo("ms-settings:notifications") { UseShellExecute = true }); }
                    catch (Exception ex) { Log.Write("ms-settings: " + ex.Message); }
                });
            return menu;
        }

        Action NotifAppMenuAction(NotifItem item)
        {
            if (!HasNotifApp(item.AppId)) return null;
            return delegate
            {
                ShowIslandMenu(BuildNotifAppMenu(item.AppId, NotifAppName(item), delegate
                {
                    if (prefs != null && !prefs.IsDisposed) prefs.RefreshNotifApps();
                }, true));
            };
        }
    }
}
