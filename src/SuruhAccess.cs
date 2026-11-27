using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;

namespace Deskmate
{
    partial class Island
    {
        const int AskRowLimit = 40;
        const int AskNoteBodyLimit = 4000;

        string ReadAskSections(List<KeyValuePair<string, Dictionary<string, object>>> reads)
        {
            var results = new List<object>();
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var read in reads)
            {
                string section = Arg(read.Value, "section").Trim().ToLowerInvariant();
                string query = Arg(read.Value, "query").Trim();
                string key = section + "|" + query.ToLowerInvariant();
                if (!seen.Add(key) || query.Length > 200) continue;
                results.Add(new { section = section, query = query, data = ReadAskSection(section, query) });
                if (results.Count == 6) break;
            }
            return Json.Encode(results.ToArray());
        }

        object ReadAskSection(string section, string query)
        {
            switch (section)
            {
                case "overview":
                    return ReadAskOverview();
                case "tasks":
                    return ReadAskTasks(query);
                case "notes":
                    return ReadAskNotes(query);
                case "schedule":
                case "calendar":
                    return ReadAskSchedule(query);
                case "reminders":
                    return ReadAskReminders(query);
                case "timer":
                case "focus":
                    return ReadAskTimer();
                case "media":
                    return ReadAskMedia();
                case "control":
                case "system":
                    return ReadAskControl();
                case "shelf":
                    return ReadAskShelf(query);
                case "clipboard":
                    return ReadAskClipboard(query);
                case "messages":
                case "notifications":
                    return ReadAskMessages(query);
                case "integrations":
                    return ReadAskIntegrations();
                case "settings":
                    return ReadAskSettings();
                default:
                    return new { status = "unknown section" };
            }
        }

        object ReadAskOverview()
        {
            EnsureWorkData();
            int openTasks = 0, doneTasks = 0, dueToday = 0;
            var today = DateTime.Today;
            foreach (var t in todos)
            {
                if (t.Done) doneTasks++;
                else
                {
                    openTasks++;
                    if (t.Due.HasValue && t.Due.Value.Date <= today) dueToday++;
                }
            }
            int extOpen = tasks == null ? 0 : tasks.Count;
            var nextEvent = NextClass();
            int todayEvents = 0;
            if (classes != null)
                foreach (var c in classes)
                    if (!c.Cancelled && c.Start.Date == today) todayEvents++;

            var notes = NoteFiles.List("", NoteFiles.LoadPins());
            DateTime prayAt = DateTime.MinValue;
            int nextPray = PrayOn ? NextPrayer(out prayAt) : -1;
            string[] prayNames = { "Subuh", "Terbit", "Dzuhur", "Ashar", "Maghrib", "Isya" };

            return new
            {
                now = AskStamp(DateTime.Now),
                tasks = new
                {
                    open = openTasks,
                    done = doneTasks,
                    dueTodayOrOverdue = dueToday,
                    lists = TaskLists.Names(taskLists, todos).ToArray(),
                    externalOpen = extOpen,
                },
                notes = new { activeCount = notes.Count },
                schedule = new
                {
                    todayCount = todayEvents,
                    googleCalendarConnected = GoogleCalendarConnected,
                    next = nextEvent == null ? null : new
                    {
                        id = nextEvent.Id,
                        title = nextEvent.Title,
                        start = AskStamp(nextEvent.Start),
                        end = AskStamp(nextEvent.End),
                        room = nextEvent.Room,
                        source = nextEvent.Source,
                    },
                },
                reminders = new
                {
                    oneShotCount = oneShots.Count,
                    drinkToday = DrinkCount(),
                    drinkGoal = DrinkGoal,
                    nextPrayer = nextPray >= 0 && nextPray < prayNames.Length ? prayNames[nextPray] + " " + Fmt.Hm(prayAt) : "",
                },
                timer = ReadAskTimer(),
                media = ReadAskMedia(),
                control = ReadAskControl(),
                integrations = ReadAskIntegrations(),
            };
        }

        object ReadAskTasks(string query)
        {
            EnsureWorkData();
            var localItems = new List<object>();
            bool localTruncated = false;
            foreach (var task in todos)
            {
                string hay = task.Title + " " + task.ListName + " " + task.Notes + " " + (task.Done ? "done selesai" : "open belum");
                if (query.Length > 0 && hay.IndexOf(query, StringComparison.OrdinalIgnoreCase) < 0) continue;
                if (localItems.Count >= AskRowLimit) { localTruncated = true; break; }
                localItems.Add(new
                {
                    id = task.Id,
                    title = Fmt.OneLine(task.Title, 240),
                    done = task.Done,
                    star = task.Star,
                    list = TaskLists.NameOf(task),
                    due = task.Due.HasValue ? AskStamp(task.Due.Value) : "",
                    notes = Fmt.OneLine(task.Notes, 400),
                    parentId = task.ParentId ?? "",
                    googleSynced = !string.IsNullOrEmpty(task.GoogleTaskId),
                });
            }

            var extItems = new List<object>();
            bool extTruncated = false;
            if (tasks != null)
            {
                foreach (var t in tasks)
                {
                    string hay = t.Title + " " + t.Course + " " + t.Source + " " + t.Status;
                    if (query.Length > 0 && hay.IndexOf(query, StringComparison.OrdinalIgnoreCase) < 0) continue;
                    if (extItems.Count >= AskRowLimit) { extTruncated = true; break; }
                    extItems.Add(new
                    {
                        id = t.Id,
                        source = t.Source,
                        title = Fmt.OneLine(t.Title, 240),
                        course = t.Course ?? "",
                        status = t.Status ?? "",
                        deadline = t.DeadlineUtc != DateTime.MinValue ? AskStamp(t.DeadlineUtc.ToLocalTime()) : "",
                        readOnly = t.ReadOnly,
                        url = t.Url ?? "",
                    });
                }
            }

            return new
            {
                lists = TaskLists.Names(taskLists, todos).ToArray(),
                focusTask = FocusTaskTitle,
                localTasks = localItems.ToArray(),
                localTruncated = localTruncated,
                externalTasks = extItems.ToArray(),
                externalTruncated = extTruncated,
            };
        }

        object ReadAskNotes(string query)
        {
            var pins = NoteFiles.LoadPins();
            var meta = NoteFiles.LoadMeta();
            bool includeArchived = query.IndexOf("arsip", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                   query.IndexOf("archive", StringComparison.OrdinalIgnoreCase) >= 0;
            string search = includeArchived && (string.Equals(query, "arsip", StringComparison.OrdinalIgnoreCase) || string.Equals(query, "archive", StringComparison.OrdinalIgnoreCase)) ? "" : query;
            var active = NoteFiles.List(search, pins, meta, false, "");
            var list = new List<NoteInfo>(active);
            if (includeArchived) list.AddRange(NoteFiles.List(search, pins, meta, true, ""));

            var items = new List<object>();
            bool truncated = false;
            bool includeBody = search.Length > 0 && list.Count <= 3;
            foreach (var note in list)
            {
                if (items.Count >= AskRowLimit) { truncated = true; break; }
                string key = NoteFiles.KeyOf(note.Path);
                string body = "";
                if (includeBody || string.Equals(key, search, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(note.Title, search, StringComparison.OrdinalIgnoreCase))
                {
                    body = NoteFiles.Read(note.Path);
                    if (body.Length > AskNoteBodyLimit) body = body.Substring(0, AskNoteBodyLimit) + "…";
                }
                items.Add(new
                {
                    key = key,
                    title = Fmt.OneLine(note.Title, 240),
                    preview = Fmt.OneLine(note.Preview, 300),
                    content = body,
                    pinned = note.Pin,
                    archived = note.Archived,
                    quick = note.Quick,
                    color = note.Color ?? "",
                    labels = note.Labels ?? "",
                    checkDone = note.CheckDone,
                    checkTotal = note.CheckTotal,
                    updated = AskStamp(note.Time),
                    driveSynced = !string.IsNullOrEmpty(note.DriveId),
                });
            }
            return new { items = items.ToArray(), truncated = truncated, paletteColors = NotePalette.Keys };
        }

        object ReadAskSchedule(string query)
        {
            string googleEmail = sources != null ? (sources.GoogleEmail ?? "") : "";
            if (classes == null)
                return new { status = "not loaded", googleCalendarConnected = GoogleCalendarConnected, googleEmail = googleEmail };
            var now = DateTime.Now;
            var items = new List<object>();
            bool truncated = false;
            foreach (var slot in classes)
            {
                if (query.Length == 0 && slot.End < now.Date) continue;
                string hay = slot.Title + " " + slot.Room + " " + slot.Source + " " + AskStamp(slot.Start);
                if (query.Length > 0 && hay.IndexOf(query, StringComparison.OrdinalIgnoreCase) < 0) continue;
                if (items.Count >= AskRowLimit) { truncated = true; break; }
                items.Add(new
                {
                    id = slot.Id ?? "",
                    title = Fmt.OneLine(slot.Title, 240),
                    start = AskStamp(slot.Start),
                    end = AskStamp(slot.End),
                    allDay = slot.AllDay,
                    cancelled = slot.Cancelled,
                    exam = slot.Exam,
                    room = slot.Room ?? "",
                    online = slot.Online,
                    url = slot.Url ?? "",
                    source = slot.Source ?? "",
                    editable = slot.Source == "google" && !string.IsNullOrEmpty(slot.Id),
                });
            }
            return new
            {
                googleCalendarConnected = GoogleCalendarConnected,
                googleEmail = googleEmail,
                items = items.ToArray(),
                truncated = truncated,
            };
        }

        object ReadAskReminders(string query)
        {
            var ones = new List<object>();
            foreach (var r in oneShots)
            {
                if (query.Length > 0 && (r.Text + " " + AskStamp(r.At)).IndexOf(query, StringComparison.OrdinalIgnoreCase) < 0) continue;
                ones.Add(new { at = AskStamp(r.At), text = Fmt.OneLine(r.Text, 240) });
            }
            var ibadah = new List<object>();
            foreach (var it in Ibadah())
            {
                if (query.Length > 0 && (it.Name + " " + it.Hm).IndexOf(query, StringComparison.OrdinalIgnoreCase) < 0) continue;
                ibadah.Add(new { name = it.Name, time = it.Hm, days = it.DaysText(), beforeMinutes = it.Before });
            }
            var prayers = new Dictionary<string, string>();
            if (PrayOn)
            {
                var pt = Prayers();
                if (pt != null && pt.Length >= 6)
                {
                    prayers["Subuh"] = Fmt.Hm(pt[0]);
                    prayers["Terbit"] = Fmt.Hm(pt[1]);
                    prayers["Dzuhur"] = Fmt.Hm(pt[2]);
                    prayers["Ashar"] = Fmt.Hm(pt[3]);
                    prayers["Maghrib"] = Fmt.Hm(pt[4]);
                    prayers["Isya"] = Fmt.Hm(pt[5]);
                }
            }
            return new
            {
                oneShots = ones.ToArray(),
                wellness = new
                {
                    drinkEnabled = settings.GetBool("drink", false),
                    drinkEveryMinutes = DrinkEvery,
                    drinkCountToday = DrinkCount(),
                    drinkGoal = DrinkGoal,
                    eyesEnabled = settings.GetBool("eyes", false),
                    stretchEnabled = settings.GetBool("stretch", false),
                    stretchEveryMinutes = StretchEvery,
                },
                ibadah = ibadah.ToArray(),
                prayer = new
                {
                    enabled = PrayOn,
                    city = settings.Get("pray.city", ""),
                    times = prayers,
                },
            };
        }

        object ReadAskTimer()
        {
            return new
            {
                active = TimerOn,
                type = timerKind.ToString(),
                paused = timerPaused,
                seconds = TimerOn ? Math.Max(0, (long)(Counting ? Remaining() : Elapsed()).TotalSeconds) : 0,
                countsDown = TimerOn && Counting,
                focusTask = FocusTaskTitle,
                laps = laps.Count,
                focusStats = new
                {
                    minutesToday = FocusMinToday,
                    sessionsToday = FocusSessionsToday,
                    totalMinutes = FocusTotalMin,
                    streakDays = FocusStreakDays,
                },
            };
        }

        object ReadAskMedia()
        {
            var devices = new List<object>();
            if (audioDevices != null)
            {
                foreach (var d in audioDevices)
                    devices.Add(new { id = d.Id, name = d.Name, isDefault = d.Id == audioDefault });
            }
            return new
            {
                active = mediaInfo.Present,
                title = Fmt.OneLine(mediaInfo.Title, 240),
                artist = Fmt.OneLine(mediaInfo.Artist, 240),
                playing = mediaInfo.Playing,
                volumePercent = (int)Math.Round(vol * 100),
                muted = muted,
                micPresent = micPresent,
                micMuted = micMuted,
                outputDevices = devices.ToArray(),
            };
        }

        object ReadAskControl()
        {
            return new
            {
                volumePercent = (int)Math.Round(vol * 100),
                muted = muted,
                micPresent = micPresent,
                micMuted = micMuted,
                brightnessSupported = brightness != null && brightness.Supported,
                brightnessPercent = brightLevel,
                hasWifi = radios != null && radios.HasWifi,
                wifiOn = radios != null && radios.WifiOn,
                hasBluetooth = radios != null && radios.HasBluetooth,
                bluetoothOn = radios != null && radios.BluetoothOn,
                dnd = QuietSwitch,
                awake = AwakeOn,
                awakeLabel = AwakeOn ? AwakeLabel() : "",
                windowsDark = winDark,
                islandHidden = hiddenByUser,
                prompterOn = PrompterOn,
            };
        }

        object ReadAskShelf(string query)
        {
            var items = new List<object>();
            bool truncated = false;
            foreach (var item in shelf)
            {
                if (query.Length > 0 && item.Name.IndexOf(query, StringComparison.OrdinalIgnoreCase) < 0) continue;
                if (items.Count >= AskRowLimit) { truncated = true; break; }
                items.Add(new { id = item.Id, name = Fmt.OneLine(item.Name, 200), path = item.Path, added = AskStamp(item.AddedUtc.ToLocalTime()) });
            }
            return new { items = items.ToArray(), truncated = truncated };
        }

        object ReadAskClipboard(string query)
        {
            var items = new List<object>();
            bool truncated = false;
            for (int i = 0; i < clips.Count; i++)
            {
                var c = clips[i];
                if (query.Length > 0 && (c.Text ?? "").IndexOf(query, StringComparison.OrdinalIgnoreCase) < 0) continue;
                if (items.Count >= 20) { truncated = true; break; }
                items.Add(new { index = i, text = Fmt.OneLine(c.Text ?? "", 300), time = AskStamp(c.Utc.ToLocalTime()) });
            }
            return new { textCount = clips.Count, imageCount = imageClips.Count, items = items.ToArray(), truncated = truncated };
        }

        object ReadAskMessages(string query)
        {
            var items = new List<object>();
            bool truncated = false;
            foreach (var item in notifs)
            {
                string hay = item.AppName + " " + item.Title + " " + item.Body;
                if (query.Length > 0 && hay.IndexOf(query, StringComparison.OrdinalIgnoreCase) < 0) continue;
                if (items.Count >= AskRowLimit) { truncated = true; break; }
                items.Add(new
                {
                    app = Fmt.OneLine(item.AppName, 100),
                    title = Fmt.OneLine(item.Title, 200),
                    body = Fmt.OneLine(item.Body, 300),
                    time = AskStamp(item.Time.ToLocalTime()),
                });
            }
            return new { items = items.ToArray(), truncated = truncated };
        }

        object ReadAskIntegrations()
        {
            var feeds = new List<object>();
            if (sources != null)
            {
                foreach (var f in sources.Feeds)
                    feeds.Add(new { name = f.Name, givesTasks = f.GivesTasks, givesEvents = f.GivesEvents, error = f.Error ?? "" });
            }
            return new
            {
                google = new
                {
                    connected = GoogleCalendarConnected,
                    email = sources != null ? (sources.GoogleEmail ?? "") : "",
                    tasksSyncEnabled = settings.GetBool("google.tasks", true),
                    driveNotesSyncEnabled = settings.GetBool("google.notes", true),
                    syncBusy = WorkSyncBusy,
                    syncError = WorkSyncError ?? "",
                },
                todoist = new { connected = sources != null && sources.Todoist != null },
                notion = new { connected = sources != null && sources.Notion != null },
                kulia = new { connected = KuliaOn },
                holidays = new { enabled = sources != null && sources.Holidays.Enabled },
                icsFeeds = feeds.ToArray(),
                ingat = new { enabled = IngatOn, running = IngatClient.Running() },
            };
        }

        object ReadAskSettings()
        {
            return new
            {
                language = Lang.English ? "en" : "id",
                dockEnabled = settings.GetBool("dock", true),
                petEnabled = settings.GetBool("pet", true),
                petKind = settings.Get("pet.kind", "cat"),
                islandEyes = settings.GetBool("island.eyes", true),
                dnd = QuietSwitch,
                shelfEnabled = settings.GetBool("shelf", true),
                clipboardEnabled = settings.GetBool("clips", true),
                ingatEnabled = IngatOn,
                prayEnabled = settings.GetBool("pray", false),
                prayCity = settings.Get("pray.city", ""),
                drinkRemind = settings.GetBool("drink", false),
                eyesRemind = settings.GetBool("eyes", false),
                stretchRemind = settings.GetBool("stretch", false),
                aiProvider = Ai.Label(Ai.Text),
            };
        }

        static string AskStamp(DateTime at)
        {
            return at.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);
        }

        ChatAct OpenAskSection(string section)
        {
            section = (section ?? "").Trim().ToLowerInvariant();
            string label;
            switch (section)
            {
                case "tasks": OpenWork(0); label = "Tugas"; break;
                case "notes": OpenWork(1); label = "Catatan"; break;
                case "schedule":
                case "calendar":
                    if (!expanded && !CanExpand()) return AskOpenBlocked();
                    ShowSchedule(); label = "Jadwal"; break;
                case "pomodoro":
                case "timer":
                    if (!expanded && !CanExpand()) return AskOpenBlocked();
                    ShowPomodoro(); label = "Pomodoro"; break;
                case "clipboard":
                    if (!expanded && !CanExpand()) return AskOpenBlocked();
                    ShowClips(); label = "Clipboard"; break;
                case "settings": OpenPrefs(); label = "Pengaturan"; break;
                case "ai": OpenPrefs("ai"); label = "Atur AI"; break;
                case "integrations": OpenIntegrations(); label = "Integrasi"; break;
                case "prompter": OpenPrompter(); label = "Prompter"; break;
                case "chat": OpenSuruh(); label = "Obrolan"; break;
                case "quick_note": OpenNote(); label = "Catatan kilat"; break;
                default:
                    int page = Array.IndexOf(PageIds, section == "voice" ? "suara" : section == "reminders" ? "remind" : section == "work" ? "class" : section);
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
    }
}
