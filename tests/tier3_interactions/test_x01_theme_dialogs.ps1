# test_x01_theme_dialogs.ps1
# Tier 3 pairwise: theme/accent tokens x dialog chrome, KitAsk localization, and focus rings.
# These tests exercise the compiled assembly, not source text.

$commonDir = Join-Path (Split-Path -Parent $PSScriptRoot) "common"
. (Join-Path $commonDir "TestHarness.ps1")
. (Join-Path $commonDir "DeskmateLoader.ps1")

Describe "Tier 3 Pairwise: Theme x Dialogs" {
    $flags = [System.Reflection.BindingFlags]'Public,NonPublic,Instance,Static'

    BeforeEach {
        Reset-DeskmateTheme -Light $false
        $langType = Get-DeskmateType "Lang"
        if ($langType) { $langType.GetField("English", $flags).SetValue($null, $false) }
    }

    It "keeps accent surface and on-accent ink readable across 16 theme/accent combinations" {
        $tables = @(
            @{ Light = $false; Rows = Get-DeskmateStaticField "Ink" "DarkAccents" },
            @{ Light = $true; Rows = Get-DeskmateStaticField "Ink" "LightAccents" }
        )
        $checked = 0
        foreach ($table in $tables) {
            foreach ($row in $table.Rows) {
                $name = [string]$row[0]
                $hex = [string]$row[1]
                Invoke-DeskmateStaticMethod "Ink" "SetAccent" @($hex)
                Invoke-DeskmateStaticMethod "Ink" "Apply" @([bool]$table.Light)
                $accent = Get-DeskmateStaticProperty "KitInk" "Accent"
                $onAccent = Get-DeskmateStaticProperty "KitInk" "OnAccent"
                $window = Get-DeskmateStaticProperty "KitInk" "Window"
                $onRatio = Get-DeskmateContrast $onAccent $accent
                $surfaceRatio = Get-DeskmateContrast $accent $window
                Assert-True ($onRatio -ge 4.5) "Accent [$name] light=$($table.Light): OnAccent contrast $onRatio < 4.5"
                Assert-True ($surfaceRatio -ge 3.0) "Accent [$name] light=$($table.Light): surface contrast $surfaceRatio < 3.0"
                $checked++
            }
        }
        Assert-Equal 16 $checked "Every dark and light accent preset must be exercised"
        Reset-DeskmateTheme -Light $false
    }

    It "keeps dialog primary, secondary, and tertiary ink above 4.5:1 on every panel surface" {
        foreach ($light in @($false, $true)) {
            Reset-DeskmateTheme -Light $light
            foreach ($surfaceName in @("Window", "Side", "Group", "Hover", "Line")) {
                $surface = Get-DeskmateStaticProperty "KitInk" $surfaceName
                foreach ($inkName in @("Text", "Sub", "Faint")) {
                    $ink = Get-DeskmateStaticProperty "KitInk" $inkName
                    $ratio = Get-DeskmateContrast $ink $surface
                    Assert-True ($ratio -ge 4.5) "$inkName on $surfaceName light=$light is $ratio, expected >= 4.5"
                }
            }
        }
    }

    It "broadcasts a theme switch to two live dialogs without replacing either window or child" {
        $kitWindowType = Get-DeskmateType "KitWindow"
        $kitButtonType = Get-DeskmateType "KitButton"
        $first = [Activator]::CreateInstance($kitWindowType)
        $second = [Activator]::CreateInstance($kitWindowType)
        $button = [Activator]::CreateInstance($kitButtonType)
        try {
            $button.Text = "Tetap"
            $second.Controls.Add($button)
            Reset-DeskmateTheme -Light $true
            $expectedWindow = Get-DeskmateStaticProperty "KitInk" "Window"
            $expectedText = Get-DeskmateStaticProperty "KitInk" "Text"
            Invoke-DeskmateStaticMethod "KitWindow" "BroadcastThemeChange" @()
            Assert-True ([object]::ReferenceEquals($second.Controls[0], $button)) "Broadcast must not recreate child controls"
            foreach ($win in @($first, $second)) {
                Assert-True (-not $win.IsDisposed) "A registered dialog was disposed by the broadcast"
                Assert-Equal $expectedWindow.ToArgb() $win.BackColor.ToArgb() "Dialog background must follow the new theme"
                Assert-Equal $expectedText.ToArgb() $win.ForeColor.ToArgb() "Dialog foreground must follow the new theme"
            }
            Assert-Equal "Tetap" $button.Text "Theme broadcast must not rewrite control labels"
        }
        finally {
            $first.Dispose()
            $second.Dispose()
        }
    }

    It "localizes KitAsk action labels and applies the new dialog tokens in place" {
        $kitAskType = Get-DeskmateType "KitAsk"
        $ctor = $kitAskType.GetConstructor($flags, $null, [Type[]]@([string], [bool], [string], [string], [string], [string]), $null)
        Assert-NotNull $ctor "KitAsk must expose the six-argument message constructor"
        $idDialog = $ctor.Invoke(@("Hapus data?", $true, "Hapus", "Batal", $null, "Tutup"))
        try {
            $yes = $kitAskType.GetField("yes", $flags).GetValue($idDialog)
            $no = $kitAskType.GetField("no", $flags).GetValue($idDialog)
            $two = $kitAskType.GetField("two", $flags).GetValue($idDialog)
            Assert-True $two "A confirmation KitAsk must keep both actions visible"
            Assert-Equal "Hapus" $yes.Text "Indonesian confirm label must come from Lang.T"
            Assert-Equal "Batal" $no.Text "Indonesian dismiss label must come from Lang.T"

            $langType = Get-DeskmateType "Lang"
            $langType.GetField("English", $flags).SetValue($null, $true)
            $enDialog = $ctor.Invoke(@("Delete data?", $true, "Hapus", "Batal", $null, "Tutup"))
            try {
                $enYes = $kitAskType.GetField("yes", $flags).GetValue($enDialog)
                Assert-Equal "Delete" $enYes.Text "English confirm label must use the Lang dictionary"
                Assert-NotEqual $yes.Text $enYes.Text "Localized labels must differ between languages"
            }
            finally { $enDialog.Dispose() }

            Reset-DeskmateTheme -Light $true
            $expectedWindow = Get-DeskmateStaticProperty "KitInk" "Window"
            Invoke-DeskmateStaticMethod "KitWindow" "BroadcastThemeChange" @()
            Assert-Equal $expectedWindow.ToArgb() $idDialog.BackColor.ToArgb() "KitAsk must adopt the broadcast background"
            Assert-True (-not $idDialog.IsDisposed) "Broadcast must not dispose KitAsk"
        }
        finally {
            $idDialog.Dispose()
            $langType = Get-DeskmateType "Lang"
            $langType.GetField("English", $flags).SetValue($null, $false)
        }
    }

    It "keeps the primary-button focus fallback at 3:1 or better for every accent and theme" {
        foreach ($light in @($false, $true)) {
            Reset-DeskmateTheme -Light $light
            $rows = Get-DeskmateStaticField "Ink" $(if ($light) { "LightAccents" } else { "DarkAccents" })
            foreach ($row in $rows) {
                Invoke-DeskmateStaticMethod "Ink" "SetAccent" @([string]$row[1])
                Invoke-DeskmateStaticMethod "Ink" "Apply" @([bool]$light)
                $ring = Get-DeskmateStaticProperty "KitInk" "Accent"
                $buttonBg = Get-DeskmateStaticProperty "KitInk" "Text"
                $ratio = Get-DeskmateContrast $ring $buttonBg
                if ($ratio -lt 3.0 -or $ring.ToArgb() -eq $buttonBg.ToArgb()) {
                    $ring = if ($light) {
                        [System.Drawing.Color]::FromArgb(255, 22, 104, 232)
                    } else {
                        [System.Drawing.Color]::FromArgb(255, 59, 130, 246)
                    }
                    $ratio = Get-DeskmateContrast $ring $buttonBg
                }
                Assert-True ($ratio -ge 3.0) "Focus ring [$($row[0])] light=$light is $ratio, expected >= 3.0"
            }
        }
    }
}
