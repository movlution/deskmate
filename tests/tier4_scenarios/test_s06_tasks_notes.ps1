# test_s06_tasks_notes.ps1
# Tier 4 scenario S06: task/note lifecycle, semantic status ink, and empty state.

$commonDir = Join-Path (Split-Path -Parent $PSScriptRoot) "common"
. (Join-Path $commonDir "TestHarness.ps1")
. (Join-Path $commonDir "DeskmateLoader.ps1")
if (-not $env:DESKMATE_DIR -or -not $env:DESKMATE_DIR.StartsWith(
    (Join-Path ([IO.Path]::GetTempPath()) "Deskmate\tests\"), [StringComparison]::OrdinalIgnoreCase)) {
    throw "Run this suite via tests/run_e2e.ps1 so task files are isolated."
}
Add-Type -AssemblyName WindowsBase, PresentationCore, PresentationFramework, WindowsFormsIntegration

Describe "Tier 4 Scenario: S06 Task And Note Lifecycle" {
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
        $langType = Get-DeskmateType "Lang"
        if ($langType) { $langType.GetField("English", $flags).SetValue($null, $false) }
    }

    It "validates a task draft, marks overdue status in Negative ink, and renders a unified empty state" {
        $todoType = Get-DeskmateType "TodoItem"
        $editorType = Get-DeskmateType "TaskEditor"
        foreach ($light in @($false, $true)) {
            Reset-DeskmateTheme -Light $light
            $negative = (Get-DeskmateStaticField "Ink" "Base").Negative
            $window = Get-DeskmateStaticProperty "KitInk" "Window"

            $emptyItem = [Activator]::CreateInstance($todoType, $true)
            $editor = [Activator]::CreateInstance($editorType, $flags, $null, @($emptyItem), $null)
            try {
                [void](Invoke-Declared $editor "SaveDraft" @())
                $warning = Read-DeclaredField $editor "warning"
                Assert-Equal "Judul wajib diisi" $warning.Text "Empty save must show the title warning"
                Assert-Equal $negative.ToArgb() $warning.ForeColor.ToArgb() "Title warning must use Ink.Base.Negative"
                Assert-True ((Get-DeskmateContrast $warning.ForeColor $window) -ge 4.5) "Title warning contrast must be >= 4.5:1"
                (Read-DeclaredField $editor "title").Text = "Tugas baru"
                Assert-Equal "" $warning.Text "A valid title must clear the warning"
            }
            finally { $editor.Dispose() }

            $lateItem = [Activator]::CreateInstance($todoType, $true)
            $lateItem.Title = "Tugas terlambat"
            $lateItem.Due = [DateTime]::Now.AddDays(-2)
            $lateEditor = [Activator]::CreateInstance($editorType, $flags, $null, @($lateItem), $null)
            try {
                $lateWarning = Read-DeclaredField $lateEditor "warning"
                Assert-True ($lateWarning.Text.Length -gt 0) "Overdue task must show a due warning"
                Assert-Equal $negative.ToArgb() $lateWarning.ForeColor.ToArgb() "Overdue warning must use Ink.Base.Negative"
                Assert-True ((Get-DeskmateContrast $lateWarning.ForeColor $window) -ge 4.5) "Overdue warning contrast must be >= 4.5:1"
            }
            finally { $lateEditor.Dispose() }

            $view = New-DeskmateControl "TaskView"
            try {
                $open = New-EmptyList (Get-DeskmateType "TaskRow")
                $done = New-EmptyList $todoType
                $view.SetData($open, $done, $false, $true, "Belum ada tugas", "Tambahkan tugas baru")
                Assert-Equal 0 $view.RowCount "Empty task list must use the dedicated empty state, not a fake row"
                $bmp = Render-DeskmateControlToBitmap -Control $view -Width 380 -Height 220
                try {
                    $nonEmpty = 0
                    for ($y = 0; $y -lt $bmp.Height; $y += 3) {
                        for ($x = 0; $x -lt $bmp.Width; $x += 3) {
                            $c = $bmp.GetPixel($x, $y)
                            if ($c.R -ne $c.G -or $c.G -ne $c.B -or $c.R -gt 20) { $nonEmpty++ }
                        }
                    }
                    Assert-True ($nonEmpty -gt 20) "Empty task state must render its icon and typography (light=$light)"
                }
                finally { $bmp.Dispose() }
            }
            finally { $view.Dispose() }
        }
    }

    It "keeps an imported note image addressable and replaces a deleted cache file with the offline placeholder" {
        $imagesType = Get-DeskmateType "TaskNoteImages"
        $bytes = New-TestPngBytes
        $cache = $null
        try {
            $source = $imagesType.GetMethod("Decode", $flags).Invoke($null, @(,[byte[]]$bytes))
            $token = [string]$imagesType.GetMethod("Import", $flags).Invoke($null, @($source))
            $hash = [regex]::Match($token, 'deskmate-image:([a-f0-9]{64})').Groups[1].Value
            Assert-True ($hash.Length -eq 64) "Imported image must keep its content-addressed hash"
            $cache = Join-Path (Join-Path $env:DESKMATE_DIR "task-images") ($hash + ".png")
            Assert-True (Test-Path -LiteralPath $cache) "Imported image must be cached locally"

            Remove-Item -LiteralPath $cache -Force
            $container = $imagesType.GetMethod("Inline", $flags).Invoke($null, @([string]$token, $null))
            Assert-NotNull $container "Deleting the cache must still leave a rendered placeholder"
            Assert-Equal $token $container.Tag "The placeholder must retain the deleted image token"
            $child = $container.Child
            Assert-Equal "TextBlock" $child.GetType().Name "A missing cache file must render a text placeholder"
            Assert-True ($child.Text.Contains("Gambar belum tersedia")) "The placeholder must explain that the image is unavailable"
        }
        finally {
            if ($cache) { Remove-Item -LiteralPath $cache -Force -ErrorAction SilentlyContinue }
        }
    }
}
