# test_s12_dock_flyout.ps1
# Tier 4 scenario S12: dock badge contrast and flyout close target states.

$commonDir = Join-Path (Split-Path -Parent $PSScriptRoot) "common"
. (Join-Path $commonDir "TestHarness.ps1")
. (Join-Path $commonDir "DeskmateLoader.ps1")

Describe "Tier 4 Scenario: S12 Dock Badge And Flyout" {
    $flags = [System.Reflection.BindingFlags]'Public,NonPublic,Instance,Static'

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

    It "keeps the unread badge readable in both themes" {
        $dockType = Get-DeskmateType "DockWindow"
        $painterType = Get-DeskmateType "Painter"
        foreach ($light in @($false, $true)) {
            Reset-DeskmateTheme -Light $light
            $dock = [Activator]::CreateInstance($dockType, $flags, $null, @($null), $null)
            $painter = [Activator]::CreateInstance($painterType, $true)
            $bmp = New-Object System.Drawing.Bitmap 180, 100
            $g = [System.Drawing.Graphics]::FromImage($bmp)
            try {
                $painterType.GetField("G", $flags).SetValue($painter, $g)
                $painterType.GetField("T", $flags).SetValue($painter, (Get-DeskmateStaticField "Ink" "Base"))
                $painterType.GetField("Alpha", $flags).SetValue($painter, [single]1)
                $dockType.GetField("p", $flags).SetValue($dock, $painter)
                [void]$dockType.GetMethod("Badge", $flags).Invoke($dock, @([System.Drawing.RectangleF](New-Object System.Drawing.RectangleF 64, 34, 32, 32), [int]7))

                $negative = (Get-DeskmateStaticField "Ink" "Base").Negative
                $count = 0
                for ($y = 0; $y -lt $bmp.Height; $y++) {
                    for ($x = 0; $x -lt $bmp.Width; $x++) {
                        $c = $bmp.GetPixel($x, $y)
                        if ($c.A -eq 255 -and [Math]::Abs($c.R - $negative.R) -lt 12 -and [Math]::Abs($c.G - $negative.G) -lt 12 -and [Math]::Abs($c.B - $negative.B) -lt 12) { $count++ }
                    }
                }
                Assert-True ($count -gt 40) "Unread badge must use the Negative pill in light=$light (pixels=$count)"
                $badgeInk = if ($light) { [System.Drawing.Color]::White } else { (Get-DeskmateStaticField "Ink" "Base").Bg }
                Assert-True ((Get-DeskmateContrast $badgeInk $negative) -ge 4.5) "Badge ink must reach 4.5:1 on Negative in light=$light"
            }
            finally {
                $g.Dispose()
                $bmp.Dispose()
                $painter.Dispose()
                $dock.Dispose()
            }
        }
    }

    It "gives flyout close buttons visible hover fill and a distinct pressed surface" {
        $dockCardType = Get-DeskmateType "DockCard"
        $dockType = Get-DeskmateType "DockWindow"
        $painterType = Get-DeskmateType "Painter"
        $thumbType = $dockCardType.GetNestedType("Thumb", $flags)
        Assert-NotNull $thumbType "DockCard.Thumb must resolve"
        $card = [System.Runtime.Serialization.FormatterServices]::GetUninitializedObject($dockCardType)
        $dock = [System.Runtime.Serialization.FormatterServices]::GetUninitializedObject($dockType)
        $painter = [Activator]::CreateInstance($painterType, $true)
        $thumbsType = [System.Collections.Generic.List``1].MakeGenericType($thumbType)
        $hitsType = [System.Collections.Generic.List``1].MakeGenericType([System.Collections.Generic.KeyValuePair``2].MakeGenericType([System.Drawing.RectangleF], [string]))
        $thumbs = [Activator]::CreateInstance($thumbsType)
        $hits = [Activator]::CreateInstance($hitsType)
        $thumb = [Activator]::CreateInstance($thumbType, $true)
        $winType = Get-DeskmateType "DockWin"
        $win = [Activator]::CreateInstance($winType, $true)
        $win.H = [IntPtr]1
        $win.Title = "Project window"
        $thumb.Win = $win
        $thumb.Id = [IntPtr]::Zero
        $thumb.Box = New-Object System.Drawing.RectangleF 20, 16, 120, 90
        $thumb.Pic = New-Object System.Drawing.RectangleF 28, 48, 104, 50
        $thumb.Close = New-Object System.Drawing.RectangleF 114, 20, 18, 18
        [void]$thumbs.Add($thumb)

        $dockCardType.GetField("dock", $flags).SetValue($card, $dock)
        $dockCardType.GetField("p", $flags).SetValue($card, $painter)
        $dockCardType.GetField("thumbs", $flags).SetValue($card, $thumbs)
        $dockCardType.GetField("hits", $flags).SetValue($card, $hits)
        $dockCardType.GetField("collapseIndex", $flags).SetValue($card, -1)
        $dockCardType.GetField("scale", $flags).SetValue($card, [single]1)
        $draw = $dockCardType.GetMethod("DrawWindows", $flags)
        Assert-NotNull $draw "DockCard.DrawWindows must be available for the flyout render"

        function New-CardFrame {
            param($hot, $press)
            $bmp = New-Object System.Drawing.Bitmap 180, 140
            $g = [System.Drawing.Graphics]::FromImage($bmp)
            try {
                $g.Clear([System.Drawing.Color]::Transparent)
                $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
                $painterType.GetField("G", $flags).SetValue($painter, $g)
                $painterType.GetField("T", $flags).SetValue($painter, (Get-DeskmateStaticField "Ink" "Base"))
                $painterType.GetField("Alpha", $flags).SetValue($painter, [single]1)
                $painterType.GetField("HalfPx", $flags).SetValue($painter, [single]0.5)
                $dockCardType.GetField("hot", $flags).SetValue($card, $hot)
                $dockCardType.GetField("press", $flags).SetValue($card, $press)
                [void]$draw.Invoke($card, @([single]1))
            }
            finally { $g.Dispose() }
            return ,$bmp
        }

        $frames = New-Object System.Collections.ArrayList
        try {
            $normal = New-CardFrame $null $null
            $hover = New-CardFrame "w1" $null
            $pressed = New-CardFrame "w1" "w1"
            $closeHover = New-CardFrame "xw1" $null
            [void]$frames.Add($normal); [void]$frames.Add($hover); [void]$frames.Add($pressed); [void]$frames.Add($closeHover)

            $normalSurface = $normal.GetPixel(24, 45)
            $hoverSurface = $hover.GetPixel(24, 45)
            $pressedSurface = $pressed.GetPixel(24, 45)
            Assert-True ($normalSurface.ToArgb() -ne $hoverSurface.ToArgb()) "Flyout hover must fill the card surface"
            Assert-True ($hoverSurface.ToArgb() -ne $pressedSurface.ToArgb()) "Flyout pressed must use the pressed surface, not hover"

            $hoverRed = 0
            $normalRed = 0
            for ($y = 18; $y -le 40; $y++) {
                for ($x = 112; $x -le 134; $x++) {
                    $hc = $closeHover.GetPixel($x, $y)
                    $nc = $normal.GetPixel($x, $y)
                    if ($hc.R -gt $hc.G + 30 -and $hc.R -gt $hc.B + 30) { $hoverRed++ }
                    if ($nc.R -gt $nc.G + 30 -and $nc.R -gt $nc.B + 30) { $normalRed++ }
                }
            }
            Assert-True ($hoverRed -gt 20) "The close target must gain a visible Negative fill on hover (red pixels=$hoverRed)"
            Assert-True ($normalRed -eq 0) "The resting close target must not show the destructive fill"
            Assert-True ((Get-DifferentPixels $normal $closeHover) -gt 80) "The close hover state must be visibly distinct from the resting card"
        }
        finally {
            foreach ($frame in $frames) { $frame.Dispose() }
            $painter.Dispose()
        }
    }
}
