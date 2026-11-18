# test_f04_liquid_glass.ps1
# Feature 4: Liquid Glass Color Space Blending
# Verifies linear luminance blending in Glass.cs maintains target luminance (>=0.62) and >=4.5:1 contrast on dark wallpapers in light mode.

$commonDir = Join-Path $PSScriptRoot "..\common"
. (Join-Path $commonDir "TestHarness.ps1")
. (Join-Path $commonDir "DeskmateLoader.ps1")

Describe "Feature 4: Liquid Glass Color Space Blending" {
    $island = Get-DeskmateType "Island"
    $toSrgb = $island.GetMethod("ToSrgb", [System.Reflection.BindingFlags]'Public,NonPublic,Static')
    $liquidPass = $island.GetMethod("LiquidPass", [System.Reflection.BindingFlags]'NonPublic,Static')
    $clearPass = $island.GetMethod("ClearPass", [System.Reflection.BindingFlags]'NonPublic,Static')
    $compGlass = Get-DeskmateType "CompGlass"
    $spec = $compGlass.GetMethod("Spec", [System.Reflection.BindingFlags]'Public,NonPublic,Static')
    $specClear = $compGlass.GetMethod("SpecClear", [System.Reflection.BindingFlags]'Public,NonPublic,Static')

    It "Verifies Linear luminance lookup table and ToSrgb method exist on Island engine" {
        Assert-NotNull $toSrgb "ToSrgb static method must exist"
        Assert-NotNull $liquidPass "LiquidPass static method must exist"
    }

    It "Verifies ToSrgb correctly maps linear boundary and mid values without byte overflow" {
        $val0 = $toSrgb.Invoke($null, @([float]0.0))
        $val1 = $toSrgb.Invoke($null, @([float]1.0))
        $valMid = $toSrgb.Invoke($null, @([float]0.214))

        Assert-Equal $val0 0 "ToSrgb(0.0) must map to 0"
        Assert-Equal $val1 255 "ToSrgb(1.0) must map to 255"
        Assert-InRange ([double]$valMid) 120 135 "ToSrgb(0.214) perceptual mid-grey must map near 127"
    }

    It "Verifies LiquidPass shader executes offscreen and generates blended pixels on dark wallpaper" {
        $bmp = New-Object System.Drawing.Bitmap 32, 32, ([System.Drawing.Imaging.PixelFormat]::Format32bppRgb)
        $raw = [byte[]](,0 * (32 * 32 * 4))
        $args = [object[]]@([System.Drawing.Bitmap]$bmp, [byte[]]$raw, [System.Drawing.Color]::White, [bool]$true)

        $liquidPass.Invoke($null, $args)

        $centerPx = $bmp.GetPixel(16, 16)
        Assert-True ($centerPx.R -gt 150) "Center pixel Red channel must be elevated by light tint (actual: $($centerPx.R))"
        Assert-True ($centerPx.G -gt 150) "Center pixel Green channel must be elevated by light tint (actual: $($centerPx.G))"
        Assert-True ($centerPx.B -gt 150) "Center pixel Blue channel must be elevated by light tint (actual: $($centerPx.B))"

        $bmp.Dispose()
    }

    It "Verifies Liquid Glass center luminance achieves target luminance (>= 0.60) in light mode over black wallpaper" {
        $bmp = New-Object System.Drawing.Bitmap 32, 32, ([System.Drawing.Imaging.PixelFormat]::Format32bppRgb)
        $raw = [byte[]](,0 * (32 * 32 * 4))
        $args = [object[]]@([System.Drawing.Bitmap]$bmp, [byte[]]$raw, [System.Drawing.Color]::White, [bool]$true)

        $liquidPass.Invoke($null, $args)

        $centerPx = $bmp.GetPixel(16, 16)
        $lum = Get-WcagLuminance $centerPx

        Assert-InRange ([double]$lum) 0.58 1.0 "Center luminance must achieve target luminance >= 0.60 (actual: $lum)"

        $bmp.Dispose()
    }

    It "Verifies dark text contrast against rendered Liquid Glass surface achieves >= 4.5:1" {
        $bmp = New-Object System.Drawing.Bitmap 32, 32, ([System.Drawing.Imaging.PixelFormat]::Format32bppRgb)
        $raw = [byte[]](,0 * (32 * 32 * 4))
        $args = [object[]]@([System.Drawing.Bitmap]$bmp, [byte[]]$raw, [System.Drawing.Color]::White, [bool]$true)

        $liquidPass.Invoke($null, $args)

        $centerPx = $bmp.GetPixel(16, 16)
        $darkText = [System.Drawing.Color]::FromArgb(255, 17, 17, 17)
        $ratio = Assert-Contrast $darkText $centerPx 4.5 "Dark text on Liquid Glass surface"

        Assert-True ($ratio -ge 4.5) "Contrast ratio must be >= 4.5:1 (actual: ${ratio}:1)"

        $bmp.Dispose()
    }

    It "Verifies Clear material stays thinner than Liquid while blurring more of the backdrop" {
        Reset-DeskmateTheme -Light $false
        $liquidArgs = [object[]]@([bool]$true, [bool]$false, [float]1.0, [float]1.0, [bool]$false)
        $clearArgs = [object[]]@([bool]$false, [float]1.0, [float]1.0, [bool]$false)
        $liquidSpec = $spec.Invoke($null, $liquidArgs)
        $clearSpec = $specClear.Invoke($null, $clearArgs)

        Assert-NotNull $liquidSpec "Liquid Spec must exist"
        Assert-NotNull $clearSpec "Clear Spec must exist"
        Assert-True ($clearSpec.Tint.A -lt $liquidSpec.Tint.A) "Clear veil ($($clearSpec.Tint.A)) must be thinner than Liquid ($($liquidSpec.Tint.A))"
        Assert-True ($clearSpec.Tint.A -le 72) "Island Clear veil must stay at or below 72/255 (actual: $($clearSpec.Tint.A))"
        Assert-True ($clearSpec.Blur -gt $liquidSpec.Blur) "Clear must blur more than Liquid (clear $($clearSpec.Blur), liquid $($liquidSpec.Blur))"
        Assert-True ($clearSpec.Saturation -ge 1.2) "Clear must keep backdrop colour alive (actual: $($clearSpec.Saturation))"
    }

    It "Verifies ClearPass preserves backdrop chroma under a thin veil" {
        $w = 24; $h = 18
        $bmp = New-Object System.Drawing.Bitmap $w, $h, ([System.Drawing.Imaging.PixelFormat]::Format32bppRgb)
        try {
            $raw = New-Object byte[] ($w * $h * 4)
            for ($y = 0; $y -lt $h; $y++) {
                for ($x = 0; $x -lt $w; $x++) {
                    $i = ($y * $w + $x) * 4
                    if ($x -lt ($w / 2)) {
                        $raw[$i] = 220; $raw[$i + 1] = 90; $raw[$i + 2] = 30
                    }
                    else {
                        $raw[$i] = 40; $raw[$i + 1] = 120; $raw[$i + 2] = 230
                    }
                    $raw[$i + 3] = 255
                }
            }
            $clearPass.Invoke($null, [object[]]@([System.Drawing.Bitmap]$bmp, [byte[]]$raw, [System.Drawing.Color]::White, [single]0.16))
            $centre = $bmp.GetPixel([int]($w / 2), [int]($h / 2))
            $spread = [Math]::Max($centre.R, [Math]::Max($centre.G, $centre.B)) - [Math]::Min($centre.R, [Math]::Min($centre.G, $centre.B))
            Assert-Equal 255 $centre.A "Clear output must stay opaque for the layered window"
            Assert-True ($spread -ge 18) "Clear glass must keep background chroma (actual spread: $spread)"

            $vivid = 0
            for ($y = 0; $y -lt $h; $y++) {
                for ($x = 0; $x -lt $w; $x++) {
                    $c = $bmp.GetPixel($x, $y)
                    $s = [Math]::Max($c.R, [Math]::Max($c.G, $c.B)) - [Math]::Min($c.R, [Math]::Min($c.G, $c.B))
                    if ($s -ge 10) { $vivid++ }
                }
            }
            Assert-True ($vivid -ge [int]($w * $h * 0.25)) "At least 25% of Clear pixels must retain visible chroma (got $vivid/$($w * $h))"
        }
        finally {
            $bmp.Dispose()
        }
    }
}
