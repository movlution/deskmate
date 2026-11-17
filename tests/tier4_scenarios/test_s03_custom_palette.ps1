# test_s03_custom_palette.ps1
# Tier 4 scenario S03: high-contrast custom palette derivation and persistence.

$commonDir = Join-Path (Split-Path -Parent $PSScriptRoot) "common"
. (Join-Path $commonDir "TestHarness.ps1")
. (Join-Path $commonDir "DeskmateLoader.ps1")

Describe "Tier 4 Scenario: S03 Custom Palette" {
    $flags = [System.Reflection.BindingFlags]'Public,NonPublic,Instance,Static'
    $islandType = Get-DeskmateType "Island"
    $inkType = Get-DeskmateType "Ink"

    BeforeEach {
        Reset-DeskmateTheme -Light $false
    }

    It "lifts extreme low-contrast inputs to the palette floors and applies the result" {
        $derive = $islandType.GetMethod("DerivePalette", $flags)
        $setPack = $inkType.GetMethod("SetPack", $flags)
        $apply = $inkType.GetMethod("Apply", $flags)
        $dict = $null
        try {
            $bg = [System.Drawing.Color]::White
            $low = [System.Drawing.Color]::FromArgb(255, 255, 255, 0)
            $before = Get-DeskmateContrast $low $bg
            Assert-True ($before -lt 2.0) "The yellow-on-white input must be low contrast before derivation"

            $dict = $derive.Invoke($null, @($bg, $low, $low))
            Assert-NotNull $dict "DerivePalette must return a palette dictionary"

            $text = [System.Drawing.ColorTranslator]::FromHtml($dict["text"])
            $sub = [System.Drawing.ColorTranslator]::FromHtml($dict["sub"])
            $faint = [System.Drawing.ColorTranslator]::FromHtml($dict["faint"])
            $cardBg = [System.Drawing.ColorTranslator]::FromHtml($dict["card.bg"])
            $cardText = [System.Drawing.ColorTranslator]::FromHtml($dict["card.text"])
            $cardAccent = [System.Drawing.ColorTranslator]::FromHtml($dict["card.accent"])
            $cardOnAccent = [System.Drawing.ColorTranslator]::FromHtml($dict["card.onAccent"])

            Assert-NotEqual $text.ToArgb() $low.ToArgb() "The palette must adjust the unusable yellow text"
            Assert-True ((Get-DeskmateContrast $text $bg) -ge 7.0) "Derived text must reach 7:1"
            Assert-True ((Get-DeskmateContrast $sub $bg) -ge 4.5) "Derived sub must reach 4.5:1"
            Assert-True ((Get-DeskmateContrast $faint $bg) -ge 3.0) "Derived faint must reach 3:1"
            Assert-True ((Get-DeskmateContrast $cardText $cardBg) -ge 4.5) "Derived card.text must stay readable on card.bg"
            Assert-True ((Get-DeskmateContrast $cardOnAccent $cardAccent) -ge 4.5) "Derived card.onAccent must stay readable on card.accent"

            $setPack.Invoke($null, (,$dict))
            $apply.Invoke($null, @([bool]$true))
            $base = Get-DeskmateStaticField "Ink" "Base"
            Assert-Equal $text.ToArgb() $base.Text.ToArgb() "Derived text must persist into Ink.Base"
            Assert-Equal $bg.ToArgb() $base.Bg.ToArgb() "Chosen background must persist into Ink.Base"
            Assert-True ((Get-DeskmateContrast $base.Text $base.Bg) -ge 7.0) "Persisted custom palette must keep its 7:1 floor"
        }
        finally {
            if ($dict) {
                $setPack.Invoke($null, (,[System.Collections.Generic.Dictionary[string,string]]::new()))
                $apply.Invoke($null, @([bool]$false))
            }
            Reset-DeskmateTheme -Light $false
        }
    }
}
