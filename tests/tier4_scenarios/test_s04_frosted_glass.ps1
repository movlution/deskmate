# test_s04_frosted_glass.ps1
# Tier 4 scenario S04: frosted glass HUD over extreme wallpapers.

$commonDir = Join-Path (Split-Path -Parent $PSScriptRoot) "common"
. (Join-Path $commonDir "TestHarness.ps1")
. (Join-Path $commonDir "DeskmateLoader.ps1")

Describe "Tier 4 Scenario: S04 Frosted Glass" {
    $flags = [System.Reflection.BindingFlags]'Public,NonPublic,Instance,Static'
    $islandType = Get-DeskmateType "Island"
    $inkType = Get-DeskmateType "Ink"
    $painterType = Get-DeskmateType "Painter"

    BeforeEach {
        Reset-DeskmateTheme -Light $false
    }

    It "keeps on-glass ink readable over white and black wallpapers and caps elevation opacity" {
        $computeTint = $islandType.GetMethod("ComputeFrostTint", $flags)
        $over = $inkType.GetMethod("Over", $flags)
        $inkA = $inkType.GetMethod("A", $flags)
        $wallpapers = @(
            @{ Name = "white"; Color = [System.Drawing.Color]::White; Light = $false; MinTint = 0.66 },
            @{ Name = "black"; Color = [System.Drawing.Color]::Black; Light = $true; MinTint = 0.83 }
        )
        foreach ($wall in $wallpapers) {
            Reset-DeskmateTheme -Light $wall.Light
            $sig = New-Object byte[] 64
            for ($i = 0; $i -lt $sig.Length; $i += 4) {
                $sig[$i] = $wall.Color.B; $sig[$i + 1] = $wall.Color.G; $sig[$i + 2] = $wall.Color.R; $sig[$i + 3] = 255
            }
            $tint = [float]$computeTint.Invoke($null, @([byte[]]$sig, [int]$sig.Length, [System.Drawing.Color](Get-DeskmateStaticField "Ink" "Base").Bg, [bool]$wall.Light))
            Assert-True ($tint -ge $wall.MinTint) "Frost tint over $($wall.Name) is $tint, expected >= $($wall.MinTint)"

            $base = Get-DeskmateStaticField "Ink" "Base"
            $surface = $over.Invoke($null, @($inkA.Invoke($null, @($base.Bg, $tint)), $wall.Color))
            $glass = (Get-DeskmateType "Tone").GetMethod("OnGlass", $flags).Invoke($base, @())
            $textRatio = Get-DeskmateContrast $glass.Text $surface
            $subRatio = Get-DeskmateContrast ($over.Invoke($null, @($glass.Sub, $surface))) $surface
            Assert-True ($textRatio -ge 4.5) "On-glass text over $($wall.Name) is $textRatio, expected >= 4.5:1"
            Assert-True ($subRatio -ge 4.5) "On-glass secondary text over $($wall.Name) is $subRatio, expected >= 4.5:1"
        }

        $shadowLayers = $islandType.GetMethod("ShadowLayers", $flags)
        $painter = [Activator]::CreateInstance($painterType, $true)
        $bmp = New-Object System.Drawing.Bitmap 200, 120
        $g = [System.Drawing.Graphics]::FromImage($bmp)
        try {
            $painter.G = $g
            $painter.T = (Get-DeskmateStaticField "Ink" "Base")
            $painter.Alpha = 1
            $painter.HalfPx = 0.5
            [void]$shadowLayers.Invoke($null, @($painter, [System.Drawing.RectangleF](New-Object System.Drawing.RectangleF 50, 30, 100, 40), [single]20, [single]0.45))
            $maxAlpha = 0
            for ($y = 0; $y -lt $bmp.Height; $y++) {
                for ($x = 0; $x -lt $bmp.Width; $x++) {
                    $a = $bmp.GetPixel($x, $y).A
                    if ($a -gt $maxAlpha) { $maxAlpha = $a }
                }
            }
            Assert-True ($maxAlpha -gt 0) "The elevation shadow must render at least one visible layer"
            Assert-True ($maxAlpha -le 60) "Glass elevation shadow alpha $maxAlpha must stay at or below H=60"
        }
        finally {
            $g.Dispose()
            $bmp.Dispose()
            $painter.Dispose()
        }
    }
}
