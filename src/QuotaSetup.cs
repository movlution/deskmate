using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Text;
using System.Threading;

namespace Deskmate
{
    // statusLine per Claude profile, old kept
    static class StatusSetup
    {
        static string Store { get { return Path.Combine(Settings.Dir, "statusline.json"); } }

        // Forward slashes survive Git Bash
        public static string Command()
        {
            return "\"" + System.Reflection.Assembly.GetEntryAssembly().Location.Replace('\\', '/') + "\" --statusline";
        }

        static bool IsOurs(object line)
        {
            string c = Json.Str(line, "command");
            return c.StartsWith("\"") && c.EndsWith(".exe\" --statusline", StringComparison.OrdinalIgnoreCase);
        }

        public static bool Installed()
        {
            var profiles = HookSetup.Profiles();
            if (profiles.Count == 0) return false;
            foreach (var dir in profiles)
            {
                var root = HookSetup.Load(Path.Combine(dir, "settings.json"));
                if (root == null || !IsOurs(HookSetup.Get(root, "statusLine"))) return false;
            }
            return true;
        }

        public static string Install()
        {
            // Bash would expand these inside quotes
            if (Command().IndexOfAny("$`".ToCharArray()) >= 0) return Lang.T("Pindahkan Deskmate.exe ke folder tanpa $ atau `");
            var store = Load();
            int done = 0;
            foreach (var dir in HookSetup.Profiles())
            {
                string name = StatusLine.SafeName(Path.GetFileName(dir)), file = Path.Combine(dir, "settings.json");
                var root = File.Exists(file) ? HookSetup.Load(file) : new Dictionary<string, object>();
                if (root == null) return Lang.T("settings.json di ") + Path.GetFileName(dir) + Lang.T(" tidak terbaca");
                object cur;
                bool had = root.TryGetValue("statusLine", out cur);
                if (!IsOurs(cur))
                {
                    // Saved before settings.json changes
                    var rec = new Dictionary<string, object> { { "had", had } };
                    if (had) rec["value"] = cur;
                    store[name] = rec;
                    Save(store);
                }
                var mine = new Dictionary<string, object> { { "type", "command" }, { "command", Command() } };
                var old = Json.Obj(Json.Obj(store, name), "value");
                foreach (var k in new[] { "padding", "refreshInterval" })
                    if (old != null && old.ContainsKey(k)) mine[k] = old[k];
                HookSetup.Backup(file);
                root["statusLine"] = mine;
                HookSetup.Save(file, root);
                done++;
            }
            return done == 0 ? Lang.T("Profil Claude Code tidak ditemukan") : null;
        }

        // Puts back exactly what was there
        public static void Remove()
        {
            var store = Load();
            foreach (var dir in HookSetup.Profiles())
            {
                string name = StatusLine.SafeName(Path.GetFileName(dir)), file = Path.Combine(dir, "settings.json");
                var root = HookSetup.Load(file);
                if (root != null && IsOurs(HookSetup.Get(root, "statusLine")))
                {
                    var rec = Json.Obj(store, name);
                    HookSetup.Backup(file);
                    if (rec != null && true.Equals(HookSetup.Get(rec, "had"))) root["statusLine"] = HookSetup.Get(rec, "value");
                    else root.Remove("statusLine");
                    HookSetup.Save(file, root);
                }
                store.Remove(name);
            }
            Save(store);
        }

        // Antigravity CLI: own statusLine, old kept
        public const string AgyName = "antigravity", AgentsKey = "agent.quota.agents";
        static string AgyFile { get { return Path.Combine(Settings.Home, ".gemini", "antigravity-cli", "settings.json"); } }

        // agy adds cmd /c and escapes quotes
        static string AgyCommand() { return AgentSetup.Exe() + " --statusline"; }

        static bool AgyOurs(object line)
        {
            return Json.Str(line, "command").EndsWith(".exe --statusline", StringComparison.OrdinalIgnoreCase);
        }

        public static bool AgyInstalled()
        {
            var root = HookSetup.Load(AgyFile);
            var line = root == null ? null : HookSetup.Get(root, "statusLine");
            return AgyOurs(line) && true.Equals(HookSetup.Get(line, "enabled")) && Json.Str(line, "command") == AgyCommand();
        }

        public static void AgyInstall()
        {
            string file = AgyFile;
            if (!File.Exists(file) || AgyCommand().IndexOf('"') >= 0 || AgyInstalled()) return;
            var root = HookSetup.Load(file);
            if (root == null) return;
            var store = Load();
            object cur;
            bool had = root.TryGetValue("statusLine", out cur);
            if (!AgyOurs(cur))
            {
                var rec = new Dictionary<string, object> { { "had", had } };
                if (had) rec["value"] = cur;
                store[AgyName] = rec;
                Save(store);
            }
            HookSetup.Backup(file);
            root["statusLine"] = new Dictionary<string, object> { { "type", "command" }, { "command", AgyCommand() }, { "enabled", true } };
            HookSetup.Save(file, root);
        }

        public static void AgyRemove()
        {
            var store = Load();
            var root = HookSetup.Load(AgyFile);
            if (root != null && AgyOurs(HookSetup.Get(root, "statusLine")))
            {
                var rec = Json.Obj(store, AgyName);
                HookSetup.Backup(AgyFile);
                if (rec != null && true.Equals(HookSetup.Get(rec, "had"))) root["statusLine"] = HookSetup.Get(rec, "value");
                else root.Remove("statusLine");
                HookSetup.Save(AgyFile, root);
            }
            store.Remove(AgyName);
            Save(store);
        }

        // Client path: no JavaScriptSerializer load
        public static string Previous(string name)
        {
            object store = null;
            try { if (File.Exists(Store)) store = LiteJson.Parse(File.ReadAllText(Store, Encoding.UTF8)); }
            catch (Exception) { return null; }
            var value = Json.Obj(Json.Obj(store, name), "value");
            if (value == null || Json.Str(value, "type") != "command" || IsOurs(value)) return null;
            string cmd = Json.Str(value, "command");
            return cmd.Trim().Length > 0 ? cmd : null;
        }

        static Dictionary<string, object> Load()
        {
            return (File.Exists(Store) ? HookSetup.Load(Store) : null) ?? new Dictionary<string, object>();
        }

        static void Save(Dictionary<string, object> store)
        {
            Directory.CreateDirectory(Settings.Dir);
            if (store.Count == 0) { if (File.Exists(Store)) File.Delete(Store); return; }
            HookSetup.Save(Store, store);
        }
    }
}
