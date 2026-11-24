# test_b06_kit_faint_contrast.ps1
# Boundary & Corner Case Tests for Feature 6: Elevate KitInk.Faint Light Contrast
# Covers KitInk.Faint contrast boundaries across light/dark modes and Kit surfaces.

$commonDir = Join-Path (Split-Path -Parent $PSScriptRoot) "common"
. (Join-Path $commonDir "TestHarness.ps1")
. (Join-Path $commonDir "DeskmateLoader.ps1")

Describe "Tier 2 Boundary Suite: Feature 6 - KitInk.Faint Contrast Boundaries" {

    BeforeEach {
        Reset-DeskmateTheme -Light $false
    }

    It "Evaluates KitInk.Faint contrast against KitInk.Window in Dark Mode" {
        Reset-DeskmateTheme -Light $false
        $faint = Get-DeskmateStaticProperty "KitInk" "Faint"
        $win = Get-DeskmateStaticProperty "KitInk" "Window"

        $ratio = Get-WcagContrastRatio $faint $win
        Assert-True ($ratio -ge 3.0) "Dark mode KitInk.Faint contrast vs Window must be >= 3.0:1 (actual: ${ratio}:1)"
    }

    It "Evaluates KitInk.Faint contrast against KitInk.Window in Light Mode" {
        Reset-DeskmateTheme -Light $true
        $faint = Get-DeskmateStaticProperty "KitInk" "Faint"
        $win = Get-DeskmateStaticProperty "KitInk" "Window"

        $ratio = Get-WcagContrastRatio $faint $win
        Assert-True ($ratio -ge 2.5) "Light mode KitInk.Faint contrast vs Window must be >= 2.5:1 (actual: ${ratio}:1)"
    }

    It "Evaluates KitInk.Faint contrast against KitInk.Side in Light Mode" {
        Reset-DeskmateTheme -Light $true
        $faint = Get-DeskmateStaticProperty "KitInk" "Faint"
        $side = Get-DeskmateStaticProperty "KitInk" "Side"

        $ratio = Get-WcagContrastRatio $faint $side
        Assert-True ($ratio -ge 2.5) "Light mode KitInk.Faint contrast vs Side must be >= 2.5:1 (actual: ${ratio}:1)"
    }

    It "Evaluates KitInk.Faint contrast against KitInk.Group in Light Mode" {
        Reset-DeskmateTheme -Light $true
        $faint = Get-DeskmateStaticProperty "KitInk" "Faint"
        $group = Get-DeskmateStaticProperty "KitInk" "Group"

        $ratio = Get-WcagContrastRatio $faint $group
        Assert-True ($ratio -ge 2.5) "Light mode KitInk.Faint contrast vs Group must be >= 2.5:1 (actual: ${ratio}:1)"
    }

    It "Evaluates KitInk.Faint contrast against KitInk.Hover in Light Mode" {
        Reset-DeskmateTheme -Light $true
        $faint = Get-DeskmateStaticProperty "KitInk" "Faint"
        $hover = Get-DeskmateStaticProperty "KitInk" "Hover"

        $ratio = Get-WcagContrastRatio $faint $hover
        Assert-True ($ratio -ge 2.2) "Light mode KitInk.Faint contrast vs Hover must be >= 2.2:1 (actual: ${ratio}:1)"
    }

    It "Keeps Text, Sub, and Faint >= 4.5:1 on every Kit surface for Calm wallpaper canvases of any hue" {
        $kit = Get-DeskmateType "KitInk"
        $island = Get-DeskmateType "Island"
        $canvasField = $kit.GetField("Canvas", [System.Reflection.BindingFlags]'Public,Static')
        $calmCanvas = $island.GetMethod("CalmCanvas", [System.Reflection.BindingFlags]'NonPublic,Static')
        Assert-NotNull $canvasField "KitInk.Canvas must exist"
        Assert-NotNull $calmCanvas "Island.CalmCanvas must exist"
        $worst = 99.0; $where = ""
        try {
            foreach ($light in @($false, $true)) {
                Reset-DeskmateTheme -Light $light
                for ($h = 0; $h -lt 360; $h += 30) {
                    # Fully saturated pick at the hue: the worst case for chroma
                    $x = 1 - [Math]::Abs((($h / 60) % 2) - 1)
                    $rgb = switch ([int][Math]::Floor($h / 60)) { 0 { 1, $x, 0 } 1 { $x, 1, 0 } 2 { 0, 1, $x } 3 { 0, $x, 1 } 4 { $x, 0, 1 } default { 1, 0, $x } }
                    $pick = [System.Drawing.Color]::FromArgb(255, [int](255 * $rgb[0]), [int](255 * $rgb[1]), [int](255 * $rgb[2]))
                    $canvasField.SetValue($null, [Nullable[System.Drawing.Color]]($calmCanvas.Invoke($null, [object[]]@($pick, [bool]$light))))
                    foreach ($ink in @("Text", "Sub", "Faint")) {
                        foreach ($surface in @("Window", "Side", "Group", "Hover", "Line")) {
                            $r = Get-WcagContrastRatio (Get-DeskmateStaticProperty "KitInk" $ink) (Get-DeskmateStaticProperty "KitInk" $surface)
                            if ($r -lt $worst) { $worst = $r; $where = "light=$light hue=$h $ink/$surface" }
                        }
                    }
                }
            }
        }
        finally { $canvasField.SetValue($null, $null); Reset-DeskmateTheme -Light $false }
        Assert-True ($worst -ge 4.5) "Calm canvas contrast must stay >= 4.5:1 (worst ${worst}:1 at $where)"
    }
}
