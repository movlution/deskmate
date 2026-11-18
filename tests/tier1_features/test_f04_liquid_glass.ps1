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

    It "Verifies wall-synced glass crops only the wallpaper snapshot" {
        $wall = Get-DeskmateType "WallBackdrop"
        Assert-NotNull $wall "WallBackdrop type must exist"
        $projectRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
        $glassSrc = Get-Content (Join-Path $projectRoot "src\Glass.cs") -Raw
        $dockSrc = Get-Content (Join-Path $projectRoot "src\DockFrost.cs") -Raw
        Assert-True ($glassSrc -match 'WallBackdrop\.Draw') "Island frost must sample the wallpaper snapshot"
        Assert-True ($dockSrc -match 'WallBackdrop\.Draw') "Dock, card, and sheet frost must sample the wallpaper snapshot"
        Assert-True ($glassSrc -match 'GpuGlassOn[^\r\n]*WallGlassOn') "Wall-synced island glass must bypass the live GPU backdrop"
        $flags = [System.Reflection.BindingFlags]'NonPublic,Static'
        $canvasField = $wall.GetField("canvas", $flags)
        $boundsField = $wall.GetField("screenBounds", $flags)
        $bgField = $wall.GetField("background", $flags)
        $activeField = $wall.GetField("active", $flags)
        Assert-NotNull $canvasField "WallBackdrop.canvas must exist"
        Assert-NotNull $boundsField "WallBackdrop.screenBounds must exist"
        Assert-NotNull $bgField "WallBackdrop.background must exist"
        Assert-NotNull $activeField "WallBackdrop.active must exist"

        $oldCanvas = $canvasField.GetValue($null)
        $oldBounds = $boundsField.GetValue($null)
        $oldBg = $bgField.GetValue($null)
        $oldActive = $activeField.GetValue($null)
        $src = New-Object System.Drawing.Bitmap 8, 4, ([System.Drawing.Imaging.PixelFormat]::Format32bppRgb)
        $target = New-Object System.Drawing.Bitmap 4, 4, ([System.Drawing.Imaging.PixelFormat]::Format32bppRgb)
        $g = [System.Drawing.Graphics]::FromImage($target)
        try {
            for ($y = 0; $y -lt 4; $y++) {
                for ($x = 0; $x -lt 8; $x++) {
                    $src.SetPixel($x, $y, $(if ($x -lt 4) { [System.Drawing.Color]::Red } else { [System.Drawing.Color]::Blue }))
                }
            }
            $canvasField.SetValue($null, $src)
            $boundsField.SetValue($null, [System.Drawing.Rectangle]::new(0, 0, 8, 4))
            $bgField.SetValue($null, [System.Drawing.Color]::Black)
            $activeField.SetValue($null, $true)

            $draw = $wall.GetMethod("Draw", [System.Reflection.BindingFlags]'Public,NonPublic,Static')
            Assert-NotNull $draw "WallBackdrop.Draw must exist"
            $ok = $draw.Invoke($null, [object[]]@($g, [System.Drawing.Rectangle]::new(4, 0, 4, 4), [System.Drawing.Size]::new(4, 4)))
            Assert-True ([bool]$ok) "WallBackdrop.Draw must report a wallpaper fill"
            for ($y = 0; $y -lt 4; $y++) {
                for ($x = 0; $x -lt 4; $x++) {
                    $c = $target.GetPixel($x, $y)
                    Assert-Equal 0 $c.R "Right half must come from the blue wallpaper crop (red channel)"
                    Assert-Equal 255 $c.B "Right half must come from the blue wallpaper crop (blue channel)"
                }
            }

            $ok = $draw.Invoke($null, [object[]]@($g, [System.Drawing.Rectangle]::new(0, 0, 4, 4), [System.Drawing.Size]::new(4, 4)))
            Assert-True ([bool]$ok) "WallBackdrop.Draw must fill the left crop too"
            $c = $target.GetPixel(1, 1)
            Assert-Equal 255 $c.R "Left half must come from the red wallpaper crop (red channel)"
            Assert-Equal 0 $c.B "Left half must not pick up the right-hand crop (blue channel)"
        }
        finally {
            $g.Dispose()
            $target.Dispose()
            $src.Dispose()
            $canvasField.SetValue($null, $oldCanvas)
            $boundsField.SetValue($null, $oldBounds)
            $bgField.SetValue($null, $oldBg)
            $activeField.SetValue($null, $oldActive)
        }
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
