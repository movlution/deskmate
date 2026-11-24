using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Deskmate
{
    // Immutable content-addressed images. Markdown carries no local paths or credentials.
    static class TaskNoteImages
    {
        internal const string Prefix = "deskmate-image:";
        internal const int MaxBytes = 4 * 1024 * 1024;
        internal static readonly Regex References = new Regex(@"!\[[^\]\r\n]*\]\(deskmate-image:([a-f0-9]{64})\)");
        static string CachePath(string hash) { return Path.Combine(Settings.Dir, "task-images", hash + ".png"); }
        static string Hash(byte[] data)
        {
            using (var sha = SHA256.Create())
                return BitConverter.ToString(sha.ComputeHash(data)).Replace("-", "").ToLowerInvariant();
        }

        internal static BitmapSource Decode(byte[] data)
        {
            if (data.Length == 0 || data.Length > MaxBytes) throw new InvalidDataException("Image must be at most 4 MB.");
            using (var stream = new MemoryStream(data))
            {
                var decoder = BitmapDecoder.Create(stream, BitmapCreateOptions.DelayCreation, BitmapCacheOption.None);
                var frame = decoder.Frames[0];
                if (frame.PixelWidth <= 0 || frame.PixelHeight <= 0 || (long)frame.PixelWidth * frame.PixelHeight > 16000000)
                    throw new InvalidDataException("Image must be at most 16 megapixels.");
            }
            using (var stream = new MemoryStream(data))
            {
                var frame = BitmapFrame.Create(stream, BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
                frame.Freeze();
                return frame;
            }
        }

        internal static string Import(BitmapSource source)
        {
            if (source == null || (long)source.PixelWidth * source.PixelHeight > 16000000)
                throw new InvalidDataException("Image must be at most 16 megapixels.");
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(source));
            using (var stream = new MemoryStream())
            {
                encoder.Save(stream);
                byte[] data = stream.ToArray();
                if (data.Length > MaxBytes) throw new InvalidDataException("Image must be at most 4 MB.");
                string hash = Hash(data);
                Directory.CreateDirectory(Path.GetDirectoryName(CachePath(hash)));
                AtomicFile.Write(CachePath(hash), data);
                return "![](" + Prefix + hash + ")";
            }
        }

        internal static InlineUIContainer Inline(string token, TextPointer position = null)
        {
            var match = References.Match(token);
            if (!match.Success || match.Value != token) throw new FormatException();
            FrameworkElement visual;
            try
            {
                string path = CachePath(match.Groups[1].Value);
                Decode(File.ReadAllBytes(path)); // Validate encoded and decoded bounds before rendering.
                // WPF undo serializes Image.Source. A public BitmapImage with a stable URI is serializable;
                // BitmapDecoder.Frames[0] is the internal BitmapFrameDecode type and breaks Undo.
                var source = new BitmapImage();
                source.BeginInit();
                source.CacheOption = BitmapCacheOption.OnLoad;
                source.UriSource = new Uri(Path.GetFullPath(path));
                source.EndInit();
                source.Freeze();
                var image = new Image { Source = source, Width = Math.Min(560, source.PixelWidth),
                    MaxHeight = 420, Stretch = Stretch.Uniform, ToolTip = Lang.T("Gambar") };
                System.Windows.Automation.AutomationProperties.SetName(image, Lang.T("Gambar"));
                visual = image;
            }
            catch (Exception)
            {
                visual = new TextBlock { Text = Lang.T("Gambar belum tersedia — sinkronkan Google Drive"),
                    TextWrapping = TextWrapping.Wrap, MaxWidth = 400 };
            }
            var container = position == null ? new InlineUIContainer(visual) : new InlineUIContainer(visual, position);
            container.Tag = token;
            container.BaselineAlignment = BaselineAlignment.Center;
            return container;
        }

        // Uses the existing OAuth refresh/error path; a separate binary seam keeps offline tests deterministic.
        internal static Func<string, string, byte[], FetchResult> Transport { get; set; }
        static FetchResult Media(ref GoogleCalVault vault, string method, string url, byte[] body, string type, out string error)
        {
            error = null;
            for (int attempt = 0; attempt < 2; attempt++)
            {
                var headers = new Dictionary<string, string> { { "Authorization", "Bearer " + vault.AccessToken } };
                if (type != null) headers["Content-Type"] = type;
                var result = Transport != null ? Transport(method, url, body)
                    : Fetch.SendBytes(method, url, headers, body, MaxBytes, 20000);
                if (result.Status == 401 && attempt == 0)
                {
                    var refresh = GoogleCalendarSource.RefreshAccessTokenStatic(vault);
                    if (refresh != null) { error = refresh.Error; return result; }
                    continue;
                }
                if (!result.Ok) error = result.Status == 403
                    ? Lang.T("Izin Google Tasks/Drive belum aktif — putuskan lalu sambungkan ulang di Pengaturan › Tugas & Kalender.")
                    : Lang.T("Gagal menyinkronkan gambar Google Drive");
                return result;
            }
            throw new InvalidOperationException();
        }

        const string Files = "https://www.googleapis.com/drive/v3/files";
        static string Find(ref GoogleCalVault vault, string key, out string error)
        {
            string query = "trashed=false and appProperties has { key='deskmateTaskImage' and value='" + key + "' }";
            var result = GoogleWorkspaceHttp.SendWithRefresh(ref vault, "GET",
                Files + "?q=" + Uri.EscapeDataString(query) + "&fields=files(id)&pageSize=1", null, null, out error);
            if (error != null) return null;
            if (!result.Ok) { error = Lang.T("Gagal menyinkronkan gambar Google Drive"); return null; }
            var files = Json.Arr(Json.Parse(result.Text), "files");
            return files.Length == 0 ? "" : Json.Str(files[0], "id");
        }

        internal static bool Sync(ref GoogleCalVault vault, string markdown, bool upload, out string error)
        {
            error = null;
            try
            {
                var seen = new HashSet<string>();
                foreach (Match match in References.Matches(markdown ?? ""))
                {
                    string hash = match.Groups[1].Value;
                    if (!seen.Add(hash)) continue;
                    string path = CachePath(hash);
                    byte[] data = File.Exists(path) ? File.ReadAllBytes(path) : null;
                    if (data != null && (data.Length > MaxBytes || Hash(data) != hash)) data = null;
                    if (!upload && data != null) continue;
                    string id = Find(ref vault, hash, out error);
                    if (id == null) return false;
                    if (id.Length == 0)
                    {
                        if (!upload || data == null) throw new IOException("Missing image");
                        // Folder is app-owned and private by default; no sharing permission is created.
                        string folder = Find(ref vault, "folder-v1", out error);
                        if (folder == null) return false;
                        if (folder.Length == 0)
                        {
                            var metadata = new Dictionary<string, object> {
                                { "name", "Deskmate Task Images" }, { "mimeType", "application/vnd.google-apps.folder" },
                                { "appProperties", new Dictionary<string, object> { { "deskmateTaskImage", "folder-v1" } } } };
                            var created = GoogleWorkspaceHttp.SendWithRefresh(ref vault, "POST", Files + "?fields=id",
                                Json.Encode(metadata), "application/json", out error);
                            if (error != null) return false;
                            if (!created.Ok) throw new IOException("Folder upload failed");
                            folder = Json.Str(Json.Parse(created.Text), "id");
                            if (string.IsNullOrEmpty(folder)) throw new IOException("Missing folder id");
                        }
                        string boundary = "deskmate_" + Guid.NewGuid().ToString("N");
                        var meta = new Dictionary<string, object> {
                            { "name", hash + ".png" }, { "parents", new string[] { folder } }, { "mimeType", "image/png" },
                            { "appProperties", new Dictionary<string, object> { { "deskmateTaskImage", hash } } } };
                        byte[] head = Encoding.UTF8.GetBytes("--" + boundary + "\r\nContent-Type: application/json; charset=UTF-8\r\n\r\n"
                            + Json.Encode(meta) + "\r\n--" + boundary + "\r\nContent-Type: image/png\r\n\r\n");
                        byte[] tail = Encoding.UTF8.GetBytes("\r\n--" + boundary + "--");
                        using (var stream = new MemoryStream())
                        {
                            stream.Write(head, 0, head.Length); stream.Write(data, 0, data.Length); stream.Write(tail, 0, tail.Length);
                            var created = Media(ref vault, "POST", "https://www.googleapis.com/upload/drive/v3/files?uploadType=multipart&fields=id",
                                stream.ToArray(), "multipart/related; boundary=" + boundary, out error);
                            if (error != null) return false;
                            if (string.IsNullOrEmpty(Json.Str(Json.Parse(created.Text), "id"))) throw new IOException("Missing image id");
                        }
                    }
                    else if (data == null)
                    {
                        var downloaded = Media(ref vault, "GET", Files + "/" + Uri.EscapeDataString(id) + "?alt=media", null, null, out error);
                        if (error != null) return false;
                        data = new byte[downloaded.Length];
                        Buffer.BlockCopy(downloaded.Data, 0, data, 0, data.Length);
                        if (Hash(data) != hash) throw new InvalidDataException("Image integrity check failed");
                        Decode(data);
                        Directory.CreateDirectory(Path.GetDirectoryName(path));
                        AtomicFile.Write(path, data);
                    }
                }
                return true;
            }
            catch (Exception)
            {
                error = Lang.T("Gagal menyinkronkan gambar Google Drive");
                return false;
            }
        }
    }
}
