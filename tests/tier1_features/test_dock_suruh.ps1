# test_dock_suruh.ps1
# Dock Ask Deskmate button: dedicated slot, toggle setting, localization, and window deduplication.

$commonDir = Join-Path (Split-Path -Parent $PSScriptRoot) "common"
. (Join-Path $commonDir "TestHarness.ps1")
. (Join-Path $commonDir "DeskmateLoader.ps1")

$suruhTmp = Join-Path ([System.IO.Path]::GetTempPath()) ("deskmate-docksuruh-" + [Guid]::NewGuid().ToString("N"))
[void][System.IO.Directory]::CreateDirectory($suruhTmp)
$env:DESKMATE_DIR = $suruhTmp
$env:DESKMATE_DOCK_DRY = "1"

Describe "Dock Ask Deskmate button" {

    $flags = [System.Reflection.BindingFlags]'Public,NonPublic,Static,Instance'
    $islandType = Get-DeskmateType "Island"
    $dwType = Get-DeskmateType "DockWindow"
    $langType = Get-DeskmateType "Lang"
    $appWatchType = Get-DeskmateType "AppWatch"
    $dockWinType = Get-DeskmateType "DockWin"

    function Get-SlotIds($dw) {
        $ids = @()
        foreach ($s in $dwType.GetField("slots", $flags).GetValue($dw)) { $ids += $s.Id }
        return ,$ids
    }

    function Compose-Dock($dw) {
        [void]$dwType.GetMethod("Compose", $flags).Invoke($dw, $null)
    }

    It "1. Shows a dedicated suruh slot by default and respects dockbar.suruh toggle" {
        $isle = [Activator]::CreateInstance($islandType, $true)
        $dw = [Activator]::CreateInstance($dwType, $flags, $null, @($isle, [System.Windows.Forms.Screen]::PrimaryScreen, $true), $null)
        try {
            $h = $dw.Handle
            Compose-Dock $dw
            $ids = Get-SlotIds $dw
            Assert-True ($ids -contains "suruh") "Dock must include the suruh slot by default"
            Assert-True ($ids -contains "work") "Dock must keep the work slot separate from suruh"

            $settings = $islandType.GetField("settings", $flags).GetValue($isle)
            $settings.Set("dockbar.suruh", $false)
            Compose-Dock $dw
            Assert-False ((Get-SlotIds $dw) -contains "suruh") "Disabling dockbar.suruh must hide the suruh slot"

            $settings.Set("dockbar.suruh", $true)
            Compose-Dock $dw
            Assert-True ((Get-SlotIds $dw) -contains "suruh") "Re-enabling dockbar.suruh must restore the suruh slot"
        }
        finally { $dw.Shutdown() }
    }

    It "2. Localizes the Dock label and registers actions/menu for sys:suruh" {
        $isle = [Activator]::CreateInstance($islandType, $true)
        $dw = [Activator]::CreateInstance($dwType, $flags, $null, @($isle, [System.Windows.Forms.Screen]::PrimaryScreen, $true), $null)
        try {
            $h = $dw.Handle
            [void]$dwType.GetMethod("Render", $flags).Invoke($dw, $null)

            $acts = $dwType.GetField("acts", $flags).GetValue($dw)
            $menus = $dwType.GetField("menus", $flags).GetValue($dw)
            Assert-True ($acts.ContainsKey("sys:suruh")) "sys:suruh must register a left-click action"
            Assert-True ($menus.ContainsKey("sys:suruh")) "sys:suruh must register a right-click context menu"

            $labelMethod = $dwType.GetMethod("LabelFor", $flags)
            $prevEn = $langType.GetField("English", $flags).GetValue($null)
            try {
                $langType.GetField("English", $flags).SetValue($null, $false)
                $argsId = @("sys:suruh", [System.Drawing.RectangleF]::Empty)
                $lblId = $labelMethod.Invoke($dw, $argsId)
                Assert-Equal $lblId "Suruh Deskmate" "Indonesian dock label must be Suruh Deskmate"

                $langType.GetField("English", $flags).SetValue($null, $true)
                $argsEn = @("sys:suruh", [System.Drawing.RectangleF]::Empty)
                $lblEn = $labelMethod.Invoke($dw, $argsEn)
                Assert-Equal $lblEn "Ask Deskmate" "English dock label must be Ask Deskmate"
                Assert-Equal ($langType.GetMethod("T", $flags).Invoke($null, @("Tombol Suruh Deskmate"))) "Ask Deskmate button" "Settings label must translate to English"
            }
            finally { $langType.GetField("English", $flags).SetValue($null, $prevEn) }
        }
        finally { $dw.Shutdown() }
    }

    It "3. Keeps dockbar.suruh portable and deduplicates both work and suruh windows in AppWatch.Arrange" {
        $portable = $islandType.GetMethod("IsPortable", $flags).Invoke($null, @("dockbar.suruh"))
        Assert-True $portable "dockbar.suruh must be exported/imported as a portable setting"

        $watch = [Activator]::CreateInstance($appWatchType, $true)
        $winsField = $appWatchType.GetField("wins", $flags).GetValue($watch)

        $hWork = [IntPtr]101
        $hSuruh = [IntPtr]202
        $hOther = [IntPtr]303

        $w1 = [Activator]::CreateInstance($dockWinType, $true); $w1.H = $hWork; $w1.Exe = "C:\Deskmate.exe"; $w1.AppId = "Deskmate.Work"; $w1.Title = "Tugas"; $w1.Seen = 1
        $w2 = [Activator]::CreateInstance($dockWinType, $true); $w2.H = $hSuruh; $w2.Exe = "C:\Deskmate.exe"; $w2.AppId = "Deskmate.Suruh"; $w2.Title = "Suruh Deskmate"; $w2.Seen = 2
        $w3 = [Activator]::CreateInstance($dockWinType, $true); $w3.H = $hOther; $w3.Exe = "C:\notepad.exe"; $w3.Title = "Notepad"; $w3.Seen = 3
        $winsField.Add($hWork, $w1)
        $winsField.Add($hSuruh, $w2)
        $winsField.Add($hOther, $w3)

        $arrange = $appWatchType.GetMethod("Arrange", $flags)
        $pins = [string[]]@()
        $list = $arrange.Invoke($watch, [object[]]@($pins, $null, $hWork, $hSuruh))
        Assert-Equal $list.Count 1 "Both work and suruh handles must be skipped from the running apps list"
        Assert-Equal $list[0].Wins[0].H $hOther "Unrelated running windows must remain in the Dock"
    }

    It "4. Opens ChatWindow via sys:suruh action, exposes preview/dot state, and clears state on close" {
        $isle = [Activator]::CreateInstance($islandType, $true)
        $hIsle = $isle.Handle
        $dw = [Activator]::CreateInstance($dwType, $flags, $null, @($isle, [System.Windows.Forms.Screen]::PrimaryScreen, $true), $null)
        try {
            $hDock = $dw.Handle
            $dockWins = $islandType.GetField("dockWins", $flags).GetValue($isle)
            $dockWins[$dwType.GetField("deviceName", $flags).GetValue($dw)] = $dw
            [void]$dwType.GetMethod("Render", $flags).Invoke($dw, $null)

            Assert-False ([bool]$islandType.GetProperty("SuruhOpen", $flags).GetValue($isle)) "SuruhOpen must be false before opening"
            Assert-Equal ([single]$dwType.GetMethod("SuruhDotTarget", $flags).Invoke($dw, $null)) ([single]0) "SuruhDotTarget must be 0 while closed"
            Assert-Null ($dwType.GetMethod("PreviewAppById", $flags).Invoke($dw, @("sys:suruh"))) "PreviewAppById must be null while closed"

            # Trigger left-click action on sys:suruh
            $acts = $dwType.GetField("acts", $flags).GetValue($dw)
            $acts["sys:suruh"].Invoke()
            [System.Windows.Forms.Application]::DoEvents()

            $openNow = [bool]$islandType.GetProperty("SuruhOpen", $flags).GetValue($isle)
            $hwndNow = [IntPtr]$islandType.GetProperty("SuruhHwnd", $flags).GetValue($isle)
            Assert-True $openNow "Clicking sys:suruh must open ChatWindow"
            Assert-True ($hwndNow -ne [IntPtr]::Zero) "ChatWindow must have a valid HWND"

            $dotNow = [single]$dwType.GetMethod("SuruhDotTarget", $flags).Invoke($dw, $null)
            Assert-True ($dotNow -ge 1) "SuruhDotTarget must be >= 1 while ChatWindow is open (got $dotNow)"

            $preview = $dwType.GetMethod("PreviewAppById", $flags).Invoke($dw, @("sys:suruh"))
            Assert-NotNull $preview "PreviewAppById must return suruhSlotApp while open"
            Assert-Equal $preview.Key "sys:suruh" "Preview app key must be sys:suruh"
            Assert-Equal $preview.Wins.Count 1 "Preview app must contain the open ChatWindow"
            Assert-Equal $preview.Wins[0].H $hwndNow "Preview app window handle must match SuruhHwnd"

            $iconRect = $dwType.GetMethod("IconScreenRect", $flags).Invoke($dw, @($preview))
            Assert-False $iconRect.IsEmpty "IconScreenRect must resolve the suruh slot rectangle for preview/motion"

            # Verify keyboard navigation includes sys:suruh
            $navIds = $dwType.GetMethod("NavIds", $flags).Invoke($dw, $null)
            Assert-True ($navIds -contains "sys:suruh") "Keyboard navigation (Win+T) must include sys:suruh"

            # Close ChatWindow and verify cleanup
            $chatWin = $islandType.GetField("chatWin", $flags).GetValue($isle)
            Assert-NotNull $chatWin "chatWin field must reference the open ChatWindow"
            $chatWin.Close()
            [System.Windows.Forms.Application]::DoEvents()

            Assert-False ([bool]$islandType.GetProperty("SuruhOpen", $flags).GetValue($isle)) "SuruhOpen must return false after closing"
            Assert-Equal ([single]$dwType.GetMethod("SuruhDotTarget", $flags).Invoke($dw, $null)) ([single]0) "SuruhDotTarget must return to 0 after closing"
            Assert-Null ($dwType.GetMethod("PreviewAppById", $flags).Invoke($dw, @("sys:suruh"))) "PreviewAppById must return null after closing"
        }
        finally {
            $cw = $islandType.GetField("chatWin", $flags).GetValue($isle)
            if ($cw -ne $null -and -not $cw.IsDisposed) { $cw.Close(); $cw.Dispose() }
            $dw.Shutdown()
            if (-not $isle.IsDisposed) { $isle.Dispose() }
        }
    }
}

try { Remove-Item -Recurse -Force $suruhTmp } catch { }
