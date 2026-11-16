# test_b07_button_states.ps1
# Boundary & Corner Case Tests for Feature 7: KitButton Interactive States & Focus Ring
# Covers enabled/disabled states, primary styling, focus rings, extreme sizes, and long text.

$commonDir = Join-Path (Split-Path -Parent $PSScriptRoot) "common"
. (Join-Path $commonDir "TestHarness.ps1")
. (Join-Path $commonDir "DeskmateLoader.ps1")

Describe "Tier 2 Boundary Suite: Feature 7 - KitButton Interactive States & Boundaries" {

    BeforeEach {
        Reset-DeskmateTheme -Light $false
    }

    It "Renders disabled KitButton offscreen without GDI+ crash" {
        $btn = New-DeskmateControl "KitButton" @{ Text = "Disabled Action"; Enabled = $false }
        $bmp = Render-DeskmateControlToBitmap $btn 120 36
        Assert-NotNull $bmp "Rendered disabled button bitmap must not be null"
        Assert-Equal $bmp.Width 120 "Width must match"
        Assert-Equal $bmp.Height 36 "Height must match"
        $bmp.Dispose()
        $btn.Dispose()
    }

    It "Renders Primary KitButton vs Normal KitButton across dark and light modes" {
        foreach ($light in @($false, $true)) {
            Reset-DeskmateTheme -Light $light

            $btnNormal = New-DeskmateControl "KitButton" @{ Text = "Normal"; Primary = $false }
            $btnPrimary = New-DeskmateControl "KitButton" @{ Text = "Primary"; Primary = $true }

            $bmpNorm = Render-DeskmateControlToBitmap $btnNormal 100 32
            $bmpPrim = Render-DeskmateControlToBitmap $btnPrimary 100 32

            Assert-NotNull $bmpNorm "Normal button must render in Light=$light"
            Assert-NotNull $bmpPrim "Primary button must render in Light=$light"

            $bmpNorm.Dispose()
            $bmpPrim.Dispose()
            $btnNormal.Dispose()
            $btnPrimary.Dispose()
        }
    }

    It "Handles extreme dimensional boundaries from micro 1x1 to billboard 1000x200" {
        $dimPairs = @(
            @{ W = 1; H = 1 },
            @{ W = 2; H = 2 },
            @{ W = 16; H = 16 },
            @{ W = 500; H = 60 },
            @{ W = 1000; H = 200 }
        )

        foreach ($dp in $dimPairs) {
            $btn = New-DeskmateControl "KitButton" @{ Text = "Size Test" }
            $bmp = Render-DeskmateControlToBitmap $btn $dp.W $dp.H
            Assert-NotNull $bmp "Button must render at $($dp.W)x$($dp.H)"
            Assert-Equal $bmp.Width $dp.W "Bitmap width must match $($dp.W)"
            Assert-Equal $bmp.Height $dp.H "Bitmap height must match $($dp.H)"
            $bmp.Dispose()
            $btn.Dispose()
        }
    }

    It "Handles degenerate text boundaries: null, empty string, and 500-character payload" {
        $textInputs = @($null, "", " ", "   ", ("A" * 500), "Unicode: 🚀 ⭐ ✔ ✗ 日本語 العربية")
        foreach ($txt in $textInputs) {
            $btn = New-DeskmateControl "KitButton" @{ Text = $txt }
            $bmp = Render-DeskmateControlToBitmap $btn 140 36
            Assert-NotNull $bmp "Button must render text input of length $(if ($txt) { $txt.Length } else { 0 })"
            $bmp.Dispose()
            $btn.Dispose()
        }
    }

    It "Verifies focused state rendering with keyboard focus cue enabled" {
        $btn = New-DeskmateControl "KitButton" @{ Text = "Focus Target" }
        # Simulate keyboard focus
        Set-DeskmateStaticField "KitInk" "ByMouse" $false

        $bmp = Render-DeskmateControlToBitmap $btn 120 36
        Assert-NotNull $bmp "Focused button bitmap must render"
        $bmp.Dispose()
        $btn.Dispose()
    }
}
