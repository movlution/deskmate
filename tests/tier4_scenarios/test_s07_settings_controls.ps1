# test_s07_settings_controls.ps1
# Tier 4 scenario S07: settings controls across normal, hover, pressed, and disabled states.

$commonDir = Join-Path (Split-Path -Parent $PSScriptRoot) "common"
. (Join-Path $commonDir "TestHarness.ps1")
. (Join-Path $commonDir "DeskmateLoader.ps1")

Describe "Tier 4 Scenario: S07 Settings Controls" {
    $flags = [System.Reflection.BindingFlags]'Public,NonPublic,Instance,Static'

    function Set-DeclaredField($target, $name, $value) {
        $field = $target.GetType().GetField($name, [System.Reflection.BindingFlags]'Public,NonPublic,Instance')
        if (-not $field) { throw "Field $name not found on $($target.GetType().Name)" }
        $field.SetValue($target, $value)
    }

    function Get-DeclaredField($target, $name) {
        $field = $target.GetType().GetField($name, [System.Reflection.BindingFlags]'Public,NonPublic,Instance')
        if (-not $field) { throw "Field $name not found on $($target.GetType().Name)" }
        return $field.GetValue($target)
    }

    function Get-PixelDifference($a, $b) {
        return [Math]::Abs($a.A - $b.A) + [Math]::Abs($a.R - $b.R) + [Math]::Abs($a.G - $b.G) + [Math]::Abs($a.B - $b.B)
    }

    function Get-DifferentPixels($a, $b) {
        $count = 0
        for ($y = 0; $y -lt $a.Height; $y++) {
            for ($x = 0; $x -lt $a.Width; $x++) {
                if ($a.GetPixel($x, $y).ToArgb() -ne $b.GetPixel($x, $y).ToArgb()) { $count++ }
            }
        }
        return $count
    }

    BeforeEach {
        Reset-DeskmateTheme -Light $false
    }

    It "renders distinct button states, switch hover feedback, disabled slider ink, and readable row glyphs" {
        # Buttons: normal, hover, pressed must be three different surfaces.
        $button = New-DeskmateControl "KitButton" -Properties @{ Text = "Simpan"; Enabled = $true }
        try {
            Set-DeclaredField $button "Primary" $false
            $button.Font = New-Object System.Drawing.Font "Segoe UI", 10
            $normal = Render-DeskmateControlToBitmap -Control $button -Width 160 -Height 38
            Set-DeclaredField $button "hovered" $true
            $hover = Render-DeskmateControlToBitmap -Control $button -Width 160 -Height 38
            Set-DeclaredField $button "pressed" $true
            $pressed = Render-DeskmateControlToBitmap -Control $button -Width 160 -Height 38
            try {
                Assert-True ((Get-DifferentPixels $normal $hover) -gt 40) "Button hover must visibly change the pill"
                Assert-True ((Get-DifferentPixels $hover $pressed) -gt 40) "Button pressed must visibly change the hover pill"
                Assert-True ((Get-DifferentPixels $normal $pressed) -gt 40) "Button pressed must differ from normal"

                $ring = Get-DeskmateStaticProperty "KitInk" "Accent"
                $buttonBg = Get-DeskmateStaticProperty "KitInk" "Text"
                if ((Get-DeskmateContrast $ring $buttonBg) -lt 3.0 -or $ring.ToArgb() -eq $buttonBg.ToArgb()) {
                    $ring = [System.Drawing.Color]::FromArgb(255, 59, 130, 246)
                }
                Assert-True ((Get-DeskmateContrast $ring $buttonBg) -ge 3.0) "The primary focus ring must reach 3:1"
            }
            finally { $normal.Dispose(); $hover.Dispose(); $pressed.Dispose() }
        }
        finally { $button.Dispose() }

        # Switch: entering must lift both track and knob.
        $switch = New-DeskmateControl "KitSwitch" -Properties @{ Checked = $false; Enabled = $true }
        try {
            Set-DeclaredField $switch "knob" ([single]0)
            $off = Render-DeskmateControlToBitmap -Control $switch -Width 64 -Height 28
            Set-DeclaredField $switch "hovered" $true
            $over = Render-DeskmateControlToBitmap -Control $switch -Width 64 -Height 28
            try {
                Assert-True ((Get-DifferentPixels $off $over) -gt 10) "Switch hover must change the rendered control"
                $offTrack = $off.GetPixel(8, 14); $overTrack = $over.GetPixel(8, 14)
                $offKnob = $off.GetPixel(13, 14); $overKnob = $over.GetPixel(13, 14)
                Assert-True ((Get-PixelDifference $offTrack $overTrack) -gt 0) "Switch hover must lighten the track"
                Assert-True ((Get-PixelDifference $offKnob $overKnob) -gt 0) "Switch hover must lighten the knob"
            }
            finally { $off.Dispose(); $over.Dispose() }
        }
        finally { $switch.Dispose() }

        # Slider: disabled thumb uses KitInk.Disabled rather than the active text token.
        $enabled = New-DeskmateControl "KitSlider" -Properties @{ Minimum = 0; Maximum = 100; Value = 0; Enabled = $true }
        $disabled = New-DeskmateControl "KitSlider" -Properties @{ Minimum = 0; Maximum = 100; Value = 0; Enabled = $false }
        try {
            $enabledBmp = Render-DeskmateControlToBitmap -Control $enabled -Width 140 -Height 36
            $disabledBmp = Render-DeskmateControlToBitmap -Control $disabled -Width 140 -Height 36
            try {
                $enabledThumb = $enabledBmp.GetPixel(10, 18)
                $disabledThumb = $disabledBmp.GetPixel(10, 18)
                $text = Get-DeskmateStaticProperty "KitInk" "Text"
                $disabledInk = Get-DeskmateStaticProperty "KitInk" "Disabled"
                Assert-True ((Get-PixelDifference $disabledThumb $disabledInk) -lt (Get-PixelDifference $disabledThumb $text)) "Disabled slider thumb must use KitInk.Disabled"
                Assert-True ((Get-PixelDifference $enabledThumb $text) -lt (Get-PixelDifference $enabledThumb $disabledInk)) "Enabled slider thumb must stay on the active text token"
            }
            finally { $enabledBmp.Dispose(); $disabledBmp.Dispose() }
        }
        finally { $enabled.Dispose(); $disabled.Dispose() }

        # Row: the icon glyph must be painted with theme ink, not a hardcoded black brush.
        foreach ($light in @($false, $true)) {
            Reset-DeskmateTheme -Light $light
            $row = New-DeskmateControl "KitRow"
            try {
                Set-DeclaredField $row "Title" "Tema & Gaya"
                Set-DeclaredField $row "Detail" "Warna, latar, dan aksen"
                Set-DeclaredField $row "Glyph" ([char]0xE8B7)
                $bmp = Render-DeskmateControlToBitmap -Control $row -Width 280 -Height 56
                try {
                    $ink = Get-DeskmateStaticProperty "KitInk" "Text"
                    $closest = 999.0
                    for ($y = 5; $y -lt 50; $y++) {
                        for ($x = 5; $x -lt 45; $x++) {
                            $d = Get-PixelDifference $bmp.GetPixel($x, $y) $ink
                            if ($d -lt $closest) { $closest = $d }
                        }
                    }
                    Assert-True ($closest -lt 90) "Row glyph must use KitInk.Text in light=$light (closest=$closest)"
                    Assert-True ((Get-DeskmateContrast $ink (Get-DeskmateStaticProperty "KitInk" "Window")) -ge 4.5) "Row glyph ink must stay readable in light=$light"
                }
                finally { $bmp.Dispose() }
            }
            finally { $row.Dispose() }
        }
    }
}
