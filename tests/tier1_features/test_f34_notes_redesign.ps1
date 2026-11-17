# test_f34_notes_redesign.ps1
# Tier 1 tests for Feature 34: Catatan Tab Modern Redesign & Markdown Title/Body Parsing.
# Exercises:
# (a) Markdown SplitNote / JoinNote round-trip (titled #, legacy unprefixed, empty, checklists)
# (b) NoteFiles metadata (Color, Labels, Archived), pins, and QuickPath (catatan.md) preservation
# (c) WorkWindow Catatan tab controls structure (no unlabeled Label... box or bottom action buttons)
# (d) WorkWindow Tab 1 interactive loading & saving (normal .md title+body & QuickPath ReadOnly)
# (e) Smoke-test rendering WorkWindow Tab 1 (Catatan) in Dark and Light mode with sample notes

$commonDir = Join-Path (Split-Path -Parent $PSScriptRoot) "common"
. (Join-Path $commonDir "TestHarness.ps1")
. (Join-Path $commonDir "DeskmateLoader.ps1")

$f34Tmp = Join-Path ([System.IO.Path]::GetTempPath()) ("deskmate-f34-" + [Guid]::NewGuid().ToString("N"))
[void][System.IO.Directory]::CreateDirectory($f34Tmp)
$env:DESKMATE_DIR = $f34Tmp

Describe "Tier 1 Feature Suite: Feature 34 - Catatan Tab Redesign" {

    $flags = [System.Reflection.BindingFlags]'Public,NonPublic,Static,Instance'
    $settingsType = Get-DeskmateType "Settings"
    $noteFilesType = Get-DeskmateType "NoteFiles"
    $noteMetaType = Get-DeskmateType "NoteMeta"
    $todoItemType = Get-DeskmateType "TodoItem"
    $taskListInfoType = Get-DeskmateType "TaskListInfo"
    $islandType = Get-DeskmateType "Island"
    $wwType = Get-DeskmateType "WorkWindow"

    function Assert-Isolated {
        $dir = [string]$settingsType.GetField("Dir", $flags).GetValue($null)
        if (-not $dir.StartsWith($f34Tmp, [StringComparison]::OrdinalIgnoreCase)) {
            throw "Settings.Dir ($dir) is not the isolated temp dir; refusing to touch real user data."
        }
        return $dir
    }

    function Reset-Dir($dir) {
        Get-ChildItem -LiteralPath $dir -Force -ErrorAction SilentlyContinue | Remove-Item -Recurse -Force -ErrorAction SilentlyContinue
    }

    function New-TestIsland {
        $todosList = [Activator]::CreateInstance([System.Collections.Generic.List``1].MakeGenericType($todoItemType))
        $taskListsList = [Activator]::CreateInstance([System.Collections.Generic.List``1].MakeGenericType($taskListInfoType))
        $isl = [System.Runtime.Serialization.FormatterServices]::GetUninitializedObject($islandType)
        $islandType.GetField("todos", $flags).SetValue($isl, $todosList)
        $islandType.GetField("taskLists", $flags).SetValue($isl, $taskListsList)
        return $isl
    }

    It "1. NoteFiles SplitNote and JoinNote round-trip titled, legacy, checklist, and empty notes" {
        $split = $noteFilesType.GetMethod("SplitNote", $flags)
        $join = $noteFilesType.GetMethod("JoinNote", $flags)
        Assert-NotNull $split "NoteFiles.SplitNote must exist"
        Assert-NotNull $join "NoteFiles.JoinNote must exist"

        # (a) Titled note with # prefix and checklist body
        $joined = [string]$join.Invoke($null, @("Rencana Rilis", "- [ ] Build`r`n- [x] Uji"))
        Assert-Equal "# Rencana Rilis`r`n`r`n- [ ] Build`r`n- [x] Uji" $joined "JoinNote formats # heading + blank line + body"

        $args1 = [object[]]@($joined, $null, $null)
        [void]$split.Invoke($null, $args1)
        Assert-Equal "Rencana Rilis" ([string]$args1[1]) "SplitNote extracts heading title"
        Assert-Equal "- [ ] Build`r`n- [x] Uji" ([string]$args1[2]) "SplitNote extracts body after blank line"

        # (b) Titled note with empty body
        $joinedTitleOnly = [string]$join.Invoke($null, @("Judul Saja", ""))
        Assert-Equal "# Judul Saja`r`n" $joinedTitleOnly "JoinNote with empty body ends cleanly"
        $args2 = [object[]]@($joinedTitleOnly, $null, $null)
        [void]$split.Invoke($null, $args2)
        Assert-Equal "Judul Saja" ([string]$args2[1]) "SplitNote extracts title with empty body"
        Assert-Equal "" ([string]$args2[2]) "SplitNote leaves body empty"

        # (c) Legacy note without # prefix
        $legacy = "Catatan lama baris 1`r`n`r`nParagraf isi"
        $args3 = [object[]]@($legacy, $null, $null)
        [void]$split.Invoke($null, $args3)
        Assert-Equal "Catatan lama baris 1" ([string]$args3[1]) "Legacy line 1 becomes title"
        Assert-Equal "Paragraf isi" ([string]$args3[2]) "Legacy skips 1 blank line for body"

        # (d) Empty file
        $args4 = [object[]]@("", $null, $null)
        [void]$split.Invoke($null, $args4)
        Assert-Equal "" ([string]$args4[1]) "Empty file yields empty title"
        Assert-Equal "" ([string]$args4[2]) "Empty file yields empty body"

        # (e) Body-only (empty title) preserves verbatim
        $bodyOnly = [string]$join.Invoke($null, @("   ", "Hanya isi catatan"))
        Assert-Equal "Hanya isi catatan" $bodyOnly "Empty title serializes body verbatim"
    }

    It "2. NoteFiles List, Pins, Meta, and QuickPath (catatan.md) work cleanly together" {
        $dir = Assert-Isolated
        Reset-Dir $dir

        $notesDir = $noteFilesType.GetProperty("Dir", $flags).GetValue($null, $null)
        $quickPath = $noteFilesType.GetProperty("QuickPath", $flags).GetValue($null, $null)
        $writeMethod = $noteFilesType.GetMethod("Write", $flags)
        $p1 = [System.IO.Path]::Combine($notesDir, "20261114-01.md")
        $p2 = [System.IO.Path]::Combine($notesDir, "20261114-02.md")

        [void]$writeMethod.Invoke($null, @($p1, "# Ide Desain`r`n`r`n- [ ] Sketsa`r`n- [x] Warna"))
        [void]$writeMethod.Invoke($null, @($p2, "# Arsip Lama`r`n`r`nSudah selesai"))
        [void]$writeMethod.Invoke($null, @($quickPath, "- 09:00 Beli kopi"))

        $pins = $noteFilesType.GetMethod("LoadPins", $flags).Invoke($null, $null)
        $setPin = $noteFilesType.GetMethod("SetPin", $flags)
        [void]$setPin.Invoke($null, @($pins, $p1, $true))

        $dictType = [System.Collections.Generic.Dictionary``2].MakeGenericType([string], $noteMetaType)
        $meta = [Activator]::CreateInstance($dictType)
        $m1 = [Activator]::CreateInstance($noteMetaType, $true)
        $m1.Key = [System.IO.Path]::GetFileName($p1)
        $m1.Color = "amber"
        $m1.Labels = "kerja, ide"
        $meta[$m1.Key] = $m1

        $m2 = [Activator]::CreateInstance($noteMetaType, $true)
        $m2.Key = [System.IO.Path]::GetFileName($p2)
        $m2.Archived = $true
        $meta[$m2.Key] = $m2

        [void]$noteFilesType.GetMethod("SaveMeta", $flags).Invoke($null, @(, $meta))

        $listMethod = $noteFilesType.GetMethods($flags) | Where-Object { $_.Name -eq "List" -and $_.GetParameters().Length -eq 5 } | Select-Object -First 1
        $activeList = $listMethod.Invoke($null, @("", $pins, $meta, $false, ""))
        Assert-Equal 2 $activeList.Count "Active notes include p1 and quickPath"
        Assert-Equal $p1 $activeList[0].Path "Pinned note sorts first"
        Assert-Equal "Ide Desain" $activeList[0].Title "Active note title parsed"
        Assert-Equal 1 $activeList[0].CheckDone "Checklist done count"
        Assert-Equal 2 $activeList[0].CheckTotal "Checklist total count"
        Assert-Equal "amber" $activeList[0].Color "Color metadata preserved"
        Assert-Equal "kerja, ide" $activeList[0].Labels "Labels metadata preserved"

        $quickItem = $activeList | Where-Object { $_.Quick } | Select-Object -First 1
        Assert-NotNull $quickItem "QuickPath note listed"
        Assert-Equal "Catatan kilat" $quickItem.Title "QuickPath keeps Catatan kilat title"

        $archivedList = $listMethod.Invoke($null, @("", $pins, $meta, $true, ""))
        Assert-Equal 1 $archivedList.Count "Archive filter shows archived note"
        Assert-Equal $p2 $archivedList[0].Path "Archived note matches p2"
    }

    It "3. WorkWindow uses modern note controls without old Label... box or bottom action buttons" {
        $dir = Assert-Isolated
        Reset-Dir $dir
        $isl = New-TestIsland
        $ww = [Activator]::CreateInstance($wwType, @($isl))

        $noteListType = Get-DeskmateType "NoteListView"
        $colorStripType = Get-DeskmateType "NoteColorStrip"
        $tagChipType = Get-DeskmateType "NoteTagChip"
        $iconBtnType = Get-DeskmateType "TaskIconButton"

        function Find-ByType($ctrl, $targetType) {
            $res = @()
            if ($targetType.IsInstanceOfType($ctrl)) { $res += $ctrl }
            foreach ($c in $ctrl.Controls) { $res += Find-ByType $c $targetType }
            return $res
        }

        Assert-True ((Find-ByType $ww $noteListType).Count -eq 1) "WorkWindow must contain NoteListView"
        Assert-True ((Find-ByType $ww $colorStripType).Count -eq 1) "WorkWindow must contain NoteColorStrip"
        Assert-True ((Find-ByType $ww $tagChipType).Count -eq 1) "WorkWindow must contain NoteTagChip"
        Assert-True ((Find-ByType $ww $iconBtnType).Count -ge 6) "WorkWindow must use TaskIconButton toolbar actions"

        $labelField = $wwType.GetField("labelBox", $flags)
        Assert-Null $labelField "WorkWindow must not retain old unlabeled labelBox field"

        $titleBox = $wwType.GetField("noteTitleBox", $flags).GetValue($ww)
        $editorBox = $wwType.GetField("editor", $flags).GetValue($ww)
        Assert-NotNull $titleBox "WorkWindow must have noteTitleBox"
        Assert-NotNull $editorBox "WorkWindow must have editor"
        Assert-True ($editorBox -is [System.Windows.Forms.RichTextBox]) "Editor must display Markdown formatting inline"
        $hTabs = $ww.Handle
        $tabsCtrl = $wwType.GetField("tabs", $flags).GetValue($ww)
        Assert-Equal 2 $tabsCtrl.Items.Length "WorkWindow tabs must have Tugas and Catatan"
        Assert-True ($tabsCtrl.Width -gt 80) "WorkWindow tabs bar must have positive laid-out width"

        $ww.Dispose()
    }

    It "4. WorkWindow Tab 1 loads & saves normal markdown title/body and keeps QuickPath ReadOnly" {
        $dir = Assert-Isolated
        Reset-Dir $dir
        $notesDir = $noteFilesType.GetProperty("Dir", $flags).GetValue($null, $null)
        $quickPath = $noteFilesType.GetProperty("QuickPath", $flags).GetValue($null, $null)
        $writeMethod = $noteFilesType.GetMethod("Write", $flags)
        $mdPath = [System.IO.Path]::Combine($notesDir, "20261114-edit.md")
        [void]$writeMethod.Invoke($null, @($mdPath, "# Judul Awal`r`n`r`nIsi awal"))
        [void]$writeMethod.Invoke($null, @($quickPath, "- Catatan cepat"))

        $isl = New-TestIsland
        $ww = [Activator]::CreateInstance($wwType, @($isl))
        $h = $ww.Handle
        $ww.SelectTab(1)

        $showNote = $wwType.GetMethod("ShowNote", $flags)
        $saveCurrent = $wwType.GetMethod("SaveCurrent", $flags, $null, @([bool]), $null)
        $titleBox = $wwType.GetField("noteTitleBox", $flags).GetValue($ww)
        $editorBox = $wwType.GetField("editor", $flags).GetValue($ww)

        [void]$showNote.Invoke($ww, @($mdPath))
        Assert-False $titleBox.ReadOnly "Normal note title must be editable"
        Assert-Equal "Judul Awal" $titleBox.Text "Loaded title into noteTitleBox"
        Assert-Equal "Isi awal" $editorBox.Text "Loaded body into editor"

        $titleBox.Text = "Judul Baru"
        $editorBox.Text = "Isi yang diperbarui"
        $saved = $saveCurrent.Invoke($ww, @($false))
        Assert-True $saved "SaveCurrent must succeed"

        $disk = [System.IO.File]::ReadAllText($mdPath)
        Assert-Equal "# Judul Baru`r`n`r`nIsi yang diperbarui" $disk "Saved markdown must serialize # title + blank line + body"

        $continueList = $wwType.GetMethod("ContinueMarkdownList", $flags)
        foreach ($sample in @(
            @("  9. Riset", "  9. Riset`r`n  10. "),
            @("* Riset", "* Riset`r`n* "),
            @("- [x] Riset", "- [x] Riset`r`n- [ ] "),
            @("1. Riset`r`n2. ", "1. Riset`r`n")
        )) {
            $editorBox.Text = $sample[0]
            $editorBox.SelectionStart = $editorBox.Text.Length
            $key = New-Object System.Windows.Forms.KeyEventArgs ([System.Windows.Forms.Keys]::Enter)
            [void]$continueList.Invoke($ww, @($editorBox, $key.PSObject.BaseObject))
            Assert-True $key.SuppressKeyPress "List Enter must replace the standard newline"
            Assert-Equal ($sample[1] -replace "`r", "") $editorBox.Text "Editor must continue or finish the Markdown list"
        }
        [void]$saveCurrent.Invoke($ww, @($false))

        $applyBlock = $editorBox.GetType().GetMethod("ApplyBlock", $flags)
        $refreshFormatting = $editorBox.GetType().GetMethod("RefreshFormatting", $flags)
        $editorBox.Text = "/"
        $editorBox.SelectionStart = 1
        [void]$applyBlock.Invoke($editorBox, @("## ", $true))
        $editorBox.SelectedText = "Riset"
        [void]$refreshFormatting.Invoke($editorBox, $null)
        $editorBox.Select(3, 5)
        Assert-True ($editorBox.SelectionFont.Bold) "Heading must render bold"
        Assert-True ($editorBox.SelectionFont.Size -gt $editorBox.Font.Size) "Heading must render larger than body text"
        Assert-Equal "## Riset" $editorBox.Text "Formatting must preserve Markdown"
        $editorBox.Undo()
        Assert-Equal "## " $editorBox.Text "Undo must restore the text edit before styling"
        $editorBox.Text = "Riset"
        $editorBox.SelectAll()
        $toggleInline = $editorBox.GetType().GetMethod("ToggleInline", $flags)
        [void]$toggleInline.Invoke($editorBox, @("**"))
        Assert-Equal "**Riset**" $editorBox.Text "Bold shortcut must preserve portable Markdown"
        Assert-Equal "Riset" $editorBox.SelectedText "Bold shortcut must keep the text selected"
        [void]$toggleInline.Invoke($editorBox, @("**"))
        Assert-Equal "Riset" $editorBox.Text "Second bold shortcut must remove the wrapping markers"
        [void]$saveCurrent.Invoke($ww, @($false))

        # QuickPath ReadOnly check
        [void]$showNote.Invoke($ww, @($quickPath))
        Assert-True $titleBox.ReadOnly "QuickPath title must be ReadOnly"
        Assert-Equal "Catatan kilat" $titleBox.Text "QuickPath displays Catatan kilat"
        $editorBox.Text = "- Catatan cepat`r`n- Baris baru"
        $savedQuick = $saveCurrent.Invoke($ww, @($false))
        Assert-True $savedQuick "QuickPath SaveCurrent must succeed"
        $quickDisk = [System.IO.File]::ReadAllText($quickPath)
        Assert-Equal "- Catatan cepat`r`n- Baris baru" $quickDisk "QuickPath must save body verbatim without # prefix"

        $ww.Dispose()
    }

    It "5. Smoke-test rendering WorkWindow Tab 1 (Catatan) in Dark and Light mode" {
        $dir = Assert-Isolated
        Reset-Dir $dir
        $notesDir = $noteFilesType.GetProperty("Dir", $flags).GetValue($null, $null)
        $writeMethod = $noteFilesType.GetMethod("Write", $flags)
        $p1 = [System.IO.Path]::Combine($notesDir, "20261114-s1.md")
        $p2 = [System.IO.Path]::Combine($notesDir, "20261114-s2.md")
        [void]$writeMethod.Invoke($null, @($p1, "# Daftar Belanja`r`n`r`n- [x] Kopi arabika`r`n- [ ] Roti gandum`r`n- [ ] Susu oat"))
        [void]$writeMethod.Invoke($null, @($p2, "# Catatan Rapat`r`n`r`nSinkronisasi jadwal rilis minggu depan."))

        $pins = $noteFilesType.GetMethod("LoadPins", $flags).Invoke($null, $null)
        [void]$noteFilesType.GetMethod("SetPin", $flags).Invoke($null, @($pins, $p1, $true))

        $dictType = [System.Collections.Generic.Dictionary``2].MakeGenericType([string], $noteMetaType)
        $meta = [Activator]::CreateInstance($dictType)
        $m1 = [Activator]::CreateInstance($noteMetaType, $true)
        $m1.Key = [System.IO.Path]::GetFileName($p1)
        $m1.Color = "emerald"
        $m1.Labels = "belanja, rumah"
        $meta[$m1.Key] = $m1
        [void]$noteFilesType.GetMethod("SaveMeta", $flags).Invoke($null, @(, $meta))

        # Dark mode
        Reset-DeskmateTheme -Light $false
        $wwDark = [Activator]::CreateInstance($wwType, @((New-TestIsland)))
        $hDark = $wwDark.Handle
        $wwDark.SelectTab(1)
        $bmpDark = New-Object System.Drawing.Bitmap $wwDark.Width, $wwDark.Height
        $wwDark.DrawToBitmap($bmpDark, (New-Object System.Drawing.Rectangle 0, 0, $wwDark.Width, $wwDark.Height))
        $pxDark = $bmpDark.GetPixel(10, 60)
        Assert-True ($pxDark.R -lt 50 -and $pxDark.G -lt 50 -and $pxDark.B -lt 50) "Dark mode Catatan tab background must be dark"
        $bmpDark.Dispose()
        $wwDark.Dispose()

        # Light mode
        Reset-DeskmateTheme -Light $true
        $wwLight = [Activator]::CreateInstance($wwType, @((New-TestIsland)))
        $hLight = $wwLight.Handle
        $wwLight.SelectTab(1)
        $bmpLight = New-Object System.Drawing.Bitmap $wwLight.Width, $wwLight.Height
        $wwLight.DrawToBitmap($bmpLight, (New-Object System.Drawing.Rectangle 0, 0, $wwLight.Width, $wwLight.Height))
        $pxLight = $bmpLight.GetPixel(10, 60)
        Assert-True ($pxLight.R -gt 200 -and $pxLight.G -gt 200 -and $pxLight.B -gt 200) "Light mode Catatan tab background must be bright"
        $bmpLight.Dispose()
        $wwLight.Dispose()

        Reset-DeskmateTheme -Light $false
        Reset-Dir $dir
    }

    if (-not ([System.Management.Automation.PSTypeName]'DeskmateTestWin32').Type) {
        Add-Type -TypeDefinition @"
using System;
using System.Runtime.InteropServices;
public static class DeskmateTestWin32 {
    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    public static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);
}
"@
    }

    It "Notes slash palette filters blocks and applies the selected block to the active line" {
        $dir = Assert-Isolated
        Reset-Dir $dir
        $notesDir = $noteFilesType.GetProperty("Dir", $flags).GetValue($null, $null)
        $note = Join-Path $notesDir "blocks.md"
        [void]$noteFilesType.GetMethod("Write", $flags).Invoke($null, @([string]$note, "# Riset`r`n`r`nPertanyaan`r`n"))
        $isl = New-TestIsland
        $docks = $islandType.GetField("dockWins", $flags)
        $docks.SetValue($isl, [Activator]::CreateInstance($docks.FieldType))
        $ww = [Activator]::CreateInstance($wwType, @($isl))
        try {
            $ww.Show()
            $ww.SelectTab(1)
            [Windows.Forms.Application]::DoEvents()
            $editor = $wwType.GetField("editor", $flags).GetValue($ww)
            [void]$editor.Focus()
            $editor.SelectionStart = $editor.Text.Length
            [void][DeskmateTestWin32]::SendMessage($editor.Handle, 0x0102, [IntPtr][int][char]'/', [IntPtr]1)
            [Windows.Forms.Application]::DoEvents()

            $menu = $wwType.GetField("blockMenu", $flags).GetValue($ww)
            Assert-NotNull $menu "Typing slash on an empty line must open the block palette"
            Assert-True $menu.Visible "The block palette must be visible"
            $menuType = $menu.GetType()
            $query = $menuType.GetField("query", $flags).GetValue($menu)
            $results = $menuType.GetField("results", $flags).GetValue($menu)
            $query.Text = "number"
            Assert-Equal 1 $results.Items.Count "Searching must filter to the numbered list"
            $enter = New-Object Windows.Forms.KeyEventArgs ([Windows.Forms.Keys]::Enter)
            [void]$menuType.GetMethod("OnQueryKey", $flags).Invoke($menu, @($query, $enter.PSObject.BaseObject))
            [Windows.Forms.Application]::DoEvents()
            Assert-Equal "Pertanyaan`n1. " $editor.Text "Enter must replace the slash with the selected Markdown block"
            Assert-True $editor.Focused "Focus must return to the note editor after choosing a block"

            [void][DeskmateTestWin32]::SendMessage($editor.Handle, 0x0102, [IntPtr][int][char]'/', [IntPtr]1)
            [Windows.Forms.Application]::DoEvents()
            $menu = $wwType.GetField("blockMenu", $flags).GetValue($ww)
            Assert-NotNull $menu "Slash must reopen the palette after an empty list marker"
            $query = $menuType.GetField("query", $flags).GetValue($menu)
            $query.Text = "quote"
            [void]$menuType.GetMethod("OnQueryKey", $flags).Invoke($menu, @($query, $enter.PSObject.BaseObject))
            [Windows.Forms.Application]::DoEvents()
            Assert-Equal "Pertanyaan`n> " $editor.Text "Reopening must replace the previous block and the slash"

            $editor.SelectedText = "Isi"
            [void][DeskmateTestWin32]::SendMessage($editor.Handle, 0x0102, [IntPtr][int][char]'/', [IntPtr]1)
            Assert-True ($null -eq $wwType.GetField("blockMenu", $flags).GetValue($ww)) "Slash inside text must stay literal"
            Assert-Equal "Pertanyaan`n> Isi/" $editor.Text "Slash in ordinary content must be preserved"
        }
        finally { $ww.Dispose() }
    }

    It "Maximized WorkWindow switches Notes and Tasks with independent native editor painting" {
        $dir = Assert-Isolated
        Reset-Dir $dir
        $isl = New-TestIsland
        $docks = $islandType.GetField("dockWins", $flags)
        $docks.SetValue($isl, [Activator]::CreateInstance($docks.FieldType))
        $ww = [Activator]::CreateInstance($wwType, @($isl))
        try {
            $ww.WindowState = [Windows.Forms.FormWindowState]::Maximized
            $ww.Show()
            [Windows.Forms.Application]::DoEvents()
            Assert-Equal ([Windows.Forms.FormWindowState]::Maximized) $ww.WindowState "Exercise the maximized window"
            $params = $wwType.GetProperty("CreateParams", $flags).GetValue($ww, $null)
            Assert-True (($params.ExStyle -band 0x02000000) -eq 0) "Native text inputs must repaint without waiting for whole-window compositing"
            $tabs = $wwType.GetField("tabs", $flags).GetValue($ww)
            $rail = $wwType.GetField("rail", $flags).GetValue($ww)
            $search = $wwType.GetField("search", $flags).GetValue($ww)
            foreach ($index in @(1, 0, 1, 0)) {
                $x = [int]($tabs.Width * (0.25 + $index * 0.5))
                $pos = [IntPtr]((([int]($tabs.Height / 2)) -shl 16) -bor $x)
                [void][DeskmateTestWin32]::SendMessage($tabs.Handle, 0x0201, [IntPtr]1, $pos)
                [void][DeskmateTestWin32]::SendMessage($tabs.Handle, 0x0202, [IntPtr]::Zero, $pos)
                [Windows.Forms.Application]::DoEvents()
                Assert-Equal $index $tabs.Selected "Click must select the intended page"
                Assert-Equal ($index -eq 0) $rail.Visible "Tasks rail visibility follows the selected tab"
                Assert-Equal ($index -eq 1) $search.Visible "Notes search visibility follows the selected tab"
            }
        }
        finally { $ww.Dispose() }
    }

    It "6. NoteTagChip and TaskIconButton fire Click exactly once per Win32 mouse click" {
        $chipType = Get-DeskmateType "Deskmate.NoteTagChip"
        $btnType = Get-DeskmateType "Deskmate.TaskIconButton"
        $chip = [Activator]::CreateInstance($chipType, $true)
        $btn = [Activator]::CreateInstance($btnType, $true)
        $chip.Size = New-Object System.Drawing.Size 120, 28
        $btn.Size = New-Object System.Drawing.Size 32, 32

        $getStyle = [System.Windows.Forms.Control].GetMethod("GetStyle", $flags)
        Assert-False ([bool]$getStyle.Invoke($chip, @([System.Windows.Forms.ControlStyles]::StandardClick))) "NoteTagChip must disable StandardClick"
        Assert-False ([bool]$getStyle.Invoke($btn, @([System.Windows.Forms.ControlStyles]::StandardClick))) "TaskIconButton must disable StandardClick"

        $null = $chip.Handle
        $null = $btn.Handle
        $script:chipClicks = 0
        $script:btnClicks = 0
        $chip.add_Click({ $script:chipClicks++ })
        $btn.add_Click({ $script:btnClicks++ })

        $WM_LBUTTONDOWN = 0x0201
        $WM_LBUTTONUP = 0x0202
        $MK_LBUTTON = [IntPtr]1
        $lParam = [IntPtr]((10 -shl 16) -bor 10)
        [void][DeskmateTestWin32]::SendMessage($chip.Handle, $WM_LBUTTONDOWN, $MK_LBUTTON, $lParam)
        [void][DeskmateTestWin32]::SendMessage($chip.Handle, $WM_LBUTTONUP, [IntPtr]::Zero, $lParam)
        [void][DeskmateTestWin32]::SendMessage($btn.Handle, $WM_LBUTTONDOWN, $MK_LBUTTON, $lParam)
        [void][DeskmateTestWin32]::SendMessage($btn.Handle, $WM_LBUTTONUP, [IntPtr]::Zero, $lParam)
        [System.Windows.Forms.Application]::DoEvents()

        Assert-Equal 1 $script:chipClicks "NoteTagChip must fire Click exactly once via Win32 WndProc"
        Assert-Equal 1 $script:btnClicks "TaskIconButton must fire Click exactly once via Win32 WndProc"
        $chip.Dispose()
        $btn.Dispose()
    }

    It "7. WorkWindow pinBtn toggles pin state once per click" {
        $dir = Assert-Isolated
        Reset-Dir $dir
        $notesDir = $noteFilesType.GetProperty("Dir", $flags).GetValue($null, $null)
        $p1 = [System.IO.Path]::Combine($notesDir, "20261114-pin.md")
        [void]$noteFilesType.GetMethod("Write", $flags).Invoke($null, @($p1, "# Pin Test`r`n`r`nBody"))

        $ww = [Activator]::CreateInstance($wwType, @((New-TestIsland)))
        $h = $ww.Handle
        $ww.SelectTab(1)
        $pinBtn = $wwType.GetField("pinBtn", $flags).GetValue($ww)
        $pinsField = $wwType.GetField("pins", $flags)
        $isPinnedMethod = $noteFilesType.GetMethod("IsPinned", $flags)
        $down = New-Object System.Windows.Forms.MouseEventArgs ([System.Windows.Forms.MouseButtons]::Left), 1, 10, 10, 0
        $up = New-Object System.Windows.Forms.MouseEventArgs ([System.Windows.Forms.MouseButtons]::Left), 1, 10, 10, 0
        $onDown = [System.Windows.Forms.Control].GetMethod("OnMouseDown", $flags)
        $onUp = [System.Windows.Forms.Control].GetMethod("OnMouseUp", $flags)

        [void]$onDown.Invoke($pinBtn, [object[]]@($down.psobject.BaseObject))
        [void]$onUp.Invoke($pinBtn, [object[]]@($up.psobject.BaseObject))
        $pins1 = $pinsField.GetValue($ww)
        Assert-True ($isPinnedMethod.Invoke($null, @($pins1, $p1))) "First click on pinBtn must pin the note"

        [void]$onDown.Invoke($pinBtn, [object[]]@($down.psobject.BaseObject))
        [void]$onUp.Invoke($pinBtn, [object[]]@($up.psobject.BaseObject))
        $pins2 = $pinsField.GetValue($ww)
        Assert-True (-not $isPinnedMethod.Invoke($null, @($pins2, $p1))) "Second click on pinBtn must unpin the note"

        $ww.Dispose()
        Reset-Dir $dir
    }

    It "8. NotePalette.CleanTags and NoteFiles.List #tag search work" {
        $dir = Assert-Isolated
        Reset-Dir $dir
        $paletteType = Get-DeskmateType "Deskmate.NotePalette"
        $clean = $paletteType.GetMethod("CleanTags", $flags).Invoke($null, @("#kerja, ide, kerja"))
        Assert-Equal $clean "kerja, ide" "CleanTags must strip #, dedupe, and join with ', '"

        $notesDir = $noteFilesType.GetProperty("Dir", $flags).GetValue($null, $null)
        $p1 = [System.IO.Path]::Combine($notesDir, "20261114-tag.md")
        [void]$noteFilesType.GetMethod("Write", $flags).Invoke($null, @($p1, "# Tag Note`r`n`r`nContent without keyword"))
        $dictType = [System.Collections.Generic.Dictionary``2].MakeGenericType([string], $noteMetaType)
        $meta = [Activator]::CreateInstance($dictType)
        $m1 = [Activator]::CreateInstance($noteMetaType, $true)
        $m1.Key = [System.IO.Path]::GetFileName($p1)
        $m1.Labels = $clean
        $meta[$m1.Key] = $m1
        [void]$noteFilesType.GetMethod("SaveMeta", $flags).Invoke($null, @(, $meta))

        $pins = $noteFilesType.GetMethod("LoadPins", $flags).Invoke($null, $null)
        $list5 = $noteFilesType.GetMethod("List", [Type[]]@([string], $pins.GetType(), $dictType, [bool], [string]))
        $found = $list5.Invoke($null, @("#kerja", $pins, $meta, $false, ""))
        Assert-Equal $found.Count 1 "Searching #kerja must find note with label 'kerja, ide'"
        Reset-Dir $dir
    }

    It "9. Clicking noteTagChip opens ListNameDialog once, saves cleaned tags, and persists across note switch & restart" {
        $dir = Assert-Isolated
        Reset-Dir $dir
        $notesDir = $noteFilesType.GetProperty("Dir", $flags).GetValue($null, $null)
        $writeMethod = $noteFilesType.GetMethod("Write", $flags)
        $p1 = [System.IO.Path]::Combine($notesDir, "20261114-t1.md")
        $p2 = [System.IO.Path]::Combine($notesDir, "20261114-t2.md")
        [void]$writeMethod.Invoke($null, @($p1, "# Catatan Satu`r`n`r`nIsi satu"))
        [void]$writeMethod.Invoke($null, @($p2, "# Catatan Dua`r`n`r`nIsi dua"))

        $ww = [Activator]::CreateInstance($wwType, @((New-TestIsland)))
        $null = $ww.Handle
        $ww.SelectTab(1)
        $openNote = $wwType.GetMethod("OpenNote", $flags)
        [void]$openNote.Invoke($ww, @($p1))
        Assert-Equal $p1 ([string]$wwType.GetField("current", $flags).GetValue($ww)) "p1 must be current note"

        $chip = $wwType.GetField("noteTagChip", $flags).GetValue($ww)
        $null = $chip.Handle

        $script:dialogOpens = 0
        $script:handledDlg = $null
        $timer = New-Object System.Windows.Forms.Timer
        $timer.Interval = 50
        $timer.add_Tick({
            foreach ($f in @([System.Windows.Forms.Application]::OpenForms)) {
                if ($f.GetType().Name -eq "ListNameDialog" -and $f -ne $script:handledDlg) {
                    $script:handledDlg = $f
                    $script:dialogOpens++
                    $inp = $f.GetType().GetField("input", $flags).GetValue($f)
                    $ok = $f.GetType().GetField("okBtn", $flags).GetValue($f)
                    $inp.Text = "#kerja, ide"
                    $ok.PerformClick()
                }
            }
        })
        $timer.Start()

        $WM_LBUTTONDOWN = 0x0201
        $WM_LBUTTONUP = 0x0202
        $MK_LBUTTON = [IntPtr]1
        $lParam = [IntPtr]((10 -shl 16) -bor 10)
        [void][DeskmateTestWin32]::SendMessage($chip.Handle, $WM_LBUTTONDOWN, $MK_LBUTTON, $lParam)
        [void][DeskmateTestWin32]::SendMessage($chip.Handle, $WM_LBUTTONUP, [IntPtr]::Zero, $lParam)

        [System.Windows.Forms.Application]::DoEvents()
        Start-Sleep -Milliseconds 100
        [System.Windows.Forms.Application]::DoEvents()
        $timer.Stop()
        $timer.Dispose()

        Assert-Equal 1 $script:dialogOpens "Physical click on noteTagChip must open ListNameDialog exactly once"
        Assert-Equal "kerja, ide" $chip.Tags "noteTagChip.Tags must reflect cleaned tags"

        [void]$openNote.Invoke($ww, @($p2))
        [void]$openNote.Invoke($ww, @($p1))
        Assert-Equal "kerja, ide" $chip.Tags "noteTagChip.Tags must persist when switching notes"
        $ww.Dispose()

        $ww2 = [Activator]::CreateInstance($wwType, @((New-TestIsland)))
        $null = $ww2.Handle
        $ww2.SelectTab(1)
        [void]$openNote.Invoke($ww2, @($p1))
        $chip2 = $wwType.GetField("noteTagChip", $flags).GetValue($ww2)
        Assert-Equal "kerja, ide" $chip2.Tags "noteTagChip.Tags must survive WorkWindow restart on disk"
        $ww2.Dispose()
        Reset-Dir $dir
    }

    It "10. NoteFiles.AddDeletedKey, IsDeletedRemote, and NoteFiles.Remove work immediately" {
        $nfType = Get-DeskmateType "Deskmate.NoteFiles"
        $flags = [System.Reflection.BindingFlags]"Public,NonPublic,Static"
        $addKey = $nfType.GetMethod("AddDeletedKey", $flags)
        $isDel = $nfType.GetMethod("IsDeletedRemote", $flags)
        $loadDel = $nfType.GetMethod("LoadDeletedDrive", $flags)
        $saveDel = $nfType.GetMethod("SaveDeletedDrive", $flags)
        $removeFn = $nfType.GetMethod("Remove", $flags)
        $writeFn = $nfType.GetMethod("Write", $flags)

        $origDel = $loadDel.Invoke($null, @())
        $testKey = "20261114-testdel.md"
        [void]$nfType.InvokeMember("AddDeletedKey", [System.Reflection.BindingFlags]"InvokeMethod,Public,NonPublic,Static", $null, $null, @($testKey))
        $matched = [bool]$nfType.InvokeMember("IsDeletedRemote", [System.Reflection.BindingFlags]"InvokeMethod,Public,NonPublic,Static", $null, $null, @("", $testKey))
        Assert-True $matched "IsDeletedRemote must return true for queued key"
        $notMatched = [bool]$nfType.InvokeMember("IsDeletedRemote", [System.Reflection.BindingFlags]"InvokeMethod,Public,NonPublic,Static", $null, $null, @("nonexistent-drive-id", "other-note.md"))
        Assert-False $notMatched "IsDeletedRemote must return false for unrelated note"
        [void]$nfType.InvokeMember("SaveDeletedDrive", [System.Reflection.BindingFlags]"InvokeMethod,Public,NonPublic,Static", $null, $null, @(,$origDel))

        $tmpNote = Join-Path $f34Tmp "fast-remove-test.md"
        [System.IO.File]::WriteAllText($tmpNote, "# Fast Remove`nBody")
        Assert-True (Test-Path -LiteralPath $tmpNote) "Temp note must exist before Remove"
        $ok = [bool]$removeFn.Invoke($null, @([string]$tmpNote))
        Assert-True $ok "NoteFiles.Remove must return true"
        Assert-False (Test-Path -LiteralPath $tmpNote) "NoteFiles.Remove must remove file immediately"

        $blockedPath = Join-Path $f34Tmp "blocked-resurrect.md"
        [void]$nfType.InvokeMember("AddDeletedKey", [System.Reflection.BindingFlags]"InvokeMethod,Public,NonPublic,Static", $null, $null, @("blocked-resurrect.md"))
        $args = [object[]]@([string]$blockedPath, [string]"# Should Not Exist", [string]"", [string]"blocked-resurrect.md", $false, [DateTime]::MinValue, [DateTime]::UtcNow, $false)
        $wrote = [bool]$nfType.InvokeMember("WriteFromDrive", [System.Reflection.BindingFlags]"InvokeMethod,Public,NonPublic,Static", $null, $null, $args)
        Assert-False $wrote "WriteFromDrive must return false for tombstoned key"
        Assert-True ([bool]$args[7]) "WriteFromDrive must set skipped=true"
        Assert-False (Test-Path -LiteralPath $blockedPath) "WriteFromDrive must not recreate tombstoned file"
        [void]$nfType.InvokeMember("SaveDeletedDrive", [System.Reflection.BindingFlags]"InvokeMethod,Public,NonPublic,Static", $null, $null, @(,$origDel))
    }

    It "11. Drive loads first, then uploads local edits and deletes without pulling" {
        $dir = Assert-Isolated
        Reset-Dir $dir

        $vaultType = Get-DeskmateType "GoogleCalVault"
        $httpType = Get-DeskmateType "GoogleWorkspaceHttp"
        $driveType = Get-DeskmateType "GoogleDriveNotesClient"
        $fetchResultType = Get-DeskmateType "FetchResult"
        $transportField = $httpType.GetField("Transport", $flags)
        $funcType = [System.Func``4].MakeGenericType([string], [string], [string], $fetchResultType)
        $syncMethod = $driveType.GetMethod("Transfer", $flags)
        $versions = [Activator]::CreateInstance([System.Collections.Generic.Dictionary[string,datetime]])

        $notesDir = [string]$noteFilesType.GetProperty("Dir", $flags).GetValue($null, $null)
        $quickPath = [string]$noteFilesType.GetProperty("QuickPath", $flags).GetValue($null, $null)
        [void][System.IO.Directory]::CreateDirectory($notesDir)

        $dictType = [System.Collections.Generic.Dictionary``2].MakeGenericType([string], $noteMetaType)
        $meta = [Activator]::CreateInstance($dictType)
        $pins = [Activator]::CreateInstance([System.Collections.Generic.HashSet[string]], @([System.StringComparer]::OrdinalIgnoreCase))
        $pendingDeletes = [Activator]::CreateInstance([System.Collections.Generic.List[string]])
        $completedDeletes = [Activator]::CreateInstance([System.Collections.Generic.List[string]])

        $script:f34Calls = New-Object 'System.Collections.Generic.List[string]'
        $script:f34Phase = "A"
        $script:f34FetchResultType = $fetchResultType
        $script:f34NfType = $noteFilesType
        $script:f34Flags = $flags

        try {
            $v = [Activator]::CreateInstance($vaultType)
            $v.ClientId = "test-client-id"
            $v.RefreshToken = "test-refresh-token"
            $v.AccessToken = "tok"
            $v.ExpiresUtc = [DateTime]::UtcNow.AddHours(1)
            $v.Save()

            $s = [Activator]::CreateInstance($settingsType)
            $s.Set("drive.notes.folder", "folder-1")
            $s.Save()

            $sb = {
                param([string]$method, [string]$url, [string]$body)
                $script:f34Calls.Add("$method $url")
                $fr = [Activator]::CreateInstance($script:f34FetchResultType)
                $json = "{}"
                if ($method -eq "GET" -and $url -like "*/files/folder-1*") {
                    $json = '{"id":"folder-1","trashed":false}'
                }
                elseif ($script:f34Phase -eq "A") {
                    if ($method -eq "GET" -and $url -like "*/files?q=*") {
                        $json = '{"files":[{"id":"drv-99","name":"deleted-before-id.md","modifiedTime":"2026-11-14T10:00:00Z","appProperties":{"deskmateKey":"deleted-before-id.md"},"trashed":false}]}'
                    }
                    elseif ($method -eq "DELETE" -and $url -like "*/files/drv-99") {
                        $json = '{}'
                    }
                }
                elseif ($script:f34Phase -eq "B") {
                    if ($method -eq "GET" -and $url -like "*/files?q=*") {
                        $json = '{"files":[{"id":"drv-race","name":"race-note.md","modifiedTime":"2026-11-14T10:00:00Z","appProperties":{"deskmateKey":"race-note.md"},"trashed":false}]}'
                    }
                    elseif ($method -eq "GET" -and $url -like "*/files/drv-race?alt=media*") {
                        [void]$script:f34NfType.InvokeMember("AddDeletedKey", [System.Reflection.BindingFlags]"InvokeMethod,Public,NonPublic,Static", $null, $null, @("race-note.md"))
                        $json = "# Should Not Resurrect"
                    }
                }
                elseif ($method -eq "GET" -and $url -like "*/files?q=*") {
                    $json = '{"files":[{"id":"drv-edit","name":"edit.md","modifiedTime":"2026-11-14T10:00:00Z","appProperties":{"deskmateKey":"edit.md"}}]}'
                }
                elseif ($method -eq "GET" -and $url -like "*/files/drv-edit?alt=media*") {
                    $json = "# Loaded from Drive"
                }
                elseif ($method -eq "PATCH" -and $url -like "*/drv-edit?*") {
                    if ($body -notlike "*# Local edit*") { throw "Local edit missing from upload" }
                    $json = '{"id":"drv-edit"}'
                }

                $bytes = [System.Text.Encoding]::UTF8.GetBytes($json)
                $fr.Status = 200
                $fr.Data = $bytes
                $fr.Length = $bytes.Length
                return $fr
            }
            $delegate = $sb -as $funcType
            Assert-NotNull $delegate "Transport delegate must convert cleanly"
            $transportField.SetValue($null, $delegate)

            # Part A: Key tombstone triggers remote DELETE and prevents re-download
            $pendingDeletes.Add("key:deleted-before-id.md")
            $syncArgsA = [object[]]@([string]$notesDir, [string]$quickPath, $meta, $pins, $pendingDeletes, $completedDeletes, $false, $versions, $false, $null)
            $okA = [bool]$syncMethod.Invoke($null, $syncArgsA)
            Assert-True $okA "Part A Sync must succeed (err=$($syncArgsA[9]))"
            Assert-True (@($script:f34Calls | Where-Object { $_ -like "DELETE */files/drv-99" }).Count -ge 1) "Part A must issue DELETE for drv-99"
            Assert-True ($completedDeletes.Contains("key:deleted-before-id.md")) "Part A completedDeletes must contain key:deleted-before-id.md"
            Assert-Equal 0 (@($script:f34Calls | Where-Object { $_ -like "*alt=media*" }).Count) "Part A must not issue alt=media GET"
            Assert-False (Test-Path -LiteralPath (Join-Path $notesDir "deleted-before-id.md")) "Part A deleted-before-id.md must not exist on disk"

            # Part B: In-flight GET ?alt=media completing after local delete tombstone does NOT resurrect
            $script:f34Phase = "B"
            $script:f34Calls.Clear()
            $emptyList = [Activator]::CreateInstance([System.Collections.Generic.List[string]])
            [void]$noteFilesType.InvokeMember("SaveDeletedDrive", [System.Reflection.BindingFlags]"InvokeMethod,Public,NonPublic,Static", $null, $null, @(,$emptyList))
            $meta.Clear()
            $pins.Clear()
            $pendingDeletes.Clear()
            $completedDeletes.Clear()

            $syncArgsB = [object[]]@([string]$notesDir, [string]$quickPath, $meta, $pins, $pendingDeletes, $completedDeletes, $true, $versions, $false, $null)
            $okB = [bool]$syncMethod.Invoke($null, $syncArgsB)
            Assert-True $okB "Part B Sync must succeed (err=$($syncArgsB[9]))"
            Assert-True (@($script:f34Calls | Where-Object { $_ -like "GET */files/drv-race?alt=media*" }).Count -ge 1) "Part B must have attempted alt=media GET for drv-race"
            Assert-False (Test-Path -LiteralPath (Join-Path $notesDir "race-note.md")) "Part B race-note.md must not exist on disk"
            Assert-False ($meta.ContainsKey("race-note.md")) "Part B meta must not contain race-note.md"
            $stillDeleted = [bool]$noteFilesType.InvokeMember("IsDeletedRemote", [System.Reflection.BindingFlags]"InvokeMethod,Public,NonPublic,Static", $null, $null, @("", "race-note.md"))
            Assert-True $stillDeleted "Part B IsDeletedRemote must remain true for race-note.md"

            $script:f34Phase = "C"
            $script:f34Calls.Clear()
            $editPath = Join-Path $notesDir "edit.md"
            $syncArgsC = [object[]]@([string]$notesDir, [string]$quickPath, $meta, $pins, $pendingDeletes, $completedDeletes, $true, $versions, $false, $null)
            Assert-True ([bool]$syncMethod.Invoke($null, $syncArgsC)) "Initial load should succeed"
            Assert-Equal "# Loaded from Drive" ([System.IO.File]::ReadAllText($editPath)) "Initial load should populate local Markdown"
            Assert-Equal 0 (@($script:f34Calls | Where-Object { $_ -match '^(PATCH|POST|DELETE) ' }).Count) "Initial load must not upload or delete notes"
            [System.IO.File]::WriteAllText($editPath, "# Local edit")
            [System.IO.File]::SetLastWriteTimeUtc($editPath, [DateTime]::Parse("2026-11-14T10:00:00.1000000Z").ToUniversalTime())
            $script:f34Calls.Clear()
            $syncArgsC[6] = $false
            Assert-True ([bool]$syncMethod.Invoke($null, $syncArgsC)) "Upload should succeed"
            Assert-Equal 1 (@($script:f34Calls | Where-Object { $_ -like "PATCH */drv-edit?*" }).Count) "Upload must propagate edits within the same second"
            Assert-Equal 0 (@($script:f34Calls | Where-Object { $_ -like "*alt=media*" }).Count) "Upload must not download notes"
            Assert-Equal "# Local edit" ([System.IO.File]::ReadAllText($editPath)) "Upload must leave local note content intact"
            $script:f34Calls.Clear()
            Assert-True ([bool]$syncMethod.Invoke($null, $syncArgsC)) "Repeated upload check should succeed"
            Assert-Equal 0 (@($script:f34Calls | Where-Object { $_ -like "PATCH */drv-edit?*" }).Count) "The same edit must not upload twice"
        } finally {
            $transportField.SetValue($null, $null)
            $vaultType.GetMethod("Forget", $flags).Invoke($null, $null) | Out-Null
            Reset-Dir $dir
        }
    }
}

Remove-Item -LiteralPath $f34Tmp -Recurse -Force -ErrorAction SilentlyContinue
