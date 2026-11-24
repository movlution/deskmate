using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Deskmate
{
    sealed class PetPeerProfile
    {
        public string Id = "";
        public string Name = "Teman";
        public string Kind = "cat";
        public string Color = "orange";
        public string Size = "m";
        public string AssetHash = "";
        public long AssetBytes;

        public Dictionary<string, object> ToDict()
        {
            return new Dictionary<string, object>
            {
                { "id", Id ?? "" },
                { "name", string.IsNullOrEmpty(Name) ? "Teman" : Name },
                { "kind", string.IsNullOrEmpty(Kind) ? "cat" : Kind },
                { "color", string.IsNullOrEmpty(Color) ? "orange" : Color },
                { "size", string.IsNullOrEmpty(Size) ? "m" : Size },
                { "assetHash", AssetHash ?? "" },
                { "assetBytes", AssetBytes }
            };
        }

        public static PetPeerProfile FromDict(Dictionary<string, object> d)
        {
            var p = new PetPeerProfile();
            if (d == null) return p;
            p.Id = PetPacket.Str(d, "id");
            string n = PetPacket.SanitizeName(PetPacket.Str(d, "name"));
            p.Name = n.Length > 0 ? n : "Teman";
            string k = PetPacket.Str(d, "kind");
            p.Kind = string.IsNullOrEmpty(k) ? "cat" : k;
            string c = PetPacket.Str(d, "color");
            p.Color = string.IsNullOrEmpty(c) ? "orange" : c;
            string s = PetPacket.Str(d, "size");
            p.Size = (s == "s" || s == "l") ? s : "m";
            p.AssetHash = PetPacket.Str(d, "assetHash");
            p.AssetBytes = PetPacket.Long(d, "assetBytes");
            return p;
        }
    }

    sealed class PetInvite
    {
        public Uri WsUri;
        public string Pin;

        public static string Format(string publicOrLanUrl, string pin)
        {
            string host = (publicOrLanUrl ?? "").Trim();
            if (host.StartsWith("https://", StringComparison.OrdinalIgnoreCase)) host = host.Substring(8);
            else if (host.StartsWith("wss://", StringComparison.OrdinalIgnoreCase)) host = host.Substring(6);
            else if (host.StartsWith("http://", StringComparison.OrdinalIgnoreCase)) host = "ws://" + host.Substring(7);
            host = host.TrimEnd('/');
            string cleanPin = (pin ?? "").Trim().ToUpperInvariant().Replace("-", "");
            if (host.StartsWith("ws://", StringComparison.OrdinalIgnoreCase))
                return host + "#" + cleanPin;
            return "deskmate://" + host + "#" + cleanPin;
        }

        public static bool TryParse(string raw, out PetInvite invite, out string error)
        {
            invite = null;
            error = null;
            if (string.IsNullOrWhiteSpace(raw)) { error = "Kode undangan kosong"; return false; }

            string s = raw.Trim();
            int hash = s.LastIndexOf('#');
            if (hash <= 0 || hash >= s.Length - 1) { error = "Format kode tidak valid (harus memuat #PIN)"; return false; }

            string addr = s.Substring(0, hash).Trim();
            string pin = s.Substring(hash + 1).Trim().ToUpperInvariant().Replace("-", "");
            if (pin.Length < 4 || pin.Length > 12) { error = "PIN tidak valid"; return false; }

            string wsUrl;
            if (addr.StartsWith("deskmate://", StringComparison.OrdinalIgnoreCase))
            {
                string host = addr.Substring(11).TrimEnd('/');
                bool isLocal = host.StartsWith("127.0.0.1", StringComparison.OrdinalIgnoreCase) ||
                               host.StartsWith("localhost", StringComparison.OrdinalIgnoreCase) ||
                               host.StartsWith("192.168.", StringComparison.OrdinalIgnoreCase) ||
                               host.StartsWith("10.", StringComparison.OrdinalIgnoreCase);
                wsUrl = (isLocal ? "ws://" : "wss://") + host + "/ws";
            }
            else if (addr.StartsWith("wss://", StringComparison.OrdinalIgnoreCase) || addr.StartsWith("ws://", StringComparison.OrdinalIgnoreCase))
            {
                wsUrl = addr.EndsWith("/ws", StringComparison.OrdinalIgnoreCase) ? addr : addr.TrimEnd('/') + "/ws";
            }
            else if (addr.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            {
                wsUrl = "wss://" + addr.Substring(8).TrimEnd('/') + "/ws";
            }
            else if (addr.StartsWith("http://", StringComparison.OrdinalIgnoreCase))
            {
                wsUrl = "ws://" + addr.Substring(7).TrimEnd('/') + "/ws";
            }
            else
            {
                bool isLocal = addr.StartsWith("127.0.0.1", StringComparison.OrdinalIgnoreCase) ||
                               addr.StartsWith("localhost", StringComparison.OrdinalIgnoreCase) ||
                               addr.StartsWith("192.168.", StringComparison.OrdinalIgnoreCase) ||
                               addr.StartsWith("10.", StringComparison.OrdinalIgnoreCase);
                wsUrl = (isLocal ? "ws://" : "wss://") + addr.TrimEnd('/') + "/ws";
            }

            Uri uri;
            if (!Uri.TryCreate(wsUrl, UriKind.Absolute, out uri) || (uri.Scheme != "ws" && uri.Scheme != "wss") || string.IsNullOrEmpty(uri.Host))
            {
                error = "Alamat host tidak valid";
                return false;
            }
            invite = new PetInvite { WsUri = uri, Pin = pin };
            return true;
        }
    }

    static class PetPacket
    {
        public const int MaxChatChars = 240;
        public const int MaxAssetJsonBytes = 64 * 1024;
        public const int MaxAssetPngBytes = 512 * 1024;
        public const int AssetChunkRawBytes = 12 * 1024; // ~16 KB base64, well under 32 KB frame limit

        public static string Str(Dictionary<string, object> d, string key)
        {
            object v;
            if (d == null || !d.TryGetValue(key, out v) || v == null) return "";
            return Convert.ToString(v, CultureInfo.InvariantCulture) ?? "";
        }

        public static double Num(Dictionary<string, object> d, string key, double fallback = 0)
        {
            object v;
            if (d == null || !d.TryGetValue(key, out v) || v == null) return fallback;
            try { return Convert.ToDouble(v, CultureInfo.InvariantCulture); }
            catch (Exception) { return fallback; }
        }

        public static int Int(Dictionary<string, object> d, string key, int fallback = 0)
        {
            return (int)Math.Round(Num(d, key, fallback));
        }

        public static long Long(Dictionary<string, object> d, string key, long fallback = 0)
        {
            object v;
            if (d == null || !d.TryGetValue(key, out v) || v == null) return fallback;
            try { return Convert.ToInt64(v, CultureInfo.InvariantCulture); }
            catch (Exception) { return fallback; }
        }

        public static bool Bool(Dictionary<string, object> d, string key)
        {
            object v;
            if (d == null || !d.TryGetValue(key, out v) || v == null) return false;
            if (v is bool) return (bool)v;
            string s = Convert.ToString(v, CultureInfo.InvariantCulture);
            return string.Equals(s, "true", StringComparison.OrdinalIgnoreCase) || s == "1";
        }

        public static string SanitizeName(string text)
        {
            if (string.IsNullOrEmpty(text)) return "";
            var sb = new StringBuilder();
            foreach (char c in text.Trim())
            {
                if (c >= 32 && c != 127) sb.Append(c);
                if (sb.Length >= 24) break;
            }
            return sb.ToString().Trim();
        }

        public static string SanitizeChat(string text)
        {
            if (string.IsNullOrEmpty(text)) return "";
            var sb = new StringBuilder();
            foreach (char c in text.Trim())
            {
                if (c == '\r' || c == '\n' || c == '\t') sb.Append(' ');
                else if (c >= 32 && c != 127) sb.Append(c);
                if (sb.Length >= MaxChatChars) break;
            }
            return sb.ToString().Trim();
        }

        public static string EncodeHello(string pin, PetPeerProfile profile)
        {
            var d = profile.ToDict();
            d["t"] = "hello";
            d["pin"] = pin ?? "";
            return Json.Encode(d);
        }

        public static string EncodeProfileUpdate(PetPeerProfile profile)
        {
            var d = profile.ToDict();
            d["t"] = "profile";
            return Json.Encode(d);
        }

        public static string EncodeRoom(string hostId, string yourId, List<PetPeerProfile> peers)
        {
            var list = new List<object>();
            foreach (var p in peers) list.Add(p.ToDict());
            return Json.Encode(new Dictionary<string, object>
            {
                { "t", "room" },
                { "hostId", hostId },
                { "yourId", yourId },
                { "peers", list }
            });
        }

        public static string EncodeJoin(PetPeerProfile peer)
        {
            return Json.Encode(new Dictionary<string, object>
            {
                { "t", "join" },
                { "peer", peer.ToDict() }
            });
        }

        public static string EncodeLeave(string id)
        {
            return Json.Encode(new Dictionary<string, object>
            {
                { "t", "leave" },
                { "id", id ?? "" }
            });
        }

        public static string EncodeState(string id, double xNorm, int dir, int mode, bool hop)
        {
            return Json.Encode(new Dictionary<string, object>
            {
                { "t", "state" },
                { "id", id ?? "" },
                { "x", Math.Round(Math.Max(0, Math.Min(1, xNorm)), 4) },
                { "dir", dir >= 0 ? 1 : -1 },
                { "mode", mode },
                { "hop", hop }
            });
        }

        public static string EncodeChat(string id, string text, int emote)
        {
            return Json.Encode(new Dictionary<string, object>
            {
                { "t", "chat" },
                { "id", id ?? "" },
                { "text", SanitizeChat(text) },
                { "emote", emote }
            });
        }

        public static string EncodeEmote(string id, int emote)
        {
            return Json.Encode(new Dictionary<string, object>
            {
                { "t", "emote" },
                { "id", id ?? "" },
                { "emote", emote }
            });
        }

        public static string EncodeAssetReq(string fromId, string toId, string hash)
        {
            return Json.Encode(new Dictionary<string, object>
            {
                { "t", "asset_req" },
                { "id", fromId ?? "" },
                { "to", toId ?? "" },
                { "hash", hash ?? "" }
            });
        }

        public static string EncodeAssetChunk(string fromId, string toId, string hash, string file, int seq, int total, string base64)
        {
            return Json.Encode(new Dictionary<string, object>
            {
                { "t", "asset_chunk" },
                { "id", fromId ?? "" },
                { "to", toId ?? "" },
                { "hash", hash ?? "" },
                { "file", file },
                { "seq", seq },
                { "total", total },
                { "data", base64 }
            });
        }

        public static string EncodeError(string code, string msg)
        {
            return Json.Encode(new Dictionary<string, object>
            {
                { "t", "err" },
                { "code", code ?? "" },
                { "msg", msg ?? "" }
            });
        }
    }

    // Guest WebSocket client built on .NET 4.8 ClientWebSocket.
    sealed class PetLinkClient : IDisposable
    {
        ClientWebSocket ws;
        CancellationTokenSource cts;
        readonly SemaphoreSlim sendLock = new SemaphoreSlim(1, 1);
        volatile bool connected;
        volatile bool stopped;

        public bool Connected { get { return connected; } }

        public event Action<Dictionary<string, object>> MessageReceived;
        public event Action<string> Disconnected;

        public void Connect(Uri uri, string pin, PetPeerProfile profile)
        {
            Stop();
            stopped = false;
            ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
            cts = new CancellationTokenSource();
            ws = new ClientWebSocket();
            ws.Options.KeepAliveInterval = TimeSpan.FromSeconds(20);

            Task.Run(() => RunLoop(uri, pin, profile, cts.Token));
        }

        async Task RunLoop(Uri uri, string pin, PetPeerProfile profile, CancellationToken token)
        {
            string reason = "Terputus dari host";
            try
            {
                using (var connectTimeout = CancellationTokenSource.CreateLinkedTokenSource(token))
                {
                    connectTimeout.CancelAfter(12000);
                    await ws.ConnectAsync(uri, connectTimeout.Token).ConfigureAwait(false);
                }
                connected = true;
                await SendAsync(PetPacket.EncodeHello(pin, profile), token).ConfigureAwait(false);

                var buf = new byte[PetHost.MaxFrameBytes + 1024];
                var ms = new MemoryStream();
                while (!token.IsCancellationRequested && ws.State == WebSocketState.Open)
                {
                    ms.SetLength(0);
                    WebSocketReceiveResult res;
                    do
                    {
                        res = await ws.ReceiveAsync(new ArraySegment<byte>(buf), token).ConfigureAwait(false);
                        if (res.MessageType == WebSocketMessageType.Close)
                        {
                            reason = "Host menutup ruang";
                            goto Done;
                        }
                        if (ms.Length + res.Count > PetHost.MaxFrameBytes * 2)
                        {
                            reason = "Paket terlalu besar";
                            goto Done;
                        }
                        ms.Write(buf, 0, res.Count);
                    } while (!res.EndOfMessage);

                    if (res.MessageType == WebSocketMessageType.Text)
                    {
                        string text = Encoding.UTF8.GetString(ms.ToArray());
                        var dict = Json.Parse(text) as Dictionary<string, object>;
                        if (dict != null)
                        {
                            if (PetPacket.Str(dict, "t") == "err")
                            {
                                string m = PetPacket.Str(dict, "msg");
                                if (!string.IsNullOrEmpty(m)) reason = m;
                            }
                            var h = MessageReceived;
                            if (h != null) h(dict);
                        }
                    }
                }
            }
            catch (OperationCanceledException)
            {
                reason = stopped ? "" : "Waktu koneksi habis";
            }
            catch (WebSocketException ex)
            {
                reason = stopped ? "" : "Host tidak dapat dihubungi (" + ex.Message + ")";
            }
            catch (Exception ex)
            {
                reason = stopped ? "" : ex.Message;
            }

        Done:
            connected = false;
            try
            {
                if (ws != null && ws.State == WebSocketState.Open)
                    await ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "bye", CancellationToken.None).ConfigureAwait(false);
            }
            catch (Exception) { }
            if (!stopped)
            {
                var d = Disconnected;
                if (d != null) d(reason);
            }
        }

        public void Send(string json)
        {
            if (!connected || stopped || ws == null || cts == null) return;
            var token = cts.Token;
            Task.Run(() => SendAsync(json, token));
        }

        async Task SendAsync(string json, CancellationToken token)
        {
            if (ws == null || ws.State != WebSocketState.Open) return;
            byte[] bytes = Encoding.UTF8.GetBytes(json);
            await sendLock.WaitAsync(token).ConfigureAwait(false);
            try
            {
                if (ws != null && ws.State == WebSocketState.Open)
                    await ws.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, token).ConfigureAwait(false);
            }
            catch (Exception) { }
            finally { sendLock.Release(); }
        }

        public void Stop()
        {
            stopped = true;
            connected = false;
            try { if (cts != null) cts.Cancel(); } catch (Exception) { }
            try { if (ws != null) ws.Abort(); } catch (Exception) { }
            try { if (ws != null) ws.Dispose(); } catch (Exception) { }
            ws = null;
            cts = null;
        }

        public void Dispose() { Stop(); sendLock.Dispose(); }
    }

    enum PetRoomMode { Off, Hosting, Joining, Connected }

    // Unified room session for both Host (PC B) and Guest (PC A), including lazy custom-pet asset transfer.
    sealed class PetRoomSession : IDisposable
    {
        readonly object sync = new object();
        readonly Dictionary<string, PetPeerProfile> peers = new Dictionary<string, PetPeerProfile>(StringComparer.Ordinal);
        readonly Dictionary<string, IncomingAsset> incoming = new Dictionary<string, IncomingAsset>(StringComparer.Ordinal);
        PetHost host;
        CloudflaredTunnel tunnel;
        PetLinkClient client;
        PetPeerProfile localProfile = new PetPeerProfile();
        string localCustomDir;
        long lastStateSentTicks;
        // Bumped by every stop, so a host still starting in the background knows it was cancelled
        int generation;

        public PetRoomMode Mode { get; private set; }
        public bool IsHost { get { return host != null && host.Running; } }
        public string LocalId { get; private set; }
        public string InviteCode { get; private set; }
        public string StatusText { get; private set; }
        public bool AllowCustomAssets { get; set; }

        public event Action StateChanged;
        public event Action<PetPeerProfile> PeerJoined;
        public event Action<PetPeerProfile> PeerProfileUpdated;
        public event Action<string> PeerLeft;
        public event Action<string, double, int, int, bool> PeerStateReceived;
        public event Action<string, string, int> ChatReceived;
        public event Action<string, int> EmoteReceived;
        public event Action<string, string, byte[], byte[]> CustomAssetReady;

        sealed class IncomingAsset
        {
            public string PeerId, Hash;
            public Dictionary<int, byte[]> JsonParts = new Dictionary<int, byte[]>();
            public Dictionary<int, byte[]> PngParts = new Dictionary<int, byte[]>();
            public int JsonTotal = -1, PngTotal = -1;
            public long StartedTicks;
        }

        internal static TimeSpan AssetRetryAfter = TimeSpan.FromSeconds(30);

        public PetRoomSession()
        {
            AllowCustomAssets = true;
            StatusText = "";
        }

        public List<PetPeerProfile> GetPeers()
        {
            lock (sync) { return new List<PetPeerProfile>(peers.Values); }
        }

        public void SetLocalProfile(PetPeerProfile profile, string customDir)
        {
            // ApplyPet runs on every dock or display change; only tell the room when something it sees changed
            var old = localProfile;
            if (old != null && customDir == localCustomDir && old.Name == profile.Name && old.Kind == profile.Kind &&
                old.Color == profile.Color && old.Size == profile.Size && old.AssetHash == profile.AssetHash &&
                old.AssetBytes == profile.AssetBytes) return;
            localProfile = profile;
            localCustomDir = customDir;
            if (!string.IsNullOrEmpty(LocalId)) localProfile.Id = LocalId;
            if (IsHost) host.UpdateHostProfile(localProfile);
            else if (client != null && client.Connected) client.Send(PetPacket.EncodeProfileUpdate(localProfile));
        }

        public void StartHost(PetPeerProfile profile, string customDir, bool useCloudflareTunnel)
        {
            Stop();
            int gen = generation;
            localProfile = profile;
            localCustomDir = customDir;
            Mode = PetRoomMode.Hosting;
            InviteCode = "";
            StatusText = useCloudflareTunnel ? Lang.T("Membuka Cloudflare Tunnel...") : Lang.T("Memulai server lokal...");
            FireStateChanged();

            Task.Run(() =>
            {
                PetHost h = null;
                CloudflaredTunnel t = null;
                try
                {
                    h = new PetHost();
                    h.PeerJoined += OnHostPeerJoined;
                    h.PeerLeft += OnHostPeerLeft;
                    h.MessageFromGuest += OnHostMessageFromGuest;
                    h.Start(localProfile, !useCloudflareTunnel, 0);
                    lock (sync)
                    {
                        // Stopped while the server was starting: this host is no longer wanted
                        if (gen != generation) { h.Stop(); return; }
                        host = h;
                    }
                    LocalId = h.HostId;
                    localProfile.Id = LocalId;

                    if (useCloudflareTunnel)
                    {
                        t = new CloudflaredTunnel();
                        string pubUrl = t.Start(h.Port, 25000);
                        lock (sync)
                        {
                            // Stopped while the tunnel was opening (up to 25 s): close it instead of leaving cloudflared running
                            if (gen != generation) { t.Stop(); return; }
                            tunnel = t;
                        }
                        InviteCode = PetInvite.Format(pubUrl, h.Pin);
                    }
                    else
                    {
                        string ip = GetLocalIpv4();
                        InviteCode = PetInvite.Format("ws://" + ip + ":" + h.Port, h.Pin);
                    }

                    Mode = PetRoomMode.Connected;
                    StatusText = Lang.T("Menunggu teman bergabung...");
                    FireStateChanged();
                }
                catch (Exception ex)
                {
                    Log.Write("pet host start: " + ex.Message);
                    bool current;
                    lock (sync) current = gen == generation;
                    if (current) StopInternal(ex.Message);
                    else
                    {
                        if (t != null) try { t.Stop(); } catch (Exception) { }
                        if (h != null) try { h.Stop(); } catch (Exception) { }
                    }
                }
            });
        }

        public void StartJoin(string inviteRaw, PetPeerProfile profile, string customDir)
        {
            PetInvite inv;
            string err;
            if (!PetInvite.TryParse(inviteRaw, out inv, out err))
            {
                StatusText = err;
                FireStateChanged();
                return;
            }

            Stop();
            localProfile = profile;
            localCustomDir = customDir;
            LocalId = PetHost.GenerateId();
            localProfile.Id = LocalId;
            Mode = PetRoomMode.Joining;
            InviteCode = "";
            StatusText = Lang.T("Menghubungkan ke host...");
            FireStateChanged();

            var c = new PetLinkClient();
            c.MessageReceived += OnGuestMessage;
            c.Disconnected += reason => StopInternal(string.IsNullOrEmpty(reason) ? Lang.T("Host terputus") : reason);
            client = c;
            c.Connect(inv.WsUri, inv.Pin, localProfile);
        }

        public void SendState(double xNorm, int dir, int mode, bool hop, bool force)
        {
            if (Mode != PetRoomMode.Connected) return;
            long now = DateTime.UtcNow.Ticks;
            if (!force && now - lastStateSentTicks < TimeSpan.FromMilliseconds(140).Ticks) return;
            lastStateSentTicks = now;
            string json = PetPacket.EncodeState(LocalId, xNorm, dir, mode, hop);
            if (IsHost) host.BroadcastFromHost(json);
            else if (client != null && client.Connected) client.Send(json);
        }

        public void SendChat(string text, int emote)
        {
            if (Mode != PetRoomMode.Connected) return;
            string clean = PetPacket.SanitizeChat(text);
            if (clean.Length == 0) return;
            string json = PetPacket.EncodeChat(LocalId, clean, emote);
            if (IsHost) host.BroadcastFromHost(json);
            else if (client != null && client.Connected) client.Send(json);
        }

        public void SendEmote(int emote)
        {
            if (Mode != PetRoomMode.Connected) return;
            string json = PetPacket.EncodeEmote(LocalId, emote);
            if (IsHost) host.BroadcastFromHost(json);
            else if (client != null && client.Connected) client.Send(json);
        }

        public void RequestCustomAsset(string peerId, string hash)
        {
            if (!AllowCustomAssets || Mode != PetRoomMode.Connected || string.IsNullOrEmpty(hash)) return;
            lock (sync)
            {
                string key = peerId + "|" + hash;
                IncomingAsset pending;
                // A transfer that lost a chunk would block the pet forever; after a while, ask again from scratch
                if (incoming.TryGetValue(key, out pending) &&
                    DateTime.UtcNow.Ticks - pending.StartedTicks < AssetRetryAfter.Ticks) return;
                incoming[key] = new IncomingAsset { PeerId = peerId, Hash = hash, StartedTicks = DateTime.UtcNow.Ticks };
            }
            string json = PetPacket.EncodeAssetReq(LocalId, peerId, hash);
            if (IsHost) host.SendToGuest(peerId, json);
            else if (client != null && client.Connected) client.Send(json);
        }

        public void Stop() { StopInternal(""); }

        void StopInternal(string statusMessage)
        {
            PetHost h;
            CloudflaredTunnel t;
            PetLinkClient c;
            lock (sync)
            {
                generation++;
                t = tunnel; tunnel = null;
                h = host; host = null;
                c = client; client = null;
            }

            if (t != null) try { t.Stop(); } catch (Exception) { }
            if (h != null) try { h.Stop(); } catch (Exception) { }
            if (c != null) try { c.Stop(); } catch (Exception) { }

            List<string> oldIds;
            lock (sync)
            {
                oldIds = new List<string>(peers.Keys);
                peers.Clear();
                incoming.Clear();
            }
            foreach (var id in oldIds)
            {
                var pl = PeerLeft;
                if (pl != null) pl(id);
            }

            Mode = PetRoomMode.Off;
            InviteCode = "";
            StatusText = statusMessage ?? "";
            FireStateChanged();
        }

        public void Dispose() { Stop(); }

        void OnHostPeerJoined(PetPeerProfile peer)
        {
            lock (sync) { peers[peer.Id] = peer; }
            UpdateConnectedStatus();
            var pj = PeerJoined;
            if (pj != null) pj(peer);
        }

        void OnHostPeerLeft(string peerId)
        {
            lock (sync) { peers.Remove(peerId); }
            UpdateConnectedStatus();
            var pl = PeerLeft;
            if (pl != null) pl(peerId);
        }

        void OnHostMessageFromGuest(string senderId, Dictionary<string, object> msg)
        {
            HandleIncomingMessage(senderId, msg);
        }

        void OnGuestMessage(Dictionary<string, object> msg)
        {
            string t = PetPacket.Str(msg, "t");
            if (t == "room")
            {
                // The host sends one snapshot, right after the PIN is accepted
                if (Mode != PetRoomMode.Joining) return;
                string yourId = PetPacket.Str(msg, "yourId");
                if (!string.IsNullOrEmpty(yourId)) LocalId = localProfile.Id = yourId;
                Mode = PetRoomMode.Connected;
                object po;
                if (msg.TryGetValue("peers", out po))
                {
                    var arr = po as object[];
                    if (arr != null)
                    {
                        foreach (var item in arr)
                        {
                            var d = item as Dictionary<string, object>;
                            if (d == null) continue;
                            var peer = PetPeerProfile.FromDict(d);
                            if (string.IsNullOrEmpty(peer.Id) || peer.Id == LocalId) continue;
                            if (!TryAddPeer(peer)) continue;
                            var pj = PeerJoined;
                            if (pj != null) pj(peer);
                        }
                    }
                }
                UpdateConnectedStatus();
                return;
            }
            if (t == "join")
            {
                object po;
                if (msg.TryGetValue("peer", out po) && po is Dictionary<string, object>)
                {
                    var peer = PetPeerProfile.FromDict((Dictionary<string, object>)po);
                    if (!string.IsNullOrEmpty(peer.Id) && peer.Id != LocalId && TryAddPeer(peer))
                    {
                        UpdateConnectedStatus();
                        var pj = PeerJoined;
                        if (pj != null) pj(peer);
                    }
                }
                return;
            }
            if (t == "leave")
            {
                string id = PetPacket.Str(msg, "id");
                bool had;
                lock (sync) { had = peers.Remove(id); }
                if (had)
                {
                    UpdateConnectedStatus();
                    var pl = PeerLeft;
                    if (pl != null) pl(id);
                }
                return;
            }
            if (t == "err")
            {
                StatusText = PetPacket.Str(msg, "msg");
                FireStateChanged();
                return;
            }

            string senderId = PetPacket.Str(msg, "id");
            if (!string.IsNullOrEmpty(senderId) && senderId != LocalId)
                HandleIncomingMessage(senderId, msg);
        }

        // A room holds the host and MaxGuests guests, so a guest never sees more than MaxGuests others
        bool TryAddPeer(PetPeerProfile peer)
        {
            lock (sync)
            {
                if (!peers.ContainsKey(peer.Id) && peers.Count >= PetHost.MaxGuests) return false;
                peers[peer.Id] = peer;
                return true;
            }
        }

        void HandleIncomingMessage(string senderId, Dictionary<string, object> msg)
        {
            string t = PetPacket.Str(msg, "t");
            if (t == "profile")
            {
                // Profiles only update peers already in the room; they never add one
                lock (sync) { if (!peers.ContainsKey(senderId)) return; }
                var updated = PetPeerProfile.FromDict(msg);
                updated.Id = senderId;
                lock (sync) { peers[senderId] = updated; }
                UpdateConnectedStatus();
                var pu = PeerProfileUpdated;
                if (pu != null) pu(updated);
            }
            else if (t == "state")
            {
                double x = PetPacket.Num(msg, "x", 0.5);
                int dir = PetPacket.Int(msg, "dir", 1);
                int mode = PetPacket.Int(msg, "mode", 0);
                bool hop = PetPacket.Bool(msg, "hop");
                var ps = PeerStateReceived;
                if (ps != null) ps(senderId, x, dir, mode, hop);
            }
            else if (t == "chat")
            {
                string text = PetPacket.SanitizeChat(PetPacket.Str(msg, "text"));
                int emote = PetPacket.Int(msg, "emote", -1);
                if (text.Length > 0)
                {
                    var ch = ChatReceived;
                    if (ch != null) ch(senderId, text, emote);
                }
            }
            else if (t == "emote")
            {
                int emote = PetPacket.Int(msg, "emote", -1);
                var em = EmoteReceived;
                if (em != null) em(senderId, emote);
            }
            else if (t == "asset_req")
            {
                string hash = PetPacket.Str(msg, "hash");
                ServeLocalCustomAsset(senderId, hash);
            }
            else if (t == "asset_chunk")
            {
                ReceiveAssetChunk(senderId, msg);
            }
        }

        void ServeLocalCustomAsset(string toPeerId, string requestedHash)
        {
            if (string.IsNullOrEmpty(localCustomDir) || string.IsNullOrEmpty(requestedHash)) return;
            if (!string.Equals(localProfile.AssetHash, requestedHash, StringComparison.OrdinalIgnoreCase)) return;

            Task.Run(() =>
            {
                try
                {
                    string jsonPath = Path.Combine(localCustomDir, "pet.json");
                    string pngPath = Path.Combine(localCustomDir, "sheet.png");
                    if (!File.Exists(jsonPath) || !File.Exists(pngPath)) return;
                    byte[] jsonBytes = File.ReadAllBytes(jsonPath);
                    byte[] pngBytes = File.ReadAllBytes(pngPath);
                    if (jsonBytes.Length > PetPacket.MaxAssetJsonBytes || pngBytes.Length > PetPacket.MaxAssetPngBytes) return;

                    SendAssetChunks(toPeerId, requestedHash, "json", jsonBytes);
                    SendAssetChunks(toPeerId, requestedHash, "png", pngBytes);
                }
                catch (Exception ex) { Log.Write("pet asset send: " + ex.Message); }
            });
        }

        void SendAssetChunks(string toPeerId, string hash, string file, byte[] bytes)
        {
            int step = PetPacket.AssetChunkRawBytes;
            int total = Math.Max(1, (bytes.Length + step - 1) / step);
            for (int seq = 0; seq < total; seq++)
            {
                int off = seq * step;
                int len = Math.Min(step, bytes.Length - off);
                string b64 = Convert.ToBase64String(bytes, off, len);
                string pkt = PetPacket.EncodeAssetChunk(LocalId, toPeerId, hash, file, seq, total, b64);
                if (IsHost) host.SendToGuest(toPeerId, pkt);
                else if (client != null && client.Connected) client.Send(pkt);
            }
        }

        void ReceiveAssetChunk(string senderId, Dictionary<string, object> msg)
        {
            if (!AllowCustomAssets) return;
            string hash = PetPacket.Str(msg, "hash");
            string file = PetPacket.Str(msg, "file");
            int seq = PetPacket.Int(msg, "seq", -1);
            int total = PetPacket.Int(msg, "total", 0);
            string b64 = PetPacket.Str(msg, "data");
            if (string.IsNullOrEmpty(hash) || seq < 0 || total <= 0 || total > 64 || seq >= total) return;

            byte[] chunk;
            try { chunk = Convert.FromBase64String(b64); }
            catch (FormatException) { return; }

            byte[] doneJson = null, donePng = null;
            lock (sync)
            {
                string key = senderId + "|" + hash;
                IncomingAsset acc;
                if (!incoming.TryGetValue(key, out acc)) return;

                if (file == "json")
                {
                    acc.JsonTotal = total;
                    acc.JsonParts[seq] = chunk;
                }
                else if (file == "png")
                {
                    acc.PngTotal = total;
                    acc.PngParts[seq] = chunk;
                }
                else return;

                if (acc.JsonTotal > 0 && acc.JsonParts.Count == acc.JsonTotal &&
                    acc.PngTotal > 0 && acc.PngParts.Count == acc.PngTotal)
                {
                    doneJson = CombineChunks(acc.JsonParts, acc.JsonTotal, PetPacket.MaxAssetJsonBytes);
                    donePng = CombineChunks(acc.PngParts, acc.PngTotal, PetPacket.MaxAssetPngBytes);
                    incoming.Remove(key);
                }
            }

            if (doneJson != null && donePng != null)
            {
                var ready = CustomAssetReady;
                if (ready != null) ready(senderId, hash, doneJson, donePng);
            }
        }

        static byte[] CombineChunks(Dictionary<int, byte[]> parts, int total, int maxBytes)
        {
            int size = 0;
            for (int i = 0; i < total; i++)
            {
                byte[] p;
                if (!parts.TryGetValue(i, out p)) return null;
                size += p.Length;
                if (size > maxBytes) return null;
            }
            var result = new byte[size];
            int off = 0;
            for (int i = 0; i < total; i++)
            {
                var p = parts[i];
                Buffer.BlockCopy(p, 0, result, off, p.Length);
                off += p.Length;
            }
            return result;
        }

        void UpdateConnectedStatus()
        {
            List<string> names;
            lock (sync)
            {
                names = new List<string>();
                foreach (var p in peers.Values) names.Add(p.Name);
            }
            if (names.Count == 0)
                StatusText = IsHost ? Lang.T("Menunggu teman bergabung...") : Lang.T("Terhubung");
            else
                StatusText = Lang.F("Terhubung dengan {0}", string.Join(", ", names.ToArray()));
            FireStateChanged();
        }

        void FireStateChanged()
        {
            var s = StateChanged;
            if (s != null) s();
        }

        static string GetLocalIpv4()
        {
            try
            {
                foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
                {
                    if (ni.OperationalStatus != OperationalStatus.Up) continue;
                    if (ni.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;
                    foreach (var ua in ni.GetIPProperties().UnicastAddresses)
                    {
                        if (ua.Address.AddressFamily == AddressFamily.InterNetwork)
                            return ua.Address.ToString();
                    }
                }
            }
            catch (Exception) { }
            return "127.0.0.1";
        }
    }
}
