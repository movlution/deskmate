# test_x02_accent_glass.ps1
# Tier 3 pairwise: adaptive frosted glass, liquid luminance guard, and palette floors.

$commonDir = Join-Path (Split-Path -Parent $PSScriptRoot) "common"
. (Join-Path $commonDir "TestHarness.ps1")
. (Join-Path $commonDir "DeskmateLoader.ps1")

Describe "Tier 3 Pairwise: Accent x Glass" {
    $flags = [System.Reflection.BindingFlags]'Public,NonPublic,Instance,Static'
    $islandType = Get-DeskmateType "Island"
    $inkType = Get-DeskmateType "Ink"
    $frostType = Get-DeskmateType "FrostJob"
    $toneType = Get-DeskmateType "Tone"
    $computeTint = $islandType.GetMethod("ComputeFrostTint", $flags)
    $derive = $islandType.GetMethod("DerivePalette", $flags)
    $liquid = $islandType.GetMethod("LiquidPass", $flags)
    $over = $inkType.GetMethod("Over", $flags)
    $inkA = $inkType.GetMethod("A", $flags)

    BeforeEach {
        Reset-DeskmateTheme -Light $false
    }

    It "keeps on-glass ink readable over pure white and pure black wallpapers" {
        $wallpapers = @(
            @{ Name = "white"; Color = [System.Drawing.Color]::White; MinTint = 0.66; Light = $false },
            @{ Name = "black"; Color = [System.Drawing.Color]::Black; MinTint = 0.83; Light = $true }
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
            $tintColor = $inkA.Invoke($null, @($base.Bg, $tint))
            $surface = $over.Invoke($null, @($tintColor, $wall.Color))
            $glass = $toneType.GetMethod("OnGlass", $flags).Invoke($base, @())
            $textRatio = Get-DeskmateContrast $glass.Text $surface
            $subSurface = $over.Invoke($null, @($glass.Sub, $surface))
            $subRatio = Get-DeskmateContrast $subSurface $surface
            Assert-True ($textRatio -ge 4.5) "On-glass primary ink over $($wall.Name) is $textRatio, expected >= 4.5"
            Assert-True ($subRatio -ge 4.5) "On-glass secondary ink over $($wall.Name) is $subRatio, expected >= 4.5"
        }
    }

    It "rebakes glass for theme and base changes while reusing an unchanged capture" {
        $job = [Activator]::CreateInstance($frostType, $true)
        $job.W = 100; $job.H = 80; $job.Left = 10; $job.Top = 20
        $job.Bg = [System.Drawing.Color]::FromArgb(255, 20, 24, 32)
        $job.Light = $false; $job.Liquid = $true; $job.Panel = $true
        $job.Extra = [single]0.55; $job.Sheen = 40
        $job.Bake = New-Object System.Drawing.Rectangle 0, 0, 100, 80

        $copy = [Activator]::CreateInstance($frostType, $true)
        foreach ($f in $frostType.GetFields($flags)) { if (-not $f.IsStatic) { $f.SetValue($copy, $f.GetValue($job)) } }
        Assert-True ($job.SameBase($copy)) "Identical base jobs must share a glass bake"
        Assert-True ($job.SameCapture($copy)) "Identical capture rectangles must be reusable"

        $copy.Light = $true
        Assert-False ($job.SameBase($copy)) "A light/dark flip must invalidate the base bake"
        $copy.Light = $false
        $copy.Bg = [System.Drawing.Color]::FromArgb(255, 240, 240, 240)
        Assert-False ($job.SameBase($copy)) "A background change must invalidate the base bake"

        $copy = [Activator]::CreateInstance($frostType, $true)
        foreach ($f in $frostType.GetFields($flags)) { if (-not $f.IsStatic) { $f.SetValue($copy, $f.GetValue($job)) } }
        $copy.W = 400; $copy.Left = 0
        Assert-False ($job.SameCapture($copy)) "A moved or resized capture must be fetched again"
        Assert-True ($job.SameBase($copy)) "Capture movement alone must not change the look bake"
    }

    It "keeps liquid-glass centre pixels dark in dark mode and light in light mode" {
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
                $pixel = $bmp.GetPixel($cx, $cy)
                $textSurface = $over.Invoke($null, @($base.Text, $pixel))
                $ratio = Get-DeskmateContrast $textSurface $pixel
                $luma = Get-WcagLuminance $pixel
                if ($light) {
                    Assert-True ($luma -ge 0.58) "Light liquid centre luma $luma must be >= 0.58"
                    Assert-True ($ratio -ge 4.5) "Light liquid centre text contrast $ratio must be >= 4.5"
                }
                else {
                    Assert-True ($luma -le 0.18) "Dark liquid centre luma $luma must be <= 0.18"
                    Assert-True ($ratio -ge 4.5) "Dark liquid centre text contrast $ratio must be >= 4.5"
                }
                Assert-Equal 255 $pixel.A "Liquid output must be opaque"
                Assert-True ($pixel.R -ne $raw[(($cy * $w + $cx) * 4) + 2] -or $pixel.G -ne $raw[(($cy * $w + $cx) * 4) + 1] -or $pixel.B -ne $raw[(($cy * $w + $cx) * 4)]) "Liquid pass must alter the sampled centre"
            }
            finally { $bmp.Dispose() }
        }
    }

    It "keeps derived palette ink above the accent-aware glass floors" {
        $cases = @(
            @{ Bg = [System.Drawing.ColorTranslator]::FromHtml("#101820"); Text = [System.Drawing.ColorTranslator]::FromHtml("#202830"); Accent = [System.Drawing.ColorTranslator]::FromHtml("#FFD24A"); Wall = [System.Drawing.Color]::Black; Light = $false },
            @{ Bg = [System.Drawing.ColorTranslator]::FromHtml("#F5F5F5"); Text = [System.Drawing.ColorTranslator]::FromHtml("#F0F0F0"); Accent = [System.Drawing.ColorTranslator]::FromHtml("#FFFF00"); Wall = [System.Drawing.Color]::White; Light = $true }
        )
        foreach ($case in $cases) {
            $dict = $derive.Invoke($null, @($case.Bg, $case.Text, $case.Accent))
            $bg = [System.Drawing.ColorTranslator]::FromHtml($dict["bg"])
            $text = [System.Drawing.ColorTranslator]::FromHtml($dict["text"])
            $sub = [System.Drawing.ColorTranslator]::FromHtml($dict["sub"])
            $accent = [System.Drawing.ColorTranslator]::FromHtml($dict["accent"])
            $tint = if ($case.Light) { [single]0.84 } else { [single]0.67 }
            $surface = $over.Invoke($null, @($inkA.Invoke($null, @($bg, $tint)), $case.Wall))
            Assert-True ((Get-DeskmateContrast $text $surface) -ge 4.5) "Derived text must survive tinted glass (light=$($case.Light))"
            Assert-True ((Get-DeskmateContrast $sub $surface) -ge 4.5) "Derived sub must survive tinted glass (light=$($case.Light))"
            Assert-True ((Get-DeskmateContrast $accent $surface) -ge 3.0) "Derived accent must survive tinted glass (light=$($case.Light))"
            Assert-True ((Get-DeskmateContrast $text $bg) -ge 7.0) "Derived text must keep its 7:1 floor"
            Assert-True ((Get-DeskmateContrast $sub $bg) -ge 4.5) "Derived sub must keep its 4.5:1 floor"
        }
    }

    It "raises tint in dark mode and lowers it in light mode for the same bright wallpaper" {
        $sig = New-Object byte[] 64
        for ($i = 0; $i -lt $sig.Length; $i += 4) {
            $sig[$i] = 255; $sig[$i + 1] = 255; $sig[$i + 2] = 255; $sig[$i + 3] = 255
        }
        Reset-DeskmateTheme -Light $false
        $darkBg = (Get-DeskmateStaticField "Ink" "Base").Bg
        $darkTint = [float]$computeTint.Invoke($null, @([byte[]]$sig, [int]$sig.Length, [System.Drawing.Color]$darkBg, [bool]$false))
        Reset-DeskmateTheme -Light $true
        $lightBg = (Get-DeskmateStaticField "Ink" "Base").Bg
        $lightTint = [float]$computeTint.Invoke($null, @([byte[]]$sig, [int]$sig.Length, [System.Drawing.Color]$lightBg, [bool]$true))
        Assert-True ($darkTint -gt $lightTint) "Dark glass tint $darkTint must exceed light tint $lightTint for a bright backdrop"
        Assert-True ($darkTint -ge 0.67) "Dark tint must reach the 0.67 contrast guard"
        Assert-True ($lightTint -ge 0.419) "Light tint must honour the 0.42 base tint"
        $glass = (Get-DeskmateStaticField "Ink" "Base").OnGlass()
        Assert-True ($glass.Sub.A -ge 189) "Tone.OnGlass must raise secondary ink alpha to at least 189"
    }
}
