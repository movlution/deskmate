# test_s01_onboarding.ps1
# Tier 4 scenario S01: first-run onboarding and default theme initialization.

$commonDir = Join-Path (Split-Path -Parent $PSScriptRoot) "common"
. (Join-Path $commonDir "TestHarness.ps1")
. (Join-Path $commonDir "DeskmateLoader.ps1")

Describe "Tier 4 Scenario: S01 First-Run Onboarding" {
    $flags = [System.Reflection.BindingFlags]'Public,NonPublic,Instance,Static'

    BeforeEach {
        Reset-DeskmateTheme -Light $false
    }

    It "renders dark tokens with readable primary contrast" {
        Reset-DeskmateTheme -Light $false
        $ink = Get-DeskmateType "Ink"
        Assert-NotNull $ink "Ink type must resolve after first-run initialization"
        Assert-False (Get-DeskmateStaticField "Ink" "Light") "A dark reset must clear Ink.Light"
        $base = Get-DeskmateStaticField "Ink" "Base"
        Assert-NotNull $base "Ink.Base must exist after Ink.Init()"
        Assert-True ($base.Bg.R -lt 60 -and $base.Text.R -gt 180) "Default tokens must be dark surface with light ink"
        $ratio = Get-DeskmateContrast $base.Text $base.Bg
        Assert-True ($ratio -ge 7.0) "Default primary contrast $ratio must be >= 7.0:1"
    }

    It "resolves all seven type roles and the three documented radius tokens" {
        Invoke-DeskmateStaticMethod "Ink" "Init" @()
        $roles = @("Hero", "Title1", "Title2", "Headline", "Body", "Callout", "Caption")
        $expectedSizes = @{ Hero = 40.0; Title1 = 22.0; Title2 = 17.0; Headline = 14.5; Body = 13.5; Callout = 12.5; Caption = 11.0 }
        foreach ($role in $roles) {
            $font = Get-DeskmateStaticProperty "Ink" $role
            Assert-NotNull $font "Type role [$role] must resolve to a font"
            Assert-True ($font.Size -gt 0) "Type role [$role] must have a positive size"
            Assert-Equal ([double]$expectedSizes[$role]) ([double]$font.Size) "Type role [$role] must use the documented size"
        }

        $fDisplay = Get-DeskmateStaticField "Ink" "FDisplay"
        $families = Get-DeskmateStaticField "Ink" "families"
        Assert-NotNull $families[$fDisplay] "Display family must be selected during initialization"
        Assert-True ($families[$fDisplay] -match "Segoe UI") "Display family must use a Segoe UI fallback chain"
        Assert-NotEqual $families[$fDisplay] ([System.Drawing.SystemFonts]::MessageBoxFont.FontFamily.Name) "Display must not degrade to the system message font"

        Assert-Equal 32.0 ([double](Get-DeskmateStaticField "Ink" "RadiusShell")) "Shell radius token must be 32px"
        Assert-Equal 20.0 ([double](Get-DeskmateStaticField "Ink" "RadiusModule")) "Module radius token must be 20px"
        Assert-Equal 12.0 ([double](Get-DeskmateStaticField "Ink" "RadiusControl")) "Control radius token must be 12px"
    }
}
