# test_x07_dock_edge_hover.ps1
# Tier 3: a resting pointer on the screen edge keeps a floating dock up.

$commonDir = Join-Path (Split-Path -Parent $PSScriptRoot) "common"
. (Join-Path $commonDir "TestHarness.ps1")
. (Join-Path $commonDir "DeskmateLoader.ps1")

Describe "Tier 3: Dock edge hover" {
    $flags = [System.Reflection.BindingFlags]'Public,NonPublic,Instance,Static'
    $dockType = Get-DeskmateType "DockWindow"
    $painterType = Get-DeskmateType "Painter"

    It "1. treats the transparent gap above the screen edge as part of a floating dock" {
        $band = $dockType.GetMethod("InEdgeBand", $flags)
        Assert-True ($null -ne $band) "DockWindow.InEdgeBand must exist"

        $monitor = New-Object System.Drawing.Rectangle 0, 0, 1920, 1080
        $onEdge = New-Object System.Drawing.Point 960, 1078
        $justAbove = New-Object System.Drawing.Point 960, 1073
        $besideEdge = New-Object System.Drawing.Point 1920, 1078

        Assert-True ([bool]$band.Invoke($null, @($monitor, [single]1, [single]6, $onEdge))) "A pointer in the 6 px bottom gap must count as dock hover"
        Assert-False ([bool]$band.Invoke($null, @($monitor, [single]1, [single]6, $justAbove))) "A pointer above the gap must not count as edge hover"
        Assert-False ([bool]$band.Invoke($null, @($monitor, [single]1, [single]6, $besideEdge))) "Another monitor at the same Y must not keep this dock up"
    }

    It "2. scales the hover band with DPI and keeps a minimal 2 px edge for full-width bars" {
        $band = $dockType.GetMethod("InEdgeBand", $flags)
        $monitor = New-Object System.Drawing.Rectangle 1920, 0, 1920, 1080
        $scaledEdge = New-Object System.Drawing.Point 2880, 1071
        $scaledAbove = New-Object System.Drawing.Point 2880, 1070
        $fullEdge = New-Object System.Drawing.Point 2880, 1078
        $fullAbove = New-Object System.Drawing.Point 2880, 1077

        Assert-True ([bool]$band.Invoke($null, @($monitor, [single]1.5, [single]6, $scaledEdge))) "At 150%, the 6 DIP gap must cover 9 physical pixels"
        Assert-False ([bool]$band.Invoke($null, @($monitor, [single]1.5, [single]6, $scaledAbove))) "At 150%, the pixel above the gap must stay outside"
        Assert-True ([bool]$band.Invoke($null, @($monitor, [single]1, [single]0, $fullEdge))) "A full-width bar must still own a 2 px edge"
        Assert-False ([bool]$band.Invoke($null, @($monitor, [single]1, [single]0, $fullAbove))) "The 2 px edge must not grow into the bar"
    }

    It "3. renders an invisible hit strip at the bottom while the bar is shown" {
        $dock = [Activator]::CreateInstance($dockType, $flags, $null, @($null), $null)
        $bitmap = New-Object System.Drawing.Bitmap 200, 120, ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
        $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
        try {
            $dockType.GetField("scale", $flags).SetValue($dock, [single]1)
            $dockType.GetField("cw", $flags).SetValue($dock, 200)
            $dockType.GetField("ch", $flags).SetValue($dock, 120)
            $dockType.GetField("g", $flags).SetValue($dock, $graphics)
            $painter = $dockType.GetField("p", $flags).GetValue($dock)
            $painterType.GetField("G", $flags).SetValue($painter, $graphics)

            [void]$dockType.GetMethod("DrawEdgeHit", $flags).Invoke($dock, @())

            $hits = $dockType.GetField("hits", $flags).GetValue($dock)
            $sensor = $null
            foreach ($hit in $hits) {
                if ($hit.Value -eq "sensor") { $sensor = $hit.Key; break }
            }
            Assert-True ($null -ne $sensor) "The shown dock must register a sensor hit strip"
            Assert-Equal 114 ([double]$sensor.Top) "The strip must start above the 6 px bottom gap"
            Assert-Equal 6 ([double]$sensor.Height) "The strip must cover the full 6 px gap"
            Assert-Equal 1 ([int]$bitmap.GetPixel(100, 119).A) "The strip must be hit-testable without becoming visible"
            Assert-Equal 0 ([int]$bitmap.GetPixel(100, 113).A) "The strip must not paint above the gap"
        }
        finally {
            $graphics.Dispose()
            $bitmap.Dispose()
            $dock.Dispose()
        }
    }
}
