using System;
using System.Collections.Generic;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Threading;
using Windows.Devices.Bluetooth;
using Windows.Devices.Bluetooth.GenericAttributeProfile;
using Windows.Devices.Bluetooth.Rfcomm;
using Windows.Devices.Enumeration;
using Windows.Devices.Radios;
using Windows.Foundation;
namespace Deskmate
{
    // Battery that Bluetooth devices report to Windows
    static class BtBattery
    {
        [StructLayout(LayoutKind.Sequential)]
        struct DevInfo { public int cbSize; public Guid ClassGuid; public int DevInst; public IntPtr Reserved; }

        [StructLayout(LayoutKind.Sequential)]
        struct PropKey { public Guid fmtid; public int pid; }

        [DllImport("setupapi.dll", CharSet = CharSet.Unicode)] static extern IntPtr SetupDiGetClassDevs(IntPtr cls, string enumerator, IntPtr parent, int flags);
        [DllImport("setupapi.dll")] static extern bool SetupDiEnumDeviceInfo(IntPtr set, int index, ref DevInfo data);
        [DllImport("setupapi.dll", CharSet = CharSet.Unicode)] static extern bool SetupDiGetDeviceProperty(IntPtr set, ref DevInfo data, ref PropKey key, out int type, byte[] buffer, int size, out int required, int flags);
        [DllImport("setupapi.dll")] static extern bool SetupDiDestroyDeviceInfoList(IntPtr set);

        static readonly Guid BatteryKey = new Guid("104EA319-6EE2-4701-BD47-8DDBF425BBE5");
        static readonly Guid NameKey = new Guid("a45c254e-df1c-4efd-8020-67d146a850e0");

        // Friendly name to percent
        public static Dictionary<string, int> Read()
        {
            var found = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            IntPtr set = SetupDiGetClassDevs(IntPtr.Zero, null, IntPtr.Zero, 0x2 | 0x4);
            if (set == IntPtr.Zero || set == new IntPtr(-1)) return found;
            try
            {
                var buf = new byte[512];
                for (int i = 0; ; i++)
                {
                    var d = new DevInfo { cbSize = Marshal.SizeOf(typeof(DevInfo)) };
                    if (!SetupDiEnumDeviceInfo(set, i, ref d)) break;
                    var key = new PropKey { fmtid = BatteryKey, pid = 2 };
                    int type, need;
                    if (!SetupDiGetDeviceProperty(set, ref d, ref key, out type, buf, buf.Length, out need, 0) || type != 0x3) continue;
                    int pct = buf[0];
                    key = new PropKey { fmtid = NameKey, pid = 14 };
                    if (!SetupDiGetDeviceProperty(set, ref d, ref key, out type, buf, buf.Length, out need, 0) || type != 0x12) continue;
                    string name = System.Text.Encoding.Unicode.GetString(buf, 0, Math.Max(0, need - 2)).Trim();
                    if (name.Length > 0 && pct <= 100) found[name] = pct;
                }
            }
            finally { SetupDiDestroyDeviceInfoList(set); }
            return found;
        }
    }

    partial class Island
    {
        Dictionary<string, int> btBattery = new Dictionary<string, int>();
        DateTime btAskedUtc = DateTime.MinValue;
        string btWarned;
        bool btBusy;

        void TickBluetooth(bool now)
        {
            if (btBusy || (!now && (DateTime.UtcNow - btAskedUtc).TotalMinutes < 5)) return;
            btAskedUtc = DateTime.UtcNow;
            btBusy = true;
            ThreadPool.QueueUserWorkItem(delegate
            {
                Dictionary<string, int> got;
                try { got = BtBattery.Read(); }
                catch (Exception ex) { Log.Write("bt battery: " + ex.GetType().Name); got = new Dictionary<string, int>(); }
                Post(() => { btBusy = false; btBattery = got; OnBluetooth(); });
            });
        }

        // Endpoint "Headphones (Buds2)" holds the device name
        int BatteryFor(string endpoint, out string device)
        {
            device = null;
            if (string.IsNullOrEmpty(endpoint)) return -1;
            foreach (var kv in btBattery)
                if (endpoint.IndexOf(kv.Key, StringComparison.OrdinalIgnoreCase) >= 0) { device = kv.Key; return kv.Value; }
            return -1;
        }

        void OnBluetooth()
        {
            var dev = DefaultDevice();
            string name;
            int pct = BatteryFor(dev != null ? dev.Name : null, out name);
            if (pct < 0) return;
            if (pct > 20) { btWarned = null; return; }
            if (pct > 15 || btWarned == name) return;
            btWarned = name;
            ShowInfo(Ink.IcoBluetooth, Lang.F("Baterai {0} {1}%", name, pct), "", true);
        }

        // "· 70%" after an output, when known
        string BatteryTail(string endpoint)
        {
            string name;
            int pct = BatteryFor(endpoint, out name);
            return pct < 0 ? "" : "  ·  " + Ink.Percent(pct);
        }
    }

    sealed class BtDevice
    {
        public string Id = "", Name = "", Kind = "";
        public ulong Address;
        public bool Paired, Connected, CanPair, IsAudio;
        public int Battery = -1, Signal;
    }

    sealed class BtScanner : IDisposable
    {
        public event Action<List<BtDevice>, bool, string> Updated;

        static readonly string[] ExtraProps = new string[]
        {
            "System.Devices.Aep.IsConnected",
            "System.Devices.Aep.IsPaired",
            "System.Devices.Aep.CanPair",
            "System.Devices.Aep.DeviceAddress",
            "System.Devices.Aep.SignalStrength",
            "System.Devices.Aep.Category"
        };

        static readonly Guid AudioSinkService = new Guid("0000110B-0000-1000-8000-00805F9B34FB");
        static readonly Guid HandsfreeService = new Guid("0000111E-0000-1000-8000-00805F9B34FB");
        static readonly Guid HeadsetService = new Guid("00001108-0000-1000-8000-00805F9B34FB");

        [StructLayout(LayoutKind.Sequential)]
        struct SYSTEMTIME
        {
            public ushort wYear, wMonth, wDayOfWeek, wDay, wHour, wMinute, wSecond, wMilliseconds;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        struct BLUETOOTH_DEVICE_INFO
        {
            public int dwSize;
            public ulong Address;
            public uint ulClassofDevice;
            [MarshalAs(UnmanagedType.Bool)] public bool fConnected;
            [MarshalAs(UnmanagedType.Bool)] public bool fRemembered;
            [MarshalAs(UnmanagedType.Bool)] public bool fAuthenticated;
            public SYSTEMTIME stLastSeen;
            public SYSTEMTIME stLastUsed;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 248)] public string szName;
        }

        [StructLayout(LayoutKind.Sequential)]
        struct BLUETOOTH_DEVICE_SEARCH_PARAMS
        {
            public int dwSize;
            [MarshalAs(UnmanagedType.Bool)] public bool fReturnAuthenticated;
            [MarshalAs(UnmanagedType.Bool)] public bool fReturnRemembered;
            [MarshalAs(UnmanagedType.Bool)] public bool fReturnUnknown;
            [MarshalAs(UnmanagedType.Bool)] public bool fReturnConnected;
            [MarshalAs(UnmanagedType.Bool)] public bool fIssueInquiry;
            public byte cTimeoutMultiplier;
            public IntPtr hRadio;
        }

        [DllImport("bthprops.cpl")] static extern IntPtr BluetoothFindFirstDevice(ref BLUETOOTH_DEVICE_SEARCH_PARAMS pbtsp, ref BLUETOOTH_DEVICE_INFO pbtdi);
        [DllImport("bthprops.cpl")] [return: MarshalAs(UnmanagedType.Bool)] static extern bool BluetoothFindNextDevice(IntPtr hFind, ref BLUETOOTH_DEVICE_INFO pbtdi);
        [DllImport("bthprops.cpl")] [return: MarshalAs(UnmanagedType.Bool)] static extern bool BluetoothFindDeviceClose(IntPtr hFind);
        [DllImport("bthprops.cpl")] static extern int BluetoothGetDeviceInfo(IntPtr hRadio, ref BLUETOOTH_DEVICE_INFO pbtdi);
        [DllImport("bthprops.cpl")] static extern int BluetoothSetServiceState(IntPtr hRadio, ref BLUETOOTH_DEVICE_INFO pbtdi, ref Guid pGuidService, uint dwServiceFlags);

        sealed class WatcherSlot
        {
            public DeviceWatcher Watcher;
            public TypedEventHandler<DeviceWatcher, DeviceInformation> OnAdded;
            public TypedEventHandler<DeviceWatcher, DeviceInformationUpdate> OnUpdated;
            public TypedEventHandler<DeviceWatcher, DeviceInformationUpdate> OnRemoved;
            public TypedEventHandler<DeviceWatcher, object> OnStopped;

            public void Stop()
            {
                if (Watcher == null) return;
                try { if (OnAdded != null) Watcher.Added -= OnAdded; } catch (Exception) { }
                try { if (OnUpdated != null) Watcher.Updated -= OnUpdated; } catch (Exception) { }
                try { if (OnRemoved != null) Watcher.Removed -= OnRemoved; } catch (Exception) { }
                try { if (OnStopped != null) Watcher.Stopped -= OnStopped; } catch (Exception) { }
                try
                {
                    var st = Watcher.Status;
                    if (st == DeviceWatcherStatus.Started || st == DeviceWatcherStatus.EnumerationCompleted)
                        Watcher.Stop();
                }
                catch (Exception) { }
                Watcher = null;
            }
        }

        readonly object sync = new object();
        readonly Dictionary<string, BtDevice> rawById = new Dictionary<string, BtDevice>(StringComparer.OrdinalIgnoreCase);
        WatcherSlot classicWatcher, bleWatcher;
        Timer debounceTimer, stopTimer;
        int scanGen;
        bool scanning, disposed;
        Dictionary<string, int> cachedBattery = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        DateTime cachedBatteryUtc = DateTime.MinValue;

        public void StartScan(bool activeScan)
        {
            StopScan();
            int gen;
            lock (sync)
            {
                if (disposed) return;
                gen = ++scanGen;
                rawById.Clear();
                scanning = activeScan;
            }

            ThreadPool.QueueUserWorkItem(delegate
            {
                RunScan(gen, activeScan);
            });
        }

        public void StopScan()
        {
            WatcherSlot w1, w2;
            Timer dt, st;
            lock (sync)
            {
                scanGen++;
                scanning = false;
                w1 = classicWatcher; classicWatcher = null;
                w2 = bleWatcher; bleWatcher = null;
                dt = debounceTimer; debounceTimer = null;
                st = stopTimer; stopTimer = null;
            }
            if (w1 != null) w1.Stop();
            if (w2 != null) w2.Stop();
            if (dt != null) try { dt.Dispose(); } catch (Exception) { }
            if (st != null) try { st.Dispose(); } catch (Exception) { }
        }

        public void Dispose()
        {
            lock (sync)
            {
                if (disposed) return;
                disposed = true;
            }
            StopScan();
        }

        void RunScan(int gen, bool activeScan)
        {
            try
            {
                if (IsStale(gen)) return;

                Radio btRadio = null;
                try
                {
                    var radios = MediaService.Await(Radio.GetRadiosAsync(), 5000);
                    foreach (var r in radios)
                    {
                        if (r.Kind == RadioKind.Bluetooth) { btRadio = r; break; }
                    }
                }
                catch (Exception ex)
                {
                    if (IsAccessDenied(ex)) { FinishStatus(gen, "denied"); return; }
                    throw;
                }

                if (IsStale(gen)) return;
                if (btRadio == null) { FinishStatus(gen, "no_adapter"); return; }
                if (btRadio.State != RadioState.On) { FinishStatus(gen, "off"); return; }

                DeviceInformationCollection classicPaired = null, blePaired = null;
                try
                {
                    classicPaired = MediaService.Await(
                        DeviceInformation.FindAllAsync(BluetoothDevice.GetDeviceSelectorFromPairingState(true), ExtraProps, DeviceInformationKind.AssociationEndpoint),
                        10000);
                    blePaired = MediaService.Await(
                        DeviceInformation.FindAllAsync(BluetoothLEDevice.GetDeviceSelectorFromPairingState(true), ExtraProps, DeviceInformationKind.AssociationEndpoint),
                        10000);
                }
                catch (Exception ex)
                {
                    if (IsAccessDenied(ex)) { FinishStatus(gen, "denied"); return; }
                    throw;
                }

                if (IsStale(gen)) return;
                var bat = ReadBatteryFresh();
                List<BtDevice> initialSnapshot;
                lock (sync)
                {
                    if (disposed || gen != scanGen) return;
                    if (classicPaired != null)
                        foreach (var di in classicPaired) UpsertDeviceLocked(di, true);
                    if (blePaired != null)
                        foreach (var di in blePaired) UpsertDeviceLocked(di, true);
                    initialSnapshot = BuildSnapshotLocked(bat);
                }
                RaiseUpdated(initialSnapshot, activeScan, "");

                if (!activeScan) return;

                var w1 = CreateWatcherSlot(gen, BluetoothDevice.GetDeviceSelectorFromPairingState(false));
                var w2 = CreateWatcherSlot(gen, BluetoothLEDevice.GetDeviceSelectorFromPairingState(false));

                Timer dt = new Timer(delegate { OnDebounce(gen); }, null, Timeout.Infinite, Timeout.Infinite);
                Timer st = new Timer(delegate { OnStopTimeout(gen); }, null, 10000, Timeout.Infinite);

                lock (sync)
                {
                    if (disposed || gen != scanGen || !scanning)
                    {
                        dt.Dispose();
                        st.Dispose();
                        w1.Stop();
                        w2.Stop();
                        return;
                    }
                    classicWatcher = w1;
                    bleWatcher = w2;
                    debounceTimer = dt;
                    stopTimer = st;
                }

                bool startedAny = false;
                try { w1.Watcher.Start(); startedAny = true; } catch (Exception ex) { Log.Write("bt classic watcher: " + ex.Message); }
                try { w2.Watcher.Start(); startedAny = true; } catch (Exception ex) { Log.Write("bt ble watcher: " + ex.Message); }
                if (!startedAny)
                {
                    FinishStatus(gen, "");
                }
            }
            catch (Exception ex)
            {
                Log.Write("bt scan: " + ex.Message);
                if (!IsStale(gen)) FinishStatus(gen, IsAccessDenied(ex) ? "denied" : "");
            }
        }

        WatcherSlot CreateWatcherSlot(int gen, string selector)
        {
            var w = DeviceInformation.CreateWatcher(selector, ExtraProps, DeviceInformationKind.AssociationEndpoint);
            var slot = new WatcherSlot { Watcher = w };
            slot.OnAdded = delegate(DeviceWatcher sender, DeviceInformation di)
            {
                lock (sync)
                {
                    if (disposed || gen != scanGen || !scanning) return;
                    UpsertDeviceLocked(di, false);
                    TriggerDebounceLocked();
                }
            };
            slot.OnUpdated = delegate(DeviceWatcher sender, DeviceInformationUpdate upd)
            {
                lock (sync)
                {
                    if (disposed || gen != scanGen || !scanning) return;
                    ApplyUpdateLocked(upd);
                    TriggerDebounceLocked();
                }
            };
            slot.OnRemoved = delegate(DeviceWatcher sender, DeviceInformationUpdate upd)
            {
                lock (sync)
                {
                    if (disposed || gen != scanGen || !scanning) return;
                    if (upd != null && !string.IsNullOrEmpty(upd.Id))
                    {
                        BtDevice existing;
                        if (rawById.TryGetValue(upd.Id, out existing) && !existing.Paired)
                            rawById.Remove(upd.Id);
                        TriggerDebounceLocked();
                    }
                }
            };
            slot.OnStopped = delegate { };
            w.Added += slot.OnAdded;
            w.Updated += slot.OnUpdated;
            w.Removed += slot.OnRemoved;
            w.Stopped += slot.OnStopped;
            return slot;
        }

        void TriggerDebounceLocked()
        {
            if (debounceTimer != null)
            {
                try { debounceTimer.Change(250, Timeout.Infinite); } catch (ObjectDisposedException) { }
            }
        }

        void OnDebounce(int gen)
        {
            var bat = ReadBatteryCached();
            List<BtDevice> snap;
            lock (sync)
            {
                if (disposed || gen != scanGen || !scanning) return;
                snap = BuildSnapshotLocked(bat);
            }
            RaiseUpdated(snap, true, "");
        }

        void OnStopTimeout(int gen)
        {
            WatcherSlot w1, w2;
            Timer dt, st;
            lock (sync)
            {
                if (disposed || gen != scanGen || !scanning) return;
                scanning = false;
                w1 = classicWatcher; classicWatcher = null;
                w2 = bleWatcher; bleWatcher = null;
                dt = debounceTimer; debounceTimer = null;
                st = stopTimer; stopTimer = null;
            }
            if (w1 != null) w1.Stop();
            if (w2 != null) w2.Stop();
            if (dt != null) try { dt.Dispose(); } catch (Exception) { }
            if (st != null) try { st.Dispose(); } catch (Exception) { }

            var bat = ReadBatteryFresh();
            List<BtDevice> snap;
            lock (sync)
            {
                if (disposed || gen != scanGen) return;
                snap = BuildSnapshotLocked(bat);
            }
            RaiseUpdated(snap, false, "");
        }

        void FinishStatus(int gen, string statusCode)
        {
            WatcherSlot w1, w2;
            Timer dt, st;
            List<BtDevice> snap = null;
            lock (sync)
            {
                if (disposed || gen != scanGen) return;
                scanning = false;
                w1 = classicWatcher; classicWatcher = null;
                w2 = bleWatcher; bleWatcher = null;
                dt = debounceTimer; debounceTimer = null;
                st = stopTimer; stopTimer = null;
                if (statusCode.Length > 0) rawById.Clear();
                snap = statusCode.Length > 0 ? new List<BtDevice>() : BuildSnapshotLocked(ReadBatteryCached());
            }
            if (w1 != null) w1.Stop();
            if (w2 != null) w2.Stop();
            if (dt != null) try { dt.Dispose(); } catch (Exception) { }
            if (st != null) try { st.Dispose(); } catch (Exception) { }
            RaiseUpdated(snap, false, statusCode);
        }

        bool IsStale(int gen)
        {
            lock (sync) { return disposed || gen != scanGen; }
        }

        void RaiseUpdated(List<BtDevice> devices, bool isScanning, string statusCode)
        {
            var h = Updated;
            if (h != null) h(devices, isScanning, statusCode);
        }

        void UpsertDeviceLocked(DeviceInformation di, bool forcePaired)
        {
            if (di == null || string.IsNullOrEmpty(di.Id)) return;
            bool isBle = di.Id.StartsWith("BluetoothLE#", StringComparison.OrdinalIgnoreCase);
            var props = di.Properties;

            bool paired = forcePaired || (di.Pairing != null && di.Pairing.IsPaired) || GetBool(props, "System.Devices.Aep.IsPaired", false);
            bool canPair = (di.Pairing != null && di.Pairing.CanPair) || GetBool(props, "System.Devices.Aep.CanPair", false);
            bool connected = GetBool(props, "System.Devices.Aep.IsConnected", false);
            ulong addr = ParseAddress(GetObj(props, "System.Devices.Aep.DeviceAddress"), di.Id);
            int sig = GetInt(props, "System.Devices.Aep.SignalStrength", 0);
            string cat = GetCategory(GetObj(props, "System.Devices.Aep.Category"));
            string name = (di.Name ?? "").Trim();

            BtDevice dev;
            if (!rawById.TryGetValue(di.Id, out dev))
            {
                dev = new BtDevice { Id = di.Id, Kind = isBle ? "BLE" : "Classic" };
                rawById[di.Id] = dev;
            }
            if (name.Length > 0) dev.Name = name;
            if (addr != 0) dev.Address = addr;
            dev.Paired |= paired;
            dev.Connected = connected;
            dev.CanPair = canPair;
            if (sig != 0) dev.Signal = sig;
            dev.IsAudio = dev.IsAudio || IsAudioCategory(cat) || IsAudioName(dev.Name);
            if (dev.Paired && !isBle && dev.Address != 0)
            {
                BLUETOOTH_DEVICE_INFO info;
                if (TryGetClassicInfo(dev.Address, out info))
                {
                    if (dev.Name.Length == 0 && !string.IsNullOrEmpty(info.szName)) dev.Name = info.szName.Trim();
                    if (((info.ulClassofDevice >> 8) & 0x1F) == 0x04) dev.IsAudio = true;
                    if (info.fConnected) dev.Connected = true;
                }
            }
        }

        void ApplyUpdateLocked(DeviceInformationUpdate upd)
        {
            if (upd == null || string.IsNullOrEmpty(upd.Id)) return;
            BtDevice dev;
            if (!rawById.TryGetValue(upd.Id, out dev)) return;
            var props = upd.Properties;
            if (props == null) return;

            object v;
            if (props.TryGetValue("System.ItemNameDisplay", out v) && v is string)
            {
                string s = ((string)v).Trim();
                if (s.Length > 0) dev.Name = s;
            }
            if (props.TryGetValue("System.Devices.Aep.IsConnected", out v) && v is bool)
                dev.Connected = (bool)v;
            if (props.TryGetValue("System.Devices.Aep.IsPaired", out v) && v is bool)
                dev.Paired = (bool)v;
            if (props.TryGetValue("System.Devices.Aep.CanPair", out v) && v is bool)
                dev.CanPair = (bool)v;
            if (props.TryGetValue("System.Devices.Aep.DeviceAddress", out v))
            {
                ulong a = ParseAddress(v, upd.Id);
                if (a != 0) dev.Address = a;
            }
            if (props.TryGetValue("System.Devices.Aep.SignalStrength", out v) && v != null)
            {
                try { dev.Signal = Convert.ToInt32(v, CultureInfo.InvariantCulture); } catch (Exception) { }
            }
            if (props.TryGetValue("System.Devices.Aep.Category", out v))
            {
                string cat = GetCategory(v);
                if (IsAudioCategory(cat)) dev.IsAudio = true;
            }
            if (!dev.IsAudio && IsAudioName(dev.Name)) dev.IsAudio = true;
        }

        List<BtDevice> BuildSnapshotLocked(Dictionary<string, int> bat)
        {
            var byAddr = new Dictionary<ulong, BtDevice>();
            var noAddr = new List<BtDevice>();

            foreach (var src in rawById.Values)
            {
                bool realName = IsRealName(src.Name);
                if (!src.Paired && !realName) continue;

                var d = new BtDevice
                {
                    Id = src.Id,
                    Name = src.Name.Length > 0 ? src.Name : FormatAddress(src.Address),
                    Kind = src.Kind,
                    Address = src.Address,
                    Paired = src.Paired,
                    Connected = src.Connected,
                    CanPair = src.CanPair,
                    IsAudio = src.IsAudio,
                    Battery = MatchBattery(src.Name, bat),
                    Signal = src.Signal
                };

                if (d.Address == 0)
                {
                    noAddr.Add(d);
                    continue;
                }

                BtDevice had;
                if (!byAddr.TryGetValue(d.Address, out had))
                {
                    byAddr[d.Address] = d;
                    continue;
                }

                // Merge Classic & BLE representations of the same physical address
                bool dNamed = IsRealName(d.Name);
                bool hadNamed = IsRealName(had.Name);
                bool dClassic = !d.Id.StartsWith("BluetoothLE#", StringComparison.OrdinalIgnoreCase);
                bool hadClassic = !had.Id.StartsWith("BluetoothLE#", StringComparison.OrdinalIgnoreCase);

                bool preferD = (d.Paired != had.Paired && d.Paired)
                    || (d.Paired == had.Paired && d.Connected != had.Connected && d.Connected)
                    || (d.Paired == had.Paired && d.Connected == had.Connected && dNamed != hadNamed && dNamed)
                    || (d.Paired == had.Paired && d.Connected == had.Connected && dNamed == hadNamed && dClassic && !hadClassic);

                BtDevice win = preferD ? d : had;
                BtDevice lose = preferD ? had : d;
                win.Paired |= lose.Paired;
                win.Connected |= lose.Connected;
                win.CanPair |= lose.CanPair;
                win.IsAudio |= lose.IsAudio;
                if (!IsRealName(win.Name) && IsRealName(lose.Name)) win.Name = lose.Name;
                if (win.Battery < 0 && lose.Battery >= 0) win.Battery = lose.Battery;
                if (win.Signal == 0 && lose.Signal != 0) win.Signal = lose.Signal;
                if (dClassic || hadClassic) win.Kind = "Classic";
                byAddr[d.Address] = win;
            }

            var list = new List<BtDevice>(byAddr.Count + noAddr.Count);
            list.AddRange(byAddr.Values);
            list.AddRange(noAddr);
            list.Sort(delegate(BtDevice a, BtDevice b)
            {
                if (a.Connected != b.Connected) return a.Connected ? -1 : 1;
                if (a.Paired != b.Paired) return a.Paired ? -1 : 1;
                if (a.IsAudio != b.IsAudio) return a.IsAudio ? -1 : 1;
                int sig = b.Signal.CompareTo(a.Signal);
                return sig != 0 ? sig : string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase);
            });
            return list;
        }

        Dictionary<string, int> ReadBatteryFresh()
        {
            Dictionary<string, int> got;
            try { got = BtBattery.Read(); }
            catch (Exception) { got = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase); }
            lock (sync)
            {
                cachedBattery = got;
                cachedBatteryUtc = DateTime.UtcNow;
            }
            return got;
        }

        Dictionary<string, int> ReadBatteryCached()
        {
            lock (sync)
            {
                if ((DateTime.UtcNow - cachedBatteryUtc).TotalSeconds < 2)
                    return cachedBattery;
            }
            return ReadBatteryFresh();
        }

        static int MatchBattery(string name, Dictionary<string, int> bat)
        {
            if (string.IsNullOrEmpty(name) || bat == null || bat.Count == 0) return -1;
            int exact;
            if (bat.TryGetValue(name, out exact)) return exact;
            foreach (var kv in bat)
            {
                if (kv.Key.Length >= 3 && (name.IndexOf(kv.Key, StringComparison.OrdinalIgnoreCase) >= 0
                    || (name.Length >= 3 && kv.Key.IndexOf(name, StringComparison.OrdinalIgnoreCase) >= 0)))
                    return kv.Value;
            }
            return -1;
        }

        public static string Pair(BtDevice dev)
        {
            if (dev == null || string.IsNullOrEmpty(dev.Id)) return "Invalid device";
            try
            {
                var info = MediaService.Await(DeviceInformation.CreateFromIdAsync(dev.Id), 15000);
                if (info == null) return "Device not found";
                if (info.Pairing == null) return "Pairing not supported";
                if (info.Pairing.IsPaired) return null;
                if (!info.Pairing.CanPair) return "Device cannot be paired";

                DevicePairingResult res = null;
                var custom = info.Pairing.Custom;
                if (custom != null)
                {
                    TypedEventHandler<DeviceInformationCustomPairing, DevicePairingRequestedEventArgs> handler =
                        delegate(DeviceInformationCustomPairing sender, DevicePairingRequestedEventArgs args)
                        {
                            if (args.PairingKind == DevicePairingKinds.ConfirmOnly || args.PairingKind == DevicePairingKinds.ConfirmPinMatch)
                                args.Accept();
                        };
                    custom.PairingRequested += handler;
                    try
                    {
                        res = MediaService.Await(
                            custom.PairAsync(DevicePairingKinds.ConfirmOnly | DevicePairingKinds.ConfirmPinMatch),
                            30000);
                    }
                    catch (Exception)
                    {
                        res = null;
                    }
                    finally
                    {
                        custom.PairingRequested -= handler;
                    }
                }

                if (res == null || (res.Status != DevicePairingResultStatus.Paired
                    && res.Status != DevicePairingResultStatus.AlreadyPaired
                    && res.Status != DevicePairingResultStatus.AccessDenied
                    && res.Status != DevicePairingResultStatus.OperationAlreadyInProgress))
                {
                    res = MediaService.Await(info.Pairing.PairAsync(), 30000);
                }

                if (res.Status == DevicePairingResultStatus.Paired || res.Status == DevicePairingResultStatus.AlreadyPaired)
                    return null;
                return "Pair failed: " + res.Status;
            }
            catch (Exception ex)
            {
                return ex.Message;
            }
        }

        public static string Unpair(BtDevice dev)
        {
            if (dev == null || string.IsNullOrEmpty(dev.Id)) return "Invalid device";
            try
            {
                var info = MediaService.Await(DeviceInformation.CreateFromIdAsync(dev.Id), 10000);
                if (info == null) return "Device not found";
                if (info.Pairing == null) return "Unpairing not supported";
                if (!info.Pairing.IsPaired) return null;
                var res = MediaService.Await(info.Pairing.UnpairAsync(), 15000);
                if (res.Status == DeviceUnpairingResultStatus.Unpaired || res.Status == DeviceUnpairingResultStatus.AlreadyUnpaired)
                    return null;
                return "Unpair failed: " + res.Status;
            }
            catch (Exception ex)
            {
                return ex.Message;
            }
        }

        public static string Connect(BtDevice dev)
        {
            if (dev == null || (string.IsNullOrEmpty(dev.Id) && dev.Address == 0)) return "Invalid device";
            bool isBleOnly = string.Equals(dev.Kind, "BLE", StringComparison.OrdinalIgnoreCase)
                || (!string.IsNullOrEmpty(dev.Id) && dev.Id.StartsWith("BluetoothLE#", StringComparison.OrdinalIgnoreCase));
            string lastErr = null;
            bool triedClassicAudio = false;

            try
            {
                BLUETOOTH_DEVICE_INFO btdi = new BLUETOOTH_DEVICE_INFO();
                bool hasClassic = dev.Address != 0 && TryGetClassicInfo(dev.Address, out btdi);
                if (hasClassic || (!isBleOnly && dev.Address != 0))
                {
                    if (hasClassic)
                    {
                        Guid[] services = new Guid[] { AudioSinkService, HandsfreeService, HeadsetService };
                        for (int i = 0; i < services.Length; i++)
                        {
                            Guid g = services[i];
                            int rc = BluetoothSetServiceState(IntPtr.Zero, ref btdi, ref g, 1);
                            if (rc == 0) triedClassicAudio = true;
                            else if (lastErr == null) lastErr = "Win32 " + rc;
                        }
                    }

                    if (!IsDeviceConnected(dev))
                    {
                        BluetoothDevice bt = null;
                        try
                        {
                            if (!string.IsNullOrEmpty(dev.Id) && dev.Id.StartsWith("Bluetooth#", StringComparison.OrdinalIgnoreCase))
                                bt = MediaService.Await(BluetoothDevice.FromIdAsync(dev.Id), 10000);
                            else if (dev.Address != 0)
                                bt = MediaService.Await(BluetoothDevice.FromBluetoothAddressAsync(dev.Address), 10000);

                            if (bt != null)
                            {
                                if (bt.ConnectionStatus != BluetoothConnectionStatus.Connected)
                                {
                                    var rf = MediaService.Await(bt.GetRfcommServicesAsync(BluetoothCacheMode.Uncached), 10000);
                                    if (rf != null)
                                    {
                                        if (rf.Services != null)
                                            foreach (var s in rf.Services) s.Dispose();
                                        if (rf.Error != BluetoothError.Success && lastErr == null)
                                            lastErr = "Bluetooth " + rf.Error;
                                    }
                                }
                                if (bt.ConnectionStatus == BluetoothConnectionStatus.Connected)
                                    return null;
                            }
                        }
                        catch (Exception ex) { if (lastErr == null) lastErr = ex.Message; }
                        finally { if (bt != null) bt.Dispose(); }
                    }
                }

                if (isBleOnly && !IsDeviceConnected(dev))
                {
                    BluetoothLEDevice ble = null;
                    try
                    {
                        if (!string.IsNullOrEmpty(dev.Id) && dev.Id.StartsWith("BluetoothLE#", StringComparison.OrdinalIgnoreCase))
                            ble = MediaService.Await(BluetoothLEDevice.FromIdAsync(dev.Id), 10000);
                        else if (dev.Address != 0)
                            ble = MediaService.Await(BluetoothLEDevice.FromBluetoothAddressAsync(dev.Address), 10000);

                        if (ble != null)
                        {
                            var gatt = MediaService.Await(ble.GetGattServicesAsync(BluetoothCacheMode.Uncached), 10000);
                            if (gatt != null && gatt.Services != null)
                                foreach (var s in gatt.Services) s.Dispose();
                            if (ble.ConnectionStatus == BluetoothConnectionStatus.Connected || (gatt != null && gatt.Status == GattCommunicationStatus.Success))
                                return null;
                            if (gatt != null && lastErr == null) lastErr = "GATT " + gatt.Status;
                        }
                    }
                    catch (Exception ex) { if (lastErr == null) lastErr = ex.Message; }
                    finally { if (ble != null) ble.Dispose(); }
                }

                int attempts = triedClassicAudio ? 8 : 2;
                for (int i = 0; i < attempts; i++)
                {
                    if (IsDeviceConnected(dev)) return null;
                    Thread.Sleep(250);
                }
                return IsDeviceConnected(dev) ? null : (lastErr ?? "Could not connect");
            }
            catch (Exception ex)
            {
                return ex.Message;
            }
        }

        public static string Disconnect(BtDevice dev)
        {
            if (dev == null || (string.IsNullOrEmpty(dev.Id) && dev.Address == 0)) return "Invalid device";
            bool isBleOnly = string.Equals(dev.Kind, "BLE", StringComparison.OrdinalIgnoreCase)
                || (!string.IsNullOrEmpty(dev.Id) && dev.Id.StartsWith("BluetoothLE#", StringComparison.OrdinalIgnoreCase));

            try
            {
                BLUETOOTH_DEVICE_INFO btdi;
                if (dev.Address != 0 && TryGetClassicInfo(dev.Address, out btdi))
                {
                    Guid[] services = new Guid[] { AudioSinkService, HandsfreeService, HeadsetService };
                    for (int i = 0; i < services.Length; i++)
                    {
                        Guid g = services[i];
                        BluetoothSetServiceState(IntPtr.Zero, ref btdi, ref g, 0);
                    }
                }

                if (isBleOnly && !string.IsNullOrEmpty(dev.Id) && dev.Id.StartsWith("BluetoothLE#", StringComparison.OrdinalIgnoreCase))
                {
                    BluetoothLEDevice ble = null;
                    try
                    {
                        ble = MediaService.Await(BluetoothLEDevice.FromIdAsync(dev.Id), 5000);
                    }
                    catch (Exception) { }
                    finally { if (ble != null) ble.Dispose(); }
                }

                for (int i = 0; i < 6; i++)
                {
                    if (!IsDeviceConnected(dev)) return null;
                    Thread.Sleep(250);
                }
                return !IsDeviceConnected(dev) ? null : "Could not disconnect";
            }
            catch (Exception ex)
            {
                return ex.Message;
            }
        }

        static bool IsDeviceConnected(BtDevice dev)
        {
            if (dev.Address != 0)
            {
                BLUETOOTH_DEVICE_INFO info;
                if (TryGetClassicInfo(dev.Address, out info) && info.fConnected)
                    return true;
            }
            if (!string.IsNullOrEmpty(dev.Id))
            {
                try
                {
                    var di = MediaService.Await(
                        DeviceInformation.CreateFromIdAsync(dev.Id, new string[] { "System.Devices.Aep.IsConnected" }, DeviceInformationKind.AssociationEndpoint),
                        5000);
                    if (di != null && GetBool(di.Properties, "System.Devices.Aep.IsConnected", false))
                        return true;
                }
                catch (Exception) { }
            }
            return false;
        }

        static bool TryGetClassicInfo(ulong address, out BLUETOOTH_DEVICE_INFO info)
        {
            info = new BLUETOOTH_DEVICE_INFO();
            info.dwSize = Marshal.SizeOf(typeof(BLUETOOTH_DEVICE_INFO));
            info.Address = address;
            if (BluetoothGetDeviceInfo(IntPtr.Zero, ref info) == 0)
                return true;

            var sp = new BLUETOOTH_DEVICE_SEARCH_PARAMS
            {
                dwSize = Marshal.SizeOf(typeof(BLUETOOTH_DEVICE_SEARCH_PARAMS)),
                fReturnAuthenticated = true,
                fReturnRemembered = true,
                fReturnConnected = true,
                fReturnUnknown = false,
                fIssueInquiry = false,
                cTimeoutMultiplier = 0,
                hRadio = IntPtr.Zero
            };
            var probe = new BLUETOOTH_DEVICE_INFO { dwSize = Marshal.SizeOf(typeof(BLUETOOTH_DEVICE_INFO)) };
            IntPtr hFind = BluetoothFindFirstDevice(ref sp, ref probe);
            if (hFind == IntPtr.Zero) return false;
            try
            {
                do
                {
                    if (probe.Address == address) { info = probe; return true; }
                }
                while (BluetoothFindNextDevice(hFind, ref probe));
            }
            finally { BluetoothFindDeviceClose(hFind); }
            return false;
        }

        static object GetObj(IReadOnlyDictionary<string, object> props, string key)
        {
            if (props == null) return null;
            object v;
            return props.TryGetValue(key, out v) ? v : null;
        }

        static bool GetBool(IReadOnlyDictionary<string, object> props, string key, bool fallback)
        {
            object v = GetObj(props, key);
            return v is bool ? (bool)v : fallback;
        }

        static int GetInt(IReadOnlyDictionary<string, object> props, string key, int fallback)
        {
            object v = GetObj(props, key);
            if (v == null) return fallback;
            try { return Convert.ToInt32(v, CultureInfo.InvariantCulture); }
            catch (Exception) { return fallback; }
        }

        static string GetCategory(object v)
        {
            if (v == null) return "";
            if (v is string) return (string)v;
            var arr = v as IEnumerable<string>;
            if (arr != null)
            {
                var parts = new List<string>(arr);
                return string.Join(",", parts.ToArray());
            }
            return v.ToString() ?? "";
        }

        static bool IsAudioCategory(string cat)
        {
            if (string.IsNullOrEmpty(cat)) return false;
            return cat.IndexOf("Audio", StringComparison.OrdinalIgnoreCase) >= 0
                || cat.IndexOf("Headphone", StringComparison.OrdinalIgnoreCase) >= 0
                || cat.IndexOf("Headset", StringComparison.OrdinalIgnoreCase) >= 0
                || cat.IndexOf("Speaker", StringComparison.OrdinalIgnoreCase) >= 0
                || cat.IndexOf("Earbud", StringComparison.OrdinalIgnoreCase) >= 0
                || cat.IndexOf("Microphone", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        static bool IsAudioName(string name)
        {
            if (string.IsNullOrEmpty(name)) return false;
            return name.IndexOf("Headphone", StringComparison.OrdinalIgnoreCase) >= 0
                || name.IndexOf("Headset", StringComparison.OrdinalIgnoreCase) >= 0
                || name.IndexOf("Speaker", StringComparison.OrdinalIgnoreCase) >= 0
                || name.IndexOf("Earbud", StringComparison.OrdinalIgnoreCase) >= 0
                || name.IndexOf("AirPods", StringComparison.OrdinalIgnoreCase) >= 0
                || name.IndexOf("Buds", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        static ulong ParseAddress(object v, string id)
        {
            if (v is ulong) return (ulong)v;
            if (v is long) return unchecked((ulong)(long)v);
            var bytes = v as byte[];
            if (bytes != null && bytes.Length >= 6)
            {
                ulong a = 0;
                for (int i = 0; i < 6; i++) a = (a << 8) | bytes[i];
                return a;
            }
            if (v is string)
            {
                ulong a = ParseMacString((string)v);
                if (a != 0) return a;
            }
            if (!string.IsNullOrEmpty(id))
            {
                int dash = id.LastIndexOf('-');
                if (dash >= 0 && dash + 1 < id.Length)
                {
                    ulong a = ParseMacString(id.Substring(dash + 1));
                    if (a != 0) return a;
                }
            }
            return 0;
        }

        static ulong ParseMacString(string s)
        {
            if (string.IsNullOrEmpty(s)) return 0;
            var hex = new char[12];
            int n = 0;
            for (int i = 0; i < s.Length; i++)
            {
                char c = s[i];
                if ((c >= '0' && c <= '9') || (c >= 'a' && c <= 'f') || (c >= 'A' && c <= 'F'))
                {
                    if (n >= 12) return 0;
                    hex[n++] = c;
                }
                else if (c != ':' && c != '-' && c != ' ')
                {
                    return 0;
                }
            }
            if (n != 12) return 0;
            ulong val;
            return ulong.TryParse(new string(hex), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out val) ? val : 0;
        }

        static string FormatAddress(ulong addr)
        {
            if (addr == 0) return "";
            string h = addr.ToString("X12", CultureInfo.InvariantCulture);
            return h.Substring(0, 2) + ":" + h.Substring(2, 2) + ":" + h.Substring(4, 2) + ":" + h.Substring(6, 2) + ":" + h.Substring(8, 2) + ":" + h.Substring(10, 2);
        }

        static bool IsRealName(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return false;
            string s = name.Trim();
            if (s.Length == 0) return false;
            if (ParseMacString(s) != 0) return false;
            if (s.StartsWith("Bluetooth ", StringComparison.OrdinalIgnoreCase) && ParseMacString(s.Substring(10).Trim()) != 0)
                return false;
            return true;
        }

        static bool IsAccessDenied(Exception ex)
        {
            if (ex == null) return false;
            if (ex is UnauthorizedAccessException) return true;
            if (Marshal.GetHRForException(ex) == unchecked((int)0x80070005)) return true;
            return ex.InnerException != null && IsAccessDenied(ex.InnerException);
        }
    }
}
