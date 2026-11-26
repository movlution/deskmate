using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Text.RegularExpressions;
using System.Windows.Forms;

namespace Deskmate
{
    sealed partial class WorkWindow : KitWindow
    {
        // ---------- notes ----------

        string NoteCountText()
        {
            int count = notes != null ? notes.Count : 0;
            if (count == 0)
            {
                if (current == null) return "";
                if (!string.IsNullOrWhiteSpace(search.Text) || !string.IsNullOrEmpty(colorFilter))
                    return Lang.T("Tidak ada catatan yang cocok");
                return Lang.T("Tidak ada catatan");
            }
            if (count == 1) return Lang.T("1 catatan");
            return Lang.F("{0} catatan", count);
        }

        void NoteEmptyStrings(out string eTitle, out string eSub)
        {
            if (noteArchiveFilter.Selected == 1)
            {
                eTitle = Lang.T("Arsip catatan kosong");
                eSub = Lang.T("Catatan yang diarsipkan akan muncul di sini");
            }
            else if (!string.IsNullOrWhiteSpace(search.Text) || !string.IsNullOrEmpty(colorFilter))
            {
                eTitle = Lang.T("Tidak ada catatan yang cocok");
                eSub = Lang.T("Coba kata kunci atau filter warna lain");
            }
            else
            {
                eTitle = Lang.T("Belum ada catatan");
                eSub = Lang.T("Buat catatan baru untuk menyimpan ide atau checklist");
            }
        }

        Color CurrentNoteColor()
        {
            NoteMeta m = GetMeta(current, false);
            return m != null ? NotePalette.ColorOf(m.Color) : Color.Empty;
        }

        Color EditorHeaderColor()
        {
            Color c = CurrentNoteColor();
            if (c == Color.Empty) return KitInk.Group;
            return Ink.Mix(KitInk.Group, c, Ink.Light ? 0.11f : 0.15f);
        }

        static int CountWords(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return 0;
            int count = 0;
            bool inWord = false;
            for (int i = 0; i < text.Length; i++)
            {
                if (char.IsWhiteSpace(text[i])) inWord = false;
                else if (!inWord) { inWord = true; count++; }
            }
            return count;
        }

        static void CountChecklist(string text, out int done, out int total)
        {
            done = 0; total = 0;
            if (string.IsNullOrEmpty(text)) return;
            foreach (var raw in text.Split('\n'))
            {
                string tr = raw.Trim();
                if (tr.StartsWith("- [ ] ", StringComparison.Ordinal)) total++;
                else if (tr.StartsWith("- [x] ", StringComparison.OrdinalIgnoreCase)) { total++; done++; }
            }
        }

        void UpdateNoteStats()
        {
            noteStatsTimer.Stop();
            if (current == null) { noteMetaLine = ""; return; }
            string body = editor.Text;
            noteMetaLine = noteModified.ToString("d MMM", CultureInfo.CurrentCulture) + "  \u00B7  " + Lang.F("{0} kata", CountWords(body));
            int done, total;
            CountChecklist(body, out done, out total);
            if (total > 0) noteMetaLine += "  \u00B7  " + Lang.F("{0}/{1} selesai", done, total);
            if (IsHandleCreated && !editorMetaRect.IsEmpty) Invalidate(editorMetaRect);
        }

        void OnNoteInputChanged()
        {
            if (loading || current == null) return;
            bool statusChanged = !dirty || saveFailed || saveStatusText.Length > 0;
            dirty = true;
            saveFailed = false;
            saveStatusText = "";
            saveTimer.Stop();
            saveTimer.Start();
            noteStatsTimer.Start();
            if (statusChanged && IsHandleCreated && !editorMetaRect.IsEmpty) Invalidate(editorMetaRect);
        }

        void RefreshNotes(string keepPath)
        {
            if (!IsHandleCreated) return;
            loading = true;
            try
            {
                notes = NoteFiles.List(search.Text, pins, noteMeta, noteArchiveFilter.Selected == 1, colorFilter);
                noteList.SetData(notes, keepPath);
            }
            finally { loading = false; }
            ApplyNoteEditorVisibility();
            // Filtering the sidebar never clears the current editor.
            Invalidate();
        }

        void OnArchiveFilterChanged()
        {
            if (!SaveCurrent(true))
            {
                noteArchiveFilter.Selected = noteArchiveFilter.Selected == 0 ? 1 : 0;
                return;
            }
            RefreshNotes(current);
            bool hasCurrent = false;
            if (current != null)
                for (int i = 0; i < notes.Count; i++)
                    if (string.Equals(notes[i].Path, current, StringComparison.OrdinalIgnoreCase)) { hasCurrent = true; break; }
            if (!hasCurrent)
                ShowNote(notes.Count > 0 ? notes[0].Path : null);
        }

        void OpenNote(string path)
        {
            if (string.Equals(path, current, StringComparison.OrdinalIgnoreCase)) return;
            if (!SaveCurrent(true)) { RefreshNotes(current); return; }
            ShowNote(path);
            // Saving the old note refreshed the list around the old path; point it at the new one
            noteList.SelectPath(current);
        }

        void ShowNote(string path)
        {
            string text = "";
            if (path != null && !NoteFiles.TryRead(path, out text))
            {
                KitAsk.Notice(this, Lang.T("Catatan tidak dapat dibaca."));
                RefreshNotes(current);
                return;
            }
            current = path;
            baseline = text;
            loading = true;
            bool isQuick = path != null && string.Equals(path, NoteFiles.QuickPath, StringComparison.OrdinalIgnoreCase);
            if (path == null)
            {
                noteTitleBox.ReadOnly = false;
                noteTitleBox.BackColor = KitInk.Group;
                noteTitleBox.Text = "";
                editor.Text = "";
                noteTitleBox.Enabled = false;
                editor.Enabled = false;
            }
            else if (isQuick)
            {
                noteTitleBox.Text = Lang.T("Catatan kilat");
                noteTitleBox.ReadOnly = true;
                noteTitleBox.BackColor = KitInk.Group;
                editor.Text = NoteFiles.NormalizeNewlines(text);
                noteTitleBox.Enabled = true;
                editor.Enabled = true;
            }
            else
            {
                string nTitle, nBody;
                NoteFiles.SplitNote(text, out nTitle, out nBody);
                noteTitleBox.ReadOnly = false;
                noteTitleBox.BackColor = KitInk.Group;
                noteTitleBox.Text = nTitle;
                editor.Text = NoteFiles.NormalizeNewlines(nBody);
                noteTitleBox.Enabled = true;
                editor.Enabled = true;
            }
            saveStatusText = "";
            saveFailed = false;
            noteTitleBox.SelectionStart = 0;
            noteTitleBox.SelectionLength = 0;
            editor.SelectionStart = 0;
            editor.SelectionLength = 0;
            loading = false;
            dirty = false;
            noteModified = DateTime.Now;
            if (path != null)
            {
                try { noteModified = System.IO.File.GetLastWriteTime(path); }
                catch (Exception) { }
            }
            UpdateNoteStats();

            NoteMeta m = GetMeta(path, false);
            noteTagChip.Tags = m != null ? (m.Labels ?? "") : "";
            string curCol = m != null ? (m.Color ?? "") : "";
            colorBtn.DotColor = NotePalette.ColorOf(curCol);
            bool pinned = path != null && NoteFiles.IsPinned(pins, path);
            pinBtn.Active = pinned;
            noteTips.SetToolTip(pinBtn, pinned ? Lang.T("Lepas sematan") : Lang.T("Sematkan"));
            bool archived = m != null && m.Archived;
            archiveBtn.Active = archived;
            noteTips.SetToolTip(archiveBtn, archived ? Lang.T("Pulihkan") : Lang.T("Arsipkan"));

            ApplyNoteEditorVisibility();
            if (IsHandleCreated) LayoutNoteTagChip();
            editor.RefreshFormatting();
            Invalidate(true);
        }

        string SerializeCurrentNote()
        {
            if (current != null && string.Equals(current, NoteFiles.QuickPath, StringComparison.OrdinalIgnoreCase))
                return NoteFiles.NormalizeNewlines(editor.Text);
            return NoteFiles.JoinNote(noteTitleBox.Text, NoteFiles.NormalizeNewlines(editor.Text));
        }

        void SetSaveState(string text)
        {
            saveStatusText = text ?? "";
            if (IsHandleCreated && !editorMetaRect.IsEmpty) Invalidate(editorMetaRect);
        }

        bool SaveCurrent() { return SaveCurrent(true); }
        bool SaveCurrent(bool notify)
        {
            if (current == null || !dirty) return true;
            saveTimer.Stop();
            string disk;
            string payload = SerializeCurrentNote();
            if (payload == baseline)
            {
                dirty = false; saveFailed = false;
                SetSaveState("");
                return true;
            }
            // Compare content, not timestamps: quick-note append and external edits
            // must never be silently overwritten by an old editor buffer.
            if (!NoteFiles.TryRead(current, out disk) || disk != baseline)
            {
                saveFailed = true; SetSaveState(Lang.T("Konflik"));
                if (!notify) return false;
                saveTimer.Stop();
                if (!KitAsk.Confirm(this, Lang.T("Catatan berubah di luar aplikasi. Timpa draf ini?"), Lang.T("Timpa"))) return false;
                if (!NoteFiles.Write(current, payload)) return SaveError(notify);
            }
            else if (!NoteFiles.Write(current, payload)) return SaveError(notify);
            saveTimer.Stop();
            baseline = payload; dirty = false; saveFailed = false;
            noteModified = DateTime.Now;
            UpdateNoteStats();
            SetSaveState("");
            owner.ScheduleDriveNotesUpload();
            RefreshNotes(current);
            return true;
        }

        bool SaveError(bool notify)
        {
            saveFailed = true; SetSaveState(Lang.T("Gagal menyimpan"));
            if (notify) KitAsk.Notice(this, Lang.T("Gagal menyimpan."));
            return false;
        }

        void NewNote()
        {
            if (owner.DriveNotesLoading) return;
            if (!SaveCurrent(true)) return;
            string path;
            try { path = NoteFiles.NewPath(); }
            catch (Exception ex) { KitAsk.Notice(this, ex.Message); return; }
            if (!NoteFiles.Write(path, "")) { SaveError(true); return; }
            owner.ScheduleDriveNotesUpload();
            search.Text = "";
            colorFilter = "";
            colorStrip.Selected = "";
            colorStrip.Invalidate();
            if (noteArchiveFilter.Selected != 0) noteArchiveFilter.Selected = 0;
            ShowNote(path);
            RefreshNotes(path);
            noteTitleBox.Focus();
        }

        void TogglePin() { TogglePin(current); }
        void TogglePin(string path)
        {
            if (owner.DriveNotesLoading) return;
            if (string.IsNullOrEmpty(path)) return;
            if (!SaveCurrent(true)) return;
            NoteMeta meta = GetMeta(path, true);
            DateTime previous = meta.UpdatedUtc;
            meta.UpdatedUtc = DateTime.UtcNow;
            if (!NoteFiles.SaveMeta(noteMeta))
            {
                meta.UpdatedUtc = previous;
                KitAsk.Notice(this, Lang.T("Gagal menyimpan."));
                return;
            }
            NoteFiles.SetPin(pins, path, !NoteFiles.IsPinned(pins, path));
            RefreshNotes(current);
            owner.ScheduleDriveNotesUpload();
            if (string.Equals(path, current, StringComparison.OrdinalIgnoreCase)) ShowNote(current);
        }

        void DeleteNote() { DeleteNote(null); }

        void DeleteNote(string path)
        {
            if (owner.DriveNotesLoading) return;
            // A pinned draft is saved first; deleting another row must not drop it
            if (!SaveCurrent(true)) return;
            string target = path ?? current;
            if (target == null) return;
            bool quick = string.Equals(target, NoteFiles.QuickPath, StringComparison.OrdinalIgnoreCase);
            // The quick-note file is shared with Ctrl+Alt+N, so it keeps the question; other notes get undo
            if (quick && !KitAsk.Confirm(this, Lang.T("Hapus catatan ini?"))) return;
            if (owner.DriveNotesLoading) return;
            saveTimer.Stop();
            string deleted = target;
            string keptText;
            if (!NoteFiles.TryRead(deleted, out keptText)) keptText = null;
            NoteMeta keptMeta = GetMeta(deleted, false);
            string keptColor = keptMeta != null ? keptMeta.Color ?? "" : "";
            string keptLabels = keptMeta != null ? keptMeta.Labels ?? "" : "";
            bool keptArchived = keptMeta != null && keptMeta.Archived;
            bool keptPin = NoteFiles.IsPinned(pins, deleted);
            string delKey = NoteFiles.KeyOf(deleted);
            NoteMeta delMeta = GetMeta(deleted, false);
            if (delMeta == null || string.IsNullOrEmpty(delMeta.DriveId))
            {
                NoteMeta freshMeta;
                if (NoteFiles.LoadMeta().TryGetValue(delKey, out freshMeta) && freshMeta != null && !string.IsNullOrEmpty(freshMeta.DriveId))
                    delMeta = freshMeta;
            }
            var prevDeletes = NoteFiles.LoadDeletedDrive();
            if (delMeta != null && !string.IsNullOrEmpty(delMeta.DriveId)) NoteFiles.AddDeletedDrive(delMeta.DriveId);
            NoteFiles.AddDeletedKey(delKey);
            if (!NoteFiles.Remove(deleted))
            {
                NoteFiles.SaveDeletedDrive(prevDeletes);
                KitAsk.Notice(this, Lang.T("Catatan gagal dihapus."));
                return;
            }
            if (NoteFiles.IsPinned(pins, deleted)) NoteFiles.SetPin(pins, deleted, false);
            if (noteMeta.Remove(delKey)) NoteFiles.SaveMeta(noteMeta);
            owner.ScheduleDriveNotesUpload();
            bool wasOpen = string.Equals(deleted, current, StringComparison.OrdinalIgnoreCase);
            if (wasOpen) { dirty = false; current = null; }
            RefreshNotes(deleted);
            if (wasOpen) { if (notes.Count > 0) ShowNote(notes[0].Path); else ShowNote(null); }
            else if (current != null) ShowNote(current);
            if (!quick && keptText != null)
                ShowToast(Lang.T("Catatan dihapus"), delegate { RestoreNote(keptText, keptColor, keptLabels, keptArchived, keptPin); });
        }

        // Undo of a delete: the text comes back as a new note, so the removed one stays removed
        // on Drive and the copy uploads like any new note
        void RestoreNote(string text, string color, string labels, bool archived, bool pin)
        {
            if (owner.DriveNotesLoading || !SaveCurrent(true)) return;
            string path = WriteNewNote(text, color, labels, archived, pin);
            if (path == null) return;
            if (noteArchiveFilter.Selected != (archived ? 1 : 0)) noteArchiveFilter.Selected = archived ? 1 : 0;
            RefreshNotes(path);
            ShowNote(path);
            RefreshNotes(path);
        }

        string WriteNewNote(string text, string color, string labels, bool archived, bool pin)
        {
            string path;
            try { path = NoteFiles.NewPath(); }
            catch (Exception ex) { KitAsk.Notice(this, ex.Message); return null; }
            if (!NoteFiles.Write(path, text ?? "")) { SaveError(true); return null; }
            if (!string.IsNullOrEmpty(color) || !string.IsNullOrEmpty(labels) || archived)
            {
                NoteMeta m = GetMeta(path, true);
                m.Color = color ?? ""; m.Labels = labels ?? ""; m.Archived = archived;
                m.UpdatedUtc = DateTime.UtcNow;
                NoteFiles.SaveMeta(noteMeta);
            }
            if (pin) NoteFiles.SetPin(pins, path, true);
            owner.ScheduleDriveNotesUpload();
            return path;
        }

        void DuplicateNote(string path)
        {
            if (owner.DriveNotesLoading || string.IsNullOrEmpty(path)) return;
            if (!SaveCurrent(true)) return;
            string text;
            if (!NoteFiles.TryRead(path, out text)) { KitAsk.Notice(this, Lang.T("Catatan tidak dapat dibaca.")); return; }
            string nTitle, nBody;
            if (string.Equals(path, NoteFiles.QuickPath, StringComparison.OrdinalIgnoreCase)) { nTitle = Lang.T("Catatan kilat"); nBody = text; }
            else NoteFiles.SplitNote(text, out nTitle, out nBody);
            if (string.IsNullOrWhiteSpace(nTitle)) nTitle = Lang.T("Tanpa judul");
            NoteMeta m = GetMeta(path, false);
            string copy = WriteNewNote(NoteFiles.JoinNote(Lang.F("{0} (salinan)", nTitle.Trim()), nBody), m != null ? m.Color : "", m != null ? m.Labels : "", false, false);
            if (copy == null) return;
            if (noteArchiveFilter.Selected != 0) noteArchiveFilter.Selected = 0;
            RefreshNotes(copy);
            ShowNote(copy);
            RefreshNotes(copy);
            noteTitleBox.Focus();
            noteTitleBox.SelectAll();
        }

        void CopyNoteText(string path)
        {
            if (string.IsNullOrEmpty(path)) return;
            string text = string.Equals(path, current, StringComparison.OrdinalIgnoreCase) ? SerializeCurrentNote() : NoteFiles.Read(path);
            if (string.IsNullOrEmpty(text)) return;
            try { Clipboard.SetText(text); ShowToast(Lang.T("Disalin ke papan klip"), null); }
            catch (Exception ex) { Log.Write("salin catatan: " + ex.Message); }
        }

        void ShowNoteContextMenu(NoteInfo n, Point screenPos)
        {
            if (n == null) return;
            var menu = new ContextMenuStrip();
            menu.Closed += delegate { BeginInvoke(new Action(menu.Dispose)); };
            bool pinned = NoteFiles.IsPinned(pins, n.Path);
            NoteMeta m = GetMeta(n.Path, false);
            bool archived = m != null && m.Archived;
            menu.Items.Add(pinned ? Lang.T("Lepas sematan") : Lang.T("Sematkan"), null, delegate { TogglePin(n.Path); });
            menu.Items.Add(archived ? Lang.T("Pulihkan") : Lang.T("Arsipkan"), null, delegate { ToggleArchive(n.Path); });
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(Lang.T("Duplikat"), null, delegate { DuplicateNote(n.Path); });
            menu.Items.Add(Lang.T("Salin isi"), null, delegate { CopyNoteText(n.Path); });
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(Lang.T("Hapus"), null, delegate { DeleteNote(n.Path); });
            MenuLook.Apply(menu);
            menu.Show(screenPos);
        }

        void OpenTagDialog()
        {
            if (owner.DriveNotesLoading) return;
            if (current == null) return;
            NoteMeta m = GetMeta(current, false);
            string initial = m != null ? (m.Labels ?? "") : "";
            using (var dlg = new ListNameDialog(Lang.T("Tag catatan"), initial, true, Lang.T("Pisahkan dengan koma, mis. kerja, ide")))
            {
                if (dlg.ShowDialog(this) != DialogResult.OK || owner.DriveNotesLoading) return;
                NoteMeta target = GetMeta(current, true);
                string prevLabels = target.Labels;
                DateTime prevUtc = target.UpdatedUtc;
                target.Labels = NotePalette.CleanTags(dlg.Value);
                target.UpdatedUtc = DateTime.UtcNow;
                if (!NoteFiles.SaveMeta(noteMeta))
                {
                    target.Labels = prevLabels;
                    target.UpdatedUtc = prevUtc;
                    KitAsk.Notice(this, Lang.T("Gagal menyimpan."));
                    return;
                }
                noteTagChip.Tags = target.Labels;
                noteTagChip.Invalidate();
                LayoutNoteTagChip();
                RefreshNotes(current);
                owner.ScheduleDriveNotesUpload();
            }
        }

        void ShowNoteColorMenu()
        {
            if (owner.DriveNotesLoading) return;
            if (current == null) return;
            NoteMeta cm = GetMeta(current, false);
            string curCol = cm != null ? (cm.Color ?? "") : "";
            var menu = new ContextMenuStrip();
            var imgs = new List<Image>();
            menu.Closed += delegate
            {
                BeginInvoke(new Action(delegate
                {
                    foreach (var img in imgs) img.Dispose();
                    menu.Dispose();
                }));
            };
            for (int i = 0; i < NotePalette.Keys.Length; i++)
            {
                string key = NotePalette.Keys[i];
                var item = new ToolStripMenuItem(NotePalette.NameOf(key, false));
                item.Checked = string.Equals(curCol, key, StringComparison.OrdinalIgnoreCase);
                var bmp = new Bitmap(14, 14);
                using (var g = Graphics.FromImage(bmp))
                {
                    KitInk.Smooth(g);
                    Color sw = NotePalette.ColorOf(key);
                    if (sw == Color.Empty)
                    {
                        g.FillEllipse(KitInk.BrushOf(KitInk.Group), 1, 1, 11, 11);
                        g.DrawEllipse(KitInk.PenOf(KitInk.Sub, 1.3f), 1, 1, 11, 11);
                    }
                    else g.FillEllipse(KitInk.BrushOf(sw), 1, 1, 11, 11);
                }
                imgs.Add(bmp);
                item.Image = bmp;
                item.Click += delegate
                {
                    if (owner.DriveNotesLoading || current == null) return;
                    NoteMeta m = GetMeta(current, true);
                    string prevColor = m.Color;
                    DateTime prevUtc = m.UpdatedUtc;
                    m.Color = key;
                    m.UpdatedUtc = DateTime.UtcNow;
                    if (!NoteFiles.SaveMeta(noteMeta))
                    {
                        m.Color = prevColor;
                        m.UpdatedUtc = prevUtc;
                        KitAsk.Notice(this, Lang.T("Gagal menyimpan."));
                        return;
                    }
                    colorBtn.DotColor = NotePalette.ColorOf(key);
                    RefreshNotes(current);
                    Invalidate(true);
                    owner.ScheduleDriveNotesUpload();
                };
                menu.Items.Add(item);
            }
            MenuLook.Apply(menu);
            menu.Show(colorBtn, new Point(0, colorBtn.Height));
        }

        internal void ReloadFromSync()
        {
            ReloadTasksFromSync();
            ReloadNotesFromDisk();
        }

        internal void ReloadTasksFromSync()
        {
            if (IsDisposed || !IsHandleCreated) return;
            if (owner != null && (owner.todos == null || owner.taskLists == null)) owner.EnsureWorkData();
            if (owner != null && owner.todos != null) owner.todos.RemoveAll(t => t == null);
            savedTasks = CopyTasks(owner != null ? owner.todos : null);
            RefillTasks(null);
        }

        internal void RefreshNoteDriveIds(Dictionary<string, NoteMeta> fresh)
        {
            foreach (var entry in fresh)
            {
                NoteMeta local;
                if (noteMeta.TryGetValue(entry.Key, out local)) local.DriveId = entry.Value.DriveId;
                else noteMeta[entry.Key] = entry.Value;
            }
        }

        internal void ReloadNotesFromDisk()
        {
            if (IsDisposed || !IsHandleCreated) return;
            pins = NoteFiles.LoadPins();
            noteMeta = NoteFiles.LoadMeta();
            RefreshNotes(current);
            if (!dirty) ShowNote(current ?? (notes.Count > 0 ? notes[0].Path : null));
        }

        NoteMeta GetMeta(string path, bool create)
        {
            if (string.IsNullOrEmpty(path)) return null;
            string key = NoteFiles.KeyOf(path);
            NoteMeta m;
            if (!noteMeta.TryGetValue(key, out m) && create)
            {
                m = new NoteMeta { Key = key };
                noteMeta[key] = m;
            }
            return m;
        }

        void ToggleArchive() { ToggleArchive(current); }
        void ToggleArchive(string path) { ToggleArchive(path, true); }
        void ToggleArchive(string path, bool offerUndo)
        {
            if (owner.DriveNotesLoading) return;
            if (string.IsNullOrEmpty(path)) return;
            if (!SaveCurrent(true)) return;
            NoteMeta m = GetMeta(path, true);
            bool prevArchived = m.Archived;
            DateTime prevUtc = m.UpdatedUtc;
            m.Archived = !m.Archived;
            m.UpdatedUtc = DateTime.UtcNow;
            if (!NoteFiles.SaveMeta(noteMeta))
            {
                m.Archived = prevArchived;
                m.UpdatedUtc = prevUtc;
                KitAsk.Notice(this, Lang.T("Gagal menyimpan."));
                return;
            }
            RefreshNotes(current);
            if (string.Equals(path, current, StringComparison.OrdinalIgnoreCase))
                ShowNote(notes.Count > 0 ? notes[0].Path : null);
            owner.ScheduleDriveNotesUpload();
            if (offerUndo)
            {
                string target = path;
                ShowToast(m.Archived ? Lang.T("Catatan diarsipkan") : Lang.T("Catatan dipulihkan"), delegate
                {
                    ToggleArchive(target, false);
                    if (System.IO.File.Exists(target))
                    {
                        RefreshNotes(target);
                        bool listed = notes.Exists(x => string.Equals(x.Path, target, StringComparison.OrdinalIgnoreCase));
                        if (listed) OpenNote(target);
                    }
                });
            }
        }

        void ShowBlockMenu(bool fromSlash)
        {
            if (current == null || editor.ReadOnly || blockMenu != null) return;
            string note = current;
            int caret = editor.SelectionStart;
            var menu = new NoteBlockMenu(Ui, delegate(string prefix)
            {
                if (!Visible || current != note || editor.ReadOnly) return;
                editor.Focus();
                editor.Select(caret, 0);
                editor.ApplyBlock(prefix, fromSlash);
                editor.RefreshFormatting();
            });
            blockMenu = menu;
            menu.Closed += delegate(object sender, ToolStripDropDownClosedEventArgs e)
            {
                blockMenu = null;
                if (e.CloseReason == ToolStripDropDownCloseReason.Keyboard && editor.Visible) editor.Focus();
                if (IsHandleCreated && !IsDisposed) BeginInvoke(new Action(menu.Dispose));
                else menu.Dispose();
            };
            if (fromSlash)
            {
                Point point = editor.GetPositionFromCharIndex(caret);
                point.Y += Math.Max(editor.Font.Height, editor.SelectionFont != null ? editor.SelectionFont.Height : 0) + 3;
                menu.Show(editor, point);
            }
            else menu.Show(blockBtn, new Point(0, blockBtn.Height + 3));
        }

        static readonly Regex MarkdownList = new Regex(
            @"^([ 	]*)([-+*] \[[ xX]\]|[-+*]|([0-9]+)[.)])([ 	]+)(.*)$");

        void ContinueMarkdownList(object sender, KeyEventArgs e)
        {
            if (e.KeyData != Keys.Enter || editor.ReadOnly || editor.SelectionLength != 0) return;
            string text = editor.Text;
            int pos = editor.SelectionStart;
            if (pos == 0 || (pos < text.Length && text[pos] != '\r' && text[pos] != '\n')) return;
            int start = NoteEditor.LineStart(text, pos);
            if (NoteEditor.InCodeBlock(text, start)) return;
            Match item = MarkdownList.Match(text.Substring(start, pos - start));
            if (!item.Success) return;

            string marker = item.Groups[2].Value;
            if (item.Groups[3].Success)
            {
                int number;
                if (!int.TryParse(item.Groups[3].Value, out number) || number == int.MaxValue) return;
                marker = (number + 1).ToString(CultureInfo.InvariantCulture) + marker[marker.Length - 1];
            }
            else if (marker.Length > 1) marker = marker[0] + " [ ]";

            e.SuppressKeyPress = true;
            if (string.IsNullOrWhiteSpace(item.Groups[5].Value))
            {
                editor.Select(start, pos - start);
                editor.SelectedText = "";
            }
            else editor.SelectedText = "\n" + item.Groups[1].Value + marker + item.Groups[4].Value;
        }

        void ToggleChecklistLine()
        {
            if (current == null || !editor.Enabled || editor.ReadOnly) return;
            int pos = editor.SelectionStart;
            string text = editor.Text;
            int lineStart = NoteEditor.LineStart(text, pos);
            int lineEnd = text.IndexOf('\n', pos);
            if (lineEnd < 0) lineEnd = text.Length;
            string line = text.Substring(lineStart, lineEnd - lineStart);
            string next;
            if (line.StartsWith("- [ ] ", StringComparison.Ordinal)) next = "- [x] " + line.Substring(6);
            else if (line.StartsWith("- [x] ", StringComparison.OrdinalIgnoreCase)) next = line.Substring(6);
            else next = "- [ ] " + line;
            editor.Select(lineStart, lineEnd - lineStart);
            editor.SelectedText = next;
            editor.Focus();
        }

        void EditorMouseDown(object sender, MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Left || !editor.Enabled || editor.ReadOnly) return;
            int idx = editor.GetCharIndexFromPosition(e.Location);
            string text = editor.Text;
            int lineStart = NoteEditor.LineStart(text, idx);
            int lineEnd = text.IndexOf('\n', lineStart);
            if (lineEnd < 0) lineEnd = text.Length;
            if (NoteEditor.InCodeBlock(text, lineStart)) return;
            Match block = NoteEditor.Block.Match(text.Substring(lineStart, lineEnd - lineStart));
            string marker = block.Groups[2].Value;
            if (marker.Length != 5 || marker[2] != '[') return;

            int box = lineStart + block.Groups[2].Index + 2;
            Point left = editor.GetPositionFromCharIndex(box);
            Point right = editor.GetPositionFromCharIndex(box + 3);
            if (e.X < left.X || e.X >= right.X || e.Y < left.Y || e.Y >= left.Y + editor.Font.Height) return;
            editor.Select(box + 1, 1);
            editor.SelectedText = marker[3] == ' ' ? "x" : " ";
        }
    }
}
