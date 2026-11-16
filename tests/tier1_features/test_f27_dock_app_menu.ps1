# test_f27_dock_app_menu.ps1
# Tier 1 Feature Tests for Feature 27: Dock App Context Menu, ShellApp.EndTask, ResolveFileTarget & MenuLook Rendering
# Opaque-box verification against compiled Deskmate.exe with proper resource cleanup.

$commonDir = Join-Path (Split-Path -Parent $PSScriptRoot) "common"
. (Join-Path $commonDir "TestHarness.ps1")
. (Join-Path $commonDir "DeskmateLoader.ps1")

Describe "Tier 1 Feature Suite: Feature 27 - Dock App Context Menu & ShellApp" {

    $flags = [System.Reflection.BindingFlags]'Public,NonPublic,Static,Instance'
    $dwType = Get-DeskmateType "DockWindow"
    $daType = Get-DeskmateType "DockApp"
    $winType = Get-DeskmateType "DockWin"
    $mgType = Get-DeskmateType "MenuGuard"
    $shellAppType = Get-DeskmateType "ShellApp"
    $menuLookType = Get-DeskmateType "MenuLook"
    $metaType = Get-DeskmateType "MenuItemMeta"
    $langType = Get-DeskmateType "Lang"

    BeforeEach {
        Reset-DeskmateTheme -Light $false
        $langType.GetField("English", [System.Reflection.BindingFlags]'Public,Static').SetValue($null, $false)
    }

    It "1. AppMenu(DockApp) opens CurrentAppSheet for pinned non-running app with expected items and renders visible pixels" {
        $dw = $null
        $sheet = $null
        try {
            $dw = [Activator]::CreateInstance($dwType, $flags, $null, @($null), $null)
            $iconsField = $dwType.GetField("fallbackIcons", $flags)
            $iconsField.SetValue($dw, [Activator]::CreateInstance($iconsField.FieldType, $true))
            $da = [Activator]::CreateInstance($daType, $true)
            $daType.GetField("Key").SetValue($da, "app:notepad")
            $daType.GetField("Name").SetValue($da, "Notepad")
            $daType.GetField("Exe").SetValue($da, "C:\Windows\System32\notepad.exe")
            $daType.GetField("Pinned").SetValue($da, $true)

            $dwType.GetMethod("AppMenu", [System.Reflection.BindingFlags]'NonPublic,Instance').Invoke($dw, @($da))
            $sheet = $dwType.GetProperty("CurrentAppSheet", $flags).GetValue($dw)
            Assert-NotNull $sheet "dw.CurrentAppSheet must hold the opened AppSheet"
            Assert-Equal $sheet.GetType().Name "AppSheet" "CurrentAppSheet must be an AppSheet instance"

            $sheetType = $sheet.GetType()
            $glassType = $sheetType.BaseType
            $items = $sheetType.GetField("Items", $flags).GetValue($sheet)
            $actionIds = $sheetType.GetField("ActionIds", $flags).GetValue($sheet)
            Assert-NotNull $items "AppSheet.Items must not be null"
            Assert-NotNull $actionIds "AppSheet.ActionIds must not be null"

            $byId = @{}
            foreach ($it in $items) { $byId[$it.Id] = $it }

            Assert-True ($actionIds -contains "header") "ActionIds must contain 'header'"
            Assert-Equal $byId["header"].Label "Notepad" "Header label must match app name"
            Assert-True ($actionIds -contains "row:launch") "ActionIds must contain 'row:launch'"
            Assert-Equal $byId["row:launch"].Label "Buka" "Pinned non-running launch label must be 'Buka'"
            Assert-True ($actionIds -contains "row:admin") "ActionIds must contain 'row:admin'"
            Assert-True ($actionIds -contains "row:pin") "ActionIds must contain 'row:pin'"
            Assert-Equal $byId["row:pin"].Label "Lepas dari Dock" "Pinned app pin label must be 'Lepas dari Dock'"
            Assert-True ($actionIds -contains "row:folder") "ActionIds must contain 'row:folder'"
            Assert-Equal $byId["row:folder"].Label "Buka lokasi berkas" "Folder label must be 'Buka lokasi berkas'"
            Assert-True ($actionIds -contains "row:copyPath") "ActionIds must contain 'row:copyPath'"
            Assert-Equal $byId["row:copyPath"].Label "Salin jalur" "Copy path label must be 'Salin jalur'"
            Assert-True ($actionIds -contains "row:props") "ActionIds must contain 'row:props'"
            Assert-Equal $byId["row:props"].Label "Properti" "Props label must be 'Properti'"

            Assert-False ($actionIds -contains "row:close") "Non-running app must NOT contain 'row:close'"
            Assert-False ($actionIds -contains "row:endtask") "Non-running app must NOT contain 'row:endtask'"

            Start-Sleep -Milliseconds 25
            $glassType.GetMethod("Step", $flags).Invoke($sheet, $null)
            $glassType.GetMethod("Render", $flags).Invoke($sheet, $null)

            $canvas = $glassType.GetField("canvas", $flags).GetValue($sheet)
            Assert-NotNull $canvas "GlassSheet.canvas must exist after Render()"
            $nonZeroAlpha = 0
            $stepX = [Math]::Max(1, [int]($canvas.Width / 24))
            $stepY = [Math]::Max(1, [int]($canvas.Height / 24))
            for ($x = 0; $x -lt $canvas.Width; $x += $stepX) {
                for ($y = 0; $y -lt $canvas.Height; $y += $stepY) {
                    if ($canvas.GetPixel($x, $y).A -gt 0) { $nonZeroAlpha++ }
                }
            }
            Assert-True ($nonZeroAlpha -gt 10) "AppSheet.Render() must produce visible non-transparent pixels (got $nonZeroAlpha)"
        }
        finally {
            if ($sheet -and -not $sheet.IsDisposed) { $sheet.Close(); $sheet.Dispose() }
            if ($dw) { $dw.Dispose() }
        }
    }

    It "2. AppMenu(DockApp) for running 1-window app has window pills, Danger row:endtask, hover springs, and Step() open spring" {
        $dw = $null
        $sheet = $null
        $form = $null
        $srcIcon = $null
        try {
            $form = New-Object System.Windows.Forms.Form
            $form.Text = "DeskmateHelperWin1"
            $form.ShowInTaskbar = $false
            $form.FormBorderStyle = [System.Windows.Forms.FormBorderStyle]::None
            $form.StartPosition = [System.Windows.Forms.FormStartPosition]::Manual
            $form.Location = New-Object System.Drawing.Point(-32000, -32000)
            $form.Size = New-Object System.Drawing.Size(120, 80)
            # A hidden owned handle supplies the real window icon without showing UI.
            $form.CreateControl()
            $handle = $form.Handle
            if ($handle -eq [IntPtr]::Zero) { throw "The offscreen helper handle was not created" }

            $dw = [Activator]::CreateInstance($dwType, $flags, $null, @($null), $null)
            $iconsField = $dwType.GetField("fallbackIcons", $flags)
            $iconsField.SetValue($dw, [Activator]::CreateInstance($iconsField.FieldType, $true))
            $iconsDict = $dwType.GetField("fallbackIcons", $flags).GetValue($dw)
            $srcIcon = New-Object System.Drawing.Bitmap(32, 32)
            $iconsDict["app:helper1"] = $srcIcon

            $da = [Activator]::CreateInstance($daType, $true)
            $daType.GetField("Key").SetValue($da, "app:helper1")
            $daType.GetField("Name").SetValue($da, "Helper App")
            $daType.GetField("Exe").SetValue($da, "C:\Windows\System32\notepad.exe")
            $daType.GetField("Pinned").SetValue($da, $false)

            $w = [Activator]::CreateInstance($winType, $true)
            $winType.GetField("H").SetValue($w, $form.Handle)
            $winType.GetField("Pid").SetValue($w, [System.Diagnostics.Process]::GetCurrentProcess().Id)
            $winType.GetField("Title").SetValue($w, "DeskmateHelperWin1")
            $winsList = $daType.GetField("Wins").GetValue($da)
            [void]$winsList.Add($w)

            $dwType.GetMethod("AppMenu", [System.Reflection.BindingFlags]'NonPublic,Instance').Invoke($dw, @($da))
            $sheet = $dwType.GetProperty("CurrentAppSheet", $flags).GetValue($dw)
            Assert-NotNull $sheet "dw.CurrentAppSheet must hold the opened AppSheet"

            $sheetType = $sheet.GetType()
            $glassType = $sheetType.BaseType
            $items = $sheetType.GetField("Items", $flags).GetValue($sheet)
            $actionIds = $sheetType.GetField("ActionIds", $flags).GetValue($sheet)
            $byId = @{}
            foreach ($it in $items) { $byId[$it.Id] = $it }

            Assert-True ($actionIds -contains "header") "Must contain 'header'"
            Assert-Equal $byId["header"].Label "Helper App" "Header must match app name"
            Assert-True ($actionIds -contains "winFocus") "Must contain 'winFocus'"
            Assert-Equal $byId["winFocus"].Label "Fokus" "winFocus label must be 'Fokus'"
            Assert-True ($actionIds -contains "winMin") "Must contain 'winMin'"
            Assert-Equal $byId["winMin"].Label "Minimal" "winMin label must be 'Minimal'"
            Assert-True ($actionIds -contains "winMax") "Must contain 'winMax'"
            Assert-Equal $byId["winMax"].Label "Maksimal" "winMax label must be 'Maksimal'"
            Assert-True ($actionIds -contains "row:launch") "Must contain 'row:launch'"
            Assert-Equal $byId["row:launch"].Label "Jendela baru" "Running launch label must be 'Jendela baru'"
            Assert-True ($actionIds -contains "row:pin") "Must contain 'row:pin'"
            Assert-Equal $byId["row:pin"].Label "Sematkan di Dock" "Unpinned pin label must be 'Sematkan di Dock'"
            Assert-True ($actionIds -contains "row:close") "Must contain 'row:close'"
            Assert-Equal $byId["row:close"].Label "Tutup jendela" "Close label must be 'Tutup jendela'"
            Assert-True ($actionIds -contains "row:endtask") "Must contain 'row:endtask'"
            Assert-Equal $byId["row:endtask"].Label "Akhiri tugas" "End task label must be 'Akhiri tugas'"
            Assert-True $byId["row:endtask"].Danger "row:endtask Danger must be true"

            # HoverMoved accepts an item ID and drives the two production springs.
            $sheetType.GetMethod("HoverMoved", $flags).Invoke($sheet, @("row:endtask"))
            $hoverAlpha = $sheetType.GetField("hoverAlpha", $flags).GetValue($sheet)
            $dangerTint = $sheetType.GetField("dangerTint", $flags).GetValue($sheet)
            Assert-Equal ([double]$hoverAlpha.Target) 1.0 "hoverAlpha.Target must be 1 for the end-task row"
            Assert-Equal ([double]$dangerTint.Target) 1.0 "dangerTint.Target must be 1 for the danger row"
            $sheetType.GetMethod("Animating", $flags).Invoke($sheet, @([single]0.016))
            Assert-True ($hoverAlpha.Value -gt 0) "hoverAlpha.Value must advance"
            Assert-True ($dangerTint.Value -gt 0) "dangerTint.Value must advance"

            # Verify Step() advances open spring without a message loop
            $openSpring = $glassType.GetField("open", $flags).GetValue($sheet)
            $openBefore = [double]$openSpring.Value
            Start-Sleep -Milliseconds 25
            $glassType.GetMethod("Step", $flags).Invoke($sheet, $null)
            $openAfter = [double]$openSpring.Value
            Assert-True ($openAfter -gt $openBefore) "Step() must advance open spring ($openBefore -> $openAfter)"
        }
        finally {
            if ($sheet -and -not $sheet.IsDisposed) { $sheet.Close(); $sheet.Dispose() }
            if ($srcIcon) { $srcIcon.Dispose() }
            if ($dw) { $dw.Dispose() }
            if ($form) { $form.Close(); $form.Dispose() }
        }
    }

    It "3. AppMenu(DockApp) for multi-window app (Wins.Count = 2) shows window rows, bulk pills, and close all" {
        $dw = $null
        $sheet = $null
        $f1 = $null
        $f2 = $null
        try {
            $f1 = New-Object System.Windows.Forms.Form
            $f1.Text = "HelperWinA"
            $f1.ShowInTaskbar = $false
            $f1.FormBorderStyle = [System.Windows.Forms.FormBorderStyle]::None
            $f1.StartPosition = [System.Windows.Forms.FormStartPosition]::Manual
            $f1.Location = New-Object System.Drawing.Point(-32000, -32000)
            $f1.Size = New-Object System.Drawing.Size(100, 80)
            $f1.CreateControl()

            $f2 = New-Object System.Windows.Forms.Form
            $f2.Text = "HelperWinB"
            $f2.ShowInTaskbar = $false
            $f2.FormBorderStyle = [System.Windows.Forms.FormBorderStyle]::None
            $f2.StartPosition = [System.Windows.Forms.FormStartPosition]::Manual
            $f2.Location = New-Object System.Drawing.Point(-32000, -32000)
            $f2.Size = New-Object System.Drawing.Size(100, 80)
            $f2.CreateControl()
            $h1 = $f1.Handle
            $h2 = $f2.Handle
            if ($h1 -eq [IntPtr]::Zero -or $h2 -eq [IntPtr]::Zero) { throw "The offscreen helper handles were not created" }

            $dw = [Activator]::CreateInstance($dwType, $flags, $null, @($null), $null)
            $iconsField = $dwType.GetField("fallbackIcons", $flags)
            $iconsField.SetValue($dw, [Activator]::CreateInstance($iconsField.FieldType, $true))
            $da = [Activator]::CreateInstance($daType, $true)
            $daType.GetField("Key").SetValue($da, "app:multi")
            $daType.GetField("Name").SetValue($da, "MultiApp")
            $daType.GetField("Exe").SetValue($da, "C:\Windows\System32\notepad.exe")

            $winsList = $daType.GetField("Wins").GetValue($da)
            foreach ($frm in @($f1, $f2)) {
                $w = [Activator]::CreateInstance($winType, $true)
                $winType.GetField("H").SetValue($w, $frm.Handle)
                $winType.GetField("Pid").SetValue($w, [System.Diagnostics.Process]::GetCurrentProcess().Id)
                $winType.GetField("Title").SetValue($w, $frm.Text)
                [void]$winsList.Add($w)
            }

            $dwType.GetMethod("AppMenu", [System.Reflection.BindingFlags]'NonPublic,Instance').Invoke($dw, @($da))
            $sheet = $dwType.GetProperty("CurrentAppSheet", $flags).GetValue($dw)
            Assert-NotNull $sheet "dw.CurrentAppSheet must hold the opened AppSheet"

            $sheetType = $sheet.GetType()
            $items = $sheetType.GetField("Items", $flags).GetValue($sheet)
            $actionIds = $sheetType.GetField("ActionIds", $flags).GetValue($sheet)
            $byId = @{}
            foreach ($it in $items) { $byId[$it.Id] = $it }

            Assert-True ($actionIds -contains "win:0") "Must contain 'win:0'"
            Assert-True ($actionIds -contains "win:1") "Must contain 'win:1'"
            Assert-True ($actionIds -contains "winClose0") "Must contain 'winClose0'"
            Assert-True ($actionIds -contains "winClose1") "Must contain 'winClose1'"
            Assert-True ($actionIds -contains "winMinAll") "Must contain 'winMinAll'"
            Assert-Equal $byId["winMinAll"].Label "Minimalkan semua" "winMinAll label must be 'Minimalkan semua'"
            Assert-True ($actionIds -contains "winRestAll") "Must contain 'winRestAll'"
            Assert-Equal $byId["winRestAll"].Label "Pulihkan semua" "winRestAll label must be 'Pulihkan semua'"
            Assert-True ($actionIds -contains "row:close") "Must contain 'row:close'"
            Assert-Equal $byId["row:close"].Label "Tutup semua jendela (2)" "Close all label must be 'Tutup semua jendela (2)'"
            Assert-True ($actionIds -contains "row:endtask") "Must contain 'row:endtask'"
            Assert-Equal $byId["row:endtask"].Label "Akhiri tugas" "End task label must be 'Akhiri tugas'"
        }
        finally {
            if ($sheet -and -not $sheet.IsDisposed) { $sheet.Close(); $sheet.Dispose() }
            if ($dw) { $dw.Dispose() }
            if ($f1) { $f1.Close(); $f1.Dispose() }
            if ($f2) { $f2.Close(); $f2.Dispose() }
        }
    }

    It "4. AppMenu(DockApp) for Explorer shows 'row:restartExplorer' ('Mulai ulang Explorer') instead of 'row:endtask'" {
        $dw = $null
        $sheet = $null
        $form = $null
        try {
            $form = New-Object System.Windows.Forms.Form
            $form.ShowInTaskbar = $false
            $form.FormBorderStyle = [System.Windows.Forms.FormBorderStyle]::None
            $form.StartPosition = [System.Windows.Forms.FormStartPosition]::Manual
            $form.Location = New-Object System.Drawing.Point(-32000, -32000)
            $form.Size = New-Object System.Drawing.Size(80, 80)
            # A hidden owned handle supplies the real window icon without showing UI.
            $form.CreateControl()
            $handle = $form.Handle
            if ($handle -eq [IntPtr]::Zero) { throw "The offscreen helper handle was not created" }

            $dw = [Activator]::CreateInstance($dwType, $flags, $null, @($null), $null)
            $iconsField = $dwType.GetField("fallbackIcons", $flags)
            $iconsField.SetValue($dw, [Activator]::CreateInstance($iconsField.FieldType, $true))
            $da = [Activator]::CreateInstance($daType, $true)
            $daType.GetField("Key").SetValue($da, "app:explorer")
            $daType.GetField("Name").SetValue($da, "File Explorer")
            $daType.GetField("AppId").SetValue($da, "Microsoft.Windows.Explorer")
            $daType.GetField("Exe").SetValue($da, "C:\Windows\explorer.exe")

            $w = [Activator]::CreateInstance($winType, $true)
            $winType.GetField("H").SetValue($w, $form.Handle)
            $winType.GetField("Pid").SetValue($w, [System.Diagnostics.Process]::GetCurrentProcess().Id)
            $winsList = $daType.GetField("Wins").GetValue($da)
            [void]$winsList.Add($w)

            $dwType.GetMethod("AppMenu", [System.Reflection.BindingFlags]'NonPublic,Instance').Invoke($dw, @($da))
            $sheet = $dwType.GetProperty("CurrentAppSheet", $flags).GetValue($dw)
            Assert-NotNull $sheet "dw.CurrentAppSheet must hold the opened AppSheet"

            $sheetType = $sheet.GetType()
            $items = $sheetType.GetField("Items", $flags).GetValue($sheet)
            $actionIds = $sheetType.GetField("ActionIds", $flags).GetValue($sheet)
            $byId = @{}
            foreach ($it in $items) { $byId[$it.Id] = $it }

            Assert-True ($actionIds -contains "row:restartExplorer") "Explorer menu must contain 'row:restartExplorer'"
            Assert-Equal $byId["row:restartExplorer"].Label "Mulai ulang Explorer" "Explorer restart label must be 'Mulai ulang Explorer'"
            Assert-False ($actionIds -contains "row:endtask") "Explorer menu must NOT contain 'row:endtask'"
        }
        finally {
            if ($sheet -and -not $sheet.IsDisposed) { $sheet.Close(); $sheet.Dispose() }
            if ($dw) { $dw.Dispose() }
            if ($form) { $form.Close(); $form.Dispose() }
        }
    }

    It "5. ShellApp.EndTask handles empty list, refuses self PID, and terminates real helper process" {
        $listGeneric = [System.Collections.Generic.List`1].MakeGenericType(@($winType))
        $endTaskMethod = $shellAppType.GetMethod("EndTask", [System.Reflection.BindingFlags]'Public,Static', $null, @([System.Collections.Generic.IList`1].MakeGenericType(@($winType))), $null)
        Assert-NotNull $endTaskMethod "ShellApp.EndTask(IList<DockWin>) must exist"

        # a) Empty list -> NotFound
        $emptyList = [Activator]::CreateInstance($listGeneric)
        $pEmpty = New-Object object[] 1
        $pEmpty[0] = $emptyList
        $resEmpty = $endTaskMethod.Invoke($null, $pEmpty)
        Assert-Equal $resEmpty.ToString() "NotFound" "Empty window list must return EndTaskStatus.NotFound"

        # b) Current process PID -> refused (NotFound, never kills self)
        $selfList = [Activator]::CreateInstance($listGeneric)
        $wSelf = [Activator]::CreateInstance($winType, $true)
        $winType.GetField("Pid").SetValue($wSelf, [System.Diagnostics.Process]::GetCurrentProcess().Id)
        [void]$selfList.Add($wSelf)
        $pSelf = New-Object object[] 1
        $pSelf[0] = $selfList
        $resSelf = $endTaskMethod.Invoke($null, $pSelf)
        Assert-Equal $resSelf.ToString() "NotFound" "Current process PID must be refused with NotFound"

        # c) Real disposable helper powershell.exe child process -> Ok & HasExited == true
        $proc = $null
        try {
            $psi = New-Object System.Diagnostics.ProcessStartInfo
            $psi.FileName = "powershell.exe"
            $psi.Arguments = "-NoProfile -WindowStyle Hidden -Command Start-Sleep -Seconds 30"
            $psi.UseShellExecute = $false
            $psi.CreateNoWindow = $true
            $proc = [System.Diagnostics.Process]::Start($psi)
            Start-Sleep -Milliseconds 150

            $childList = [Activator]::CreateInstance($listGeneric)
            $wChild = [Activator]::CreateInstance($winType, $true)
            $winType.GetField("Pid").SetValue($wChild, $proc.Id)
            [void]$childList.Add($wChild)

            $pChild = New-Object object[] 1
            $pChild[0] = $childList
            $resChild = $endTaskMethod.Invoke($null, $pChild)
            Assert-Equal $resChild.ToString() "Ok" "EndTask on helper process must return EndTaskStatus.Ok"
            $proc.WaitForExit(2000) | Out-Null
            Assert-True $proc.HasExited "Helper process must have exited after ShellApp.EndTask"
        }
        finally {
            if ($proc) {
                if (-not $proc.HasExited) { try { $proc.Kill() } catch {} }
                $proc.Dispose()
            }
        }
    }

    It "6. ShellApp.ResolveFileTarget resolves .exe and .lnk paths cleanly" {
        $resolveMethod = $shellAppType.GetMethod("ResolveFileTarget", [System.Reflection.BindingFlags]'Public,Static')
        Assert-NotNull $resolveMethod "ShellApp.ResolveFileTarget must exist"

        $notepad = "C:\Windows\System32\notepad.exe"
        $resExe = $resolveMethod.Invoke($null, [object[]]@([string]"", [string]$notepad))
        Assert-Equal $resExe $notepad "ResolveFileTarget must return existing .exe path"

        $lnkPath = Join-Path ([System.IO.Path]::GetTempPath()) ("deskmate_test_" + [Guid]::NewGuid().ToString("N") + ".lnk")
        try {
            $wsh = New-Object -ComObject WScript.Shell
            $sc = $wsh.CreateShortcut($lnkPath)
            $sc.TargetPath = $notepad
            $sc.Save()
            [void][System.Runtime.InteropServices.Marshal]::ReleaseComObject($sc)
            [void][System.Runtime.InteropServices.Marshal]::ReleaseComObject($wsh)

            $resLnk = $resolveMethod.Invoke($null, [object[]]@([string]$lnkPath, [string]""))
            Assert-True ([string]::Equals($resLnk, $notepad, [System.StringComparison]::OrdinalIgnoreCase)) "ResolveFileTarget must resolve .lnk target to notepad.exe (got '$resLnk')"
        }
        finally {
            if (Test-Path $lnkPath) { Remove-Item $lnkPath -Force -ErrorAction SilentlyContinue }
        }
    }

    It "7. MenuLook renders AppMenu items with Image and MenuItemMeta { Glyph, Danger, Header } without truncation and Lang.T translates keys" {
        $cms = $null
        $bmp = $null
        $iconBmp = $null
        try {
            $cms = New-Object System.Windows.Forms.ContextMenuStrip
            $iconBmp = New-Object System.Drawing.Bitmap(16, 16)
            for ($ix = 2; $ix -lt 14; $ix++) {
                for ($iy = 2; $iy -lt 14; $iy++) {
                    $iconBmp.SetPixel($ix, $iy, [System.Drawing.Color]::DodgerBlue)
                }
            }

            $hMeta = [Activator]::CreateInstance($metaType, $true)
            $metaType.GetField("Header").SetValue($hMeta, $true)
            $hItem = New-Object System.Windows.Forms.ToolStripMenuItem("Helper App", $iconBmp)
            $hItem.Tag = $hMeta
            [void]$cms.Items.Add($hItem)

            $gMeta = [Activator]::CreateInstance($metaType, $true)
            $metaType.GetField("Glyph").SetValue($gMeta, [char]0xE7EF)
            $gItem = New-Object System.Windows.Forms.ToolStripMenuItem("Jalankan sebagai administrator")
            $gItem.Tag = $gMeta
            [void]$cms.Items.Add($gItem)

            $dMeta = [Activator]::CreateInstance($metaType, $true)
            $metaType.GetField("Glyph").SetValue($dMeta, [char]0xE711)
            $metaType.GetField("Danger").SetValue($dMeta, $true)
            $dItem = New-Object System.Windows.Forms.ToolStripMenuItem("Akhiri tugas")
            $dItem.Tag = $dMeta
            [void]$cms.Items.Add($dItem)

            $applyMethod = $menuLookType.GetMethod("Apply", [System.Reflection.BindingFlags]'Public,Static', $null, @([System.Windows.Forms.ToolStripDropDownMenu]), $null)
            $applyMethod.Invoke($null, @($cms.psobject.BaseObject))

            $cms.CreateControl()
            [System.Windows.Forms.Application]::DoEvents()

            $bmp = New-Object System.Drawing.Bitmap($cms.Width, $cms.Height)
            $cms.DrawToBitmap($bmp, (New-Object System.Drawing.Rectangle(0, 0, $cms.Width, $cms.Height)))

            $bg = $bmp.GetPixel(2, 2)
            $nonBg = 0
            $rightMost = -1
            for ($x = 2; $x -lt ($bmp.Width - 2); $x++) {
                for ($y = 4; $y -lt ($bmp.Height - 4); $y++) {
                    $p = $bmp.GetPixel($x, $y)
                    if ($p.ToArgb() -ne $bg.ToArgb()) {
                        $nonBg++
                        if ($x -gt $rightMost) { $rightMost = $x }
                    }
                }
            }

            Assert-True ($nonBg -gt 100) "Rendered MenuLook bitmap must contain non-background pixels (got $nonBg)"
            Assert-True ($rightMost -lt ($bmp.Width - 4)) "Rendered text must have right margin and not clip at right edge (rightMost=$rightMost, width=$($bmp.Width))"

            # Lang.T English translations
            $enField = $langType.GetField("English", [System.Reflection.BindingFlags]'Public,Static')
            $prevEn = $enField.GetValue($null)
            try {
                $enField.SetValue($null, $true)
                Assert-Equal (Invoke-DeskmateStaticMethod "Lang" "T" @("Fokuskan")) "Focus" "Lang.T('Fokuskan')"
                Assert-Equal (Invoke-DeskmateStaticMethod "Lang" "T" @("Pulihkan")) "Restore" "Lang.T('Pulihkan')"
                Assert-Equal (Invoke-DeskmateStaticMethod "Lang" "T" @("Minimalkan")) "Minimize" "Lang.T('Minimalkan')"
                Assert-Equal (Invoke-DeskmateStaticMethod "Lang" "T" @("Maksimalkan")) "Maximize" "Lang.T('Maksimalkan')"
                Assert-Equal (Invoke-DeskmateStaticMethod "Lang" "T" @("Akhiri tugas")) "End task" "Lang.T('Akhiri tugas')"
                Assert-Equal (Invoke-DeskmateStaticMethod "Lang" "T" @("Mulai ulang Explorer")) "Restart Explorer" "Lang.T('Mulai ulang Explorer')"
                Assert-Equal (Invoke-DeskmateStaticMethod "Lang" "T" @("Berkas & info")) "File & info" "Lang.T('Berkas & info')"
                Assert-Equal (Invoke-DeskmateStaticMethod "Lang" "T" @("Salin jalur")) "Copy path" "Lang.T('Salin jalur')"
                Assert-Equal (Invoke-DeskmateStaticMethod "Lang" "T" @("Properti")) "Properties" "Lang.T('Properti')"
                Assert-Equal (Invoke-DeskmateStaticMethod "Lang" "T" @("Sedang berjalan · {0} jendela")) "Running · {0} windows" "Lang.T('Sedang berjalan · {0} jendela')"
                Assert-Equal (Invoke-DeskmateStaticMethod "Lang" "T" @("Berjalan")) "Running" "Lang.T('Berjalan')"
                Assert-Equal (Invoke-DeskmateStaticMethod "Lang" "T" @("Disematkan")) "Pinned" "Lang.T('Disematkan')"
                Assert-Equal (Invoke-DeskmateStaticMethod "Lang" "T" @("Fokus")) "Focus" "Lang.T('Fokus')"
                Assert-Equal (Invoke-DeskmateStaticMethod "Lang" "T" @("Minimal")) "Minimize" "Lang.T('Minimal')"
                Assert-Equal (Invoke-DeskmateStaticMethod "Lang" "T" @("Maksimal")) "Maximize" "Lang.T('Maksimal')"
            }
            finally {
                $enField.SetValue($null, $prevEn)
            }
        }
        finally {
            if ($bmp) { $bmp.Dispose() }
            if ($iconBmp) { $iconBmp.Dispose() }
            if ($cms) { $cms.Close(); $cms.Dispose() }
        }
    }
}
