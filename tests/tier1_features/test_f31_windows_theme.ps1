# test_f31_windows_theme.ps1
# Feature 31: Windows-aligned theme system
# Verifies system-first mode normalization, neutral Windows surfaces, and selector order.

$commonDir = Join-Path $PSScriptRoot "..\common"
. (Join-Path $commonDir "TestHarness.ps1")
. (Join-Path $commonDir "DeskmateLoader.ps1")

# Settings.Dir is static readonly: keep the fresh-profile test out of user data.
$f31Tmp = Join-Path ([System.IO.Path]::GetTempPath()) ("deskmate-f31-" + [Guid]::NewGuid().ToString("N"))
[void][System.IO.Directory]::CreateDirectory($f31Tmp)
$env:DESKMATE_DIR = $f31Tmp

Describe "Feature 31: Windows-aligned theme system" {
    $flags = [System.Reflection.BindingFlags]'Public,NonPublic,Static,Instance'
    $islandType = Get-DeskmateType "Island"
    $inkType = Get-DeskmateType "Ink"
    $toneType = Get-DeskmateType "Tone"

    It "Normalizes system, legacy auto, English aliases, and unknown values" {
        $canonical = $islandType.GetMethod("CanonicalThemeMode", $flags)
        Assert-NotNull $canonical "CanonicalThemeMode must exist on Island"

        Assert-Equal "sistem" $canonical.Invoke($null, @([string]$null)) "Missing mode must fall back to Sistem"
        Assert-Equal "sistem" $canonical.Invoke($null, @([string]"")) "Empty mode must fall back to Sistem"
        Assert-Equal "sistem" $canonical.Invoke($null, @([string]"otomatis")) "Legacy otomatis must become Sistem"
        Assert-Equal "sistem" $canonical.Invoke($null, @([string]"system")) "English system alias must become Sistem"
        Assert-Equal "sistem" $canonical.Invoke($null, @([string]"kontras-tinggi")) "Unknown values must fall back to Sistem"
        Assert-Equal "terang" $canonical.Invoke($null, @([string]"TERANG")) "Light must normalize case-insensitively"
        Assert-Equal "terang" $canonical.Invoke($null, @([string]"light")) "English light alias must become Terang"
        Assert-Equal "gelap" $canonical.Invoke($null, @([string]"gelap")) "Dark must stay Gelap"
        Assert-Equal "gelap" $canonical.Invoke($null, @([string]"dark")) "English dark alias must become Gelap"
    }

    It "Defaults a fresh profile to Sistem and maps the current Windows appearance" {
        $island = [Activator]::CreateInstance($islandType, $true)
        try {
            $themeMode = $islandType.GetMethod("ThemeMode", $flags).Invoke($island, @())
            $themeLight = $islandType.GetMethod("ThemeLight", $flags).Invoke($island, @())
            $winDark = $islandType.GetField("winDark", $flags).GetValue($island)
            Assert-Equal "sistem" $themeMode "A profile without a saved theme must default to Sistem"
            Assert-Equal ([bool](-not $winDark)) ([bool]$themeLight) "Sistem must follow the current Windows light/dark setting"
        }
        finally { $island.Dispose() }
    }

    It "Maps selector indices with Sistem first, then Terang and Gelap" {
        $index = $islandType.GetMethod("ThemeIndex", $flags)
        Assert-NotNull $index "ThemeIndex must exist on Island"
        Assert-Equal 0 $index.Invoke($null, @([string]"sistem")) "Sistem must be the first card"
        Assert-Equal 0 $index.Invoke($null, @([string]"otomatis")) "Legacy auto must use the Sistem card"
        Assert-Equal 1 $index.Invoke($null, @([string]"terang")) "Terang must be the middle card"
        Assert-Equal 2 $index.Invoke($null, @([string]"gelap")) "Gelap must be the last card"

        $picker = $islandType.GetNestedType("ThemeCardPicker", [System.Reflection.BindingFlags]'Public,NonPublic')
        Assert-NotNull $picker "ThemeCardPicker must exist"
        $labelsField = $picker.GetField("Labels", $flags)
        Assert-NotNull $labelsField "ThemeCardPicker.Labels must exist"
        $labels = $labelsField.GetValue($null)
        Assert-Equal 3 $labels.Length "The picker must expose exactly three choices"
        Assert-Equal "Sistem" $labels[0] "First label must be Sistem"
        Assert-Equal "Terang" $labels[1] "Second label must be Terang"
        Assert-Equal "Gelap" $labels[2] "Third label must be Gelap"
    }

    It "Exposes the three cards as accessible radio choices and supports arrow keys" {
        $pickerType = $islandType.GetNestedType("ThemeCardPicker", [System.Reflection.BindingFlags]'Public,NonPublic')
        $picker = [Activator]::CreateInstance($pickerType)
        try {
            $picker.Size = New-Object System.Drawing.Size 246, 64
            $picker.SelectedIndex = 0
            $access = $picker.AccessibilityObject
            Assert-Equal 3 $access.GetChildCount() "The picker must expose one accessible child per card"
            Assert-Equal "Sistem" $access.GetChild(0).Name "The first accessible child must be Sistem"
            Assert-Equal "RadioButton" $access.GetChild(0).Role.ToString() "Theme cards must read as radio buttons"
            $selected = [System.Windows.Forms.AccessibleStates]::Selected
            Assert-True (($access.GetChild(0).State -band $selected) -ne 0) "Sistem must be announced as selected"
            Assert-True (($access.GetChild(1).State -band $selected) -eq 0) "Terang must not be announced as selected"

            $key = New-Object System.Windows.Forms.KeyEventArgs ([System.Windows.Forms.Keys]::Right)
            $onKeyDown = $pickerType.GetMethod("OnKeyDown", [System.Reflection.BindingFlags]'NonPublic,Instance')
            $onKeyDown.Invoke($picker, [object[]]@([System.Windows.Forms.KeyEventArgs]$key))
            Assert-Equal 1 $picker.SelectedIndex "Right arrow must move to Terang"
        }
        finally { $picker.Dispose() }
    }

    It "Uses Windows 11 neutral canvas colors instead of pure black and white" {
        $dark = $toneType.GetMethod("Dark", $flags).Invoke($null, @())
        $paper = $toneType.GetMethod("Paper", $flags).Invoke($null, @())

        Assert-Equal ([System.Drawing.ColorTranslator]::FromHtml("#202020").ToArgb()) $dark.Bg.ToArgb() "Dark canvas must match Windows 11 #202020"
        Assert-Equal ([System.Drawing.ColorTranslator]::FromHtml("#FFFFFF").ToArgb()) $dark.Text.ToArgb() "Dark primary ink must be white"
        Assert-Equal ([System.Drawing.ColorTranslator]::FromHtml("#F3F3F3").ToArgb()) $paper.Bg.ToArgb() "Light canvas must match Windows 11 #F3F3F3"
        Assert-Equal ([System.Drawing.ColorTranslator]::FromHtml("#1A1A1A").ToArgb()) $paper.Text.ToArgb() "Light primary ink must be #1A1A1A"
        Assert-True ($dark.IsDark) "Dark tone must still be classified dark"
        Assert-False ($paper.IsDark) "Paper tone must still be classified light"
    }

    It "Keeps cards lighter than the canvas in both themes" {
        Reset-DeskmateTheme -Light $false
        $darkWindow = Get-DeskmateStaticProperty "KitInk" "Window"
        $darkGroup = Get-DeskmateStaticProperty "KitInk" "Group"
        Assert-True ($darkGroup.R -gt $darkWindow.R) "Dark cards must be lifted above the #202020 canvas"

        Reset-DeskmateTheme -Light $true
        $lightWindow = Get-DeskmateStaticProperty "KitInk" "Window"
        $lightGroup = Get-DeskmateStaticProperty "KitInk" "Group"
        Assert-True ($lightGroup.R -gt $lightWindow.R) "Light cards must be lighter than the #F3F3F3 canvas"

        Reset-DeskmateTheme -Light $false
    }

    It "Keeps on-glass secondary ink readable at the darkest bright-wallpaper guard" {
        Reset-DeskmateTheme -Light $false
        $over = $inkType.GetMethod("Over", $flags)
        $alpha = $inkType.GetMethod("A", $flags)
        $onGlass = $toneType.GetMethod("OnGlass", $flags)
        $base = Get-DeskmateStaticField "Ink" "Base"

        $tint = $alpha.Invoke($null, @($base.Bg, [single]0.66))
        $surface = $over.Invoke($null, @($tint, [System.Drawing.Color]::White))
        $glass = $onGlass.Invoke($base, @())
        $sub = $over.Invoke($null, @($glass.Sub, $surface))
        $ratio = Get-DeskmateContrast $sub $surface
        Assert-True ($ratio -ge 4.5) "Secondary ink on bright wallpaper must stay >= 4.5:1 on the charcoal canvas (actual: ${ratio}:1)"
        Assert-True ($glass.Sub.A -ge 228) "OnGlass must raise secondary ink enough for the #202020 canvas"

        Reset-DeskmateTheme -Light $false
    }
}
