# test_dock_shelf.ps1
# Dock shelf: anything dropped on the dock is held in a tray slot, ready to be dragged into another app.

$commonDir = Join-Path (Split-Path -Parent $PSScriptRoot) "common"
. (Join-Path $commonDir "TestHarness.ps1")
. (Join-Path $commonDir "DeskmateLoader.ps1")

$shelfTmp = Join-Path ([System.IO.Path]::GetTempPath()) ("deskmate-dockshelf-" + [Guid]::NewGuid().ToString("N"))
[void][System.IO.Directory]::CreateDirectory($shelfTmp)
$env:DESKMATE_DIR = $shelfTmp
$env:DESKMATE_DOCK_DRY = "1"

Describe "Dock shelf: hold on drop, drag out to another app" {

    $flags = [System.Reflection.BindingFlags]'Public,NonPublic,Static,Instance'
    $islandType = Get-DeskmateType "Island"
    $dwType = Get-DeskmateType "DockWindow"

    function New-SampleFile([string]$name) {
        $f = Join-Path $shelfTmp $name
        [System.IO.File]::WriteAllText($f, "sample")
        return $f
    }

    function Get-SlotIds($dw) {
        $ids = @()
        foreach ($s in $dwType.GetField("slots", $flags).GetValue($dw)) { $ids += $s.Id }
        return ,$ids
    }

    function Step-Dock($dw, [int]$frames = 90) {
        $step = $dwType.GetMethod("Step", $flags)
        for ($i = 0; $i -lt $frames; $i++) { [void]$step.Invoke($dw, @([single]0.016)) }
        [void]$dwType.GetMethod("Compose", $flags).Invoke($dw, $null)
    }

    It "1. DropPaths passes files through and turns text and pictures into files" {
        $drop = $dwType.GetMethod("DropPaths", $flags)
        $f = New-SampleFile "laporan.pdf"
        $files = New-Object System.Windows.Forms.DataObject
        $files.SetData([System.Windows.Forms.DataFormats]::FileDrop, [string[]]@($f))
        $out = $drop.Invoke($null, [object[]]@([System.Windows.Forms.IDataObject]$files))
        Assert-Equal $out.Count 1 "One dropped file stays one path"
        Assert-Equal $out[0] $f "A dropped file keeps its own path"

        $text = New-Object System.Windows.Forms.DataObject
        $text.SetData([System.Windows.Forms.DataFormats]::UnicodeText, "halo dari dock")
        $out = $drop.Invoke($null, [object[]]@([System.Windows.Forms.IDataObject]$text))
        Assert-Equal $out.Count 1 "Text becomes one file"
        Assert-True ($out[0].EndsWith(".txt")) "Text is saved as .txt"
        Assert-Equal ([System.IO.File]::ReadAllText($out[0])) "halo dari dock" "Saved text keeps its content"

        $bmp = New-Object System.Drawing.Bitmap 8, 8
        try {
            $pic = New-Object System.Windows.Forms.DataObject
            $pic.SetData([System.Windows.Forms.DataFormats]::Bitmap, $bmp)
            $out = $drop.Invoke($null, [object[]]@([System.Windows.Forms.IDataObject]$pic))
            Assert-Equal $out.Count 1 "A picture becomes one file"
            Assert-True ($out[0].EndsWith(".png")) "A picture is saved as .png"
            Assert-True ((Get-Item $out[0]).Length -gt 0) "Saved picture is not empty"
        } finally { $bmp.Dispose() }

        $holdable = $dwType.GetMethod("Holdable", $flags)
        Assert-True ($holdable.Invoke($null, [object[]]@([System.Windows.Forms.IDataObject]$text))) "Text drags are holdable"
        Assert-False ($holdable.Invoke($null, [object[]]@([System.Windows.Forms.IDataObject](New-Object System.Windows.Forms.DataObject)))) "Empty drags are not holdable"
    }

    It "2. HoldInDock keeps safe paths once, newest first, and refuses links" {
        $isle = [Activator]::CreateInstance($islandType, $true)
        $held = $islandType.GetField("dockHeld", $flags).GetValue($isle)
        $hold = $islandType.GetMethod("HoldInDock", $flags)
        $a = New-SampleFile "a.txt"; $b = New-SampleFile "b.txt"; $lnk = New-SampleFile "c.lnk"
        $hold.Invoke($isle, @(,[string[]]@($a, $b, $lnk)))
        Assert-Equal $held.Count 2 ".lnk files are refused"
        Assert-Equal $held[0] $b "Newest drop comes first"
        $hold.Invoke($isle, @(,[string[]]@($a)))
        Assert-Equal $held.Count 2 "A file held twice is held once"
        Assert-Equal $held[0] $a "A re-dropped file moves to the front"
        $islandType.GetMethod("ReleaseDockHeld", $flags).Invoke($isle, @(,[string[]]@($a)))
        Assert-Equal $held.Count 1 "Releasing a delivered file removes it"
    }

    It "3. The tray slot appears while something is dragged over or held, and leaves after" {
        $isle = [Activator]::CreateInstance($islandType, $true)
        $dw = [Activator]::CreateInstance($dwType, $flags, $null, @($isle, [System.Windows.Forms.Screen]::PrimaryScreen, $true), $null)
        try {
            $h = $dw.Handle
            Step-Dock $dw
            Assert-False ((Get-SlotIds $dw) -contains "shelf") "No tray slot while nothing is held"

            $dwType.GetField("fileOver", $flags).SetValue($dw, $true)
            Step-Dock $dw
            Assert-True ((Get-SlotIds $dw) -contains "shelf") "A drag over the dock brings the tray slot"
            $dwType.GetField("fileOver", $flags).SetValue($dw, $false)

            $f = New-SampleFile "kirim.docx"
            $islandType.GetMethod("HoldInDock", $flags).Invoke($isle, @(,[string[]]@($f)))
            Step-Dock $dw
            Assert-True ((Get-SlotIds $dw) -contains "shelf") "Held files keep the tray slot"
            $label = $dwType.GetMethod("ShelfLabel", $flags).Invoke($dw, $null)
            Assert-True ($label.Contains("kirim.docx")) "Label names the held file (got '$label')"

            $islandType.GetMethod("ReleaseDockHeld", $flags).Invoke($isle, @(,[string[]]@($f)))
            Step-Dock $dw
            Assert-False ((Get-SlotIds $dw) -contains "shelf") "Tray slot leaves once everything is delivered"

            $isle.GetType().GetField("settings", $flags).GetValue($isle).Set("dockbar.shelf", $false)
            $dwType.GetField("fileOver", $flags).SetValue($dw, $true)
            Step-Dock $dw
            Assert-False ((Get-SlotIds $dw) -contains "shelf") "Turned off in settings: no tray slot"
        }
        finally { $dw.Shutdown() }
    }
}

try { Remove-Item -Recurse -Force $shelfTmp } catch { }
