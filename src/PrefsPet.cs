using System;
using System.Collections.Generic;
using System.Drawing;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace Deskmate
{
    partial class Island
    {
        partial class PrefsWindow
        {
            // Dock page, Pet part: on and off, animal, fur, size, speed, and the folder for own pets
            void PetRows(Action<string> cap, Action<KitRow> add)
            {
                Action refresh = null;
                Action<string, object> change = (key, value) =>
                {
                    island.SetPetChoice(key, value);
                    refresh();
                    searchIndex.Remove("pet");
                };
                var petOn = Switch("pet.on", false, v => change("pet.on", v));
                var petRow = Row(Lang.T("Pet di dock"), Lang.T("Hewan kecil yang berjalan di atas dock, atau di tepi bawah layar tanpa dock"), petOn);
                petRow.Glyph = (char)0xEB51;
                add(petRow);

                string kind = settings.Get("pet.kind", "cat");
                var ids = new List<string> { "cat", "dog", "chao" };
                var names = new List<string> { Lang.T("Kucing"), Lang.T("Anjing"), Lang.T("Chao") };
                foreach (var n in PetSheet.Custom()) { ids.Add("custom:" + n); names.Add(n); }
                int at = ids.IndexOf(kind);
                // A chosen folder that has gone still shows, with the reason below
                if (at < 0 && kind.StartsWith("custom:", StringComparison.Ordinal)) { ids.Add(kind); names.Add(kind.Substring(7)); at = ids.Count - 1; }
                var kindPop = Popup(names.ToArray(), Math.Max(0, at), i => change("pet.kind", ids[i]));
                var kindRow = Row(Lang.T("Hewan"), null, kindPop);
                // Reserve room for status text so changing pets never shifts the rows.
                kindRow.Height = (int)(54 * Ui);
                kindRow.Glyph = (char)0xE8EC;
                add(kindRow);

                string[] colorNames = { Lang.T("Oranye"), Lang.T("Cokelat"), Lang.T("Abu-abu"), Lang.T("Hitam"), Lang.T("Krem") };
                var colorPop = Popup(colorNames, Math.Max(0, Array.IndexOf(PetSheet.Colors, settings.Get("pet.color", "orange"))),
                    i => change("pet.color", PetSheet.Colors[i]));
                var colorRow = Row(Lang.T("Warna bulu"), null, colorPop);
                colorRow.Height = kindRow.Height;
                colorRow.Glyph = (char)0xE790;
                add(colorRow);

                string size = settings.Get("pet.size", "m");
                var sizeSeg = Seg(new[] { Lang.T("Kecil"), Lang.T("Sedang"), Lang.T("Besar") }, size == "s" ? 0 : size == "l" ? 2 : 1,
                    i => change("pet.size", i == 0 ? "s" : i == 2 ? "l" : "m"));
                var sizeRow = Row(Lang.T("Ukuran"), null, sizeSeg);
                sizeRow.Glyph = (char)0xE740;
                add(sizeRow);

                bool screenArea = settings.Get("pet.area", "dock") == "screen";
                var areaSeg = Seg(new[] { Lang.T("Dock"), Lang.T("Bawah layar") }, screenArea ? 1 : 0,
                    i => change("pet.area", i == 1 ? "screen" : "dock"));
                var areaRow = Row(Lang.T("Area jalan"), Lang.T("Di bawah layar, pet berjalan selebar layar dan melompat naik ke dock"), areaSeg);
                areaRow.Glyph = (char)0xE707;
                add(areaRow);

                var speed = Slider(50, 200, 10, island.PetSpeed, v => v + "%", v => change("pet.speed", v));
                var speedRow = Row(Lang.T("Kecepatan jalan"), null, speed);
                speedRow.Glyph = (char)0xEC4A;
                add(speedRow);

                var react = Switch("pet.react", true, v => change("pet.react", v));
                var reactRow = Row(Lang.T("Reaksi"), Lang.T("Kaget, senang, bergoyang saat musik, dan tidur saat Anda pergi"), react);
                reactRow.Glyph = (char)0xE76E;
                add(reactRow);

                var chase = Switch("pet.autoChase", false, v => change("pet.autoChase", v));
                add(Row(Lang.T("Kejar kursor otomatis"), Lang.T("Sapukan kursor cepat ke samping di sepanjang dock untuk mengajak pet berlari sebentar"), chase));

                refresh = () =>
                {
                    bool on = petOn.Checked;
                    bool custom = ids[kindPop.Selected].StartsWith("custom:", StringComparison.Ordinal);
                    kindPop.Enabled = sizeSeg.Enabled = areaSeg.Enabled = speed.Enabled = react.Enabled = chase.Enabled = on;
                    colorPop.Enabled = on && !custom;
                    SetPetDetail(kindRow, on && island.petError != null
                        ? Lang.T("Gagal dimuat, memakai kucing") + ": " + Lang.T(island.petError) : "");
                    SetPetDetail(colorRow, custom ? Lang.T("Pet buatan sendiri memakai warna dari gambarnya") : "");
                };
                refresh();

                cap(Lang.T("Pet buatan sendiri"));
                var folderRow = Row(Lang.T("Folder pet"), Lang.T("Per pet: sheet.png dan pet.json dari Aseprite; tag walk wajib, pose lain opsional"),
                    Button(Lang.T("Buka folder"), false, delegate { island.OpenPetFolder(); }));
                folderRow.Glyph = (char)0xE8B7;
                add(folderRow);

                PetLinkRows(cap, add);
            }

            void PetLinkRows(Action<string> cap, Action<KitRow> add)
            {
                cap(Lang.T("Teman pet (Online)"));

                var nameField = new KitField(Ui, false, island.PetDisplayName, 24);
                nameField.Box.Text = settings.Get("pet.link.name", "");
                nameField.Width = (int)(170 * Ui);
                Action saveName = delegate
                {
                    string clean = PetPacket.SanitizeName(nameField.Box.Text);
                    if (clean == settings.Get("pet.link.name", "")) return;
                    settings.Set("pet.link.name", clean);
                    settings.Save();
                    island.SyncLocalPetProfile();
                };
                nameField.Submit = saveName;
                nameField.Box.LostFocus += delegate { saveName(); };
                var nameRow = Row(Lang.T("Nama tampilan"), Lang.T("Muncul di atas gelembung chat pet Anda"), nameField);
                nameRow.Glyph = (char)0xE77B;
                add(nameRow);

                var customRecv = Switch("pet.link.custom", true, v =>
                {
                    settings.Set("pet.link.custom", v);
                    settings.Save();
                    island.SyncLocalPetProfile();
                });
                var customRow = Row(Lang.T("Terima custom pet dari teman"),
                    Lang.T("Unduh sprite custom pet teman secara otomatis dan simpan di cache"), customRecv);
                customRow.Glyph = (char)0xE896;
                add(customRow);

                // Host controls (Cloudflare Quick Tunnel)
                bool downloadingCf = false;
                var hostWrap = new Panel { AutoSize = false, BackColor = Color.Transparent, Height = (int)(ControlH * Ui), Width = (int)(220 * Ui) };
                KitButton copyBtn = null;
                KitButton hostBtn = null;
                KitRow hostRow = null;
                KitRow joinRow = null;
                KitField joinField = null;
                KitButton joinBtn = null;

                Action syncUi = null;

                hostBtn = Button(Lang.T("Mulai host"), true, delegate
                {
                    if (downloadingCf) return;
                    if (island.PetRoomState != PetRoomMode.Off)
                    {
                        island.StopPetRoom();
                        return;
                    }
                    if (CloudflaredTunnel.FindExecutable() == null)
                    {
                        downloadingCf = true;
                        SetPetDetail(hostRow, Lang.T("Mengunduh cloudflared..."));
                        hostBtn.Enabled = false;
                        Task.Run(() =>
                        {
                            string err = null;
                            try { CloudflaredTunnel.DownloadOfficial(); }
                            catch (Exception ex) { err = ex.Message; }
                            if (!hostRow.IsDisposed && hostRow.IsHandleCreated)
                            {
                                try
                                {
                                    hostRow.BeginInvoke(new Action(() =>
                                    {
                                        downloadingCf = false;
                                        if (err == null) island.StartPetRoomHost(true);
                                        syncUi();
                                        if (err != null && !hostRow.IsDisposed)
                                            SetPetDetail(hostRow, Lang.T("Gagal mengunduh cloudflared") + ": " + err);
                                    }));
                                }
                                catch (ObjectDisposedException) { }
                                catch (InvalidOperationException) { }
                            }
                        });
                        return;
                    }
                    island.StartPetRoomHost(true);
                });

                copyBtn = Button(Lang.T("Salin kode"), false, delegate
                {
                    string code = island.PetRoomInvite;
                    if (string.IsNullOrEmpty(code)) return;
                    try { Clipboard.SetText(code); } catch (Exception) { }
                    SetPetDetail(hostRow, Lang.T("Kode undangan disalin") + " · " + code);
                });

                hostWrap.Controls.Add(hostBtn);
                hostWrap.Controls.Add(copyBtn);
                hostRow = Row(Lang.T("Jadi host (Internet)"), Lang.T("PC Anda menjadi server sementara lewat Cloudflare Quick Tunnel (maks 4 pet)"), hostWrap);
                hostRow.Height = (int)(58 * Ui);
                hostRow.Glyph = (char)0xE774;
                add(hostRow);

                // Join controls
                var joinWrap = new Panel { AutoSize = false, BackColor = Color.Transparent, Height = (int)(ControlH * Ui), Width = (int)(260 * Ui) };
                joinField = new KitField(Ui, false, "deskmate://…#PIN", 160);
                joinBtn = Button(Lang.T("Gabung"), false, delegate
                {
                    if (island.PetRoomState != PetRoomMode.Off && !island.PetRoomIsHost)
                    {
                        island.StopPetRoom();
                        return;
                    }
                    island.StartPetRoomJoin(joinField.Box.Text);
                });
                joinField.Submit = delegate { if (joinBtn.Enabled) joinBtn.PerformClick(); };
                joinWrap.Controls.Add(joinField);
                joinWrap.Controls.Add(joinBtn);
                joinRow = Row(Lang.T("Gabung ke teman"), Lang.T("Tempel kode undangan dari host lalu tekan Gabung"), joinWrap);
                joinRow.Height = (int)(58 * Ui);
                joinRow.Glyph = (char)0xE71B;
                add(joinRow);

                syncUi = () =>
                {
                    bool hostAlive = !hostRow.IsDisposed;
                    bool joinAlive = !joinRow.IsDisposed;
                    if (!hostAlive && !joinAlive) return;

                    var mode = island.PetRoomState;
                    bool active = mode != PetRoomMode.Off;
                    bool isHost = island.PetRoomIsHost || mode == PetRoomMode.Hosting;
                    bool hasCf = CloudflaredTunnel.FindExecutable() != null;
                    string code = island.PetRoomInvite;
                    string status = island.PetRoomStatus;
                    int gap = (int)(6 * Ui);

                    if (hostAlive)
                    {
                        hostBtn.Text = isHost ? Lang.T("Berhenti") : (hasCf ? Lang.T("Mulai host") : Lang.T("Pasang & Host"));
                        hostBtn.Primary = !isHost;
                        hostBtn.Enabled = !downloadingCf && (!active || isHost);
                        bool showCopy = isHost && !string.IsNullOrEmpty(code);
                        LayoutPetHostControls(hostRow, hostWrap, hostBtn, copyBtn, Ui, showCopy);
                        if (hostRow.Parent != null) PlaceField(hostRow);
                        hostBtn.Invalidate();
                        copyBtn.Invalidate();
                        hostWrap.Invalidate();
                        hostRow.Invalidate();

                        if (isHost)
                        {
                            string detail = !string.IsNullOrEmpty(code)
                                ? status + " · " + code
                                : (string.IsNullOrEmpty(status) ? Lang.T("Membuka Cloudflare Tunnel...") : status);
                            SetPetDetail(hostRow, detail);
                        }
                        else if (active)
                        {
                            SetPetDetail(hostRow, Lang.T("Putuskan koneksi terlebih dahulu untuk menjadi host"));
                        }
                        else if (!downloadingCf)
                        {
                            SetPetDetail(hostRow, Lang.T("PC Anda menjadi server sementara lewat Cloudflare Quick Tunnel (maks 4 pet)"));
                        }
                    }

                    if (joinAlive)
                    {
                        joinBtn.Text = (active && !isHost) ? Lang.T("Putuskan") : Lang.T("Gabung");
                        joinBtn.Enabled = !isHost;
                        joinField.Enabled = !active;
                        int jbw = Math.Max((int)(78 * Ui), TextRenderer.MeasureText(joinBtn.Text, joinBtn.Font, Size.Empty, TextFormatFlags.NoPrefix).Width + (int)(24 * Ui));
                        joinField.Bounds = new Rectangle(0, 0, joinWrap.Width - jbw - gap, joinWrap.Height);
                        joinBtn.Bounds = new Rectangle(joinField.Right + gap, 0, jbw, joinWrap.Height);
                        joinBtn.Invalidate();
                        joinWrap.Invalidate();
                        joinRow.Invalidate();

                        if (isHost)
                        {
                            SetPetDetail(joinRow, Lang.T("Berhenti menjadi host untuk bergabung ke teman lain"));
                        }
                        else if (active)
                        {
                            SetPetDetail(joinRow, string.IsNullOrEmpty(status) ? Lang.T("Menghubungkan ke host...") : status);
                        }
                        else
                        {
                            SetPetDetail(joinRow, string.IsNullOrEmpty(status)
                                ? Lang.T("Tempel kode undangan dari host lalu tekan Gabung")
                                : status);
                        }
                    }
                };

                Action onChanged = () =>
                {
                    Control target = (!hostRow.IsDisposed && hostRow.IsHandleCreated) ? (Control)hostRow
                        : ((!joinRow.IsDisposed && joinRow.IsHandleCreated) ? (Control)joinRow : null);
                    if (target == null) return;
                    try { target.BeginInvoke(syncUi); }
                    catch (ObjectDisposedException) { }
                    catch (InvalidOperationException) { }
                };
                EventHandler onDisposed = delegate
                {
                    if (hostRow.IsDisposed && joinRow.IsDisposed) island.PetRoomChanged -= onChanged;
                };
                island.PetRoomChanged += onChanged;
                hostRow.Disposed += onDisposed;
                joinRow.Disposed += onDisposed;
                hostRow.HandleCreated += delegate { syncUi(); };
                joinRow.HandleCreated += delegate { syncUi(); };
                syncUi();
            }

            static void SetPetDetail(KitRow row, string detail)
            {
                if (row.Detail == detail) return;
                row.Detail = detail;
                if (row.Field != null) row.Field.AccessibleDescription = detail;
                row.Invalidate();
            }
        }

        internal static void LayoutPetHostControls(KitRow hostRow, Panel hostWrap, KitButton hostBtn, KitButton copyBtn, float ui, bool showCopy)
        {
            copyBtn.Visible = showCopy;
            int gap = (int)(6 * ui);
            int hw = Math.Max((int)(96 * ui), TextRenderer.MeasureText(hostBtn.Text, hostBtn.Font, Size.Empty, TextFormatFlags.NoPrefix).Width + (int)(28 * ui));
            int cw = showCopy ? Math.Max((int)(88 * ui), TextRenderer.MeasureText(copyBtn.Text, copyBtn.Font, Size.Empty, TextFormatFlags.NoPrefix).Width + (int)(28 * ui)) : 0;
            hostWrap.Width = hw + (showCopy ? gap + cw : 0);
            hostBtn.Bounds = new Rectangle(0, 0, hw, hostWrap.Height);
            if (showCopy) copyBtn.Bounds = new Rectangle(hw + gap, 0, cw, hostWrap.Height);
            if (hostRow.Parent != null && hostRow.Field != null)
            {
                hostRow.Field.Left = hostRow.Width - hostRow.Field.Width - (int)(16 * ui);
                hostRow.Field.Top = (hostRow.Height - hostRow.Field.Height) / 2;
            }
        }
    }
}
