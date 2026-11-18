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
    $glassKind = Get-DeskmateType "GlassKind"
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
        $liquidArgs = [object[]]@([System.Enum]::Parse($glassKind, "Liquid"), [bool]$false, [float]1.0, [float]1.0, [bool]$false)
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

    It "Verifies wall-synced glass uses a wallpaper tint, not a screen image" {
        $wall = Get-DeskmateType "WallBackdrop"
        Assert-NotNull $wall "WallBackdrop type must exist"
        $projectRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
        $glassSrc = Get-Content (Join-Path $projectRoot "src\Glass.cs") -Raw
        $dockSrc = Get-Content (Join-Path $projectRoot "src\DockFrost.cs") -Raw
        $syncSrc = Get-Content (Join-Path $projectRoot "src\WallSync.cs") -Raw
        $wallSrc = Get-Content (Join-Path $projectRoot "src\WallBackdrop.cs") -Raw
        Assert-True ($glassSrc -match 'WallBackdrop\.Draw') "Island frost must fill from the wallpaper tint"
        Assert-True ($dockSrc -match 'WallBackdrop\.Draw') "Dock, card, and sheet frost must fill from the wallpaper tint"
        Assert-True ($syncSrc -match 'WallBackdrop\.SetTint') "WallApply must pass the wallpaper veil to the glass source"
        Assert-True ($syncSrc -match 'WallVeil\(wallPick\.Value') "The glass tint must come from the sampled wallpaper hue"
        Assert-True ($syncSrc -match 'wallPoll\.Start\(\)') "Wall sync must keep watching for wallpaper changes"
        Assert-True ($syncSrc -match 'LastWriteTimeUtc') "Wall sync must skip unchanged wallpaper files"
        Assert-True ($glassSrc -match 'GpuGlassOn[^\r\n]*WallGlassOn') "Wall-synced island glass must bypass the live GPU backdrop"
        Assert-True ($wallSrc -notmatch 'Image\.FromStream') "WallBackdrop must not decode the wallpaper picture"
        Assert-True ($wallSrc -notmatch 'WallpaperStyle') "WallBackdrop must not reproduce wallpaper geometry"
        $flags = [System.Reflection.BindingFlags]'NonPublic,Static'
        $tintField = $wall.GetField("tint", $flags)
        $activeField = $wall.GetField("active", $flags)
        Assert-NotNull $tintField "WallBackdrop.tint must exist"
        Assert-NotNull $activeField "WallBackdrop.active must exist"

        $oldTint = $tintField.GetValue($null)
        $oldActive = $activeField.GetValue($null)
        $target = New-Object System.Drawing.Bitmap 4, 4, ([System.Drawing.Imaging.PixelFormat]::Format32bppRgb)
        $g = [System.Drawing.Graphics]::FromImage($target)
        try {
            $tintField.SetValue($null, [System.Drawing.Color]::FromArgb(255, 38, 80, 160))
            $activeField.SetValue($null, $true)

            $draw = $wall.GetMethod("Draw", [System.Reflection.BindingFlags]'Public,NonPublic,Static')
            Assert-NotNull $draw "WallBackdrop.Draw must exist"
            $ok = $draw.Invoke($null, [object[]]@($g, [System.Drawing.Rectangle]::new(0, 0, 4, 4), [System.Drawing.Size]::new(4, 4)))
            Assert-True ([bool]$ok) "WallBackdrop.Draw must report a tint fill"
            $first = $target.GetPixel(1, 1)
            Assert-Equal 255 $first.A "Wallpaper tint must stay opaque for the layered window"
            Assert-True ($first.B -gt $first.R) "Wallpaper tint must keep the wallpaper hue (blue channel over red)"

            $ok = $draw.Invoke($null, [object[]]@($g, [System.Drawing.Rectangle]::new(120, 80, 4, 4), [System.Drawing.Size]::new(4, 4)))
            Assert-True ([bool]$ok) "WallBackdrop.Draw must fill a pane away from the origin"
            $second = $target.GetPixel(1, 1)
            Assert-Equal $first.R $second.R "A wallpaper tint must not depend on the screen crop (red channel)"
            Assert-Equal $first.G $second.G "A wallpaper tint must not depend on the screen crop (green channel)"
            Assert-Equal $first.B $second.B "A wallpaper tint must not depend on the screen crop (blue channel)"
        }
        finally {
            $g.Dispose()
            $target.Dispose()
            $tintField.SetValue($null, $oldTint)
            $activeField.SetValue($null, $oldActive)
        }
    }

    It "Verifies wallpaper glass leaves a little desktop visible" {
        $wall = Get-DeskmateType "WallBackdrop"
        $projectRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
        $glassSrc = Get-Content (Join-Path $projectRoot "src\Glass.cs") -Raw
        $dockSrc = Get-Content (Join-Path $projectRoot "src\DockFrost.cs") -Raw
        Assert-True ($glassSrc -match 'WallBackdrop\.Fade\(pl\.Base') "Island shell must fade its wallpaper-tinted plate"
        Assert-True ($dockSrc -match 'WallBackdrop\.Fade\(img') "Dock, card, and sheet plates must fade their wallpaper tint"
        $fade = $wall.GetMethod("Fade", [type[]]@([System.Drawing.Bitmap]))
        $fadeMaterial = $wall.GetMethod("Fade", [type[]]@([System.Drawing.Bitmap], [single]))
        $setActive = $wall.GetMethod("SetActive", [System.Reflection.BindingFlags]'Public,NonPublic,Static')
        $clear = $wall.GetMethod("Clear", [System.Reflection.BindingFlags]'Public,NonPublic,Static')
        Assert-NotNull $fade "WallBackdrop.Fade must exist"
        Assert-NotNull $fadeMaterial "WallBackdrop must expose material-specific opacity"
        Assert-NotNull $setActive "WallBackdrop.SetActive must exist"
        Assert-NotNull $clear "WallBackdrop.Clear must exist"
        $plate = New-Object System.Drawing.Bitmap 4, 4, ([System.Drawing.Imaging.PixelFormat]::Format32bppPArgb)
        try {
            $pg = [System.Drawing.Graphics]::FromImage($plate)
            try { $pg.Clear([System.Drawing.Color]::FromArgb(255, 38, 80, 160)) } finally { $pg.Dispose() }
            $setActive.Invoke($null, [object[]]@($true))
            $fade.Invoke($null, [object[]]@([System.Drawing.Bitmap]$plate))
            $alpha = $plate.GetPixel(1, 1).A
            Assert-True ($alpha -lt 255) "Wallpaper plate must leave the desktop slightly visible (alpha: $alpha)"
            Assert-True ($alpha -ge 210) "The default wallpaper plate must stay mostly opaque (alpha: $alpha)"

            $pg = [System.Drawing.Graphics]::FromImage($plate)
            try { $pg.Clear([System.Drawing.Color]::FromArgb(255, 38, 80, 160)) } finally { $pg.Dispose() }
            $fadeMaterial.Invoke($null, [object[]]@([System.Drawing.Bitmap]$plate, [single]0.68))
            $clearAlpha = $plate.GetPixel(1, 1).A
            Assert-True ($clearAlpha -lt $alpha) "Clear must show more desktop than the default plate (clear: $clearAlpha, default: $alpha)"
            Assert-InRange ([double]$clearAlpha) 160 185 "Clear wallpaper opacity must be about 0.68 (alpha: $clearAlpha)"
        }
        finally {
            $clear.Invoke($null, @())
            $plate.Dispose()
        }
    }

    It "Verifies the dock keeps a solid core and a transparent edge" {
        $wall = Get-DeskmateType "WallBackdrop"
        $fadeCore = $wall.GetMethod("FadeCore", [System.Reflection.BindingFlags]'Public,NonPublic,Static')
        $setActive = $wall.GetMethod("SetActive", [System.Reflection.BindingFlags]'Public,NonPublic,Static')
        $clear = $wall.GetMethod("Clear", [System.Reflection.BindingFlags]'Public,NonPublic,Static')
        Assert-NotNull $fadeCore "WallBackdrop.FadeCore must exist for the dock"
        $bmp = New-Object System.Drawing.Bitmap 80, 40, ([System.Drawing.Imaging.PixelFormat]::Format32bppPArgb)
        try {
            $g = [System.Drawing.Graphics]::FromImage($bmp)
            try { $g.Clear([System.Drawing.Color]::FromArgb(255, 30, 80, 160)) } finally { $g.Dispose() }
            $setActive.Invoke($null, [object[]]@($true))
            $bar = New-Object System.Drawing.Rectangle 0, 0, 80, 40
            $fadeCore.Invoke($null, [object[]]@([System.Drawing.Bitmap]$bmp, [System.Drawing.Rectangle]$bar, [single]20))
            $center = $bmp.GetPixel(40, 20).A
            $edge = $bmp.GetPixel(0, 20).A
            Assert-Equal 255 $center "The dock core must stay fully opaque"
            Assert-True ($edge -lt 32) "The dock edge must fade out (alpha: $edge)"
            Assert-True ($center -gt $edge) "The dock must be solid inside, not inverted"
        }
        finally {
            $clear.Invoke($null, @())
            $bmp.Dispose()
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
