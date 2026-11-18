# test_f35_material_identity.ps1
# Feature 35: one material recipe shared by GPU, software, dock, cards, and sheets.
# Verifies that Clear, Liquid, Frost, and Solid keep distinct alpha, blur,
# chroma, chrome, wall opacity, and text-legibility rules.

$commonDir = Join-Path $PSScriptRoot "..\common"
. (Join-Path $commonDir "TestHarness.ps1")
. (Join-Path $commonDir "DeskmateLoader.ps1")

Describe "Feature 35: Material Identity Across Every Surface" {
    $flags = [System.Reflection.BindingFlags]'Public,NonPublic,Static'
    $recipe = Get-DeskmateType "GlassRecipe"
    $kindType = Get-DeskmateType "GlassKind"
    $compGlass = Get-DeskmateType "CompGlass"
    $wall = Get-DeskmateType "WallBackdrop"
    $jobType = Get-DeskmateType "FrostJob"

    $clear = [System.Enum]::Parse($kindType, "Clear")
    $liquid = [System.Enum]::Parse($kindType, "Liquid")
    $frost = [System.Enum]::Parse($kindType, "Frost")
    $solid = [System.Enum]::Parse($kindType, "Solid")

    $tintAlpha = $recipe.GetMethod("TintAlpha", $flags)
    $blur = $recipe.GetMethod("Blur", $flags)
    $saturation = $recipe.GetMethod("Saturation", $flags)
    $tintBase = $recipe.GetMethod("TintBase", $flags)
    $wallOpacity = $recipe.GetMethod("WallOpacity", $flags)
    $needsShadow = $recipe.GetMethod("NeedsShadow", $flags)
    $rimTop = $recipe.GetMethod("RimTop", $flags)
    $innerLight = $recipe.GetMethod("InnerLight", $flags)
    $sheen = $recipe.GetMethod("Sheen", $flags)

    It "provides the complete shared recipe on the compiled assembly" {
        Assert-NotNull $recipe "GlassRecipe must exist"
        Assert-NotNull $kindType "GlassKind must exist"
        Assert-NotNull $tintAlpha "TintAlpha must exist"
        Assert-NotNull $blur "Blur must exist"
        Assert-NotNull $saturation "Saturation must exist"
        Assert-NotNull $tintBase "TintBase must exist"
        Assert-NotNull $wallOpacity "WallOpacity must exist"
        Assert-NotNull $needsShadow "NeedsShadow must exist"
    }

    It "orders tint density Clear < Liquid < Frost on island, panel, and bar" {
        foreach ($light in @($false, $true)) {
            foreach ($panel in @($false, $true)) {
                foreach ($bar in @($false, $true)) {
                    $ca = $tintAlpha.Invoke($null, [object[]]@($clear, [bool]$light, [bool]$panel, [bool]$bar))
                    $la = $tintAlpha.Invoke($null, [object[]]@($liquid, [bool]$light, [bool]$panel, [bool]$bar))
                    $fa = $tintAlpha.Invoke($null, [object[]]@($frost, [bool]$light, [bool]$panel, [bool]$bar))
                    $sa = $tintAlpha.Invoke($null, [object[]]@($solid, [bool]$light, [bool]$panel, [bool]$bar))
                    $where = "light=$light panel=$panel bar=$bar"
                    Assert-True ($ca -lt $la) "Clear tint must be thinner than Liquid for $where (clear $ca, liquid $la)"
                    Assert-True ($la -lt $fa) "Liquid tint must be thinner than Frost for $where (liquid $la, frost $fa)"
                    Assert-True ($fa -lt $sa) "Frost must remain translucent while Solid is opaque for $where (frost $fa, solid $sa)"
                    Assert-Equal 255 $sa "Solid tint must be fully opaque for $where"
                }
            }
        }
    }

    It "keeps Clear the strongest blur, Liquid the least, and Frost between them" {
        foreach ($light in @($false, $true)) {
            $cb = $blur.Invoke($null, [object[]]@($clear, [bool]$true, [bool]$false, [single]1.0))
            $lb = $blur.Invoke($null, [object[]]@($liquid, [bool]$true, [bool]$false, [single]1.0))
            $fb = $blur.Invoke($null, [object[]]@($frost, [bool]$true, [bool]$false, [single]1.0))
            Assert-True ($cb -gt $lb) "Clear must blur more than Liquid (clear $cb, liquid $lb)"
            Assert-True ($fb -gt $lb) "Frost must diffuse more than Liquid (frost $fb, liquid $lb)"
            Assert-True ($cb -ge $fb) "Clear must remain the clearest pane rather than Frost (clear $cb, frost $fb)"
        }
    }

    It "keeps Liquid vivid, Clear balanced, and Frost desaturated" {
        $cs = $saturation.Invoke($null, [object[]]@($clear))
        $ls = $saturation.Invoke($null, [object[]]@($liquid))
        $fs = $saturation.Invoke($null, [object[]]@($frost))
        Assert-True ($ls -ge 1.5) "Liquid must stay vivid (actual: $ls)"
        Assert-True ($cs -ge 1.2) "Clear must keep the backdrop colour alive (actual: $cs)"
        Assert-True ($fs -le 0.7) "Frost must desaturate the backdrop (actual: $fs)"

        $veil = [System.Drawing.Color]::FromArgb(255, 32, 80, 160)
        $ct = $tintBase.Invoke($null, [object[]]@($clear, $veil, [bool]$false))
        $lt = $tintBase.Invoke($null, [object[]]@($liquid, $veil, [bool]$false))
        $ft = $tintBase.Invoke($null, [object[]]@($frost, $veil, [bool]$false))
        Assert-Equal $veil.R $ct.R "Clear must take the backdrop hue unchanged (red)"
        Assert-Equal $veil.G $ct.G "Clear must take the backdrop hue unchanged (green)"
        Assert-Equal $veil.B $ct.B "Clear must take the backdrop hue unchanged (blue)"
        $lSpread = [Math]::Max($lt.R, [Math]::Max($lt.G, $lt.B)) - [Math]::Min($lt.R, [Math]::Min($lt.G, $lt.B))
        $fSpread = [Math]::Max($ft.R, [Math]::Max($ft.G, $ft.B)) - [Math]::Min($ft.R, [Math]::Min($ft.G, $ft.B))
        Assert-True ($lSpread -gt $fSpread + 20) "Liquid must keep more chroma than Frost (liquid $lSpread, frost $fSpread)"
        Assert-True ($fSpread -le 40) "Frost must read neutral rather than tinted (spread: $fSpread)"
    }

    It "makes the GPU spec consume the same recipe instead of a parallel table" {
        Reset-DeskmateTheme -Light $false
        $spec = $compGlass.GetMethod("Spec", $flags)
        foreach ($kind in @($clear, $liquid, $frost)) {
            $actual = $spec.Invoke($null, [object[]]@($kind, [bool]$true, [single]1.0, [single]1.0, [bool]$false))
            $expectedA = $tintAlpha.Invoke($null, [object[]]@($kind, [bool]$false, [bool]$true, [bool]$false))
            $expectedBlur = $blur.Invoke($null, [object[]]@($kind, [bool]$true, [bool]$false, [single]1.0))
            $expectedSat = $saturation.Invoke($null, [object[]]@($kind))
            Assert-Equal $expectedA $actual.Tint.A "GPU tint alpha must match the recipe for $kind"
            Assert-InRange ([double]$actual.Blur) ([double]$expectedBlur) ([double]$expectedBlur) "GPU blur must match the recipe for $kind"
            Assert-InRange ([double]$actual.Saturation) ([double]$expectedSat) ([double]$expectedSat) "GPU saturation must match the recipe for $kind"
        }
    }

    It "keeps Liquid and Frost text readable on the worst white and black backdrops" {
        $inkType = Get-DeskmateType "Ink"
        $toneType = Get-DeskmateType "Tone"
        $tint = $recipe.GetMethod("Tint", $flags)
        $over = $inkType.GetMethod("Over", $flags)
        $onGlass = $toneType.GetMethod("OnGlass", [System.Reflection.BindingFlags]'Public,NonPublic,Instance')
        foreach ($light in @($false, $true)) {
            Reset-DeskmateTheme -Light $light
            $veil = $inkType.GetProperty("Veil").GetValue($null)
            $baseTone = Get-DeskmateStaticField "Ink" "Base"
            $text = $onGlass.Invoke($baseTone, @()).Text
            $backdrop = if ($light) { [System.Drawing.Color]::Black } else { [System.Drawing.Color]::White }
            foreach ($kind in @($liquid, $frost)) {
                $t = $tint.Invoke($null, [object[]]@($kind, $veil, [bool]$light, [bool]$true, [bool]$false))
                $surface = $over.Invoke($null, [object[]]@($t, $backdrop))
                $contrast = Get-DeskmateContrast $text $surface
                Assert-True ($contrast -ge 4.5) "$kind text must reach 4.5:1 over $backdrop in light=$light (actual: $([Math]::Round($contrast, 2)))"
            }
        }
    }

    It "invalidates a Frost bake when only the material changes" {
        $job = [Activator]::CreateInstance($jobType, $true)
        $job.W = 100; $job.H = 80; $job.Left = 10; $job.Top = 20
        $job.Bg = [System.Drawing.Color]::FromArgb(255, 88, 94, 102)
        $job.Light = $false; $job.Kind = $frost; $job.Panel = $true
        $job.Extra = [single]0.22; $job.Sheen = 40
        $copy = [Activator]::CreateInstance($jobType, $true)
        foreach ($f in $jobType.GetFields([System.Reflection.BindingFlags]'Public,NonPublic,Instance')) {
            $f.SetValue($copy, $f.GetValue($job))
        }
        Assert-True ($job.SameBase($copy)) "An unchanged Frost bake must be reusable"
        $copy.Kind = $liquid
        Assert-False ($job.SameBase($copy)) "Changing the material must invalidate the bake base"
    }

    It "orders Clear, Liquid, and Frost wallpaper opacity and proves Fade uses it" {
        $clearOp = $wallOpacity.Invoke($null, [object[]]@($clear, [bool]$true, [bool]$false))
        $liquidOp = $wallOpacity.Invoke($null, [object[]]@($liquid, [bool]$true, [bool]$false))
        $frostOp = $wallOpacity.Invoke($null, [object[]]@($frost, [bool]$true, [bool]$false))
        Assert-True ($clearOp -lt $liquidOp) "Clear must show more desktop than Liquid ($clearOp vs $liquidOp)"
        Assert-True ($liquidOp -lt $frostOp) "Liquid must show more desktop than Frost ($liquidOp vs $frostOp)"
        Assert-InRange ([double]$frostOp) 0.90 0.96 "Frost must stay nearly opaque"

        $fade = $wall.GetMethod("Fade", [type[]]@([System.Drawing.Bitmap], [single]))
        $setActive = $wall.GetMethod("SetActive", $flags)
        $clearWall = $wall.GetMethod("Clear", $flags)
        Assert-NotNull $fade "The material-specific Fade overload must exist"
        try {
            $setActive.Invoke($null, [object[]]@($true))
            foreach ($case in @(
                @{ Kind = $clear; Opacity = $clearOp; Min = 160; Max = 185 },
                @{ Kind = $frost; Opacity = $frostOp; Min = 230; Max = 245 }
            )) {
                $plate = New-Object System.Drawing.Bitmap 4, 4, ([System.Drawing.Imaging.PixelFormat]::Format32bppPArgb)
                try {
                    $pg = [System.Drawing.Graphics]::FromImage($plate)
                    try { $pg.Clear([System.Drawing.Color]::FromArgb(255, 32, 80, 160)) } finally { $pg.Dispose() }
                    $fade.Invoke($null, [object[]]@([System.Drawing.Bitmap]$plate, [single]$case.Opacity))
                    $alpha = $plate.GetPixel(1, 1).A
                    Assert-InRange ([double]$alpha) ([double]$case.Min) ([double]$case.Max) "Wall fade must follow $($case.Kind) opacity (alpha: $alpha)"
                }
                finally { $plate.Dispose() }
            }
        }
        finally { $clearWall.Invoke($null, @()) }
    }

    It "gives each material its own chrome and legibility rule" {
        foreach ($light in @($false, $true)) {
            $clearRim = $rimTop.Invoke($null, [object[]]@($clear, [bool]$light))
            $liquidRim = $rimTop.Invoke($null, [object[]]@($liquid, [bool]$light))
            $frostRim = $rimTop.Invoke($null, [object[]]@($frost, [bool]$light))
            Assert-True ($clearRim -gt $liquidRim) "Clear needs the brightest rim (clear $clearRim, liquid $liquidRim)"
            Assert-True ($liquidRim -gt $frostRim) "Frost needs the softest rim (liquid $liquidRim, frost $frostRim)"

            $clearLight = $innerLight.Invoke($null, [object[]]@($clear, [bool]$light))
            $liquidLight = $innerLight.Invoke($null, [object[]]@($liquid, [bool]$light))
            $frostLight = $innerLight.Invoke($null, [object[]]@($frost, [bool]$light))
            Assert-True ($frostLight -gt $clearLight) "Frost needs the strongest soft inner light (frost $frostLight, clear $clearLight)"
            Assert-True ($clearLight -gt $liquidLight) "Liquid gloss must not flatten into a bright wash (clear $clearLight, liquid $liquidLight)"
        }
        Assert-True ($sheen.Invoke($null, [object[]]@($liquid, [bool]$false)) -gt $sheen.Invoke($null, [object[]]@($frost, [bool]$false))) "Liquid must keep the strongest gloss"
        Assert-True ($needsShadow.Invoke($null, [object[]]@($clear))) "Clear must enable the legibility shadow"
        Assert-False ($needsShadow.Invoke($null, [object[]]@($liquid))) "Liquid must stay shadow-free"
        Assert-False ($needsShadow.Invoke($null, [object[]]@($frost))) "Frost must not need the Clear shadow"
    }

    It "wires the recipe into software Frost, shadows, and every GPU surface" {
        $root = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
        $glassSrc = Get-Content (Join-Path $root "src\Glass.cs") -Raw
        $dockFrostSrc = Get-Content (Join-Path $root "src\DockFrost.cs") -Raw
        $dockPaintSrc = Get-Content (Join-Path $root "src\DockPaint.cs") -Raw
        $dockCardSrc = Get-Content (Join-Path $root "src\DockCard.cs") -Raw
        $dockSheetSrc = Get-Content (Join-Path $root "src\DockSheet.cs") -Raw
        $painterSrc = Get-Content (Join-Path $root "src\Painter.cs") -Raw
        Assert-True ($glassSrc -match 'GlassRecipe\.FrostExtra') "Island Frost must add the shared frosting layer"
        Assert-True ($dockFrostSrc -match 'GlassRecipe\.FrostTint') "Software dock Frost must use the neutral shared tint"
        Assert-True ($dockFrostSrc -match 'GlassRecipe\.FrostExtra') "Software dock Frost must add the shared frosting layer"
        Assert-True ($dockPaintSrc -match 'GlassRecipe\.Overlay') "The dock bar must take its veil from the recipe"
        Assert-True ($dockCardSrc -match 'GlassRecipe\.PlateVeil') "Cards must take their software veil from the recipe"
        Assert-True ($dockSheetSrc -match 'GlassRecipe\.PlateVeil') "Sheets must take their software veil from the recipe"
        Assert-True ($dockCardSrc -match 'GlassRecipe\.NeedsShadow') "Cards must enable the Clear text shadow"
        Assert-True ($dockSheetSrc -match 'GlassRecipe\.NeedsShadow') "Sheets must enable the Clear text shadow"
        Assert-True ($painterSrc -match 'ShadowOf\(c, 1f\)') "Painter must draw the primary shadow pass"
        Assert-True ($painterSrc -match 'ShadowOf\(c, 0\.45f\)') "Painter must draw the falloff pass for small text"
        Assert-True ($glassSrc -match 'CompGlass\.Spec\(Kind') "The island GPU path must use the shared GlassKind"
        Assert-True ($dockPaintSrc -match 'CompGlass\.Spec\(kind') "The dock GPU path must use the shared GlassKind"
    }
}
