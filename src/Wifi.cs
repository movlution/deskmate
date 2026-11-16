using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Security;
using System.Text;
using System.Threading;

namespace Deskmate
{
    // Nearby Wi-Fi networks, through Windows' Native Wifi API
    static class Wifi
    {
        public enum ScanStatus { Ok, NoAdapter, RadioOff, AccessDenied, Failed }

        public sealed class Net
        {
            public string Ssid = "", Profile = "";
            public int Signal, Auth, Cipher;
            public bool Secured, Connected, Known;
            public Guid Iface;
            // Needs a password Deskmate can save
            public bool Personal { get { return Auth == 4 || Auth == 7 || Auth == 9; } }
            public bool Open { get { return !Secured || Auth == 10; } }
        }

        [StructLayout(LayoutKind.Sequential)]
        struct ConnParams
        {
            public int Mode;
            [MarshalAs(UnmanagedType.LPWStr)] public string Profile;
            public IntPtr Ssid, Bssids;
            public int BssType;
            public uint Flags;
        }

        struct IfaceInfo
        {
            public Guid Id;
            public int State;
        }

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        delegate void WlanNotificationCallback(IntPtr data, IntPtr context);

        [DllImport("wlanapi.dll")] static extern int WlanOpenHandle(uint version, IntPtr reserved, out uint negotiated, out IntPtr handle);
        [DllImport("wlanapi.dll")] static extern int WlanCloseHandle(IntPtr handle, IntPtr reserved);
        [DllImport("wlanapi.dll")] static extern int WlanEnumInterfaces(IntPtr handle, IntPtr reserved, out IntPtr list);
        [DllImport("wlanapi.dll")] static extern int WlanScan(IntPtr handle, ref Guid iface, IntPtr ssid, IntPtr ie, IntPtr reserved);
        [DllImport("wlanapi.dll")] static extern int WlanGetAvailableNetworkList(IntPtr handle, ref Guid iface, uint flags, IntPtr reserved, out IntPtr list);
        [DllImport("wlanapi.dll")] static extern int WlanConnect(IntPtr handle, ref Guid iface, ref ConnParams p, IntPtr reserved);
        [DllImport("wlanapi.dll")] static extern int WlanDisconnect(IntPtr handle, ref Guid iface, IntPtr reserved);
        [DllImport("wlanapi.dll", CharSet = CharSet.Unicode)] static extern int WlanSetProfile(IntPtr handle, ref Guid iface, uint flags, string xml, string security, bool overwrite, IntPtr reserved, out uint reason);
        [DllImport("wlanapi.dll")] static extern int WlanRegisterNotification(IntPtr handle, uint source, bool ignoreDuplicate, WlanNotificationCallback callback, IntPtr context, IntPtr reserved, out uint prevSource);
        [DllImport("wlanapi.dll")] static extern int WlanQueryInterface(IntPtr handle, ref Guid iface, int opcode, IntPtr reserved, out uint dataSize, out IntPtr data, out int valueType);
        [DllImport("wlanapi.dll")] static extern void WlanFreeMemory(IntPtr p);

        static IntPtr Open()
        {
            IntPtr h;
            return Open(out h) == 0 ? h : IntPtr.Zero;
        }

        static int Open(out IntPtr handle)
        {
            uint version;
            int rc = WlanOpenHandle(2, IntPtr.Zero, out version, out handle);
            if (rc != 0) handle = IntPtr.Zero;
            return rc;
        }

        static List<IfaceInfo> Interfaces(IntPtr h)
        {
            var list = new List<IfaceInfo>();
            IntPtr p;
            if (WlanEnumInterfaces(h, IntPtr.Zero, out p) != 0) return list;
            try
            {
                int n = Marshal.ReadInt32(p);
                // Items: GUID, 256-char name, state
                for (int i = 0; i < n; i++)
                {
                    var b = new byte[16];
                    IntPtr item = new IntPtr(p.ToInt64() + 8 + i * 532);
                    Marshal.Copy(item, b, 0, 16);
                    list.Add(new IfaceInfo { Id = new Guid(b), State = Marshal.ReadInt32(item, 528) });
                }
            }
            finally { WlanFreeMemory(p); }
            return list;
        }

        static bool IsRadioOff(IntPtr h, IfaceInfo iface)
        {
            if (iface.State == 0) return true;
            Guid id = iface.Id;
            uint size;
            IntPtr p;
            int vt;
            if (WlanQueryInterface(h, ref id, 7 /* wlan_intf_opcode_radio_state */, IntPtr.Zero, out size, out p, out vt) != 0 || p == IntPtr.Zero)
                return false;
            try
            {
                int n = Marshal.ReadInt32(p, 0);
                if (n <= 0) return false;
                int count = Math.Min(n, 64);
                for (int i = 0; i < count; i++)
                {
                    int sw = Marshal.ReadInt32(p, 4 + i * 12 + 4);
                    int hw = Marshal.ReadInt32(p, 4 + i * 12 + 8);
                    if (sw != 2 && hw != 2) return false;
                }
                return true;
            }
            finally { WlanFreeMemory(p); }
        }

        // Ask the adapters for a fresh look without waiting
        public static void Scan()
        {
            ScanAndWait(0);
        }

        // Trigger a Wi-Fi scan and wait up to timeoutMs for completion notification
        public static ScanStatus ScanAndWait(int timeoutMs)
        {
            IntPtr h;
            int openRc = Open(out h);
            if (openRc != 0) return openRc == 5 ? ScanStatus.AccessDenied : ScanStatus.Failed;
            try
            {
                var ifaces = Interfaces(h);
                if (ifaces.Count == 0) return ScanStatus.NoAdapter;

                bool wait = timeoutMs > 0;
                ManualResetEvent done = wait ? new ManualResetEvent(false) : null;
                WlanNotificationCallback cb = null;
                bool registered = false;
                int pending = ifaces.Count;
                bool anyComplete = false;
                bool anyScanFail = false;

                try
                {
                    if (wait)
                    {
                        cb = delegate(IntPtr data, IntPtr ctx)
                        {
                            if (data == IntPtr.Zero) return;
                            int source = Marshal.ReadInt32(data, 0);
                            if (source != 8) return;
                            int code = Marshal.ReadInt32(data, 4);
                            if (code == 9 || code == 10)
                            {
                                if (code == 9) anyComplete = true;
                                else anyScanFail = true;
                                if (Interlocked.Decrement(ref pending) <= 0)
                                {
                                    try { done.Set(); } catch (ObjectDisposedException) { }
                                }
                            }
                        };
                        uint prev;
                        int regRc = WlanRegisterNotification(h, 8 /* WLAN_NOTIFICATION_SOURCE_ACM */, true, cb, IntPtr.Zero, IntPtr.Zero, out prev);
                        if (regRc == 5) return ScanStatus.AccessDenied;
                        registered = regRc == 0;
                    }

                    bool anyOk = false, anyDenied = false, anyOff = false;
                    foreach (var iface in ifaces)
                    {
                        if (IsRadioOff(h, iface)) anyOff = true;
                        var id = iface.Id;
                        int rc = WlanScan(h, ref id, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);
                        if (rc == 0)
                        {
                            anyOk = true;
                        }
                        else
                        {
                            if (wait && Interlocked.Decrement(ref pending) <= 0)
                            {
                                try { done.Set(); } catch (ObjectDisposedException) { }
                            }
                            if (rc == 5) anyDenied = true;
                            else if (rc == 5023 || rc == 1167 || rc == 4319 || iface.State == 0) anyOff = true;
                        }
                    }

                    if (anyOk && wait && registered && Interlocked.CompareExchange(ref pending, 0, 0) > 0)
                        done.WaitOne(timeoutMs);

                    if (anyOk) return (!anyComplete && anyScanFail) ? (anyOff ? ScanStatus.RadioOff : ScanStatus.Failed) : ScanStatus.Ok;
                    if (anyDenied) return ScanStatus.AccessDenied;
                    if (anyOff) return ScanStatus.RadioOff;
                    return ScanStatus.Failed;
                }
                finally
                {
                    if (registered)
                    {
                        uint prev;
                        WlanRegisterNotification(h, 0, true, null, IntPtr.Zero, IntPtr.Zero, out prev);
                    }
                    GC.KeepAlive(cb);
                    if (done != null) done.Dispose();
                }
            }
            finally { WlanCloseHandle(h, IntPtr.Zero); }
        }

        // One row per network name, connected first, in-range before saved, strongest first
        public static List<Net> List()
        {
            ScanStatus ignored;
            return List(out ignored);
        }

        public static List<Net> List(out ScanStatus status)
        {
            var seen = new Dictionary<string, Net>();
            IntPtr h;
            int openRc = Open(out h);
            if (openRc != 0)
            {
                status = openRc == 5 ? ScanStatus.AccessDenied : ScanStatus.Failed;
                return new List<Net>();
            }

            bool anyOk = false, anyDenied = false, anyOff = false, anyRadioOn = false;
            try
            {
                var ifaces = Interfaces(h);
                if (ifaces.Count == 0)
                {
                    status = ScanStatus.NoAdapter;
                    return new List<Net>();
                }

                foreach (var iface in ifaces)
                {
                    bool off = IsRadioOff(h, iface);
                    if (off) anyOff = true;
                    else anyRadioOn = true;

                    var id = iface.Id;
                    IntPtr p;
                    int rc = WlanGetAvailableNetworkList(h, ref id, 0, IntPtr.Zero, out p);
                    if (rc != 0)
                    {
                        if (rc == 5) anyDenied = true;
                        else if (rc == 5023 || rc == 1167 || rc == 4319 || iface.State == 0) anyOff = true;
                        continue;
                    }
                    anyOk = true;
                    try
                    {
                        int n = Marshal.ReadInt32(p);
                        for (int i = 0; i < n; i++)
                        {
                            var e = new IntPtr(p.ToInt64() + 8 + i * 628);
                            int len = Math.Min(32, Marshal.ReadInt32(e, 512));
                            if (len == 0) continue;
                            var raw = new byte[len];
                            Marshal.Copy(new IntPtr(e.ToInt64() + 516), raw, 0, len);
                            int flags = Marshal.ReadInt32(e, 620);
                            var net = new Net
                            {
                                Ssid = Encoding.UTF8.GetString(raw),
                                Profile = Marshal.PtrToStringUni(e) ?? "",
                                Signal = Marshal.ReadInt32(e, 604),
                                Secured = Marshal.ReadInt32(e, 608) != 0,
                                Auth = Marshal.ReadInt32(e, 612),
                                Cipher = Marshal.ReadInt32(e, 616),
                                Connected = (flags & 1) != 0,
                                Known = (flags & 2) != 0,
                                Iface = iface.Id,
                            };
                            Net had;
                            if (!seen.TryGetValue(net.Ssid, out had)) { seen[net.Ssid] = net; continue; }

                            bool takeMeta = (net.Connected && !had.Connected)
                                || (net.Connected == had.Connected && net.Known && !had.Known)
                                || (net.Connected == had.Connected && net.Known == had.Known && net.Signal > had.Signal);
                            int bestSig = Math.Max(had.Signal, net.Signal);
                            bool conn = had.Connected || net.Connected;
                            bool known = had.Known || net.Known;
                            string prof = net.Profile.Length > 0 && (had.Profile.Length == 0 || takeMeta) ? net.Profile : had.Profile;
                            int auth = net.Auth != 0 && (had.Auth == 0 || takeMeta) ? net.Auth : had.Auth;
                            int cipher = net.Cipher != 0 && (had.Cipher == 0 || takeMeta) ? net.Cipher : had.Cipher;

                            if (takeMeta)
                            {
                                net.Signal = bestSig;
                                net.Connected = conn;
                                net.Known = known;
                                net.Profile = prof;
                                net.Auth = auth;
                                net.Cipher = cipher;
                                seen[net.Ssid] = net;
                            }
                            else
                            {
                                had.Signal = bestSig;
                                had.Connected = conn;
                                had.Known = known;
                                had.Profile = prof;
                                had.Auth = auth;
                                had.Cipher = cipher;
                            }
                        }
                    }
                    finally { WlanFreeMemory(p); }
                }
            }
            finally { WlanCloseHandle(h, IntPtr.Zero); }

            if (anyDenied && !anyOk) status = ScanStatus.AccessDenied;
            else if (anyOff && !anyRadioOn) status = ScanStatus.RadioOff;
            else if (anyOk) status = ScanStatus.Ok;
            else if (anyOff) status = ScanStatus.RadioOff;
            else status = ScanStatus.Failed;

            var list = new List<Net>(seen.Values);
            list.Sort(delegate(Net a, Net b)
            {
                if (a.Connected != b.Connected) return a.Connected ? -1 : 1;
                bool aIn = a.Signal > 0, bIn = b.Signal > 0;
                if (aIn != bIn) return aIn ? -1 : 1;
                if (a.Known != b.Known) return a.Known ? -1 : 1;
                int c = b.Signal.CompareTo(a.Signal);
                return c != 0 ? c : string.Compare(a.Ssid, b.Ssid, StringComparison.OrdinalIgnoreCase);
            });
            return list;
        }

        // Saved networks join by profile; new ones get a profile first
        public static string Connect(Net net, string password)
        {
            var h = Open();
            if (h == IntPtr.Zero) return "wlan";
            try
            {
                var id = net.Iface;
                string profile = net.Known && net.Profile.Length > 0 ? net.Profile : null;
                if (profile == null)
                {
                    uint reason;
                    int set = WlanSetProfile(h, ref id, 0, ProfileXml(net, password), null, true, IntPtr.Zero, out reason);
                    if (set != 0) return "profile " + set + "/" + reason;
                    profile = net.Ssid;
                }
                var cp = new ConnParams { Mode = 0, Profile = profile, BssType = 1 };
                int r = WlanConnect(h, ref id, ref cp, IntPtr.Zero);
                return r == 0 ? null : "connect " + r;
            }
            finally { WlanCloseHandle(h, IntPtr.Zero); }
        }

        public static void Disconnect(Net net)
        {
            var h = Open();
            if (h == IntPtr.Zero) return;
            try { var id = net.Iface; WlanDisconnect(h, ref id, IntPtr.Zero); }
            finally { WlanCloseHandle(h, IntPtr.Zero); }
        }

        static string ProfileXml(Net net, string password)
        {
            string auth, enc;
            if (net.Auth == 10) { auth = "OWE"; enc = "AES"; }
            else if (!net.Secured) { auth = "open"; enc = "none"; }
            else if (net.Auth == 9) { auth = "WPA3SAE"; enc = "AES"; }
            else if (net.Auth == 4) { auth = "WPAPSK"; enc = net.Cipher == 2 ? "TKIP" : "AES"; }
            else { auth = "WPA2PSK"; enc = net.Cipher == 2 ? "TKIP" : "AES"; }
            var hex = new StringBuilder();
            foreach (byte b in Encoding.UTF8.GetBytes(net.Ssid)) hex.Append(b.ToString("X2"));
            string name = SecurityElement.Escape(net.Ssid);
            var x = new StringBuilder();
            x.Append("<?xml version=\"1.0\"?><WLANProfile xmlns=\"http://www.microsoft.com/networking/WLAN/profile/v1\">");
            x.Append("<name>").Append(name).Append("</name><SSIDConfig><SSID><hex>").Append(hex).Append("</hex><name>").Append(name).Append("</name></SSID></SSIDConfig>");
            x.Append("<connectionType>ESS</connectionType><connectionMode>auto</connectionMode><MSM><security><authEncryption>");
            x.Append("<authentication>").Append(auth).Append("</authentication><encryption>").Append(enc).Append("</encryption><useOneX>false</useOneX></authEncryption>");
            if (password != null && net.Secured && net.Auth != 10)
                x.Append("<sharedKey><keyType>passPhrase</keyType><protected>false</protected><keyMaterial>").Append(SecurityElement.Escape(password)).Append("</keyMaterial></sharedKey>");
            x.Append("</security></MSM></WLANProfile>");
            return x.ToString();
        }
    }
}
