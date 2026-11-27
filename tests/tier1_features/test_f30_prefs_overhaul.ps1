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
    $materialType = $islandType.GetNestedType("MaterialPicker", $flags)
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

    It "1. PrefsWindow opens on 'general', Sections has the Deskmate card plus 14 tiled categories in 5 groups, and search TextBox is in Controls" {
        $isle = $null; $prefs = $null
        try {
            $isle = New-TestIsland
            $prefs = [Activator]::CreateInstance($prefsType, $flags, $null, @($isle), $null)

            $section = $prefsType.GetField("section", $flags).GetValue($prefs)
            Assert-Equal "general" $section "Default PrefsWindow section must be 'general'"

            $sections = $prefsType.GetField("Sections", $flags).GetValue($null)
            Assert-Equal 15 $sections.Length "Sections must contain the Deskmate card and 14 categories"
            Assert-Equal "about" $sections[0][0] "First section must be the Deskmate card"
            Assert-Equal ([string][char]1) $sections[0][2] "The card must carry KitRow.MarkGlyph"

            $groups = @($sections | ForEach-Object { $_[3] } | Sort-Object -Unique)
            Assert-Equal 5 $groups.Count "Sidebar must have 5 groups"
            $label = $inkType.GetMethod("Label", [Type[]]@([string], [bool]))
            foreach ($s in ($sections | Select-Object -Skip 1)) {
                $tile = $label.Invoke($null, @($s[4], $true))
                Assert-False $tile.IsEmpty "Section '$($s[0])' must name an Ink.Labels tile color (got '$($s[4])')"
            }
            Assert-Null ($prefsType.GetField("GroupHeaders", $flags)) "Sidebar group headings are gone: groups are separated by space"

            $search = $prefsType.GetField("search", $flags).GetValue($prefs)
            Assert-NotNull $search "search TextBox field must exist"
            Assert-True ($prefs.Controls.Contains($search)) "search TextBox must be added to PrefsWindow.Controls"
        }
        finally {
            if ($prefs) { $prefs.Dispose() }
            if ($isle) { $isle.Dispose() }
        }
    }

    It "2. NormalizeSection maps legacy IDs, merged parts, and cat:0..5 to sidebar pages" {
        $norm = $prefsType.GetMethod("NormalizeSection", $flags)
        Assert-NotNull $norm "NormalizeSection must exist"

        $cases = @{
            ""          = "general"
            "unknown"   = "general"
            "overview"  = "general"
            "kulia"     = "work"
            "integrasi" = "work"
            "packs"     = "look"
            "behave"    = "island"
            "idle"      = "island"
            "home"      = "island"
            "tabs"      = "island"
            "music"     = "files"
            "access"    = "keys"
            "dikte"     = "ai"
            "phone"     = "system"
            "cat:0"     = "general"
            "cat:1"     = "look"
            "cat:2"     = "island"
            "cat:3"     = "dock"
            "cat:4"     = "work"
            "cat:5"     = "about"
            "remind"    = "remind"
        }
        foreach ($k in $cases.Keys) {
            $actual = $norm.Invoke($null, @($k))
            Assert-Equal $cases[$k] $actual "NormalizeSection('$k') must return '$($cases[$k])'"
        }
    }

    It "3. 'dock' page owns the 'dockbar.on' switch, and 'general' carries no Beranda duplicates" {
        $isle = $null; $prefs = $null
        try {
            $isle = New-TestIsland
            $settings = $islandType.GetField("settings", $flags).GetValue($isle)
            $expectedDock = $settings.GetBool("dockbar.on", $false)

            $prefs = [Activator]::CreateInstance($prefsType, $flags, $null, @($isle), $null)
            Show-OffscreenPrefs $prefs
            $prefs.Go("overview")
            Assert-Equal "general" ($prefsType.GetField("section", $flags).GetValue($prefs)) "Go('overview') must land on 'general'"
            $generalTitles = @(Get-ContentRows $prefs | ForEach-Object { $_.Title })
            Assert-False ($generalTitles -contains "Dock") "'general' must not duplicate the Dock switch"
            Assert-False ($generalTitles -contains "Atur Notch") "'general' must not carry Beranda quick jumps"

            $prefs.Go("dock")
            $rows = Get-ContentRows $prefs
            $dockRow = $rows | Where-Object { $_.Title -eq "Dock" } | Select-Object -First 1
            Assert-NotNull $dockRow "'dock' must contain 'Dock' row"

            $dockSwitch = $null
            foreach ($ctrl in $dockRow.Controls) {
                if ($ctrl -is $kitSwitchType) { $dockSwitch = $ctrl; break }
            }
            Assert-NotNull $dockSwitch "'Dock' row must contain a KitSwitch"
            Assert-Equal $expectedDock $dockSwitch.Checked "'Dock' KitSwitch.Checked must match settings.GetBool('dockbar.on', false)"


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

    It "4b. MaterialPicker paints five visibly different material previews and exposes one choice per material" {
        $isle = $null; $prefs = $null; $bmps = @()
        try {
            $isle = New-TestIsland
            $prefs = [Activator]::CreateInstance($prefsType, $flags, $null, @($isle), $null)
            Show-OffscreenPrefs $prefs
            $prefs.Go("look")

            $picker = $null
            foreach ($r in (Get-ContentRows $prefs)) {
                foreach ($ctrl in $r.Controls) {
                    if ($ctrl -is $materialType) { $picker = $ctrl }
                }
            }
            Assert-NotNull $picker "'look' page must construct MaterialPicker"
            Assert-True ($picker.Width -ge 290) "MaterialPicker must leave room for five labelled previews"
            Assert-True ($picker.Height -ge 60) "MaterialPicker must leave room for a preview and a label"
            Assert-Equal 5 $picker.AccessibilityObject.GetChildCount() "MaterialPicker must expose five material choices"

            $bg = $kitInkType.GetProperty("Group", [System.Reflection.BindingFlags]'Public,Static').GetValue($null)
            for ($i = 0; $i -lt 5; $i++) {
                $picker.SelectedIndex = $i
                $bmp = New-Object System.Drawing.Bitmap($picker.Width, $picker.Height)
                $picker.DrawToBitmap($bmp, (New-Object System.Drawing.Rectangle(0, 0, $picker.Width, $picker.Height)))
                $bmps += $bmp
                Assert-True ((Count-NonBgPixels $bmp $bg) -gt 50) "MaterialPicker $i must paint visible pixels"
            }
            for ($i = 1; $i -lt 5; $i++) {
                Assert-True ((Count-DiffPixels $bmps[$i - 1] $bmps[$i]) -gt 25) "Material previews $($i - 1) and $i must look different"
            }
        }
        finally {
            foreach ($bmp in $bmps) { $bmp.Dispose() }
            if ($prefs) { $prefs.Dispose() }
            if ($isle) { $isle.Dispose() }
        }
    }

    It "5. 'tabs' opens the merged Island page on the 'Tab dan kartu' tab, while 'dock' does not duplicate Kartu Ringkasan" {
        $isle = $null; $prefs = $null
        try {
            $isle = New-TestIsland
            $prefs = [Activator]::CreateInstance($prefsType, $flags, $null, @($isle), $null)
            Show-OffscreenPrefs $prefs
            $prefs.Go("tabs")
            Assert-Equal "island" ($prefsType.GetField("section", $flags).GetValue($prefs)) "Go('tabs') must land on the Island page"
            $nav = $prefsType.GetField("partNav", $flags).GetValue($prefs)
            Assert-NotNull $nav "Merged Island page must expose PartTabStrip"
            Assert-Equal 1 $nav.Selected "Go('tabs') must activate the 'Tab dan kartu' tab"
            $tabTexts = Get-ContentTexts $prefs
            Assert-True ($tabTexts -contains "Tab yang tampil") "'tabs' must include 'Tab yang tampil'"
            Assert-True ($tabTexts -contains "Kartu Ringkasan") "'tabs' must include 'Kartu Ringkasan'"
            Assert-True ($tabTexts -contains "Urutan kartu") "'tabs' must include 'Urutan kartu'"
            Assert-True ($tabTexts -contains "Kartu yang tampil") "'tabs' must include 'Kartu yang tampil'"
            Assert-False ($tabTexts -contains "Posisi") "Tabbed 'tabs' view must not mix in 'Posisi'"

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
            Assert-True ($islandTexts -contains "Posisi") "'island' must include 'Posisi'"
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

            $side = [Activator]::CreateInstance($sideListType, $flags, $null, $null, $null)
            $itemsList = $sideListType.GetField("Items", $flags).GetValue($side)
            foreach ($s in $sections) { [void]$itemsList.Add($s) }
            $side.Size = New-Object System.Drawing.Size(220, 620)
            $side.CreateControl()

            $sideListType.GetField("Selected", $flags).SetValue($side, "general")
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
                "PageHome"     = "island"
                "PageClass"    = "work"
                "PageRemind"   = "remind"
                "PageAgents"   = "agents"
                "PageMessages" = "messages"
                "PagePhone"    = "system"
                "PageVoice"    = "ai"
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
            "Umum"                      = "General"
            "Tampilan"                  = "Appearance"
            "Island"                    = "Island"
            "Dock & Taskbar"            = "Dock & Taskbar"
            "Notifikasi"                = "Notifications"
            "Tugas & Kalender"          = "Tasks & Calendar"
            "Cuaca & Pengingat"         = "Weather & Reminders"
            "AI & Dikte"                = "AI & Dictation"
            "Agen Coding"               = "Coding Agents"
            "Musik & Berkas"            = "Music & Files"
            "Perangkat & HP"            = "Devices & Phone"
            "Gamifikasi Ketikan"        = "Typing Gamification"
            "Pintasan & Aksesibilitas"  = "Shortcuts & Accessibility"
            "Tab dan kartu"             = "Tabs and cards"
            "Integrasi dan kalender"    = "Integrations and calendar"
            "Dikte dan rapat"           = "Dictation and calls"
            "HP Android"                = "Android phone"
            "Versi {0}"                 = "Version {0}"
        }
        foreach ($k in $expectedLang.Keys) {
            $actual = $tMethod.Invoke($null, @($k))
            Assert-Equal $expectedLang[$k] $actual "English translation mismatch for '$k'"
        }
    }

    It "9. Display selection preserves monitor fallback and offers all displays" {
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
            $count = [System.Windows.Forms.Screen]::AllScreens.Length
            Assert-Equal ($count + 1) $displayPicker.Items.Length "Selector must list All displays and every detected display"
            Assert-Equal "Semua layar" $displayPicker.Items[0] "All displays must be the first option"
            if ($count -gt 0) { Assert-True $displayPicker.Enabled "All displays can be selected before a second screen is connected" }
            Assert-True ($displayPicker.Selected -gt 0) "Existing profiles must open in single-display mode"

            $settings.Set("monitor.all", $true)
            $prefs.Go("island")
            $displayRow = Get-ContentRows $prefs | Where-Object { $_.Title -eq "Layar" } | Select-Object -First 1
            $displayPicker = $displayRow.Controls | Where-Object { $_ -is $kitPopupType } | Select-Object -First 1
            Assert-Equal 0 $displayPicker.Selected "All displays should be selected when the preference is enabled"
            Assert-Equal $unavailable ($settings.Get("monitor", "")) "All-display mode must preserve the single-display preference"
        }
        finally {
            if ($prefs) { $prefs.Dispose() }
            if ($isle) { $isle.Dispose() }
        }
    }

    It "Island replica bounds follow placement, DPI, and screen edges without spilling onto a neighbor" {
        $bounds = $islandType.GetMethod("IslandReplicaBounds", $flags)
        $left = New-Object System.Drawing.Rectangle(-1920, -200, 1920, 1080)
        $right = New-Object System.Drawing.Rectangle(0, 0, 2560, 1440)
        $centered = $bounds.Invoke($null, @($left, [single]1, 0, $true, [single]16, [single]0.22, 0))
        Assert-Equal -200 $centered.Top "Docked replica must touch its display's top"
        Assert-Equal -960 ($centered.Left + $centered.Width / 2) "Negative-coordinate screens must center correctly"
        $corner = $bounds.Invoke($null, @($right, [single]1.5, 2, $false, [single]16, [single]0.22, 0))
        Assert-Equal 84 $corner.Width "Mini launcher width must scale with DPI"
        Assert-Equal 9 $corner.Height "Mini launcher thickness must scale with DPI"
        Assert-Equal ($right.Right - 24) $corner.Right "Corner inset must scale with DPI"
        $side = $bounds.Invoke($null, @($left, [single]1.5, 3, $true, [single]16, [single]1, 0))
        Assert-Equal 9 $side.Width "Vertical Mini launcher must use the same thickness"
        Assert-Equal 84 $side.Height "Vertical Mini launcher must rotate the horizontal dimensions"
        Assert-Equal $left.Left $side.Left "Side replica must stick to its own edge"
        Assert-True ($left.Contains($side)) "Side replica must stay within its monitor even at 100% vertical offset"
        $portable = $islandType.GetMethod("IsPortable", $flags)
        Assert-True ($portable.Invoke($null, @("monitor.all"))) "Export and import must preserve the display mode"
    }

    It "Island replica can paint a layered surface without showing or activating a window" {
        $isle = $null; $replica = $null
        try {
            $isle = New-TestIsland
            $type = $islandType.GetNestedType("IslandReplica", $flags)
            $replica = [Activator]::CreateInstance($type, $flags, $null, @($isle, 'headless'), $null)
            $type.GetField("DpiScale", $flags).SetValue($replica, [single]1.25)
            $at = New-Object System.Drawing.Rectangle(-32000, -32000, 110, 30)
            $draw = $type.GetMethod("DrawReplica", $flags)
            Assert-True ($draw.Invoke($replica, @($at, [Drawing.Color]::Black, [Drawing.Color]::White, 0, $true))) "UpdateLayeredWindow must accept the replica surface"
            Assert-False $replica.Visible "Preparing a replica surface must not show its window"
        }
        finally {
            if ($replica) { $replica.Dispose() }
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

            $prefs.Go("dock")
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

    It "Pet choices update in place without resetting the Settings scroll position" {
        Assert-IsolatedSettings
        $isle = $null; $prefs = $null; $pet = $null
        try {
            $isle = New-TestIsland
            $cfg = $islandType.GetField("settings", $flags).GetValue($isle)
            $cfg.Set("pet.on", $true)
            $cfg.Set("pet.kind", "cat")
            $custom = Join-Path $f30Tmp "pets\QA Incomplete"
            [void][IO.Directory]::CreateDirectory($custom)
            [IO.File]::WriteAllText((Join-Path $custom "pet.json"), "{}")

            # Reuse a hidden pet so ApplyPet never shows a window on the active desktop.
            $petType = $islandType.GetNestedType("PetWindow", $flags)
            $pet = [Activator]::CreateInstance($petType, $flags, $null,
                @([Drawing.Rectangle]::new(-32000, -32000, 1000, 600), [single]1, [IntPtr]::Zero, $null), $null)
            $petType.GetField("hidden", $flags).SetValue($pet, $true)
            [void]$pet.Handle
            $islandType.GetField("pet", $flags).SetValue($isle, $pet)

            $prefs = [Activator]::CreateInstance($prefsType, $flags, $null, @($isle), $null)
            # Create the hidden controls as in render_prefs.ps1; Handle alone does not mark them Created.
            $create = [Windows.Forms.Control].GetMethod("CreateControl", [Reflection.BindingFlags]'NonPublic,Instance', $null, [Type[]]@([bool]), $null)
            [void]$create.Invoke($prefs, @($true))
            $prefs.Go("pet")
            $content = $prefsType.GetField("content", $flags).GetValue($prefs)
            # Give the hidden panel a known scroll range.
            $content.AutoScrollMinSize = [Drawing.Size]::new(0, 2000)
            $content.AutoScrollPosition = [Drawing.Point]::new(0, 800)
            $baseline = $content.AutoScrollPosition
            Assert-True ($baseline.Y -lt 0) "The pet section must be scrolled into view first"
            $rows = @(Get-ContentRows $prefs)
            $animal = ($rows | Where-Object Title -eq "Hewan").Field
            $fur = ($rows | Where-Object Title -eq "Warna bulu").Field
            $toggle = ($rows | Where-Object Title -eq "Pet di dock").Field

            $animal.Pick(1)
            Assert-Equal "dog" $cfg.Get("pet.kind", "") "Choosing Dog must persist the new value"
            Assert-False $animal.IsDisposed "Changing the animal must not replace the picker"
            Assert-Equal $baseline.Y $content.AutoScrollPosition.Y "Changing Cat to Dog must preserve scroll"

            $animal.Pick([Array]::IndexOf($animal.Items, "QA Incomplete"))
            Assert-False $fur.Enabled "A custom pet must disable the fur picker"
            Assert-True ($animal.Parent.Detail -like "*sheet.png*") "Invalid custom pet must explain the fallback"
            $animal.Pick(0)
            Assert-True $fur.Enabled "The fur picker must be re-enabled for Cat"
            Assert-Equal "" $animal.Parent.Detail "A valid pet must clear the error"
            $toggle.Checked = $false
            Assert-False $animal.Enabled "Turning Pet off must disable the animal picker"
            Assert-False $toggle.IsDisposed "Turning Pet off must not replace the toggle"
            Assert-Equal $baseline.Y $content.AutoScrollPosition.Y "Custom pet and toggle updates must preserve scroll"
        }
        finally {
            if ($prefs) { $prefs.Dispose() }
            if ($pet -and -not $pet.IsDisposed) { $pet.Close(); $pet.Dispose() }
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
