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

    It "2. scales the hover band with DPI and keeps a minimal 6 DIP edge for full-width bars" {
        $band = $dockType.GetMethod("InEdgeBand", $flags)
        $monitor = New-Object System.Drawing.Rectangle 1920, 0, 1920, 1080
        $scaledEdge = New-Object System.Drawing.Point 2880, 1071
        $scaledAbove = New-Object System.Drawing.Point 2880, 1070
        $fullEdge = New-Object System.Drawing.Point 2880, 1074
        $fullAbove = New-Object System.Drawing.Point 2880, 1073

        Assert-True ([bool]$band.Invoke($null, @($monitor, [single]1.5, [single]6, $scaledEdge))) "At 150%, the 6 DIP gap must cover 9 physical pixels"
        Assert-False ([bool]$band.Invoke($null, @($monitor, [single]1.5, [single]6, $scaledAbove))) "At 150%, the pixel above the gap must stay outside"
        Assert-True ([bool]$band.Invoke($null, @($monitor, [single]1, [single]0, $fullEdge))) "A full-width bar must still own a 6 DIP edge for auto-hide"
        Assert-False ([bool]$band.Invoke($null, @($monitor, [single]1, [single]0, $fullAbove))) "The 6 DIP edge must not grow further into the bar"
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

    It "4. moves the whole shape on one calm, synchronized spring" {
        $animType = Get-DeskmateType "Anim"
        $reducedField = $animType.GetField("Reduced", $flags)
        $paceField = $animType.GetField("Pace", $flags)
        $bounceField = $animType.GetField("Bounce", $flags)
        $oldReduced = $reducedField.GetValue($null)
        $oldPace = $paceField.GetValue($null)
        $oldBounce = $bounceField.GetValue($null)
        $dock = [Activator]::CreateInstance($dockType, $flags, $null, @($null), $null)
        try {
            $reducedField.SetValue($null, $false)
            $paceField.SetValue($null, [single]1)
            $bounceField.SetValue($null, [single]1)

            $reveal = $dockType.GetField("reveal", $flags).GetValue($dock)
            $ready = $dockType.GetProperty("RevealReady", $flags)
            [void]$dockType.GetMethod("Reveal", $flags).Invoke($dock, @($true))
            $reveal.Value = 0
            $reveal.Velocity = 0
            $reveal.Target = 1

            Assert-False ([bool]$ready.GetValue($dock)) "Pointer effects must wait while the shape is still moving"

            $firstFrame = 0.0
            $rise90 = -1.0
            $peak = 0.0
            $maxStep = 0.0
            $previous = 0.0
            for ($i = 1; $i -le 60; $i++) {
                $reveal.Step([single](1.0 / 60.0))
                $v = [double]$reveal.Value
                $step = [Math]::Abs($v - $previous)
                if ($step -gt $maxStep) { $maxStep = $step }
                $previous = $v
                if ($i -eq 1) { $firstFrame = $v }
                if ($v -gt $peak) { $peak = $v }
                if ($rise90 -lt 0 -and $v -ge 0.90) { $rise90 = $i / 60.0 }
            }

            Assert-True ($firstFrame -ge 0.05 -and $firstFrame -le 0.09) "The rise must start without a lurch (first frame $([Math]::Round($firstFrame * 100, 1))%)"
            Assert-True ($rise90 -ge 0.15 -and $rise90 -le 0.20) "The whole shape must reach 90% calmly in 150-200 ms (got $([Math]::Round($rise90 * 1000)) ms)"
            Assert-True ($maxStep -le 0.16) "No single frame may jump more than 16% of the rise (peak step $([Math]::Round($maxStep * 100, 1))%)"
            Assert-True ($peak -le 1.005) "The rise must not visibly bounce past rest (peak $([Math]::Round($peak * 100, 1))%)"
            Assert-True ([Math]::Abs([double]$reveal.Value - 1.0) -le 0.002) "The rise must settle cleanly at rest"
            Assert-True ([bool]$ready.GetValue($dock)) "Pointer effects may begin only after the shape has arrived"

            [void]$dockType.GetMethod("Reveal", $flags).Invoke($dock, @($false))
            $reveal.Value = 1
            $reveal.Velocity = 0
            $reveal.Target = 0

            $hide90 = -1.0
            $floor = 1.0
            for ($i = 1; $i -le 60; $i++) {
                $reveal.Step([single](1.0 / 60.0))
                $v = [double]$reveal.Value
                if ($hide90 -lt 0 -and $v -le 0.10) { $hide90 = $i / 60.0 }
                if ($v -lt $floor) { $floor = $v }
            }

            Assert-True ($hide90 -ge 0.15 -and $hide90 -le 0.20) "The retract must use the same calm cadence (got $([Math]::Round($hide90 * 1000)) ms)"
            Assert-True ($floor -ge -0.002) "The retract must not dip below its resting edge"
            Assert-True ([Math]::Abs($rise90 - $hide90) -le 0.03) "Rise and retract must feel like one shape"
        }
        finally {
            $reducedField.SetValue($null, $oldReduced)
            $paceField.SetValue($null, $oldPace)
            $bounceField.SetValue($null, $oldBounce)
            $dock.Dispose()
        }
    }
}
