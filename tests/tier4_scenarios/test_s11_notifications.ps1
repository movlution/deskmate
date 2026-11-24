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

    It "returns CLI exit code 0 when a live island mutex accepts the dispatch" {
        $settingsType = Get-DeskmateType "Settings"
        $relayType = Get-DeskmateType "Relay"
        $mutexName = $settingsType.GetProperty("MutexName", $flags).GetValue($null, $null)
        Assert-NotNull $mutexName "Settings.MutexName must resolve for the isolated test profile"
        $mutex = [Activator]::CreateInstance([System.Threading.Mutex], [object[]]@($false, [string]$mutexName))
        try {
            Assert-True ([bool]$relayType.GetMethod("Running", $flags).Invoke($null, @())) "The live-island probe must see the mutex the test just created"

            $cli = Find-DeskmateCliBinary
            Assert-NotNull $cli "Deskmate CLI binary must resolve for --notify"

            $title = ("Outside notification title " * 6).Trim()
            $detail = "The application is alive; the notify command must not fail on a long title."
            $psi = New-Object System.Diagnostics.ProcessStartInfo
            $psi.FileName = $cli.FileName
            $prefix = if ($cli.PrefixArgs -and $cli.PrefixArgs.Count -gt 0) {
                (($cli.PrefixArgs | ForEach-Object { '"' + $_ + '"' }) -join " ") + " "
            } else { "" }
            $psi.Arguments = $prefix + '--notify "' + $title + '" "' + $detail + '"'
            $psi.UseShellExecute = $false
            $psi.CreateNoWindow = $true
            $psi.RedirectStandardOutput = $true
            $psi.RedirectStandardError = $true
            $psi.EnvironmentVariables["DESKMATE_DIR"] = [string]$env:DESKMATE_DIR
            if ($env:DESKMATE_HOME) { $psi.EnvironmentVariables["DESKMATE_HOME"] = [string]$env:DESKMATE_HOME }
            $psi.EnvironmentVariables["DESKMATE_DOCK_DRY"] = "1"
            $proc = [System.Diagnostics.Process]::Start($psi)
            try {
                $errTask = $proc.StandardError.ReadToEndAsync()
                $stdout = $proc.StandardOutput.ReadToEnd()
                $stderr = $errTask.GetAwaiter().GetResult()
                if (-not $proc.WaitForExit(15000)) {
                    try { $proc.Kill() } catch { }
                    throw "CLI --notify did not exit within 15 seconds"
                }
                Assert-Equal 0 $proc.ExitCode "CLI --notify must return 0 after successful dispatch; stderr=$stderr"
                Assert-Equal 0 $stderr.Length "CLI --notify must stay quiet on the success path"
            }
            finally { $proc.Dispose() }
        }
        finally { $mutex.Dispose() }
    }
}
