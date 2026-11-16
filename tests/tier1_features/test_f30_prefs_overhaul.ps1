# test_f30_prefs_overhaul.ps1
# Tier 1 Feature Tests for Feature 30: Overhauled Settings (PrefsWindow) & Notch Synchronization
# Opaque-box verification against compiled Deskmate.exe with proper resource cleanup.

$commonDir = Join-Path (Split-Path -Parent $PSScriptRoot) "common"
. (Join-Path $commonDir "TestHarness.ps1")
. (Join-Path $commonDir "DeskmateLoader.ps1")

# Settings.Dir is static readonly: keep persisted monitor cases out of user data.
$f30Tmp = Join-Path ([System.IO.Path]::GetTempPath()) ("deskmate-f30-" + [Guid]::NewGuid().ToString("N"))
[void][System.IO.Directory]::CreateDirectory($f30Tmp)
$env:DESKMATE_DIR = $f30Tmp

Describe "Tier 1 Feature Suite: Feature 30 - Overhauled Settings & Notch Synchronization" {

    $flags = [System.Reflection.BindingFlags]'Public,NonPublic,Static,Instance'
    $islandType = Get-DeskmateType "Island"
    $prefsType = $islandType.GetNestedType("PrefsWindow", $flags)
    $sideListType = $islandType.GetNestedType("SideList", $flags)
    $themeCardType = $islandType.GetNestedType("ThemeCardPicker", $flags)
    $kitRowType = Get-DeskmateType "KitRow"
    $kitSwitchType = Get-DeskmateType "KitSwitch"
    $kitPaletteType = Get-DeskmateType "KitPalette"
    $kitPopupType = Get-DeskmateType "KitPopup"
    $kitButtonType = Get-DeskmateType "KitButton"
    $inkType = Get-DeskmateType "Ink"
    $kitInkType = Get-DeskmateType "KitInk"
    $langType = Get-DeskmateType "Lang"
    $settingsType = Get-DeskmateType "Settings"

    function Assert-IsolatedSettings {
        $dir = [string]$settingsType.GetField("Dir", $flags).GetValue($null)
        Assert-True ($dir.StartsWith($f30Tmp, [StringComparison]::OrdinalIgnoreCase)) "Settings must use the test-only directory"
    }

    function New-TestIsland {
        $isle = [Activator]::CreateInstance($islandType, $true)
        [void]$inkType.GetMethod("Init", $flags).Invoke($null, $null)
        return $isle
    }

    function Show-OffscreenPrefs($prefs) {
        $prefs.ShowInTaskbar = $false
        $prefs.Location = New-Object System.Drawing.Point(-32000, -32000)
        $prefs.Show()
    }

    function Collect-Texts($parent, $texts) {
        foreach ($c in $parent.Controls) {
            if ($c -is $kitRowType) {
                if ($c.Title) { [void]$texts.Add($c.Title) }
            }
            elseif ($c -is [System.Windows.Forms.Label]) {
                if ($c.Text) { [void]$texts.Add($c.Text) }
            }
            elseif ($c.Controls.Count -gt 0) {
                Collect-Texts $c $texts
            }
        }
    }

    function Get-ContentTexts($prefs) {
        $content = $prefsType.GetField("content", $flags).GetValue($prefs)
        $texts = New-Object System.Collections.Generic.List[string]
        Collect-Texts $content $texts
        return $texts
    }

    function Collect-Rows($parent, $list) {
        foreach ($c in $parent.Controls) {
            if ($c -is $kitRowType) { [void]$list.Add($c) }
            elseif ($c.Controls.Count -gt 0) { Collect-Rows $c $list }
        }
    }

    function Get-ContentRows($prefs) {
        $content = $prefsType.GetField("content", $flags).GetValue($prefs)
        $list = New-Object System.Collections.Generic.List[object]
        Collect-Rows $content $list
        return $list
    }

    function Count-NonBgPixels([System.Drawing.Bitmap]$bmp, [System.Drawing.Color]$bg) {
        $count = 0
        for ($y = 0; $y -lt $bmp.Height; $y += 2) {
            for ($x = 0; $x -lt $bmp.Width; $x += 2) {
                $px = $bmp.GetPixel($x, $y)
                if ($px.A -gt 10 -and ($px.R -ne $bg.R -or $px.G -ne $bg.G -or $px.B -ne $bg.B)) {
                    $count++
                }
            }
        }
        return $count
    }

    function Count-DiffPixels([System.Drawing.Bitmap]$a, [System.Drawing.Bitmap]$b) {
        $diff = 0
        $w = [Math]::Min($a.Width, $b.Width)
        $h = [Math]::Min($a.Height, $b.Height)
        for ($y = 0; $y -lt $h; $y += 2) {
            for ($x = 0; $x -lt $w; $x += 2) {
                if ($a.GetPixel($x, $y).ToArgb() -ne $b.GetPixel($x, $y).ToArgb()) {
                    $diff++
                }
            }
        }
        return $diff
    }

    BeforeEach {
        Reset-DeskmateTheme -Light $false
        $langType.GetField("English", [System.Reflection.BindingFlags]'Public,Static').SetValue($null, $false)
    }

    It "1. PrefsWindow opens on 'overview', Sections has 19 items across 5 GroupHeaders, and search TextBox is in Controls" {
        $isle = $null; $prefs = $null
        try {
            $isle = New-TestIsland
            $prefs = [Activator]::CreateInstance($prefsType, $flags, $null, @($isle), $null)

            $section = $prefsType.GetField("section", $flags).GetValue($prefs)
            Assert-Equal "overview" $section "Default PrefsWindow section must be 'overview'"

            $sections = $prefsType.GetField("Sections", $flags).GetValue($null)
            Assert-Equal 19 $sections.Length "Sections must contain 19 flat 1-click items"
            Assert-Equal "overview" $sections[0][0] "First section id must be 'overview'"
            Assert-Equal "Beranda" $sections[0][1] "First section title must be 'Beranda'"

            $headers = $prefsType.GetField("GroupHeaders", $flags).GetValue($null)
            Assert-Equal 5 $headers.Length "GroupHeaders must contain 5 group headers"

            $search = $prefsType.GetField("search", $flags).GetValue($prefs)
            Assert-NotNull $search "search TextBox field must exist"
            Assert-True ($prefs.Controls.Contains($search)) "search TextBox must be added to PrefsWindow.Controls"
        }
        finally {
            if ($prefs) { $prefs.Dispose() }
            if ($isle) { $isle.Dispose() }
        }
    }

    It "2. NormalizeSection maps all legacy section IDs and cat:0..5 accurately" {
        $norm = $prefsType.GetMethod("NormalizeSection", $flags)
        Assert-NotNull $norm "NormalizeSection must exist"

        $cases = @{
            ""          = "overview"
            "unknown"   = "overview"
            "kulia"     = "integrasi"
            "packs"     = "look"
            "behave"    = "island"
            "idle"      = "island"
            "home"      = "tabs"
            "music"     = "files"
            "access"    = "keys"
            "cat:0"     = "overview"
            "cat:1"     = "look"
            "cat:2"     = "island"
            "cat:3"     = "dock"
            "cat:4"     = "work"
            "cat:5"     = "about"
            "tabs"      = "tabs"
            "remind"    = "remind"
        }
        foreach ($k in $cases.Keys) {
            $actual = $norm.Invoke($null, @($k))
            Assert-Equal $cases[$k] $actual "NormalizeSection('$k') must return '$($cases[$k])'"
        }
    }

    It "3. 'overview' page uses 'dockbar.on' switch matching settings and contains quick-jump buttons" {
        $isle = $null; $prefs = $null
        try {
            $isle = New-TestIsland
            $settings = $islandType.GetField("settings", $flags).GetValue($isle)
            $expectedDock = $settings.GetBool("dockbar.on", $false)

            $prefs = [Activator]::CreateInstance($prefsType, $flags, $null, @($isle), $null)
            Show-OffscreenPrefs $prefs
            $prefs.Go("overview")

            $rows = Get-ContentRows $prefs
            $dockRow = $rows | Where-Object { $_.Title -eq "Dock" } | Select-Object -First 1
            Assert-NotNull $dockRow "'overview' must contain 'Dock' row"

            $dockSwitch = $null
            foreach ($ctrl in $dockRow.Controls) {
                if ($ctrl -is $kitSwitchType) { $dockSwitch = $ctrl; break }
            }
            Assert-NotNull $dockSwitch "'Dock' row must contain a KitSwitch"
            Assert-Equal $expectedDock $dockSwitch.Checked "'Dock' KitSwitch.Checked must match settings.GetBool('dockbar.on', false)"

            $jumpBtns = 0
            foreach ($r in $rows) {
                if ($r.Title -in @("Atur Notch", "Atur Dock", "Integrasi", "Gamifikasi Ketikan")) {
                    foreach ($ctrl in $r.Controls) {
                        if ($ctrl -is $kitButtonType) { $jumpBtns++ }
                    }
                }
            }
            Assert-Equal 4 $jumpBtns "'overview' must contain 4 quick-jump KitButtons"
        }
        finally {
            if ($prefs) { $prefs.Dispose() }
            if ($isle) { $isle.Dispose() }
        }
    }

    It "4. 'look' page constructs ThemeCardPicker, KitPalette, and Paket tema popup, and paints ThemeCardPicker visibly" {
        $isle = $null; $prefs = $null; $bmp0 = $null; $bmp1 = $null
        try {
            $isle = New-TestIsland
            $prefs = [Activator]::CreateInstance($prefsType, $flags, $null, @($isle), $null)
            Show-OffscreenPrefs $prefs
            $prefs.Go("look")

            $rows = Get-ContentRows $prefs
            $picker = $null; $palette = $null; $packPopup = $null
            foreach ($r in $rows) {
                foreach ($ctrl in $r.Controls) {
                    if ($ctrl -is $themeCardType) { $picker = $ctrl }
                    elseif ($ctrl -is $kitPaletteType) { $palette = $ctrl }
                    elseif ($ctrl -is $kitPopupType -and $r.Title -eq "Paket") { $packPopup = $ctrl }
                }
            }
            Assert-NotNull $picker "'look' page must construct ThemeCardPicker"
            Assert-NotNull $palette "'look' page must construct KitPalette"
            Assert-NotNull $packPopup "'look' page must construct 'Paket' KitPopup"

            $picker.SelectedIndex = 0
            $bmp0 = New-Object System.Drawing.Bitmap($picker.Width, $picker.Height)
            $picker.DrawToBitmap($bmp0, (New-Object System.Drawing.Rectangle(0, 0, $picker.Width, $picker.Height)))
            $bg = $kitInkType.GetProperty("Group", [System.Reflection.BindingFlags]'Public,Static').GetValue($null)
            Assert-True ((Count-NonBgPixels $bmp0 $bg) -gt 50) "ThemeCardPicker must render visible pixels onto Bitmap"

            $picker.SelectedIndex = 1
            $bmp1 = New-Object System.Drawing.Bitmap($picker.Width, $picker.Height)
            $picker.DrawToBitmap($bmp1, (New-Object System.Drawing.Rectangle(0, 0, $picker.Width, $picker.Height)))
            Assert-True ((Count-DiffPixels $bmp0 $bmp1) -gt 20) "ThemeCardPicker SelectedIndex 0 vs 1 must render distinct selection highlights"
        }
        finally {
            if ($bmp1) { $bmp1.Dispose() }
            if ($bmp0) { $bmp0.Dispose() }
            if ($prefs) { $prefs.Dispose() }
            if ($isle) { $isle.Dispose() }
        }
    }

    It "5. 'tabs' includes Tab rows and Kartu Ringkasan, while 'dock' does not duplicate Kartu Ringkasan" {
        $isle = $null; $prefs = $null
        try {
            $isle = New-TestIsland
            $prefs = [Activator]::CreateInstance($prefsType, $flags, $null, @($isle), $null)
            Show-OffscreenPrefs $prefs
            $prefs.Go("tabs")
            $tabTexts = Get-ContentTexts $prefs
            Assert-True ($tabTexts -contains "Tab yang tampil") "'tabs' must include 'Tab yang tampil'"
            Assert-True ($tabTexts -contains "Kartu Ringkasan") "'tabs' must include 'Kartu Ringkasan'"
            Assert-True ($tabTexts -contains "Urutan kartu") "'tabs' must include 'Urutan kartu'"
            Assert-True ($tabTexts -contains "Kartu yang tampil") "'tabs' must include 'Kartu yang tampil'"

            $prefs.Go("dock")
            $dockTexts = Get-ContentTexts $prefs
            Assert-False ($dockTexts -contains "Kartu Ringkasan") "'dock' must NOT duplicate 'Kartu Ringkasan'"
            Assert-False ($dockTexts -contains "Urutan kartu") "'dock' must NOT duplicate 'Urutan kartu'"
        }
        finally {
            if ($prefs) { $prefs.Dispose() }
            if ($isle) { $isle.Dispose() }
        }
    }

    It "6. 'island', 'remind', and 'keys' (and Go('access')) render expected sections without duplication" {
        $isle = $null; $prefs = $null
        try {
            $isle = New-TestIsland
            $prefs = [Activator]::CreateInstance($prefsType, $flags, $null, @($isle), $null)
            Show-OffscreenPrefs $prefs
            $prefs.Go("island")
            $islandTexts = Get-ContentTexts $prefs
            Assert-True ($islandTexts -contains "Letak & Ukuran") "'island' must include 'Letak & Ukuran'"
            Assert-True ($islandTexts -contains "Perilaku & Fokus") "'island' must include 'Perilaku & Fokus'"
            Assert-True ($islandTexts -contains "Saat Diam") "'island' must include 'Saat Diam'"

            $prefs.Go("remind")
            $remindTexts = Get-ContentTexts $prefs
            Assert-True ($remindTexts -contains "Lokasi & Cuaca") "'remind' must include 'Lokasi & Cuaca'"
            Assert-True ($remindTexts -contains "Cuaca dan hujan") "'remind' must include 'Cuaca dan hujan'"
            Assert-False ($remindTexts -contains "Istirahat fokus") "'remind' must NOT duplicate 'Istirahat fokus'"

            $prefs.Go("keys")
            $keysTexts = Get-ContentTexts $prefs
            Assert-True ($keysTexts -contains "Aksesibilitas") "'keys' must include 'Aksesibilitas'"
            Assert-True ($keysTexts -contains "Pintasan keyboard") "'keys' must include 'Pintasan keyboard'"

            $prefs.Go("access")
            Assert-Equal "keys" ($prefsType.GetField("section", $flags).GetValue($prefs)) "Go('access') must normalize to 'keys'"
            $accessTexts = Get-ContentTexts $prefs
            Assert-True ($accessTexts -contains "Aksesibilitas") "Go('access') must render 'Aksesibilitas' cleanly"
        }
        finally {
            if ($prefs) { $prefs.Dispose() }
            if ($isle) { $isle.Dispose() }
        }
    }

    It "7. SideList renders visible accent bar for selected item onto a Bitmap" {
        $side = $null; $bmpA = $null; $bmpB = $null
        try {
            $sections = $prefsType.GetField("Sections", $flags).GetValue($null)
            $headers = $prefsType.GetField("GroupHeaders", $flags).GetValue($null)

            $side = [Activator]::CreateInstance($sideListType, $flags, $null, $null, $null)
            $itemsList = $sideListType.GetField("Items", $flags).GetValue($side)
            foreach ($s in $sections) { [void]$itemsList.Add($s) }
            $sideListType.GetField("Headers", $flags).SetValue($side, $headers)
            $side.Size = New-Object System.Drawing.Size(220, 620)
            $side.CreateControl()

            $sideListType.GetField("Selected", $flags).SetValue($side, "overview")
            $bmpA = New-Object System.Drawing.Bitmap(220, 620)
            $side.DrawToBitmap($bmpA, (New-Object System.Drawing.Rectangle(0, 0, 220, 620)))
            $bg = $kitInkType.GetProperty("Side", [System.Reflection.BindingFlags]'Public,Static').GetValue($null)
            Assert-True ((Count-NonBgPixels $bmpA $bg) -gt 100) "SideList must render visible items and accent bar"

            $sideListType.GetField("Selected", $flags).SetValue($side, "look")
            $bmpB = New-Object System.Drawing.Bitmap(220, 620)
            $side.DrawToBitmap($bmpB, (New-Object System.Drawing.Rectangle(0, 0, 220, 620)))
            Assert-True ((Count-DiffPixels $bmpA $bmpB) -gt 30) "Changing SideList.Selected must move the rendered accent bar"
        }
        finally {
            if ($bmpB) { $bmpB.Dispose() }
            if ($bmpA) { $bmpA.Dispose() }
            if ($side) { $side.Dispose() }
        }
    }

    It "8. Island.OpenPrefsForPage maps Notch pages to Settings sections and Lang.T translates new headers and titles" {
        $isle = $null
        try {
            $isle = New-TestIsland
            $openForPage = $islandType.GetMethod("OpenPrefsForPage", $flags)
            Assert-NotNull $openForPage "Island.OpenPrefsForPage must exist"

            $pageMap = @{
                "PageHome"     = "tabs"
                "PageClass"    = "integrasi"
                "PageRemind"   = "remind"
                "PageAgents"   = "agents"
                "PageMessages" = "messages"
                "PagePhone"    = "phone"
                "PageVoice"    = "dikte"
            }

            foreach ($pgFieldName in $pageMap.Keys) {
                $pgVal = [int]($islandType.GetField($pgFieldName, $flags).GetValue($null))
                $openForPage.Invoke($isle, @($pgVal))
                $prefsInst = $islandType.GetField("prefs", $flags).GetValue($isle)
                Assert-NotNull $prefsInst "OpenPrefsForPage($pgFieldName) must open PrefsWindow"
                $actualSec = $prefsType.GetField("section", $flags).GetValue($prefsInst)
                Assert-Equal $pageMap[$pgFieldName] $actualSec "OpenPrefsForPage($pgFieldName) must navigate to '$($pageMap[$pgFieldName])'"
            }
        }
        finally {
            if ($isle) {
                $pField = $islandType.GetField("prefs", $flags)
                if ($pField) {
                    $pInst = $pField.GetValue($isle)
                    if ($pInst -and -not $pInst.IsDisposed) { $pInst.Close(); $pInst.Dispose() }
                }
                $isle.Dispose()
            }
        }

        $langType.GetField("English", [System.Reflection.BindingFlags]'Public,Static').SetValue($null, $true)
        $tMethod = $langType.GetMethod("T", [System.Reflection.BindingFlags]'Public,Static')
        $expectedLang = @{
            "Tampilan & Ruang Kerja"    = "Appearance & Workspace"
            "Produktivitas"             = "Productivity"
            "Perangkat & Media"         = "Devices & Media"
            "Aplikasi"                  = "Apps"
            "Tema & Gaya"               = "Theme & Style"
            "Notch & Perilaku"          = "Notch & Behavior"
            "Tab & Kartu Notch"         = "Notch Tabs & Cards"
            "Dock & Taskbar"            = "Dock & Taskbar"
            "Integrasi & Kalender"      = "Integrations & Calendar"
            "Tugas & Fokus"             = "Tasks & Focus"
            "Layanan AI"                = "AI Services"
            "Agen Coding"               = "Coding Agents"
            "Dikte & Rapat"             = "Dictation & Meetings"
            "Notifikasi & Pesan"        = "Notifications & Messages"
            "Musik & Berkas"            = "Music & Files"
            "Gamifikasi Ketikan"        = "Typing Gamification"
            "Sistem & Perangkat"        = "System & Devices"
            "Cuaca & Pengingat"         = "Weather & Reminders"
            "Umum & Data"               = "General & Data"
            "Pintasan & Aksesibilitas"  = "Shortcuts & Accessibility"
        }
        foreach ($k in $expectedLang.Keys) {
            $actual = $tMethod.Invoke($null, @($k))
            Assert-Equal $expectedLang[$k] $actual "English translation mismatch for '$k'"
        }
    }

    It "9. Display selection clears malformed values, preserves unavailable displays, and explains a single display" {
        $isle = $null; $prefs = $null
        try {
            $isle = New-TestIsland
            Assert-IsolatedSettings
            $settings = $islandType.GetField("settings", $flags).GetValue($isle)
            $chosenScreen = $islandType.GetMethod("ChosenScreen", $flags)
            Assert-NotNull $chosenScreen "Island must resolve a selected display"

            $settings.Set("monitor", "not-a-display")
            $settings.Save()
            $isle.Dispose()
            $isle = New-TestIsland
            $settings = $islandType.GetField("settings", $flags).GetValue($isle)
            [void]$chosenScreen.Invoke($isle, $null)
            Assert-False ($settings.Has("monitor")) "Malformed persisted display must be cleared"

            $unavailable = "\\.\DISPLAY9999"
            $settings.Set("monitor", $unavailable)
            $settings.Save()
            $isle.Dispose()
            $isle = New-TestIsland
            $settings = $islandType.GetField("settings", $flags).GetValue($isle)
            $resolved = $chosenScreen.Invoke($isle, $null)
            $primary = [System.Windows.Forms.Screen]::PrimaryScreen
            Assert-Equal $resolved.DeviceName $primary.DeviceName "Unavailable display must fall back to primary"
            Assert-Equal $settings.Get("monitor", "") $unavailable "Unavailable display preference must be retained"

            $prefs = [Activator]::CreateInstance($prefsType, $flags, $null, @($isle), $null)
            Show-OffscreenPrefs $prefs
            $prefs.Go("island")
            $displayRow = Get-ContentRows $prefs | Where-Object { $_.Title -eq "Layar" } | Select-Object -First 1
            Assert-NotNull $displayRow "Island settings must include a display selector"
            $displayPicker = $displayRow.Controls | Where-Object { $_ -is $kitPopupType } | Select-Object -First 1
            Assert-NotNull $displayPicker "Display row must contain a popup selector"
            if ([System.Windows.Forms.Screen]::AllScreens.Length -eq 1) {
                Assert-False $displayPicker.Enabled "Single display selector must be disabled"
                Assert-Equal $displayRow.Detail "Sambungkan layar lain untuk memilihnya" "Single display selector must explain how to enable it"
            }
            else {
                Assert-True $displayPicker.Enabled "Multiple displays must enable the selector"
                Assert-Equal $displayRow.Detail "Pilih layar tempat island tampil" "Multiple display selector must explain its target"
            }
        }
        finally {
            if ($prefs) { $prefs.Dispose() }
            if ($isle) { $isle.Dispose() }
        }
    }

    It "10. Display changes refresh only the open Island settings page" {
        $isle = $null; $prefs = $null
        try {
            $isle = New-TestIsland
            $prefs = [Activator]::CreateInstance($prefsType, $flags, $null, @($isle), $null)
            Show-OffscreenPrefs $prefs
            $content = $prefsType.GetField("content", $flags).GetValue($prefs)
            $refresh = $prefsType.GetMethod("RefreshDisplayPicker", $flags)
            Assert-NotNull $refresh "Preferences must expose a display refresh"

            $prefs.Go("tabs")
            $tabsFirst = $content.Controls[0]
            [void]$refresh.Invoke($prefs, $null)
            Assert-True ([object]::ReferenceEquals($tabsFirst, $content.Controls[0])) "Display refresh must leave other settings pages intact"

            $prefs.Go("island")
            $islandFirst = $content.Controls[0]
            [void]$refresh.Invoke($prefs, $null)
            Assert-False ([object]::ReferenceEquals($islandFirst, $content.Controls[0])) "Display refresh must rebuild the open Island page"
            $displayRow = Get-ContentRows $prefs | Where-Object { $_.Title -eq "Layar" } | Select-Object -First 1
            Assert-NotNull $displayRow "Refreshed Island page must retain the display selector"
        }
        finally {
            if ($prefs) { $prefs.Dispose() }
            if ($isle) { $isle.Dispose() }
        }
    }

    It "11. Dock reconciliation plans every detected display with one selected global owner" {
        $plan = $null
        $planMethod = $islandType.GetMethod("PlanDocks", $flags)
        Assert-NotNull $planMethod "Island must plan dock reconciliation"
        $args = New-Object object[] 3
        $args[0] = [string[]]@("\\.\DISPLAY1", "\\.\DISPLAY2", "\\.\DISPLAY1")
        $args[1] = [string[]]@("\\.\DISPLAY1", "\\.\DISPLAY9")
        $args[2] = "\\.\DISPLAY2"
        $plan = $planMethod.Invoke($null, $args)
        $live = $plan.GetType().GetField("Live", $flags).GetValue($plan)
        $close = $plan.GetType().GetField("Close", $flags).GetValue($plan)
        $owner = $plan.GetType().GetField("Owner", $flags).GetValue($plan)
        Assert-Equal 2 $live.Count "Every unique detected display needs a dock"
        Assert-Equal "\\.\DISPLAY1|\\.\DISPLAY2" (($live | ForEach-Object { $_ }) -join "|") "Detected displays must preserve their order"
        Assert-Equal "\\.\DISPLAY9" ($close[0]) "Disconnected dock must be removed"
        Assert-Equal "\\.\DISPLAY2" $owner "Selected island display must be the sole global owner"
    }
}
