# test_s09_color_loupe.ps1
# Tier 4 scenario S09: color picker loupe sampling and precision reticle.

$commonDir = Join-Path (Split-Path -Parent $PSScriptRoot) "common"
. (Join-Path $commonDir "TestHarness.ps1")
. (Join-Path $commonDir "DeskmateLoader.ps1")

Describe "Tier 4 Scenario: S09 Color Loupe" {
    $flags = [System.Reflection.BindingFlags]'Public,NonPublic,Instance,Static'

    BeforeEach {
        Reset-DeskmateTheme -Light $false
    }

    It "renders the loupe on token surfaces with a dual-stroke reticle and a visible hex readout" {
        $loupeType = Get-DeskmateType "PickerOverlay+Loupe"
        Assert-NotNull $loupeType "PickerOverlay+Loupe must resolve from the compiled assembly"
        $loupe = [Activator]::CreateInstance($loupeType)
        $sample = New-Object System.Drawing.Bitmap 11, 11
        try {
            $g = [System.Drawing.Graphics]::FromImage($sample)
            try { $g.Clear([System.Drawing.Color]::FromArgb(255, 128, 128, 128)) }
            finally { $g.Dispose() }
            $show = $loupeType.GetMethods() | Where-Object { $_.Name -eq "Show" -and $_.GetParameters().Length -eq 4 } | Select-Object -First 1
            Assert-NotNull $show "Loupe.Show(Bitmap, Color, Point, int) must exist"
            $showArgs = [object[]]::new(4)
            $showArgs[0] = $sample.psobject.BaseObject
            $showArgs[1] = [System.Drawing.Color]::FromArgb(255, 128, 128, 128)
            $showArgs[2] = (New-Object System.Drawing.Point 100, 100).psobject.BaseObject
            $showArgs[3] = 10
            $show.Invoke($loupe, $showArgs)
            $bmp = Render-DeskmateControlToBitmap -Control $loupe -Width $loupe.Width -Height $loupe.Height
            try {
                $window = Get-DeskmateStaticProperty "KitInk" "Window"
                $text = Get-DeskmateStaticProperty "KitInk" "Text"
                Assert-True ((Get-DeskmateContrast $text $window) -ge 4.5) "Loupe text must stay readable on the token surface"
                $corner = $bmp.GetPixel(3, 3)
                Assert-True ([Math]::Abs($corner.R - $window.R) -le 3 -and [Math]::Abs($corner.G - $window.G) -le 3 -and [Math]::Abs($corner.B - $window.B) -le 3) "Loupe background must be KitInk.Window"
                $swatch = $bmp.GetPixel(8, 124)
                Assert-True ([Math]::Abs($swatch.R - 128) -le 8 -and [Math]::Abs($swatch.G - 128) -le 8 -and [Math]::Abs($swatch.B - 128) -le 8) "The sampled color swatch must show the picked grey"

                # The reticle is a dark outer stroke plus a white inner stroke. Scan the
                # whole 10px zoom cell so both strokes are observed regardless of AA phase.
                $mid = 6 + 5 * 10
                $minReticle = 255
                $maxReticle = 0
                for ($y = $mid - 3; $y -le $mid + 12; $y++) {
                    for ($x = $mid - 3; $x -le $mid + 12; $x++) {
                        $c = $bmp.GetPixel($x, $y)
                        $v = [Math]::Min($c.R, [Math]::Min($c.G, $c.B))
                        if ($v -lt $minReticle) { $minReticle = $v }
                        $v = [Math]::Max($c.R, [Math]::Max($c.G, $c.B))
                        if ($v -gt $maxReticle) { $maxReticle = $v }
                    }
                }
                Assert-True ($minReticle -lt 150) "Outer crosshair stroke must be visibly dark (min=$minReticle)"
                Assert-True ($maxReticle -gt 220) "Inner crosshair stroke must be visibly light (max=$maxReticle)"

                $readoutPixels = 0
                for ($y = 120; $y -lt [Math]::Min($bmp.Height, 145); $y++) {
                    for ($x = 20; $x -lt $bmp.Width - 2; $x++) {
                        $c = $bmp.GetPixel($x, $y)
                        if ($c.R -ne $window.R -or $c.G -ne $window.G -or $c.B -ne $window.B) { $readoutPixels++ }
                    }
                }
                Assert-True ($readoutPixels -gt 20) "Hex readout must render visible ink beside the swatch"
            }
            finally { $bmp.Dispose() }
        }
        finally {
            $sample.Dispose()
            $loupe.Dispose()
        }
    }
}
