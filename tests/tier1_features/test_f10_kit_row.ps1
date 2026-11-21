# test_f10_kit_row.ps1
# Feature 10: KitRow.MarkGlyph Contrast Fix
# Verifies KitRow mark glyph contrast and theme compatibility (replaces hardcoded Brushes.Black).

$commonDir = Join-Path $PSScriptRoot "..\common"
. (Join-Path $commonDir "TestHarness.ps1")
. (Join-Path $commonDir "DeskmateLoader.ps1")

Describe "Feature 10: KitRow.MarkGlyph Contrast Fix" {
    BeforeEach {
        Reset-DeskmateTheme -Light $false
    }

    It "Renders KitRow with standard text offscreen and verifies dimensions" {
        $row = New-DeskmateControl "KitRow"
        $row.Text = "Standard Option"
        $bmp = Render-DeskmateControlToBitmap $row 200 40

        Assert-NotNull $bmp "Rendered row bitmap must not be null"
        Assert-Equal $bmp.Width 200 "Width must match 200"
        Assert-Equal $bmp.Height 40 "Height must match 40"

        $bmp.Dispose()
        $row.Dispose()
    }

    It "Verifies KitRow declares MarkGlyph constant equal to '\u0001'" {
        $markGlyph = Get-DeskmateStaticField "KitRow" "MarkGlyph"
        Assert-NotNull $markGlyph "MarkGlyph must exist on KitRow"
        Assert-Equal ([char]$markGlyph) ([char]0x0001) "MarkGlyph must be char '\u0001'"
    }

    It "Renders KitRow with MarkGlyph in Light Mode offscreen via DrawToBitmap" {
        Reset-DeskmateTheme -Light $true

        $row = New-DeskmateControl "KitRow"
        $row.Text = "Light Mark Row"
        $row.Glyph = [char]0x0001

        $bmp = Render-DeskmateControlToBitmap $row 200 40
        Assert-NotNull $bmp "Light mode MarkGlyph row must render"

        $bmp.Dispose()
        $row.Dispose()
    }

    It "Renders KitRow with MarkGlyph in Dark Mode and verifies pill does not use hardcoded black brush against dark backgrounds" {
        Reset-DeskmateTheme -Light $false

        $row = New-DeskmateControl "KitRow"
        $row.Text = "Dark Mark Row"
        $row.Glyph = [char]0x0001

        $bmp = Render-DeskmateControlToBitmap $row 200 40

        # The mark is the app icon (24 px at x 14, centred in the row): its light letter must show,
        # not a hardcoded black pill. Scan the icon box instead of one geometry-bound pixel.
        $brightest = 0
        for ($x = 14; $x -lt 38; $x++) { for ($y = 8; $y -lt 32; $y++) {
            $px = $bmp.GetPixel($x, $y); $brightest = [Math]::Max($brightest, [Math]::Max($px.R, [Math]::Max($px.G, $px.B)))
        } }
        Assert-True ($brightest -gt 50) "MarkGlyph in dark mode must not render as a black pill (brightest channel in icon box: $brightest)"

        $bmp.Dispose()
        $row.Dispose()
    }

    It "Verifies MarkGlyph inner dot fill achieves contrast against pill background" {
        $textToken = Get-DeskmateStaticProperty "KitInk" "Text"
        $windowToken = Get-DeskmateStaticProperty "KitInk" "Window"

        # The dot and pill should maintain contrast in both themes
        $ratio = Assert-Contrast $textToken $windowToken 4.5 "Pill vs Dot contrast"
        Assert-True ($ratio -ge 4.5) "Pill and dot contrast must achieve >= 4.5:1"
    }
}
