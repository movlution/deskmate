# test_f29_notch_control_scan.ps1
# Tier 1 Feature Tests for Feature 29: Notch Control Center Grid Polish & Inline Wi-Fi / Bluetooth Scan
# Opaque-box verification against compiled Deskmate.exe with proper resource cleanup.

$commonDir = Join-Path (Split-Path -Parent $PSScriptRoot) "common"
. (Join-Path $commonDir "TestHarness.ps1")
. (Join-Path $commonDir "DeskmateLoader.ps1")

Describe "Tier 1 Feature Suite: Feature 29 - Notch Control Center & Inline Wi-Fi/BT Scan" {

    $flags = [System.Reflection.BindingFlags]'Public,NonPublic,Static,Instance'
    $islandType = Get-DeskmateType "Island"
    $wifiType = Get-DeskmateType "Wifi"
    $scanStatusType = $wifiType.GetNestedType("ScanStatus", $flags)
    $netType = $wifiType.GetNestedType("Net", $flags)
    $btDevType = Get-DeskmateType "BtDevice"
    $audioDevType = Get-DeskmateType "AudioDevice"
    $timerKindType = Get-DeskmateType "TimerKind"
    $passBoxType = $islandType.GetNestedType("WifiPassBox", $flags)
    $inkType = Get-DeskmateType "Ink"
    if (-not ("F29PassHelper" -as [type])) {
        Add-Type -TypeDefinition @"
public static class F29PassHelper {
    public static void OnDone<T>(T net, string pass) { }
}
"@ -Language CSharp
    }

    $langType = Get-DeskmateType "Lang"

    function New-TestIsland {
        $isle = [Activator]::CreateInstance($islandType, $true)
        [void]$inkType.GetMethod("Init", $flags).Invoke($null, $null)
        return $isle
    }

    function Setup-Painter($isle, [System.Drawing.Graphics]$g) {
        $p = $islandType.GetField("p", $flags).GetValue($isle)
        $p.G = $g
        $p.T = $inkType.GetField("Base", $flags).GetValue($null)
        $p.HalfPx = 0.5
        $p.Alpha = 1.0
        $islandType.GetField("g", $flags).SetValue($isle, $g)
        $islandType.GetField("hitsOn", $flags).SetValue($isle, $true)
        $islandType.GetField("pointerInside", $flags).SetValue($isle, $true)
    }

    function Clear-Hits($isle) {
        $hitsList = $islandType.GetField("hits", $flags).GetValue($isle)
        $hitsList.Clear()
    }

    function Get-HitIds($isle) {
        $hitsList = $islandType.GetField("hits", $flags).GetValue($isle)
        $ids = @()
        foreach ($h in $hitsList) { $ids += $h.Id }
        return $ids
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

    It "1. Island devicesOpen maps to controlMode == 1 and SwitchControlMode(2/3/0) manages height and scan lifecycle" {
        $isle = $null
        try {
            $isle = New-TestIsland
            $devOpenProp = $islandType.GetProperty("devicesOpen", $flags)
            $ctrlModeField = $islandType.GetField("controlMode", $flags)
            $switchMethod = $islandType.GetMethod("SwitchControlMode", $flags)
            $heightMethod = $islandType.GetMethod("ControlHeight", $flags)
            $wifiScanField = $islandType.GetField("ctlWifiScanning", $flags)
            $btScanField = $islandType.GetField("ctlBtScanning", $flags)

            Assert-Equal 0 ($ctrlModeField.GetValue($isle)) "Initial controlMode must be 0"
            Assert-False ($devOpenProp.GetValue($isle)) "Initial devicesOpen must be false"

            $devOpenProp.SetValue($isle, $true)
            Assert-Equal 1 ($ctrlModeField.GetValue($isle)) "devicesOpen = true must set controlMode to 1"
            Assert-True ($devOpenProp.GetValue($isle)) "devicesOpen getter must return true when controlMode == 1"

            $devOpenProp.SetValue($isle, $false)
            Assert-Equal 0 ($ctrlModeField.GetValue($isle)) "devicesOpen = false must set controlMode to 0"
            Assert-False ($devOpenProp.GetValue($isle)) "devicesOpen getter must return false when controlMode == 0"

            # Switch to Wi-Fi scan mode (2)
            $switchMethod.Invoke($isle, @(2))
            Assert-Equal 2 ($ctrlModeField.GetValue($isle)) "SwitchControlMode(2) must set controlMode to 2"
            $hWifi = [float]$heightMethod.Invoke($isle, $null)
            Assert-True ($hWifi -ge 240 -and $hWifi -le 356) "ControlHeight() in Wi-Fi mode must be in [240, 356], got $hWifi"

            # Switch to Bluetooth scan mode (3)
            $switchMethod.Invoke($isle, @(3))
            Assert-Equal 3 ($ctrlModeField.GetValue($isle)) "SwitchControlMode(3) must set controlMode to 3"
            $hBt = [float]$heightMethod.Invoke($isle, $null)
            Assert-True ($hBt -ge 240 -and $hBt -le 356) "ControlHeight() in Bluetooth mode must be in [240, 356], got $hBt"

            # Switch back to Grid mode (0) stops active scans cleanly
            $switchMethod.Invoke($isle, @(0))
            Assert-Equal 0 ($ctrlModeField.GetValue($isle)) "SwitchControlMode(0) must return controlMode to 0"
            Assert-False ($wifiScanField.GetValue($isle)) "SwitchControlMode(0) must stop ctlWifiScanning"
            Assert-False ($btScanField.GetValue($isle)) "SwitchControlMode(0) must stop ctlBtScanning"
        }
        finally {
            if ($isle) { $isle.Dispose() }
        }
    }

    It "2. DrawControlPage Grid renders visible pixels, CircleToggle expand chips, DeviceChip battery pill, and Timer finish estimate" {
        $isle = $null; $bmp = $null; $g = $null
        $bmpNoBat = $null; $gNoBat = $null; $bmpBat = $null; $gBat = $null
        $bmpTmRun = $null; $gTmRun = $null; $bmpTmPause = $null; $gTmPause = $null
        try {
            $isle = New-TestIsland
            $radios = $islandType.GetField("radios", $flags).GetValue($isle)
            $radios.HasWifi = $true
            $radios.WifiOn = $true
            $radios.HasBluetooth = $true
            $radios.BluetoothOn = $true

            $islandType.GetField("controlMode", $flags).SetValue($isle, 0)
            $islandType.GetField("prevControlMode", $flags).SetValue($isle, 0)
            $islandType.GetField("devicesUtc", $flags).SetValue($isle, [DateTime]::MinValue)

            $bmp = New-Object System.Drawing.Bitmap(660, 360)
            $g = [System.Drawing.Graphics]::FromImage($bmp)
            Setup-Painter $isle $g

            $bg = [System.Drawing.Color]::FromArgb(20, 20, 24)
            $g.Clear($bg)
            Clear-Hits $isle

            $futureNow = [DateTime]::UtcNow.AddSeconds(5)
            $drawPage = $islandType.GetMethod("DrawControlPage", $flags)
            $drawPage.Invoke($isle, @([float]660, [float]360, $futureNow))

            Assert-True ((Count-NonBgPixels $bmp $bg) -gt 400) "DrawControlPage Grid must render visible non-background pixels"

            $hits = Get-HitIds $isle
            foreach ($id in @("tileWifi", "tileBt", "tileAwake", "tileWifiMore", "tileBtMore", "tileAwakeMore")) {
                Assert-True ($hits -contains $id) "DrawControlPage Grid must register hit target '$id'"
            }

            # Verify DeviceChip renders battery pill when btBattery is populated for default audio device
            $devList = $islandType.GetField("audioDevices", $flags).GetValue($isle)
            $dev = [Activator]::CreateInstance($audioDevType, $true)
            $dev.Id = "ep-bt-1"
            $dev.Name = "Headphones (Sony XM5)"
            $dev.Form = 3
            [void]$devList.Add($dev)
            $islandType.GetField("audioDefault", $flags).SetValue($isle, "ep-bt-1")

            $deviceChip = $islandType.GetMethod("DeviceChip", $flags)
            $chipRect = New-Object System.Drawing.RectangleF(0, 0, 300, 56)

            $bmpNoBat = New-Object System.Drawing.Bitmap(300, 56)
            $gNoBat = [System.Drawing.Graphics]::FromImage($bmpNoBat)
            Setup-Painter $isle $gNoBat
            $gNoBat.Clear($bg)
            Clear-Hits $isle
            $deviceChip.Invoke($isle, @($chipRect))
            Assert-True ((Get-HitIds $isle) -contains "ctlDevices") "DeviceChip must register ctlDevices hit target"

            $btBat = $islandType.GetField("btBattery", $flags).GetValue($isle)
            $btBat["Sony XM5"] = 85

            $bmpBat = New-Object System.Drawing.Bitmap(300, 56)
            $gBat = [System.Drawing.Graphics]::FromImage($bmpBat)
            Setup-Painter $isle $gBat
            $gBat.Clear($bg)
            Clear-Hits $isle
            $deviceChip.Invoke($isle, @($chipRect))
            Assert-True ((Count-DiffPixels $bmpNoBat $bmpBat) -gt 15) "DeviceChip must render battery pill pixels when btBattery is populated"

            # Verify DrawTimerModule renders 'Selesai ...' finish estimate when Focus timer is active (vs paused)
            $islandType.GetMethod("ControlActs", $flags).Invoke($isle, $null)
            $islandType.GetField("timerKind", $flags).SetValue($isle, [Enum]::Parse($timerKindType, "Focus"))
            $islandType.GetField("timerStartUtc", $flags).SetValue($isle, [DateTime]::UtcNow.AddMinutes(-5))
            $islandType.GetField("timerEndUtc", $flags).SetValue($isle, [DateTime]::UtcNow.AddMinutes(20))

            $drawTimer = $islandType.GetMethod("DrawTimerModule", $flags)
            $tmRect = New-Object System.Drawing.RectangleF(0, 0, 620, 100)

            $islandType.GetField("timerPaused", $flags).SetValue($isle, $false)
            $bmpTmRun = New-Object System.Drawing.Bitmap(620, 100)
            $gTmRun = [System.Drawing.Graphics]::FromImage($bmpTmRun)
            Setup-Painter $isle $gTmRun
            $gTmRun.Clear($bg)
            Clear-Hits $isle
            $drawTimer.Invoke($isle, @($tmRect, $futureNow, $false))
            Assert-True ((Count-NonBgPixels $bmpTmRun $bg) -gt 200) "DrawTimerModule active Focus timer must render visible pixels"

            $islandType.GetField("timerPaused", $flags).SetValue($isle, $true)
            $bmpTmPause = New-Object System.Drawing.Bitmap(620, 100)
            $gTmPause = [System.Drawing.Graphics]::FromImage($bmpTmPause)
            Setup-Painter $isle $gTmPause
            $gTmPause.Clear($bg)
            Clear-Hits $isle
            $drawTimer.Invoke($isle, @($tmRect, $futureNow, $false))
            Assert-True ((Count-DiffPixels $bmpTmRun $bmpTmPause) -gt 20) "Active Focus timer with 'Selesai ...' estimate must differ visually from paused timer"
        }
        finally {
            if ($gTmPause) { $gTmPause.Dispose() }
            if ($bmpTmPause) { $bmpTmPause.Dispose() }
            if ($gTmRun) { $gTmRun.Dispose() }
            if ($bmpTmRun) { $bmpTmRun.Dispose() }
            if ($gBat) { $gBat.Dispose() }
            if ($bmpBat) { $bmpBat.Dispose() }
            if ($gNoBat) { $gNoBat.Dispose() }
            if ($bmpNoBat) { $bmpNoBat.Dispose() }
            if ($g) { $g.Dispose() }
            if ($bmp) { $bmp.Dispose() }
            if ($isle) { $isle.Dispose() }
        }
    }

    It "3. DrawControlWifi (controlMode == 2) renders radio-off pill and populated Wi-Fi network rows with hit targets" {
        $isle = $null; $bmp = $null; $g = $null
        try {
            $isle = New-TestIsland
            $islandType.GetMethod("ControlActs", $flags).Invoke($isle, $null)
            $radios = $islandType.GetField("radios", $flags).GetValue($isle)
            $radios.HasWifi = $true
            $radios.WifiOn = $false

            $islandType.GetField("controlMode", $flags).SetValue($isle, 2)
            $islandType.GetField("prevControlMode", $flags).SetValue($isle, 2)
            $islandType.GetField("ctlWifiStatus", $flags).SetValue($isle, [Enum]::Parse($scanStatusType, "RadioOff"))

            $bmp = New-Object System.Drawing.Bitmap(660, 320)
            $g = [System.Drawing.Graphics]::FromImage($bmp)
            Setup-Painter $isle $g

            $bg = [System.Drawing.Color]::FromArgb(20, 20, 24)
            $drawWifi = $islandType.GetMethod("DrawControlWifi", $flags)
            $now = [DateTime]::UtcNow

            # A. Radio off
            $g.Clear($bg)
            Clear-Hits $isle
            $drawWifi.Invoke($isle, @([float]16, [float]48, [float]628, [float]320, $now))
            Assert-True ((Count-NonBgPixels $bmp $bg) -gt 150) "DrawControlWifi radio-off view must render visible pixels"
            $offHits = Get-HitIds $isle
            foreach ($id in @("ctlWifiPowerOn", "ctlBack", "ctlWifiRescan", "ctlWifiSettings")) {
                Assert-True ($offHits -contains $id) "DrawControlWifi radio-off view must register '$id'"
            }

            # B. Populated ctlWifiNets
            $radios.WifiOn = $true
            $islandType.GetField("ctlWifiStatus", $flags).SetValue($isle, [Enum]::Parse($scanStatusType, "Ok"))
            $wifiList = $islandType.GetField("ctlWifiNets", $flags).GetValue($isle)
            $wifiList.Clear()
            $n0 = [Activator]::CreateInstance($netType, $true)
            $n0.Ssid = "OfficeWiFi"; $n0.Signal = 88; $n0.Connected = $true; $n0.Known = $true; $n0.Secured = $true
            $n1 = [Activator]::CreateInstance($netType, $true)
            $n1.Ssid = "CafeGuest"; $n1.Signal = 54; $n1.Connected = $false; $n1.Secured = $false
            [void]$wifiList.Add($n0); [void]$wifiList.Add($n1)

            $g.Clear($bg)
            Clear-Hits $isle
            $drawWifi.Invoke($isle, @([float]16, [float]48, [float]628, [float]320, $now))
            Assert-True ((Count-NonBgPixels $bmp $bg) -gt 250) "DrawControlWifi populated list must render visible pixels"
            $netHits = Get-HitIds $isle
            Assert-True ($netHits -contains "ctlW0") "DrawControlWifi populated list must register ctlW0"
            Assert-True ($netHits -contains "ctlWAct0") "DrawControlWifi connected row must register ctlWAct0"
            Assert-True ($netHits -contains "ctlW1") "DrawControlWifi populated list must register ctlW1"
        }
        finally {
            if ($g) { $g.Dispose() }
            if ($bmp) { $bmp.Dispose() }
            if ($isle) { $isle.Dispose() }
        }
    }

    It "4. DrawControlBluetooth (controlMode == 3) renders radio-off pill and populated BT devices with hit targets" {
        $isle = $null; $bmp = $null; $g = $null
        try {
            $isle = New-TestIsland
            $islandType.GetMethod("ControlActs", $flags).Invoke($isle, $null)
            $radios = $islandType.GetField("radios", $flags).GetValue($isle)
            $radios.HasBluetooth = $true
            $radios.BluetoothOn = $false

            $islandType.GetField("controlMode", $flags).SetValue($isle, 3)
            $islandType.GetField("prevControlMode", $flags).SetValue($isle, 3)
            $islandType.GetField("ctlBtStatusCode", $flags).SetValue($isle, "off")

            $bmp = New-Object System.Drawing.Bitmap(660, 320)
            $g = [System.Drawing.Graphics]::FromImage($bmp)
            Setup-Painter $isle $g

            $bg = [System.Drawing.Color]::FromArgb(20, 20, 24)
            $drawBt = $islandType.GetMethod("DrawControlBluetooth", $flags)
            $now = [DateTime]::UtcNow

            # A. Radio off
            $g.Clear($bg)
            Clear-Hits $isle
            $drawBt.Invoke($isle, @([float]16, [float]48, [float]628, [float]320, $now))
            Assert-True ((Count-NonBgPixels $bmp $bg) -gt 150) "DrawControlBluetooth radio-off view must render visible pixels"
            $offHits = Get-HitIds $isle
            foreach ($id in @("ctlBtPowerOn", "ctlBack", "ctlBtRescan", "ctlBtSettings")) {
                Assert-True ($offHits -contains $id) "DrawControlBluetooth radio-off view must register '$id'"
            }

            # B. Populated ctlBtDevices (connected audio device with battery + available device)
            $radios.BluetoothOn = $true
            $islandType.GetField("ctlBtStatusCode", $flags).SetValue($isle, "")
            $btList = $islandType.GetField("ctlBtDevices", $flags).GetValue($isle)
            $btList.Clear()
            $b0 = [Activator]::CreateInstance($btDevType, $true)
            $b0.Id = "bt0"; $b0.Name = "Galaxy Buds2"; $b0.Connected = $true; $b0.Paired = $true; $b0.IsAudio = $true; $b0.Battery = 78
            $b1 = [Activator]::CreateInstance($btDevType, $true)
            $b1.Id = "bt1"; $b1.Name = "MX Master 3S"; $b1.Connected = $false; $b1.Paired = $false; $b1.IsAudio = $false; $b1.Battery = -1
            [void]$btList.Add($b0); [void]$btList.Add($b1)

            $g.Clear($bg)
            Clear-Hits $isle
            $drawBt.Invoke($isle, @([float]16, [float]48, [float]628, [float]320, $now))
            Assert-True ((Count-NonBgPixels $bmp $bg) -gt 250) "DrawControlBluetooth populated list must render visible pixels"
            $btHits = Get-HitIds $isle
            foreach ($id in @("ctlBt0", "ctlBtAct0", "ctlBt1", "ctlBtAct1")) {
                Assert-True ($btHits -contains $id) "DrawControlBluetooth populated list must register '$id'"
            }
        }
        finally {
            if ($g) { $g.Dispose() }
            if ($bmp) { $bmp.Dispose() }
            if ($isle) { $isle.Dispose() }
        }
    }

    It "5. WifiPassBox construction, masked PasswordChar, eye toggle (revealed), Relayout(), and Dispose()" {
        $passBox = $null
        try {
            $n = [Activator]::CreateInstance($netType, $true)
            $n.Ssid = "SecureOffice"
            $n.Secured = $true

            $doneType = [System.Action`2].MakeGenericType(@($netType, [string]))
            $mi = [F29PassHelper].GetMethod("OnDone").MakeGenericMethod(@($netType))
            $doneCb = [Delegate]::CreateDelegate($doneType, $mi)
            $passBox = [Activator]::CreateInstance($passBoxType, $flags, $null, @($n, $doneCb), $null)
            Assert-NotNull $passBox "WifiPassBox must construct cleanly"

            $boxField = $passBoxType.GetField("box", $flags)
            $eyeField = $passBoxType.GetField("eye", $flags)
            $revealedField = $passBoxType.GetField("revealed", $flags)

            $tb = $boxField.GetValue($passBox)
            $eye = $eyeField.GetValue($passBox)

            Assert-True $tb.UseSystemPasswordChar "WifiPassBox text box must mask password initially"
            Assert-False ($revealedField.GetValue($passBox)) "WifiPassBox revealed must start false"

            $passBox.CreateControl()
            $passBox.Relayout()
            Assert-True ($passBox.ClientSize.Width -gt 200 -and $passBox.ClientSize.Height -gt 80) "WifiPassBox Relayout() must size ClientSize"

            # Toggle eye on -> revealed = true, UseSystemPasswordChar = false
            $onClick = $eye.GetType().GetMethod("OnClick", $flags)
            $onClick.Invoke($eye, @([EventArgs]::Empty))
            Assert-True ($revealedField.GetValue($passBox)) "Eye toggle must set revealed = true"
            Assert-False $tb.UseSystemPasswordChar "Eye toggle must unmask UseSystemPasswordChar"

            # Toggle eye off -> revealed = false, UseSystemPasswordChar = true
            $onClick.Invoke($eye, @([EventArgs]::Empty))
            Assert-False ($revealedField.GetValue($passBox)) "Second eye toggle must set revealed = false"
            Assert-True $tb.UseSystemPasswordChar "Second eye toggle must re-mask UseSystemPasswordChar"
        }
        finally {
            if ($passBox) { $passBox.Dispose() }
        }
    }

    It "6. Lang.T returns English translations for Control Center timer and WifiPassBox keys" {
        $langType.GetField("English", [System.Reflection.BindingFlags]'Public,Static').SetValue($null, $true)
        $tMethod = $langType.GetMethod("T", [System.Reflection.BindingFlags]'Public,Static')
        $expected = @{
            "Selesai {0}" = "Ends {0}"
            "Esc batal" = "Esc to cancel"
            "Tampilkan kata sandi" = "Show password"
            "Sembunyikan kata sandi" = "Hide password"
        }
        foreach ($k in $expected.Keys) {
            $actual = $tMethod.Invoke($null, @($k))
            Assert-Equal $expected[$k] $actual "English translation mismatch for '$k'"
        }
    }

    It "7. Interactive toast hover has bounded grace and dismisses on expiry" {
        $isle = $null
        try {
            $isle = New-TestIsland
            $toastType = Get-DeskmateType "Toast"
            $toast = [Activator]::CreateInstance($toastType)
            $toast.Interactive = $true
            $toast.Seconds = 3.0
            $toast.ShownUtc = [DateTime]::UtcNow
            $islandType.GetField("toast", $flags).SetValue($isle, $toast)
            $islandType.GetField("pointerInside", $flags).SetValue($isle, $true)
            $timer = $islandType.GetField("toastTimer", $flags).GetValue($isle)
            $timer.Interval = 50
            $timer.Start()
            $events = $timer.GetType()
            $tickField = $events.GetField("_onTimer", $flags)
            if (-not $tickField) { $tickField = $events.GetField("onTimer", $flags) }
            $tick = $tickField.GetValue($timer)
            Assert-NotNull $tick "The production toast timer Tick delegate must be registered"
            $tick.DynamicInvoke($timer, [EventArgs]::Empty)
            Assert-NotNull ($islandType.GetField("toast", $flags).GetValue($isle)) "An early hover tick must not dismiss the interactive toast"
            Assert-True $timer.Enabled "An early hover must schedule the remaining grace"
            Assert-True ($timer.Interval -le 4500) "The hover timer must be capped by the absolute lifetime"
            Assert-False ($islandType.GetMethod("ToastExpired", $flags).Invoke($isle, $null)) "The original lifetime must remain inside the grace period"
            $toast.ShownUtc = [DateTime]::UtcNow.AddSeconds(-5)
            Assert-True ($islandType.GetMethod("ToastExpired", $flags).Invoke($isle, $null)) "An interactive toast older than its 1.5-second grace must expire"
            $tick.DynamicInvoke($timer, [EventArgs]::Empty)
            Assert-Null ($islandType.GetField("toast", $flags).GetValue($isle)) "The expiry callback must dismiss the toast even while hovered"
            Assert-False $timer.Enabled "The toast timer must stop after expiry"
        }
        finally {
            if ($isle) { $isle.Dispose() }
        }
    }

    It "8. Pointer leave does not extend a toast and expiry is based on arrival time" {
        $isle = $null
        try {
            $isle = New-TestIsland
            $toastType = Get-DeskmateType "Toast"
            $toast = [Activator]::CreateInstance($toastType)
            $toast.Interactive = $true
            $toast.Seconds = 3.0
            $toast.ShownUtc = [DateTime]::UtcNow.AddSeconds(-3.2)
            $islandType.GetField("toast", $flags).SetValue($isle, $toast)
            $islandType.GetField("pointerInside", $flags).SetValue($isle, $false)
            $timer = $islandType.GetField("toastTimer", $flags).GetValue($isle)
            $timer.Interval = 50
            $timer.Start()
            $events = $timer.GetType()
            $tickField = $events.GetField("_onTimer", $flags)
            if (-not $tickField) { $tickField = $events.GetField("onTimer", $flags) }
            $tick = $tickField.GetValue($timer)
            $tick.DynamicInvoke($timer, [EventArgs]::Empty)
            Assert-Null ($islandType.GetField("toast", $flags).GetValue($isle)) "A non-hovered expired toast must be dismissed"
            Assert-False $timer.Enabled "Dismissal must not reschedule the toast timer"
        }
        finally {
            if ($isle) { $isle.Dispose() }
        }
    }

    It "9. Num Lock bubble click survives a redraw that shifts the bubble" {
        $isle = New-TestIsland
        try {
            $viewType = Get-DeskmateType "View"
            $settings = $islandType.GetField("settings", $flags).GetValue($isle)
            $settings.Set("keys.num", "1")
            $islandType.GetField("numOn", $flags).SetValue($isle, $true)
            $islandType.GetField("current", $flags).SetValue($isle, [Enum]::Parse($viewType, "Media"))
            $islandType.GetField("scale", $flags).SetValue($isle, [single]1)
            $islandType.GetField("shell", $flags).SetValue($isle, [Drawing.RectangleF]::new(20, 0, 240, 34))
            $rect = $islandType.GetField("bubbleRect", $flags)
            $rect.SetValue($isle, [Drawing.RectangleF]::new(266, 2, 30, 30))
            $spring = $islandType.GetField("bubble", $flags).GetValue($isle)
            $spring.Snap([single]1)
            $down = $islandType.GetMethod("OnMouseDown", $flags)
            $up = $islandType.GetMethod("OnMouseUp", $flags)
            $click = [Windows.Forms.MouseEventArgs]::new([Windows.Forms.MouseButtons]::Left, 1, 280, 17, 0)

            [void]$down.Invoke($isle, @($click))
            $rect.SetValue($isle, [Drawing.RectangleF]::new(306, 2, 30, 30))
            [void]$up.Invoke($isle, @($click))

            Assert-True ($islandType.GetField("expanded", $flags).GetValue($isle)) "Click must open the panel even when release falls in the new gap"
            Assert-Equal 0 $spring.Value "The outgoing bubble must disappear before the panel expands"
        }
        finally {
            $isle.Dispose()
        }
    }

    It "10. The opening panel lays out tabs at their final width" {
        $isle = New-TestIsland
        $bitmap = [Drawing.Bitmap]::new(600, 300)
        $graphics = [Drawing.Graphics]::FromImage($bitmap)
        $reduced = (Get-DeskmateType "Anim").GetField("Reduced", $flags)
        $previous = $reduced.GetValue($null)
        try {
            Setup-Painter $isle $graphics
            $reduced.SetValue($null, $false)
            $islandType.GetField("current", $flags).SetValue($isle, [Enum]::Parse((Get-DeskmateType "View"), "Panel"))
            $spring = $islandType.GetField("sw", $flags).GetValue($isle)
            $spring.Target = 560
            $shell = $islandType.GetField("shell", $flags)
            $draw = $islandType.GetMethod("DrawContent", $flags)
            $tone = $inkType.GetField("Base", $flags).GetValue($null)
            $width = $islandType.GetField("tabLayoutW", $flags)

            $shell.SetValue($isle, [Drawing.RectangleF]::new(220, 0, 120, 34))
            [void]$draw.Invoke($isle, @([DateTime]::UtcNow, $tone, $false))
            Assert-Equal 0 ($width.GetValue($isle)) "The compact frame must not lay out panel controls"

            $shell.SetValue($isle, [Drawing.RectangleF]::new(55, 0, 450, 185))
            [void]$draw.Invoke($isle, @([DateTime]::UtcNow, $tone, $false))
            Assert-Equal 560 ($width.GetValue($isle)) "Tabs must use the full width from their first visible frame"
        }
        finally {
            $reduced.SetValue($null, $previous)
            $isle.Dispose()
            $graphics.Dispose()
            $bitmap.Dispose()
        }
    }

    It "11. Bare pill clicks use the original press bounds and require a matching press" {
        $isle = New-TestIsland
        try {
            $expanded = $islandType.GetField("expanded", $flags)
            $shell = $islandType.GetField("shell", $flags)
            $shell.SetValue($isle, [Drawing.RectangleF]::new(20, 0, 240, 34))
            $down = $islandType.GetMethod("OnMouseDown", $flags)
            $up = $islandType.GetMethod("OnMouseUp", $flags)
            $click = [Windows.Forms.MouseEventArgs]::new([Windows.Forms.MouseButtons]::Left, 1, 24, 17, 0)

            [void]$up.Invoke($isle, @($click))
            Assert-False ($expanded.GetValue($isle)) "Release alone must not open the panel"

            [void]$down.Invoke($isle, @($click))
            $shell.SetValue($isle, [Drawing.RectangleF]::new(30, 0, 220, 34))
            [void]$up.Invoke($isle, @($click))
            Assert-True ($expanded.GetValue($isle)) "A small resize must keep a valid body click"
        }
        finally { $isle.Dispose() }
    }

    It "12. Tab hit positions are stable when selection changes in both styles" {
        $isle = New-TestIsland
        $bitmap = [Drawing.Bitmap]::new(560, 150)
        $graphics = [Drawing.Graphics]::FromImage($bitmap)
        try {
            Setup-Painter $isle $graphics
            $settings = $islandType.GetField("settings", $flags).GetValue($isle)
            $draw = $islandType.GetMethod("DrawTabs", $flags)
            $pageField = $islandType.GetField("page", $flags)
            $tabs = $islandType.GetField("tabs", $flags).GetValue($isle)
            $hits = $islandType.GetField("hits", $flags).GetValue($isle)
            foreach ($style in @("icons", "dots")) {
                $settings.Set("tabs.style", $style)
                $pageField.SetValue($isle, $tabs[0])
                Clear-Hits $isle
                [void]$draw.Invoke($isle, @([single]440, [DateTime]::UtcNow))
                $before = @($hits | Where-Object { $_.Id -match '^tab\d+$' } | ForEach-Object { $_.Rect.ToString() })
                Assert-Equal $tabs.Count $before.Count "Every tab must still fit at minimum panel width"

                $pageField.SetValue($isle, $tabs[1])
                Clear-Hits $isle
                [void]$draw.Invoke($isle, @([single]440, [DateTime]::UtcNow))
                $after = @($hits | Where-Object { $_.Id -match '^tab\d+$' } | ForEach-Object { $_.Rect.ToString() })
                Assert-Equal ($before -join '|') ($after -join '|') "Changing selection must not move $style tab targets"
            }
        }
        finally {
            $isle.Dispose()
            $graphics.Dispose()
            $bitmap.Dispose()
        }
    }

    It "13. An open Island context menu suspends hover-to-close and restores it on dismissal" {
        $isle = New-TestIsland
        $popup = [Windows.Forms.ContextMenuStrip]::new()
        try {
            [void]$popup.Items.Add("Context action")
            $islandType.GetField("expanded", $flags).SetValue($isle, $true)
            $islandType.GetField("pointerInside", $flags).SetValue($isle, $true)
            $islandType.GetField("frameLeft", $flags).SetValue($isle, -32000)
            $islandType.GetField("frameTop", $flags).SetValue($isle, -32000)
            $timer = $islandType.GetField("leaveTimer", $flags).GetValue($isle)
            $timer.Start()

            $islandType.GetMethod("ShowIslandMenu", $flags).Invoke($isle, @($popup)) | Out-Null
            Assert-True $popup.Visible "The menu must open"
            Assert-False $timer.Enabled "Opening the menu must cancel the pending collapse"
            $islandType.GetMethod("PointerLeave", $flags).Invoke($isle, $null) | Out-Null
            Assert-False $timer.Enabled "Entering the menu must not queue panel collapse"
            Assert-True ($islandType.GetField("expanded", $flags).GetValue($isle)) "The panel must stay open during menu use"

            $guard = (Get-DeskmateType "MenuGuard").GetMethod("Down", $flags)
            $inside = [Drawing.Point]::new($popup.Left + 5, $popup.Top + 5)
            $guard.Invoke($null, @($inside)) | Out-Null
            [Windows.Forms.Application]::DoEvents()
            Assert-True $popup.Visible "The outside-click guard must leave the menu open for a press inside"

            $guard.Invoke($null, @([Drawing.Point]::new(-32000, -32000))) | Out-Null
            [Windows.Forms.Application]::DoEvents()
            Assert-Null ($islandType.GetField("islandMenu", $flags).GetValue($isle)) "Dismissal must release the active menu"
            Assert-True $timer.Enabled "Hover-to-close must resume when the cursor is outside the panel"
        }
        finally {
            $popup.Dispose()
            $isle.Dispose()
        }
    }
}
