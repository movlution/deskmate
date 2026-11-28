# test_x08_dock_full_width_layout.ps1
# Tier 3: full-width dock splits widgets to the left, apps to the center, and tray/clock to the right.

$commonDir = Join-Path (Split-Path -Parent $PSScriptRoot) "common"
. (Join-Path $commonDir "TestHarness.ps1")
. (Join-Path $commonDir "DeskmateLoader.ps1")

Describe "Tier 3: Dock full-width taskbar layout" {
    $flags = [System.Reflection.BindingFlags]'Public,NonPublic,Instance,Static'
    $islandType = Get-DeskmateType "Island"
    $dockType = Get-DeskmateType "DockWindow"
    $settingsType = Get-DeskmateType "Settings"
    $dockAppType = Get-DeskmateType "DockApp"
    $weatherType = Get-DeskmateType "WeatherNow"
    $taskbarType = Get-DeskmateType "Taskbar"

    function New-LayoutFixture([string]$style, [bool]$replace, [int]$monitorWidth, [int]$appCount, [bool]$withWeather) {
        Reset-DeskmateTheme -Light $false
        $isle = [System.Runtime.Serialization.FormatterServices]::GetUninitializedObject($islandType)
        $settings = [Activator]::CreateInstance($settingsType, $true)
        $settings.Set("dockbar.style", $style)
        $settings.Set("dockbar.replace", $replace)
        $settings.Set("dockbar.size", "m")
        $settings.Set("dockbar.work", $true)
        $settings.Set("dockbar.shelf", $false)
        $settings.Set("dockbar.w.weather", $withWeather)
        $settings.Set("dockbar.w.clock", $false)
        $settings.Set("dockbar.w.media", $false)
        $settings.Set("dockbar.w.ai", $false)
        $settings.Set("dockbar.w.timer", $false)
        $settings.Set("dockbar.w.battery", $false)
        $settings.Set("dockbar.w.system", $false)
        $settings.Set("weather", $withWeather)
        $settings.Set("pray.city", "Jakarta")

        $islandType.GetField("settings", $flags).SetValue($isle, $settings)
        $islandType.GetField("hasBattery", $flags).SetValue($isle, $true)
        $islandType.GetField("battery", $flags).SetValue($isle, 88)
        $islandType.GetField("onAc", $flags).SetValue($isle, $true)
        $islandType.GetField("vol", $flags).SetValue($isle, [single]0.6)
        $islandType.GetField("muted", $flags).SetValue($isle, $false)
        $islandType.GetField("dockHeld", $flags).SetValue($isle, (New-Object 'System.Collections.Generic.List[string]'))
        $dwField = $islandType.GetField("dockWins", $flags)
        $dwField.SetValue($isle, [Activator]::CreateInstance($dwField.FieldType, $true))
        if ($withWeather -and $null -ne $weatherType) {
            $w = [Activator]::CreateInstance($weatherType, $true)
            $wfTemp = $weatherType.GetField("Temp", $flags)
            $wfCode = $weatherType.GetField("Code", $flags)
            if ($wfTemp) { $wfTemp.SetValue($w, 26) }
            if ($wfCode) { $wfCode.SetValue($w, 95) }
            $islandType.GetField("weather", $flags).SetValue($isle, $w)
        }

        $netField = $islandType.GetField("net", $flags)
        if ($netField) {
            $net = [System.Runtime.Serialization.FormatterServices]::GetUninitializedObject($netField.FieldType)
            $curField = $netField.FieldType.GetField("Current", $flags)
            if ($curField) {
                $cur = [Activator]::CreateInstance($curField.FieldType, $true)
                $curField.SetValue($net, $cur)
            }
            $netField.SetValue($isle, $net)
        }

        $dock = [Activator]::CreateInstance($dockType, $flags, $null, @($isle), $null)
        $dockType.GetField("scale", $flags).SetValue($dock, [single]1)
        $dockType.GetField("mon", $flags).SetValue($dock, (New-Object System.Drawing.Rectangle 0, 0, $monitorWidth, 1080))
        $dockType.GetField("area", $flags).SetValue($dock, (New-Object System.Drawing.Rectangle 0, 1022, $monitorWidth, 58))
        $dockType.GetField("shown", $flags).SetValue($dock, $true)
        $reveal = $dockType.GetField("reveal", $flags).GetValue($dock)
        [void]$reveal.GetType().GetMethod("Snap", $flags).Invoke($reveal, @([single]1))

        $apps = $dockType.GetField("apps", $flags).GetValue($dock)
        for ($i = 0; $i -lt $appCount; $i++) {
            $app = [Activator]::CreateInstance($dockAppType, $true)
            $app.Key = "app$i"
            $app.Name = "App $i"
            $app.Pinned = $true
            [void]$app.Show.GetType().GetMethod("Snap", $flags).Invoke($app.Show, @([single]1))
            [void]$app.Grow.GetType().GetMethod("Snap", $flags).Invoke($app.Grow, @([single]1))
            [void]$apps.Add($app)
        }

        [void]$dockType.GetMethod("Place", $flags).Invoke($dock, @())
        [void]$dockType.GetMethod("Compose", $flags).Invoke($dock, @())
        return @{ Island = $isle; Settings = $settings; Dock = $dock }
    }

    It "1. places widgets on the left, apps strictly centered, and tray/clock on the right in full-width mode" {
        $oldPaused = $taskbarType.GetField("Paused", $flags).GetValue($null)
        $taskbarType.GetField("Paused", $flags).SetValue($null, $false)
        $fx = New-LayoutFixture "full" $true 1920 6 $true
        try {
            $d = $fx.Dock
            $left = $dockType.GetField("leftPill", $flags).GetValue($d)
            $right = $dockType.GetField("rightPill", $flags).GetValue($d)
            $center = $dockType.GetField("centerPill", $flags).GetValue($d)
            $sepA = [single]$dockType.GetField("sepA", $flags).GetValue($d)
            $sepB = [single]$dockType.GetField("sepB", $flags).GetValue($d)
            $slots = $dockType.GetField("slots", $flags).GetValue($d)
            $padR = [single]$dockType.GetField("padR", $flags).GetValue($d)

            Assert-False $left.IsEmpty "Weather widget should populate leftPill"
            Assert-False $right.IsEmpty "Tray/clock should populate rightPill when replacing taskbar"
            Assert-Equal 8.0 ([double]$left.X) "Widgets must pin to the left edge (Side = 8 DIP)"
            Assert-True ([Math]::Abs([double]($right.Right + $padR) - 1920.0) -lt 0.1) "Right pill + Show Desktop pad must reach the exact right screen edge"
            Assert-Equal 0.0 ([double]$center.X) "Full-width centerPill must start at 0"
            Assert-Equal 1920.0 ([double]$center.Width) "Full-width centerPill must span full monitor width"
            Assert-True ([single]::IsNaN($sepA)) "Full-width layout must omit outer left divider"
            Assert-True ([single]::IsNaN($sepB)) "Full-width layout must omit outer right divider"

            $first = $slots[0].Icon
            $last = $slots[$slots.Count - 1].Icon
            $slotsMid = ($first.Left + $last.Right) / 2.0
            Assert-True ([Math]::Abs($slotsMid - 960.0) -lt 1.0) "App/Start slots must stay centered at monitor midpoint (got $slotsMid)"
            Assert-True ($left.Right + 20 -lt $first.Left) "Left widgets must be separated from centered apps"
            Assert-True ($last.Right + 20 -lt $right.Left) "Right tray/clock must be separated from centered apps"

            # Verify Show Desktop sliver reaches x = 1920 when drawn
            $painter = $dockType.GetField("p", $flags).GetValue($d)
            $painter.GetType().GetField("T", $flags).SetValue($painter, (Get-DeskmateStaticField "Ink" "Base"))
            [void]$dockType.GetMethod("DrawSpan", $flags).Invoke($d, @($right, $false))
            $hits = $dockType.GetField("hits", $flags).GetValue($d)
            $desk = $null
            foreach ($h in $hits) { if ($h.Value -eq "desk") { $desk = $h.Key; break } }
            Assert-True ($null -ne $desk) "Show Desktop sliver hit rect must be registered"
            Assert-True ([Math]::Abs([double]$desk.Right - 1920.0) -lt 0.1) "Show Desktop sliver must reach the far right edge (got $($desk.Right))"
        }
        finally {
            $fx.Dock.Dispose()
            $taskbarType.GetField("Paused", $flags).SetValue($null, $oldPaused)
        }
    }

    It "2. keeps centered apps stationary when widgets are toggled on/off in full-width mode" {
        $oldPaused = $taskbarType.GetField("Paused", $flags).GetValue($null)
        $taskbarType.GetField("Paused", $flags).SetValue($null, $false)
        $fxA = New-LayoutFixture "full" $true 1920 6 $true
        $fxB = New-LayoutFixture "full" $true 1920 6 $false
        try {
            $slotsA = $dockType.GetField("slots", $flags).GetValue($fxA.Dock)
            $slotsB = $dockType.GetField("slots", $flags).GetValue($fxB.Dock)
            $midA = ($slotsA[0].Icon.Left + $slotsA[$slotsA.Count - 1].Icon.Right) / 2.0
            $midB = ($slotsB[0].Icon.Left + $slotsB[$slotsB.Count - 1].Icon.Right) / 2.0
            Assert-True ([Math]::Abs($midA - $midB) -lt 0.5) "Centered apps must not shift when widgets appear/disappear (with=$midA, without=$midB)"
        }
        finally {
            $fxA.Dock.Dispose()
            $fxB.Dock.Dispose()
            $taskbarType.GetField("Paused", $flags).SetValue($null, $oldPaused)
        }
    }

    It "3. prevents overlap between left widgets, apps, and right tray on narrow screens" {
        $oldPaused = $taskbarType.GetField("Paused", $flags).GetValue($null)
        $taskbarType.GetField("Paused", $flags).SetValue($null, $false)
        $fx = New-LayoutFixture "full" $true 800 12 $true
        try {
            $d = $fx.Dock
            $left = $dockType.GetField("leftPill", $flags).GetValue($d)
            $right = $dockType.GetField("rightPill", $flags).GetValue($d)
            $slots = $dockType.GetField("slots", $flags).GetValue($d)
            $first = $slots[0].Icon
            $last = $slots[$slots.Count - 1].Icon
            if (-not $left.IsEmpty) {
                Assert-True ($left.Right -lt $first.Left) "Left widgets must not overlap apps on narrow monitor ($($left.Right) vs $($first.Left))"
            }
            Assert-True ($last.Right -lt $right.Left) "Apps must not overlap right tray on narrow monitor ($($last.Right) vs $($right.Left))"
        }
        finally {
            $fx.Dock.Dispose()
            $taskbarType.GetField("Paused", $flags).SetValue($null, $oldPaused)
        }
    }

    It "4. keeps floating mode as a single centered capsule with dividers unchanged" {
        $oldPaused = $taskbarType.GetField("Paused", $flags).GetValue($null)
        $taskbarType.GetField("Paused", $flags).SetValue($null, $false)
        $fx = New-LayoutFixture "float" $true 1920 6 $true
        try {
            $d = $fx.Dock
            $left = $dockType.GetField("leftPill", $flags).GetValue($d)
            $right = $dockType.GetField("rightPill", $flags).GetValue($d)
            $center = $dockType.GetField("centerPill", $flags).GetValue($d)
            $sepA = [single]$dockType.GetField("sepA", $flags).GetValue($d)
            $sepB = [single]$dockType.GetField("sepB", $flags).GetValue($d)

            Assert-True ($center.Width -lt 1200) "Floating dock must remain a compact centered capsule (got $($center.Width))"
            Assert-True ($left.X -gt 200) "Floating left widgets must stay inside the centered capsule"
            Assert-True ($right.Right -lt 1720) "Floating right tray must stay inside the centered capsule"
            Assert-False ([single]::IsNaN($sepA)) "Floating dock must still draw left divider"
            Assert-False ([single]::IsNaN($sepB)) "Floating dock must still draw right divider"
        }
        finally {
            $fx.Dock.Dispose()
            $taskbarType.GetField("Paused", $flags).SetValue($null, $oldPaused)
        }
    }
}
