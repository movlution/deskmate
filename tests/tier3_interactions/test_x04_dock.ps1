# test_x04_dock.ps1
# Tier 3 pairwise: dock badges and specular rim x semantic tokens and theme.

$commonDir = Join-Path (Split-Path -Parent $PSScriptRoot) "common"
. (Join-Path $commonDir "TestHarness.ps1")
. (Join-Path $commonDir "DeskmateLoader.ps1")

Describe "Tier 3 Pairwise: Dock x Theme" {
    $flags = [System.Reflection.BindingFlags]'Public,NonPublic,Instance,Static'
    $islandType = Get-DeskmateType "Island"
    $dockType = Get-DeskmateType "DockWindow"
    $painterType = Get-DeskmateType "Painter"
    $toneType = Get-DeskmateType "Tone"

    function New-TestDock {
        $dock = [Activator]::CreateInstance($dockType, $flags, $null, @($null), $null)
        $painter = [Activator]::CreateInstance($painterType, $true)
        $dockType.GetField("p", $flags).SetValue($dock, $painter)
        return @{ Dock = $dock; Painter = $painter }
    }

    function Set-PainterSurface($painter, $bitmap) {
        $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
        $painterType.GetField("G", $flags).SetValue($painter, $graphics)
        $painterType.GetField("T", $flags).SetValue($painter, (Get-DeskmateStaticField "Ink" "Base"))
        $painterType.GetField("Alpha", $flags).SetValue($painter, [single]1)
        $painterType.GetField("HalfPx", $flags).SetValue($painter, [single]0.5)
        return $graphics
    }

    function Invoke-Badge($dock, $painter, $graphics, $count, $rect) {
        $bmp = New-Object System.Drawing.Bitmap 180, 100
        $g = [System.Drawing.Graphics]::FromImage($bmp)
        $painterType.GetField("G", $flags).SetValue($painter, $g)
        $method = $dockType.GetMethod("Badge", $flags)
        [void]$method.Invoke($dock, @([System.Drawing.RectangleF]$rect, [int]$count))
        return $bmp
    }

    BeforeEach {
        Reset-DeskmateTheme -Light $false
    }

    It "renders unread badges with the Negative pill and clamps every count above 99 to the same 99+ width" {
        $parts = New-TestDock
        $dock = $parts.Dock
        $painter = $parts.Painter
        try {
            $bitmap = Invoke-Badge $dock $painter $null 7 (New-Object System.Drawing.RectangleF 60, 34, 34, 34)
            try {
                $negative = (Get-DeskmateStaticField "Ink" "Base").Negative
                $count = 0
                for ($y = 0; $y -lt $bitmap.Height; $y++) {
                    for ($x = 0; $x -lt $bitmap.Width; $x++) {
                        $c = $bitmap.GetPixel($x, $y)
                        if ($c.A -eq 255 -and [Math]::Abs($c.R - $negative.R) -lt 12 -and [Math]::Abs($c.G - $negative.G) -lt 12 -and [Math]::Abs($c.B - $negative.B) -lt 12) { $count++ }
                    }
                }
                Assert-True ($count -gt 40) "Negative badge fill must cover the badge area (pixels=$count)"
            }
            finally { $bitmap.Dispose() }

            $b100 = Invoke-Badge $dock $painter $null 100 (New-Object System.Drawing.RectangleF 60, 34, 34, 34)
            $b9999 = Invoke-Badge $dock $painter $null 9999 (New-Object System.Drawing.RectangleF 60, 34, 34, 34)
            try {
                $negative = (Get-DeskmateStaticField "Ink" "Base").Negative
                $bounds100 = @{ Min = 9999; Max = -1 }
                $bounds9999 = @{ Min = 9999; Max = -1 }
                foreach ($pair in @(@($b100, $bounds100), @($b9999, $bounds9999))) {
                    $bmp = $pair[0]; $bounds = $pair[1]
                    for ($y = 0; $y -lt $bmp.Height; $y++) {
                        for ($x = 0; $x -lt $bmp.Width; $x++) {
                            $c = $bmp.GetPixel($x, $y)
                            if ($c.A -eq 255 -and [Math]::Abs($c.R - $negative.R) -lt 12 -and [Math]::Abs($c.G - $negative.G) -lt 12 -and [Math]::Abs($c.B - $negative.B) -lt 12) {
                                if ($x -lt $bounds.Min) { $bounds.Min = $x }
                                if ($x -gt $bounds.Max) { $bounds.Max = $x }
                            }
                        }
                    }
                }
                Assert-True ($bounds100.Min -ge 0 -and $bounds100.Max -gt $bounds100.Min) "The 100 badge must render a pill"
                Assert-Equal ($bounds100.Max - $bounds100.Min) ($bounds9999.Max - $bounds9999.Min) "100 and 9999 must both clamp to the same 99+ pill width"
            }
            finally { $b100.Dispose(); $b9999.Dispose() }
        }
        finally { $dock.Dispose() }
    }

    It "switches badge ink to Bg in dark mode and White in light mode while keeping 4.5:1 on Negative" {
        foreach ($light in @($false, $true)) {
            Reset-DeskmateTheme -Light $light
            $parts = New-TestDock
            $dock = $parts.Dock
            $painter = $parts.Painter
            try {
                $bmp = Invoke-Badge $dock $painter $null 42 (New-Object System.Drawing.RectangleF 64, 36, 32, 32)
                try {
                    $negative = (Get-DeskmateStaticField "Ink" "Base").Negative
                    $expected = if ($light) { [System.Drawing.Color]::White } else { (Get-DeskmateStaticField "Ink" "Base").Bg }
                    $closest = 999.0
                    for ($y = 0; $y -lt $bmp.Height; $y++) {
                        for ($x = 0; $x -lt $bmp.Width; $x++) {
                            $c = $bmp.GetPixel($x, $y)
                            if ($c.A -ne 255) { continue }
                            $d = [Math]::Sqrt([Math]::Pow($c.R - $expected.R, 2) + [Math]::Pow($c.G - $expected.G, 2) + [Math]::Pow($c.B - $expected.B, 2))
                            if ($d -lt $closest) { $closest = $d }
                        }
                    }
                    Assert-True ($closest -lt 24) "Badge ink in light=$light must use $(if ($light) { 'White' } else { 'Tone.Bg' }); closest=$closest"
                    $ratio = Get-DeskmateContrast $expected $negative
                    Assert-True ($ratio -ge 4.5) "Badge ink contrast in light=$light is $ratio, expected >= 4.5"
                }
                finally { $bmp.Dispose() }
            }
            finally { $dock.Dispose() }
        }
    }

    It "draws the rim with the Highlight token and raises its alpha on light glass" {
        $parts = New-TestDock
        $dock = $parts.Dock
        $painter = $parts.Painter
        $bitmaps = New-Object System.Collections.ArrayList
        try {
            foreach ($light in @($false, $true)) {
                Reset-DeskmateTheme -Light $light
                $tone = $toneType.GetMethod($(if ($light) { "Paper" } else { "Dark" }), $flags).Invoke($null, @())
                $tone.Highlight = if ($light) { [System.Drawing.Color]::FromArgb(255, 40, 220, 80) } else { [System.Drawing.Color]::FromArgb(255, 230, 50, 60) }
                $bmp = New-Object System.Drawing.Bitmap 160, 80
                $path = New-Object System.Drawing.Drawing2D.GraphicsPath
                try {
                    $path.AddRectangle((New-Object System.Drawing.RectangleF 10, 10, 140, 50))
                    $g = [System.Drawing.Graphics]::FromImage($bmp)
                    try {
                        $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
                        $painterType.GetField("G", $flags).SetValue($painter, $g)
                        $dockType.GetField("g", $flags).SetValue($dock, $g)
                        $painterType.GetField("T", $flags).SetValue($painter, $tone)
                        $painterType.GetField("Alpha", $flags).SetValue($painter, [single]1)
                        [void]$dockType.GetMethod("RimLight", $flags).Invoke($dock, @([System.Drawing.RectangleF](New-Object System.Drawing.RectangleF 10, 10, 140, 50), [System.Drawing.Drawing2D.GraphicsPath]$path))
                    }
                    finally { $g.Dispose() }
                    $maxA = 0; $peak = [System.Drawing.Color]::Black
                    for ($y = 0; $y -lt $bmp.Height; $y++) {
                        for ($x = 0; $x -lt $bmp.Width; $x++) {
                            $c = $bmp.GetPixel($x, $y)
                            if ($c.A -gt $maxA) { $maxA = $c.A; $peak = $c }
                        }
                    }
                    Assert-True ($maxA -gt 20) "RimLight must draw a visible line in light=$light"
                    if ($light) {
                        Assert-True ($peak.G -gt $peak.R -and $peak.G -gt $peak.B) "Light rim must carry the green Highlight channel"
                        Assert-True ($maxA -ge 130) "Light rim alpha $maxA must use the 150 strength"
                    }
                    else {
                        Assert-True ($peak.R -gt $peak.G -and $peak.R -gt $peak.B) "Dark rim must carry the red Highlight channel"
                        Assert-True ($maxA -le 110) "Dark rim alpha $maxA must use the 92 strength"
                    }
                    [void]$bitmaps.Add($bmp)
                    $bmp = $null
                }
                finally {
                    $path.Dispose()
                    if ($bmp) { $bmp.Dispose() }
                }
            }
        }
        finally {
            foreach ($bmp in $bitmaps) { $bmp.Dispose() }
            $dock.Dispose()
        }
    }
}

