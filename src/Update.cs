using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Security.Cryptography;
using System.Threading;
using System.Windows.Forms;

namespace Deskmate
{
    // One-click update: download, verify SHA-256, swap, restart
    partial class Island
    {
        const string ReleaseRoot = "https://github.com/movlution/deskmate/releases/";
        const string LatestApi = "https://api.github.com/repos/movlution/deskmate/releases/latest";
        const string ExeAsset = "Deskmate.exe";
        const long AssetMax = 20 * 1024 * 1024;
        string newAsset, newDigest;
        bool updating;

        internal sealed class UpdateRelease
        {
            public Version Version;
            public string Url, Asset, Digest;
            public DateTime Uploaded;
        }

        static bool TryReleaseVersion(string tag, out Version version)
        {
            string number = tag.StartsWith("v", StringComparison.Ordinal) ? tag.Substring(1) : tag;
            return Version.TryParse(number, out version) && number == version.ToString();
        }

        static bool IsSha256(string hash)
        {
            return hash.Length == 64 && hash.All(Uri.IsHexDigit);
        }

        internal static UpdateRelease FindUpdateRelease(Version installed, Func<string, bool, WebResult> get)
        {
            string tag = "";
            Version latest = null;
            object metadata = null;
            var api = get(LatestApi, false);
            if (api.Ok)
            {
                try { metadata = Json.Parse(api.Body); }
                catch (ArgumentException) { }
                catch (InvalidOperationException) { }
                tag = Json.Str(metadata, "tag_name");
                if (!TryReleaseVersion(tag, out latest) || Json.Str(metadata, "html_url") != ReleaseRoot + "tag/" + tag)
                {
                    Log.Write("update: invalid release metadata from GitHub API");
                    metadata = null;
                }
            }
            else Log.Write("update: GitHub API status " + api.Status);

            if (metadata == null)
            {
                // Read GitHub's redirect without loading the release page or following untrusted URLs.
                string prefix = ReleaseRoot + "tag/";
                var page = get(ReleaseRoot + "latest", false);
                if (page.Status >= 300 && page.Status < 400 && page.Location != null &&
                    page.Location.StartsWith(prefix, StringComparison.Ordinal))
                {
                    tag = page.Location.Substring(prefix.Length);
                    if (!TryReleaseVersion(tag, out latest)) latest = null;
                }
                else latest = null;
                if (latest == null)
                {
                    Log.Write("update: latest release redirect unavailable (status " + page.Status + ")");
                    return null;
                }
            }

            var release = new UpdateRelease { Version = latest, Url = ReleaseRoot + "tag/" + tag };
            string download = ReleaseRoot + "download/" + tag + "/";
            var assets = Json.Arr(metadata, "assets");
            if (assets != null)
            {
                foreach (var asset in assets)
                {
                    if (Json.Str(asset, "name") != ExeAsset || Json.Str(asset, "browser_download_url") != download + ExeAsset) continue;
                    string digest = Json.Str(asset, "digest");
                    if (!digest.StartsWith("sha256:", StringComparison.Ordinal) || !IsSha256(digest.Substring(7))) continue;
                    release.Asset = download + ExeAsset;
                    release.Digest = digest.Substring(7).ToLowerInvariant();
                    release.Uploaded = Json.Time(asset, "updated_at");
                    return release;
                }
            }
            // A redirect identifies newer versions. Same-version rebuilds need the API asset timestamp.
            if (latest <= installed) return release;
            var sums = get(download + "SHA256SUMS.txt", true);
            if (sums.Ok)
            {
                foreach (string line in sums.Body.Split('\n'))
                {
                    var columns = line.Trim().Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                    if (columns.Length != 2 || (columns[1] != ExeAsset && columns[1] != "*" + ExeAsset) || !IsSha256(columns[0])) continue;
                    release.Asset = download + ExeAsset;
                    release.Digest = columns[0].ToLowerInvariant();
                    return release;
                }
            }
            Log.Write("update: no verified Deskmate.exe checksum (manifest status " + sums.Status + ")");
            return release;
        }

        static string ownDigest;

        // SHA-256 of the running exe, once
        static string OwnDigest()
        {
            if (ownDigest != null) return ownDigest;
            using (var sha = SHA256.Create())
            using (var f = File.OpenRead(Application.ExecutablePath))
                ownDigest = BitConverter.ToString(sha.ComputeHash(f)).Replace("-", "").ToLowerInvariant();
            return ownDigest;
        }

        void InstallUpdate(Form owner)
        {
            if (updating) return;
            if (newAsset == null || newDigest == null) { if (newUrl != null) OpenUrl(newUrl); return; }
            string url = newAsset, digest = newDigest, version = newVersion, releaseUrl = newUrl;
            if (!KitAsk.Confirm(owner,
                Lang.F("Unduh dan pasang Deskmate {0} sekarang? Deskmate akan dibuka ulang setelah pemasangan.", version),
                "Pasang", "Nanti", Lang.T("Perbarui Deskmate"))) return;

            updating = true;
            if (UpdateChanged != null) UpdateChanged();
            ShowInfo(Ink.IcoRefresh, Lang.F("Mengunduh Deskmate {0}", version), "", true);
            ThreadPool.QueueUserWorkItem(delegate
            {
                string error = null;
                try { error = Swap(url, digest); }
                catch (Exception ex) { error = ex.Message; }
                Post(() =>
                {
                    updating = false;
                    if (UpdateChanged != null) UpdateChanged();
                    if (error == null)
                    {
                        Log.Write("update: installed " + version);
                        // Open dialogs would keep this copy alive
                        foreach (var f in new List<Form>(Application.OpenForms.Cast<Form>())) if (f != this) f.Close();
                        Close();
                        return;
                    }
                    if (error == Restart) { ShowInfo(Ink.IcoRefresh, Lang.F("Deskmate {0} terpasang", version), Lang.T("buka ulang Deskmate"), true); return; }
                    Log.Write("update failed: " + error);
                    ShowInfo(Ink.IcoRefresh, Lang.T("Pembaruan gagal"), Lang.T("halaman rilis dibuka"), true);
                    if (releaseUrl != null) OpenUrl(releaseUrl);
                });
            });
        }

        static string Swap(string url, string digest)
        {
            byte[] data;
            var req = (HttpWebRequest)WebRequest.Create(url);
            req.UserAgent = "Deskmate/" + AppVersion;
            req.Timeout = 60000;
            req.ReadWriteTimeout = 30000;
            ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
            using (var res = (HttpWebResponse)req.GetResponse())
            using (var stream = res.GetResponseStream())
            using (var mem = new MemoryStream())
            {
                if (res.ResponseUri.Scheme != Uri.UriSchemeHttps) return "bukan https";
                var buf = new byte[65536];
                int n;
                while ((n = stream.Read(buf, 0, buf.Length)) > 0)
                {
                    mem.Write(buf, 0, n);
                    if (mem.Length > AssetMax) return "berkas terlalu besar";
                }
                data = mem.ToArray();
            }
            string hash;
            using (var sha = SHA256.Create()) hash = BitConverter.ToString(sha.ComputeHash(data)).Replace("-", "").ToLowerInvariant();
            if (hash != digest) return "SHA-256 tidak cocok";

            string exe = Application.ExecutablePath, dir = Path.GetDirectoryName(exe);
            string fresh = Path.Combine(dir, "Deskmate.new.exe"), old = Path.Combine(dir, "Deskmate.old.exe");
            File.WriteAllBytes(fresh, data);
            // Once releases are signed, the next one must be signed by the same publisher
            string mine = Signature.Signer(exe);
            if (mine != null && Signature.Signer(fresh) != mine)
            {
                try { File.Delete(fresh); } catch (IOException) { }
                return "tanda tangan tidak cocok";
            }
            if (File.Exists(old)) File.Delete(old);
            // A running exe can be renamed, not overwritten
            MoveRetry(exe, old);
            try { MoveRetry(fresh, exe); }
            catch
            {
                // Restore the running copy, always
                try { MoveRetry(old, exe); }
                catch (Exception ex)
                {
                    Log.Write("update restore: " + ex.Message);
                    File.Copy(old, exe, true);
                }
                try { File.Delete(fresh); } catch (Exception) { }
                throw;
            }
            try { Process.Start(new ProcessStartInfo(exe, "--after " + Process.GetCurrentProcess().Id) { UseShellExecute = false, WorkingDirectory = dir }); }
            catch (Exception ex) { Log.Write("update restart: " + ex.Message); return Restart; }
            return null;
        }

        const string Restart = "restart";

        // Antivirus may hold a fresh exe for a moment
        static void MoveRetry(string from, string to)
        {
            for (int i = 0; ; i++)
            {
                try { File.Move(from, to); return; }
                catch (IOException) { if (i >= 9) throw; }
                catch (UnauthorizedAccessException) { if (i >= 9) throw; }
                Thread.Sleep(300);
            }
        }

        // Leftovers from the previous version; updates overwrite, so no backups are kept
        static void CleanUpdate()
        {
            string dir = Path.GetDirectoryName(Application.ExecutablePath);
            var leftovers = new List<string> { Path.Combine(dir, "Deskmate.old.exe"), Path.Combine(dir, "Deskmate.new.exe") };
            try { leftovers.AddRange(Directory.GetFiles(dir, "Deskmate.previous-*.exe")); }
            catch (Exception ex) { Log.Write("update cleanup: " + ex.Message); }
            foreach (var leftover in leftovers)
                try { if (File.Exists(leftover)) File.Delete(leftover); }
                catch (Exception ex) { Log.Write("update cleanup: " + ex.Message); }
        }
    }
}
