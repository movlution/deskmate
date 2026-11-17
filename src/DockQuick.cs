using System;
using System.Collections.Generic;
using System.Drawing;
using System.Threading;
using System.Windows.Forms;

namespace Deskmate
{
    partial class Island
    {
        // Quick settings card (Win+A)
        sealed partial class DockWindow
        {
            public void OpenQuick(string subView)
            {
                if (closing) return;
                CloseCard();
                labelShow.Target = 0;
                Reveal(true);
                var qs = sheet as QuickSheet;
                if (qs == null)
                {
                    if (sheet != null) SheetDone(sheet);
                    qs = new QuickSheet(isle, this);
                    sheet = qs;
                    sheet.OpenAt(HitScreenRect(SheetAnchor("quick")), Full ? Rectangle.Empty : ToScreen(centerPill), mon, BarTopShownScreen(), scale);
                }
                else if (!Dry)
                {
                    qs.Activate();
                }
                if (subView == "wifi") qs.OpenWifi();
                else if (subView == "bt") qs.OpenBluetooth();
            }
        }

        sealed class QuickSheet : GlassSheet
        {
            readonly System.Windows.Forms.Timer refresh = new System.Windows.Forms.Timer { Interval = 400 };
            RectangleF volRect, brightRect;
            // Wi-Fi list, inside the same card
            bool wifiView, scanning;
            List<Wifi.Net> nets = new List<Wifi.Net>();
            Wifi.ScanStatus wifiScanStatus = Wifi.ScanStatus.Ok;
            Wifi.Net asking;
            string password = "", status;
            int first;

            // Bluetooth list, inside the same card
            bool btView, btScanning, btBusy;
            readonly BtScanner btScanner = new BtScanner();
            List<BtDevice> btDevices = new List<BtDevice>();
            string btStatusCode = "", btStatus;
            int firstBt;

            const float Pad = 20, Foot = 56, RowH = 50, ListTop = 56;
            const float TileTop = 20, TilePitch = 82, TileDiameter = 48;
            const float ViewHeight = 2 * TilePitch, MaxTileScroll = 2 * TilePitch;
            const float GridBottom = TileTop + ViewHeight + 12;
            float tileScroll, scrollGrab;

            public QuickSheet(Island isle, DockWindow dock) : base(isle, dock, "quick", "Deskmate Quick")
            {
                W = 360;
                // Radios change on other threads
                refresh.Tick += delegate { Render(); };
                btScanner.Updated += delegate (List<BtDevice> list, bool scan, string code)
                {
                    Post(delegate
                    {
                        btDevices = SortBt(list);
                        btScanning = scan;
                        btStatusCode = code ?? "";
                        Render();
                    });
                };
            }

            bool HasBright { get { return isle.brightLevel >= 0; } }

            protected override void Opening(Rectangle mon, int barTop)
            {
                H = GridBottom + 48 + 40 + (HasBright ? 56 : 0) + Foot + 12;
                float availableHeight = (barTop - mon.Top) / scale - 40;
                if (availableHeight < H && barTop > mon.Top)
                    scale = (barTop - mon.Top) / (H + 40);
                isle.volume.RefreshDevices();
                refresh.Start();
            }

            protected override void Teardown()
            {
                refresh.Stop();
                refresh.Dispose();
                try { btScanner.StopScan(); } catch (Exception) { }
                try { btScanner.Dispose(); } catch (Exception) { }
            }

            protected override void Backdrop(RectangleF r) { Shelf(r, Foot); }

            protected override void Draw()
            {
                if (wifiView) { DrawWifi(); return; }
                if (btView) { DrawBluetooth(); return; }
                DrawTileGrid();

                float y = GridBottom;
                volRect = new RectangleF(Pad, y, W - Pad * 2, 48);
                char volGlyph = isle.muted ? Ink.IcoMute : isle.vol < 0.01f ? Ink.IcoVol0 : isle.vol < 0.34f ? Ink.IcoVol1 : isle.vol < 0.67f ? Ink.IcoVol2 : Ink.IcoVol3;
                Slider(volRect, "vol", isle.muted ? 0 : isle.vol, volGlyph, Lang.T("Suara"), Ink.Percent((int)Math.Round(isle.vol * 100)), "mute");
                y += 52;
                DrawDevice(new RectangleF(Pad, y, W - Pad * 2, 34));
                y += 40;
                if (HasBright)
                {
                    brightRect = new RectangleF(Pad, y, W - Pad * 2, 48);
                    Slider(brightRect, "bright", isle.brightLevel / 100f, Ink.IcoSun, Lang.T("Layar"), Ink.Percent(isle.brightLevel), null);
                }
                DrawFoot();
            }

            void DrawTileGrid()
            {
                var viewport = new RectangleF(0, TileTop, W, ViewHeight);
                var clip = g.Save();
                g.IntersectClip(viewport);
                DrawControlTiles();
                DrawMoreTiles();
                g.Restore(clip);

                var rail = new RectangleF(W - 15, TileTop, 13, ViewHeight);
                Hit(rail, "tileScroll");
                bool hover = hotId == "tileScroll" || pressId == "tileScroll";
                if (hover) p.FillRound(new RectangleF(W - 10, TileTop, 4, ViewHeight), 2, p.T.FillHover);
                p.FillRound(new RectangleF(W - 10, ScrollThumbTop, 4, ViewHeight / 2), 2, hover ? p.T.Sub : p.T.Faint);
            }

            float ScrollThumbTop { get { return TileTop + ViewHeight / 2 * tileScroll / MaxTileScroll; } }

            void ScrollTiles(float offset) { tileScroll = Math.Max(0, Math.Min(MaxTileScroll, offset)); }

            void DrawControlTiles()
            {
                var radios = isle.radios;
                var net = isle.net.Current;
                string wifi = net.Wifi && net.Any && net.Captive ? Lang.T("Perlu login") : net.Wifi && net.Any && !net.Online ? Lang.T("Tanpa internet")
                            : net.Wifi && net.Name.Length > 0 ? net.Name : Lang.T("Wi-Fi");
                Tile(0, Ink.IcoWifi, radios.WifiOn, radios.HasWifi, Clip(wifi), "wifi", "wifiList");
                Tile(1, Ink.IcoBluetooth, radios.BluetoothOn, radios.HasBluetooth, Lang.T("Bluetooth"), "bt", "btMore");
                Tile(2, Ink.IcoQuiet, isle.QuietSwitch, true, Lang.T("Senyap"), "quiet");
                Tile(3, IcoDarkMode, isle.winDark, true, Lang.T("Gelap"), "dark");
                Tile(4, IcoAwake, isle.AwakeOn, true, isle.AwakeLabel(), "awake", "awakeMore");
                Tile(5, isle.micMuted ? IcoMicOff : Ink.IcoMic, isle.micPresent && !isle.micMuted, isle.micPresent, Lang.T("Mikrofon"), "mic");
            }

            void DrawMoreTiles()
            {
                Tile(6, '\uE703', false, true, "Cast", "cast");
                Tile(7, '\uEBC6', false, true, Lang.T("Proyeksi"), "project");
                Tile(8, Ink.IcoVol3, false, true, Lang.T("Keluaran audio"), "output");
                Tile(9, Ink.IcoSun, false, true, Lang.T("Lampu malam"), "nightLight");
                Tile(10, '\uEBB9', false, true, Lang.T("Hemat daya"), "batterySaver");
                Tile(11, '\uE704', false, true, Lang.T("Hotspot seluler"), "hotspot");
            }

            void Tile(int index, char glyph, bool on, bool available, string label, string id, string more = null)
            {
                float x = (index % 3 + 0.5f) * W / 3;
                float rowTop = TileTop + index / 3 * TilePitch - tileScroll;
                if (rowTop + TilePitch <= TileTop || rowTop >= TileTop + ViewHeight) return;
                float y = rowTop + 6 + TileDiameter / 2;
                int firstHit = hits.Count;
                Toggle(x, y, TileDiameter, glyph, on, available, label, id, more);
                var viewport = new RectangleF(0, TileTop, W, ViewHeight);
                for (int i = hits.Count - 1; i >= firstHit; i--)
                {
                    var hit = hits[i];
                    var visible = RectangleF.Intersect(hit.Key, viewport);
                    if (visible.Height <= 0) hits.RemoveAt(i);
                    else hits[i] = new KeyValuePair<RectangleF, string>(visible, hit.Value);
                }
            }

            static string Clip(string s) { return s.Length > 12 ? s.Substring(0, 11) + "…" : s; }

            // Where the sound goes; click to switch
            void DrawDevice(RectangleF r)
            {
                AudioDevice cur = null;
                foreach (var d in isle.audioDevices) if (d.Id == isle.audioDefault) cur = d;
                Hit(r, "device");
                if (hotId == "device") p.FillRound(r, 10, pressId == "device" ? p.T.FillPressed : p.T.FillHover);
                float cy = r.Y + r.Height / 2;
                p.Icon(DeviceGlyph(cur != null ? cur.Form : 1), 14f, p.T.Sub, r.X + 25, cy);
                p.Text(cur != null ? cur.Name : Lang.T("Perangkat suara"), Ink.Small(12f), p.T.Text, r.X + 46, cy, r.Width - 80, StringAlignment.Near);
                p.Icon((char)0xE70D, 10f, p.T.Sub, r.Right - 18, cy);
            }

            void DrawFoot()
            {
                float cy = H - Foot / 2;
                if (isle.hasBattery)
                {
                    bool low = isle.battery <= 20 && !isle.onAc;
                    p.Battery(Pad + 4, cy, isle.battery / 100f, low ? p.T.Negative : p.T.Text, isle.onAc);
                    p.Text(Ink.Percent(isle.battery), Ink.Semi(13f), low ? p.T.Negative : p.T.Text, Pad + 40, cy, 60, StringAlignment.Near);
                    p.Text(isle.onAc ? Lang.T("mengisi daya") : Lang.T("Baterai"), Ink.Small(11.5f), p.T.Sub, Pad + 84, cy, 120, StringAlignment.Near);
                }
                Button(new RectangleF(W - Pad - 38, cy - 19, 38, 38), "gear", (char)0xE713, Lang.T("Pengaturan Windows"));
                Button(new RectangleF(W - Pad - 80, cy - 19, 38, 38), "deskmate", (char)0xE8A9, Lang.T("Pengaturan Deskmate"));
            }

            // Networks nearby; saved ones join on a click, new ones ask a password
            void DrawWifi()
            {
                Button(new RectangleF(Pad - 8, 12, 34, 34), "back", (char)0xE72B, Lang.T("Kembali"));
                Caption(Lang.T("Wi-Fi"), Pad + 34, 29);
                if (scanning) p.Text(Lang.T("Mencari…"), Ink.Small(11.5f), p.T.Sub, W - Pad - 38, 29, 90, StringAlignment.Far);
                Button(new RectangleF(W - Pad - 30, 12, 34, 34), "rescan", (char)0xE72C, Lang.T("Cari ulang"));
                float bottom = H - Foot - 6;
                bool noAdapter = !isle.radios.HasWifi || wifiScanStatus == Wifi.ScanStatus.NoAdapter;
                bool radioOff = !noAdapter && (!isle.radios.WifiOn || wifiScanStatus == Wifi.ScanStatus.RadioOff);
                bool denied = !noAdapter && !radioOff && wifiScanStatus == Wifi.ScanStatus.AccessDenied;
                if (noAdapter) Note(Lang.T("Tidak ada adaptor Wi-Fi"));
                else if (radioOff) NoteAction(Lang.T("Wi-Fi mati"), "wifiPowerOn", Lang.T("Nyalakan Wi-Fi"));
                else if (denied) NoteAction(Lang.T("Perlu izin lokasi Windows"), "locSettings", Lang.T("Pengaturan Lokasi"));
                else if (nets.Count == 0) Note(scanning ? Lang.T("Mencari jaringan…") : Lang.T("Tidak ada jaringan"));
                else
                {
                    var st = g.Save();
                    g.SetClip(new RectangleF(0, ListTop, W, bottom - ListTop));
                    float y = ListTop;
                    first = Math.Max(0, Math.Min(first, Math.Max(0, nets.Count - 1)));
                    for (int i = first; i < nets.Count && y < bottom; i++)
                    {
                        var n = nets[i];
                        bool open = asking == n;
                        var row = new RectangleF(Pad - 8, y, W - Pad * 2 + 16, open ? RowH + 50 : RowH);
                        string id = "w" + i;
                        string offId = "off" + i;
                        Hit(new RectangleF(row.X, row.Y, row.Width, RowH), id);
                        if (open) p.Module(row, 14, p.T.Fill);
                        else if (hotId == id || hotId == offId) p.FillRound(row, 14, pressId == id ? p.T.FillPressed : p.T.Fill);
                        float cy = y + RowH / 2;
                        char bars = n.Signal < 25 ? (char)0xE872 : n.Signal < 50 ? (char)0xE873 : n.Signal < 75 ? (char)0xE874 : (char)0xE701;
                        // Missing arcs stay faintly there
                        if (n.Signal < 75) p.Icon((char)0xE701, 16f, Ink.A(p.T.Text, 0.22f), row.X + 24, cy);
                        p.Icon(bars, 16f, p.T.Text, row.X + 24, cy);
                        if (n.Secured && !n.Open) p.Icon((char)0xE72E, 8f, p.T.Sub, row.X + 32, cy + 6);
                        p.Text(n.Ssid, Ink.Semi(13f), p.T.Text, row.X + 46, cy - 8, row.Width - 140, StringAlignment.Near);
                        var now = isle.net.Current;
                        string sub = n.Connected && now.Captive ? Lang.T("Perlu login, klik untuk masuk") : n.Connected && now.Any && !now.Online ? Lang.T("Tersambung, tanpa internet")
                                   : n.Connected ? Lang.T("Tersambung") : n.Known ? Lang.T("Tersimpan") : n.Open ? Lang.T("Terbuka") : n.Personal ? Lang.T("Butuh kata sandi") : Lang.T("Masuk lewat Pengaturan");
                        p.Text(sub, Ink.Small(11f), n.Connected ? p.T.Accent : p.T.Sub, row.X + 46, cy + 9, row.Width - 140, StringAlignment.Near);
                        if (n.Connected)
                        {
                            var off = new RectangleF(row.Right - 92, cy - 14, 82, 28);
                            Hit(off, offId);
                            bool overOff = hotId == offId;
                            p.FillRound(off, 14, pressId == offId ? p.T.FillPressed : overOff ? p.T.FillHover : (hotId == id ? p.T.Fill : Ink.A(p.T.Fill, 0.7f)));
                            p.TextCenter(Lang.T("Putuskan"), Ink.Small(11.5f), overOff ? p.T.Text : p.T.Sub, off.X + off.Width / 2, cy);
                        }
                        if (open) DrawPassword(new RectangleF(row.X + 12, y + RowH, row.Width - 24, 38));
                        y += row.Height + 2;
                    }
                    g.Restore(st);
                }
                if (status != null) p.Text(status, Ink.Small(11.5f), p.T.Sub, Pad + 4, H - Foot / 2, W - Pad * 2 - 126, StringAlignment.Near);
                var link = new RectangleF(W - Pad - 120, H - Foot / 2 - 15, 124, 30);
                Hit(link, "wifiSettings");
                if (hotId == "wifiSettings") p.FillRound(link, 15, p.T.FillHover);
                p.TextCenter(Lang.T("Pengaturan Wi-Fi"), Ink.Small(11.5f), p.T.Text, link.X + link.Width / 2, link.Y + 15);
            }

            void DrawBluetooth()
            {
                Button(new RectangleF(Pad - 8, 12, 34, 34), "btBack", (char)0xE72B, Lang.T("Kembali"));
                Caption(Lang.T("Bluetooth"), Pad + 34, 29);
                if (btScanning) p.Text(Lang.T("Mencari…"), Ink.Small(11.5f), p.T.Sub, W - Pad - 38, 29, 90, StringAlignment.Far);
                Button(new RectangleF(W - Pad - 30, 12, 34, 34), "btRescan", (char)0xE72C, Lang.T("Cari ulang"));
                float bottom = H - Foot - 6;
                bool noAdapter = !isle.radios.HasBluetooth || btStatusCode == "no_adapter";
                bool radioOff = !noAdapter && (!isle.radios.BluetoothOn || btStatusCode == "off");
                bool denied = !noAdapter && !radioOff && btStatusCode == "denied" && btDevices.Count == 0;
                if (noAdapter) Note(Lang.T("Tidak ada adaptor Bluetooth"));
                else if (radioOff) NoteAction(Lang.T("Bluetooth mati"), "btPowerOn", Lang.T("Nyalakan Bluetooth"));
                else if (denied) NoteAction(Lang.T("Akses Bluetooth ditolak"), "btSettings", Lang.T("Pengaturan Bluetooth"));
                else if (btDevices.Count == 0) Note(btScanning ? Lang.T("Memindai perangkat…") : Lang.T("Tidak ada perangkat ditemukan"));
                else
                {
                    var st = g.Save();
                    g.SetClip(new RectangleF(0, ListTop, W, bottom - ListTop));
                    float y = ListTop;
                    firstBt = Math.Max(0, Math.Min(firstBt, Math.Max(0, btDevices.Count - 1)));
                    for (int i = firstBt; i < btDevices.Count && y < bottom; i++)
                    {
                        var d = btDevices[i];
                        var row = new RectangleF(Pad - 8, y, W - Pad * 2 + 16, RowH);
                        string id = "bt" + i;
                        string offId = "btOff" + i;
                        string actId = "btAct" + i;
                        Hit(row, id);
                        bool rowHot = hotId == id || hotId == offId || hotId == actId;
                        if (rowHot) p.FillRound(row, 14, pressId == id ? p.T.FillPressed : p.T.Fill);
                        float cy = y + RowH / 2;
                        char glyph = d.IsAudio ? (char)0xE7F6 : (char)0xE702;
                        p.Icon(glyph, 16f, d.Connected ? p.T.Accent : p.T.Text, row.X + 24, cy);

                        string actLabel = d.Connected ? Lang.T("Putuskan") : d.Paired ? Lang.T("Sambungkan") : Lang.T("Sandingkan");
                        string pillId = d.Connected ? offId : actId;
                        float pillW = Math.Max(82, p.Measure(actLabel, Ink.Small(11.5f)) + 22);
                        var pill = new RectangleF(row.Right - pillW - 10, cy - 14, pillW, 28);
                        float textW = Math.Max(60, pill.X - (row.X + 46) - 8);

                        string devName = string.IsNullOrEmpty(d.Name) ? Lang.T("Perangkat Bluetooth") : d.Name;
                        p.Text(devName, Ink.Semi(13f), p.T.Text, row.X + 46, cy - 8, textW, StringAlignment.Near);

                        string sub;
                        if (d.Connected) sub = d.Battery >= 0 ? Lang.T("Terhubung") + " · " + Ink.Percent(d.Battery) : Lang.T("Terhubung");
                        else if (d.Paired) sub = Lang.T("Tersanding");
                        else sub = Lang.T("Tersedia");
                        p.Text(sub, Ink.Small(11f), d.Connected ? p.T.Accent : p.T.Sub, row.X + 46, cy + 9, textW, StringAlignment.Near);

                        Hit(pill, pillId);
                        bool pillHot = hotId == pillId;
                        bool pillDown = pressId == pillId;
                        p.FillRound(pill, 14, pillDown ? p.T.FillPressed : pillHot ? p.T.FillHover : (rowHot ? p.T.Fill : Ink.A(p.T.Fill, 0.7f)));
                        p.TextCenter(actLabel, Ink.Small(11.5f), pillHot ? p.T.Text : p.T.Sub, pill.X + pill.Width / 2, cy);
                        y += row.Height + 2;
                    }
                    g.Restore(st);
                }
                if (btStatus != null) p.Text(btStatus, Ink.Small(11.5f), p.T.Sub, Pad + 4, H - Foot / 2, W - Pad * 2 - 146, StringAlignment.Near);
                var link = new RectangleF(W - Pad - 140, H - Foot / 2 - 15, 144, 30);
                Hit(link, "btSettings");
                if (hotId == "btSettings") p.FillRound(link, 15, p.T.FillHover);
                p.TextCenter(Lang.T("Pengaturan Bluetooth"), Ink.Small(11.5f), p.T.Text, link.X + link.Width / 2, link.Y + 15);
            }

            void Note(string text) { p.Text(text, Ink.Small(12f), p.T.Sub, Pad + 4, ListTop + 24, W - Pad * 2, StringAlignment.Near); }

            void NoteAction(string text, string actionId, string actionLabel)
            {
                Note(text);
                float pw = Math.Max(110, p.Measure(actionLabel, Ink.Semi(12f)) + 28);
                var pill = new RectangleF(Pad + 4, ListTop + 46, pw, 32);
                Hit(pill, actionId);
                bool hover = hotId == actionId, down = pressId == actionId;
                p.FillRound(pill, 16, down ? Ink.Mix(p.T.Accent, p.T.OnAccent, 0.22f) : hover ? Ink.Mix(p.T.Accent, p.T.OnAccent, 0.1f) : p.T.Accent);
                p.TextCenter(actionLabel, Ink.Semi(12f), p.T.OnAccent, pill.X + pill.Width / 2, pill.Y + pill.Height / 2);
            }

            void DrawPassword(RectangleF r)
            {
                var box = new RectangleF(r.X, r.Y, r.Width - 108, r.Height - 4);
                p.FillRound(box, 10, p.T.FillHover);
                p.StrokeRound(box, 10, p.T.Line, 1f);
                float cy = box.Y + box.Height / 2;
                var f = Ink.Semi(13f);
                string shown = password.Length == 0 ? null : new string('•', password.Length);
                if (shown == null) p.Text(Lang.T("Kata sandi"), Ink.Small(12f), p.T.Sub, box.X + 12, cy, box.Width - 20, StringAlignment.Near);
                else p.Text(shown, f, p.T.Text, box.X + 12, cy, box.Width - 20, StringAlignment.Near);
                float caret = box.X + 12 + (shown != null ? Math.Min(box.Width - 24, p.Measure(shown, f)) + 1 : -2);
                g.DrawLine(p.P(p.T.Text, 1.4f), caret, cy - 8, caret, cy + 8);
                var join = new RectangleF(box.Right + 8, box.Y, 100, box.Height);
                bool ready = password.Length >= 8;
                if (ready) Hit(join, "join");
                p.FillRound(join, box.Height / 2, ready ? (hotId == "join" ? Ink.Mix(p.T.Accent, p.T.OnAccent, 0.1f) : p.T.Accent) : p.T.Fill);
                p.TextCenter(Lang.T("Sambungkan"), Ink.Semi(12f), ready ? p.T.OnAccent : p.T.Faint, join.X + join.Width / 2, cy);
            }

            public void OpenWifi()
            {
                btView = false;
                btScanner.StopScan();
                btStatus = null;
                wifiView = true;
                asking = null;
                status = null;
                first = 0;
                Rescan();
                Render();
            }

            public void OpenBluetooth()
            {
                wifiView = false;
                asking = null;
                password = "";
                btView = true;
                btStatus = null;
                firstBt = 0;
                btScanner.StartScan(true);
                Render();
            }

            static List<BtDevice> SortBt(List<BtDevice> list)
            {
                var sorted = new List<BtDevice>();
                if (list == null) return sorted;
                sorted.AddRange(list);
                sorted.Sort(delegate (BtDevice a, BtDevice b)
                {
                    int ga = a.Connected ? 0 : a.Paired ? 1 : 2;
                    int gb = b.Connected ? 0 : b.Paired ? 1 : 2;
                    if (ga != gb) return ga.CompareTo(gb);
                    int sa = a.Signal, sb = b.Signal;
                    if (sa != sb) return sb.CompareTo(sa);
                    return string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase);
                });
                return sorted;
            }

            // A quick list now, the fresh scan after Windows finishes
            void Rescan()
            {
                if (scanning) return;
                scanning = true;
                var t = new Thread(delegate ()
                {
                    List<Wifi.Net> quick = null, full = null;
                    Wifi.ScanStatus stQuick = Wifi.ScanStatus.Ok, stFull = Wifi.ScanStatus.Ok;
                    try
                    {
                        quick = Wifi.List(out stQuick);
                        Post(delegate
                        {
                            if (quick != null) nets = quick;
                            wifiScanStatus = stQuick;
                            Render();
                        });
                        if (stQuick != Wifi.ScanStatus.NoAdapter && stQuick != Wifi.ScanStatus.RadioOff && stQuick != Wifi.ScanStatus.AccessDenied)
                        {
                            Wifi.ScanStatus scanSt = Wifi.ScanAndWait(4000);
                            Wifi.ScanStatus stList;
                            full = Wifi.List(out stList);
                            if (scanSt == Wifi.ScanStatus.AccessDenied || scanSt == Wifi.ScanStatus.RadioOff || scanSt == Wifi.ScanStatus.NoAdapter)
                                stFull = scanSt;
                            else if (stList != Wifi.ScanStatus.Ok)
                                stFull = stList;
                            else
                                stFull = scanSt == Wifi.ScanStatus.Failed && (full == null || full.Count == 0) ? Wifi.ScanStatus.Failed : Wifi.ScanStatus.Ok;
                        }
                        else
                        {
                            stFull = stQuick;
                        }
                    }
                    catch (Exception ex) { Log.Write("wifi list: " + ex.Message); stFull = Wifi.ScanStatus.Failed; }
                    Post(delegate
                    {
                        if (full != null) nets = full;
                        wifiScanStatus = stFull;
                        scanning = false;
                        Render();
                    });
                }) { IsBackground = true, Name = "wifi scan" };
                t.Start();
            }

            void Join(Wifi.Net n, string pass)
            {
                status = Lang.F("Menyambungkan ke {0}…", n.Ssid);
                asking = null;
                password = "";
                Render();
                var t = new Thread(delegate ()
                {
                    string err = null;
                    try { err = Wifi.Connect(n, pass); }
                    catch (Exception ex) { err = ex.Message; }
                    if (err != null)
                    {
                        Log.Write("wifi join: " + err);
                        List<Wifi.Net> errList = null;
                        Wifi.ScanStatus errSt;
                        try { errList = Wifi.List(out errSt); } catch (Exception) { }
                        Post(delegate
                        {
                            if (errList != null) nets = errList;
                            status = err.Length > 0 ? Lang.F("Gagal: {0}", err) : Lang.F("Gagal menyambung ke {0}", n.Ssid);
                            Render();
                        });
                        return;
                    }
                    List<Wifi.Net> list = null;
                    bool ok = false;
                    for (int k = 0; k < 15; k++)
                    {
                        Thread.Sleep(400);
                        if (IsDisposed) return;
                        Wifi.ScanStatus st;
                        try { list = Wifi.List(out st); } catch (Exception) { list = null; }
                        if (list != null && list.Exists(x => x.Ssid == n.Ssid && x.Connected))
                        {
                            ok = true;
                            break;
                        }
                    }
                    Post(delegate
                    {
                        if (list != null) nets = list;
                        status = ok ? Lang.F("Tersambung ke {0}", n.Ssid) : Lang.F("Gagal menyambung ke {0}", n.Ssid);
                        Render();
                    });
                }) { IsBackground = true, Name = "wifi join" };
                t.Start();
            }

            void BtAction(BtDevice d, string kind)
            {
                if (btBusy || d == null) return;
                btBusy = true;
                if (kind == "pair") btStatus = Lang.T("Menyandingkan…");
                else if (kind == "connect") btStatus = Lang.T("Menghubungkan…");
                else btStatus = Lang.T("Memutuskan…");
                Render();
                var t = new Thread(delegate ()
                {
                    string err = null;
                    try
                    {
                        if (kind == "pair") err = BtScanner.Pair(d);
                        else if (kind == "unpair") err = BtScanner.Unpair(d);
                        else if (kind == "connect") err = BtScanner.Connect(d);
                        else err = BtScanner.Disconnect(d);
                    }
                    catch (Exception ex) { err = ex.Message; }
                    if (err != null) Log.Write("bt " + kind + ": " + err);
                    Post(delegate
                    {
                        btBusy = false;
                        if (kind == "pair")
                            btStatus = err == null ? Lang.T("Berhasil tersanding") : (err.Length > 0 ? Lang.T("Gagal menyandingkan") + " (" + err + ")" : Lang.T("Gagal menyandingkan"));
                        else if (kind == "connect")
                            btStatus = err == null ? Lang.T("Terhubung") : (err.Length > 0 ? Lang.T("Gagal menghubungkan") + " (" + err + ")" : Lang.T("Gagal menghubungkan"));
                        else
                            btStatus = err == null ? null : Lang.F("Gagal: {0}", err);
                        if (btView) btScanner.StartScan(true);
                        Render();
                    });
                }) { IsBackground = true, Name = "bt " + kind };
                t.Start();
            }

            void DelayAction(int ms, Action a)
            {
                var t = new System.Windows.Forms.Timer { Interval = ms };
                t.Tick += delegate
                {
                    t.Stop();
                    t.Dispose();
                    if (!IsDisposed) a();
                };
                t.Start();
            }

            void Post(Action a)
            {
                try { if (!IsDisposed && IsHandleCreated) BeginInvoke(a); } catch (Exception) { }
            }

            // Password field opens inside the list
            void ShowRow(int i)
            {
                float room = H - Foot - 6 - ListTop - (RowH + 50);
                first = Math.Max(first, i - Math.Max(0, (int)Math.Floor(room / (RowH + 2))));
            }

            static int IndexOf(string id, string tag)
            {
                int i;
                return id != null && id.StartsWith(tag, StringComparison.Ordinal) && int.TryParse(id.Substring(tag.Length), out i) ? i : -1;
            }

            protected override bool Drag(string id, PointF at)
            {
                if (wifiView || btView) return false;
                if (id == "tileScroll")
                {
                    ScrollTiles((at.Y - TileTop - scrollGrab) * MaxTileScroll / (ViewHeight / 2));
                    return true;
                }
                if (id == "vol") { isle.setVol(SliderValue(volRect, at)); return true; }
                if (id == "bright" && HasBright) { isle.setBright(SliderValue(brightRect, at)); return true; }
                return false;
            }

            protected override void OnMouseDown(MouseEventArgs e)
            {
                if (!Leaving && !wifiView && !btView && e.Button == MouseButtons.Left && HitAt(e.Location) == "tileScroll")
                {
                    float grab = Local(e.Location).Y - ScrollThumbTop;
                    scrollGrab = grab >= 0 && grab <= ViewHeight / 2 ? grab : ViewHeight / 4;
                }
                base.OnMouseDown(e);
            }

            protected override void OnMouseWheel(MouseEventArgs e)
            {
                if (Leaving) return;
                if (wifiView || btView || Local(e.Location).Y >= GridBottom)
                {
                    base.OnMouseWheel(e);
                    return;
                }
                pressId = null;
                ScrollTiles(tileScroll - e.Delta * TilePitch / SystemInformation.MouseWheelScrollDelta);
                hotId = null;
                Render();
            }

            protected override void Wheel(string id, int delta)
            {
                if (wifiView) { first -= Math.Sign(delta); return; }
                if (btView) { firstBt -= Math.Sign(delta); return; }
                float step = delta > 0 ? 0.02f : -0.02f;
                if (id == "bright" && HasBright) isle.setBright(Ease.Clamp01(isle.brightLevel / 100f + step * 2));
                else isle.setVol(Ease.Clamp01(isle.vol + step));
            }

            protected override void Clicked(string id, MouseButtons button)
            {
                if (wifiView) { WifiClicked(id, button); return; }
                if (btView) { BtClicked(id, button); return; }
                if (button == MouseButtons.Right)
                {
                    if (id == "wifi") OpenWifi();
                    else if (id == "bt") OpenBluetooth();
                    else if (id == "vol" || id == "device" || id == "output") SettingsMenu(Lang.T("Pengaturan suara"), "ms-settings:sound");
                    return;
                }
                if (button != MouseButtons.Left) return;
                switch (id)
                {
                    case "wifi": isle.actWifi(); break;
                    case "wifiList": OpenWifi(); break;
                    case "bt": isle.actBt(); break;
                    case "btMore": OpenBluetooth(); break;
                    case "quiet": isle.actQuiet(); break;
                    case "dark": isle.actDark(); break;
                    case "awake": isle.actAwake(); break;
                    case "awakeMore": ShowMenu(isle.AwakeItems()); break;
                    case "mic": isle.actMic(); break;
                    case "cast": Done(); Taskbar.Win((byte)Keys.K); break;
                    case "project": Done(); Taskbar.Win((byte)Keys.P); break;
                    case "nightLight": Open("ms-settings:nightlight", null); Done(); break;
                    case "batterySaver": Open(isle.hasBattery ? "ms-settings:batterysaver" : "ms-settings:powersleep", null); Done(); break;
                    case "hotspot": Open("ms-settings:network-mobilehotspot", null); Done(); break;
                    case "mute": isle.actMute(); break;
                    case "output":
                    case "device": DeviceMenu(); break;
                    case "gear": Open("ms-settings:", null); Done(); break;
                    case "deskmate": Done(); isle.OpenPrefs("dock"); break;
                }
            }

            void WifiClicked(string id, MouseButtons button)
            {
                if (button != MouseButtons.Left) return;
                int i = IndexOf(id, "w"), off = IndexOf(id, "off");
                if (off >= 0 && off < nets.Count)
                {
                    var n = nets[off];
                    new Thread(delegate ()
                    {
                        Wifi.Disconnect(n);
                        Thread.Sleep(1500);
                        Post(delegate { status = null; Rescan(); });
                    }) { IsBackground = true }.Start();
                    status = Lang.F("Memutus {0}…", n.Ssid);
                    return;
                }
                if (i >= 0 && i < nets.Count)
                {
                    var n = nets[i];
                    // Hotel or campus login page
                    if (n.Connected && isle.net.Current.Captive) { Open("http://www.msftconnecttest.com/redirect", null); Done(); return; }
                    if (n.Connected) return;
                    if (n.Known || n.Open) Join(n, null);
                    else if (n.Personal) { asking = asking == n ? null : n; password = ""; if (asking != null) ShowRow(i); }
                    else { Open("ms-settings:network-wifi", null); Done(); }
                    return;
                }
                switch (id)
                {
                    case "back": wifiView = false; asking = null; break;
                    case "rescan": Rescan(); break;
                    case "wifiPowerOn":
                        isle.radios.SetPower(true, true);
                        status = Lang.T("Mencari jaringan…");
                        DelayAction(1200, delegate
                        {
                            if (!IsDisposed && wifiView)
                            {
                                isle.radios.Refresh();
                                status = null;
                                Rescan();
                            }
                        });
                        break;
                    case "locSettings": Open("ms-settings:privacy-location", null); Done(); break;
                    case "join": if (asking != null && password.Length >= 8) Join(asking, password); break;
                    case "wifiSettings": Open("ms-settings:network-wifi", null); Done(); break;
                }
            }

            void BtClicked(string id, MouseButtons button)
            {
                int off = IndexOf(id, "btOff"), act = IndexOf(id, "btAct"), i = IndexOf(id, "bt");
                if (button == MouseButtons.Right)
                {
                    int idx = off >= 0 ? off : act >= 0 ? act : i;
                    if (idx >= 0 && idx < btDevices.Count)
                    {
                        var d = btDevices[idx];
                        if (d.Paired)
                        {
                            var m = new ContextMenuStrip();
                            if (d.Connected) m.Items.Add(Lang.T("Putuskan"), null, delegate { BtAction(d, "disconnect"); });
                            else m.Items.Add(Lang.T("Sambungkan"), null, delegate { BtAction(d, "connect"); });
                            m.Items.Add(Lang.T("Lepas sandi"), null, delegate { BtAction(d, "unpair"); });
                            ShowMenu(m);
                        }
                    }
                    return;
                }
                if (button != MouseButtons.Left) return;
                if (off >= 0 && off < btDevices.Count) { BtAction(btDevices[off], "disconnect"); return; }
                if (act >= 0 && act < btDevices.Count)
                {
                    var d = btDevices[act];
                    if (!d.Connected) BtAction(d, d.Paired ? "connect" : "pair");
                    return;
                }
                if (i >= 0 && i < btDevices.Count)
                {
                    var d = btDevices[i];
                    if (!d.Connected) BtAction(d, d.Paired ? "connect" : "pair");
                    return;
                }
                switch (id)
                {
                    case "btBack": btView = false; btScanner.StopScan(); btStatus = null; break;
                    case "btRescan": btStatus = null; btScanner.StartScan(true); break;
                    case "btPowerOn":
                        isle.radios.SetPower(false, true);
                        btStatus = null;
                        DelayAction(1200, delegate
                        {
                            if (!IsDisposed && btView)
                            {
                                isle.radios.Refresh();
                                btScanner.StartScan(true);
                            }
                        });
                        break;
                    case "btSettings": Open("ms-settings:bluetooth", null); Done(); break;
                }
            }

            protected override bool ProcessCmdKey(ref Message msg, Keys key)
            {
                if (Leaving) return true;
                if (wifiView)
                {
                    if (asking != null)
                    {
                        switch (key)
                        {
                            case Keys.Escape: asking = null; password = ""; Render(); return true;
                            case Keys.Enter: if (password.Length >= 8) Join(asking, password); return true;
                            case Keys.Back: if (password.Length > 0) password = password.Substring(0, password.Length - 1); Render(); return true;
                            case Keys.Control | Keys.V:
                                try { if (Clipboard.ContainsText()) password = (password + Clipboard.GetText().Trim()); } catch (Exception) { }
                                if (password.Length > 63) password = password.Substring(0, 63);
                                Render();
                                return true;
                        }
                    }
                    else if (key == Keys.Escape) { wifiView = false; Render(); return true; }
                }
                if (btView && key == Keys.Escape) { btView = false; btScanner.StopScan(); btStatus = null; Render(); return true; }
                if (!wifiView && !btView && (key == Keys.PageUp || key == Keys.PageDown))
                {
                    ScrollTiles(tileScroll + (key == Keys.PageUp ? -ViewHeight : ViewHeight));
                    Render();
                    return true;
                }
                return base.ProcessCmdKey(ref msg, key);
            }

            protected override void OnKeyPress(KeyPressEventArgs e)
            {
                base.OnKeyPress(e);
                if (Leaving || !wifiView || asking == null || e.KeyChar < ' ' || password.Length >= 63) return;
                password += e.KeyChar;
                e.Handled = true;
                Render();
            }


            void SettingsMenu(string label, string uri)
            {
                var m = new ContextMenuStrip();
                m.Items.Add(label, null, delegate { Open(uri, null); Done(); });
                ShowMenu(m);
            }

            void DeviceMenu()
            {
                var m = new ContextMenuStrip();
                foreach (var d in isle.audioDevices)
                {
                    string id = d.Id;
                    m.Items.Add(new ToolStripMenuItem(d.Name, null, delegate { isle.volume.SetDefault(id); }) { Checked = d.Id == isle.audioDefault });
                }
                if (m.Items.Count > 0) m.Items.Add(new ToolStripSeparator());
                m.Items.Add(Lang.T("Pengaturan suara"), null, delegate { Open("ms-settings:sound", null); Done(); });
                ShowMenu(m);
            }
        }
    }
}
