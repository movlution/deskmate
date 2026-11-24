# test_f28_wifi_bluetooth.ps1
# Tier 1 Feature Tests for Feature 28: Wi-Fi & Bluetooth Quick Settings
# Opaque-box verification against compiled Deskmate.exe with proper resource cleanup.

$commonDir = Join-Path (Split-Path -Parent $PSScriptRoot) "common"
. (Join-Path $commonDir "TestHarness.ps1")
. (Join-Path $commonDir "DeskmateLoader.ps1")

Describe "Tier 1 Feature Suite: Feature 28 - Wi-Fi & Bluetooth Quick Settings" {

    $flags = [System.Reflection.BindingFlags]'Public,NonPublic,Static,Instance'
    $wifiType = Get-DeskmateType "Wifi"
    $scanStatusType = $wifiType.GetNestedType("ScanStatus", $flags)
    $netType = $wifiType.GetNestedType("Net", $flags)
    $radioType = Get-DeskmateType "RadioService"
    $btDevType = Get-DeskmateType "BtDevice"
    $btScannerType = Get-DeskmateType "BtScanner"
    $islandType = Get-DeskmateType "Island"
    $dwType = Get-DeskmateType "DockWindow"
    $qsType = $islandType.GetNestedType("QuickSheet", $flags)
    $inkType = Get-DeskmateType "Ink"
    $langType = Get-DeskmateType "Lang"

    if (-not ("F28BtHelper" -as [type])) {
        $cs = @"
using System;
using System.Collections.Generic;
using System.Threading;
public static class F28BtHelper {
    public static readonly ManualResetEvent Done = new ManualResetEvent(false);
    public static string LastCode = "";
    public static int LastCount = -1;
    public static bool LastScanning = false;
    public static void Reset() {
        Done.Reset();
        LastCode = "";
        LastCount = -1;
        LastScanning = false;
    }
    public static void OnUpdated<T>(List<T> list, bool scan, string code) {
        LastCount = list != null ? list.Count : 0;
        LastScanning = scan;
        LastCode = code ?? "";
        if (!scan || LastCode == "off" || LastCode == "no_adapter" || LastCode == "denied") {
            Done.Set();
        }
    }
    public static void OnRadioChanged() { Done.Set(); }
}
"@
        Add-Type -TypeDefinition $cs -Language CSharp
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

    function Get-HitIds($qs) {
        $hitsList = $qsType.GetField("hits", $flags).GetValue($qs)
        $ids = @()
        foreach ($kv in $hitsList) { $ids += $kv.Value }
        return $ids
    }

    BeforeEach {
        Reset-DeskmateTheme -Light $false
        $langType.GetField("English", [System.Reflection.BindingFlags]'Public,Static').SetValue($null, $false)
    }

    It "1. Wifi.ScanStatus enum, ScanAndWait(4000), and List(out status) return Ok with Connected sorted first" {
        $names = [Enum]::GetNames($scanStatusType)
        Assert-True ($names -contains "Ok") "ScanStatus must contain Ok"
        Assert-True ($names -contains "NoAdapter") "ScanStatus must contain NoAdapter"
        Assert-True ($names -contains "RadioOff") "ScanStatus must contain RadioOff"
        Assert-True ($names -contains "AccessDenied") "ScanStatus must contain AccessDenied"
        Assert-True ($names -contains "Failed") "ScanStatus must contain Failed"

        $scanWait = $wifiType.GetMethod("ScanAndWait", [System.Reflection.BindingFlags]'Public,Static')
        $scanSt = $scanWait.Invoke($null, @(4000))
        Assert-True (@("Ok","AccessDenied","NoAdapter","RadioOff") -contains $scanSt.ToString()) "Wifi.ScanAndWait(4000) must return a defined status on this machine (got $scanSt)"

        $listMethod = $wifiType.GetMethods([System.Reflection.BindingFlags]'Public,Static') |
            Where-Object { $_.Name -eq "List" -and $_.GetParameters().Length -eq 1 } |
            Select-Object -First 1
        Assert-NotNull $listMethod "Wifi.List(out ScanStatus) must exist"

        $outArgs = [object[]]@($null)
        $nets = $listMethod.Invoke($null, $outArgs)
        Assert-True (@("Ok","AccessDenied","NoAdapter","RadioOff") -contains $outArgs[0].ToString()) "Wifi.List out status must be a defined status on this machine (got $($outArgs[0]))"
        if ($outArgs[0].ToString() -eq "Ok") { Assert-True ($nets.Count -ge 1) "Wifi.List should return at least 1 nearby network (got $($nets.Count))" }

        $hasConnected = $false
        foreach ($n in $nets) { if ($n.Connected) { $hasConnected = $true; break } }
        if ($hasConnected) {
            Assert-True $nets[0].Connected "Connected network must be sorted first in Wifi.List"
        }
    }

    It "2. RadioService exposes SetPower(bool, bool) and Refresh() and reads live radio capabilities" {
        $setPower = $radioType.GetMethod("SetPower", [System.Reflection.BindingFlags]'Public,Instance')
        $refresh = $radioType.GetMethod("Refresh", [System.Reflection.BindingFlags]'Public,Instance')
        Assert-NotNull $setPower "RadioService.SetPower(bool, bool) must exist"
        Assert-Equal 2 $setPower.GetParameters().Length "SetPower must take 2 parameters"
        Assert-NotNull $refresh "RadioService.Refresh() must exist"

        [F28BtHelper]::Reset()
        $rs = [Activator]::CreateInstance($radioType, $true)
        $changedEv = $radioType.GetEvent("Changed")
        $changedDel = [Delegate]::CreateDelegate([System.Action], [F28BtHelper].GetMethod("OnRadioChanged"))
        $changedEv.AddEventHandler($rs, $changedDel)
        $rs.Start()
        $rs.Refresh()
        [void][F28BtHelper]::Done.WaitOne(3000)
        for ($i = 0; $i -lt 60 -and -not $radioType.GetField("HasWifi", $flags).GetValue($rs); $i++) {
            Start-Sleep -Milliseconds 50
        }
        $hasWifi = $radioType.GetField("HasWifi", $flags).GetValue($rs)
        $hasBt = $radioType.GetField("HasBluetooth", $flags).GetValue($rs)
        Assert-True ($hasWifi -is [bool]) "HasWifi must be boolean"
        Assert-True ($hasBt -is [bool]) "HasBluetooth must be boolean"
        Assert-True $hasWifi "Live machine should report HasWifi = true"
    }

    It "3. BtScanner StartScan/StopScan/Dispose lifecycle reports status cleanly without throwing" {
        [F28BtHelper]::Reset()
        $scanner = $null
        try {
            $scanner = [Activator]::CreateInstance($btScannerType, $true)
            $listType = [System.Collections.Generic.List`1].MakeGenericType(@($btDevType))
            $actionType = [System.Action`3].MakeGenericType(@($listType, [bool], [string]))
            $mi = [F28BtHelper].GetMethod("OnUpdated").MakeGenericMethod(@($btDevType))
            $del = [Delegate]::CreateDelegate($actionType, $mi)
            $ev = $btScannerType.GetEvent("Updated")
            $ev.AddEventHandler($scanner, $del)

            $scanner.StartScan($true)
            $signaled = [F28BtHelper]::Done.WaitOne(5000)
            Assert-True $signaled "BtScanner should report completion or radio-off status within 5s"
            Assert-True (([F28BtHelper]::LastCode -eq "off") -or ([F28BtHelper]::LastCount -ge 0)) "BtScanner must report 'off' or valid device list"

            $scanner.StopScan()
        }
        finally {
            if ($scanner) { $scanner.Dispose() }
        }
    }

    It "4. BtScanner merges addresses via ParseAddress/BuildSnapshotLocked, merges MatchBattery, and SortBt orders Connected->Paired->Available" {
        $scanner = $null
        try {
            $scanner = [Activator]::CreateInstance($btScannerType, $true)
            $parseAddr = $btScannerType.GetMethod("ParseAddress", $flags)
            $a1 = $parseAddr.Invoke($null, @("AA:BB:CC:DD:EE:FF", ""))
            $a2 = $parseAddr.Invoke($null, @($null, "Bluetooth#Bluetooth00:11:22:33:44:55-aa:bb:cc:dd:ee:ff"))
            Assert-True ($a1 -gt 0) "ParseAddress must parse colon-separated MAC"
            Assert-True ($a2 -gt 0) "ParseAddress must fallback-parse MAC from device Id"
            Assert-Equal $a1 $a2 "Both address formats for AA:BB:CC:DD:EE:FF must yield identical ulong"

            $rawById = $btScannerType.GetField("rawById", $flags).GetValue($scanner)

            $d1 = [Activator]::CreateInstance($btDevType, $true)
            $d1.Id = "dev1"; $d1.Name = ""; $d1.Address = $a1; $d1.Paired = $true; $d1.Connected = $false; $d1.IsAudio = $false; $d1.Signal = -65
            $d2 = [Activator]::CreateInstance($btDevType, $true)
            $d2.Id = "BluetoothLE#dev2"; $d2.Name = "Sony WH-1000XM5"; $d2.Address = $a1; $d2.Paired = $false; $d2.Connected = $true; $d2.IsAudio = $true; $d2.Signal = -45
            $d3 = [Activator]::CreateInstance($btDevType, $true)
            $d3.Id = "dev3"; $d3.Name = "Available Speaker"; $d3.Address = [uint64]123456; $d3.Paired = $false; $d3.Connected = $false; $d3.Signal = -30
            $d4 = [Activator]::CreateInstance($btDevType, $true)
            $d4.Id = "dev4"; $d4.Name = "Paired Mouse"; $d4.Address = [uint64]654321; $d4.Paired = $true; $d4.Connected = $false; $d4.Signal = -80

            $rawById["dev1"] = $d1
            $rawById["dev2"] = $d2
            $rawById["dev3"] = $d3
            $rawById["dev4"] = $d4

            $batDict = New-Object 'System.Collections.Generic.Dictionary[string,int]' ([StringComparer]::OrdinalIgnoreCase)
            $batDict["Sony WH-1000XM5"] = 85

            $rawDict = $batDict.PSObject.BaseObject
            $buildSnap = $btScannerType.GetMethod("BuildSnapshotLocked", $flags)
            $snap = $buildSnap.Invoke($scanner, [object[]]@($rawDict))
            Assert-Equal 3 $snap.Count "Duplicate address dev1+dev2 must merge into 1 device (total 3)"

            $merged = $snap | Where-Object { $_.Address -eq $a1 } | Select-Object -First 1
            Assert-NotNull $merged "Merged device must exist"
            Assert-True ($merged.Connected -and $merged.Paired -and $merged.IsAudio) "Merged device must combine Connected, Paired, IsAudio"
            Assert-Equal "Sony WH-1000XM5" $merged.Name "Merged device must preserve non-empty Name"
            Assert-Equal 85 $merged.Battery "MatchBattery must merge 85% battery for Sony WH-1000XM5"

            $matchBat = $btScannerType.GetMethod("MatchBattery", $flags)
            Assert-Equal -1 ($matchBat.Invoke($null, [object[]]@("Unknown Device", $rawDict))) "Unmatched device battery must be -1"

            $sortBt = $qsType.GetMethod("SortBt", $flags)
            $sorted = $sortBt.Invoke($null, (, $snap))
            Assert-Equal "Sony WH-1000XM5" $sorted[0].Name "SortBt[0] must be Connected device"
            Assert-Equal "Paired Mouse" $sorted[1].Name "SortBt[1] must be Paired device"
            Assert-Equal "Available Speaker" $sorted[2].Name "SortBt[2] must be Available device"
        }
        finally {
            if ($scanner) { $scanner.Dispose() }
        }
    }

    It "5. BtScanner Pair/Unpair/Connect/Disconnect guard invalid devices and return non-null error cleanly" {
        $emptyDev = [Activator]::CreateInstance($btDevType, $true)
        foreach ($mName in @("Pair", "Unpair", "Connect", "Disconnect")) {
            $m = $btScannerType.GetMethod($mName, [System.Reflection.BindingFlags]'Public,Static')
            $errNull = $m.Invoke($null, @($null))
            $errEmpty = $m.Invoke($null, @($emptyDev))
            Assert-True (-not [string]::IsNullOrEmpty($errNull)) "BtScanner.$mName(null) must return non-null error"
            Assert-True (-not [string]::IsNullOrEmpty($errEmpty)) "BtScanner.$mName(empty) must return non-null error"
        }
    }

    It "6. QuickSheet OpenWifi/OpenBluetooth Draw() renders radio-off action pills and populated lists with hit targets" {
        $isle = $null; $dw = $null; $qs = $null; $bmp = $null; $g = $null
        try {
            $isle = [System.Runtime.Serialization.FormatterServices]::GetUninitializedObject($islandType)
            $radios = [Activator]::CreateInstance($radioType, $true)
            $radios.HasWifi = $true; $radios.WifiOn = $false
            $radios.HasBluetooth = $true; $radios.BluetoothOn = $false
            $islandType.GetField("radios", $flags).SetValue($isle, $radios)

            $dw = [Activator]::CreateInstance($dwType, $flags, $null, @($isle), $null)
            $qs = [Activator]::CreateInstance($qsType, $flags, $null, @($isle, $dw), $null)
            $qsType.GetField("W", $flags).SetValue($qs, [float]360)
            $qsType.GetField("H", $flags).SetValue($qs, [float]420)
            $netType2 = Get-DeskmateType "NetWatcher"
            $netWatcher = [Activator]::CreateInstance($netType2, $true)
            $islandType.GetField("net", $flags).SetValue($isle, $netWatcher)

            $bmp = New-Object System.Drawing.Bitmap(360, 420)
            $g = [System.Drawing.Graphics]::FromImage($bmp)
            $p = $qsType.GetField("p", $flags).GetValue($qs)
            $p.G = $g
            $p.T = $inkType.GetField("Base", $flags).GetValue($null)
            $p.HalfPx = 0.5
            $p.Alpha = 1.0
            $qsType.GetField("g", $flags).SetValue($qs, $g)

            $bg = [System.Drawing.Color]::FromArgb(24, 24, 28)
            $drawMethod = $qsType.GetMethod("Draw", $flags)
            $hitsField = $qsType.GetField("hits", $flags)

            # A. Radio-off Wi-Fi
            $qsType.GetField("wifiView", $flags).SetValue($qs, $true)
            $qsType.GetField("btView", $flags).SetValue($qs, $false)
            $qsType.GetField("wifiScanStatus", $flags).SetValue($qs, [Enum]::Parse($scanStatusType, "RadioOff"))
            $g.Clear($bg)
            $hitsField.GetValue($qs).Clear()
            $drawMethod.Invoke($qs, $null)
            Assert-True ((Count-NonBgPixels $bmp $bg) -gt 100) "Radio-off Wi-Fi view must render visible pixels"
            Assert-True ((Get-HitIds $qs) -contains "wifiPowerOn") "Radio-off Wi-Fi view must register wifiPowerOn hit target"

            # B. Radio-off Bluetooth
            $qsType.GetField("wifiView", $flags).SetValue($qs, $false)
            $qsType.GetField("btView", $flags).SetValue($qs, $true)
            $qsType.GetField("btStatusCode", $flags).SetValue($qs, "off")
            $g.Clear($bg)
            $hitsField.GetValue($qs).Clear()
            $drawMethod.Invoke($qs, $null)
            Assert-True ((Count-NonBgPixels $bmp $bg) -gt 100) "Radio-off Bluetooth view must render visible pixels"
            Assert-True ((Get-HitIds $qs) -contains "btPowerOn") "Radio-off Bluetooth view must register btPowerOn hit target"

            # C. Populated Wi-Fi list
            $radios.WifiOn = $true
            $qsType.GetField("wifiView", $flags).SetValue($qs, $true)
            $qsType.GetField("btView", $flags).SetValue($qs, $false)
            $qsType.GetField("wifiScanStatus", $flags).SetValue($qs, [Enum]::Parse($scanStatusType, "Ok"))
            $netList = $qsType.GetField("nets", $flags).GetValue($qs)
            $netList.Clear()
            $n0 = [Activator]::CreateInstance($netType, $true)
            $n0.Ssid = "HomeWiFi"; $n0.Signal = 92; $n0.Connected = $true; $n0.Known = $true; $n0.Secured = $true
            $n1 = [Activator]::CreateInstance($netType, $true)
            $n1.Ssid = "GuestNet"; $n1.Signal = 65; $n1.Connected = $false; $n1.Secured = $false
            [void]$netList.Add($n0); [void]$netList.Add($n1)

            $g.Clear($bg)
            $hitsField.GetValue($qs).Clear()
            $drawMethod.Invoke($qs, $null)
            $wifiHits = Get-HitIds $qs
            Assert-True ((Count-NonBgPixels $bmp $bg) -gt 200) "Populated Wi-Fi list must render visible pixels"
            Assert-True ($wifiHits -contains "w0") "Populated Wi-Fi list must register w0 hit target"
            Assert-True ($wifiHits -contains "off0") "Connected Wi-Fi row must register off0 hit target"

            # D. Populated Bluetooth list
            $radios.BluetoothOn = $true
            $qsType.GetField("wifiView", $flags).SetValue($qs, $false)
            $qsType.GetField("btView", $flags).SetValue($qs, $true)
            $qsType.GetField("btStatusCode", $flags).SetValue($qs, "")
            $btList = $qsType.GetField("btDevices", $flags).GetValue($qs)
            $btList.Clear()
            $b0 = [Activator]::CreateInstance($btDevType, $true)
            $b0.Id = "b0"; $b0.Name = "AirPods Pro"; $b0.Connected = $true; $b0.Paired = $true; $b0.IsAudio = $true; $b0.Battery = 90
            $b1 = [Activator]::CreateInstance($btDevType, $true)
            $b1.Id = "b1"; $b1.Name = "Keychron K2"; $b1.Connected = $false; $b1.Paired = $false; $b1.IsAudio = $false
            [void]$btList.Add($b0); [void]$btList.Add($b1)

            $g.Clear($bg)
            $hitsField.GetValue($qs).Clear()
            $drawMethod.Invoke($qs, $null)
            $btHits = Get-HitIds $qs
            Assert-True ((Count-NonBgPixels $bmp $bg) -gt 200) "Populated Bluetooth list must render visible pixels"
            Assert-True ($btHits -contains "bt0") "Populated Bluetooth list must register bt0 hit target"
            Assert-True ($btHits -contains "btOff0") "Connected Bluetooth row must register btOff0 hit target"
            Assert-True ($btHits -contains "btAct1") "Available Bluetooth row must register btAct1 hit target"
        }
        finally {
            if ($g) { $g.Dispose() }
            if ($bmp) { $bmp.Dispose() }
            if ($qs) { $qs.Dispose() }
            if ($dw) { $dw.Dispose() }
        }
    }

    It "7. Lang.T returns English translations for all new Wi-Fi & Bluetooth keys without TypeInitializationException" {
        $langType.GetField("English", [System.Reflection.BindingFlags]'Public,Static').SetValue($null, $true)
        $tMethod = $langType.GetMethod("T", [System.Reflection.BindingFlags]'Public,Static')
        $expected = @{
            "Nyalakan Wi-Fi" = "Turn on Wi-Fi"
            "Nyalakan Bluetooth" = "Turn on Bluetooth"
            "Memindai perangkat…" = "Scanning for devices…"
            "Tersanding" = "Paired"
            "Sandingkan" = "Pair"
            "Perlu izin lokasi Windows" = "Windows location permission needed"
            "Bluetooth mati" = "Bluetooth is off"
            "Tidak ada adaptor Wi-Fi" = "No Wi-Fi adapter"
            "Tidak ada adaptor Bluetooth" = "No Bluetooth adapter"
            "Pengaturan Lokasi" = "Location settings"
            "Tidak ada perangkat ditemukan" = "No devices found"
            "Tersedia" = "Available"
            "Menyandingkan…" = "Pairing…"
            "Lepas sandi" = "Unpair"
            "Menghubungkan…" = "Connecting…"
        }
        foreach ($k in $expected.Keys) {
            $actual = $tMethod.Invoke($null, @($k))
            Assert-Equal $expected[$k] $actual "English translation mismatch for '$k'"
        }
    }
}
