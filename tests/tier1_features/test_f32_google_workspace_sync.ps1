# test_f32_google_workspace_sync.ps1
# Tier 1 tests for Feature 32: Google Tasks two-way sync + Google Drive note sync (offline only; no network).
# Exercises persistence layers (TodoFile, NoteFiles), Portable settings, GoogleTasksSource.Fetch and Lang.

$commonDir = Join-Path (Split-Path -Parent $PSScriptRoot) "common"
. (Join-Path $commonDir "TestHarness.ps1")
. (Join-Path $commonDir "DeskmateLoader.ps1")

# Settings.Dir is static readonly: DESKMATE_DIR must be set before it is first touched.
$f32Tmp = Join-Path ([System.IO.Path]::GetTempPath()) ("deskmate-f32-" + [Guid]::NewGuid().ToString("N"))
[void][System.IO.Directory]::CreateDirectory($f32Tmp)
$env:DESKMATE_DIR = $f32Tmp

Describe "Tier 1 Feature Suite: Feature 32 - Google Workspace Sync (offline)" {

    $flags = [System.Reflection.BindingFlags]'Public,NonPublic,Static,Instance'
    $settingsType = Get-DeskmateType "Settings"
    $todoItemType = Get-DeskmateType "TodoItem"
    $todoFileType = Get-DeskmateType "TodoFile"
    $noteFilesType = Get-DeskmateType "NoteFiles"
    $noteMetaType = Get-DeskmateType "NoteMeta"
    $islandType = Get-DeskmateType "Island"
    $langType = Get-DeskmateType "Lang"
    $srcType = Get-DeskmateType "GoogleTasksSource"

    function Assert-Isolated {
        $dir = [string]$settingsType.GetField("Dir", $flags).GetValue($null)
        if (-not $dir.StartsWith($f32Tmp, [StringComparison]::OrdinalIgnoreCase)) {
            throw "Settings.Dir ($dir) is not the isolated temp dir; refusing to touch real user data (Settings was initialised before DESKMATE_DIR was set)."
        }
        return $dir
    }

    function Reset-Dir($dir) {
        Get-ChildItem -LiteralPath $dir -Force -ErrorAction SilentlyContinue | Remove-Item -Recurse -Force -ErrorAction SilentlyContinue
    }

    It "1. TodoFile Save/Load round-trips Google fields with tab/newline escaping" {
        $dir = Assert-Isolated
        Reset-Dir $dir
        $listType = [System.Collections.Generic.List``1].MakeGenericType($todoItemType)
        $list = [Activator]::CreateInstance($listType)
        $t = [Activator]::CreateInstance($todoItemType, $true)
        $t.Id = "abc12345"
        $t.Title = "Judul"
        $t.Done = $true
        $t.Star = $true
        $t.ListName = "Daftar`tKerja"
        $t.Notes = "baris1`nbaris2`tTab\akhir`r`nCRLF"
        $t.GoogleTaskId = "gt-001"
        $t.GoogleListId = "gl-777"
        $t.UpdatedUtc = [DateTime]::SpecifyKind((New-Object DateTime 2025, 3, 4, 5, 6, 7), [DateTimeKind]::Utc)
        $t.Dirty = $false
        $list.Add($t)
        $ok = $todoFileType.GetMethod("Save", $flags).Invoke($null, @(, $list))
        Assert-True $ok "Save returned false"
        $back = $todoFileType.GetMethod("Load", $flags).Invoke($null, $null)
        Assert-Equal 1 $back.Count "Loaded count"
        $b = $back[0]
        Assert-Equal "abc12345" $b.Id
        Assert-Equal "Judul" $b.Title
        Assert-True $b.Done "Done"
        Assert-True $b.Star "Star"
        Assert-Equal "Daftar`tKerja" $b.ListName
        Assert-Equal $t.Notes $b.Notes "Notes escaping"
        Assert-Equal "gt-001" $b.GoogleTaskId
        Assert-Equal "gl-777" $b.GoogleListId
        Assert-Equal $t.UpdatedUtc.Ticks $b.UpdatedUtc.ToUniversalTime().Ticks "UpdatedUtc"
        Assert-False $b.Dirty "Dirty must round-trip false"
        # file stays one line per task despite embedded newlines
        $path = Join-Path $dir "tugas.tsv"
        Assert-Equal 1 @([System.IO.File]::ReadAllLines($path)).Count "one physical line per task"
    }

    It "2. NoteFiles SaveMeta/LoadMeta round-trips Color, Labels, Archived and DriveId" {
        $dir = Assert-Isolated
        Reset-Dir $dir
        $dictType = [System.Collections.Generic.Dictionary``2].MakeGenericType([string], $noteMetaType)
        $d = [Activator]::CreateInstance($dictType)
        $m = [Activator]::CreateInstance($noteMetaType, $true)
        $m.Key = "ide.md"
        $m.Color = "#ff8800"
        $m.Labels = "kerja,penting"
        $m.Archived = $true
        $m.DriveId = "drive-xyz"
        $d.Add("ide.md", $m)
        [void]$noteFilesType.GetMethod("SaveMeta", $flags).Invoke($null, @(, $d))
        $back = $noteFilesType.GetMethod("LoadMeta", $flags).Invoke($null, $null)
        Assert-Equal 1 $back.Count "meta count"
        $r = $back["ide.md"]
        Assert-NotNull $r "meta entry"
        Assert-Equal "#ff8800" $r.Color
        Assert-Equal "kerja,penting" $r.Labels
        Assert-True $r.Archived "Archived"
        Assert-Equal "drive-xyz" $r.DriveId
    }

    It "3. Pending-delete queues dedupe (NoteFiles drive ids, TodoFile google tasks)" {
        $dir = Assert-Isolated
        Reset-Dir $dir
        $addN = $noteFilesType.GetMethod("AddDeletedDrive", $flags)
        [void]$addN.Invoke($null, @("fid-1"))
        [void]$addN.Invoke($null, @("fid-1"))
        [void]$addN.Invoke($null, @("fid-2"))
        [void]$addN.Invoke($null, @(""))
        $n = $noteFilesType.GetMethod("LoadDeletedDrive", $flags).Invoke($null, $null)
        Assert-Equal 2 $n.Count "drive deletes deduped, empty ignored"
        Assert-True ($n.Contains("fid-1") -and $n.Contains("fid-2")) "both ids present"

        $addT = $todoFileType.GetMethod("AddDeletedGoogle", $flags)
        [void]$addT.Invoke($null, @("listA", "task-1"))
        [void]$addT.Invoke($null, @("listA", "task-1"))
        [void]$addT.Invoke($null, @("listB", "task-2"))
        [void]$addT.Invoke($null, @("listB", ""))
        $t = $todoFileType.GetMethod("LoadDeletedGoogle", $flags).Invoke($null, $null)
        Assert-Equal 2 $t.Count "google task deletes deduped, empty ignored"
        Assert-Equal "listA" $t[0][0]
        Assert-Equal "task-1" $t[0][1]
        Assert-Equal "task-2" $t[1][1]
    }

    It "4. Settings export whitelist (Portable) includes google.tasks and google.drive.notes" {
        $portable = $islandType.GetField("Portable", $flags).GetValue($null)
        Assert-True $portable.Contains("google.tasks") "google.tasks portable"
        Assert-True $portable.Contains("google.drive.notes") "google.drive.notes portable"
    }

    It "5. GoogleTasksSource.Fetch returns empty Tasks when disabled snapshot (no network)" {
        $src = [Activator]::CreateInstance($srcType, $true)
        $snapOn = $srcType.GetMethod("Snapshot").Invoke($src, $null)
        Assert-True ([bool]$snapOn) "default Enabled snapshot is true"
        $srcType.GetField("Enabled").SetValue($src, $false)
        $snap = $srcType.GetMethod("Snapshot").Invoke($src, $null)
        Assert-False ([bool]$snap) "snapshot reflects Enabled=false"
        $res = $srcType.GetMethod("Fetch").Invoke($src, @($snap, [DateTime]::UtcNow))
        $tasks = $res.GetType().GetField("Tasks").GetValue($res)
        Assert-NotNull $tasks "Tasks list not null"
        Assert-Equal 0 $tasks.Count "no tasks when disabled"
    }

    It "6. Lang.T English differs from the Indonesian id for the Google Tasks sync label" {
        $eng = $langType.GetField("English", [System.Reflection.BindingFlags]'Public,Static')
        $old = $eng.GetValue($null)
        try {
            $eng.SetValue($null, $true)
            $en = $langType.GetMethod("T", [System.Reflection.BindingFlags]'Public,Static').Invoke($null, @("Sinkronkan Google Tasks"))
            Assert-NotEqual "Sinkronkan Google Tasks" $en "English must be translated"
            Assert-Equal "Sync Google Tasks" $en
        }
        finally { $eng.SetValue($null, $old) }
    }

    It "7. Lang.T English translates the Google sync failure strings" {
        $eng = $langType.GetField("English", [System.Reflection.BindingFlags]'Public,Static')
        $old = $eng.GetValue($null)
        $tm = $langType.GetMethod("T", [System.Reflection.BindingFlags]'Public,Static')
        try {
            $eng.SetValue($null, $true)
            Assert-Equal "Failed to save to Google Tasks" ($tm.Invoke($null, @("Gagal menyimpan ke Google Tasks")))
            Assert-Equal "Failed to save note to Google Drive" ($tm.Invoke($null, @("Gagal menyimpan catatan ke Google Drive")))
            Assert-Equal "Failed to save note" ($tm.Invoke($null, @("Gagal menyimpan catatan")))
        }
        finally { $eng.SetValue($null, $old) }
    }

    Remove-Item -LiteralPath $f32Tmp -Recurse -Force -ErrorAction SilentlyContinue
}
