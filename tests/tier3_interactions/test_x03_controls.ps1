# test_x03_controls.ps1
# Tier 3 pairwise: accent/theme state x KitSwitch, KitButton, KitSlider, and KitRow.

$commonDir = Join-Path (Split-Path -Parent $PSScriptRoot) "common"
. (Join-Path $commonDir "TestHarness.ps1")
. (Join-Path $commonDir "DeskmateLoader.ps1")

Describe "Tier 3 Pairwise: Controls x Theme" {
    $flags = [System.Reflection.BindingFlags]'Public,NonPublic,Instance,Static'

    function Invoke-Declared($target, $name, [object[]]$arguments) {
        $method = $target.GetType().GetMethod($name, [System.Reflection.BindingFlags]'Public,NonPublic,Instance')
        if (-not $method) { throw "Method $name not found on $($target.GetType().Name)" }
        return $method.Invoke($target, $arguments)
    }

    function Set-DeclaredField($target, $name, $value) {
        $field = $target.GetType().GetField($name, [System.Reflection.BindingFlags]'Public,NonPublic,Instance')
        if (-not $field) { throw "Field $name not found on $($target.GetType().Name)" }
        $field.SetValue($target, $value)
    }

    function Color-Distance($a, $b) {
        return [Math]::Sqrt([Math]::Pow($a.R - $b.R, 2) + [Math]::Pow($a.G - $b.G, 2) + [Math]::Pow($a.B - $b.B, 2))
    }

    BeforeEach {
        Reset-DeskmateTheme -Light $false
    }

    It "moves the switch track from line to the current accent and dims its knob when disabled" {
        foreach ($light in @($false, $true)) {
            Reset-DeskmateTheme -Light $light
            foreach ($accentHex in @("#4C8DFF", "#FF6B9A")) {
                Invoke-DeskmateStaticMethod "Ink" "SetAccent" @($accentHex)
                Invoke-DeskmateStaticMethod "Ink" "Apply" @([bool]$light)
                $switch = New-DeskmateControl "KitSwitch" -Properties @{ Checked = $true; Enabled = $true }
                try {
                    Set-DeclaredField $switch "knob" ([single]1)
                    $bmp = Render-DeskmateControlToBitmap -Control $switch -Width 64 -Height 28
                    try {
                        $sample = $bmp.GetPixel(8, 14)
                        $accent = Get-DeskmateStaticProperty "KitInk" "Accent"
                        $line = Get-DeskmateStaticProperty "KitInk" "Line"
                        Assert-True ((Color-Distance $sample $accent) -lt (Color-Distance $sample $line)) "Checked switch track must use the active accent [$accentHex]"
                    }
                    finally { $bmp.Dispose() }
                }
                finally { $switch.Dispose() }
            }

            $off = New-DeskmateControl "KitSwitch" -Properties @{ Checked = $false; Enabled = $true }
            $disabled = New-DeskmateControl "KitSwitch" -Properties @{ Checked = $false; Enabled = $false }
            try {
                Set-DeclaredField $off "knob" ([single]0)
                Set-DeclaredField $disabled "knob" ([single]0)
                $offBmp = Render-DeskmateControlToBitmap -Control $off -Width 64 -Height 28
                $disabledBmp = Render-DeskmateControlToBitmap -Control $disabled -Width 64 -Height 28
                try {
                    $onKnob = $offBmp.GetPixel(13, 14)
                    $offKnob = $disabledBmp.GetPixel(13, 14)
                    $disabledInk = Get-DeskmateStaticProperty "KitInk" "Disabled"
                    $enabledInk = Get-DeskmateStaticProperty "KitInk" "Text"
                    Assert-True ((Color-Distance $onKnob $enabledInk) -lt (Color-Distance $onKnob $disabledInk)) "Enabled switch knob must use primary ink"
                    Assert-True ((Color-Distance $offKnob $disabledInk) -lt (Color-Distance $offKnob $enabledInk)) "Disabled switch knob must use disabled ink"
                }
                finally { $offBmp.Dispose(); $disabledBmp.Dispose() }
            }
            finally { $off.Dispose(); $disabled.Dispose() }
        }
    }

    It "renders a primary button on Text with Window ink at 4.5:1 or better in both themes" {
        foreach ($light in @($false, $true)) {
            Reset-DeskmateTheme -Light $light
            $button = New-DeskmateControl "KitButton" -Properties @{ Text = "Simpan"; Enabled = $true }
            try {
                Set-DeclaredField $button "Primary" $true
                $button.Font = New-Object System.Drawing.Font "Segoe UI", 10
                $bmp = Render-DeskmateControlToBitmap -Control $button -Width 160 -Height 38
                try {
                    $surface = $bmp.GetPixel(14, 19)
                    $text = Get-DeskmateStaticProperty "KitInk" "Text"
                    $window = Get-DeskmateStaticProperty "KitInk" "Window"
                    Assert-True ((Color-Distance $surface $text) -lt 8) "Primary button surface must be the Text token"
                    $ratio = Get-DeskmateContrast $window $text
                    Assert-True ($ratio -ge 4.5) "Primary label contrast in light=$light is $ratio, expected >= 4.5"
                }
                finally { $bmp.Dispose() }
            }
            finally { $button.Dispose() }
        }
    }

    It "steps and clamps a slider from the keyboard without theme state changing the value" {
        foreach ($light in @($false, $true)) {
            Reset-DeskmateTheme -Light $light
            $slider = New-DeskmateControl "KitSlider"
            try {
                $slider.Minimum = 10
                $slider.Maximum = 20
                $slider.Step = 2
                $slider.Value = 15
                $script:lastSliderValue = $null
                $slider.add_Changed({ param($v) $script:lastSliderValue = $v })
                $right = New-Object System.Windows.Forms.KeyEventArgs ([System.Windows.Forms.Keys]::Right)
                $left = New-Object System.Windows.Forms.KeyEventArgs ([System.Windows.Forms.Keys]::Left)
                $homeKey = New-Object System.Windows.Forms.KeyEventArgs ([System.Windows.Forms.Keys]::Home)
                $endKey = New-Object System.Windows.Forms.KeyEventArgs ([System.Windows.Forms.Keys]::End)

                Invoke-Declared $slider "OnKeyDown" @([System.Windows.Forms.KeyEventArgs]$right)
                Assert-Equal 18 $slider.Value "Right must snap to the next step"
                Invoke-Declared $slider "OnKeyDown" @([System.Windows.Forms.KeyEventArgs]$right)
                Assert-Equal 20 $slider.Value "Right must reach the maximum"
                Invoke-Declared $slider "OnKeyDown" @([System.Windows.Forms.KeyEventArgs]$right)
                Assert-Equal 20 $slider.Value "Right must clamp at the maximum"
                Invoke-Declared $slider "OnKeyDown" @([System.Windows.Forms.KeyEventArgs]$left)
                Assert-Equal 18 $slider.Value "Left must move down one step"
                Invoke-Declared $slider "OnKeyDown" @([System.Windows.Forms.KeyEventArgs]$homeKey)
                Assert-Equal 10 $slider.Value "Home must jump to the minimum"
                Invoke-Declared $slider "OnKeyDown" @([System.Windows.Forms.KeyEventArgs]$endKey)
                Assert-Equal 20 $slider.Value "End must jump to the maximum"
                Assert-Equal 20 $script:lastSliderValue "Changed must fire with the final value"
            }
            finally { $slider.Dispose() }
        }
    }

    It "paints an enabled slider thumb brighter than its disabled thumb on the same track" {
        Reset-DeskmateTheme -Light $false
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
                $track = Get-DeskmateStaticProperty "KitInk" "Line"
                Assert-True ((Color-Distance $enabledThumb $text) -lt (Color-Distance $enabledThumb $disabledInk)) "Enabled thumb must be primary ink"
                Assert-True ((Color-Distance $disabledThumb $disabledInk) -lt (Color-Distance $disabledThumb $text)) "Disabled thumb must be disabled ink"
                $enabledRatio = Get-DeskmateContrast $enabledThumb $track
                $disabledRatio = Get-DeskmateContrast $disabledThumb $track
                Assert-True ($enabledRatio -gt $disabledRatio) "Disabled thumb contrast $disabledRatio must be lower than enabled $enabledRatio"
            }
            finally { $enabledBmp.Dispose(); $disabledBmp.Dispose() }
        }
        finally { $enabled.Dispose(); $disabled.Dispose() }
    }

    It "keeps long row titles and details on the row surface with readable Sub/Faint ink in both themes" {
        foreach ($light in @($false, $true)) {
            Reset-DeskmateTheme -Light $light
            $row = New-DeskmateControl "KitRow"
            try {
                Set-DeclaredField $row "Title" ("Judul pengaturan yang sangat panjang " * 4)
                Set-DeclaredField $row "Detail" ("Detail penjelasan yang juga panjang dan harus dipotong " * 4)
                Set-DeclaredField $row "Glyph" ([char]0xE8B7)
                $bmp = Render-DeskmateControlToBitmap -Control $row -Width 280 -Height 56
                try {
                    $group = Get-DeskmateStaticProperty "KitInk" "Group"
                    $sub = Get-DeskmateStaticProperty "KitInk" "Sub"
                    $faint = Get-DeskmateStaticProperty "KitInk" "Faint"
                    Assert-True ((Get-DeskmateContrast $sub $group) -ge 4.5) "Row detail ink must stay above 4.5:1 in light=$light"
                    Assert-True ((Get-DeskmateContrast $faint $group) -ge 4.5) "Row tertiary ink must stay above 4.5:1 in light=$light"
                    $nonEmpty = 0
                    for ($y = 0; $y -lt $bmp.Height; $y += 2) {
                        for ($x = 0; $x -lt $bmp.Width; $x += 2) {
                            $c = $bmp.GetPixel($x, $y)
                            if ($c.R -ne $c.G -or $c.G -ne $c.B -or $c.R -gt 20) { $nonEmpty++ }
                        }
                    }
                    Assert-True ($nonEmpty -gt 50) "Row render must contain title/detail/glyph ink in light=$light"
                }
                finally { $bmp.Dispose() }
            }
            finally { $row.Dispose() }
        }
    }
}

