using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Windows.Forms;

namespace Deskmate
{
    // Connects Island's desktop pet to PetRoomSession: manages remote PetWindows, lazy custom-pet
    // sprite caching + hot-swap, and chat/emote routing between devices.
    partial class Island
    {
        PetRoomSession petRoom;
        readonly Dictionary<string, PetWindow> remotePets = new Dictionary<string, PetWindow>(StringComparer.Ordinal);
        readonly Dictionary<string, PetPeerProfile> remoteProfiles = new Dictionary<string, PetPeerProfile>(StringComparer.Ordinal);
        // Peers whose custom pet is still being fetched: peer id -> asset hash
        readonly Dictionary<string, string> pendingPeerAssets = new Dictionary<string, string>(StringComparer.Ordinal);
        PetChatInput petChatInput;
        public event Action PetRoomChanged;

        internal bool PetRoomConnected { get { return petRoom != null && petRoom.Mode == PetRoomMode.Connected; } }
        internal PetRoomMode PetRoomState { get { return petRoom != null ? petRoom.Mode : PetRoomMode.Off; } }
        internal string PetRoomInvite { get { return petRoom != null ? (petRoom.InviteCode ?? "") : ""; } }
        internal string PetRoomStatus { get { return petRoom != null ? (petRoom.StatusText ?? "") : ""; } }
        internal bool PetRoomIsHost { get { return petRoom != null && petRoom.IsHost; } }

        internal string PetDisplayName
        {
            get
            {
                // Never the Windows login name: the room may be reached over a public tunnel
                string n = PetPacket.SanitizeName(settings.Get("pet.link.name", ""));
                return n.Length > 0 ? n : Lang.T("Teman");
            }
        }

        internal bool PetAllowCustomReceive { get { return settings.GetBool("pet.link.custom", true); } }

        PetRoomSession EnsurePetRoom()
        {
            if (petRoom != null) return petRoom;
            petRoom = new PetRoomSession { AllowCustomAssets = PetAllowCustomReceive };
            petRoom.StateChanged += () => RunOnUi(OnPetRoomStateChanged);
            petRoom.PeerJoined += p => RunOnUi(() => OnRemotePeerJoined(p));
            petRoom.PeerProfileUpdated += p => RunOnUi(() => OnRemotePeerUpdated(p));
            petRoom.PeerLeft += id => RunOnUi(() => OnRemotePeerLeft(id));
            petRoom.PeerStateReceived += (id, x, dir, mode, hop, v) => RunOnUi(() => OnRemotePeerState(id, x, dir, mode, hop, v));
            petRoom.ChatReceived += (id, text, emote) => RunOnUi(() => OnRemoteChatReceived(id, text, emote));
            petRoom.EmoteReceived += (id, emote) => RunOnUi(() => OnRemoteEmoteReceived(id, emote));
            petRoom.PrankReceived += (id, kind) => RunOnUi(() => OnRemotePrankReceived(id, kind));
            petRoom.CustomAssetReady += (id, hash, json, png) => RunOnUi(() => OnRemoteCustomAssetReady(id, hash, json, png));
            return petRoom;
        }

        void RunOnUi(Action action)
        {
            if (IsDisposed) return;
            try
            {
                if (InvokeRequired) BeginInvoke(action);
                else action();
            }
            catch (ObjectDisposedException) { }
            catch (InvalidOperationException) { }
        }

        PetPeerProfile BuildLocalPetProfile(out string customDir)
        {
            customDir = null;
            string kind = PetKind;
            string color = settings.Get("pet.color", "orange");
            string size = settings.Get("pet.size", "m");
            string assetHash = "";
            long assetBytes = 0;

            if (kind.StartsWith("custom:", StringComparison.Ordinal))
            {
                string name = kind.Substring(7);
                if (name.Length > 0 && name != "." && name != ".." && name.IndexOfAny(Path.GetInvalidFileNameChars()) < 0)
                {
                    string dir = Path.Combine(PetSheet.Folder, name);
                    if (Directory.Exists(dir))
                    {
                        try
                        {
                            long total;
                            string h = PetSheet.ComputeHash(dir, out total);
                            if (!string.IsNullOrEmpty(h))
                            {
                                customDir = dir;
                                assetHash = h;
                                assetBytes = total;
                            }
                        }
                        catch (Exception ex) { Log.Write("pet hash: " + ex.Message); }
                    }
                }
            }

            return new PetPeerProfile
            {
                Name = PetDisplayName,
                Kind = kind,
                Color = color,
                Size = size,
                AssetHash = assetHash,
                AssetBytes = assetBytes
            };
        }

        internal void StartPetRoomHost(bool useCloudflareTunnel)
        {
            if (!PetOn) SetPetChoice("pet.on", true);
            string dir;
            var prof = BuildLocalPetProfile(out dir);
            var room = EnsurePetRoom();
            room.AllowCustomAssets = PetAllowCustomReceive;
            room.StartHost(prof, dir, useCloudflareTunnel);
        }

        internal void StartPetRoomJoin(string inviteCode)
        {
            if (!PetOn) SetPetChoice("pet.on", true);
            string dir;
            var prof = BuildLocalPetProfile(out dir);
            var room = EnsurePetRoom();
            room.AllowCustomAssets = PetAllowCustomReceive;
            room.StartJoin(inviteCode, prof, dir);
        }

        internal void StopPetRoom()
        {
            if (petRoom != null) petRoom.Stop();
            CloseRemotePets();
            ClosePetChatUi();
            ClosePrankOverlay();
        }

        internal void SyncLocalPetProfile()
        {
            if (petRoom == null || petRoom.Mode == PetRoomMode.Off) return;
            petRoom.AllowCustomAssets = PetAllowCustomReceive;
            string dir;
            var prof = BuildLocalPetProfile(out dir);
            petRoom.SetLocalProfile(prof, dir);
        }

        internal bool PublishLocalPetState(double xNorm, int dir, int mode, bool hop, bool force, double v = 0)
        {
            return petRoom != null && petRoom.Mode == PetRoomMode.Connected &&
                petRoom.SendState(xNorm, dir, mode, hop, force, v);
        }

        internal void SendPetChat(string text, int emote)
        {
            if (petRoom == null || petRoom.Mode != PetRoomMode.Connected) return;
            string clean = PetPacket.SanitizeChat(text);
            if (clean.Length == 0) return;

            petRoom.SendChat(clean, emote);
            if (pet != null && !pet.IsDisposed)
            {
                pet.ShowChatBubble(PetDisplayName, clean, false, () => OpenPetChatInput(pet));
                if (emote >= 0 && emote <= (int)PetEmote.Scratch) pet.PopEmote((PetEmote)emote);
            }
        }

        void SendPetEmote(PetEmote kind)
        {
            if (pet != null && !pet.IsDisposed) pet.PopEmote(kind);
            if (petRoom != null && petRoom.Mode == PetRoomMode.Connected)
                petRoom.SendEmote((int)kind);
        }

        void OpenPetChatInput(PetWindow anchor)
        {
            if (!PetRoomConnected || anchor == null || anchor.IsDisposed || !anchor.CanShowBubble) return;
            ClosePetChatUi();
            var target = pet != null && !pet.IsDisposed && pet.CanShowBubble ? pet : anchor;
            // Opened from a friend's pet: that friend can also be pranked
            string prankPeer = anchor.IsRemote ? anchor.PeerId : null;
            Action<PetPrank> onPrank = null;
            if (!string.IsNullOrEmpty(prankPeer)) onPrank = kind => SendPetPrank(prankPeer, kind);
            petChatInput = new PetChatInput(target.UiScale, target.WorkArea.Width,
                (text, emote) => SendPetChat(text, emote),
                em => SendPetEmote(em), null, onPrank, PrankLabel);
            petChatInput.FormClosed += delegate { petChatInput = null; };
            if (!petChatInput.MoveAbove(target.WorkArea, new Point(target.DrawnCenterX, target.BubbleAnchorY)))
            {
                petChatInput.Dispose();
                petChatInput = null;
                return;
            }
            petChatInput.Show(target);
            petChatInput.FocusInput();
        }

        void ClosePetChatUi()
        {
            if (petChatInput != null && !petChatInput.IsDisposed) petChatInput.Close();
            petChatInput = null;
        }

        void OnPetRoomStateChanged()
        {
            var h = PetRoomChanged;
            if (h != null) h();
            if (petRoom != null && petRoom.Mode == PetRoomMode.Connected && pet != null && !pet.IsDisposed)
                PublishLocalPetState(pet.NormX, 1, 0, false, true);
        }

        void OnRemotePeerJoined(PetPeerProfile profile)
        {
            if (!PetOn || profile == null || string.IsNullOrEmpty(profile.Id)) return;
            remoteProfiles[profile.Id] = profile;

            PetWindow win;
            if (!remotePets.TryGetValue(profile.Id, out win) || win.IsDisposed)
            {
                var dock = dockWin != null && !dockWin.IsDisposed ? dockWin : null;
                var screen = dock != null && dock.PetScreen != null ? dock.PetScreen : ChosenScreen();
                IntPtr mon = MonitorForScreen(screen);
                Rectangle work = screen.WorkingArea.IsEmpty ? screen.Bounds : screen.WorkingArea;
                float s = MonitorScale(mon);

                win = new PetWindow(work, s, mon, () => dormant || hiddenByUser)
                {
                    Isle = this,
                    IsRemote = true,
                    PeerId = profile.Id,
                    PeerName = profile.Name,
                    Text = "Deskmate Pet (" + profile.Name + ")"
                };
                win.Use(ResolvePeerSheet(profile, true), SizePx(profile.Size), 100);
                win.Show();
                win.Owner = dock;
                win.Floor(dock != null ? dock.PetFloor() : Rectangle.Empty, dock != null);
                remotePets[profile.Id] = win;
            }
            else
            {
                win.PeerName = profile.Name;
                win.Use(ResolvePeerSheet(profile, true), SizePx(profile.Size), 100);
            }

            if (pet != null && !pet.IsDisposed)
                PublishLocalPetState(pet.NormX, 1, 0, false, true);
        }

        void OnRemotePeerUpdated(PetPeerProfile profile)
        {
            if (profile == null || string.IsNullOrEmpty(profile.Id)) return;
            remoteProfiles[profile.Id] = profile;
            PetWindow win;
            if (remotePets.TryGetValue(profile.Id, out win) && !win.IsDisposed)
            {
                win.PeerName = profile.Name;
                win.Use(ResolvePeerSheet(profile, true), SizePx(profile.Size), 100);
            }
        }

        void OnRemotePeerLeft(string peerId)
        {
            remoteProfiles.Remove(peerId);
            pendingPeerAssets.Remove(peerId);
            PetWindow win;
            if (remotePets.TryGetValue(peerId, out win))
            {
                remotePets.Remove(peerId);
                if (!win.IsDisposed) win.Close();
            }
        }

        void OnRemotePeerState(string peerId, double xNorm, int dir, int mode, bool hop, double v)
        {
            PetWindow win;
            if (!remotePets.TryGetValue(peerId, out win) || win.IsDisposed) return;
            // Still showing a placeholder: ask again (the session waits AssetRetryAfter between asks)
            string waiting;
            if (petRoom != null && pendingPeerAssets.TryGetValue(peerId, out waiting))
                petRoom.RequestCustomAsset(peerId, waiting);
            PetMode m = (mode >= 0 && mode <= (int)PetMode.Scratch) ? (PetMode)mode : PetMode.Idle;
            win.ApplyRemoteState(xNorm, dir, m, hop, v);
        }

        void OnRemoteChatReceived(string peerId, string text, int emote)
        {
            PetWindow win;
            if (!remotePets.TryGetValue(peerId, out win) || win.IsDisposed) return;
            PetPeerProfile prof;
            string sender = remoteProfiles.TryGetValue(peerId, out prof) ? prof.Name : (win.PeerName ?? "Teman");

            // Turn both pets to face each other when chatting
            if (pet != null && !pet.IsDisposed)
            {
                win.FacePoint(pet.DrawnCenterX);
                pet.FacePoint(win.DrawnCenterX);
            }

            if (emote >= 0 && emote <= (int)PetEmote.Scratch) win.PopEmote((PetEmote)emote);
            win.ShowChatBubble(sender, text, Sharing, () => OpenPetChatInput(win));
        }

        void OnRemoteEmoteReceived(string peerId, int emote)
        {
            PetWindow win;
            if (!remotePets.TryGetValue(peerId, out win) || win.IsDisposed) return;
            if (emote >= 0 && emote <= (int)PetEmote.Scratch) win.PopEmote((PetEmote)emote);
        }

        void OnRemoteCustomAssetReady(string peerId, string hash, byte[] jsonBytes, byte[] pngBytes)
        {
            if (!PetAllowCustomReceive || !IsAssetHash(hash)) return;
            string actual = PetSheet.ComputeHashBytes(jsonBytes, pngBytes);
            if (!string.Equals(actual, hash, StringComparison.OrdinalIgnoreCase))
            {
                Log.Write("pet asset hash mismatch from " + peerId);
                return;
            }
            try
            {
                var sheet = PetSheet.LoadBytes(jsonBytes, pngBytes);
                SavePetCache(hash, jsonBytes, pngBytes);
                RememberPeerSheet(hash, sheet);
                string waiting;
                if (pendingPeerAssets.TryGetValue(peerId, out waiting) && waiting == hash) pendingPeerAssets.Remove(peerId);
                PetWindow win;
                PetPeerProfile prof;
                if (remotePets.TryGetValue(peerId, out win) && !win.IsDisposed &&
                    remoteProfiles.TryGetValue(peerId, out prof) &&
                    string.Equals(prof.AssetHash, hash, StringComparison.OrdinalIgnoreCase))
                {
                    win.Use(sheet, SizePx(prof.Size), 100);
                }
            }
            catch (Exception ex)
            {
                Log.Write("pet remote custom invalid: " + ex.Message);
            }
        }

        PetSheet ResolvePeerSheet(PetPeerProfile profile, bool allowLazyPull)
        {
            pendingPeerAssets.Remove(profile.Id ?? "");
            if (profile.Kind != null && profile.Kind.StartsWith("custom:", StringComparison.Ordinal))
            {
                string hash = profile.AssetHash ?? "";
                if (PetAllowCustomReceive && IsAssetHash(hash))
                {
                    // 1. Check remote cache first
                    PetSheet cached = TryLoadPetCache(hash);
                    if (cached != null) return cached;

                    // 2. Check if user happens to have the same custom pet locally
                    string localName = profile.Kind.Substring(7);
                    if (localName.Length > 0 && localName != "." && localName != ".." &&
                        localName.IndexOfAny(Path.GetInvalidFileNameChars()) < 0)
                    {
                        string localDir = Path.Combine(PetSheet.Folder, localName);
                        if (Directory.Exists(localDir))
                        {
                            try
                            {
                                long total;
                                if (string.Equals(PetSheet.ComputeHash(localDir, out total), hash, StringComparison.OrdinalIgnoreCase))
                                    return PetSheet.Load(localDir);
                            }
                            catch (Exception) { }
                        }
                    }

                    // 3. Lazy-pull via WebSocket if within size limits, returning built-in placeholder meanwhile
                    if (allowLazyPull && petRoom != null && profile.AssetBytes > 0 &&
                        profile.AssetBytes <= PetPacket.MaxAssetJsonBytes + PetPacket.MaxAssetPngBytes)
                    {
                        petRoom.RequestCustomAsset(profile.Id, hash);
                        pendingPeerAssets[profile.Id] = hash;
                    }
                    else if (allowLazyPull && profile.AssetBytes > PetPacket.MaxAssetJsonBytes + PetPacket.MaxAssetPngBytes)
                    {
                        Log.Write("pet remote custom too large to share (" + profile.AssetBytes + " bytes), showing built-in");
                    }
                }
            }
            return PetSheet.BuiltIn(PetSheet.NormalizeBuiltInKind(profile.Kind), profile.Color ?? "orange");
        }

        static int SizePx(string size) { return size == "s" ? 32 : size == "l" ? 64 : 48; }

        // The hash names a folder under pet-cache, so it must be exactly what ComputeHashBytes makes: 16 lowercase hex
        internal static bool IsAssetHash(string hash)
        {
            if (hash == null || hash.Length != 16) return false;
            foreach (char c in hash)
                if (!((c >= '0' && c <= '9') || (c >= 'a' && c <= 'f'))) return false;
            return true;
        }

        // Decoded friends' pets by hash, so a profile update or rejoin doesn't decode the PNG again.
        // The same instance also lets PetWindow.Use skip rebuilding its atlas. A room holds 3 friends.
        static readonly Dictionary<string, PetSheet> peerSheets = new Dictionary<string, PetSheet>(StringComparer.Ordinal);

        static void RememberPeerSheet(string hash, PetSheet sheet)
        {
            if (peerSheets.Count >= 6 && !peerSheets.ContainsKey(hash)) peerSheets.Clear();
            peerSheets[hash] = sheet;
        }

        static PetSheet TryLoadPetCache(string hash)
        {
            if (!IsAssetHash(hash)) return null;
            PetSheet sheet;
            if (peerSheets.TryGetValue(hash, out sheet)) return sheet;
            try
            {
                string dir = Path.Combine(PetSheet.CacheFolder, hash);
                if (!Directory.Exists(dir)) return null;
                sheet = PetSheet.Load(dir);
                try { Directory.SetLastWriteTimeUtc(dir, DateTime.UtcNow); } catch (Exception) { }
                RememberPeerSheet(hash, sheet);
                return sheet;
            }
            catch (Exception) { return null; }
        }

        static void SavePetCache(string hash, byte[] jsonBytes, byte[] pngBytes)
        {
            try
            {
                string root = PetSheet.CacheFolder;
                Directory.CreateDirectory(root);
                string dir = Path.Combine(root, hash);
                Directory.CreateDirectory(dir);
                File.WriteAllBytes(Path.Combine(dir, "pet.json"), jsonBytes);
                File.WriteAllBytes(Path.Combine(dir, "sheet.png"), pngBytes);
                PrunePetCache(root, 20);
            }
            catch (Exception ex) { Log.Write("pet cache save: " + ex.Message); }
        }

        static void PrunePetCache(string root, int maxEntries)
        {
            try
            {
                var dirs = new List<DirectoryInfo>(new DirectoryInfo(root).GetDirectories());
                if (dirs.Count <= maxEntries) return;
                dirs.Sort((a, b) => a.LastWriteTimeUtc.CompareTo(b.LastWriteTimeUtc));
                for (int i = 0; i < dirs.Count - maxEntries; i++)
                {
                    try { dirs[i].Delete(true); } catch (Exception) { }
                }
            }
            catch (Exception) { }
        }

        void ApplyRemotePets(Rectangle work, float s, IntPtr mon, DockWindow dock)
        {
            Rectangle fl = dock != null ? dock.PetFloor() : Rectangle.Empty;
            foreach (var win in remotePets.Values)
            {
                if (win.IsDisposed) continue;
                win.Retarget(work, s, mon);
                win.Owner = dock;
                win.Floor(fl, dock != null);
            }
        }

        void DetachRemotePets()
        {
            foreach (var win in remotePets.Values)
                if (!win.IsDisposed) win.Owner = null;
        }

        void RemotePetsFollow(Rectangle floor)
        {
            foreach (var win in remotePets.Values)
                if (!win.IsDisposed) win.Floor(floor, true);
        }

        void CloseRemotePets()
        {
            var list = new List<PetWindow>(remotePets.Values);
            remotePets.Clear();
            remoteProfiles.Clear();
            pendingPeerAssets.Clear();
            foreach (var win in list)
                if (!win.IsDisposed) win.Close();
        }
    }
}
