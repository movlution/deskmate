# test_f33_tasks_google_parity.ps1
# Tier 1 tests for Feature 33: Google Tasks Parity & Aesthetic Redesign.
# Exercises:
# (a) TodoFile 16-col Load/Save round-trip with ParentId and Position
# (b) TaskLists registry save/load
# (c) TaskTree sorting and hierarchy
# (d) Verify WorkWindow does NOT contain any button with text "Sinkronkan"
# (e) Smoke-test instantiating and rendering WorkWindow in Light and Dark mode without throwing or blank output

$commonDir = Join-Path (Split-Path -Parent $PSScriptRoot) "common"
. (Join-Path $commonDir "TestHarness.ps1")
. (Join-Path $commonDir "DeskmateLoader.ps1")

# Settings.Dir is static readonly: DESKMATE_DIR must be set before it is first touched.
$f33Tmp = Join-Path ([System.IO.Path]::GetTempPath()) ("deskmate-f33-" + [Guid]::NewGuid().ToString("N"))
[void][System.IO.Directory]::CreateDirectory($f33Tmp)
$env:DESKMATE_DIR = $f33Tmp

Describe "Tier 1 Feature Suite: Feature 33 - Google Tasks Parity & Aesthetic Redesign" {

    $flags = [System.Reflection.BindingFlags]'Public,NonPublic,Static,Instance'
    $settingsType = Get-DeskmateType "Settings"
    $todoItemType = Get-DeskmateType "TodoItem"
    $todoFileType = Get-DeskmateType "TodoFile"
    $taskListInfoType = Get-DeskmateType "TaskListInfo"
    $taskListsType = Get-DeskmateType "TaskLists"
    $taskTreeType = Get-DeskmateType "TaskTree"
    $taskRowType = Get-DeskmateType "TaskRow"
    $islandType = Get-DeskmateType "Island"
    $wwType = Get-DeskmateType "WorkWindow"

    function Assert-Isolated {
        $dir = [string]$settingsType.GetField("Dir", $flags).GetValue($null)
        if (-not $dir.StartsWith($f33Tmp, [StringComparison]::OrdinalIgnoreCase)) {
            throw "Settings.Dir ($dir) is not the isolated temp dir; refusing to touch real user data."
        }
        return $dir
    }

    function Reset-Dir($dir) {
        Get-ChildItem -LiteralPath $dir -Force -ErrorAction SilentlyContinue | Remove-Item -Recurse -Force -ErrorAction SilentlyContinue
    }

    function New-TestIsland($todosList, $taskListsList) {
        $isl = [System.Runtime.Serialization.FormatterServices]::GetUninitializedObject($islandType)
        $islandType.GetField("todos", $flags).SetValue($isl, $todosList)
        $islandType.GetField("taskLists", $flags).SetValue($isl, $taskListsList)
        return $isl
    }

    It "1. TodoFile 16-col Load/Save round-trips ParentId, Position, and Moved" {
        $dir = Assert-Isolated
        Reset-Dir $dir

        $listType = [System.Collections.Generic.List``1].MakeGenericType($todoItemType)
        $list = [Activator]::CreateInstance($listType)

        $p = [Activator]::CreateInstance($todoItemType, $true)
        $p.Id = "root001"
        $p.Title = "Parent task"
        $p.Done = $false
        $p.Star = $true
        $p.ListName = "Kerja"
        $p.Notes = "Detail parent"
        $p.ParentId = ""
        $p.Position = 0
        $p.Moved = $false
        $p.Dirty = $false
        $list.Add($p)

        $c = [Activator]::CreateInstance($todoItemType, $true)
        $c.Id = "sub001"
        $c.Title = "Child subtask"
        $c.Done = $false
        $c.Star = $false
        $c.ListName = "Kerja"
        $c.Notes = "Detail subtask"
        $c.ParentId = "root001"
        $c.Position = 1
        $c.Moved = $true
        $c.Dirty = $true
        $list.Add($c)

        $ok = $todoFileType.GetMethod("Save", $flags).Invoke($null, @(, $list))
        Assert-True $ok "TodoFile.Save must return true"

        # Verify physical file has 16 columns
        $tsvPath = Join-Path $dir "tugas.tsv"
        Assert-True (Test-Path $tsvPath) "tugas.tsv must exist"
        $lines = [System.IO.File]::ReadAllLines($tsvPath)
        Assert-Equal 2 $lines.Length "tugas.tsv must contain 2 lines"
        $cols = $lines[1].Split("`t")
        Assert-Equal 16 $cols.Length "tugas.tsv line must contain exactly 16 tab-separated columns"
        Assert-Equal "root001" $cols[13] "Col 14 is ParentId"
        Assert-Equal "1" $cols[14] "Col 15 is Position"
        Assert-Equal "1" $cols[15] "Col 16 is Moved"

        # Round-trip Load
        $loaded = $todoFileType.GetMethod("Load", $flags).Invoke($null, $null)
        Assert-Equal 2 $loaded.Count "Loaded task count"

        $lp = $loaded[0]
        Assert-Equal "root001" $lp.Id
        Assert-Equal "" $lp.ParentId
        Assert-Equal 0 $lp.Position
        Assert-False $lp.Moved "Parent moved"

        $lc = $loaded[1]
        Assert-Equal "sub001" $lc.Id
        Assert-Equal "root001" $lc.ParentId
        Assert-Equal 1 $lc.Position
        Assert-True $lc.Moved "Child moved"
    }

    It "2. TaskLists registry save, load, and ensure default list" {
        $dir = Assert-Isolated
        Reset-Dir $dir

        $listType = [System.Collections.Generic.List``1].MakeGenericType($taskListInfoType)
        $lists = [Activator]::CreateInstance($listType)

        $l1 = [Activator]::CreateInstance($taskListInfoType)
        $l1.Id = "default"
        $l1.Name = "Tugas Saya"
        $lists.Add($l1)

        $l2 = [Activator]::CreateInstance($taskListInfoType)
        $l2.Id = "lst2"
        $l2.Name = "Proyek Riset"
        $l2.GoogleId = "google-list-99"
        $l2.Dirty = $true
        $lists.Add($l2)

        $saveMethod = $taskListsType.GetMethod("Save", $flags)
        $ok = $saveMethod.Invoke($null, @(, $lists))
        Assert-True $ok "TaskLists.Save must succeed"

        $loadMethod = $taskListsType.GetMethod("Load", $flags)
        $back = $loadMethod.Invoke($null, $null)
        Assert-Equal 2 $back.Count "Loaded task lists count"
        Assert-Equal "Tugas Saya" $back[0].Name "Default list must be first"
        Assert-Equal "Proyek Riset" $back[1].Name
        Assert-Equal "google-list-99" $back[1].GoogleId
        Assert-True $back[1].Dirty "Dirty flag must round-trip"
    }

    It "3. TaskTree sorting and hierarchy creates correct depth and parent-child tree" {
        $listType = [System.Collections.Generic.List``1].MakeGenericType($todoItemType)
        $all = [Activator]::CreateInstance($listType)

        $t1 = [Activator]::CreateInstance($todoItemType, $true)
        $t1.Id = "t1"
        $t1.Title = "Top Task 1"
        $t1.Position = 0
        $t1.ParentId = ""
        $all.Add($t1)

        $sub1 = [Activator]::CreateInstance($todoItemType, $true)
        $sub1.Id = "s1"
        $sub1.Title = "Subtask 1A"
        $sub1.Position = 0
        $sub1.ParentId = "t1"
        $all.Add($sub1)

        $sub2 = [Activator]::CreateInstance($todoItemType, $true)
        $sub2.Id = "s2"
        $sub2.Title = "Subtask 1B"
        $sub2.Position = 1
        $sub2.ParentId = "t1"
        $all.Add($sub2)

        $t2 = [Activator]::CreateInstance($todoItemType, $true)
        $t2.Id = "t2"
        $t2.Title = "Top Task 2"
        $t2.Position = 1
        $t2.ParentId = ""
        $all.Add($t2)

        # Call TaskTree.Open(all, list=null, starred=false, sort="manual", listOrder=null, collapsed=null)
        $openMethod = $taskTreeType.GetMethod("Open", $flags)
        $rows = $openMethod.Invoke($null, @($all, $null, $false, "manual", $null, $null))

        Assert-Equal 4 $rows.Count "Open rows count"
        Assert-Equal "t1" $rows[0].Item.Id "Row 0 is t1"
        Assert-Equal 0 $rows[0].Depth "t1 depth 0"
        Assert-True $rows[0].HasChildren "t1 has children"
        Assert-Equal 2 $rows[0].ChildCount "t1 child count 2"

        Assert-Equal "s1" $rows[1].Item.Id "Row 1 is s1"
        Assert-Equal 1 $rows[1].Depth "s1 depth 1"
        Assert-False $rows[1].HasChildren "s1 has no children"

        Assert-Equal "s2" $rows[2].Item.Id "Row 2 is s2"
        Assert-Equal 1 $rows[2].Depth "s2 depth 1"

        Assert-Equal "t2" $rows[3].Item.Id "Row 3 is t2"
        Assert-Equal 0 $rows[3].Depth "t2 depth 0"

        # Test Reorder: move s2 up
        $reorderMethod = $taskTreeType.GetMethod("Reorder", $flags)
        $reordered = $reorderMethod.Invoke($null, @($all, $sub2, $true))
        Assert-True $reordered "Reorder must return true"
        Assert-Equal 0 $sub2.Position "s2 position is now 0"
        Assert-Equal 1 $sub1.Position "s1 position is now 1"
    }

    It "4. WorkWindow does NOT contain any button with text 'Sinkronkan'" {
        $todosList = [Activator]::CreateInstance([System.Collections.Generic.List``1].MakeGenericType($todoItemType))
        $taskListsList = [Activator]::CreateInstance([System.Collections.Generic.List``1].MakeGenericType($taskListInfoType))
        $island = New-TestIsland $todosList $taskListsList

        $ww = [Activator]::CreateInstance($wwType, @($island))
        Assert-NotNull $ww "WorkWindow instance created"

        # Check all controls recursively
        function Find-SyncButton($control) {
            if ($control.Text -and $control.Text.Trim() -eq "Sinkronkan") {
                return $control
            }
            foreach ($child in $control.Controls) {
                $found = Find-SyncButton $child
                if ($found) { return $found }
            }
            return $null
        }

        $syncBtn = Find-SyncButton $ww
        Assert-Null $syncBtn "WorkWindow must not contain any button with text 'Sinkronkan'"

        # Verify WorkWindow contains TaskRail, TaskTitle, TaskAddBox, TaskView, TaskSyncStatus
        $railType = Get-DeskmateType "TaskRail"
        $titleType = Get-DeskmateType "TaskTitle"
        $addBoxType = Get-DeskmateType "TaskAddBox"
        $taskViewType = Get-DeskmateType "TaskView"
        $syncStatusType = Get-DeskmateType "TaskSyncStatus"

        function Has-ControlType($control, $targetType) {
            if ($targetType.IsInstanceOfType($control)) { return $true }
            foreach ($child in $control.Controls) {
                if (Has-ControlType $child $targetType) { return $true }
            }
            return $false
        }

        Assert-True (Has-ControlType $ww $railType) "WorkWindow must contain TaskRail"
        Assert-True (Has-ControlType $ww $titleType) "WorkWindow must contain TaskTitle"
        Assert-True (Has-ControlType $ww $addBoxType) "WorkWindow must contain TaskAddBox"
        Assert-True (Has-ControlType $ww $taskViewType) "WorkWindow must contain TaskView"
        Assert-True (Has-ControlType $ww $syncStatusType) "WorkWindow must contain TaskSyncStatus"

        $ww.Dispose()
    }

    It "5. Smoke-test instantiating and rendering WorkWindow in Light and Dark mode" {
        $todosList = [Activator]::CreateInstance([System.Collections.Generic.List``1].MakeGenericType($todoItemType))
        $taskListsList = [Activator]::CreateInstance([System.Collections.Generic.List``1].MakeGenericType($taskListInfoType))

        # Add sample tasks
        $sample1 = [Activator]::CreateInstance($todoItemType, $true)
        $sample1.Id = "smp1"
        $sample1.Title = "Sample Task 1"
        $sample1.ListName = "Tugas Saya"
        $sample1.Position = 0
        $todosList.Add($sample1)

        $sample2 = [Activator]::CreateInstance($todoItemType, $true)
        $sample2.Id = "smp2"
        $sample2.Title = "Sample Completed Task"
        $sample2.ListName = "Tugas Saya"
        $sample2.Done = $true
        $todosList.Add($sample2)

        $defList = [Activator]::CreateInstance($taskListInfoType)
        $defList.Id = "default"
        $defList.Name = "Tugas Saya"
        $taskListsList.Add($defList)

        # Test Dark Mode rendering
        Reset-DeskmateTheme -Light $false
        $islandDark = New-TestIsland $todosList $taskListsList
        $wwDark = [Activator]::CreateInstance($wwType, @($islandDark))
        $bmpDark = New-Object System.Drawing.Bitmap 820, 560
        $wwDark.DrawToBitmap($bmpDark, (New-Object System.Drawing.Rectangle 0, 0, 820, 560))

        # Assert Dark mode renders with dark window surface
        $darkPixel = $bmpDark.GetPixel(50, 50)
        Assert-True ($darkPixel.R -lt 50 -and $darkPixel.G -lt 50 -and $darkPixel.B -lt 50) "Dark mode background must be dark"
        $bmpDark.Dispose()
        $wwDark.Dispose()

        # Test Light Mode rendering
        Reset-DeskmateTheme -Light $true
        $islandLight = New-TestIsland $todosList $taskListsList
        $wwLight = [Activator]::CreateInstance($wwType, @($islandLight))
        $bmpLight = New-Object System.Drawing.Bitmap 820, 560
        $wwLight.DrawToBitmap($bmpLight, (New-Object System.Drawing.Rectangle 0, 0, 820, 560))

        # Assert Light mode renders with light window surface
        $lightPixel = $bmpLight.GetPixel(50, 50)
        Assert-True ($lightPixel.R -gt 200 -and $lightPixel.G -gt 200 -and $lightPixel.B -gt 200) "Light mode background must be bright"
        $bmpLight.Dispose()
        $wwLight.Dispose()

        # Restore dark theme as default
        Reset-DeskmateTheme -Light $false
    }

    It "6. WorkWindow handles null owner.todos and owner.taskLists on handle creation without throwing" {
        $nullIsland = New-TestIsland $null $null
        $wwNull = [Activator]::CreateInstance($wwType, @($nullIsland))
        Assert-NotNull $wwNull "WorkWindow instance created with null todos/taskLists"

        $h = $wwNull.Handle
        Assert-True ($h -ne [IntPtr]::Zero) "WorkWindow handle must be created cleanly"

        $bmp = New-Object System.Drawing.Bitmap 820, 560
        $wwNull.DrawToBitmap($bmp, (New-Object System.Drawing.Rectangle 0, 0, 820, 560))
        $bmp.Dispose()
        $wwNull.Dispose()
    }

    It "7. WorkWindow and TaskTree handle null elements inside todos list without throwing" {
        $listType = [System.Collections.Generic.List``1].MakeGenericType($todoItemType)
        $todosWithNull = [Activator]::CreateInstance($listType)
        $todosWithNull.Add($null)

        $validTask = [Activator]::CreateInstance($todoItemType, $true)
        $validTask.Id = "valid01"
        $validTask.Title = "Valid Task"
        $validTask.ListName = "Tugas Saya"
        $validTask.Done = $false
        $todosWithNull.Add($validTask)

        $openRows = $taskTreeType.GetMethod("Open", $flags).Invoke($null, @($todosWithNull, $null, $false, "manual", $null, $null))
        Assert-Equal 1 $openRows.Count "TaskTree.Open must skip null elements and return 1 row"
        Assert-Equal "valid01" $openRows[0].Item.Id "TaskTree.Open row must be validTask"

        $doneItems = $taskTreeType.GetMethod("Done", $flags).Invoke($null, @($todosWithNull, $null, $false))
        Assert-Equal 0 $doneItems.Count "TaskTree.Done must skip null elements and return 0 items"

        $openCount = $taskTreeType.GetMethod("OpenCount", $flags).Invoke($null, @($todosWithNull, $null, $false))
        Assert-Equal 1 $openCount "TaskTree.OpenCount must skip null elements and return 1"

        $taskListsList = [Activator]::CreateInstance([System.Collections.Generic.List``1].MakeGenericType($taskListInfoType))
        $island = New-TestIsland $todosWithNull $taskListsList
        $ww = [Activator]::CreateInstance($wwType, @($island))
        Assert-NotNull $ww "WorkWindow instance created with null element in todos"

        $h = $ww.Handle
        Assert-True ($h -ne [IntPtr]::Zero) "WorkWindow handle must be created cleanly"

        $bmp = New-Object System.Drawing.Bitmap 820, 560
        $ww.DrawToBitmap($bmp, (New-Object System.Drawing.Rectangle 0, 0, 820, 560))
        $bmp.Dispose()
        $ww.Dispose()
    }
}
