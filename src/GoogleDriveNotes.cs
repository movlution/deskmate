using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace Deskmate
{
    static class GoogleDriveNotesClient
    {
        const string FilesEndpoint = "https://www.googleapis.com/drive/v3/files";
        const string UploadEndpoint = "https://www.googleapis.com/upload/drive/v3/files";
        const string FolderName = "Deskmate Catatan";
        const string LegacyFolderName = "Tepi Catatan";
        const string FolderSettingKey = "drive.notes.folder";

        static void RenameLegacyFolder(ref GoogleCalVault vault, string id, string name)
        {
            if (name != LegacyFolderName) return;
            string error;
            var body = Json.Encode(new Dictionary<string, object> { { "name", FolderName } });
            var result = GoogleWorkspaceHttp.SendWithRefresh(ref vault, "PATCH", FilesEndpoint + "/" + Uri.EscapeDataString(id),
                body, "application/json; charset=utf-8", out error);
            if (!result.Ok) Log.Write("Drive folder rename: " + (error ?? result.Status.ToString()));
        }

        static string NoteProperty(object props, string key)
        {
            var properties = props as Dictionary<string, object>;
            return properties != null && properties.ContainsKey("deskmate" + key) ? Json.Str(props, "deskmate" + key) : Json.Str(props, "tepi" + key);
        }

        static string EnsureFolder(ref GoogleCalVault vault, out string error)
        {
            error = null;
            string savedId = new Settings().Get(FolderSettingKey, "");
            if (!string.IsNullOrEmpty(savedId))
            {
                string checkUrl = FilesEndpoint + "/" + Uri.EscapeDataString(savedId) + "?fields=id,name,trashed";
                FetchResult cr = GoogleWorkspaceHttp.SendWithRefresh(ref vault, "GET", checkUrl, null, null, out error);
                if (error != null) return null;
                if (cr.Ok)
                {
                    try
                    {
                        object co = Json.Parse(cr.Text);
                        if (co != null && !GoogleWorkspaceHttp.JsonBool(co, "trashed"))
                        {
                            RenameLegacyFolder(ref vault, savedId, Json.Str(co, "name"));
                            return savedId;
                        }
                    }
                    catch (Exception) { }
                }
            }

            string q = "(name='" + FolderName + "' or name='" + LegacyFolderName + "') and mimeType='application/vnd.google-apps.folder' and trashed=false";
            string listUrl = FilesEndpoint + "?q=" + Uri.EscapeDataString(q) + "&fields=files(id,name)";
            FetchResult lr = GoogleWorkspaceHttp.SendWithRefresh(ref vault, "GET", listUrl, null, null, out error);
            if (error != null) return null;
            if (!lr.Ok)
            {
                error = Lang.T("Gagal memuat catatan Google Drive");
                return null;
            }
            if (lr.Ok)
            {
                try
                {
                    object lo = Json.Parse(lr.Text);
                    object[] files = lo != null ? Json.Arr(lo, "files") : null;
                    if (files != null && files.Length > 0)
                    {
                        object folder = files[0];
                        foreach (object candidate in files)
                            if (Json.Str(candidate, "name") == FolderName) { folder = candidate; break; }
                        string fid = Json.Str(folder, "id");
                        if (!string.IsNullOrEmpty(fid))
                        {
                            RenameLegacyFolder(ref vault, fid, Json.Str(folder, "name"));
                            var s = new Settings();
                            s.Set(FolderSettingKey, fid);
                            s.Save();
                            return fid;
                        }
                    }
                }
                catch (Exception) { }
            }

            var body = new Dictionary<string, object>
            {
                { "name", FolderName },
                { "mimeType", "application/vnd.google-apps.folder" }
            };
            FetchResult mr = GoogleWorkspaceHttp.SendWithRefresh(ref vault, "POST", FilesEndpoint + "?fields=id", Json.Encode(body), "application/json; charset=utf-8", out error);
            if (error != null) return null;
            if (mr.Ok)
            {
                try
                {
                    object mo = Json.Parse(mr.Text);
                    string fid = mo != null ? Json.Str(mo, "id") : "";
                    if (!string.IsNullOrEmpty(fid))
                    {
                        var s = new Settings();
                        s.Set(FolderSettingKey, fid);
                        s.Save();
                        return fid;
                    }
                }
                catch (Exception) { }
            }
            error = Lang.T("Gagal membuat folder Google Drive");
            return null;
        }

        static DateTime ParseUtc(string s)
        {
            if (string.IsNullOrEmpty(s)) return DateTime.MinValue;
            DateTime d;
            if (DateTime.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out d))
                return d.ToUniversalTime();
            return DateTime.MinValue;
        }

        static DateTime NoteVersion(object file)
        {
            DateTime version = ParseUtc(NoteProperty(Json.Obj(file, "appProperties"), "UpdatedUtc"));
            return version == DateTime.MinValue ? ParseUtc(Json.Str(file, "modifiedTime")) : version;
        }

        static string BuildMultipart(Dictionary<string, object> metaObj, string content, string boundary)
        {
            var sb = new StringBuilder();
            sb.Append("--").Append(boundary).Append("\r\n");
            sb.Append("Content-Type: application/json; charset=UTF-8\r\n\r\n");
            sb.Append(Json.Encode(metaObj)).Append("\r\n");
            sb.Append("--").Append(boundary).Append("\r\n");
            sb.Append("Content-Type: text/markdown; charset=UTF-8\r\n\r\n");
            sb.Append(content ?? "").Append("\r\n");
            sb.Append("--").Append(boundary).Append("--");
            return sb.ToString();
        }

        internal static bool Transfer(string notesDir, string quickPath, Dictionary<string, NoteMeta> meta, HashSet<string> pins,
            List<string> pendingDeletes, List<string> completedDeletes, bool load, Dictionary<string, DateTime> driveVersions,
            out bool changed, out string error)
        {
            changed = false;
            error = null;
            string firstErr = null;
            if (meta == null) return false;

            GoogleCalVault vault = GoogleCalendarSource.EnsureVaultToken(out error);
            if (vault == null) return false;

            if (!load && pendingDeletes != null && pendingDeletes.Count > 0)
            {
                for (int i = pendingDeletes.Count - 1; i >= 0; i--)
                {
                    string entry = pendingDeletes[i];
                    if (string.IsNullOrEmpty(entry)) { pendingDeletes.RemoveAt(i); continue; }
                    if (entry.StartsWith("key:", StringComparison.OrdinalIgnoreCase)) continue;
                    string delErr;
                    FetchResult dr = GoogleWorkspaceHttp.SendWithRefresh(ref vault, "DELETE", FilesEndpoint + "/" + Uri.EscapeDataString(entry), null, null, out delErr);
                    if (delErr != null) { error = delErr; return false; }
                    if (dr.Ok || dr.Status == 404)
                    {
                        pendingDeletes.RemoveAt(i);
                        if (completedDeletes != null) completedDeletes.Add(entry);
                        changed = true;
                    }
                    else if (firstErr == null) firstErr = Lang.T("Gagal menyimpan catatan ke Google Drive");
                }
            }
            string folderId = EnsureFolder(ref vault, out error);
            if (error != null || string.IsNullOrEmpty(folderId)) return false;

            string q = "'" + folderId + "' in parents and trashed=false";
            string listUrl = FilesEndpoint + "?q=" + Uri.EscapeDataString(q) + "&fields=nextPageToken,files(id,name,modifiedTime,appProperties,trashed)&pageSize=200";
            var remoteById = new Dictionary<string, object>(StringComparer.Ordinal);
            var remoteByKey = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
            string page = "";
            do
            {
                string url = page.Length == 0 ? listUrl : listUrl + "&pageToken=" + Uri.EscapeDataString(page);
                FetchResult lr = GoogleWorkspaceHttp.SendWithRefresh(ref vault, "GET", url, null, null, out error);
                if (error != null) return false;
                try
                {
                    var result = lr.Ok ? Json.Parse(lr.Text) as Dictionary<string, object> : null;
                    if (result == null) throw new FormatException();
                    foreach (var f in Json.Arr(result, "files") ?? new object[0])
                    {
                        if (GoogleWorkspaceHttp.JsonBool(f, "trashed")) continue;
                        string fid = Json.Str(f, "id");
                        if (string.IsNullOrEmpty(fid)) continue;
                        remoteById[fid] = f;
                        string key = NoteProperty(Json.Obj(f, "appProperties"), "Key");
                        if (string.IsNullOrEmpty(key)) key = Json.Str(f, "name");
                        if (!string.IsNullOrEmpty(key)) remoteByKey[key] = f;
                    }
                    page = Json.Str(result, "nextPageToken");
                }
                catch (Exception)
                {
                    error = Lang.T("Gagal memuat catatan Google Drive");
                    return false;
                }
            } while (page.Length != 0);

            if (!load && pendingDeletes != null && pendingDeletes.Count > 0)
            {
                for (int i = pendingDeletes.Count - 1; i >= 0; i--)
                {
                    string entry = pendingDeletes[i];
                    if (string.IsNullOrEmpty(entry) || !entry.StartsWith("key:", StringComparison.OrdinalIgnoreCase)) continue;
                    string key = entry.Substring(4);
                    object rf;
                    if (remoteByKey.TryGetValue(key, out rf))
                    {
                        string fid = Json.Str(rf, "id");
                        string delErr;
                        FetchResult dr = GoogleWorkspaceHttp.SendWithRefresh(ref vault, "DELETE", FilesEndpoint + "/" + Uri.EscapeDataString(fid), null, null, out delErr);
                        if (delErr != null) { error = delErr; return false; }
                        if (dr.Ok || dr.Status == 404)
                        {
                            remoteById.Remove(fid);
                            remoteByKey.Remove(key);
                            pendingDeletes.RemoveAt(i);
                            if (completedDeletes != null) completedDeletes.Add(entry);
                            changed = true;
                        }
                        else if (firstErr == null) firstErr = Lang.T("Gagal menyimpan catatan ke Google Drive");
                    }
                    else
                    {
                        pendingDeletes.RemoveAt(i);
                        if (completedDeletes != null) completedDeletes.Add(entry);
                        changed = true;
                    }
                }
            }

            var localFiles = new List<string>();
            if (!load)
            {
                if (Directory.Exists(notesDir)) localFiles.AddRange(Directory.GetFiles(notesDir, "*.md"));
                if (File.Exists(quickPath)) localFiles.Add(quickPath);
            }

            foreach (string path in localFiles)
            {
                if (!File.Exists(path)) continue;
                string key = path == quickPath ? NoteFiles.QuickKey : Path.GetFileName(path);
                NoteMeta nm;
                if (!meta.TryGetValue(key, out nm))
                {
                    nm = new NoteMeta { Key = key };
                    meta[key] = nm;
                }
                else if (string.IsNullOrEmpty(nm.Key)) nm.Key = key;
                if ((pendingDeletes != null && (pendingDeletes.Contains("key:" + key) || (!string.IsNullOrEmpty(nm.DriveId) && pendingDeletes.Contains(nm.DriveId)))) || NoteFiles.IsDeletedRemote(nm.DriveId, key)) continue;

                DateTime localMtime = File.GetLastWriteTimeUtc(path);
                if (nm.UpdatedUtc > localMtime) localMtime = nm.UpdatedUtc;

                object remoteObj = null;
                if (!string.IsNullOrEmpty(nm.DriveId) && remoteById.ContainsKey(nm.DriveId))
                    remoteObj = remoteById[nm.DriveId];
                else if (remoteByKey.ContainsKey(key))
                    remoteObj = remoteByKey[key];

                DateTime uploadedVersion;
                if (!driveVersions.TryGetValue(key, out uploadedVersion))
                    uploadedVersion = remoteObj == null ? DateTime.MinValue : NoteVersion(remoteObj);
                if (localMtime > uploadedVersion)
                {
                    string text;
                    if (!NoteFiles.TryRead(path, out text))
                    {
                        if (firstErr == null) firstErr = Lang.T("Gagal memuat");
                        continue;
                    }
                    // A save that overlaps this read stays pending for the next upload.
                    var appProps = new Dictionary<string, object>
                    {
                        { "deskmateKey", key },
                        { "deskmateColor", nm.Color ?? "" },
                        { "deskmatePinned", (pins != null && pins.Contains(key)) ? "1" : "0" },
                        { "deskmateArchived", nm.Archived ? "1" : "0" },
                        { "deskmateLabels", nm.Labels ?? "" },
                        { "deskmateUpdatedUtc", localMtime.ToUniversalTime().ToString("o", CultureInfo.InvariantCulture) }
                    };
                    var fileMeta = new Dictionary<string, object>
                    {
                        { "name", key == NoteFiles.QuickKey ? "catatan-kilat.md" : key },
                        { "appProperties", appProps }
                    };
                    string boundary = "deskmate_" + Guid.NewGuid().ToString("N");
                    string payload = BuildMultipart(fileMeta, text, boundary);
                    string ct = "multipart/related; boundary=" + boundary;
                    string upErr;
                    FetchResult ur;
                    if (remoteObj == null)
                    {
                        fileMeta["parents"] = new List<object> { folderId };
                        payload = BuildMultipart(fileMeta, text, boundary);
                        ur = GoogleWorkspaceHttp.SendWithRefresh(ref vault, "POST", UploadEndpoint + "?uploadType=multipart&fields=id,modifiedTime", payload, ct, out upErr);
                    }
                    else
                    {
                        string fid = Json.Str(remoteObj, "id");
                        ur = GoogleWorkspaceHttp.SendWithRefresh(ref vault, "PATCH", UploadEndpoint + "/" + Uri.EscapeDataString(fid) + "?uploadType=multipart&fields=id,modifiedTime", payload, ct, out upErr);
                    }
                    if (upErr != null) { error = upErr; return false; }
                    if (ur.Ok)
                    {
                        try
                        {
                            object uo = Json.Parse(ur.Text);
                            string fid = uo != null ? Json.Str(uo, "id") : "";
                            if (string.IsNullOrEmpty(fid)) throw new FormatException();
                            nm.DriveId = fid;
                            driveVersions[key] = localMtime;
                            changed = true;
                        }
                        catch (Exception)
                        {
                            if (firstErr == null) firstErr = Lang.T("Gagal menyimpan catatan ke Google Drive");
                        }
                    }
                    else if (firstErr == null) firstErr = Lang.T("Gagal menyimpan catatan ke Google Drive");
                }
            }

            // Download remote notes that are newer or missing locally
            if (!load)
            {
                error = firstErr;
                return true;
            }
            driveVersions.Clear();
            foreach (var kv in remoteByKey)
            {
                object f = kv.Value;
                string fid = Json.Str(f, "id");
                object props = Json.Obj(f, "appProperties");
                string key = kv.Key;

                if (key != NoteFiles.QuickKey && (Path.GetFileName(key) != key || !key.EndsWith(".md", StringComparison.OrdinalIgnoreCase) || key.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)) continue;
                if ((pendingDeletes != null && (pendingDeletes.Contains(fid) || pendingDeletes.Exists(x => string.Equals(x, "key:" + key, StringComparison.OrdinalIgnoreCase)))) || NoteFiles.IsDeletedRemote(fid, key)) continue;
                string targetPath = key == NoteFiles.QuickKey ? quickPath : Path.Combine(notesDir, key);
                DateTime remoteMtime = NoteVersion(f);
                driveVersions[key] = remoteMtime;
                bool exists = File.Exists(targetPath);
                DateTime localMtime = exists ? File.GetLastWriteTimeUtc(targetPath) : DateTime.MinValue;

                NoteMeta localMeta;
                DateTime localVersion = localMtime;
                if (meta.TryGetValue(key, out localMeta) && localMeta.UpdatedUtc > localVersion)
                    localVersion = localMeta.UpdatedUtc;

                if (!exists || remoteMtime > localVersion)
                {
                    string dlErr;
                    FetchResult dr = GoogleWorkspaceHttp.SendWithRefresh(ref vault, "GET", FilesEndpoint + "/" + Uri.EscapeDataString(fid) + "?alt=media", null, null, out dlErr);
                    if (dlErr != null) { error = dlErr; return false; }
                    if (!dr.Ok)
                    {
                        if (firstErr == null) firstErr = Lang.T("Gagal menyimpan catatan");
                    }
                    else
                    {
                        bool skipped;
                        bool wrote = NoteFiles.WriteFromDrive(targetPath, dr.Text ?? "", fid, key, exists, localMtime, remoteMtime, out skipped);
                        if (skipped) continue;
                        if (!wrote)
                        {
                            if (firstErr == null) firstErr = Lang.T("Gagal menyimpan catatan");
                        }
                        else
                        {
                            NoteMeta nm;
                            if (!meta.TryGetValue(key, out nm))
                            {
                                nm = new NoteMeta { Key = key };
                                meta[key] = nm;
                            }
                            else if (string.IsNullOrEmpty(nm.Key)) nm.Key = key;
                            nm.DriveId = fid;
                            if (props != null)
                            {
                                nm.Color = NoteProperty(props, "Color");
                                nm.Archived = NoteProperty(props, "Archived") == "1";
                                nm.Labels = NoteProperty(props, "Labels");
                                if (pins != null)
                                {
                                    if (NoteProperty(props, "Pinned") == "1") pins.Add(key);
                                    else pins.Remove(key);
                                }
                            }
                            nm.UpdatedUtc = remoteMtime;
                            changed = true;
                        }
                    }
                }
            }

            error = firstErr;
            return true;
        }
    }
}
