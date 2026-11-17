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
        void StartControlBtScan()
        {
            ctlBtMsg = null;
            ctlBtScanning = true;
            ctlBtScanner.StartScan(true);
            Kick();
        }

        static List<BtDevice> SortBtDevices(List<BtDevice> list)
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

        void ControlBtAction(BtDevice d, string kind)
        {
            if (ctlBtBusy || d == null) return;
            ctlBtBusy = true;
            if (kind == "pair") ctlBtMsg = Lang.T("Menyandingkan…");
            else if (kind == "connect") ctlBtMsg = Lang.T("Menghubungkan…");
            else ctlBtMsg = Lang.T("Memutuskan…");
            Kick();
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
                if (err != null) Log.Write("ctl bt " + kind + ": " + err);
                Post(delegate
                {
                    if (IsDisposed) return;
                    ctlBtBusy = false;
                    if (kind == "pair")
                        ctlBtMsg = err == null ? Lang.T("Berhasil tersanding") : (err.Length > 0 ? Lang.T("Gagal menyandingkan") + " (" + err + ")" : Lang.T("Gagal menyandingkan"));
                    else if (kind == "connect")
                        ctlBtMsg = err == null ? Lang.T("Terhubung") : (err.Length > 0 ? Lang.T("Gagal menghubungkan") + " (" + err + ")" : Lang.T("Gagal menghubungkan"));
                    else
                        ctlBtMsg = err == null ? null : Lang.F("Gagal: {0}", err);
                    if (controlMode == 3 && expanded && page == PageControl) ctlBtScanner.StartScan(true);
                    Kick();
                });
            }) { IsBackground = true, Name = "ctl bt " + kind };
            t.Start();
        }

        void ControlBtMenu(BtDevice d)
        {
            if (d == null || !d.Paired) return;
            var m = new ContextMenuStrip();
            if (d.Connected) m.Items.Add(Lang.T("Putuskan"), null, delegate { ControlBtAction(d, "disconnect"); });
            else m.Items.Add(Lang.T("Sambungkan"), null, delegate { ControlBtAction(d, "connect"); });
            m.Items.Add(Lang.T("Lepas sandi"), null, delegate { ControlBtAction(d, "unpair"); });
            ShowIslandMenu(m);
        }

    }
}
