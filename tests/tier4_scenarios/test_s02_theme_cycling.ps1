# test_s02_theme_cycling.ps1
# Tier 4 scenario S02: settings theme switching and accent preset cycling.

$commonDir = Join-Path (Split-Path -Parent $PSScriptRoot) "common"
. (Join-Path $commonDir "TestHarness.ps1")
. (Join-Path $commonDir "DeskmateLoader.ps1")

Describe "Tier 4 Scenario: S02 Theme Switcher" {
    $flags = [System.Reflection.BindingFlags]'Public,NonPublic,Instance,Static'

    BeforeEach {
        Reset-DeskmateTheme -Light $false
    }

    It "cycles every accent preset through both themes with readable contrast" {
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
                Assert-Equal $table.Light (Get-DeskmateStaticField "Ink" "Light") "Theme mode must follow Apply()"

                $accent = Get-DeskmateStaticProperty "KitInk" "Accent"
                $onAccent = Get-DeskmateStaticProperty "KitInk" "OnAccent"
                $onRatio = Get-DeskmateContrast $onAccent $accent
                Assert-True ($onRatio -ge 4.5) "Accent [$name] light=$($table.Light): OnAccent $onRatio < 4.5:1"
                if ($table.Light) {
                    $surfaceRatio = Get-DeskmateContrast $accent ([System.Drawing.ColorTranslator]::FromHtml("#F5F5F5"))
                    Assert-True ($surfaceRatio -ge 3.5) "Accent [$name] light: non-text contrast $surfaceRatio < 3.5:1"
                }
                $checked++
            }
        }
        Assert-Equal 16 $checked "Both accent tables must be exercised, including Monochrome"
    }

    It "keeps Faint readable on every settings surface in both themes" {
        foreach ($light in @($false, $true)) {
            Reset-DeskmateTheme -Light $light
            $faint = Get-DeskmateStaticProperty "KitInk" "Faint"
            foreach ($surfaceName in @("Side", "Group", "Hover", "Line")) {
                $surface = Get-DeskmateStaticProperty "KitInk" $surfaceName
                $ratio = Get-DeskmateContrast $faint $surface
                Assert-True ($ratio -ge 4.5) "Faint on $surfaceName light=$light is $ratio, expected >= 4.5:1"
            }
        }
    }
}
