# test_s05_liquid_glass.ps1
# Tier 4 scenario S05: liquid-glass pass under extreme luminance.

$commonDir = Join-Path (Split-Path -Parent $PSScriptRoot) "common"
. (Join-Path $commonDir "TestHarness.ps1")
. (Join-Path $commonDir "DeskmateLoader.ps1")

Describe "Tier 4 Scenario: S05 Liquid Glass" {
    $flags = [System.Reflection.BindingFlags]'Public,NonPublic,Instance,Static'
    $islandType = Get-DeskmateType "Island"

    BeforeEach {
        Reset-DeskmateTheme -Light $false
    }

    It "preserves light-mode target luminance and keeps gradient edges vivid without clipping" {
        $liquid = $islandType.GetMethod("LiquidPass", $flags)
        foreach ($light in @($false, $true)) {
            Reset-DeskmateTheme -Light $light
            $w = 24; $h = 18
            $bmp = New-Object System.Drawing.Bitmap $w, $h, ([System.Drawing.Imaging.PixelFormat]::Format32bppRgb)
            try {
                $raw = New-Object byte[] ($w * $h * 4)
                for ($y = 0; $y -lt $h; $y++) {
                    for ($x = 0; $x -lt $w; $x++) {
                        $i = ($y * $w + $x) * 4
                        $raw[$i] = [byte](20 + ($x * 9) % 220)
                        $raw[$i + 1] = [byte](30 + ($y * 11) % 200)
                        $raw[$i + 2] = [byte](40 + (($x + $y) * 7) % 210)
                        $raw[$i + 3] = 255
                    }
                }
                $base = Get-DeskmateStaticField "Ink" "Base"
                $liquid.Invoke($null, @([System.Drawing.Bitmap]$bmp, [byte[]]$raw, [System.Drawing.Color]$base.Bg, [bool]$light))

                $cx = [int]($w / 2); $cy = [int]($h / 2)
                $centre = $bmp.GetPixel($cx, $cy)
                $luma = Get-WcagLuminance $centre
                Assert-Equal 255 $centre.A "Liquid output must be opaque"
                if ($light) {
                    Assert-True ($luma -ge 0.62) "Light liquid centre luma $luma must be >= 0.62"
                }
                else {
                    Assert-True ($luma -le 0.18) "Dark liquid centre luma $luma must be <= 0.18"
                }

                $vivid = 0
                $clipped = 0
                for ($y = 0; $y -lt $h; $y++) {
                    for ($x = 0; $x -lt $w; $x++) {
                        $c = $bmp.GetPixel($x, $y)
                        $spread = [Math]::Max($c.R, [Math]::Max($c.G, $c.B)) - [Math]::Min($c.R, [Math]::Min($c.G, $c.B))
                        if ($spread -ge 10) { $vivid++ }
                        if (($c.R -eq 0 -and $c.G -eq 0 -and $c.B -eq 0) -or ($c.R -eq 255 -and $c.G -eq 255 -and $c.B -eq 255)) { $clipped++ }
                    }
                }
                $total = $w * $h
                Assert-True ($vivid -ge [int]($total * 0.25)) "At least 25% of liquid pixels must retain visible chroma (got $vivid/$total)"
                Assert-True ($clipped -le [int]($total * 0.08)) "Liquid pass must not hard-clip more than 8% of pixels (got $clipped/$total)"
            }
            finally {
                $bmp.Dispose()
            }
        }
    }
}
