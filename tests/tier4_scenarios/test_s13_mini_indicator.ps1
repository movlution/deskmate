# test_s13_mini_indicator.ps1
# Tier 4 scenario S13: mini edge indicator size and glanceable contrast.

$commonDir = Join-Path (Split-Path -Parent $PSScriptRoot) "common"
. (Join-Path $commonDir "TestHarness.ps1")
. (Join-Path $commonDir "DeskmateLoader.ps1")

Describe "Tier 4 Scenario: S13 Mini Indicator" {
    $flags = [System.Reflection.BindingFlags]'Public,NonPublic,Instance,Static'

    BeforeEach {
        Reset-DeskmateTheme -Light $false
    }

    It "keeps the mini dot large enough and every status colour at least 3:1 against the host edge" {
        $dotR = [double](Get-DeskmateStaticField "Island" "MiniDotR")
        $track = [double](Get-DeskmateStaticField "Island" "MiniTrackThick")
        $area = [Math]::PI * $dotR * $dotR
        Assert-True ($dotR -ge 2.5) "Mini dot radius $dotR must be at least 2.5px"
        Assert-True ($area -ge 19.6) "Mini dot area $area must be at least 19.6px^2"
        Assert-True ($track -ge 4.0) "Mini status track thickness $track must be at least 4px"
        Assert-True ($track -gt 3.0) "Mini status track must remain thicker than the legacy 3px row"

        foreach ($light in @($false, $true)) {
            Reset-DeskmateTheme -Light $light
            $window = Get-DeskmateStaticProperty "KitInk" "Window"
            $base = Get-DeskmateStaticField "Ink" "Base"
            foreach ($pair in @(
                @{ Name = "active"; Color = $base.Accent },
                @{ Name = "idle"; Color = $base.Sub },
                @{ Name = "attention"; Color = $base.Negative }
            )) {
                $ratio = Get-DeskmateContrast $pair.Color $window
                Assert-True ($ratio -ge 3.0) "Mini $($pair.Name) indicator in light=$light is $ratio, expected >= 3.0:1 against the host edge"
            }
        }
    }
}
