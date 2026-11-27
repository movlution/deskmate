# test_s11_notifications.ps1
# Tier 4 scenario S11: long notification copy and CLI dispatch.

$commonDir = Join-Path (Split-Path -Parent $PSScriptRoot) "common"
. (Join-Path $commonDir "TestHarness.ps1")
. (Join-Path $commonDir "DeskmateLoader.ps1")

Describe "Tier 4 Scenario: S11 Notification Pipeline" {
    $flags = [System.Reflection.BindingFlags]'Public,NonPublic,Instance,Static'

    function Get-DeskmateDifferentPixels($a, $b) {
        $count = 0
        for ($y = 0; $y -lt $a.Height; $y++) {
            for ($x = 0; $x -lt $a.Width; $x++) {
                if ($a.GetPixel($x, $y).ToArgb() -ne $b.GetPixel($x, $y).ToArgb()) { $count++ }
            }
        }
        return $count
    }

    function Get-InkCount($bmp, $background, [int]$x0, [int]$x1, [int]$y0, [int]$y1) {
        $count = 0
        for ($y = $y0; $y -le $y1; $y++) {
            for ($x = $x0; $x -le $x1; $x++) {
                $c = $bmp.GetPixel($x, $y)
                if ([Math]::Abs($c.R - $background.R) + [Math]::Abs($c.G - $background.G) + [Math]::Abs($c.B - $background.B) -gt 24) { $count++ }
            }
        }
        return $count
    }

    BeforeEach {
        Reset-DeskmateTheme -Light $false
    }

    It "clips a long title beside a capped app header without sibling collisions" {
        $islandType = Get-DeskmateType "Island"
        $painterType = Get-DeskmateType "Painter"
        $notifType = Get-DeskmateType "NotifItem"
        $toastType = Get-DeskmateType "Toast"
        $settingsType = Get-DeskmateType "Settings"
        $island = [System.Runtime.Serialization.FormatterServices]::GetUninitializedObject($islandType)
        $settings = [Activator]::CreateInstance($settingsType)
        $painter = [Activator]::CreateInstance($painterType, $true)
        $w = 404; $h = 66
        $background = [System.Drawing.Color]::FromArgb(255, 9, 12, 18)
        $bmp = New-Object System.Drawing.Bitmap $w, $h
        $g = [System.Drawing.Graphics]::FromImage($bmp)
        try {
            $g.Clear($background)
            $islandType.GetField("settings", $flags).SetValue($island, $settings)
            $painterType.GetField("G", $flags).SetValue($painter, $g)
            $painterType.GetField("T", $flags).SetValue($painter, (Get-DeskmateStaticField "Ink" "Base"))
            $painterType.GetField("Alpha", $flags).SetValue($painter, [single]1)
            $painterType.GetField("HalfPx", $flags).SetValue($painter, [single]0.5)
            $islandType.GetField("p", $flags).SetValue($island, $painter)
            $islandType.GetField("notifBurst", $flags).SetValue($island, 3)

            $item = [Activator]::CreateInstance($notifType)
            $item.AppId = "scenario.app"
            $item.AppName = ("A Very Long Application Header " * 9).Trim()
            $item.Title = ("Notification title " * 12).Trim()
            $item.Body = ("The body carries detail without clipping into the title. " * 8).Trim()
            Assert-True ($item.Title.Length -gt 100) "The scenario must exercise a title longer than 100 characters"
            $toast = [Activator]::CreateInstance($toastType, $true)
            $toast.Data = $item
            $islandType.GetField("toast", $flags).SetValue($island, $toast)

            $meta = "3 pesan · " + $item.AppName
            $mw = [Math]::Min($w * 0.35, $painter.Measure($meta, (Get-DeskmateStaticProperty "Ink" "Caption")))
            $titleLeft = 66
            $titleWidth = [Math]::Max(40, $w - 66 - 22 - $mw - 12)
            $titleRight = $titleLeft + $titleWidth
            $metaLeft = ($w - 22) - $mw
            Assert-True ($mw -le ($w * 0.35 + 0.5)) "The app header must be capped at 35% of the island width"
            Assert-True ($titleRight -le $metaLeft) "Title and app header boxes must never overlap"

            $draw = $islandType.GetMethod("DrawNotif", $flags)
            $draw.Invoke($island, @([single]$w, [single]$h))

            $titleInk = Get-InkCount $bmp $background 66 ([int][Math]::Floor($titleRight) - 2) 12 36
            $bodyInk = Get-InkCount $bmp $background 66 ($w - 24) 38 62
            $gapInk = Get-InkCount $bmp $background ([int][Math]::Ceiling($titleRight) + 1) ([int][Math]::Floor($metaLeft) - 1) 14 36
            Assert-True ($titleInk -gt 20) "The clipped title must still render visible text (pixels=$titleInk)"
            Assert-True ($bodyInk -gt 20) "The body must render on its own line (pixels=$bodyInk)"
            Assert-True ($gapInk -le 2) "No title or header ink may spill into the 12px sibling gap (pixels=$gapInk)"
        }
        finally {
            $g.Dispose()
            $bmp.Dispose()
            $painter.Dispose()
        }
    }

    It "opens the messages list panel instead of launching the app when clicking a notch notification toast" {
        $islandType = Get-DeskmateType "Island"
        $painterType = Get-DeskmateType "Painter"
        $notifType = Get-DeskmateType "NotifItem"
        $toastType = Get-DeskmateType "Toast"
        $viewType = Get-DeskmateType "View"
        $settingsType = Get-DeskmateType "Settings"
        $springType = Get-DeskmateType "Spring"
        $island = [System.Runtime.Serialization.FormatterServices]::GetUninitializedObject($islandType)
        $settings = [Activator]::CreateInstance($settingsType)
        $painter = [Activator]::CreateInstance($painterType, $true)
        $w = 404; $h = 66
        $bmp = New-Object System.Drawing.Bitmap $w, $h
        $g = [System.Drawing.Graphics]::FromImage($bmp)
        try {
            $islandType.GetField("settings", $flags).SetValue($island, $settings)
            $painterType.GetField("G", $flags).SetValue($painter, $g)
            $painterType.GetField("T", $flags).SetValue($painter, (Get-DeskmateStaticField "Ink" "Base"))
            $painterType.GetField("Alpha", $flags).SetValue($painter, [single]1)
            $painterType.GetField("HalfPx", $flags).SetValue($painter, [single]0.5)
            $islandType.GetField("p", $flags).SetValue($island, $painter)
            $islandType.GetField("hitsOn", $flags).SetValue($island, $true)
            $islandType.GetField("pointerInside", $flags).SetValue($island, $true)
            foreach ($listField in @("hits", "hitPool", "approvals", "notifs")) {
                $f = $islandType.GetField($listField, $flags)
                if ($f -ne $null) { $f.SetValue($island, [Activator]::CreateInstance($f.FieldType)) }
            }
            $islandType.GetField("emblemFiles", $flags).SetValue($island, [string[]]@())
            $islandType.GetField("cornerFiles", $flags).SetValue($island, [string[]]@())
            $islandType.GetField("bubble", $flags).SetValue($island, [Activator]::CreateInstance($springType, @([single]0, [single]300, [single]34, [single]0.01)))
            $islandType.GetField("homeScroll", $flags).SetValue($island, [Activator]::CreateInstance($springType, @([single]0, [single]300, [single]34, [single]0.2)))
            $islandType.GetField("notifScroll", $flags).SetValue($island, [Activator]::CreateInstance($springType, @([single]120, [single]300, [single]34, [single]0.2)))
            $islandType.GetField("toastTimer", $flags).SetValue($island, (New-Object System.Windows.Forms.Timer))

            $item = [Activator]::CreateInstance($notifType)
            $item.Id = [uint32]42
            $item.AppId = "phone:com.whatsapp"
            $item.AppName = "WhatsApp"
            $item.Title = "Budi"
            $item.Body = "Halo, jadi rapat?"
            $item.Held = $true
            $notifsList = $islandType.GetField("notifs", $flags).GetValue($island)
            $notifsList.Add($item) | Out-Null

            $toast = [Activator]::CreateInstance($toastType, $true)
            $toast.View = [Enum]::Parse($viewType, "Notif")
            $toast.Interactive = $true
            $toast.Data = $item
            $islandType.GetField("toast", $flags).SetValue($island, $toast)
            $islandType.GetField("unread", $flags).SetValue($island, 1)

            $islandType.GetMethod("DrawNotif", $flags).Invoke($island, @([single]$w, [single]$h))

            $hits = $islandType.GetField("hits", $flags).GetValue($island)
            $hit = $null
            foreach ($entry in $hits) {
                if ($entry.Id -eq "notifOpen") { $hit = $entry; break }
            }
            Assert-NotNull $hit "DrawNotif must register the notifOpen hit target"

            $hit.Act.Invoke()

            $pageMessages = [int]$islandType.GetField("PageMessages", $flags).GetValue($null)
            Assert-True ([bool]$islandType.GetField("expanded", $flags).GetValue($island)) "Clicking a notch notification toast must expand the panel"
            Assert-Equal $pageMessages ([int]$islandType.GetField("page", $flags).GetValue($island)) "Clicking a notch notification toast must open PageMessages"
            Assert-Null ($islandType.GetField("toast", $flags).GetValue($island)) "Clicking a notch notification toast must dismiss the active toast"
            Assert-Equal 0 ([int]$islandType.GetField("unread", $flags).GetValue($island)) "Opening the messages list must clear the unread counter"
            Assert-Equal 1 $notifsList.Count "Opening the messages list must keep the held notification in the list instead of removing/launching it"
        }
        finally {
            $g.Dispose()
            $bmp.Dispose()
            $painter.Dispose()
        }
    }

    It "returns CLI exit code 0 when a live island mutex accepts the dispatch" {
        $settingsType = Get-DeskmateType "Settings"
        $relayType = Get-DeskmateType "Relay"
        $mutexName = $settingsType.GetProperty("MutexName", $flags).GetValue($null, $null)
        Assert-NotNull $mutexName "Settings.MutexName must resolve for the isolated test profile"
        $mutex = [Activator]::CreateInstance([System.Threading.Mutex], [object[]]@($false, [string]$mutexName))
        try {
            Assert-True ([bool]$relayType.GetMethod("Running", $flags).Invoke($null, @())) "The live-island probe must see the mutex the test just created"

            $title = ("Outside notification title " * 6).Trim()
            $detail = "The application is alive; the notify command must not fail on a long title."
            $psi = New-Object System.Diagnostics.ProcessStartInfo
            $psi.FileName = [string]$global:DeskmateAssemblyPath
            $psi.Arguments = '--notify "' + $title + '" "' + $detail + '"'
            $psi.UseShellExecute = $false
            $psi.CreateNoWindow = $true
            $psi.RedirectStandardOutput = $true
            $psi.RedirectStandardError = $true
            $psi.EnvironmentVariables["DESKMATE_DIR"] = [string]$env:DESKMATE_DIR
            $proc = [System.Diagnostics.Process]::Start($psi)
            try {
                if (-not $proc.WaitForExit(15000)) {
                    try { $proc.Kill() } catch { }
                    throw "CLI --notify did not exit within 15 seconds"
                }
                $stdout = $proc.StandardOutput.ReadToEnd()
                $stderr = $proc.StandardError.ReadToEnd()
                Assert-Equal 0 $proc.ExitCode "CLI --notify must return 0 after successful dispatch; stderr=$stderr"
                Assert-Equal 0 $stderr.Length "CLI --notify must stay quiet on the success path"
            }
            finally { $proc.Dispose() }
        }
        finally { $mutex.Dispose() }
    }
}
