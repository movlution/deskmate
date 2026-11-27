using System;

namespace Deskmate
{
    public static class UpdateReleaseCheck
    {
        const string Root = "https://github.com/movlution/deskmate/releases/";
        const string Api = "https://api.github.com/repos/movlution/deskmate/releases/latest";
        const string Tag = Root + "tag/v2.21.0";
        const string Download = Root + "download/v2.21.0/";
        static readonly string Digest = new string('a', 64);

        static void Assert(bool condition, string message)
        {
            if (!condition) throw new Exception(message);
        }

        public static string Run()
        {
            var old = new Version(2, 17, 1);
            var current = new Version(2, 21, 0);
            string metadata = "{\"tag_name\":\"v2.21.0\",\"html_url\":\"" + Tag + "\",\"assets\":[{\"name\":\"Deskmate.exe\"," +
                "\"browser_download_url\":\"" + Download + "Deskmate.exe\",\"digest\":\"sha256:" + Digest +
                "\",\"updated_at\":\"2026-11-27T01:56:59Z\"}]}";
            var api = Island.FindUpdateRelease(current, (url, follow) =>
            {
                Assert(url == Api && !follow, "Usable API metadata should need only one request");
                return new WebResult { Status = 200, Body = metadata };
            });
            Assert(api.Version == current && api.Digest == Digest && api.Uploaded > DateTime.MinValue, "API should preserve same-version rebuild metadata");

            foreach (int status in new[] { 0, 200, 403, 429 })
            {
                var fallback = Island.FindUpdateRelease(old, (url, follow) =>
                {
                    if (url == Api) return new WebResult { Status = status, Body = "<invalid JSON>" };
                    if (url == Root + "latest")
                    {
                        Assert(!follow, "Inspect the latest release redirect");
                        return new WebResult { Status = 302, Location = Tag };
                    }
                    Assert(url == Download + "SHA256SUMS.txt" && follow, "Look up the checksum for the pinned release");
                    return new WebResult { Status = 200, Body = new string('b', 64) + "  Deskmate-Setup.exe\r\n" + Digest.ToUpperInvariant() + "  Deskmate.exe\r\n" };
                });
                Assert(fallback.Version == current && fallback.Url == Tag && fallback.Asset == Download + "Deskmate.exe" &&
                       fallback.Digest == Digest, "Resolve a verified update when the API is unavailable or invalid");
            }

            var manual = Island.FindUpdateRelease(old, (url, follow) =>
            {
                if (url == Api) return new WebResult { Status = 200, Body = metadata.Replace(Digest, new string('z', 64)) };
                Assert(url == Download + "SHA256SUMS.txt", "Try the manifest when the API has no usable checksum");
                return new WebResult { Status = 200, Body = Digest + "  Deskmate-Setup.exe\n" + new string('z', 64) + "  Deskmate.exe" };
            });
            Assert(manual.Url == Tag && manual.Asset == null && manual.Digest == null, "Offer the official page when verification metadata is unavailable");

            var same = Island.FindUpdateRelease(current, (url, follow) =>
            {
                if (url == Api) return new WebResult { Status = 403 };
                Assert(url == Root + "latest", "Skip the manifest when the fallback tag is already installed");
                return new WebResult { Status = 302, Location = Tag };
            });
            Assert(same.Version == current && same.Digest == null, "Do not infer a same-version rebuild without the API");

            foreach (string location in new[] { "https://example.com/v2.21.0", Tag + "?ignored", null })
            {
                var rejected = Island.FindUpdateRelease(old, (url, follow) =>
                {
                    if (url == Api) return new WebResult { Status = 403 };
                    Assert(url == Root + "latest", "Reject an unexpected redirect before requesting assets");
                    return new WebResult { Status = 302, Location = location };
                });
                Assert(rejected == null, "Report a failed check when neither endpoint identifies a trusted release");
            }

            return "update release resolution passed";
        }
    }
}
