# test_f06_kit_faint.ps1
# Feature 6: Elevate KitInk.Faint Light Contrast
# Verifies KitInk.Faint light-mode mix ensures >= 4.5:1 contrast across Side, Group, Hover, and Line surfaces.

$commonDir = Join-Path $PSScriptRoot "..\common"
. (Join-Path $commonDir "TestHarness.ps1")
. (Join-Path $commonDir "DeskmateLoader.ps1")

Describe "Feature 6: Elevate KitInk.Faint Light Contrast" {
    BeforeEach {
        Reset-DeskmateTheme -Light $true
    }

    It "Verifies KitInk.Faint token property exists and returns a fully opaque Color" {
        $faint = Get-DeskmateStaticProperty "KitInk" "Faint"
        Assert-NotNull $faint "KitInk.Faint must not be null"
        Assert-Equal $faint.A 255 "KitInk.Faint must be fully opaque"
    }

    It "Verifies KitInk.Faint contrast against KitInk.Window in Light Mode achieves >= 4.5:1" {
        $faint = Get-DeskmateStaticProperty "KitInk" "Faint"
        $window = Get-DeskmateStaticProperty "KitInk" "Window"

        $ratio = Assert-Contrast $faint $window 4.5 "Faint vs Window"
        Assert-True ($ratio -ge 4.5) "Contrast must be >= 4.5:1 (actual: ${ratio}:1)"
    }

    It "Verifies KitInk.Faint contrast against KitInk.Side in Light Mode achieves >= 4.5:1" {
        $faint = Get-DeskmateStaticProperty "KitInk" "Faint"
        $side = Get-DeskmateStaticProperty "KitInk" "Side"

        $ratio = Assert-Contrast $faint $side 4.5 "Faint vs Side"
        Assert-True ($ratio -ge 4.5) "Contrast must be >= 4.5:1 (actual: ${ratio}:1)"
    }

    It "Verifies KitInk.Faint contrast against KitInk.Group in Light Mode achieves >= 4.5:1" {
        $faint = Get-DeskmateStaticProperty "KitInk" "Faint"
        $group = Get-DeskmateStaticProperty "KitInk" "Group"

        $ratio = Assert-Contrast $faint $group 4.5 "Faint vs Group"
        Assert-True ($ratio -ge 4.5) "Contrast must be >= 4.5:1 (actual: ${ratio}:1)"
    }

    It "Verifies KitInk.Faint contrast against KitInk.Hover in Light Mode achieves >= 4.5:1" {
        $faint = Get-DeskmateStaticProperty "KitInk" "Faint"
        $hover = Get-DeskmateStaticProperty "KitInk" "Hover"

        $ratio = Assert-Contrast $faint $hover 4.5 "Faint vs Hover"
        Assert-True ($ratio -ge 4.5) "Contrast must be >= 4.5:1 (actual: ${ratio}:1)"
    }

    It "Verifies KitInk.Faint remains distinguishable from KitInk.Disabled (Disabled is dimmer than Faint)" {
        $faint = Get-DeskmateStaticProperty "KitInk" "Faint"
        $disabled = Get-DeskmateStaticProperty "KitInk" "Disabled"
        $window = Get-DeskmateStaticProperty "KitInk" "Window"

        $faintLum = Get-WcagLuminance $faint
        $disabledLum = Get-WcagLuminance $disabled
        $windowLum = Get-WcagLuminance $window

        # In light mode (white window), faint must be darker (further from white) than disabled
        # or disabled is dimmer (closer to background)
        $distFaint = [Math]::Abs($windowLum - $faintLum)
        $distDisabled = [Math]::Abs($windowLum - $disabledLum)

        Assert-True ($distFaint -gt $distDisabled) "Faint ($faintLum) must have greater contrast from Window than Disabled ($disabledLum)"
    }
}
