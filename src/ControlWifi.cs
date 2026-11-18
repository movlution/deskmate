using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Threading;
using System.Windows.Forms;
using WinTimer = System.Windows.Forms.Timer;

namespace Deskmate
{
    partial class Island
    {
        void DrawControlWifi(float x0, float y, float full, float h, DateTime now)
        {
            DrawControlSubHeader(x0, y, full, Ink.IcoWifi, Lang.T("Wi-Fi"), ctlWifiScanning, ctlWifiMsg, "ctlWifiRescan", actWifiRescan, "ctlWifiSettings", actWifiSettings);
            float listY = y + CtlSubHeadH;
            float listH = Math.Max(80, h - Ink.ModuleInset - listY);
            var listArea = new RectangleF(x0, listY, full, listH);
            Module(listArea, Ink.RadiusModule);

            bool noAdapter = !radios.HasWifi || ctlWifiStatus == Wifi.ScanStatus.NoAdapter;
            bool radioOff = !noAdapter && (!radios.WifiOn || ctlWifiStatus == Wifi.ScanStatus.RadioOff);
            bool denied = !noAdapter && !radioOff && ctlWifiStatus == Wifi.ScanStatus.AccessDenied;
            if (noAdapter) { DrawControlSubNotice(listArea, Ink.IcoWifi, Lang.T("Tidak ada adaptor Wi-Fi"), null, null, null); return; }
            if (radioOff) { DrawControlSubNotice(listArea, Ink.IcoWifi, Lang.T("Wi-Fi mati"), Lang.T("Nyalakan Wi-Fi"), "ctlWifiPowerOn", actWifiPowerOn); return; }
            if (denied) { DrawControlSubNotice(listArea, Ink.IcoLock, Lang.T("Perlu izin lokasi Windows"), Lang.T("Pengaturan Lokasi"), "ctlWifiLoc", actWifiLoc); return; }
            if (ctlWifiNets.Count == 0)
            {
                DrawControlSubNotice(listArea, Ink.IcoWifi, ctlWifiScanning ? Lang.T("Mencari jaringan…") : Lang.T("Tidak ada jaringan"), null, null, null);
                return;
            }

            SyncWifiActs();
            var clipArea = new RectangleF(listArea.X, listArea.Y + 2, listArea.Width, Math.Max(0, listArea.Height - 4));
            float maxScroll = Math.Max(0, ControlSubContentH() - clipArea.Height);
            if (controlScroll.Target > maxScroll) controlScroll.Target = maxScroll;

            var st = g.Save();
            g.IntersectClip(clipArea);
            var curNet = net.Current;
            for (int i = 0; i < ctlWifiNets.Count; i++)
            {
                float ry = clipArea.Y + i * CtlSubRowPitch - controlScroll.Value;
                if (ry + CtlSubRowH < clipArea.Top || ry > clipArea.Bottom) continue;
                var n = ctlWifiNets[i];
                var row = new RectangleF(listArea.X + 6, ry, listArea.Width - 12, CtlSubRowH);
                string rowId = ctlWifiRowIds[i], actId = ctlWifiPillIds[i];
                AddHit(row, rowId, ctlWifiRowActs[i]);
                bool rowHot = Hover(rowId) || Hover(actId) || (keyboardMode && (focusId == rowId || focusId == actId));
                if (rowHot) p.FillRound(row, 12, Pressed(rowId) ? p.T.FillPressed : p.T.FillHover);
                else if (n.Connected) p.FillRound(row, 12, Ink.A(p.T.Accent, 0.10f));

                float cy = row.Y + CtlSubRowH / 2;
                char bars = n.Signal < 25 ? (char)0xE872 : n.Signal < 50 ? (char)0xE873 : n.Signal < 75 ? (char)0xE874 : (char)0xE701;
                if (n.Signal < 75) p.Icon((char)0xE701, 15f, Ink.A(p.T.Text, 0.22f), row.X + 22, cy);
                p.Icon(bars, 15f, n.Connected ? p.T.Accent : p.T.Text, row.X + 22, cy);
                if (n.Secured && !n.Open) p.Icon((char)0xE72E, 8f, p.T.Sub, row.X + 30, cy + 6);

                string sub = n.Connected && curNet.Captive ? Lang.T("Perlu login")
                           : n.Connected && curNet.Any && !curNet.Online ? Lang.T("Tersambung, tanpa internet")
                           : n.Connected ? Lang.T("Tersambung")
                           : n.Known ? Lang.T("Tersimpan")
                           : n.Open ? Lang.T("Terbuka")
                           : n.Personal ? Lang.T("Butuh kata sandi")
                           : Lang.T("Masuk lewat Pengaturan");

                bool showAct = n.Connected || rowHot;
                string actLabel = n.Connected ? Lang.T("Putuskan") : Lang.T("Sambungkan");
                float pillW = showAct ? Math.Max(82, p.Measure(actLabel, Ink.SmallSemi(11.5f)) + 20) : 0;
                float textX = row.X + 42;
                float textW = Math.Max(60, (showAct ? row.Right - pillW - 16 : row.Right - 12) - textX);
                p.Text(n.Ssid, Ink.Semi(13f), p.T.Text, textX, cy - 8, textW, StringAlignment.Near);
                p.Text(sub, Ink.Small(11f), n.Connected ? p.T.Accent : p.T.Sub, textX, cy + 9, textW, StringAlignment.Near);

                if (showAct)
                {
                    var pill = new RectangleF(row.Right - pillW - 8, cy - 13, pillW, 26);
                    AddHit(pill, actId, ctlWifiPillActs[i]);
                    bool pHot = Hover(actId), pDown = Pressed(actId);
                    Color pFill = n.Connected
                        ? (pDown ? p.T.FillPressed : pHot ? p.T.FillHover : p.T.Fill)
                        : (pDown ? KitInk.Deepen(p.T.Accent, p.T.OnAccent, 0.2f) : pHot ? KitInk.Deepen(p.T.Accent, p.T.OnAccent, 0.1f) : p.T.Accent);
                    Color pInk = n.Connected ? (pHot ? p.T.Text : p.T.Sub) : p.T.OnAccent;
                    p.FillRound(pill, 13, pFill);
                    p.TextCenter(actLabel, Ink.SmallSemi(11.5f), pInk, pill.X + pill.Width / 2, cy);
                }
            }
            g.Restore(st);
        }

        void DrawControlBluetooth(float x0, float y, float full, float h, DateTime now)
        {
            DrawControlSubHeader(x0, y, full, Ink.IcoBluetooth, Lang.T("Bluetooth"), ctlBtScanning, ctlBtMsg, "ctlBtRescan", actBtRescan, "ctlBtSettings", actBtSettings);
            float listY = y + CtlSubHeadH;
            float listH = Math.Max(80, h - Ink.ModuleInset - listY);
            var listArea = new RectangleF(x0, listY, full, listH);
            Module(listArea, Ink.RadiusModule);

            bool noAdapter = !radios.HasBluetooth || ctlBtStatusCode == "no_adapter";
            bool radioOff = !noAdapter && (!radios.BluetoothOn || ctlBtStatusCode == "off");
            bool denied = !noAdapter && !radioOff && ctlBtStatusCode == "denied" && ctlBtDevices.Count == 0;
            if (noAdapter) { DrawControlSubNotice(listArea, Ink.IcoBluetooth, Lang.T("Tidak ada adaptor Bluetooth"), null, null, null); return; }
            if (radioOff) { DrawControlSubNotice(listArea, Ink.IcoBluetooth, Lang.T("Bluetooth mati"), Lang.T("Nyalakan Bluetooth"), "ctlBtPowerOn", actBtPowerOn); return; }
            if (denied) { DrawControlSubNotice(listArea, Ink.IcoLock, Lang.T("Akses Bluetooth ditolak"), Lang.T("Pengaturan Bluetooth"), "ctlBtDeniedSettings", actBtSettings); return; }
            if (ctlBtDevices.Count == 0)
            {
                DrawControlSubNotice(listArea, Ink.IcoBluetooth, ctlBtScanning ? Lang.T("Memindai perangkat…") : Lang.T("Tidak ada perangkat ditemukan"), null, null, null);
                return;
            }

            SyncBtActs();
            var clipArea = new RectangleF(listArea.X, listArea.Y + 2, listArea.Width, Math.Max(0, listArea.Height - 4));
            float maxScroll = Math.Max(0, ControlSubContentH() - clipArea.Height);
            if (controlScroll.Target > maxScroll) controlScroll.Target = maxScroll;

            var st = g.Save();
            g.IntersectClip(clipArea);
            for (int i = 0; i < ctlBtDevices.Count; i++)
            {
                float ry = clipArea.Y + i * CtlSubRowPitch - controlScroll.Value;
                if (ry + CtlSubRowH < clipArea.Top || ry > clipArea.Bottom) continue;
                var d = ctlBtDevices[i];
                var row = new RectangleF(listArea.X + 6, ry, listArea.Width - 12, CtlSubRowH);
                string rowId = ctlBtRowIds[i], actId = ctlBtPillIds[i];
                Action menuAct = d.Paired ? ctlBtMenuActs[i] : null;
                AddHit(row, rowId, ctlBtRowActs[i], null, menuAct);
                bool rowHot = Hover(rowId) || Hover(actId) || (keyboardMode && (focusId == rowId || focusId == actId));
                if (rowHot) p.FillRound(row, 12, Pressed(rowId) ? p.T.FillPressed : p.T.FillHover);
                else if (d.Connected) p.FillRound(row, 12, Ink.A(p.T.Accent, 0.10f));

                float cy = row.Y + CtlSubRowH / 2;
                var tile = new RectangleF(row.X + 8, cy - 14, 28, 28);
                p.Module(tile, 8, d.Connected ? Ink.A(p.T.Accent, 0.18f) : p.T.Fill);
                char glyph = d.IsAudio ? (char)0xE7F6 : (char)0xE702;
                p.Icon(glyph, 14f, d.Connected ? p.T.Accent : p.T.Text, tile.X + 14, cy);

                string actLabel = d.Connected ? Lang.T("Putuskan") : d.Paired ? Lang.T("Sambungkan") : Lang.T("Sandingkan");
                float pillW = Math.Max(82, p.Measure(actLabel, Ink.SmallSemi(11.5f)) + 20);
                var pill = new RectangleF(row.Right - pillW - 8, cy - 13, pillW, 26);
                float textX = tile.Right + 10;
                float textW = Math.Max(60, pill.X - textX - 8);

                string devName = string.IsNullOrEmpty(d.Name) ? Lang.T("Perangkat Bluetooth") : d.Name;
                p.Text(devName, Ink.Semi(13f), p.T.Text, textX, cy - 8, textW, StringAlignment.Near);

                string sub;
                if (d.Connected) sub = d.Battery >= 0 ? Lang.T("Terhubung") + " · " + Ink.Percent(d.Battery) : Lang.T("Terhubung");
                else if (d.Paired) sub = Lang.T("Tersanding");
                else sub = Lang.T("Tersedia");
                p.Text(sub, Ink.Small(11f), d.Connected ? p.T.Accent : p.T.Sub, textX, cy + 9, textW, StringAlignment.Near);

                AddHit(pill, actId, ctlBtPillActs[i], null, menuAct);
                bool pHot = Hover(actId), pDown = Pressed(actId);
                Color pFill = pDown ? p.T.FillPressed : pHot ? p.T.FillHover : (rowHot ? p.T.Fill : Ink.A(p.T.Fill, 0.8f));
                p.FillRound(pill, 13, pFill);
                p.TextCenter(actLabel, Ink.SmallSemi(11.5f), pHot ? p.T.Text : p.T.Sub, pill.X + pill.Width / 2, cy);
            }
            g.Restore(st);
        }

        void StartControlWifiScan()
        {
            if (ctlWifiScanning) return;
            ctlWifiScanning = true;
            Kick();
            var t = new Thread(delegate ()
            {
                List<Wifi.Net> quick = null, full = null;
                Wifi.ScanStatus stQuick = Wifi.ScanStatus.Ok, stFull = Wifi.ScanStatus.Ok;
                try
                {
                    quick = Wifi.List(out stQuick);
                    Post(delegate
                    {
                        if (IsDisposed) return;
                        if (quick != null) ctlWifiNets = quick;
                        ctlWifiStatus = stQuick;
                        Kick();
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
                catch (Exception ex) { Log.Write("ctl wifi scan: " + ex.Message); stFull = Wifi.ScanStatus.Failed; }
                Post(delegate
                {
                    if (IsDisposed) return;
                    if (full != null) ctlWifiNets = full;
                    ctlWifiStatus = stFull;
                    ctlWifiScanning = false;
                    Kick();
                });
            }) { IsBackground = true, Name = "ctl wifi scan" };
            t.Start();
        }

        void ControlWifiJoin(Wifi.Net n, string pass)
        {
            if (n == null) return;
            ctlWifiMsg = Lang.F("Menyambungkan ke {0}…", n.Ssid);
            Kick();
            var t = new Thread(delegate ()
            {
                string err = null;
                try { err = Wifi.Connect(n, pass); }
                catch (Exception ex) { err = ex.Message; }
                if (err != null)
                {
                    Log.Write("ctl wifi join: " + err);
                    List<Wifi.Net> errList = null;
                    Wifi.ScanStatus errSt;
                    try { errList = Wifi.List(out errSt); } catch (Exception) { }
                    Post(delegate
                    {
                        if (IsDisposed) return;
                        if (errList != null) ctlWifiNets = errList;
                        ctlWifiMsg = err.Length > 0 ? Lang.F("Gagal: {0}", err) : Lang.F("Gagal menyambung ke {0}", n.Ssid);
                        Kick();
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
                    if (IsDisposed) return;
                    if (list != null) ctlWifiNets = list;
                    ctlWifiMsg = ok ? Lang.F("Tersambung ke {0}", n.Ssid) : Lang.F("Gagal menyambung ke {0}", n.Ssid);
                    Kick();
                });
            }) { IsBackground = true, Name = "ctl wifi join" };
            t.Start();
        }

        void ControlWifiDisconnect(Wifi.Net n)
        {
            if (n == null) return;
            ctlWifiMsg = Lang.F("Memutus {0}…", n.Ssid);
            Kick();
            var t = new Thread(delegate ()
            {
                try { Wifi.Disconnect(n); } catch (Exception ex) { Log.Write("ctl wifi disc: " + ex.Message); }
                Thread.Sleep(1500);
                Post(delegate
                {
                    if (IsDisposed) return;
                    ctlWifiMsg = null;
                    StartControlWifiScan();
                });
            }) { IsBackground = true, Name = "ctl wifi disc" };
            t.Start();
        }

        void OpenWifiPassBox(Wifi.Net n)
        {
            if (n == null) return;
            CloseWifiPassBox();
            var box = new WifiPassBox(n, ControlWifiJoin);
            wifiPassBox = box;
            box.FormClosed += delegate
            {
                if (ReferenceEquals(wifiPassBox, box)) wifiPassBox = null;
                if (!IsDisposed && !pointerInside && expanded)
                {
                    leaveTimer.Stop();
                    leaveTimer.Start();
                }
            };
            var area = Screen.FromHandle(Handle).WorkingArea;
            box.StartPosition = FormStartPosition.Manual;
            box.Show();
            int cx = Left + (int)Math.Round((shell.X + shell.Width / 2) * scale);
            int by = Top + (int)Math.Round((shell.Y + shell.Height) * scale);
            int x = Math.Max(area.X + 8, Math.Min(area.Right - box.Width - 8, cx - box.Width / 2));
            int y = Math.Min(area.Bottom - box.Height - 8, by + (int)(8 * scale));
            box.Location = new Point(x, y);
            Native.SetForegroundWindow(box.Handle);
        }

    }
}
