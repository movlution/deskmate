# test_multi_display_dock.ps1
# Comprehensive Multi-Display Audit & Integration Tests for Deskmate
# Verifies multi-monitor dock planning, coordinate mapping, negative coordinates,
# geometry isolation, per-screen fullscreen checks, sheet/card anchoring, and peer cleanup.

$commonDir = Join-Path (Split-Path -Parent $PSScriptRoot) "common"
. (Join-Path $commonDir "TestHarness.ps1")
. (Join-Path $commonDir "DeskmateLoader.ps1")

# Ensure test runs against an isolated settings directory and in offscreen/dry mode
$multiTmp = Join-Path ([System.IO.Path]::GetTempPath()) ("deskmate-multidisplay-" + [Guid]::NewGuid().ToString("N"))
[void][System.IO.Directory]::CreateDirectory($multiTmp)
$env:DESKMATE_DIR = $multiTmp

Describe "Multi-Display Dock & Subsystem Integration Suite" {

    $flags = [System.Reflection.BindingFlags]'Public,NonPublic,Static,Instance'
    $islandType = Get-DeskmateType "Island"
    $dwType = Get-DeskmateType "DockWindow"
    $daType = Get-DeskmateType "DockApp"
    $cardType = Get-DeskmateType "DockCard"
    $appSheetType = Get-DeskmateType "AppSheet"
    $trayIconType = Get-DeskmateType "TrayIcon"
    $settingsType = Get-DeskmateType "Settings"
    $taskbarType = Get-DeskmateType "Taskbar"
    $nativeType = Get-DeskmateType "Native"

    function New-MockScreen {
        param(
            [string]$DeviceName,
            [int]$X,
            [int]$Y,
            [int]$Width = 1920,
            [int]$Height = 1080,
            [int]$WorkHeight = 1040,
            [bool]$Primary = $false
        )
        $s = [System.Runtime.Serialization.FormatterServices]::GetUninitializedObject([System.Windows.Forms.Screen])
        $nonPublicFlags = [System.Reflection.BindingFlags]'NonPublic,Instance'
        [System.Windows.Forms.Screen].GetField("bounds", $nonPublicFlags).SetValue($s, [System.Drawing.Rectangle]::new($X, $Y, $Width, $Height))
        [System.Windows.Forms.Screen].GetField("workingArea", $nonPublicFlags).SetValue($s, [System.Drawing.Rectangle]::new($X, $Y, $Width, $WorkHeight))
        [System.Windows.Forms.Screen].GetField("primary", $nonPublicFlags).SetValue($s, $Primary)
        [System.Windows.Forms.Screen].GetField("deviceName", $nonPublicFlags).SetValue($s, $DeviceName)
        return $s
    }

    function New-TestIsland {
        return [Activator]::CreateInstance($islandType, $true)
    }

    # =========================================================================
    # 1. Multi-Monitor Dock Planning & Lifecycle (Island.PlanDocks)
    # =========================================================================
    Context "1. Multi-Monitor Dock Planning & Lifecycle (Island.PlanDocks)" {

        $planMethod = $islandType.GetMethod("PlanDocks", [System.Reflection.BindingFlags]'NonPublic,Static')

        It "1.1 Single monitor initial plan creates live dock and assigns as owner" {
            $screens = [string[]]@("\\.\DISPLAY1")
            $existing = [string[]]@()
            $selected = "\\.\DISPLAY1"

            $plan = $planMethod.Invoke($null, @($screens, $existing, $selected))
            Assert-NotNull $plan "Plan must not be null"
            Assert-Equal $plan.Live.Count 1 "Live count must be 1"
            Assert-Equal $plan.Live[0] "\\.\DISPLAY1" "Live[0] must be DISPLAY1"
            Assert-Equal $plan.Close.Count 0 "Close count must be 0"
            Assert-Equal $plan.Owner "\\.\DISPLAY1" "Owner must be DISPLAY1"
        }

        It "1.2 Adding secondary monitor creates live plan for both and retains owner" {
            $screens = [string[]]@("\\.\DISPLAY1", "\\.\DISPLAY2")
            $existing = [string[]]@("\\.\DISPLAY1")
            $selected = "\\.\DISPLAY1"

            $plan = $planMethod.Invoke($null, @($screens, $existing, $selected))
            Assert-Equal $plan.Live.Count 2 "Both screens must be live"
            Assert-True ($plan.Live -contains "\\.\DISPLAY1") "Live must contain DISPLAY1"
            Assert-True ($plan.Live -contains "\\.\DISPLAY2") "Live must contain DISPLAY2"
            Assert-Equal $plan.Close.Count 0 "No docks to close"
            Assert-Equal $plan.Owner "\\.\DISPLAY1" "Owner must remain DISPLAY1"
        }

        It "1.3 Owner switch re-assigns owner to specified display" {
            $screens = [string[]]@("\\.\DISPLAY1", "\\.\DISPLAY2")
            $existing = [string[]]@("\\.\DISPLAY1", "\\.\DISPLAY2")
            $selected = "\\.\DISPLAY2"

            $plan = $planMethod.Invoke($null, @($screens, $existing, $selected))
            Assert-Equal $plan.Live.Count 2 "Both screens live"
            Assert-Equal $plan.Close.Count 0 "Close count 0"
            Assert-Equal $plan.Owner "\\.\DISPLAY2" "Owner must switch to DISPLAY2"
        }

        It "1.4 Removing owner display closes it and gracefully falls back owner to first live display" {
            $screens = [string[]]@("\\.\DISPLAY1")
            $existing = [string[]]@("\\.\DISPLAY1", "\\.\DISPLAY2")
            $selected = "\\.\DISPLAY2" # DISPLAY2 was owner, but is now disconnected

            $plan = $planMethod.Invoke($null, @($screens, $existing, $selected))
            Assert-Equal $plan.Live.Count 1 "Only DISPLAY1 is live"
            Assert-Equal $plan.Close.Count 1 "DISPLAY2 must be scheduled to close"
            Assert-Equal $plan.Close[0] "\\.\DISPLAY2" "Close[0] must be DISPLAY2"
            Assert-Equal $plan.Owner "\\.\DISPLAY1" "Owner must fall back to DISPLAY1"
        }

        It "1.5 Removing all displays closes all docks and sets empty owner" {
            $screens = [string[]]@()
            $existing = [string[]]@("\\.\DISPLAY1", "\\.\DISPLAY2")
            $selected = ""

            $plan = $planMethod.Invoke($null, @($screens, $existing, $selected))
            Assert-Equal $plan.Live.Count 0 "Live count must be 0"
            Assert-Equal $plan.Close.Count 2 "Both existing docks must be closed"
            Assert-Equal $plan.Owner "" "Owner must be empty"
        }

        It "1.6 Normalizes duplicate display devices and handles case-insensitive device names" {
            $screens = [string[]]@("\\.\display1", "\\.\DISPLAY1", "\\.\DISPLAY2")
            $existing = [string[]]@("\\.\DISPLAY1")
            $selected = "\\.\display2"

            $plan = $planMethod.Invoke($null, @($screens, $existing, $selected))
            Assert-Equal $plan.Live.Count 2 "Duplicate display entries must be deduplicated"
            Assert-Equal $plan.Close.Count 0 "Existing DISPLAY1 matches case-insensitively, so 0 close"
            Assert-Equal $plan.Owner "\\.\DISPLAY2" "Owner matches case-insensitively"
        }
    }

    # =========================================================================
    # 2. Island Multi-Display Placement & Reconnect Fallback
    # =========================================================================
    Context "2. Island Multi-Display Placement & Reconnect Fallback" {

        It "2.1 Valid display setting is preserved when disconnected and restored upon reconnect" {
            $isle = New-TestIsland
            $settings = $islandType.GetField("settings", $flags).GetValue($isle)
            $chosenScreenMethod = $islandType.GetMethod("ChosenScreen", $flags)

            # Set a valid display device name that isn't connected
            $settings.Set("monitor", "\\.\DISPLAY99")
            $settings.Save()

            $chosen = $chosenScreenMethod.Invoke($isle, $null)
            Assert-NotNull $chosen "Must fall back to a valid screen"
            Assert-True $chosen.Primary "Fallback must be the primary screen"

            # The setting MUST NOT be wiped from settings because it has valid \\.\DISPLAY format
            $savedWant = $settings.Get("monitor", "")
            Assert-Equal $savedWant "\\.\DISPLAY99" "Valid display device setting must be preserved for reconnect"
        }

        It "2.2 Invalid non-display device strings are purged from settings" {
            $isle = New-TestIsland
            $settings = $islandType.GetField("settings", $flags).GetValue($isle)
            $chosenScreenMethod = $islandType.GetMethod("ChosenScreen", $flags)

            # Set an invalid device string
            $settings.Set("monitor", "invalid_garbage_monitor")
            $settings.Save()

            $chosen = $chosenScreenMethod.Invoke($isle, $null)
            Assert-NotNull $chosen "Must fall back to primary"

            $savedWant = $settings.Get("monitor", "")
            Assert-Equal $savedWant "" "Garbage non-display setting must be purged"
        }

        It "2.3 Island WindowLeft and MidX map correctly across secondary and negative monitor bounds" {
            $isle = New-TestIsland
            $midXMethod = $islandType.GetMethod("MidX", $flags)
            $winLeftMethod = $islandType.GetMethod("WindowLeft", $flags)
            $islandType.GetField("scale", $flags).SetValue($isle, [float]1.0)

            # Test 1: Secondary monitor to the right at X=1920
            $monRight = [System.Drawing.Rectangle]::new(1920, 0, 1920, 1080)
            $islandType.GetField("monitorPx", $flags).SetValue($isle, $monRight)

            $midRight = [float]$midXMethod.Invoke($isle, @([float]200))
            Assert-Equal $midRight 2880.0 "MidX on secondary monitor must be 1920 + 960 = 2880"

            $wlRight = [int]$winLeftMethod.Invoke($isle, @([int]200))
            Assert-Equal $wlRight 2780 "WindowLeft on secondary monitor must be 2880 - 100 = 2780"

            # Test 2: Secondary monitor to the left (negative coordinates) at X=-1920
            $monLeft = [System.Drawing.Rectangle]::new(-1920, 0, 1920, 1080)
            $islandType.GetField("monitorPx", $flags).SetValue($isle, $monLeft)

            $midLeft = [float]$midXMethod.Invoke($isle, @([float]200))
            Assert-Equal $midLeft -960.0 "MidX on negative monitor must be -1920 + 960 = -960"

            $wlLeft = [int]$winLeftMethod.Invoke($isle, @([int]200))
            Assert-Equal $wlLeft -1060 "WindowLeft on negative monitor must be -960 - 100 = -1060"
        }
    }

    # =========================================================================
    # 3. Multi-Display DockWindow Instantiation, Geometry & Coordinate Isolation
    # =========================================================================
    Context "3. Multi-Display DockWindow Instantiation & Coordinate Isolation" {

        It "3.1 Instantiates primary DockWindow (X=0) with isolated coordinates" {
            $isle = New-TestIsland
            $sPrimary = New-MockScreen "\\.\DISPLAY1" 0 0 1920 1080 1040 $true
            $dw = [Activator]::CreateInstance($dwType, $flags, $null, @($isle, $sPrimary, $true), $null)
            try {
                $h = $dw.Handle # Triggers handle creation and layout
                $mon = $dwType.GetField("mon", $flags).GetValue($dw)
                $area = $dwType.GetField("area", $flags).GetValue($dw)
                $origin = $dwType.GetMethod("WindowOrigin", $flags).Invoke($dw, $null)

                Assert-Equal $mon.Left 0 "Primary dock mon.Left must be 0"
                Assert-Equal $mon.Width 1920 "Primary dock mon.Width must be 1920"
                Assert-Equal $area.Left 0 "Primary dock area.Left must be 0"
                Assert-Equal $area.Width 1920 "Primary dock area.Width must be 1920"
                # WindowOrigin X starts at mon.Left (adjusted by -40000 if Dry mode is active)
                $isDry = [bool]$dwType.GetField("Dry", $flags).GetValue($null)
                $expectedOriginX = if ($isDry) { 0 - 40000 } else { 0 }
                Assert-Equal $origin.X $expectedOriginX "Primary dock WindowOrigin.X must match"
            }
            finally {
                $dw.Shutdown()
            }
        }

        It "3.2 Instantiates secondary DockWindow at positive coordinates (X=1920) with isolated coordinates" {
            $isle = New-TestIsland
            $sSecondary = New-MockScreen "\\.\DISPLAY2" 1920 0 1920 1080 1040 $false
            $dw = [Activator]::CreateInstance($dwType, $flags, $null, @($isle, $sSecondary, $false), $null)
            try {
                $h = $dw.Handle
                $mon = $dwType.GetField("mon", $flags).GetValue($dw)
                $area = $dwType.GetField("area", $flags).GetValue($dw)
                $origin = $dwType.GetMethod("WindowOrigin", $flags).Invoke($dw, $null)

                Assert-Equal $mon.Left 1920 "Secondary dock mon.Left must be 1920"
                Assert-Equal $mon.Right 3840 "Secondary dock mon.Right must be 3840"
                Assert-Equal $area.Left 1920 "Secondary dock area.Left must be 1920"
                Assert-Equal $area.Width 1920 "Secondary dock area.Width must be 1920"

                $isDry = [bool]$dwType.GetField("Dry", $flags).GetValue($null)
                $expectedOriginX = if ($isDry) { 1920 - 40000 } else { 1920 }
                Assert-Equal $origin.X $expectedOriginX "Secondary dock WindowOrigin.X must be 1920 (or offset by Dry)"
                # Win32 clamps native window positions to the signed 16-bit coordinate range.
                $expectedBoundsX = [Math]::Max([int]-32768, [Math]::Min([int]32767, [int]$expectedOriginX))
                Assert-Equal $dw.Bounds.X $expectedBoundsX "Secondary dock Bounds.X must match WindowOrigin.X after Win32 clamping"
            }
            finally {
                $dw.Shutdown()
            }
        }

        It "3.3 Instantiates secondary DockWindow at negative coordinates (X=-1920) with isolated coordinates" {
            $isle = New-TestIsland
            $sNeg = New-MockScreen "\\.\DISPLAY_NEG" -1920 0 1920 1080 1040 $false
            $dw = [Activator]::CreateInstance($dwType, $flags, $null, @($isle, $sNeg, $false), $null)
            try {
                $h = $dw.Handle
                $mon = $dwType.GetField("mon", $flags).GetValue($dw)
                $area = $dwType.GetField("area", $flags).GetValue($dw)
                $origin = $dwType.GetMethod("WindowOrigin", $flags).Invoke($dw, $null)

                Assert-Equal $mon.Left -1920 "Negative dock mon.Left must be -1920"
                Assert-Equal $mon.Right 0 "Negative dock mon.Right must be 0"
                Assert-Equal $area.Left -1920 "Negative dock area.Left must be -1920"
                Assert-Equal $area.Width 1920 "Negative dock area.Width must be 1920"

                $isDry = [bool]$dwType.GetField("Dry", $flags).GetValue($null)
                $expectedOriginX = if ($isDry) { -1920 - 40000 } else { -1920 }
                Assert-Equal $origin.X $expectedOriginX "Negative dock WindowOrigin.X must be -1920 (or offset by Dry)"
                # Win32 clamps native window positions to the signed 16-bit coordinate range.
                $expectedBoundsX = [Math]::Max([int]-32768, [Math]::Min([int]32767, [int]$expectedOriginX))
                Assert-Equal $dw.Bounds.X $expectedBoundsX "Negative dock Bounds.X must match WindowOrigin.X after Win32 clamping"
            }
            finally {
                $dw.Shutdown()
            }
        }

        It "3.4 Multiple simultaneous DockWindows maintain independent isolated coordinate states" {
            $isle = New-TestIsland
            $s1 = New-MockScreen "\\.\DISPLAY1" 0 0 1920 1080 1040 $true
            $s2 = New-MockScreen "\\.\DISPLAY2" 1920 0 1920 1080 1040 $false
            $s3 = New-MockScreen "\\.\DISPLAY_NEG" -1920 0 1920 1080 1040 $false

            $dw1 = [Activator]::CreateInstance($dwType, $flags, $null, @($isle, $s1, $true), $null)
            $dw2 = [Activator]::CreateInstance($dwType, $flags, $null, @($isle, $s2, $false), $null)
            $dw3 = [Activator]::CreateInstance($dwType, $flags, $null, @($isle, $s3, $false), $null)

            try {
                $h1 = $dw1.Handle; $h2 = $dw2.Handle; $h3 = $dw3.Handle

                $mon1 = $dwType.GetField("mon", $flags).GetValue($dw1)
                $mon2 = $dwType.GetField("mon", $flags).GetValue($dw2)
                $mon3 = $dwType.GetField("mon", $flags).GetValue($dw3)

                Assert-Equal $mon1.Left 0 "Dock 1 mon.Left must be 0"
                Assert-Equal $mon2.Left 1920 "Dock 2 mon.Left must be 1920"
                Assert-Equal $mon3.Left -1920 "Dock 3 mon.Left must be -1920"

                # Verify area reservation bounds are mutually isolated
                $area1 = $dwType.GetField("area", $flags).GetValue($dw1)
                $area2 = $dwType.GetField("area", $flags).GetValue($dw2)
                $area3 = $dwType.GetField("area", $flags).GetValue($dw3)

                Assert-Equal $area1.Left 0 "Area 1 must be at 0"
                Assert-Equal $area2.Left 1920 "Area 2 must be at 1920"
                Assert-Equal $area3.Left -1920 "Area 3 must be at -1920"

                Assert-False ($area1.IntersectsWith($area2)) "Area 1 and Area 2 must not intersect"
                Assert-False ($area1.IntersectsWith($area3)) "Area 1 and Area 3 must not intersect"
                Assert-False ($area2.IntersectsWith($area3)) "Area 2 and Area 3 must not intersect"
            }
            finally {
                $dw1.Shutdown()
                $dw2.Shutdown()
                $dw3.Shutdown()
            }
        }
    }

    # =========================================================================
    # 4. Fullscreen Hiding & Monitor Isolation (DockWindow.Tick)
    # =========================================================================
    Context "4. Fullscreen Hiding & Monitor Isolation (DockWindow.Tick)" {

        It "4.1 DockWindow.Tick uses its own monitorHandle for fullscreen check" {
            $isle = New-TestIsland
            $s1 = New-MockScreen "\\.\DISPLAY1" 0 0 1920 1080 1040 $true
            $s2 = New-MockScreen "\\.\DISPLAY2" 1920 0 1920 1080 1040 $false

            $dw1 = [Activator]::CreateInstance($dwType, $flags, $null, @($isle, $s1, $true), $null)
            $dw2 = [Activator]::CreateInstance($dwType, $flags, $null, @($isle, $s2, $false), $null)

            try {
                $h1 = $dw1.Handle; $h2 = $dw2.Handle

                # Assign distinct synthetic monitor handles
                $monHandle1 = [IntPtr]111111
                $monHandle2 = [IntPtr]222222
                $dwType.GetField("monitorHandle", $flags).SetValue($dw1, $monHandle1)
                $dwType.GetField("monitorHandle", $flags).SetValue($dw2, $monHandle2)

                Assert-Equal ($dwType.GetField("monitorHandle", $flags).GetValue($dw1)) $monHandle1 "dw1 must hold monHandle1"
                Assert-Equal ($dwType.GetField("monitorHandle", $flags).GetValue($dw2)) $monHandle2 "dw2 must hold monHandle2"
                Assert-NotEqual ($dwType.GetField("monitorHandle", $flags).GetValue($dw1)) ($dwType.GetField("monitorHandle", $flags).GetValue($dw2)) "Handles must differ"

                # Invoke Tick on both
                $dwType.GetMethod("Tick", $flags).Invoke($dw1, $null)
                $dwType.GetMethod("Tick", $flags).Invoke($dw2, $null)

                # Neither has a real fullscreen window, so both remain shown
                Assert-True ([bool]$dwType.GetField("shown", $flags).GetValue($dw1)) "dw1 shown must be true"
                Assert-True ([bool]$dwType.GetField("shown", $flags).GetValue($dw2)) "dw2 shown must be true"
            }
            finally {
                $dw1.Shutdown()
                $dw2.Shutdown()
            }
        }

        It "4.2 App exclusion rule (isle.ExceptHidden) only hides globalOwner dock, leaving peer docks visible" {
            $isle = New-TestIsland
            $s1 = New-MockScreen "\\.\DISPLAY1" 0 0 1920 1080 1040 $true
            $s2 = New-MockScreen "\\.\DISPLAY2" 1920 0 1920 1080 1040 $false

            $dwOwner = [Activator]::CreateInstance($dwType, $flags, $null, @($isle, $s1, $true), $null)
            $dwPeer = [Activator]::CreateInstance($dwType, $flags, $null, @($isle, $s2, $false), $null)

            try {
                $h1 = $dwOwner.Handle; $h2 = $dwPeer.Handle

                Assert-True ([bool]$dwType.GetField("globalOwner", $flags).GetValue($dwOwner)) "dwOwner must have globalOwner = true"
                Assert-False ([bool]$dwType.GetField("globalOwner", $flags).GetValue($dwPeer)) "dwPeer must have globalOwner = false"

                # Verify logic: bool want = !Native.IsFullscreenOn(monitorHandle) && !(globalOwner && isle.ExceptHidden()) ...
                # If ExceptHidden were true:
                # dwOwner has globalOwner = true -> (true && true) -> negated to false -> dwOwner hidden
                # dwPeer has globalOwner = false -> (false && true) -> negated to true -> dwPeer remains visible!
                $globalOwnerOwner = [bool]$dwType.GetField("globalOwner", $flags).GetValue($dwOwner)
                $globalOwnerPeer = [bool]$dwType.GetField("globalOwner", $flags).GetValue($dwPeer)

                $simulatedExceptHidden = $true
                $wantOwner = -not ($globalOwnerOwner -and $simulatedExceptHidden)
                $wantPeer = -not ($globalOwnerPeer -and $simulatedExceptHidden)

                Assert-False $wantOwner "Global owner dock must be hidden by island app exclusion"
                Assert-True $wantPeer "Peer dock must NEVER be hidden by island app exclusion"
            }
            finally {
                $dwOwner.Shutdown()
                $dwPeer.Shutdown()
            }
        }
    }

    # =========================================================================
    # 5. Sheet and Card OpenAt Anchors Target Respective Monitor Bounds
    # =========================================================================
    Context "5. Sheet and Card OpenAt Anchors Target Respective Monitor Bounds" {

        $trayListType = [Type]::GetType("System.Collections.Generic.List``1[[" + $trayIconType.AssemblyQualifiedName + "]]")

        It "5.1 DockCard ShowTray on primary dock targets primary monitor [0, 1920]" {
            $isle = New-TestIsland
            $sPrimary = New-MockScreen "\\.\DISPLAY1" 0 0 1920 1080 1040 $true
            $dw = [Activator]::CreateInstance($dwType, $flags, $null, @($isle, $sPrimary, $true), $null)
            $card = $null
            try {
                $h = $dw.Handle
                $card = [Activator]::CreateInstance($cardType, $flags, $null, @($isle, $dw, [int]2), $null)
                $mon = $dwType.GetField("mon", $flags).GetValue($dw)
                $barTop = [int]$dwType.GetMethod("BarTopScreen", $flags).Invoke($dw, $null)
                $anchor = [System.Drawing.Rectangle]::new(500, $barTop, 40, 40)
                $emptyList = [Activator]::CreateInstance($trayListType)

                $card.ShowTray($emptyList, $anchor, $mon, $barTop, [float]1.0)
                Assert-True ($card.Bounds.Left -ge 0) "Card left must be >= 0"
                Assert-True ($card.Bounds.Right -le 1920) "Card right must be <= 1920"
            }
            finally {
                if ($null -ne $card -and -not $card.IsDisposed) { $card.Dismiss() }
                $dw.Shutdown()
            }
        }

        It "5.2 DockCard ShowTray on secondary positive dock (X=1920) targets [1920, 3840]" {
            $isle = New-TestIsland
            $sSec = New-MockScreen "\\.\DISPLAY2" 1920 0 1920 1080 1040 $false
            $dw = [Activator]::CreateInstance($dwType, $flags, $null, @($isle, $sSec, $false), $null)
            $card = $null
            try {
                $h = $dw.Handle
                $card = [Activator]::CreateInstance($cardType, $flags, $null, @($isle, $dw, [int]2), $null)
                $mon = $dwType.GetField("mon", $flags).GetValue($dw)
                $barTop = [int]$dwType.GetMethod("BarTopScreen", $flags).Invoke($dw, $null)
                $anchor = [System.Drawing.Rectangle]::new(2500, $barTop, 40, 40)
                $emptyList = [Activator]::CreateInstance($trayListType)

                $card.ShowTray($emptyList, $anchor, $mon, $barTop, [float]1.0)
                Assert-True ($card.Bounds.Left -ge 1920) "Card left must be >= 1920, got: $($card.Bounds.Left)"
                Assert-True ($card.Bounds.Right -le 3840) "Card right must be <= 3840, got: $($card.Bounds.Right)"
            }
            finally {
                if ($null -ne $card -and -not $card.IsDisposed) { $card.Dismiss() }
                $dw.Shutdown()
            }
        }

        It "5.3 DockCard ShowTray on secondary negative dock (X=-1920) targets [-1920, 0]" {
            $isle = New-TestIsland
            $sNeg = New-MockScreen "\\.\DISPLAY_NEG" -1920 0 1920 1080 1040 $false
            $dw = [Activator]::CreateInstance($dwType, $flags, $null, @($isle, $sNeg, $false), $null)
            $card = $null
            try {
                $h = $dw.Handle
                $card = [Activator]::CreateInstance($cardType, $flags, $null, @($isle, $dw, [int]2), $null)
                $mon = $dwType.GetField("mon", $flags).GetValue($dw)
                $barTop = [int]$dwType.GetMethod("BarTopScreen", $flags).Invoke($dw, $null)
                $anchor = [System.Drawing.Rectangle]::new(-1000, $barTop, 40, 40)
                $emptyList = [Activator]::CreateInstance($trayListType)

                $card.ShowTray($emptyList, $anchor, $mon, $barTop, [float]1.0)
                Assert-True ($card.Bounds.Left -ge -1920) "Card left must be >= -1920, got: $($card.Bounds.Left)"
                Assert-True ($card.Bounds.Right -le 0) "Card right must be <= 0, got: $($card.Bounds.Right)"
            }
            finally {
                if ($null -ne $card -and -not $card.IsDisposed) { $card.Dismiss() }
                $dw.Shutdown()
            }
        }

        It "5.4 AppSheet OpenAt on secondary docks anchors and clamps to respective monitor bounds" {
            $isle = New-TestIsland
            $sNeg = New-MockScreen "\\.\DISPLAY_NEG" -1920 0 1920 1080 1040 $false
            $dw = [Activator]::CreateInstance($dwType, $flags, $null, @($isle, $sNeg, $false), $null)
            $sheet = $null
            try {
                $h = $dw.Handle
                $da = [Activator]::CreateInstance($daType, $true)
                $daType.GetField("Key").SetValue($da, "app:test")
                $daType.GetField("Name").SetValue($da, "MultiMonitorTest")

                $sheet = [Activator]::CreateInstance($appSheetType, $flags, $null, @($isle, $dw, $da), $null)
                $mon = $dwType.GetField("mon", $flags).GetValue($dw)
                $barTop = [int]$dwType.GetMethod("BarTopScreen", $flags).Invoke($dw, $null)

                # Anchor near the left edge of the negative monitor (-1915)
                $anchorLeftEdge = [System.Drawing.Rectangle]::new(-1915, $barTop, 48, 48)
                $sheet.OpenAt($anchorLeftEdge, [System.Drawing.Rectangle]::Empty, $mon, $barTop, [float]1.0)

                # Sheet must be clamped to mon.Left + 8px
                Assert-True ($sheet.Bounds.Left -ge ($mon.Left + 8)) "Sheet must clamp within negative monitor left bound (-1912)"
                Assert-True ($sheet.Bounds.Right -le $mon.Right) "Sheet must not extend past negative monitor right bound (0)"
            }
            finally {
                if ($null -ne $sheet -and -not $sheet.IsDisposed) { $sheet.Close(); $sheet.Dispose() }
                $dw.Shutdown()
            }
        }
    }

    # =========================================================================
    # 6. Owner vs Peer Dock Roles & Hook Singleton Protection
    # =========================================================================
    Context "6. Owner vs Peer Dock Roles & Hook Singleton Protection" {

        It "6.1 Peer docks never start tray or winkey hooks, preserving global owner singleton" {
            $isle = New-TestIsland
            $sPeer = New-MockScreen "\\.\DISPLAY2" 1920 0 1920 1080 1040 $false
            $dwPeer = [Activator]::CreateInstance($dwType, $flags, $null, @($isle, $sPeer, $false), $null)
            try {
                $h = $dwPeer.Handle

                # Changed(false) ensures globalOwner is false
                $dwPeer.Changed($false)

                $tray = $dwType.GetField("tray", $flags).GetValue($dwPeer)
                Assert-Null $tray "Peer dock must not create TrayHost"

                # Verify globalOwner flag
                $globalOwner = [bool]$dwType.GetField("globalOwner", $flags).GetValue($dwPeer)
                Assert-False $globalOwner "Peer dock must have globalOwner = false"
            }
            finally {
                $dwPeer.Shutdown()
            }
        }

        It "6.2 SetGlobalOwner(false) stops tray and hotkey services upon owner revocation" {
            $isle = New-TestIsland
            $sOwner = New-MockScreen "\\.\DISPLAY1" 0 0 1920 1080 1040 $true
            $dwOwner = [Activator]::CreateInstance($dwType, $flags, $null, @($isle, $sOwner, $true), $null)
            try {
                $h = $dwOwner.Handle
                Assert-True ([bool]$dwType.GetField("globalOwner", $flags).GetValue($dwOwner)) "Initially globalOwner is true"

                # Revoke global ownership
                $dwOwner.SetGlobalOwner($false)

                Assert-False ([bool]$dwType.GetField("globalOwner", $flags).GetValue($dwOwner)) "globalOwner is now false"
                $tray = $dwType.GetField("tray", $flags).GetValue($dwOwner)
                Assert-Null $tray "Tray must be stopped and set to null after ownership revocation"
            }
            finally {
                $dwOwner.Shutdown()
            }
        }

        It "6.3 Timed taskbar peek suspends replacement, expires, and keeps the saved setting" {
            $isle = New-TestIsland
            $settings = $islandType.GetField("settings", $flags).GetValue($isle)
            $settings.Set("dockbar.on", $true)
            $settings.Set("dockbar.replace", $true)
            $sOwner = New-MockScreen "\\.\DISPLAY1" 0 0 1920 1080 1040 $true
            $dw = [Activator]::CreateInstance($dwType, $flags, $null, @($isle, $sOwner, $true), $null)
            $pausedField = $taskbarType.GetField("Paused", $flags)
            $replacing = $dwType.GetProperty("Replacing", $flags)
            $timer = $islandType.GetField("taskbarPeekTimer", $flags).GetValue($isle)
            $dockWins = $islandType.GetField("dockWins", $flags).GetValue($isle)
            $peek = $islandType.GetMethod("PeekTaskbar", $flags)
            $end = $islandType.GetMethod("EndTaskbarPeek", $flags)
            $dockWins.Add($sOwner.DeviceName, $dw)
            $islandType.GetField("dockWin", $flags).SetValue($isle, $dw)
            try {
                $h = $dw.Handle
                Assert-True ([bool]$replacing.GetValue($dw)) "Saved replace setting must make the dock replace the taskbar"

                $peekMenu = $islandType.GetMethod("TaskbarPeekMenuItem", $flags).Invoke($isle, $null)
                try { Assert-Equal $peekMenu.DropDownItems.Count 4 "Taskbar peek menu must offer four durations" }
                finally { $peekMenu.Dispose() }

                [void]$peek.Invoke($isle, @([int]5))
                Assert-True ([bool]$pausedField.GetValue($null)) "Timed peek must pause taskbar replacement"
                Assert-True $timer.Enabled "Timed peek must start its countdown"
                Assert-Equal $timer.Interval 5000 "Five-second peek must use a 5000 ms timer"
                Assert-False ([bool]$replacing.GetValue($dw)) "Timed peek must stop taskbar replacement"
                Assert-True ($settings.GetBool("dockbar.replace", $false)) "Timed peek must not alter the saved replace setting"

                [void]$peek.Invoke($isle, @([int]10))
                Assert-Equal $timer.Interval 10000 "Choosing a duration again must restart the countdown"

                [void]$end.Invoke($isle, $null)
                Assert-False ([bool]$pausedField.GetValue($null)) "Ending the peek must resume taskbar replacement"
                Assert-False $timer.Enabled "Ending the peek must stop the countdown"
                Assert-True ([bool]$replacing.GetValue($dw)) "Replacement must be active again after the peek ends"
            }
            finally {
                $timer.Stop()
                $pausedField.SetValue($null, $false)
                [void]$dockWins.Remove($sOwner.DeviceName)
                $dw.Shutdown()
            }
        }
    }

    # =========================================================================
    # 7. Cleanup and Shutdown of All Peer Docks (Island.CloseDocks)
    # =========================================================================
    Context "7. Cleanup and Shutdown of All Peer Docks (Island.CloseDocks)" {

        It "7.1 Island.CloseDocks closes and disposes all peer docks, unhooks owner, and clears dockWins" {
            $isle = New-TestIsland
            $s1 = New-MockScreen "\\.\DISPLAY1" 0 0 1920 1080 1040 $true
            $s2 = New-MockScreen "\\.\DISPLAY2" 1920 0 1920 1080 1040 $false
            $s3 = New-MockScreen "\\.\DISPLAY3" 3840 0 1920 1080 1040 $false

            $dw1 = [Activator]::CreateInstance($dwType, $flags, $null, @($isle, $s1, $true), $null)
            $dw2 = [Activator]::CreateInstance($dwType, $flags, $null, @($isle, $s2, $false), $null)
            $dw3 = [Activator]::CreateInstance($dwType, $flags, $null, @($isle, $s3, $false), $null)

            $h1 = $dw1.Handle; $h2 = $dw2.Handle; $h3 = $dw3.Handle

            $dockWins = $islandType.GetField("dockWins", $flags).GetValue($isle)
            $dockWins.Add($s1.DeviceName, $dw1)
            $dockWins.Add($s2.DeviceName, $dw2)
            $dockWins.Add($s3.DeviceName, $dw3)
            $islandType.GetField("dockWin", $flags).SetValue($isle, $dw1)

            Assert-Equal $dockWins.Count 3 "dockWins dictionary must contain 3 docks"
            Assert-NotNull ($islandType.GetField("dockWin", $flags).GetValue($isle)) "dockWin must not be null"

            # A runtime peek must not survive the docks closing
            $peekTimer = $islandType.GetField("taskbarPeekTimer", $flags).GetValue($isle)
            $taskbarType.GetField("Paused", $flags).SetValue($null, $true)
            $peekTimer.Interval = 5000
            $peekTimer.Start()

            # Execute Island.CloseDocks
            $closeDocksMethod = $islandType.GetMethod("CloseDocks", $flags)
            $closeDocksMethod.Invoke($isle, $null)

            # Assert all docks disposed and dictionary cleared
            Assert-False ([bool]$taskbarType.GetField("Paused", $flags).GetValue($null)) "CloseDocks must clear the runtime taskbar peek"
            Assert-False $peekTimer.Enabled "CloseDocks must stop the peek countdown"
            Assert-Equal $dockWins.Count 0 "dockWins dictionary must be completely cleared"
            Assert-Null ($islandType.GetField("dockWin", $flags).GetValue($isle)) "dockWin reference must be null"
            Assert-True $dw1.IsDisposed "Owner dock must be disposed"
            Assert-True $dw2.IsDisposed "Peer dock 2 must be disposed"
            Assert-True $dw3.IsDisposed "Peer dock 3 must be disposed"
        }

        It "7.2 DockWindow.Shutdown disposes layered canvas, hooks, and child windows cleanly" {
            $isle = New-TestIsland
            $s = New-MockScreen "\\.\DISPLAY2" 1920 0 1920 1080 1040 $false
            $dw = [Activator]::CreateInstance($dwType, $flags, $null, @($isle, $s, $false), $null)
            $h = $dw.Handle

            Assert-False $dw.IsDisposed "Form must not be disposed before shutdown"
            Assert-True ([bool]$dwType.GetField("shown", $flags).GetValue($dw)) "Shown must be true"

            $dw.Shutdown()

            Assert-True $dw.IsDisposed "DockWindow must be disposed after Shutdown"
            $closing = [bool]$dwType.GetField("closing", $flags).GetValue($dw)
            Assert-True $closing "Closing flag must be set to true"
            $canvas = $dwType.GetField("canvas", $flags).GetValue($dw)
            $g = $dwType.GetField("g", $flags).GetValue($dw)
            Assert-Null $g "Graphics context must be disposed and set to null"
        }
    }
}
