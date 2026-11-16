using System;
using System.Collections.Generic;
using System.Globalization;

namespace Deskmate
{
    partial class Island
    {
        const int AskRowLimit = 30;

        string ReadAskSections(List<KeyValuePair<string, Dictionary<string, object>>> reads)
        {
            var results = new List<object>();
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var read in reads)
            {
                string section = Arg(read.Value, "section").Trim().ToLowerInvariant();
                string query = Arg(read.Value, "query").Trim();
                if (!seen.Add(section) || query.Length > 200) continue;
                results.Add(new { section = section, data = ReadAskSection(section, query) });
                if (results.Count == 4) break;
            }
            return Json.Encode(results.ToArray());
        }

        object ReadAskSection(string section, string query)
        {
            var rows = new List<string>();
            Action<string, string> add = (title, detail) =>
            {
                if (rows.Count <= AskRowLimit &&
                    (section == "notes" || query.Length == 0 || (title + " " + detail).IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0))
                    rows.Add(Fmt.OneLine(title, 240) + " | " + Fmt.OneLine(detail, 400));
            };
            switch (section)
            {
                case "tasks":
                    EnsureWorkData();
                    foreach (var task in todos)
                        add(task.Title, (task.Done ? "done" : "open") + " | " + task.ListName +
                            (task.Due.HasValue ? " | due " + AskStamp(task.Due.Value) : "") + " | " + task.Notes);
                    break;
                case "notes":
                    foreach (var note in NoteFiles.List(query, NoteFiles.LoadPins()))
                        add(note.Title, note.Preview);
                    break;
                case "schedule":
                    if (classes == null) return new { status = "not loaded" };
                    var now = DateTime.Now;
                    foreach (var slot in classes)
                        if (query.Length > 0 || slot.End >= now.Date)
                            add(slot.Title, AskStamp(slot.Start) + " to " + AskStamp(slot.End) +
                                (slot.AllDay ? " (all day)" : "") + (slot.Cancelled ? " (cancelled)" : "") + " | " + slot.Room);
                    break;
                case "reminders":
                    foreach (var reminder in oneShots) add(reminder.Text, AskStamp(reminder.At));
                    break;
                case "timer":
                    return new { active = TimerOn, type = timerKind.ToString(), paused = timerPaused,
                        seconds = TimerOn ? Math.Max(0, (long)(Counting ? Remaining() : Elapsed()).TotalSeconds) : 0,
                        countsDown = TimerOn && Counting };
                case "media":
                    return new { active = mediaInfo.Present, title = Fmt.OneLine(mediaInfo.Title, 240),
                        artist = Fmt.OneLine(mediaInfo.Artist, 240), playing = mediaInfo.Playing };
                case "shelf":
                    foreach (var item in shelf) add(item.Name, AskStamp(item.AddedUtc.ToLocalTime()));
                    break;
                case "clipboard":
                    return new { textItems = clips.Count, imageItems = imageClips.Count };
                case "messages":
                    foreach (var item in notifs) add(item.AppName, AskStamp(item.Time.ToLocalTime()));
                    break;
                default:
                    return new { status = "unknown section" };
            }
            bool truncated = rows.Count > AskRowLimit;
            if (truncated) rows.RemoveAt(AskRowLimit);
            return new { items = rows.ToArray(), truncated = truncated };
        }

        static string AskStamp(DateTime at)
        {
            return at.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);
        }

        ChatAct OpenAskSection(string section)
        {
            string label;
            switch (section)
            {
                case "tasks": OpenWork(0); label = "Tugas"; break;
                case "notes": OpenWork(1); label = "Catatan"; break;
                case "schedule":
                    if (!expanded && !CanExpand()) return AskOpenBlocked();
                    ShowSchedule(); label = "Jadwal"; break;
                case "clipboard":
                    if (!expanded && !CanExpand()) return AskOpenBlocked();
                    ShowClips(); label = "Clipboard"; break;
                case "settings": OpenPrefs(); label = "Pengaturan"; break;
                case "ai": OpenPrefs("ai"); label = "Atur AI"; break;
                default:
                    int page = Array.IndexOf(PageIds, section == "voice" ? "suara" : section == "reminders" ? "remind" : section);
                    if (page < 0) return null;
                    if (!expanded && !CanExpand()) return AskOpenBlocked();
                    if (page == PageShelf) SetSegment(PageShelf, 0, true);
                    Show(page);
                    label = PageNames[page];
                    break;
            }
            return new ChatAct { Glyph = IcoDone, Text = Lang.F("Dibuka: {0}", Lang.T(label)) };
        }

        static ChatAct AskOpenBlocked()
        {
            return new ChatAct { Glyph = IcoFail, Text = Lang.T("Panel Deskmate belum bisa dibuka") };
        }

        ChatAct AddAskTask(string title, string due)
        {
            title = title.Trim();
            if (title.Length == 0 || title.Length > 3000)
                return new ChatAct { Glyph = IcoFail, Text = Lang.T("Judul tugas tidak valid") };
            DateTime at = default(DateTime);
            if (due.Length > 0 && !DateTime.TryParseExact(due, new[] { "yyyy-MM-dd", "yyyy-MM-ddTHH:mm" },
                CultureInfo.InvariantCulture, DateTimeStyles.None, out at))
                return new ChatAct { Glyph = IcoFail, Text = Lang.T("Tanggal tugas tidak jelas") };

            EnsureWorkData();
            var task = new TodoItem { Title = title, ListName = TaskLists.Default, Due = due.Length > 0 ? (DateTime?)at : null };
            todos.Add(task);
            if (!TodosChanged())
            {
                todos.Remove(task);
                return new ChatAct { Glyph = IcoFail, Text = Lang.T("Tugas gagal disimpan") };
            }
            ReloadWorkFromSync();
            return new ChatAct { Glyph = Ink.IcoCalendar, Text = Lang.F("Tugas baru: {0}", title) };
        }
    }
}
