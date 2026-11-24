# test_x05_tasks_notes.ps1
# Tier 3 pairwise: task validation x semantic ink, empty state x theme, and note images x offline cache.

$commonDir = Join-Path (Split-Path -Parent $PSScriptRoot) "common"
. (Join-Path $commonDir "TestHarness.ps1")
. (Join-Path $commonDir "DeskmateLoader.ps1")
if (-not $env:DESKMATE_DIR -or -not $env:DESKMATE_DIR.StartsWith(
    (Join-Path ([IO.Path]::GetTempPath()) "Deskmate\tests\"), [StringComparison]::OrdinalIgnoreCase)) {
    throw "Run this suite via tests/run_e2e.ps1 so task files are isolated."
}
Add-Type -AssemblyName WindowsBase, PresentationCore, PresentationFramework, WindowsFormsIntegration

Describe "Tier 3 Pairwise: Tasks x Notes" {
    $flags = [System.Reflection.BindingFlags]'Public,NonPublic,Instance,Static'

    function Invoke-Declared($target, $name, [object[]]$arguments) {
        $method = $target.GetType().GetMethod($name, [System.Reflection.BindingFlags]'Public,NonPublic,Instance')
        if (-not $method) { throw "Method $name not found on $($target.GetType().Name)" }
        return $method.Invoke($target, $arguments)
    }

    function Read-DeclaredField($target, $name) {
        return $target.GetType().GetField($name, [System.Reflection.BindingFlags]'Public,NonPublic,Instance').GetValue($target)
    }

    function New-EmptyList($itemType) {
        $listType = [System.Collections.Generic.List``1].MakeGenericType($itemType)
        return ,([Activator]::CreateInstance($listType))
    }

    function New-TestPngBytes {
        $bmp = New-Object System.Drawing.Bitmap 6, 6
        try {
            $g = [System.Drawing.Graphics]::FromImage($bmp)
            try { $g.Clear([System.Drawing.Color]::FromArgb(255, 36, 118, 220)) }
            finally { $g.Dispose() }
            $stream = New-Object System.IO.MemoryStream
            try {
                $bmp.Save($stream, [System.Drawing.Imaging.ImageFormat]::Png)
                return $stream.ToArray()
            }
            finally { $stream.Dispose() }
        }
        finally { $bmp.Dispose() }
    }

    BeforeEach {
        Reset-DeskmateTheme -Light $false
    }

    It "shows the Negative title-warning for an empty task and clears it once a title is entered" {
        $itemType = Get-DeskmateType "TodoItem"
        $item = [Activator]::CreateInstance($itemType, $true)
        $item.Title = ""
        $editor = [Activator]::CreateInstance((Get-DeskmateType "TaskEditor"), $flags, $null, @($item), $null)
        try {
            [void](Invoke-Declared $editor "SaveDraft" @())
            $warning = Read-DeclaredField $editor "warning"
            $negative = (Get-DeskmateStaticField "Ink" "Base").Negative
            Assert-Equal "Judul wajib diisi" $warning.Text "Empty save must show the title warning"
            Assert-Equal $negative.ToArgb() $warning.ForeColor.ToArgb() "Title warning must use Ink.Base.Negative"
            $ratio = Get-DeskmateContrast $warning.ForeColor (Get-DeskmateStaticProperty "KitInk" "Window")
            Assert-True ($ratio -ge 4.5) "Title warning contrast $ratio must be >= 4.5"

            $title = Read-DeclaredField $editor "title"
            $title.Text = "Tugas valid"
            Assert-Equal "" $warning.Text "Entering a title must clear the title warning"
        }
        finally { $editor.Dispose() }
    }

    It "shows an overdue warning with the same Negative ink and clears it when the due toggle is removed" {
        $itemType = Get-DeskmateType "TodoItem"
        $item = [Activator]::CreateInstance($itemType, $true)
        $item.Title = "Tenggat lampau"
        $item.Due = [DateTime]::Now.AddDays(-2)
        $editor = [Activator]::CreateInstance((Get-DeskmateType "TaskEditor"), $flags, $null, @($item), $null)
        try {
            $warning = Read-DeclaredField $editor "warning"
            $negative = (Get-DeskmateStaticField "Ink" "Base").Negative
            Assert-True ($warning.Text.Length -gt 0) "An overdue task must show a due warning"
            Assert-Equal $negative.ToArgb() $warning.ForeColor.ToArgb() "Due warning must use Ink.Base.Negative"
            $ratio = Get-DeskmateContrast $warning.ForeColor (Get-DeskmateStaticProperty "KitInk" "Window")
            Assert-True ($ratio -ge 4.5) "Due warning contrast $ratio must be >= 4.5"

            $hasDue = Read-DeclaredField $editor "hasDue"
            $hasDue.Checked = $false
            Assert-Equal "" $warning.Text "Clearing the due toggle must clear the warning"
        }
        finally { $editor.Dispose() }
    }

    It "renders the task empty state with readable theme ink and visibly different light/dark surfaces" {
        $taskRowType = Get-DeskmateType "TaskRow"
        $todoType = Get-DeskmateType "TodoItem"
        $bitmaps = @{}
        try {
            foreach ($light in @($false, $true)) {
                Reset-DeskmateTheme -Light $light
                $view = New-DeskmateControl "TaskView"
                try {
                    $open = New-EmptyList $taskRowType
                    $done = New-EmptyList $todoType
                    $view.SetData($open, $done, $false, $true, "Belum ada tugas", "Tambahkan tugas baru")
                    Assert-Equal 0 $view.RowCount "An empty open/done view must render the dedicated empty state, not a fake row"
                    $bmp = Render-DeskmateControlToBitmap -Control $view -Width 360 -Height 220
                    try {
                        $bitmaps[$light] = $bmp
                        $ink = Get-DeskmateStaticProperty "KitInk" "Text"
                        $sub = Get-DeskmateStaticProperty "KitInk" "Sub"
                        $faint = Get-DeskmateStaticProperty "KitInk" "Faint"
                        $surface = Get-DeskmateStaticProperty "KitInk" "Window"
                        Assert-True ((Get-DeskmateContrast $ink $surface) -ge 4.5) "Empty title ink must be readable"
                        Assert-True ((Get-DeskmateContrast $sub $surface) -ge 4.5) "Empty subtitle ink must be readable"
                        Assert-True ((Get-DeskmateContrast $faint $surface) -ge 4.5) "Empty tertiary ink must be readable"
                        $nonEmpty = 0
                        for ($y = 0; $y -lt $bmp.Height; $y += 4) {
                            for ($x = 0; $x -lt $bmp.Width; $x += 4) {
                                $c = $bmp.GetPixel($x, $y)
                                if ($c.R -ne $c.G -or $c.G -ne $c.B -or $c.R -gt 20) { $nonEmpty++ }
                            }
                        }
                        Assert-True ($nonEmpty -gt 20) "Empty state must render ink in light=$light"
                    }
                    finally { $bmp = $null }
                }
                finally { $view.Dispose() }
            }
            $darkBmp = $bitmaps[$false]
            $lightBmp = $bitmaps[$true]
            $diff = 0
            for ($y = 0; $y -lt $darkBmp.Height; $y += 2) {
                for ($x = 0; $x -lt $darkBmp.Width; $x += 2) {
                    if ($darkBmp.GetPixel($x, $y).ToArgb() -ne $lightBmp.GetPixel($x, $y).ToArgb()) { $diff++ }
                }
            }
            Assert-True ($diff -gt 100) "Light and dark empty states must not reuse the same pixels"
        }
        finally {
            foreach ($bmp in $bitmaps.Values) { if ($bmp) { $bmp.Dispose() } }
        }
    }

    It "round-trips an imported note image through the editor and keeps the content-addressed token" {
        $imagesType = Get-DeskmateType "TaskNoteImages"
        $editorType = Get-DeskmateType "TaskNoteEditor"
        $bytes = New-TestPngBytes
        try {
            $source = $imagesType.GetMethod("Decode", $flags).Invoke($null, @(,[byte[]]$bytes))
            $token = [string]$imagesType.GetMethod("Import", $flags).Invoke($null, @($source))
            Assert-True ($token -match '^!\[\]\(deskmate-image:[a-f0-9]{64}\)$') "Import must return a deskmate-image markdown token"
            $hash = [regex]::Match($token, 'deskmate-image:([a-f0-9]{64})').Groups[1].Value
            $cache = Join-Path (Join-Path $env:DESKMATE_DIR "task-images") ($hash + ".png")
            Assert-True (Test-Path -LiteralPath $cache) "Imported image must be cached on disk"

            $editor = [Activator]::CreateInstance($editorType, $flags, $null, @(), $null)
            try {
                $markdown = "Catatan`r`n`r`n$token`r`n"
                [void](Invoke-Declared $editor "LoadMarkdown" @($markdown))
                $rich = Read-DeclaredField $editor "editor"
                $para = New-Object System.Windows.Documents.Paragraph
                $run = New-Object System.Windows.Documents.Run "Tambahan setelah gambar"
                $para.Inlines.Add($run)
                $rich.Document.Blocks.Add($para)
                $saved = [string](Invoke-Declared $editor "SaveMarkdown" @($markdown))
                Assert-True ($saved.Contains($token)) "Saving an edited note must preserve the image reference"
                Assert-True ($saved.Contains("Tambahan setelah gambar")) "Saving must keep text added after the image"
            }
            finally { $editor.Dispose() }
        }
        finally {
            if ($cache) { Remove-Item -LiteralPath $cache -Force -ErrorAction SilentlyContinue }
        }
    }

    It "renders the localized offline placeholder when the image cache file is missing" {
        $imagesType = Get-DeskmateType "TaskNoteImages"
        $bytes = New-TestPngBytes
        $cache = $null
        try {
            $source = $imagesType.GetMethod("Decode", $flags).Invoke($null, @(,[byte[]]$bytes))
            $token = [string]$imagesType.GetMethod("Import", $flags).Invoke($null, @($source))
            $hash = [regex]::Match($token, 'deskmate-image:([a-f0-9]{64})').Groups[1].Value
            $cache = Join-Path (Join-Path $env:DESKMATE_DIR "task-images") ($hash + ".png")
            Remove-Item -LiteralPath $cache -Force

            $container = $imagesType.GetMethod("Inline", $flags).Invoke($null, @([string]$token, $null))
            Assert-NotNull $container "Offline Inline must still return a container"
            Assert-Equal $token $container.Tag "Offline container must retain the original token"
            $child = $container.Child
            Assert-NotNull $child "Offline container must have a placeholder child"
            Assert-Equal "TextBlock" $child.GetType().Name "Offline placeholder must be a TextBlock"
            Assert-True ($child.Text.Contains("Gambar belum tersedia")) "Offline placeholder must explain the missing image"
        }
        finally {
            if ($cache) { Remove-Item -LiteralPath $cache -Force -ErrorAction SilentlyContinue }
        }
    }
}
