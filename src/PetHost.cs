using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;

namespace Deskmate
{
    // Embedded RFC 6455 WebSocket host so PC B acts as the server directly, plus Cloudflare Quick Tunnel wrapper.
    sealed class PetHost : IDisposable
    {
        public const int MaxGuests = 3; // 1 Host + up to 3 Guests = 4 pets total
        public const int MaxFrameBytes = 32768;
        const string WsGuid = "258EAFA5-E914-47DA-95CA-C5AB0DC85B11";
        const string PinAlphabet = "ABCDEFGHJKMNPQRSTUVWXYZ23456789";

        readonly object sync = new object();
        readonly Dictionary<string, GuestConn> guests = new Dictionary<string, GuestConn>(StringComparer.Ordinal);
        // Guests plus connections still handshaking
        internal const int MaxOpenConns = MaxGuests + 5;
        // A wrong PIN holds its socket this long, so guessing runs at most MaxOpenConns tries per delay
        internal static int BadPinDelayMs = 1500;
        int openConns;
        TcpListener listener;
        Thread acceptThread;
        volatile bool running;

        public int Port { get; private set; }
        public string Pin { get; private set; }
        public string HostId { get; private set; }
        public bool Running { get { return running; } }
        public PetPeerProfile HostProfile { get; set; }

        public event Action<PetPeerProfile> PeerJoined;
        public event Action<string> PeerLeft;
        public event Action<string, Dictionary<string, object>> MessageFromGuest;

        sealed class GuestConn
        {
            public string Id;
            public PetPeerProfile Profile;
            public TcpClient Tcp;
            public NetworkStream Stream;
            public readonly object WriteLock = new object();
            // Frames waiting for the writer; Writing is true while a pool thread drains them
            public readonly Queue<byte[]> Outbox = new Queue<byte[]>();
            public bool Writing, Dead;
            public readonly Queue<long> ChatTicks = new Queue<long>();
            public readonly Queue<long> StateTicks = new Queue<long>();
        }

        public static string GeneratePin()
        {
            var buf = new byte[6];
            using (var rng = new RNGCryptoServiceProvider()) rng.GetBytes(buf);
            var chars = new char[6];
            for (int i = 0; i < 6; i++) chars[i] = PinAlphabet[buf[i] % PinAlphabet.Length];
            return new string(chars);
        }

        public static string GenerateId()
        {
            var buf = new byte[6];
            using (var rng = new RNGCryptoServiceProvider()) rng.GetBytes(buf);
            var sb = new StringBuilder(12);
            for (int i = 0; i < buf.Length; i++) sb.Append(buf[i].ToString("x2"));
            return sb.ToString();
        }

        public void Start(PetPeerProfile hostProfile, bool lan, int port = 0)
        {
            Stop();
            HostProfile = hostProfile;
            HostId = string.IsNullOrEmpty(hostProfile.Id) ? GenerateId() : hostProfile.Id;
            HostProfile.Id = HostId;
            Pin = GeneratePin();

            listener = new TcpListener(lan ? IPAddress.Any : IPAddress.Loopback, port);
            listener.Start(8);
            Port = ((IPEndPoint)listener.LocalEndpoint).Port;
            running = true;

            acceptThread = new Thread(AcceptLoop) { IsBackground = true, Name = "DeskmatePetHost" };
            acceptThread.Start();
        }

        public void UpdateHostProfile(PetPeerProfile profile)
        {
            profile.Id = HostId;
            HostProfile = profile;
            string msg = PetPacket.EncodeProfileUpdate(profile);
            Broadcast(msg, null);
        }

        public void BroadcastFromHost(string json)
        {
            Broadcast(json, null);
        }

        public void SendToGuest(string peerId, string json)
        {
            GuestConn g;
            lock (sync) { if (!guests.TryGetValue(peerId ?? "", out g)) return; }
            SendText(g, json);
        }

        public void Stop()
        {
            running = false;
            try { if (listener != null) listener.Stop(); } catch (Exception) { }
            listener = null;

            List<GuestConn> list;
            lock (sync)
            {
                list = new List<GuestConn>(guests.Values);
                guests.Clear();
            }
            foreach (var g in list)
            {
                // Stop runs on the UI thread: say goodbye only if the socket is free right now
                if (Monitor.TryEnter(g.WriteLock, 50))
                {
                    try { WriteFrame(g, Frame(0x8, new byte[0])); } catch (Exception) { }
                    finally { Monitor.Exit(g.WriteLock); }
                }
                Kill(g);
            }
        }

        public void Dispose() { Stop(); }

        void AcceptLoop()
        {
            while (running)
            {
                TcpClient client;
                try { client = listener.AcceptTcpClient(); }
                catch (SocketException) { break; }
                catch (ObjectDisposedException) { break; }
                catch (InvalidOperationException) { break; }
                catch (Exception ex) { Log.Write("pet host accept: " + ex.Message); break; }

                // The tunnel URL is public: cap open sockets so stalled handshakes or PIN guessing can't pile up threads
                if (Interlocked.Increment(ref openConns) > MaxOpenConns)
                {
                    Interlocked.Decrement(ref openConns);
                    try { client.Close(); } catch (Exception) { }
                    continue;
                }
                client.NoDelay = true;
                var t = new Thread(() =>
                {
                    try { ServeClient(client); }
                    finally { Interlocked.Decrement(ref openConns); }
                }) { IsBackground = true, Name = "DeskmatePetGuest" };
                t.Start();
            }
        }

        void ServeClient(TcpClient tcp)
        {
            GuestConn conn = null;
            try
            {
                var stream = tcp.GetStream();
                stream.ReadTimeout = 6000;
                // A guest that stops reading fails its writer after this, instead of holding it forever
                stream.WriteTimeout = 10000;
                if (!PerformHandshake(stream)) return;

                conn = new GuestConn { Tcp = tcp, Stream = stream };

                // First frame must be hello with valid PIN
                string firstText;
                if (!ReadNextTextMessage(conn, out firstText)) return;
                var hello = Json.Parse(firstText) as Dictionary<string, object>;
                if (hello == null || PetPacket.Str(hello, "t") != "hello")
                {
                    SendErrorAndClose(conn, "bad_hello", "Handshake tidak valid");
                    return;
                }
                string pin = (PetPacket.Str(hello, "pin") ?? "").Trim().ToUpperInvariant().Replace("-", "");
                if (!PinMatches(pin, Pin))
                {
                    Thread.Sleep(BadPinDelayMs);
                    SendErrorAndClose(conn, "bad_pin", "PIN salah");
                    return;
                }

                var profile = PetPeerProfile.FromDict(hello);
                if (string.IsNullOrEmpty(profile.Id) || profile.Id == HostId) profile.Id = GenerateId();
                conn.Id = profile.Id;
                conn.Profile = profile;

                List<PetPeerProfile> existing;
                lock (sync)
                {
                    if (guests.Count >= MaxGuests)
                    {
                        SendErrorAndClose(conn, "room_full", "Ruang sudah penuh");
                        return;
                    }
                    if (guests.ContainsKey(conn.Id)) profile.Id = conn.Id = GenerateId();
                    existing = new List<PetPeerProfile> { HostProfile };
                    foreach (var kv in guests.Values) existing.Add(kv.Profile);
                    guests[conn.Id] = conn;
                }

                // Clear handshake timeout; rely on TCP / ping-pong
                stream.ReadTimeout = 60000;

                // Send room snapshot to the joining guest
                SendText(conn, PetPacket.EncodeRoom(HostId, conn.Id, existing));

                // Notify other guests and Host UI
                string joinJson = PetPacket.EncodeJoin(profile);
                Broadcast(joinJson, conn.Id);
                var pj = PeerJoined;
                if (pj != null) pj(profile);

                // Message pump
                string text;
                while (running && ReadNextTextMessage(conn, out text))
                {
                    var msg = Json.Parse(text) as Dictionary<string, object>;
                    if (msg == null) continue;
                    string type = PetPacket.Str(msg, "t");
                    if (type == "ping")
                    {
                        SendText(conn, "{\"t\":\"pong\"}");
                        continue;
                    }
                    // Only peer traffic is relayed; room, join, leave and err come from the host alone
                    if (!IsGuestType(type)) continue;
                    if (!AllowMessage(conn, type)) continue;
                    if (type == "chat")
                    {
                        string clean = PetPacket.SanitizeChat(PetPacket.Str(msg, "text"));
                        if (clean.Length == 0) continue;
                        msg["text"] = clean;
                    }
                    // Stamp authoritative sender id
                    msg["id"] = conn.Id;

                    if (type == "profile")
                    {
                        var updated = PetPeerProfile.FromDict(msg);
                        updated.Id = conn.Id;
                        conn.Profile = updated;
                    }

                    string to = PetPacket.Str(msg, "to");
                    if (type == "asset_req" || type == "asset_chunk")
                    {
                        if (string.Equals(to, HostId, StringComparison.Ordinal))
                        {
                            var h = MessageFromGuest;
                            if (h != null) h(conn.Id, msg);
                        }
                        else if (!string.IsNullOrEmpty(to))
                        {
                            SendToGuest(to, Json.Encode(msg));
                        }
                        continue;
                    }

                    // Relay to Host UI and all other guests
                    string relayJson = Json.Encode(msg);
                    Broadcast(relayJson, conn.Id);
                    var handler = MessageFromGuest;
                    if (handler != null) handler(conn.Id, msg);
                }
            }
            catch (IOException) { }
            catch (SocketException) { }
            catch (ObjectDisposedException) { }
            catch (Exception ex) { Log.Write("pet host guest: " + ex.Message); }
            finally
            {
                if (conn != null && !string.IsNullOrEmpty(conn.Id))
                {
                    bool removed = false;
                    lock (sync) { removed = guests.Remove(conn.Id); }
                    if (removed)
                    {
                        Broadcast(PetPacket.EncodeLeave(conn.Id), conn.Id);
                        var pl = PeerLeft;
                        if (pl != null) pl(conn.Id);
                    }
                }
                try { tcp.Close(); } catch (Exception) { }
            }
        }

        // Constant time, so response timing says nothing about how much of a guess was right
        static bool PinMatches(string given, string expected)
        {
            if (given == null || expected == null || given.Length != expected.Length) return false;
            int diff = 0;
            for (int i = 0; i < given.Length; i++) diff |= given[i] ^ expected[i];
            return diff == 0;
        }

        internal static bool IsGuestType(string type)
        {
            return type == "state" || type == "chat" || type == "emote" || type == "profile" ||
                   type == "asset_req" || type == "asset_chunk";
        }

        // Per guest, per 3 s: 5 chats/emotes/asset requests, 30 state/profile updates.
        // Asset chunks are only accepted by a peer that asked for them, so they are not counted.
        static bool AllowMessage(GuestConn c, string type)
        {
            if (type == "asset_chunk") return true;
            bool social = type == "chat" || type == "emote" || type == "asset_req";
            return Allow(social ? c.ChatTicks : c.StateTicks, social ? 5 : 30);
        }

        static bool Allow(Queue<long> ticks, int max)
        {
            long now = DateTime.UtcNow.Ticks;
            long window = TimeSpan.FromSeconds(3).Ticks;
            while (ticks.Count > 0 && now - ticks.Peek() > window) ticks.Dequeue();
            if (ticks.Count >= max) return false;
            ticks.Enqueue(now);
            return true;
        }

        void Broadcast(string json, string exceptId)
        {
            List<GuestConn> targets;
            lock (sync) { targets = new List<GuestConn>(guests.Values); }
            foreach (var g in targets)
            {
                if (exceptId != null && g.Id == exceptId) continue;
                SendText(g, json);
            }
        }

        // Runs on the guest's own thread, so it writes directly: the socket closes right after
        void SendErrorAndClose(GuestConn conn, string code, string msg)
        {
            try { SendFrame(conn, 0x1, Encoding.UTF8.GetBytes(PetPacket.EncodeError(code, msg))); } catch (Exception) { }
            try { SendFrame(conn, 0x8, new byte[0]); } catch (Exception) { }
        }

        static bool PerformHandshake(NetworkStream stream)
        {
            var sb = new StringBuilder();
            var buf = new byte[1];
            while (sb.Length < 4096)
            {
                int n = stream.Read(buf, 0, 1);
                if (n <= 0) return false;
                sb.Append((char)buf[0]);
                int len = sb.Length;
                if (len >= 4 && sb[len - 4] == '\r' && sb[len - 3] == '\n' && sb[len - 2] == '\r' && sb[len - 1] == '\n')
                    break;
            }
            string req = sb.ToString();
            if (!req.StartsWith("GET ", StringComparison.Ordinal)) return false;

            string wsKey = null;
            foreach (var line in req.Split(new[] { "\r\n" }, StringSplitOptions.RemoveEmptyEntries))
            {
                if (line.StartsWith("Sec-WebSocket-Key:", StringComparison.OrdinalIgnoreCase))
                {
                    wsKey = line.Substring(18).Trim();
                    break;
                }
            }
            if (string.IsNullOrEmpty(wsKey)) return false;

            string accept;
            using (var sha1 = SHA1.Create())
            {
                byte[] hash = sha1.ComputeHash(Encoding.ASCII.GetBytes(wsKey + WsGuid));
                accept = Convert.ToBase64String(hash);
            }

            string resp = "HTTP/1.1 101 Switching Protocols\r\n" +
                          "Upgrade: websocket\r\n" +
                          "Connection: Upgrade\r\n" +
                          "Sec-WebSocket-Accept: " + accept + "\r\n\r\n";
            byte[] bytes = Encoding.ASCII.GetBytes(resp);
            stream.Write(bytes, 0, bytes.Length);
            stream.Flush();
            return true;
        }

        static bool ReadNextTextMessage(GuestConn conn, out string text)
        {
            text = null;
            var stream = conn.Stream;
            while (true)
            {
                var head = new byte[2];
                if (!ReadExact(stream, head, 0, 2)) return false;

                bool fin = (head[0] & 0x80) != 0;
                int opcode = head[0] & 0x0F;
                bool masked = (head[1] & 0x80) != 0;
                long len = head[1] & 0x7F;

                if (len == 126)
                {
                    var ext = new byte[2];
                    if (!ReadExact(stream, ext, 0, 2)) return false;
                    len = (ext[0] << 8) | ext[1];
                }
                else if (len == 127)
                {
                    // Never accept > 32 KB frames
                    return false;
                }

                if (len > MaxFrameBytes || !masked) return false;

                var mask = new byte[4];
                if (!ReadExact(stream, mask, 0, 4)) return false;

                var payload = new byte[(int)len];
                if (len > 0 && !ReadExact(stream, payload, 0, (int)len)) return false;
                for (int i = 0; i < payload.Length; i++) payload[i] ^= mask[i & 3];

                if (opcode == 0x8) // Close
                {
                    try { SendFrame(conn, 0x8, new byte[0]); } catch (Exception) { }
                    return false;
                }
                if (opcode == 0x9) // Ping -> Pong
                {
                    SendFrame(conn, 0xA, payload);
                    continue;
                }
                if (opcode == 0xA) continue; // Pong
                if (opcode == 0x1 && fin)
                {
                    text = Encoding.UTF8.GetString(payload);
                    return true;
                }
                // Unsupported opcode or fragmented frame
                return false;
            }
        }

        static bool ReadExact(Stream s, byte[] buf, int off, int count)
        {
            int got = 0;
            while (got < count)
            {
                int n = s.Read(buf, off + got, count - got);
                if (n <= 0) return false;
                got += n;
            }
            return true;
        }

        // Enough for a full custom pet transfer (about 50 chunks) plus live traffic. A guest this far
        // behind is not reading, so it is dropped rather than queuing without bound.
        internal const int MaxOutbox = 256;

        // Never blocks the caller: BroadcastFromHost runs on the UI thread, and a slow tunnel or a
        // guest that stopped reading must not freeze the pet. One pool thread per guest writes in order.
        static void SendText(GuestConn conn, string text)
        {
            if (conn == null || string.IsNullOrEmpty(text)) return;
            byte[] frame = Frame(0x1, Encoding.UTF8.GetBytes(text));
            bool start = false, overflow = false;
            lock (conn.Outbox)
            {
                if (conn.Dead) return;
                if (conn.Outbox.Count >= MaxOutbox) { conn.Dead = true; conn.Outbox.Clear(); overflow = true; }
                else
                {
                    conn.Outbox.Enqueue(frame);
                    if (!conn.Writing) { conn.Writing = true; start = true; }
                }
            }
            if (overflow) { Log.Write("pet host: guest not reading, dropped"); Kill(conn); return; }
            if (start) ThreadPool.QueueUserWorkItem(_ => Drain(conn));
        }

        static void Drain(GuestConn conn)
        {
            while (true)
            {
                byte[] frame;
                lock (conn.Outbox)
                {
                    if (conn.Dead || conn.Outbox.Count == 0) { conn.Writing = false; return; }
                    frame = conn.Outbox.Dequeue();
                }
                try
                {
                    lock (conn.WriteLock) WriteFrame(conn, frame);
                }
                catch (Exception)
                {
                    lock (conn.Outbox) { conn.Dead = true; conn.Outbox.Clear(); conn.Writing = false; }
                    Kill(conn);
                    return;
                }
            }
        }

        // Closing the socket ends the guest's read loop, which then reports the leave
        static void Kill(GuestConn conn)
        {
            lock (conn.Outbox) conn.Dead = true;
            try { conn.Tcp.Close(); } catch (Exception) { }
        }

        static byte[] Frame(byte opcode, byte[] payload)
        {
            int len = payload.Length, head = len < 126 ? 2 : 4;
            var frame = new byte[head + len];
            frame[0] = (byte)(0x80 | opcode);
            if (len < 126) frame[1] = (byte)len;
            else
            {
                frame[1] = 126;
                frame[2] = (byte)(len >> 8);
                frame[3] = (byte)(len & 0xFF);
            }
            Buffer.BlockCopy(payload, 0, frame, head, len);
            return frame;
        }

        // Caller holds WriteLock
        static void WriteFrame(GuestConn conn, byte[] frame)
        {
            conn.Stream.Write(frame, 0, frame.Length);
        }

        // Direct write for the guest's own thread (pong, error, close)
        static void SendFrame(GuestConn conn, byte opcode, byte[] payload)
        {
            byte[] frame = Frame(opcode, payload);
            lock (conn.WriteLock) WriteFrame(conn, frame);
        }
    }

    // Runs cloudflared.exe quick tunnel and binds it to a Windows Job Object so it dies with Deskmate.
    sealed class CloudflaredTunnel : IDisposable
    {
        static readonly Regex UrlRx = new Regex(@"https://[a-z0-9-]+\.trycloudflare\.com", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        const string DownloadUrl = "https://github.com/cloudflare/cloudflared/releases/latest/download/cloudflared-windows-amd64.exe";

        Process proc;
        IntPtr job;

        public string PublicUrl { get; private set; }
        public bool Active { get { return proc != null && !proc.HasExited; } }

        public static string ToolsPath { get { return Path.Combine(Settings.Dir, "tools", "cloudflared.exe"); } }

        public static string ParseTryCloudflareUrl(string text)
        {
            if (string.IsNullOrEmpty(text)) return null;
            var m = UrlRx.Match(text);
            return m.Success ? m.Value.ToLowerInvariant() : null;
        }

        public static string FindExecutable()
        {
            var candidates = new List<string>
            {
                ToolsPath,
                Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "cloudflared.exe"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".local", "bin", "cloudflared.exe")
            };
            string pathEnv = Environment.GetEnvironmentVariable("PATH") ?? "";
            foreach (var dir in pathEnv.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries))
            {
                try { candidates.Add(Path.Combine(dir.Trim(), "cloudflared.exe")); } catch (Exception) { }
            }
            foreach (var c in candidates)
            {
                try { if (!string.IsNullOrEmpty(c) && File.Exists(c)) return c; } catch (Exception) { }
            }
            return null;
        }

        public string Start(int localPort, int timeoutMs = 20000)
        {
            Stop();
            string exe = FindExecutable();
            if (string.IsNullOrEmpty(exe)) throw new FileNotFoundException("cloudflared.exe tidak ditemukan");

            var ready = new ManualResetEventSlim(false);
            string foundUrl = null;
            string lastErr = "";

            var psi = new ProcessStartInfo(exe, "tunnel --url http://127.0.0.1:" + localPort + " --no-autoupdate")
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8,
                WorkingDirectory = Path.GetTempPath()
            };

            proc = new Process { StartInfo = psi, EnableRaisingEvents = true };
            Action<string> scan = line =>
            {
                if (string.IsNullOrEmpty(line)) return;
                lastErr = line;
                string u = ParseTryCloudflareUrl(line);
                if (u != null && foundUrl == null)
                {
                    foundUrl = u;
                    ready.Set();
                }
            };
            proc.ErrorDataReceived += (s, e) => scan(e.Data);
            proc.OutputDataReceived += (s, e) => scan(e.Data);
            proc.Exited += (s, e) => ready.Set();

            proc.Start();
            AttachKillOnCloseJob(proc);
            proc.BeginErrorReadLine();
            proc.BeginOutputReadLine();

            bool signaled = ready.Wait(timeoutMs);
            ready.Dispose();

            if (!signaled || string.IsNullOrEmpty(foundUrl))
            {
                Stop();
                throw new InvalidOperationException(string.IsNullOrEmpty(lastErr) ? "Gagal membuka Cloudflare Tunnel" : lastErr);
            }
            PublicUrl = foundUrl;
            return foundUrl;
        }

        public static string DownloadOfficial()
        {
            ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
            string dest = ToolsPath;
            string dir = Path.GetDirectoryName(dest);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            string tmp = dest + ".tmp";

            var req = (HttpWebRequest)WebRequest.Create(DownloadUrl);
            req.UserAgent = "Deskmate";
            req.Timeout = 30000;
            req.ReadWriteTimeout = 60000;
            using (var resp = (HttpWebResponse)req.GetResponse())
            using (var s = resp.GetResponseStream())
            using (var fs = File.Create(tmp))
            {
                var buf = new byte[65536];
                int n;
                while ((n = s.Read(buf, 0, buf.Length)) > 0) fs.Write(buf, 0, n);
            }
            // We are about to run this file: it must carry a valid Authenticode signature from Cloudflare
            if (!IsSignedByCloudflare(tmp))
            {
                try { File.Delete(tmp); } catch (Exception) { }
                throw new InvalidDataException(Lang.T("tanda tangan cloudflared.exe tidak valid"));
            }
            if (File.Exists(dest)) File.Delete(dest);
            File.Move(tmp, dest);
            return dest;
        }

        public void Stop()
        {
            PublicUrl = null;
            if (proc != null)
            {
                try
                {
                    if (!proc.HasExited)
                    {
                        try { proc.Kill(); } catch (Exception) { }
                        try { proc.WaitForExit(1500); } catch (Exception) { }
                    }
                }
                catch (Exception) { }
                try { proc.Dispose(); } catch (Exception) { }
                proc = null;
            }
            if (job != IntPtr.Zero)
            {
                CloseHandle(job);
                job = IntPtr.Zero;
            }
        }

        public void Dispose() { Stop(); }

        void AttachKillOnCloseJob(Process p)
        {
            try
            {
                job = CreateJobObject(IntPtr.Zero, null);
                if (job == IntPtr.Zero) return;
                var info = new JOBOBJECT_EXTENDED_LIMIT_INFORMATION();
                info.BasicLimitInformation.LimitFlags = JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE;
                int sz = Marshal.SizeOf(typeof(JOBOBJECT_EXTENDED_LIMIT_INFORMATION));
                IntPtr ptr = Marshal.AllocHGlobal(sz);
                try
                {
                    Marshal.StructureToPtr(info, ptr, false);
                    SetInformationJobObject(job, 9, ptr, (uint)sz);
                    AssignProcessToJobObject(job, p.Handle);
                }
                finally { Marshal.FreeHGlobal(ptr); }
            }
            catch (Exception ex) { Log.Write("cloudflared job: " + ex.Message); }
        }

        internal static bool IsSignedByCloudflare(string path)
        {
            if (!VerifyAuthenticode(path)) return false;
            try
            {
                using (var cert = new X509Certificate2(X509Certificate.CreateFromSignedFile(path)))
                    return string.Equals(cert.GetNameInfo(X509NameType.SimpleName, false),
                        "Cloudflare, Inc.", StringComparison.Ordinal);
            }
            catch (Exception) { return false; }
        }

        static bool VerifyAuthenticode(string path)
        {
            var file = new WINTRUST_FILE_INFO
            {
                cbStruct = (uint)Marshal.SizeOf(typeof(WINTRUST_FILE_INFO)),
                pcwszFilePath = path
            };
            IntPtr pFile = Marshal.AllocHGlobal(Marshal.SizeOf(typeof(WINTRUST_FILE_INFO)));
            try
            {
                Marshal.StructureToPtr(file, pFile, false);
                var data = new WINTRUST_DATA
                {
                    cbStruct = (uint)Marshal.SizeOf(typeof(WINTRUST_DATA)),
                    dwUIChoice = 2,          // WTD_UI_NONE
                    fdwRevocationChecks = 0, // WTD_REVOKE_NONE
                    dwUnionChoice = 1,       // WTD_CHOICE_FILE
                    pFile = pFile,
                    dwStateAction = 0
                };
                var action = new Guid("00AAC56B-CD44-11d0-8CC2-00C04FC295EE"); // WINTRUST_ACTION_GENERIC_VERIFY_V2
                return WinVerifyTrust(new IntPtr(-1), ref action, ref data) == 0;
            }
            catch (Exception) { return false; }
            finally
            {
                Marshal.DestroyStructure(pFile, typeof(WINTRUST_FILE_INFO));
                Marshal.FreeHGlobal(pFile);
            }
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        struct WINTRUST_FILE_INFO
        {
            public uint cbStruct;
            [MarshalAs(UnmanagedType.LPWStr)] public string pcwszFilePath;
            public IntPtr hFile, pgKnownSubject;
        }

        [StructLayout(LayoutKind.Sequential)]
        struct WINTRUST_DATA
        {
            public uint cbStruct;
            public IntPtr pPolicyCallbackData, pSIPClientData;
            public uint dwUIChoice, fdwRevocationChecks, dwUnionChoice;
            public IntPtr pFile;
            public uint dwStateAction;
            public IntPtr hWVTStateData, pwszURLReference;
            public uint dwProvFlags, dwUIContext;
            public IntPtr pSignatureSettings;
        }

        [DllImport("wintrust.dll", ExactSpelling = true)]
        static extern int WinVerifyTrust(IntPtr hwnd, ref Guid pgActionID, ref WINTRUST_DATA pWVTData);

        const uint JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE = 0x2000;

        [StructLayout(LayoutKind.Sequential)]
        struct JOBOBJECT_BASIC_LIMIT_INFORMATION
        {
            public long PerProcessUserTimeLimit, PerJobUserTimeLimit;
            public uint LimitFlags;
            public UIntPtr MinimumWorkingSetSize, MaximumWorkingSetSize;
            public uint ActiveProcessLimit;
            public UIntPtr Affinity;
            public uint PriorityClass, SchedulingClass;
        }

        [StructLayout(LayoutKind.Sequential)]
        struct IO_COUNTERS
        {
            public ulong ReadOperationCount, WriteOperationCount, OtherOperationCount;
            public ulong ReadTransferCount, WriteTransferCount, OtherTransferCount;
        }

        [StructLayout(LayoutKind.Sequential)]
        struct JOBOBJECT_EXTENDED_LIMIT_INFORMATION
        {
            public JOBOBJECT_BASIC_LIMIT_INFORMATION BasicLimitInformation;
            public IO_COUNTERS IoInfo;
            public UIntPtr ProcessMemoryLimit, JobMemoryLimit, PeakProcessMemoryUsed, PeakJobMemoryUsed;
        }

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] static extern IntPtr CreateJobObject(IntPtr lpJobAttributes, string lpName);
        [DllImport("kernel32.dll")] static extern bool SetInformationJobObject(IntPtr hJob, int infoType, IntPtr lpJobObjectInfo, uint cbJobObjectInfoLength);
        [DllImport("kernel32.dll")] static extern bool AssignProcessToJobObject(IntPtr hJob, IntPtr hProcess);
        [DllImport("kernel32.dll")] static extern bool CloseHandle(IntPtr hObject);
    }
}
