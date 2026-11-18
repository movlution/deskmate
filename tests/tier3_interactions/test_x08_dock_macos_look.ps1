# test_x08_dock_macos_look.ps1
# Tier 3: the dock keeps a macOS-like composition: free-floating rounded
# icons, compact widget slots, and small running dots instead of plates.

$commonDir = Join-Path (Split-Path -Parent $PSScriptRoot) "common"
. (Join-Path $commonDir "TestHarness.ps1")
. (Join-Path $commonDir "DeskmateLoader.ps1")

Describe "Tier 3: Dock macOS composition" {
    $flags = [System.Reflection.BindingFlags]'Public,NonPublic,Instance,Static'
    $dockType = Get-DeskmateType "DockWindow"
    $painterType = Get-DeskmateType "Painter"
    $appType = Get-DeskmateType "DockApp"
    $slotType = $dockType.GetNestedType("Slot", $flags)
    $toneType = Get-DeskmateType "Tone"

    function New-TestDock {
        $dock = [Activator]::CreateInstance($dockType, $flags, $null, @($null), $null)
        $painter = [Activator]::CreateInstance($painterType, $true)
        $dockType.GetField("p", $flags).SetValue($dock, $painter)
        return @{ Dock = $dock; Painter = $painter }
    }

    function Set-PainterSurface($painter, $bitmap, $tone) {
        $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
        $graphics.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
        $painterType.GetField("G", $flags).SetValue($painter, $graphics)
        $painterType.GetField("T", $flags).SetValue($painter, $tone)
        $painterType.GetField("Alpha", $flags).SetValue($painter, [single]1)
        $painterType.GetField("HalfPx", $flags).SetValue($painter, [single]0.5)
        return $graphics
    }

    function New-TestApp($key, $name) {
        $app = [Activator]::CreateInstance($appType, $true)
        $app.Key = $key
        $app.Name = $name
        $app.Pinned = $true
        return $app
    }

    function New-TestSlot($app, $rect) {
        $slot = [Activator]::CreateInstance($slotType, $true)
        $slot.Id = "app:" + $app.Key
        $slot.App = $app
        $slot.Icon = $rect
        $slot.Hit = $rect
        $slot.Show = 1
        return $slot
    }

    function Measure-AlphaBounds($bitmap) {
        $minX = 9999; $maxX = -1; $minY = 9999; $maxY = -1
        for ($y = 0; $y -lt $bitmap.Height; $y++) {
            for ($x = 0; $x -lt $bitmap.Width; $x++) {
                if ($bitmap.GetPixel($x, $y).A -gt 0) {
                    if ($x -lt $minX) { $minX = $x }
                    if ($x -gt $maxX) { $maxX = $x }
                    if ($y -lt $minY) { $minY = $y }
                    if ($y -gt $maxY) { $maxY = $y }
                }
            }
        }
        return @{ Width = ($maxX - $minX + 1); Height = ($maxY - $minY + 1); Any = ($maxX -ge 0) }
    }

    BeforeEach {
        Reset-DeskmateTheme -Light $false
    }

    It "1. reserves dock height for the artwork, not for a taskbar tile" {
        $glyphK = [single]$dockType.GetField("GlyphK", $flags).GetRawConstantValue()
        $padTop = [single]$dockType.GetField("PadTop", $flags).GetRawConstantValue()
        $padBottom = [single]$dockType.GetField("PadBottom", $flags).GetRawConstantValue()
        $media = [single]$dockType.GetMethod("WidgetW", $flags).Invoke($null, @("media"))
        $weather = [single]$dockType.GetMethod("WidgetW", $flags).Invoke($null, @("weather"))

        Write-Host "       GlyphK=$glyphK padding=$($padTop + $padBottom) media=$media weather=$weather" -ForegroundColor Gray
        Assert-True ($glyphK -ge 0.9) "App artwork must fill at least 90% of its slot (got $glyphK)"
        Assert-True (($padTop + $padBottom) -le 14) "The dock must stay a compact capsule (padding=$($padTop + $padBottom))"
        Assert-True ($media -le 56) "Media must be an icon-sized slot, not a media panel (got $media)"
        Assert-True ($weather -le 68) "Weather must be an icon-sized slot (got $weather)"
    }

    It "2. draws an app icon without a permanent plate behind it" {
        $parts = New-TestDock
        $dock = $parts.Dock
        $painter = $parts.Painter
        $bitmap = New-Object System.Drawing.Bitmap 200, 80
        $graphics = $null
        $icon = New-Object System.Drawing.Bitmap 64, 64, ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
        try {
            $graphics = Set-PainterSurface $painter $bitmap (Get-DeskmateStaticField "Ink" "Base")
            $dockType.GetField("scale", $flags).SetValue($dock, [single]1)
            $dockType.GetField("iconSize", $flags).SetValue($dock, [single]34)
            $dockType.GetField("barTop", $flags).SetValue($dock, [single]10)
            $dockType.GetField("barBottom", $flags).SetValue($dock, [single]57)
            $dockType.GetField("g", $flags).SetValue($dock, $graphics)

            $app = New-TestApp "plate-test" "Plate Test"
            $slot = New-TestSlot $app ([System.Drawing.RectangleF]::new(70, 12, 34, 34))
            $slots = $dockType.GetField("slots", $flags).GetValue($dock)
            $slots.Add($slot)

            # A fully transparent icon leaves only the chrome to inspect.
            $sourceKey = "plate-test@" + [int]$dockType.GetMethod("IconSourcePx", $flags).Invoke($dock, @())
            $icons = $dockType.GetField("fallbackIcons", $flags).GetValue($dock)
            $icons[$sourceKey] = $icon

            $dockType.GetMethod("DrawApp", $flags).Invoke($dock, @($slot))
            $graphics.Dispose()
            $graphics = $null
            $bounds = Measure-AlphaBounds $bitmap
            Assert-False $bounds.Any "DrawApp must not paint a resting plate around a transparent icon (found $($bounds.Width)x$($bounds.Height) px)"
        }
        finally {
            if ($graphics) { $graphics.Dispose() }
            $bitmap.Dispose()
            $icon.Dispose()
            $dock.Dispose()
        }
    }

    It "3. rounds app artwork into the macOS icon silhouette" {
        $parts = New-TestDock
        $dock = $parts.Dock
        $source = New-Object System.Drawing.Bitmap 64, 64, ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
        try {
            $g = [System.Drawing.Graphics]::FromImage($source)
            $g.Clear([System.Drawing.Color]::White)
            $g.Dispose()
            $dockType.GetField("scale", $flags).SetValue($dock, [single]1)
            $dockType.GetField("iconSize", $flags).SetValue($dock, [single]34)
            $rounded = $dockType.GetMethod("RestIcon", $flags).Invoke($dock, @("round-test", [System.Drawing.Bitmap]$source))
            Assert-NotNull $rounded "RestIcon must return a cached app bitmap"
            $corner = $rounded.GetPixel(0, 0).A
            $edge = $rounded.GetPixel([int]($rounded.Width / 2), 0).A
            $center = $rounded.GetPixel([int]($rounded.Width / 2), [int]($rounded.Height / 2)).A
            Write-Host "       corner alpha=$corner edge alpha=$edge center alpha=$center" -ForegroundColor Gray
            Assert-True ($corner -eq 0) "The icon corner must be clipped to a rounded square (alpha=$corner)"
            Assert-True ($center -eq 255) "The icon centre must remain fully opaque (alpha=$center)"
        }
        finally {
            $source.Dispose()
            $dock.Dispose()
        }
    }

    It "4. keeps the Start launcher free of a resting plate" {
        $parts = New-TestDock
        $dock = $parts.Dock
        $painter = $parts.Painter
        $bitmap = New-Object System.Drawing.Bitmap 120, 60
        $graphics = $null
        try {
            $graphics = Set-PainterSurface $painter $bitmap (Get-DeskmateStaticField "Ink" "Base")
            $dockType.GetField("scale", $flags).SetValue($dock, [single]1)
            $dockType.GetField("iconSize", $flags).SetValue($dock, [single]34)
            $dockType.GetField("g", $flags).SetValue($dock, $graphics)
            $slot = [Activator]::CreateInstance($slotType, $true)
            $slot.Id = "start"
            $slot.Icon = [System.Drawing.RectangleF]::new(40, 10, 34, 34)
            $slot.Hit = $slot.Icon
            $slot.Show = 1
            $dockType.GetMethod("DrawSys", $flags).Invoke($dock, @($slot))
            $graphics.Dispose()
            $graphics = $null
            $corner = $bitmap.GetPixel(45, 15).A
            $bounds = Measure-AlphaBounds $bitmap
            Write-Host "       Start corner alpha=$corner grid bounds=$($bounds.Width)x$($bounds.Height)" -ForegroundColor Gray
            Assert-True ($corner -eq 0) "The Start button must not paint a plate behind the launcher grid (alpha=$corner)"
            Assert-True $bounds.Any "The Start launcher grid must be visible"
            Assert-True ($bounds.Width -le 28 -and $bounds.Height -le 28) "The Start grid must stay inside its slot ($($bounds.Width)x$($bounds.Height))"
        }
        finally {
            if ($graphics) { $graphics.Dispose() }
            $bitmap.Dispose()
            $dock.Dispose()
        }
    }

    It "5. uses a small round running dot instead of a taskbar underline" {

        $parts = New-TestDock
        $dock = $parts.Dock
        $painter = $parts.Painter
        $bitmap = New-Object System.Drawing.Bitmap 80, 50
        $graphics = $null
        try {
            $graphics = Set-PainterSurface $painter $bitmap (Get-DeskmateStaticField "Ink" "Base")
            $dockType.GetField("g", $flags).SetValue($dock, $graphics)
            $dockType.GetField("barBottom", $flags).SetValue($dock, [single]34)
            $indicator = $dockType.GetMethods($flags) | Where-Object { $_.Name -eq "Indicator" -and $_.GetParameters().Count -eq 3 } | Select-Object -First 1
            Assert-NotNull $indicator "Dock indicator overload must resolve"
            [void]$indicator.Invoke($dock, @([single]1, [bool]$false, [System.Drawing.RectangleF]::new(20, 10, 34, 34)))
            $graphics.Dispose()
            $graphics = $null
            $bounds = Measure-AlphaBounds $bitmap
            Write-Host "       running dot bounds=$($bounds.Width)x$($bounds.Height)" -ForegroundColor Gray
            Assert-True $bounds.Any "The running indicator must be visible"
            Assert-True ($bounds.Width -le 5 -and $bounds.Height -le 5) "Running indicator must be a dot, not a wide bar ($($bounds.Width)x$($bounds.Height))"
            Assert-True ([Math]::Abs($bounds.Width - $bounds.Height) -le 1) "Running indicator must be round ($($bounds.Width)x$($bounds.Height))"
        }
        finally {
            if ($graphics) { $graphics.Dispose() }
            $bitmap.Dispose()
            $dock.Dispose()
        }
    }
}
