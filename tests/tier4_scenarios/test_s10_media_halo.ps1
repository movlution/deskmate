# test_s10_media_halo.ps1
# Tier 4 scenario S10: media halo breathing and on-glass legibility.

$commonDir = Join-Path (Split-Path -Parent $PSScriptRoot) "common"
. (Join-Path $commonDir "TestHarness.ps1")
. (Join-Path $commonDir "DeskmateLoader.ps1")

Describe "Tier 4 Scenario: S10 Media Halo" {
    $flags = [System.Reflection.BindingFlags]'Public,NonPublic,Instance,Static'
    $islandType = Get-DeskmateType "Island"
    $inkType = Get-DeskmateType "Ink"
    $painterType = Get-DeskmateType "Painter"
    $toneType = Get-DeskmateType "Tone"
    $animType = Get-DeskmateType "Anim"

    BeforeEach {
        Reset-DeskmateTheme -Light $false
    }

    function Get-DifferentPixelCount($a, $b) {
        $count = 0
        for ($y = 0; $y -lt $a.Height; $y++) {
            for ($x = 0; $x -lt $a.Width; $x++) {
                if ($a.GetPixel($x, $y).ToArgb() -ne $b.GetPixel($x, $y).ToArgb()) { $count++ }
            }
        }
        return $count
    }

    It "keeps on-glass ink readable over arbitrary wallpapers and keeps the halo subtle and stable in reduced motion" {
        $over = $inkType.GetMethod("Over", $flags)
        $inkA = $inkType.GetMethod("A", $flags)
        $onGlass = $toneType.GetMethod("OnGlass", $flags)
        foreach ($case in @(
            @{ Light = $false; Wall = [System.Drawing.Color]::White; Tint = 0.66 },
            @{ Light = $true; Wall = [System.Drawing.Color]::Black; Tint = 0.83 }
        )) {
            Reset-DeskmateTheme -Light $case.Light
            $base = Get-DeskmateStaticField "Ink" "Base"
            $surface = $over.Invoke($null, @($inkA.Invoke($null, @($base.Bg, [single]$case.Tint)), $case.Wall))
            $glass = $onGlass.Invoke($base, @())
            $textRatio = Get-DeskmateContrast $glass.Text $surface
            $sub = $over.Invoke($null, @($glass.Sub, $surface))
            $subRatio = Get-DeskmateContrast $sub $surface
            Assert-True ($textRatio -ge 4.5) "On-glass text over wallpaper light=$($case.Light) is $textRatio, expected >= 4.5:1"
            Assert-True ($subRatio -ge 4.5) "On-glass secondary over wallpaper light=$($case.Light) is $subRatio, expected >= 4.5:1"
        }

        $island = [System.Runtime.Serialization.FormatterServices]::GetUninitializedObject($islandType)
        $painter = [Activator]::CreateInstance($painterType, $true)
        $clock = New-Object System.Diagnostics.Stopwatch
        $clock.Start()
        $islandType.GetField("p", $flags).SetValue($island, $painter)
        $islandType.GetField("flip", $flags).SetValue($island, [single]1)
        $islandType.GetField("clock", $flags).SetValue($island, $clock)
        $painterType.GetField("Alpha", $flags).SetValue($painter, [single]1)
        $painterType.GetField("T", $flags).SetValue($painter, (Get-DeskmateStaticField "Ink" "Base"))
        $drawGlow = $islandType.GetMethod("DrawGlow", $flags)
        $reducedField = $animType.GetField("Reduced", $flags)
        $oldReduced = $reducedField.GetValue($null)

        function New-HaloFrame {
            param($island, $drawGlow, $islandType, $painter, $painterType, $flags, $width, $height)
            $bmp = New-Object System.Drawing.Bitmap $width, $height
            $g = [System.Drawing.Graphics]::FromImage($bmp)
            try {
                $g.Clear([System.Drawing.Color]::Transparent)
                $painterType.GetField("G", $flags).SetValue($painter, $g)
                [void]$drawGlow.Invoke($island, @([DateTime]::Now, [System.Drawing.RectangleF](New-Object System.Drawing.RectangleF 60, 35, 110, 40), [single]20))
            }
            finally { $g.Dispose() }
            return ,$bmp
        }

        $frames = New-Object System.Collections.ArrayList
        try {
            $reducedField.SetValue($null, $true)
            $r1 = New-HaloFrame $island $drawGlow $islandType $painter $painterType $flags 230 110
            Start-Sleep -Milliseconds 120
            $r2 = New-HaloFrame $island $drawGlow $islandType $painter $painterType $flags 230 110
            [void]$frames.Add($r1); [void]$frames.Add($r2)
            Assert-Equal 0 (Get-DifferentPixelCount $r1 $r2) "Reduced motion must hold the halo perfectly still"

            $maxAlpha = 0
            for ($y = 0; $y -lt $r1.Height; $y++) {
                for ($x = 0; $x -lt $r1.Width; $x++) {
                    $a = $r1.GetPixel($x, $y).A
                    if ($a -gt $maxAlpha) { $maxAlpha = $a }
                }
            }
            Assert-True ($maxAlpha -gt 0) "The waiting approval halo must render"
            Assert-True ($maxAlpha -lt 220) "The halo must remain translucent rather than becoming a solid banner; max alpha $maxAlpha"

            $reducedField.SetValue($null, $false)
            $m1 = New-HaloFrame $island $drawGlow $islandType $painter $painterType $flags 230 110
            Start-Sleep -Milliseconds 120
            $m2 = New-HaloFrame $island $drawGlow $islandType $painter $painterType $flags 230 110
            [void]$frames.Add($m1); [void]$frames.Add($m2)
            Assert-True ((Get-DifferentPixelCount $m1 $m2) -gt 0) "Normal motion must breathe the halo between frames"
        }
        finally {
            $reducedField.SetValue($null, $oldReduced)
            foreach ($frame in $frames) { $frame.Dispose() }
            $painter.Dispose()
        }
    }
}
