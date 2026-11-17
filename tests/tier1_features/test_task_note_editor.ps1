$common = Join-Path (Split-Path -Parent $PSScriptRoot) "common"
. (Join-Path $common "TestHarness.ps1")
. (Join-Path $common "DeskmateLoader.ps1")
if (-not $env:DESKMATE_DIR -or -not $env:DESKMATE_DIR.StartsWith(
    (Join-Path ([IO.Path]::GetTempPath()) "Deskmate\tests\"), [StringComparison]::OrdinalIgnoreCase)) {
    throw "Run this suite via tests/run_e2e.ps1 so task files are isolated."
}
Add-Type -AssemblyName WindowsBase, PresentationCore, PresentationFramework, WindowsFormsIntegration

Describe "Task note editor" {
    $flags = [Reflection.BindingFlags]"Public, NonPublic, Instance, Static"
    $editorType = Get-DeskmateType "TaskNoteEditor"
    $taskEditorType = Get-DeskmateType "TaskEditor"
    $itemType = Get-DeskmateType "TodoItem"
    $fileType = Get-DeskmateType "TodoFile"

    function Invoke-Editor($target, $name, [object[]]$arguments) {
        $target.GetType().GetMethod($name, $flags).Invoke($target, $arguments)
    }

    function Read-Field($target, $name) {
        $target.GetType().GetField($name, $flags).GetValue($target)
    }

    function Text-At($cell) {
        $range = New-Object System.Windows.Documents.TextRange $cell.ContentStart, $cell.ContentEnd
        $range.Text.Trim()
    }

    It "uses the chosen dimensions, allows typing in the first cell and exits at the end" {
        $hostEditor = [Activator]::CreateInstance($editorType)
        try {
            Invoke-Editor $hostEditor LoadMarkdown @("")
            $wpf = Read-Field $hostEditor editor
            Invoke-Editor $hostEditor ShowTablePicker
            (Read-Field $hostEditor rowCount).Text = "0"
            Assert-False (Invoke-Editor $hostEditor CreateTable) "Invalid size must leave the picker open"
            (Read-Field $hostEditor rowCount).Text = "2"
            (Read-Field $hostEditor columnCount).Text = "3"
            Assert-True (Invoke-Editor $hostEditor CreateTable) "A valid size inserts the table"
            $table = $wpf.Document.Blocks.FirstBlock
            $rows = $table.RowGroups[0].Rows
            Assert-Equal 2 $rows.Count
            Assert-Equal 3 $table.Columns.Count
            Assert-True (Read-Field $hostEditor manageTable).IsEnabled
            $wpf.Selection.Text = "Header"
            Assert-Equal "Header" (Text-At $rows[0].Cells[0])
            Invoke-Editor $hostEditor MoveCell @($rows[1].Cells[2], $false)
            Assert-False (Read-Field $hostEditor manageTable).IsEnabled
            $wpf.Selection.Text = "After table"
            Assert-Equal 2 $rows.Count
            Assert-True ((Invoke-Editor $hostEditor SaveMarkdown @("")).EndsWith("After table"))
        }
        finally { $hostEditor.Dispose() }
    }

    It "preserves original Markdown and saves edits, formatting, pipes and separate adjacent tables" {
        $original = "| Name | Status |`r`n| :--- | ---: |`r`n| **Item** | A \| B |`r`n"
        $hostEditor = [Activator]::CreateInstance($editorType)
        $reopened = [Activator]::CreateInstance($editorType)
        try {
            Invoke-Editor $hostEditor LoadMarkdown @($original)
            Assert-Equal $original (Invoke-Editor $hostEditor SaveMarkdown @($original))
            $wpf = Read-Field $hostEditor editor
            $table = $wpf.Document.Blocks.FirstBlock
            $first = $table.RowGroups[0].Rows[0].Cells[0]
            Invoke-Editor $hostEditor ChangeTable @("column", 1, $first)
            $wpf.Selection.Text = "Owner"
            Invoke-Editor $hostEditor ChangeTable @("row", 1, $table.RowGroups[0].Rows[1].Cells[0])
            $wpf.Selection.Text = "Follow-up"
            Invoke-Editor $hostEditor InsertTable @(2, 1)
            $wpf.Selection.Text = "Second table"
            $markdown = Invoke-Editor $hostEditor SaveMarkdown @($original)

            $item = [Activator]::CreateInstance($itemType)
            $item.Title = "Round-trip"
            $item.Notes = $markdown
            $tasks = [System.Collections.Generic.List``1].MakeGenericType($itemType).GetConstructor([type[]]@()).Invoke($null)
            $tasks.Add($item)
            Assert-True ($fileType.GetMethod("Save", $flags).Invoke($null, @(,$tasks)))
            $stored = $fileType.GetMethod("Load", $flags).Invoke($null, $null)[0]
            Assert-Equal $markdown $stored.Notes
            Invoke-Editor $reopened LoadMarkdown @($stored.Notes)
            $tables = @((Read-Field $reopened editor).Document.Blocks | Where-Object { $_ -is [Windows.Documents.Table] })
            Assert-Equal 2 $tables.Count
            Assert-Equal 3 $tables[0].Columns.Count
            Assert-Equal 3 $tables[0].RowGroups[0].Rows.Count
            Assert-Equal "Owner" (Text-At $tables[0].RowGroups[0].Rows[0].Cells[1])
            Assert-Equal "A | B" (Text-At $tables[0].RowGroups[0].Rows[1].Cells[2])
            Assert-Equal "Second table" (Text-At $tables[1].RowGroups[0].Rows[0].Cells[0])
            Assert-True ($stored.Notes.Contains("**Item**"))
            Assert-True ($stored.Notes.Contains("| :--- | --- | ---: |"))
        }
        finally { $hostEditor.Dispose(); $reopened.Dispose() }
    }

    It "pastes Markdown as a real editable table and keeps pipe text below it separate" {
        $hostEditor = [Activator]::CreateInstance($editorType)
        $reopened = [Activator]::CreateInstance($editorType)
        try {
            Invoke-Editor $hostEditor LoadMarkdown @("")
            $wpf = Read-Field $hostEditor editor
            $clipboard = New-Object System.Windows.DataObject
            $clipboard.SetData([Windows.DataFormats]::UnicodeText, "| H1 | H2 |`n| --- | --- |`n| Content | Check |")
            $paste = New-Object System.Windows.DataObjectPastingEventArgs $clipboard, $false, ([Windows.DataFormats]::UnicodeText)
            Invoke-Editor $hostEditor Paste @($wpf, $paste.PSObject.BaseObject)
            $table = $wpf.Document.Blocks.FirstBlock
            Assert-True ($table -is [Windows.Documents.Table])
            Invoke-Editor $hostEditor ChangeTable @("row", 1, $table.RowGroups[0].Rows[1].Cells[0])
            Assert-Equal 3 $table.RowGroups[0].Rows.Count
            Invoke-Editor $hostEditor ChangeTable @("deleteRow", 0, $table.RowGroups[0].Rows[2].Cells[0])
            Assert-Equal 2 $table.RowGroups[0].Rows.Count
            Invoke-Editor $hostEditor ChangeTable @("exit", 0, $table.RowGroups[0].Rows[0].Cells[0])
            $wpf.Selection.Text = "A note | still text"
            $markdown = Invoke-Editor $hostEditor SaveMarkdown @("")
            Invoke-Editor $reopened LoadMarkdown @($markdown)
            $document = (Read-Field $reopened editor).Document
            Assert-Equal 2 $document.Blocks.FirstBlock.RowGroups[0].Rows.Count
            Assert-Equal "A note | still text" (Text-At $document.Blocks.LastBlock)
        }
        finally { $hostEditor.Dispose(); $reopened.Dispose() }
    }

    It "retains the draft when the task file cannot be written and allows a successful retry" {
        $item = [Activator]::CreateInstance($itemType)
        $item.Title = "Save retry"
        $dialog = [Activator]::CreateInstance($taskEditorType, @(,$item))
        try {
            $null = $dialog.Handle
            $hostEditor = Read-Field $dialog notesBox
            $wpf = Read-Field $hostEditor editor
            Invoke-Editor $hostEditor InsertTable @(2, 2)
            $wpf.Selection.Text = "Keep this draft"

            $tasks = [System.Collections.Generic.List``1].MakeGenericType($itemType).GetConstructor([type[]]@()).Invoke($null)
            $tasks.Add($item)
            $saveMethod = $fileType.GetMethod("Save", $flags)
            $commitField = $taskEditorType.GetField("Commit", $flags)
            $handler = {
                param($draft)
                $item.Notes = $draft.Notes
                if (-not $saveMethod.Invoke($null, @(,$tasks))) { return "Disk write failed" }
                return $null
            }.GetNewClosure()
            $commitField.SetValue($dialog, [System.Management.Automation.LanguagePrimitives]::ConvertTo($handler, $commitField.FieldType))

            $file = Join-Path $env:DESKMATE_DIR "tugas.tsv"
            $lock = [IO.File]::Open($file, "OpenOrCreate", "ReadWrite", "None")
            try {
                Invoke-Editor $dialog SaveDraft
                Assert-False $dialog.IsDisposed
                Assert-Equal "Disk write failed" (Read-Field $dialog warning).Text
                Assert-True ($dialog.Notes.Contains("Keep this draft"))
            }
            finally { $lock.Dispose() }

            Invoke-Editor $dialog SaveDraft
            Assert-Equal ([Windows.Forms.DialogResult]::OK) $dialog.DialogResult
            Assert-True ($fileType.GetMethod("Load", $flags).Invoke($null, $null)[0].Notes.Contains("Keep this draft"))
        }
        finally { $dialog.Dispose() }
    }

    It "keeps an existing custom time and maps each selected date to its displayed day" {
        $item = [Activator]::CreateInstance($itemType)
        $item.Title = "Custom time"
        $item.Due = [datetime]"2026-11-20T09:07:00"
        $dialog = [Activator]::CreateInstance($taskEditorType, @(,$item))
        try {
            Assert-Equal $item.Due $dialog.Due
            $date = Read-Field $dialog date
            $date.Pick(10)
            Assert-Equal ($item.Due.AddDays(3)) $dialog.Due
            $date.Pick(10)
            Assert-Equal ($item.Due.AddDays(6)) $dialog.Due
            Assert-Equal $dialog.Due.ToString("ddd, dd MMM") $date.Items[$date.Selected]
        }
        finally { $dialog.Dispose() }
    }
}
