using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Text;
using System.Threading;
using System.Windows.Forms;

namespace Deskmate
{
    public static class PetLinkCheck
    {
        static void Assert(bool ok, string msg)
        {
            if (!ok) throw new Exception("Assertion failed: " + msg);
        }

        public static string Run()
        {
            TestInviteParsing();
            TestCloudflaredUrlParser();
            TestSanitizeChat();
            TestHostAndClientLoopback();
            TestCustomPetLazyPull();
            TestHostCopyButtonLayout();
            TestRelayFilter();
            TestConnectionCap();
            TestAssetHashAndSignature();
            TestSlowGuestDoesNotBlockHost();
            TestClientSendsInOrder();
            TestPrankRouting();
            TestPrankCooldown();
            TestNameTag();
            TestRoomChatLog();
            TestChatWordDelete();
            return "PetLinkCheck OK (chat word delete, prank routing, prank cooldown, name tag, room chat log, invite, cloudflared parser, RFC6455 host/client, PIN rejection, state/chat, host disconnect, lazy custom pet asset, host copy-button layout, relay filter, connection cap, cache hash, cloudflared signature, stalled guest, ordered sends)";
        }

        // The room chat history: follows the newest message, stays put once scrolled up, and draws without a window
        static void TestRoomChatLog()
        {
            var lines = new List<Island.PetChatLine>();
            using (var log = new Island.PetChatLog(lines, 1f, Color.Black, Color.White, true) { Size = new Size(300, 120) })
            using (var bmp = new Bitmap(300, 120))
            {
                log.Added();
                Assert(log.ContentHeight == 0, "empty history has no height");
                log.DrawToBitmap(bmp, new Rectangle(0, 0, 300, 120));
                lines.Add(new Island.PetChatLine { Sender = "", Text = "Budi joined", Notice = true, At = DateTime.Now });
                for (int i = 0; i < 12; i++)
                    lines.Add(new Island.PetChatLine { Sender = i % 2 == 0 ? "Faris" : "Budi", Text = "pesan nomor " + i + " yang cukup panjang untuk dibungkus ke baris berikutnya", Mine = i % 2 == 0, At = DateTime.Now });
                log.Added();
                Assert(log.ContentHeight > 120, "many messages overflow the view");
                Assert(log.ScrollOffset == log.ContentHeight - 120, "view follows the newest message");
                log.ScrollBy(-50);
                int held = log.ScrollOffset;
                lines.Add(new Island.PetChatLine { Sender = "Budi", Text = "baru", At = DateTime.Now });
                log.Added();
                Assert(log.ScrollOffset == held, "scrolled-up view is not yanked down by a new message");
                log.ScrollBy(100000);
                lines.Add(new Island.PetChatLine { Sender = "Budi", Text = "lagi", At = DateTime.Now });
                log.Added();
                Assert(log.ScrollOffset == log.ContentHeight - 120, "back at the bottom, it follows again");
                log.DrawToBitmap(bmp, new Rectangle(0, 0, 300, 120));

                // A friend's line while reading older ones: held in place, the new-message pill shows
                log.ScrollBy(-80);
                lines.Add(new Island.PetChatLine { Sender = "Budi", Text = "halo", At = DateTime.Now });
                log.Added();
                Assert(log.Unseen, "unread friend message raises the pill");
                log.DrawToBitmap(bmp, new Rectangle(0, 0, 300, 120));
                // Your own line always brings the view back down
                lines.Add(new Island.PetChatLine { Sender = "Faris", Text = "balas", Mine = true, At = DateTime.Now });
                log.Added();
                Assert(!log.Unseen && log.ScrollOffset == log.ContentHeight - 120, "sending snaps to the newest message");

                // An unbroken link or laugh still wraps inside the bubble
                lines.Add(new Island.PetChatLine { Sender = "Budi", Text = "wkwkwkwkwkwkwkwkwkwkwkwkwkwkwkwkwkwkwkwkwkwkwkwkwkwkwk https://example.com/" + new string('a', 80), At = DateTime.Now });
                log.Added();
                Assert(log.WidestText <= log.TextLimit, "long words wrap inside the bubble: " + log.WidestText + " > " + log.TextLimit);

                lines.Clear();
                log.Added();
                Assert(log.ContentHeight == 0 && !log.Unseen, "cleared history is empty and has no pill");
            }
        }

        sealed class WordBox : KitText
        {
            public void Press(Keys k) { OnKeyDown(new KeyEventArgs(k)); }
        }

        // The chat composer is a KitText: Ctrl+Backspace and Ctrl+Delete remove one word at the caret
        static void TestChatWordDelete()
        {
            using (var box = new WordBox())
            {
                box.Text = "akuu sarapan dlu mas";
                box.Select(box.TextLength, 0);
                box.Press(Keys.Control | Keys.Back);
                Assert(box.Text == "akuu sarapan dlu ", "Ctrl+Backspace removes the last word: " + box.Text);
                box.Press(Keys.Control | Keys.Back);
                Assert(box.Text == "akuu sarapan ", "Ctrl+Backspace skips the space, then removes a word: " + box.Text);
                box.Text = "halo, teman";
                box.Select(box.TextLength, 0);
                box.Press(Keys.Control | Keys.Back);
                box.Press(Keys.Control | Keys.Back);
                Assert(box.Text == "halo", "punctuation goes as its own word: " + box.Text);
                box.Text = "satu dua tiga";
                box.Select(0, 0);
                box.Press(Keys.Control | Keys.Delete);
                Assert(box.Text == " dua tiga", "Ctrl+Delete removes the next word: " + box.Text);
                box.Text = "satu dua tiga";
                box.Select(5, 3);
                box.Press(Keys.Control | Keys.Back);
                Assert(box.Text == "satu  tiga", "with a selection, only the selection goes: " + box.Text);
            }
        }

        // The room name tag: drawn off-screen only, sized by its text and scale, hidden on request
        static void TestNameTag()
        {
            var owner = new Form { ShowInTaskbar = false, StartPosition = FormStartPosition.Manual, Bounds = new Rectangle(-32000, -32000, 1, 1) };
            var tag = new Island.PetNameTag();
            try
            {
                tag.Put("Faris", 1f, -30000, -30000, owner);
                Size small = tag.TagSize;
                Assert(small.Width > 0 && small.Height > 0, "name tag has a size");
                Assert(tag.Showing, "name tag shows");
                tag.Put("Faris dan kucingnya", 1f, -30000, -30000, owner);
                Assert(tag.TagSize.Width > small.Width, "longer name, wider tag");
                Assert(tag.TagSize.Height == small.Height, "one line tall either way");
                tag.Put("Faris", 2f, -30000, -30000, owner);
                Assert(tag.TagSize.Height > small.Height, "tag follows the monitor scale");
                tag.Conceal();
                Assert(!tag.Showing, "name tag hides");
            }
            finally
            {
                tag.Close();
                tag.Dispose();
                owner.Dispose();
            }
        }

        // A guest may only send peer traffic; room, join, leave and err must never reach other guests
        static void TestRelayFilter()
        {
            using (var host = new PetHost())
            {
                host.Start(new PetPeerProfile { Name = "Host" }, false, 0);
                var wsUri = new Uri("ws://127.0.0.1:" + host.Port + "/ws");
                var seenAtB = new List<string>();
                var bReady = new ManualResetEventSlim(false);
                var aJoinedAtB = new ManualResetEventSlim(false);
                var markerAtB = new ManualResetEventSlim(false);
                using (var a = new PetLinkClient())
                using (var b = new PetLinkClient())
                {
                    b.MessageReceived += msg =>
                    {
                        string t = PetPacket.Str(msg, "t");
                        if (t == "room") { bReady.Set(); return; }
                        if (t == "join" && !aJoinedAtB.IsSet) { aJoinedAtB.Set(); return; }
                        if (t == "chat" && PetPacket.Str(msg, "text") == "marker") { markerAtB.Set(); return; }
                        lock (seenAtB) seenAtB.Add(t);
                    };
                    var aReady = new ManualResetEventSlim(false);
                    a.MessageReceived += msg => { if (PetPacket.Str(msg, "t") == "room") aReady.Set(); };

                    b.Connect(wsUri, host.Pin, new PetPeerProfile { Name = "B" });
                    Assert(bReady.Wait(5000), "B joined");
                    a.Connect(wsUri, host.Pin, new PetPeerProfile { Name = "A" });
                    Assert(aReady.Wait(5000), "A joined");
                    Assert(aJoinedAtB.Wait(5000), "B saw the real join of A");

                    a.Send("{\"t\":\"join\",\"peer\":{\"id\":\"fake01\",\"name\":\"Fake\"}}");
                    a.Send("{\"t\":\"room\",\"hostId\":\"x\",\"yourId\":\"hijack\",\"peers\":[]}");
                    a.Send("{\"t\":\"leave\",\"id\":\"anything\"}");
                    a.Send("{\"t\":\"err\",\"code\":\"x\",\"msg\":\"spoof\"}");
                    Thread.Sleep(50);
                    a.Send(PetPacket.EncodeChat("", "marker", -1));
                    Assert(markerAtB.Wait(5000), "legit chat still relayed after forbidden types");
                    lock (seenAtB) Assert(seenAtB.Count == 0, "forbidden types relayed: " + string.Join(",", seenAtB.ToArray()));
                }
            }
        }

        // One prank per friend per cooldown; friends are counted apart
        static void TestPrankCooldown()
        {
            var gate = new PetPrankGate();
            var t0 = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);
            int cd = (int)PetPrankGate.Cooldown.TotalSeconds;
            Assert(gate.SecondsLeft("a", t0) == 0, "fresh friend is ready");
            Assert(gate.TryUse("a", t0), "first prank goes through");
            Assert(!gate.TryUse("a", t0.AddSeconds(1)), "second prank right after is blocked");
            Assert(gate.SecondsLeft("a", t0.AddSeconds(1)) == cd - 1, "countdown rounds up");
            Assert(gate.SecondsLeft("a", t0.AddSeconds(cd - 0.2)) == 1, "last fraction still shows 1 s");
            Assert(gate.TryUse("b", t0.AddSeconds(1)), "another friend has its own cooldown");
            Assert(!gate.TryUse("a", t0.AddSeconds(cd - 0.01)), "still blocked just before the end");
            Assert(gate.TryUse("a", t0.AddSeconds(cd)), "allowed again once the cooldown is over");
            Assert(!gate.TryUse("", t0) && !gate.TryUse(null, t0), "no key, no prank");
        }

        // A prank goes only to the friend it names (or the host), never to the rest of the room
        static void TestPrankRouting()
        {
            using (var host = new PetHost())
            {
                host.Start(new PetPeerProfile { Name = "Host" }, false, 0);
                var wsUri = new Uri("ws://127.0.0.1:" + host.Port + "/ws");
                string bId = null;
                var bReady = new ManualResetEventSlim(false);
                var cReady = new ManualResetEventSlim(false);
                var aReady = new ManualResetEventSlim(false);
                var prankAtB = new ManualResetEventSlim(false);
                var markerAtC = new ManualResetEventSlim(false);
                var prankAtHost = new ManualResetEventSlim(false);
                int prankAtC = 0, prankKindAtB = -1;
                string prankFromAtB = null;
                host.MessageFromGuest += (id, msg) => { if (PetPacket.Str(msg, "t") == "prank" && PetPacket.Int(msg, "kind") == 3) prankAtHost.Set(); };
                using (var a = new PetLinkClient())
                using (var b = new PetLinkClient())
                using (var c = new PetLinkClient())
                {
                    string aId = null;
                    a.MessageReceived += msg => { if (PetPacket.Str(msg, "t") == "room") { aId = PetPacket.Str(msg, "yourId"); aReady.Set(); } };
                    b.MessageReceived += msg =>
                    {
                        string t = PetPacket.Str(msg, "t");
                        if (t == "room") { bId = PetPacket.Str(msg, "yourId"); bReady.Set(); }
                        else if (t == "prank") { prankKindAtB = PetPacket.Int(msg, "kind", -1); prankFromAtB = PetPacket.Str(msg, "id"); prankAtB.Set(); }
                    };
                    c.MessageReceived += msg =>
                    {
                        string t = PetPacket.Str(msg, "t");
                        if (t == "room") cReady.Set();
                        else if (t == "prank") Interlocked.Increment(ref prankAtC);
                        else if (t == "chat" && PetPacket.Str(msg, "text") == "after") markerAtC.Set();
                    };
                    b.Connect(wsUri, host.Pin, new PetPeerProfile { Name = "B" });
                    Assert(bReady.Wait(5000), "B joined");
                    c.Connect(wsUri, host.Pin, new PetPeerProfile { Name = "C" });
                    Assert(cReady.Wait(5000), "C joined");
                    a.Connect(wsUri, host.Pin, new PetPeerProfile { Name = "A" });
                    Assert(aReady.Wait(5000), "A joined");

                    a.Send(PetPacket.EncodePrank("spoofed", bId, (int)PetPrank.Fire));
                    a.Send(PetPacket.EncodePrank("", host.HostId, (int)PetPrank.Snow));
                    a.Send(PetPacket.EncodeChat("", "after", -1));
                    Assert(prankAtB.Wait(5000), "B got the prank aimed at it");
                    Assert(prankKindAtB == (int)PetPrank.Fire, "prank kind kept");
                    Assert(prankFromAtB == aId, "host stamps the real sender id");
                    Assert(prankAtHost.Wait(5000), "host got the prank aimed at it");
                    Assert(markerAtC.Wait(5000), "C still gets room chat");
                    Assert(prankAtC == 0, "prank leaked to a bystander");
                }
            }
        }

        // Stalled handshakes beyond the cap are dropped at once instead of each holding a thread
        static void TestConnectionCap()
        {
            using (var host = new PetHost())
            {
                host.Start(new PetPeerProfile { Name = "Host" }, false, 0);
                var held = new List<System.Net.Sockets.TcpClient>();
                try
                {
                    for (int i = 0; i < PetHost.MaxOpenConns; i++)
                        held.Add(new System.Net.Sockets.TcpClient("127.0.0.1", host.Port));
                    Thread.Sleep(300);
                    using (var extra = new System.Net.Sockets.TcpClient("127.0.0.1", host.Port))
                    {
                        var s = extra.GetStream();
                        s.ReadTimeout = 3000;
                        int n;
                        try { n = s.Read(new byte[1], 0, 1); }
                        catch (IOException) { n = 0; }
                        Assert(n == 0, "connection past the cap must be closed by the host");
                    }
                }
                finally
                {
                    foreach (var c in held) try { c.Close(); } catch (Exception) { }
                }
            }
        }

        // BroadcastFromHost runs on the UI thread: a guest that joined and then stopped reading must
        // neither freeze it nor make the host queue without bound
        static void TestSlowGuestDoesNotBlockHost()
        {
            using (var host = new PetHost())
            {
                host.Start(new PetPeerProfile { Name = "Host" }, false, 0);
                var left = new ManualResetEventSlim(false);
                var joined = new ManualResetEventSlim(false);
                host.PeerJoined += p => joined.Set();
                host.PeerLeft += id => left.Set();
                using (var tcp = new System.Net.Sockets.TcpClient("127.0.0.1", host.Port))
                {
                    tcp.ReceiveBufferSize = 4096;
                    var s = tcp.GetStream();
                    byte[] req = Encoding.ASCII.GetBytes("GET /ws HTTP/1.1\r\nHost: x\r\nUpgrade: websocket\r\nConnection: Upgrade\r\n" +
                        "Sec-WebSocket-Key: dGhlIHNhbXBsZSBub25jZQ==\r\nSec-WebSocket-Version: 13\r\n\r\n");
                    s.Write(req, 0, req.Length);
                    var head = new StringBuilder();
                    while (!head.ToString().EndsWith("\r\n\r\n", StringComparison.Ordinal)) head.Append((char)s.ReadByte());
                    byte[] hello = MaskedText(PetPacket.EncodeHello(host.Pin, new PetPeerProfile { Name = "Sleepy" }));
                    s.Write(hello, 0, hello.Length);
                    Assert(joined.Wait(5000), "raw guest joined");

                    // From here on the guest never reads. 2000 x 16 KB is far more than socket buffers hold.
                    string big = PetPacket.EncodeChat("h", new string('x', 200), -1);
                    var chunk = new string('y', 16 * 1024);
                    var sw = System.Diagnostics.Stopwatch.StartNew();
                    for (int i = 0; i < 2000; i++)
                        host.BroadcastFromHost(i % 2 == 0 ? big : "{\"t\":\"state\",\"pad\":\"" + chunk + "\"}");
                    sw.Stop();
                    Assert(sw.ElapsedMilliseconds < 1000, "broadcast must not block on a stalled guest: " + sw.ElapsedMilliseconds + " ms");
                    Assert(left.Wait(15000), "a guest that stops reading is dropped");
                }
            }
        }

        // State packets from one client must arrive in the order they were sent
        static void TestClientSendsInOrder()
        {
            using (var host = new PetHost())
            {
                host.Start(new PetPeerProfile { Name = "Host" }, false, 0);
                var seen = new List<double>();
                var done = new ManualResetEventSlim(false);
                host.MessageFromGuest += (id, msg) =>
                {
                    if (PetPacket.Str(msg, "t") != "state") return;
                    lock (seen)
                    {
                        seen.Add(PetPacket.Num(msg, "x", -1));
                        if (seen.Count == 25) done.Set();
                    }
                };
                var ready = new ManualResetEventSlim(false);
                using (var c = new PetLinkClient())
                {
                    c.MessageReceived += m => { if (PetPacket.Str(m, "t") == "room") ready.Set(); };
                    c.Connect(new Uri("ws://127.0.0.1:" + host.Port + "/ws"), host.Pin, new PetPeerProfile { Name = "A" });
                    Assert(ready.Wait(5000), "client joined");
                    // 25 is under the host's 30-per-3-s state budget
                    for (int i = 0; i < 25; i++) c.Send(PetPacket.EncodeState("", i / 100.0, 1, 1, false));
                    Assert(done.Wait(5000), "all states arrived");
                    lock (seen)
                        for (int i = 1; i < seen.Count; i++)
                            Assert(seen[i] > seen[i - 1], "state " + i + " arrived out of order");
                }
            }
        }

        static byte[] MaskedText(string text)
        {
            byte[] p = Encoding.UTF8.GetBytes(text);
            var mask = new byte[] { 1, 2, 3, 4 };
            var ms = new MemoryStream();
            ms.WriteByte(0x81);
            if (p.Length < 126) ms.WriteByte((byte)(0x80 | p.Length));
            else { ms.WriteByte(0x80 | 126); ms.WriteByte((byte)(p.Length >> 8)); ms.WriteByte((byte)p.Length); }
            ms.Write(mask, 0, 4);
            for (int i = 0; i < p.Length; i++) ms.WriteByte((byte)(p[i] ^ mask[i & 3]));
            return ms.ToArray();
        }

        static void TestAssetHashAndSignature()
        {
            Assert(Island.IsAssetHash("0123456789abcdef"), "hex hash accepted");
            Assert(!Island.IsAssetHash("..\\..\\abcdefghi"), "path-like 16-char hash rejected");
            Assert(!Island.IsAssetHash("0123456789ABCDEF"), "uppercase hash rejected (ComputeHashBytes writes lowercase)");
            Assert(!Island.IsAssetHash("0123"), "short hash rejected");

            string fake = Path.Combine(Path.GetTempPath(), "Deskmate-fake-cloudflared-" + Guid.NewGuid().ToString("N") + ".exe");
            File.WriteAllBytes(fake, Encoding.ASCII.GetBytes("MZ not really a program"));
            try { Assert(!CloudflaredTunnel.IsSignedByCloudflare(fake), "unsigned file must not pass"); }
            finally { try { File.Delete(fake); } catch (Exception) { } }

            string explorer = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "explorer.exe");
            if (File.Exists(explorer))
                Assert(!CloudflaredTunnel.IsSignedByCloudflare(explorer), "a file signed by someone else must not pass");

            string real = CloudflaredTunnel.FindExecutable();
            if (real != null)
                Assert(CloudflaredTunnel.IsSignedByCloudflare(real), "official cloudflared.exe must pass: " + real);
        }

        static void TestInviteParsing()
        {
            string code = PetInvite.Format("https://swift-cat-123.trycloudflare.com", "k7p-2m9");
            Assert(code == "deskmate://swift-cat-123.trycloudflare.com#K7P2M9", "format cloudflare invite: " + code);

            PetInvite inv;
            string err;
            Assert(PetInvite.TryParse(code, out inv, out err), "parse cloudflare invite: " + err);
            Assert(inv.WsUri.AbsoluteUri == "wss://swift-cat-123.trycloudflare.com/ws", "ws uri: " + inv.WsUri);
            Assert(inv.Pin == "K7P2M9", "pin: " + inv.Pin);

            Assert(PetInvite.TryParse("ws://127.0.0.1:47825#ABCD12", out inv, out err), "parse lan invite: " + err);
            Assert(inv.WsUri.AbsoluteUri == "ws://127.0.0.1:47825/ws", "lan uri: " + inv.WsUri);

            Assert(!PetInvite.TryParse("deskmate://no-pin.trycloudflare.com", out inv, out err), "missing pin must fail");
            Assert(!PetInvite.TryParse("", out inv, out err), "empty invite must fail");
        }

        static void TestCloudflaredUrlParser()
        {
            string logLine = "2026-11-24T07:00:00Z INF |  https://warm-breeze-42.trycloudflare.com  |";
            string url = CloudflaredTunnel.ParseTryCloudflareUrl(logLine);
            Assert(url == "https://warm-breeze-42.trycloudflare.com", "parsed url: " + url);
            Assert(CloudflaredTunnel.ParseTryCloudflareUrl("no url here") == null, "non-matching line must return null");
        }

        static void TestSanitizeChat()
        {
            string raw = "  Halo\r\ndunia\t\x01! " + new string('a', 300);
            string clean = PetPacket.SanitizeChat(raw);
            Assert(clean.Length == PetPacket.MaxChatChars, "chat capped at 240: " + clean.Length);
            Assert(clean.StartsWith("Halo  dunia ! ", StringComparison.Ordinal), "control chars cleaned: " + clean.Substring(0, 20));
        }

        static void TestHostAndClientLoopback()
        {
            using (var host = new PetHost())
            {
                var hostProf = new PetPeerProfile { Name = "HostB", Kind = "cat", Color = "orange", Size = "m" };
                host.Start(hostProf, false, 0);
                Assert(host.Port > 0 && host.Pin.Length == 6, "host started on ephemeral port with 6-char PIN");

                var wsUri = new Uri("ws://127.0.0.1:" + host.Port + "/ws");

                // 1. Bad PIN must be rejected
                var badPinGotErr = new ManualResetEventSlim(false);
                string badCode = null;
                using (var badClient = new PetLinkClient())
                {
                    badClient.MessageReceived += msg =>
                    {
                        if (PetPacket.Str(msg, "t") == "err")
                        {
                            badCode = PetPacket.Str(msg, "code");
                            badPinGotErr.Set();
                        }
                    };
                    badClient.Connect(wsUri, "WRONG1", new PetPeerProfile { Name = "Intruder" });
                    Assert(badPinGotErr.Wait(5000), "bad pin should receive err packet");
                    Assert(badCode == "bad_pin", "expected bad_pin, got: " + badCode);
                }

                // 2. Valid PIN join, state sync, chat, and host shutdown disconnect
                var joinedHost = new ManualResetEventSlim(false);
                var gotRoom = new ManualResetEventSlim(false);
                var gotStateAtHost = new ManualResetEventSlim(false);
                var gotChatAtHost = new ManualResetEventSlim(false);
                var gotChatAtGuest = new ManualResetEventSlim(false);
                var guestDisconnected = new ManualResetEventSlim(false);

                string guestIdAtHost = null;
                double stateX = -1;
                string chatAtHost = null, chatAtGuest = null;

                host.PeerJoined += p => { guestIdAtHost = p.Id; joinedHost.Set(); };
                host.MessageFromGuest += (id, msg) =>
                {
                    string t = PetPacket.Str(msg, "t");
                    if (t == "state") { stateX = PetPacket.Num(msg, "x", -1); gotStateAtHost.Set(); }
                    if (t == "chat") { chatAtHost = PetPacket.Str(msg, "text"); gotChatAtHost.Set(); }
                };

                using (var guest = new PetLinkClient())
                {
                    guest.MessageReceived += msg =>
                    {
                        string t = PetPacket.Str(msg, "t");
                        if (t == "room") gotRoom.Set();
                        if (t == "chat") { chatAtGuest = PetPacket.Str(msg, "text"); gotChatAtGuest.Set(); }
                    };
                    guest.Disconnected += reason => guestDisconnected.Set();

                    guest.Connect(wsUri, host.Pin, new PetPeerProfile { Name = "GuestA", Kind = "dog", Color = "brown", Size = "m" });
                    Assert(joinedHost.Wait(5000), "host should see guest join");
                    Assert(gotRoom.Wait(5000), "guest should receive room snapshot");
                    Assert(!string.IsNullOrEmpty(guestIdAtHost), "guest id assigned");

                    // Guest -> Host state & chat
                    guest.Send(PetPacket.EncodeState(guestIdAtHost, 0.73, -1, 1, true));
                    guest.Send(PetPacket.EncodeChat(guestIdAtHost, "Halo dari A!", 0));
                    Assert(gotStateAtHost.Wait(5000) && Math.Abs(stateX - 0.73) < 0.001, "state x synced");
                    Assert(gotChatAtHost.Wait(5000) && chatAtHost == "Halo dari A!", "chat received at host");

                    // Host -> Guest chat
                    host.BroadcastFromHost(PetPacket.EncodeChat(host.HostId, "Halo juga dari B!", 1));
                    Assert(gotChatAtGuest.Wait(5000) && chatAtGuest == "Halo juga dari B!", "chat received at guest");

                    // Host stops -> Guest disconnects immediately
                    host.Stop();
                    Assert(guestDisconnected.Wait(5000), "guest must disconnect when host stops");
                }
            }
        }

        static void TestCustomPetLazyPull()
        {
            // Create a minimal valid 1-frame custom pet in a temp directory
            string dir = Path.Combine(Path.GetTempPath(), "Deskmate-custom-pet-test-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            try
            {
                string json = "{\"frames\":[{\"frame\":{\"x\":0,\"y\":0,\"w\":16,\"h\":16},\"duration\":100}],\"meta\":{\"frameTags\":[{\"name\":\"walk\",\"from\":0,\"to\":0,\"direction\":\"forward\"}]}}";
                byte[] jsonBytes = Encoding.UTF8.GetBytes(json);
                byte[] pngBytes;
                using (var bmp = new Bitmap(16, 16, PixelFormat.Format32bppArgb))
                {
                    bmp.SetPixel(8, 15, Color.Orange);
                    using (var ms = new MemoryStream())
                    {
                        bmp.Save(ms, ImageFormat.Png);
                        pngBytes = ms.ToArray();
                    }
                }
                File.WriteAllBytes(Path.Combine(dir, "pet.json"), jsonBytes);
                File.WriteAllBytes(Path.Combine(dir, "sheet.png"), pngBytes);

                long total;
                string hash = ComputeSha16(jsonBytes, pngBytes);
                Assert(hash.Length == 16, "16-char hash");

                // Host has the custom pet; Guest joins and lazy-pulls it via PetRoomSession
                using (var hostSess = new PetRoomSession())
                using (var guestSess = new PetRoomSession())
                {
                    var hostReady = new ManualResetEventSlim(false);
                    hostSess.StateChanged += () => { if (hostSess.Mode == PetRoomMode.Connected && !string.IsNullOrEmpty(hostSess.InviteCode)) hostReady.Set(); };

                    var hostProf = new PetPeerProfile
                    {
                        Name = "CustomHost",
                        Kind = "custom:mini",
                        Color = "orange",
                        Size = "m",
                        AssetHash = hash,
                        AssetBytes = jsonBytes.Length + pngBytes.Length
                    };
                    hostSess.StartHost(hostProf, dir, false);
                    Assert(hostReady.Wait(5000), "host session ready");

                    var assetGot = new ManualResetEventSlim(false);
                    byte[] recvJson = null, recvPng = null;
                    string recvHash = null;

                    guestSess.PeerJoined += p =>
                    {
                        if (p.Kind.StartsWith("custom:", StringComparison.Ordinal) && !string.IsNullOrEmpty(p.AssetHash))
                            guestSess.RequestCustomAsset(p.Id, p.AssetHash);
                    };
                    guestSess.CustomAssetReady += (id, h, j, png) =>
                    {
                        recvHash = h;
                        recvJson = j;
                        recvPng = png;
                        assetGot.Set();
                    };

                    guestSess.StartJoin(hostSess.InviteCode, new PetPeerProfile { Name = "GuestB" }, null);
                    Assert(assetGot.Wait(6000), "guest should receive lazy-pulled custom pet chunks");
                    Assert(recvHash == hash, "received hash matches");
                    Assert(ComputeSha16(recvJson, recvPng) == hash, "received bytes SHA-256 matches");
                    Assert(recvJson.Length == jsonBytes.Length && recvPng.Length == pngBytes.Length, "received byte lengths match");
                    total = recvJson.Length + recvPng.Length;
                    Assert(total > 0, "non-empty asset");
                }
            }
            finally
            {
                try { Directory.Delete(dir, true); } catch (Exception) { }
            }
        }

        static void TestHostCopyButtonLayout()
        {
            float[] scales = { 1.0f, 1.25f, 1.5f };
            string[][] labels =
            {
                new[] { "Start host", "Stop", "Copy code", "Install & Host" },
                new[] { "Mulai host", "Berhenti", "Salin kode", "Pasang & Host" }
            };

            foreach (float ui in scales)
            {
                foreach (var pair in labels)
                {
                    int[] widths = { (int)(380 * ui), (int)(640 * ui) };
                    foreach (int rowW in widths)
                    {
                        using (var group = new KitGroup { Width = rowW, Height = (int)(58 * ui) })
                        using (var hostWrap = new Panel { AutoSize = false, BackColor = Color.Transparent, Height = (int)(30 * ui), Width = (int)(220 * ui) })
                        using (var hostBtn = new KitButton { Text = pair[0], Primary = true, Font = Ink.Text(12.5f * ui) })
                        using (var copyBtn = new KitButton { Text = pair[2], Primary = false, Font = Ink.Text(12.5f * ui) })
                        using (var hostRow = new KitRow { Title = "Host room (Internet)", Detail = "Invite code copied · deskmate://drew-lat-images-pillow.trycloudflare.com#Z5SRPR", Glyph = (char)0xE774, Field = hostWrap, Width = rowW, Height = (int)(58 * ui) })
                        {
                            hostWrap.Controls.Add(hostBtn);
                            hostWrap.Controls.Add(copyBtn);
                            hostRow.Controls.Add(hostWrap);
                            group.Controls.Add(hostRow);

                            // 1. Idle state (Start host, copy hidden)
                            Island.LayoutPetHostControls(hostRow, hostWrap, hostBtn, copyBtn, ui, false);
                            int padRight = (int)(16 * ui);
                            Assert(!copyBtn.Visible, "copyBtn hidden in idle state");
                            Assert(hostWrap.Right == rowW - padRight, "idle hostWrap aligned to right edge: " + hostWrap.Right + " vs " + (rowW - padRight));
                            int idleLeft = hostWrap.Left;

                            // 2. Hosting with invite code (Stop + Copy code visible)
                            hostBtn.Text = pair[1];
                            hostBtn.Primary = false;
                            Island.LayoutPetHostControls(hostRow, hostWrap, hostBtn, copyBtn, ui, true);
                            Assert(copyBtn.Visible, "copyBtn visible when hosting with invite code");
                            Assert(hostWrap.Left < idleLeft, "hostWrap must shift left when copyBtn appears");
                            Assert(hostWrap.Left > (int)hostRow.TextX(ui) + (int)(80 * ui), "detail text still has room left of Stop");
                            Assert(hostWrap.Right == rowW - padRight, "hosting hostWrap right aligned: " + hostWrap.Right + " vs " + (rowW - padRight));
                            Assert(hostBtn.Left == 0 && copyBtn.Left > hostBtn.Right, "copyBtn sits to the right of hostBtn with gap");
                            Assert(hostWrap.Left + copyBtn.Right == rowW - padRight, "copyBtn fully inside hostRow right edge");
                            int copyTextW = TextRenderer.MeasureText(copyBtn.Text, copyBtn.Font, Size.Empty, TextFormatFlags.NoPrefix).Width;
                            Assert(copyBtn.Width > copyTextW, "copyBtn wider than its label: " + copyBtn.Width + " vs " + copyTextW);

                            // Render to offscreen bitmap and verify copyBtn actually draws visibly inside the group
                            if (rowW == (int)(640 * ui))
                            {
                                using (var bmp = new Bitmap(group.Width, group.Height))
                                {
                                    group.DrawToBitmap(bmp, new Rectangle(0, 0, bmp.Width, bmp.Height));
                                    var copyRect = new Rectangle(hostWrap.Left + copyBtn.Left, hostWrap.Top + copyBtn.Top, copyBtn.Width, copyBtn.Height);
                                    Assert(copyRect.Right <= bmp.Width && copyRect.Bottom <= bmp.Height, "copyRect inside rendered bitmap");
                                    int distinct = 0;
                                    Color first = bmp.GetPixel(copyRect.X + 2, copyRect.Y + 2);
                                    for (int y = copyRect.Y + 4; y < copyRect.Bottom - 4; y += 2)
                                        for (int x = copyRect.X + 4; x < copyRect.Right - 4; x += 2)
                                            if (bmp.GetPixel(x, y) != first) distinct++;
                                    Assert(distinct > 10, "copyBtn rendered visible label pixels inside group (distinct=" + distinct + ")");
                                }
                            }

                            // 3. Stop hosting -> shrinks back and re-aligns to right edge
                            hostBtn.Text = pair[3];
                            hostBtn.Primary = true;
                            Island.LayoutPetHostControls(hostRow, hostWrap, hostBtn, copyBtn, ui, false);
                            Assert(!copyBtn.Visible, "copyBtn hidden after stopping host");
                            Assert(hostWrap.Right == rowW - padRight, "stopped hostWrap right aligned");
                            int installTextW = TextRenderer.MeasureText(hostBtn.Text, hostBtn.Font, Size.Empty, TextFormatFlags.NoPrefix).Width;
                            Assert(hostBtn.Width > installTextW, "Install & Host button wider than label (with ampersand)");
                        }
                    }
                }
            }
        }

        static string ComputeSha16(byte[] a, byte[] b)
        {
            using (var sha = System.Security.Cryptography.SHA256.Create())
            {
                sha.TransformBlock(a, 0, a.Length, null, 0);
                sha.TransformFinalBlock(b, 0, b.Length);
                var h = sha.Hash;
                var sb = new StringBuilder(16);
                for (int i = 0; i < 8; i++) sb.Append(h[i].ToString("x2"));
                return sb.ToString();
            }
        }
    }
}
